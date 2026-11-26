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
| Things | `GET /inventory`, `GET /bank`, `GET /shop`, `GET /quests`, `GET /quest?id=`, `GET /quests/cached` |
| Scripts | `GET /scripts`, `GET /script` |
| Movement | `POST /join`, `/jump`, `/walk`, `/walk-speed`, `/goto` |
| Combat | `POST /attack`, `/kill`, `/cancel-attack`, `/exit-combat`, `/rest`, `/reload-map` |
| Session | `POST /login`, `/logout`, `/relogin`, `/select-server` |
| Processes | `GET /client`, `POST /client/start` |
| Bank | `POST /bank/open`, `/bank/deposit`, `/bank/withdraw` |
| Quests | `POST /quests/load`, `/quests/accept`, `/quests/complete`, `/quests/abandon` |
| Shop | `POST /shop/load`, `/shop/buy`, `/shop/close` |
| Scripts | `POST /scripts/write`, `/scripts/run`, `/scripts/pause`, `/scripts/resume`, `/scripts/stop` |

`/pause`, `/resume` and `/stop` are short aliases of the script routes.

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
| 409 | the game is in the wrong state: `player_not_playing`, `map_not_loaded`, `client_not_started`, `bank_not_loaded`, `inventory_full`, `shop_not_loaded`, a move the game refused (`deposit_failed`, `withdraw_failed`), or another action holds the gate (`another_action_in_progress`, `another_script_action_in_progress`) |
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

## Tests

`Skua.Core.Tests` holds 234 tests for this feature and for the two script members it
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
that list only after the bank screen has been opened, so an empty list with
`loaded: false` means "unknown", not "empty". `POST /bank/open` opens it and asks for
the list; read `GET /bank` again after that. Moves need a loaded list, and a name or an
item id: `POST /bank/deposit {"name":"Treasure Potion"}` or `{"id":18927}`, and
`POST /bank/withdraw` the other way.
