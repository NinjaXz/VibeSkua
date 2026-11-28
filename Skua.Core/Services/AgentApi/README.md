# Agent API

A local HTTP endpoint that lets an outside program drive one character: move,
fight, trade, hand in quests, and load scripts. It is built for agents (LLM
drivers), but any HTTP client can use it.

The endpoint is **off by default**. Nothing changes for an existing user until the
switch is turned on in Settings.

## Turn it on

Settings window, three new rows:

| Setting | Default | Note |
|---|---|---|
| Agent API Enabled | off | starts the listener at launch, or immediately from the row |
| Agent API Port | 8766 | loopback only unless the bind address changes |
| Agent API Token | empty | required when the bind address is not loopback |

The bind address is a settings key only: `AgentApiBind` in
`%APPDATA%\VibeSkua\Skua.settings.json`, under `client`. `AgentApiEnabled`,
`AgentApiPort` and `AgentApiToken` sit next to it.

For one run without touching settings, set an environment variable instead:
`VIBESKUA_AGENT_ENABLED=1`, `_BIND`, `_PORT`, `_TOKEN`.

Every request needs the token as a bearer value, except when the bind address is
loopback and no token was saved:

```
curl -H "Authorization: Bearer <token>" http://127.0.0.1:8766/state
```

No CORS header is sent, so a web page cannot read answers. Set a token anyway: a
cross-origin request can still arrive and act. The endpoint has no TLS: keep it on
loopback, or put it behind your own tunnel.

## What a request looks like

`GET` answers a snapshot. `POST` performs one action and answers the new state, so
a driver needs no extra call to see the result.

```json
{ "ok": true,  "data": { ... }, "timestamp": "2026-11-26T13:31:02Z" }
{ "ok": false, "error": "player_not_playing", "timestamp": "..." }
```

| Group | Endpoints |
|---|---|
| Health, state | `GET /health`, `GET /state`, `GET /logs` |
| World | `GET /map`, `GET /map/npcs`, `GET /players`, `GET /monsters`, `GET /servers` |
| Things | `GET /inventory`, `GET /bank`, `GET /shop`, `GET /quests`, `GET /quest?id=`, `GET /quests/cached`, `GET /quests/completed` |
| Scripts | `GET /scripts` (recursive, `filter`, `limit`, `from`), `GET /script`, `GET /script/options?name=` |
| Movement | `POST /join`, `/jump`, `/walk`, `/walk-speed`, `/goto` |
| Combat | `POST /attack`, `/kill`, `/cancel-attack`, `/exit-combat`, `/rest`, `/reload-map` |
| Session | `POST /login`, `/logout`, `/relogin`, `/select-server` |
| Processes | `GET /client`, `POST /client/start` |
| Bank | `POST /bank/open`, `/bank/deposit`, `/bank/withdraw` |
| Party | `GET /party`, `POST /party/on`, `/party/invite`, `/party/accept`, `/party/summon`, `/party/kick`, `/party/leave` |
| Room | `GET /room`, `POST /room` (`{"number":250123}` or `{"public":true}`) |
| Quests | `POST /quests/load`, `/quests/check`, `/quests/accept`, `/quests/complete`, `/quests/abandon` |
| Shop | `POST /shop/load`, `/shop/buy`, `/shop/close` |
| Scripts | `POST /scripts/write`, `/scripts/run` (silent by default), `/script/options`, `/scripts/pause`, `/scripts/resume`, `/scripts/stop` |

`/pause`, `/resume` and `/stop` are short aliases of the script routes.

A map name is not a room. Two accounts that `POST /join` the same map name usually
land in different rooms, and `GET /state` on each answers the same map name with a
different `map.roomId`. A carry needs the party channel: `POST /party/on` and
`/party/invite {name}` on the account that leads, `POST /room {"number":N}` on every account of a party pins them to one room: with the option on
the client joins `map-N` and the route also teleports you into room N, so the
`map.roomId` values match whatever the accounts' quests say. One account makes the
room, the others ask for the same number.
Library story and class scripts call `SetOptions()`, which turns the option off, so re-arm it
after each script start. `POST /party/accept` on the other, then
`POST /party/summon {name}` on the leader pulls the member into the leader's room. The
member accepts a later summon on its own only if a script listens for the packet; the
library's `Army/CoreArmyLite.cs` `PartyManagement` does that. The summon worked once the two
`map.roomId` values match.

Script writes go to the client's own script folder
(`%APPDATA%\VibeSkua\Scripts`). A name must be a bare file name, `.cs` is added
when it is missing, and an existing file needs `"overwrite": true`.

## Status codes

