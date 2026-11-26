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
| Things | `GET /inventory`, `GET /shop`, `GET /quests`, `GET /quest?id=`, `GET /quests/cached` |
| Scripts | `GET /scripts`, `GET /script` |
| Movement | `POST /join`, `/jump`, `/walk`, `/walk-speed`, `/goto` |
| Combat | `POST /attack`, `/kill`, `/cancel-attack`, `/exit-combat`, `/rest`, `/reload-map` |
| Session | `POST /login`, `/logout`, `/relogin`, `/select-server` |
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
| 404 | unknown endpoint |
| 405 | wrong method for a known endpoint |
| 409 | the game is in the wrong state: `player_not_playing`, `map_not_loaded`, `inventory_full`, `shop_not_loaded`, or another action holds the gate (`another_action_in_progress`, `another_script_action_in_progress`) |
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

It uses the FFDec that ships with the client and its bundled java. Nothing new is
required. Cache and downloads sit in `%APPDATA%\VibeSkua\cache`, and `?refresh=1`
forces a fresh read.

## Tests

`Skua.Core.Tests` holds 177 tests for this feature and for the two script members it
adds (`Quests.Abandon`, `Shops.Close`). They run in about a second and need no game,
no network and no token:

```
dotnet test Skua.Core.Tests -c Release
```

The router sits behind a framework-free seam: a handler receives
`(method, path, query, body)` and returns `(status, payload)`. `AgentApiServer` only
moves bytes, so the whole contract is testable without a socket. The few HTTP tests
bind a real loopback port to prove auth and framing.

## Known behaviour

The client starts two processes: the first relaunches the app with `--embed`. Both
call the startup code, so one binds the port and the other logs one line and runs on
without the endpoint. The mobile remote-control server on port 8088 behaves the same
way today. A wrong setting (bad port, taken port, public bind without a token) costs
only the endpoint: the client starts either way.
