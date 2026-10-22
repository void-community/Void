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
| `POST` | `/api/game/start/forge` | `202` | Start Forge. |
| `POST` | `/api/game/start/fabric` | `202` | Start Fabric. |
| `POST` | `/api/game/start/quilt` | `202` | Start Quilt. |
| `POST` | `/api/game/start/legacyfabric` | `202` | Start LegacyFabric. |
| `POST` | `/api/game/start/curseforge` | `202` | Start a CurseForge modpack. |
| `POST` | `/api/game/connect` | `200` | Connect the game to a server. |
| `POST` | `/api/game/disconnect` | `200` | Leave the server, keeping the game running. |
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
  "message": "Game menu is ready",
  "error": null,
  "failure": null,
  "disconnect": null,
  "warnings": [],
  "updatedAt": "2026-08-12T12:00:00+00:00"
}
```

| Field | Description |
| :---- | :---------- |
| `state` | The game's state, described below. |
| `operationId` | Identifier of the latest operation. It increases with every accepted operation. |
| `sessionId` | [Diagnostic session](#diagnostics) of the latest launch, or `null` before the first one. |
| `operation` | The latest operation: `start-vanilla`, `start-neoforge`, `start-forge`, `start-fabric`, `start-quilt`, `start-legacyfabric`, `start-curseforge`, `connect`, `disconnect`, `send-chat`, `screenshot`, `options`, or `stop`. |
| `operationState` | `none`, `running`, `succeeded`, `failed`, or `canceled`. |
| `processId` | Process identifier while the game runs. |
| `exitCode` | Exit code once the game has exited. It can be `0` even after a crash, so use `state` and `failure` instead. |
| `server` | `host` and `port` of the connected server, otherwise `null`. |
| `message` | Short human-readable description. |
| `error` | Error message when something failed, otherwise `null`. |
| `failure` | [Failure details](#failures) when something failed, otherwise `null`. |
| `disconnect` | Why the game isn't connected to a server, [described below](#after-a-connection-ends), otherwise `null`. |
| `warnings` | Non-fatal notes, such as a dialog the client dismissed. |
| `updatedAt` | Time of the latest change. |

| State | Meaning |
| :---- | :------ |
| `idle` | No game is running. |
| `starting` | The game is being prepared and launched. |
| `loading` | The game window is open and the game is loading. |
| `ready` | The game shows its menu and isn't connected to a server. |
| `connected` | The game is connected to a server. |
| `stopping` | The game is being stopped. |
| `failed` | The launch failed, or the game ended without a stop request. |

### Waiting for a game

Start requests return the status with `202 Accepted` and keep running in the background.
Save the returned `operationId` and poll `/api/game/status` until `state` is `ready`.
The start operation has `succeeded` once the game window opens, and `state` stays `loading` until the game shows its menu, which can take minutes with many mods.
The client sets no time limit on loading, so apply your own.
Before its window opens, a game that prints nothing for 5 minutes has stopped responding: the client closes it, and the start fails with `client.process.unresponsive`.
Treat the start as failed if `state` becomes `failed` or `idle`, or if `operationId` changes to another caller's operation.

When the client can't tell whether the game shows its menu, the game becomes `ready` once its window opens and `warnings` says why.

### When the game ends on its own

The game should only end through `POST /api/game/stop`. If Minecraft quits, crashes, or is killed instead, `state` becomes `failed` with one of these failure codes:

- `client.process.crashed` when Minecraft saved a crash report, or its Java runtime an error log. The message says what they say, for example `Minecraft crashed (Manually triggered debug crash): java.lang.Throwable: Manually triggered debug crash` or `Minecraft's Java runtime crashed: SIGSEGV (0xb) at pc=…, pid=…, tid=… in C [libc.so.6+0x8f228]`.
- `client.process.out_of_memory` when it ran out of memory and was killed.
- `client.process.exited` otherwise.

The client also closes a game that can't go on by itself, with the same result: NeoForge showing that mods failed to load, or Minecraft still running 10 seconds after it reported a crash.
If no operation was running at the time, `operation` and `operationState` still describe the last one.
A failed game can be started again.

