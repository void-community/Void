---
title: API Reference
description: Control the Docker Minecraft client over HTTP.
---

The API is served under `/api` on container port `80`. Request and response bodies are JSON with camel case names and enum values, unless noted otherwise.
The examples publish the port as `8080`.

## Endpoints

| Method | Path | Success | Description |
| :----- | :--- | :------ | :---------- |
| `GET` | `/api/health` | `200` | Check that the API is ready. |
| `GET` | `/api/game/status` | `200` | Read the game state and the latest operation. |
| `POST` | `/api/game/start/vanilla` | `202` | Start a vanilla Minecraft release. |
| `POST` | `/api/game/start/neoforge` | `202` | Start NeoForge. |
| `POST` | `/api/game/start/curseforge` | `202` | Start a CurseForge modpack. |
| `POST` | `/api/game/connect` | `200` | Connect the game to a server. |
| `POST` | `/api/game/send-chat` | `204` | Send a chat message or a command. |
| `GET` | `/api/game/players` | `200` | Read the local player and the players around it. |
| `GET` | `/api/game/screenshot` | `200` | Capture the game window as `image/png`. |
| `PUT` | `/api/game/options` | `204` | Replace Minecraft's `options.txt`. |
| `POST` | `/api/game/stop` | `200` | Stop the game. |
| `GET` | `/api/game/diagnostics` | `200` | List diagnostic sessions. |
| `GET` | `/api/game/diagnostics/{sessionId}` | `200` | Download a session's diagnostics as ZIP. |

## Health

```bash
curl --fail http://localhost:8080/api/health
```

Returns `200 OK` with the text `ok` once the API accepts requests, and `503 Service Unavailable` before that.

## Status

`GET /api/game/status` describes the game and the most recent operation:

```json
{
  "state": "ready",
  "operationId": 1,
  "sessionId": "16721635-5993-47fc-827e-727262544c24",
  "operation": "start-vanilla",
  "operationState": "succeeded",
  "processId": 432,
  "exitCode": null,
  "server": null,
  "message": "Game window is ready",
  "error": null,
  "failure": null,
  "warnings": [],
  "updatedAt": "2026-08-12T12:00:00+00:00"
}
```