| Code | Meaning |
|---|---|
| 200 | done, or a snapshot |
| 400 | the request is missing a value or has the wrong type |
| 401 | no token or the wrong token |
| 404 | unknown endpoint, or an item that is not where the request said (`item_not_in_inventory`, `item_not_in_bank`) |
| 405 | wrong method for a known endpoint |
| 409 | the game is in the wrong state: `player_not_playing`, `map_not_loaded`, `client_not_started`, `bank_not_loaded`, `inventory_full`, `shop_not_loaded`, a bank move that changed nothing (`deposit_did_not_take_effect`, `withdraw_did_not_take_effect`), or another action holds the gate (`another_action_in_progress`, `another_script_action_in_progress`) |
| 500 | the action failed inside the game, or a script did not compile |
| 501 | no map reader is wired in this build |
| 502 | the map file could not be fetched or read |
| 503 | no decompiler or no java on this machine |
| 504 | the action ran past its window (`action_timed_out`) |

One action runs at a time. A second request that wants the same gate gets 409
after a 250 ms wait, and an action that overruns its window gets 504. Long actions
(`kill`, `login`, quest hand-ins) have their own longer windows.

## Map reader

`GET /map/npcs` answers the NPCs of the current map, with their cells, the quests
each one holds, the shops, and the exits to other maps. It downloads the map's
`Client.swf`, decompiles it, parses the result, and caches it per map id.

It uses the FFDec folder that sits next to the client (`FFDec/ffdec.jar`) and its
bundled java when present; with no bundled java it takes `JAVA_HOME`, then `PATH`.
Cache and downloads sit in `%APPDATA%\VibeSkua\cache`, and `?refresh=1` forces a fresh
read. Today the parser names the NPCs and their cells; the monster, portal and quest
columns come back empty until the parser reads the frame scripts.

## Script library and script choices

The script folder holds near 2000 scripts inside folders, and each script can ask
for choices in a window the client draws. An agent has no window, so three rules make
a library script usable:

- A name may be a relative path: `Army/Farm/ArmySevenCirclesWar.cs`. `GET /scripts`
  walks the folders and returns `path`, `title`, `description` and `tags`, so
  `?filter=army` is how an agent finds work. `limit` and `from` page the answer.
- `/scripts/run` starts with the options window closed, the same way the mobile remote
  control does. Pass `"silent": false` only when a person sits at the client.
- `GET /script/options?name=` reports the questions a script asks, its defaults and the
  stored choice. `POST /script/options` stores one. The store is the file the client
  already uses: `options/<OptionsStorage>.cfg`, one `Name=Value` line per choice, with
  `Category:Name=Value` for a grouped choice.

Two limits worth knowing. A script that borrows a shared option, such as
`CoreBots.Instance.SkipOptions`, is not listed: only `new Option<T>(...)` rows are.
And a script with no `OptionsStorage` shares `options/default.cfg` with every other one,
which is how the client behaves too.

## Tests

`Skua.Core.Tests` holds 239 tests for this feature and for the two script members it
adds (`Quests.Abandon`, `Shops.Close`). They run in about a second and need no game,
no network and no token:

```
dotnet test Skua.Core.Tests -c Release
```

The router sits behind a framework-free seam: a handler receives
`(method, path, query, body)` and returns `(status, payload)`. `AgentApiServer` only
moves bytes, so the whole contract is testable without a socket. The few HTTP tests
bind a real loopback port to prove auth and framing.

## Which process answers

The tabbed client keeps every character in its own child process, so the process that
holds this endpoint is often the **tab host**, which holds no character.

- `GET /health` and `GET /client` say the role: `host` or `child`. Check it before you
  drive a character.
- A session route in a host answers `409 client_not_started` instead of failing inside
  the game.
- `POST /client/start {"username":"hero","server":"Alteon"}` starts a client for a
  **saved** account. The client uses the password it already stores, so no secret
  crosses the wire.
- A child binds a free loopback port of its own and writes that port to
  `%TEMP%\VibeSkuaTabs\<pid>.agent.txt`. `GET /client` lists the living children, and the
  character is driven on that child's port. The mobile remote control uses
  `<pid>.remote.txt` in the same folder.

Both processes call the startup code, so one binds the port and the other logs one
line and runs on without the endpoint. The mobile remote-control server on port 8088
behaves the same way today. A wrong setting (bad port, taken port, public bind without
a token) costs only the endpoint: the client starts either way.

## Bank

`GET /bank` answers `loaded`, the slot counts, and the stored items. The game fills
that list only after the bank screen has been opened, and it never sets its own loaded
flag, so an answer with items counts as loaded and an empty list with `loaded: false`
means "unknown", not "empty". `POST /bank/open` opens the window and asks for the list
without blocking on it; read `GET /bank` again after that.

A move names one item, by name or by id: `POST /bank/deposit {"name":"Treasure
Potion"}` or `{"id":18927}`, and `POST /bank/withdraw` the other way. A deposit only
needs the item to be in the bag. A withdrawal waits for the bank list and answers
`bank_not_loaded` until it exists. An item that is not where the request said it is
gets 404 (`item_not_in_inventory`, `item_not_in_bank`).

The game moves bank items on its own clock, and it reports false while a hand-back is
still happening, so a move is judged by the state it caused: the bag is read again. A
move that changed nothing answers 409 (`deposit_did_not_take_effect`,
`withdraw_did_not_take_effect`) even when the game's own call said it worked.