When the whole container is killed instead, for example for [running out of memory on Kubernetes](/docs/client/images/#memory), the client starts over as `idle` with `operationId` `0`, and the [diagnostic session](#listing-sessions) of that game says it was cut off.

## Starting a Game

One game runs at a time. A start request is accepted when the state is `idle` or `failed` and returns `409 Conflict` otherwise.

All start requests accept these optional fields:

| Field | Description |
| :---- | :---------- |
| `arguments` | [PortableMC start arguments](https://github.com/mindstorm38/portablemc), such as `["--username", "TestPlayer"]`. Without a username, the game uses a generated offline name. |
| `memoryMb` | Maximum Java heap of the game in MiB. It must be lower than the container's [**memory limit**](/docs/client/images/#memory), which also holds the rest of the game and the client. Don't combine it with an `-Xmx` argument. |

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

To start an exact NeoForge build, for example the one your server runs, set `loaderVersion` to the build as NeoForge's Maven names it, such as `21.1.264`, or `1.20.1-47.1.106` for Minecraft 1.20.1. It can't be combined with `maximumMinecraftVersion`.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"loaderVersion":"21.1.264","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

With `version`, `loaderVersion` can also be the build as servers report it, such as `47.1.106` for Minecraft 1.20.1. When that Minecraft version has no such build, the start fails with `client.install.failed`.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.20.1","loaderVersion":"47.1.106","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

### Forge, Fabric, Quilt, and LegacyFabric

`version` is required and is a Mojang release identifier. `loaderVersion` is an optional loader build. Without it, the client starts the loader's newest stable build for that version, or its newest build when none is stable yet.
```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.20.1","loaderVersion":"47.4.0","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/forge
```

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.1","arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/fabric
```

Forge builds are named as on Forge's downloads, such as `47.4.0` for 1.20.1, or `10.13.4.1614-1.7.10` for 1.7.10, or as servers report them, such as `10.13.4.1614` for 1.7.10. A version or build that doesn't exist fails the start with `client.install.failed`.

Starts other than CurseForge load only the mods in the game directory's `mods` folder. An image built on the client can add files for the exact Minecraft version with [**preparation scripts**](/docs/client/images/#preparing-games).

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

Requests to CurseForge and downloads that fail temporarily are retried a few times. If a modpack file still can't be downloaded, the start fails with an error that lists every missing file.

A modpack runs in the `modpack` folder of the game directory, with its own mods, configuration, options, worlds, logs, and crash reports. Other starts run in the game directory itself and don't load a modpack's files. All of them share the downloaded Minecraft versions, libraries, and assets.

The `modpack` folder holds one modpack at a time. Starting the installed modpack again starts it right away. Starting another modpack or file replaces it, downloading only the files that aren't there yet. A failed or stopped start leaves no modpack installed, so the next start installs again and reuses what was downloaded. Files the previous modpack added in subfolders such as `config/` are removed. Worlds, files the game created, and `options.txt` stay.

## Connecting

The game must be `loading` or `ready`. A game that is still loading connects once it shows its menu. `port` must be between `1` and `65535`.

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

Switching servers behind a proxy keeps the game `connected`.

### After a connection ends

If the game leaves the server, for example after a kick or a timeout, `state` returns to `ready` and `server` becomes `null`.
`disconnect` keeps why the game isn't connected, both after it left a server and after a connection failed:

```json
"disconnect": {
  "server": {
    "host": "minecraft-server",
    "port": 25565
  },
  "code": "client.connection.lost",
  "stage": "connection.closed",
  "message": "Kicked by an operator",
  "disconnectedAt": "2026-08-12T12:05:00+00:00"
}
```

A failed connection has the `code`, `stage`, and `message` of its [failure](#failures). Unlike `failure`, `disconnect` stays through later operations, such as a screenshot, until the game connects, stops, or exits.

### Disconnecting

The game must be `connected`. It leaves the server as with the Disconnect button of its menu, and keeps running:

```bash
curl --fail-with-body \
  --request POST \
  http://localhost:8080/api/game/disconnect
```

The response is the [status](#status) once the game has left: `state` is `ready`, `server` is `null`, and `disconnect` has the code `client.disconnect.requested`. The game can then connect to any server again, without restarting.

Returns `409 Conflict` when the game isn't connected or another operation is running, and `504 Gateway Timeout` when the game is still connected 10 seconds after leaving. A game that fails to leave stays `connected`.

## Sending Chat

The game must be `connected`. Messages that start with `/` are sent as commands. While a proxy moves the game to another server, the message waits until the game is in that server's world.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"message":"Hello from the client"}' \
  http://localhost:8080/api/game/send-chat
```

Returns `409 Conflict` when the game has left its server, or still isn't in a world 5 minutes later.

## Players

`GET /api/game/players` returns the local player and the other players the game can see. The game must be `loading`, `ready`, or `connected`. No server plugin or mod is needed.

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

The game must be `loading`, `ready`, or `connected`.

```bash
curl --fail --output minecraft.png http://localhost:8080/api/game/screenshot
```

## Game Options

Send the complete contents of Minecraft's `options.txt` as `text/plain`. They apply from the next launch, including the installed modpack's. A modpack without options of its own starts with these.

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
    "disconnect": null,
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
| `client.process.unresponsive` | Minecraft printed nothing for 5 minutes before opening its window, so the client closed it. |
| `client.install.unavailable` | A server the game is installed from, such as CurseForge, Mojang or a JVM agent's server, stayed unavailable after retries. Starting again later may work. The stage is `curseforge`, `minecraft` or `agent`, as below. |
| `client.install.failed` | The game couldn't be installed. Stage `curseforge` means the modpack couldn't be installed, for example because it or one of its files doesn't exist; stage `minecraft` means Minecraft or its mod loader couldn't be prepared, for example because the version doesn't exist, or one of the image's [**preparation scripts**](/docs/client/images/#preparing-games) failed; stage `agent` means one of the [**JVM agents**](/docs/client/images/#jvm-agents) doesn't exist or couldn't be downloaded. |
| `client.connect.rejected` | The server disconnected the game while it was joining. |
| `client.connect.failed` | The game couldn't connect for another reason. |
| `client.connection.lost` | The game left a server it had joined. Only in [`disconnect`](#after-a-connection-ends). |
| `client.disconnect.requested` | The game left the server because it was [asked to](#disconnecting). Only in [`disconnect`](#after-a-connection-ends). |
| `client.disconnect.failed` | The game couldn't leave the server. |
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

If the client itself was killed while a game ran, its session ends when the client starts again with the same diagnostics directory: `endedAt` is when its last evidence was saved, `status` is the last state recorded, and `warnings` says the client was stopped before the session ended.

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