| Field | Description |
| :---- | :---------- |
| `state` | The game's state, described below. |
| `operationId` | Identifier of the latest operation. It increases with every accepted operation. |
| `sessionId` | [Diagnostic session](#diagnostics) of the latest launch, or `null` before the first one. |
| `operation` | The latest operation: `start-vanilla`, `start-neoforge`, `start-curseforge`, `connect`, `send-chat`, `screenshot`, `options`, or `stop`. |
| `operationState` | `none`, `running`, `succeeded`, `failed`, or `canceled`. |
| `processId` | Process identifier while the game runs. |
| `exitCode` | Exit code once the game has exited. It can be `0` even after a crash, so use `state` and `failure` instead. |
| `server` | `host` and `port` of the connected server, otherwise `null`. |
| `message` | Short human-readable description. |
| `error` | Error message when something failed, otherwise `null`. |
| `failure` | [Failure details](#failures) when something failed, otherwise `null`. |
| `warnings` | Non-fatal notes, such as a dialog the client dismissed. |
| `updatedAt` | Time of the latest change. |

| State | Meaning |
| :---- | :------ |
| `idle` | No game is running. |
| `starting` | The game is being prepared and launched. |
| `ready` | The game is running and not connected to a server. |
| `connected` | The game is connected to a server. |
| `stopping` | The game is being stopped. |
| `failed` | The launch failed, or the game ended without a stop request. |

### Waiting for an operation

Start requests return the status with `202 Accepted` and keep running in the background.
Save the returned `operationId` and poll `/api/game/status` until that operation reaches `ready` and `succeeded`.
Treat it as failed if it reports `failed` or `canceled`, or if `operationId` changes to another operation.

### When the game ends on its own

The game should only end through `POST /api/game/stop`. If Minecraft quits, crashes, or is killed instead, `state` becomes `failed` with one of these failure codes:

- `client.process.crashed` when Minecraft saved a crash report, or its Java runtime an error log. The message says what they say, for example `Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash` or `Minecraft's Java runtime crashed: SIGSEGV (0xb) at pc=…, pid=…, tid=… in C [libc.so.6+0x8f228]`.
- `client.process.out_of_memory` when it ran out of memory and was killed.
- `client.process.exited` otherwise.

The client also closes a game that can't go on by itself, with the same result: NeoForge showing that mods failed to load, or Minecraft still running 10 seconds after it reported a crash.
If no operation was running at the time, `operation` and `operationState` still describe the last one.
A failed game can be started again.

## Starting a Game

One game runs at a time. A start request is accepted when the state is `idle` or `failed` and returns `409 Conflict` otherwise.

All start requests accept these optional fields:

| Field | Description |
| :---- | :---------- |
| `arguments` | [PortableMC start arguments](https://github.com/mindstorm38/portablemc), such as `["--username", "TestPlayer"]`. Without a username, the game uses a generated offline name. |
| `memoryMb` | Maximum Java heap of the game in MiB. It does not limit the container's memory. Don't combine it with an `-Xmx` argument. |

Mojang's launcher gives Minecraft 2 GiB, or 4 GiB from 26.1 onwards. See [**Minecraft Java Edition memory allocation**](https://help.minecraft.net/hc/en-us/articles/39083573916941).

### Vanilla

`version` is required and is a Mojang release identifier:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.8","memoryMb":2048,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/vanilla
```

### NeoForge

`version` is an optional Minecraft release identifier. Without it, the client starts the newest Minecraft release that has a stable NeoForge build. Snapshots and pre-releases are never chosen.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"memoryMb":4096,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

To limit that choice, for example to what your server accepts, set `maximumMinecraftVersion`. It can't be combined with `version`.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"maximumMinecraftVersion":"1.21.8","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

To start a specific Minecraft version:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.1","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

The client also installs Sodium when a build exists for that Minecraft version.

### CurseForge

`slug` is the modpack's project slug and `fileId` is the modpack file to start. The container needs a [CurseForge API key](/docs/client/images/#curseforge-configuration).

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"slug":"all-the-mods-10","fileId":1234567,"memoryMb":4096,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/curseforge
```

:::note
Replace the example `fileId` with the file you want to start.
:::

If any modpack file can't be downloaded, the launch fails with an error that lists every missing file.

## Connecting

The game must be `ready`. `port` must be between `1` and `65535`.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"host":"minecraft-server","port":25565}' \
  http://localhost:8080/api/game/connect
```

The response arrives once the game has joined:

```json
{
  "server": {
    "host": "minecraft-server",
    "port": 25565
  },
  "connectedAt": "2026-08-12T12:01:00+00:00"
}
```

Connecting is safe to repeat. Requests for the same server share one connection attempt, and a game already connected to that server returns the original response. A request for a different server returns `409 Conflict` while the game is connecting or connected.
Canceling the HTTP request doesn't cancel the connection attempt. Use `POST /api/game/stop` to abort it, which answers waiting requests with `409 Conflict`.

Mods that replace the main menu are supported. When the client has to work around a menu, it says so in `warnings`.

A failed connection reports one of these `failure.stage` values:

| Stage | Meaning |
| :---- | :------ |
| `connection.failed` | The game couldn't reach the server, for example because the host is unknown or the connection was refused. The message is the game's error. |
| `connection.rejected` | The server disconnected the game while it was joining. The message is the server's reason. |
| `world.timeout` | The game reached the server but not a world within 5 minutes. The message is what the game shows. |
| `player.unexpected` | The server let the game in under a different player name than it was started with. |
| `ui.route` | The client couldn't reach the Direct Connection screen within 60 seconds. |
| `window.blocked` | A window from another program covered the game for 10 seconds. The message names it. |

If the game later leaves the server, for example after a kick or a timeout, `state` returns to `ready`, `server` becomes `null`, and `message` gives the reason.
Switching servers behind a proxy keeps the game `connected`.

## Sending Chat

The game must be `connected`. Messages that start with `/` are sent as commands.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"message":"Hello from the client"}' \
  http://localhost:8080/api/game/send-chat
```

Returns `409 Conflict` when the game isn't in a world, for example while a proxy switches servers.

## Players

`GET /api/game/players` returns the local player and the other players the game can see. The game must be `ready` or `connected`. No server plugin or mod is needed.

```json
{
  "local": {
    "uuid": "f84c6a79-7f8b-4b7c-a02a-7bc7e3f99574",
    "name": "LocalPlayer",
    "position": { "x": 0.5, "y": 64.0, "z": 0.5 },
    "body": { "yaw": 15.0 },
    "head": { "yaw": 20.0, "pitch": -5.0 }
  },
  "remote": [
    {
      "uuid": "62ca1f73-b4d1-47bb-b176-052905a08b35",
      "name": "Neighbour",
      "position": { "x": 2.5, "y": 64.0, "z": 0.5 },
      "body": { "yaw": -10.0 },
      "head": { "yaw": -5.0, "pitch": 2.5 }
    }
  ]
}
```

Angles are in degrees. `uuid` and `name` are `null` when the game doesn't know them.
Returns `409 Conflict` when the game isn't in a world, and `503 Service Unavailable` when the players can't be read.

## Screenshot

The game must be `ready` or `connected`.

```bash
curl --fail --output minecraft.png http://localhost:8080/api/game/screenshot
```

## Game Options

Send the complete contents of Minecraft's `options.txt` as `text/plain`. They apply from the next launch.

```bash
curl --fail-with-body \
  --request PUT \
  --header 'Content-Type: text/plain' \
  --data-binary $'fullscreen:false\nrenderDistance:2\nenableVsync:false' \
  http://localhost:8080/api/game/options
```

## Stopping

```bash
curl --fail-with-body --request POST http://localhost:8080/api/game/stop
```

Stopping cancels any running operation, such as a start or a connection attempt, and waits until Minecraft has exited:

```json
{
  "mode": "graceful",
  "status": {
    "state": "idle",
    "operationId": 5,
    "sessionId": "16721635-5993-47fc-827e-727262544c24",
    "operation": "stop",
    "operationState": "succeeded",
    "processId": null,
    "exitCode": 0,
    "server": null,
    "message": "Game stopped",
    "error": null,
    "failure": null,
    "warnings": [],
    "updatedAt": "2026-08-12T12:02:00+00:00"
  }
}
```

| Mode | Meaning |
| :--- | :------ |
| `alreadyStopped` | No game was running. |
| `graceful` | Minecraft closed when asked. |
| `forced` | Minecraft didn't close within 10 seconds and was killed. |

## Errors

Errors use the [Problem Details](https://www.rfc-editor.org/rfc/rfc9457) format. Failed game operations include the same `failure` object as the status:

```json
{
  "type": "about:blank",
  "title": "Client connect failed",
  "status": 500,
  "detail": "The server is full!",
  "failure": {
    "code": "client.connect.rejected",
    "operation": "connect",
    "stage": "connection.rejected",
    "message": "The server is full!",
    "exceptionType": "Void.Client.Failures.GameClientException",
    "stackTrace": "…"
  }
}
```

| Status | Meaning |
| :----- | :------ |
| `400 Bad Request` | A required value is missing or invalid. |
| `404 Not Found` | The diagnostic session doesn't exist. |
| `408 Request Timeout` | The request was canceled. |
| `409 Conflict` | The game's state doesn't allow the request, or another operation is running. |
| `500 Internal Server Error` | The operation failed. |
| `503 Service Unavailable` | The API, or the data requested, isn't available yet. |
| `504 Gateway Timeout` | The operation timed out. |

### Failures

| Field | Description |
| :---- | :---------- |
| `code` | Stable failure code to check in code. |
| `operation` | The part of the client that failed. |
| `stage` | The step that failed, such as the [connection stages](#connecting). |
| `message` | Human-readable description. |
| `exceptionType`, `stackTrace` | Details for debugging. Don't show them to untrusted users. |

| Code | Meaning |
| :--- | :------ |
| `client.process.crashed` | Minecraft or its Java runtime crashed. The message has the error from the crash report or the Java error log. |
| `client.process.exited` | Minecraft ended without a stop request. |
| `client.process.out_of_memory` | Minecraft ran out of memory and was killed. |
| `client.connect.rejected` | The server disconnected the game while it was joining. |
| `client.connect.failed` | The game couldn't connect for another reason. |
| `client.chat.failed` | The chat message couldn't be sent. |
| `client.players.unavailable` | The players couldn't be read. |
| `client.operation.timeout` | The operation timed out. |
| `client.operation.failed` | Any other failure. |

## Diagnostics

Every launch creates a diagnostic session with the game's output, logs, crash reports, and a screenshot on failure. Its identifier is the `sessionId` in the start response.
Sessions remain after the game stops, so download them before removing the container.

```sh
client_session_id=$(curl --fail --silent \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.8","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/vanilla | jq -r '.sessionId')

# Run the test, then stop the game.
curl --fail --request POST http://localhost:8080/api/game/stop

curl --fail --output "client-diagnostics-$client_session_id.zip" \
  "http://localhost:8080/api/game/diagnostics/$client_session_id"
```

Downloads also work while the game runs and don't affect it. An unknown or expired session returns `404 Not Found`.

:::caution
Game logs and screenshots can contain player names, server addresses, and chat.
:::

### Listing sessions

`GET /api/game/diagnostics` lists sessions, newest first:

```json
[
  {
    "sessionId": "16721635-5993-47fc-827e-727262544c24",
    "launch": "start-vanilla:1.21.8:",
    "startedAt": "2026-09-05T12:00:00+00:00",
    "endedAt": "2026-09-05T12:02:00+00:00",
    "status": null,
    "lastFailure": null,
    "warnings": [],
    "downloadUrl": "/api/game/diagnostics/16721635-5993-47fc-827e-727262544c24"
  }
]
```

`status` is the session's latest game status. `lastFailure` keeps the session's most recent failure even after a stop clears it from the status. `endedAt` is `null` while the session is running. `warnings` lists evidence that is missing or was cut short.

### Archive contents

| File | Contents |
| :--- | :------- |
| `session.json` | The session's details, as in the list. |
| `operations.jsonl` | Every status change and operation result. |
| `console-*.log`, `previous-*` | Recent console output of the game and the client. |
| `logs-latest.log`, `logs-debug.log` | Minecraft's logs. |
| `crash-reports-crash-*.txt` | Minecraft crash reports. |
| `hs_err_pid*.log` | Error logs of the Java runtime, when it crashed. |
| `debug-disconnect-*.txt` | Minecraft disconnect reports. |
| `failure-*.png` | Screenshots taken when an operation failed. |

### Retention

| Variable | Default | Meaning |
| :------- | :------ | :------ |
| `VOID_DIAGNOSTICS_DIRECTORY` | `/var/lib/void-client/diagnostics` | Where sessions are stored. |
| `VOID_DIAGNOSTICS_MAXIMUM_SESSIONS` | `10` | Maximum number of sessions kept. |
| `VOID_DIAGNOSTICS_MAXIMUM_TOTAL_MB` | `256` | Storage for all sessions, in MiB. |
| `VOID_DIAGNOSTICS_MAXIMUM_SESSION_MB` | `32` | Storage for one session, in MiB. |

When a limit is reached, the oldest finished sessions are removed first. The running session is never removed. Evidence that doesn't fit is left out and noted in its `warnings`.

To keep sessions after the container is removed, mount a volume at the diagnostics directory, for example `--volume client-diagnostics:/var/lib/void-client/diagnostics`.
