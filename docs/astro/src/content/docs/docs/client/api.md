---
title: API Reference
description: Control the Docker Minecraft client over HTTP.
---

The client API is available under `/api` on container port `80`.
JSON property names and enum values use camel case.

## Endpoints

| Method | Path | Success | Description |
| :----- | :--- | :------ | :---------- |
| `GET` | `/api/health` | `200` | Check whether the client service is ready. |
| `GET` | `/api/game/status` | `200` | Read the game state and latest operation. |
| `GET` | `/api/game/diagnostics` | `200` | List retained diagnostic sessions. |
| `GET` | `/api/game/diagnostics/{sessionId}` | `200` | Download a session diagnostic ZIP. |
| `GET` | `/api/game/players` | `200` | Read the live local player and client-tracked players. |
| `PUT` | `/api/game/options` | `204` | Replace Minecraft `options.txt`. |
| `POST` | `/api/game/start/vanilla` | `202` | Start a Mojang vanilla release. |
| `POST` | `/api/game/start/neoforge` | `202` | Start a NeoForge release, the newest with a stable build by default. |
| `POST` | `/api/game/start/curseforge` | `202` | Start a CurseForge modpack file. |
| `POST` | `/api/game/connect` | `200` | Converge the running game on a server connection. |
| `POST` | `/api/game/send-chat` | `204` | Send chat text or a command. |
| `GET` | `/api/game/screenshot` | `200` | Return the Minecraft window as `image/png`. |
| `POST` | `/api/game/stop` | `200` | Stop the current game. |

## Status and Operations

`GET /api/game/status` returns the current state and the most recently accepted operation:

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
| `state` | Game lifecycle: `idle`, `starting`, `ready`, `connected`, `stopping`, or `failed`. |
| `operationId` | Increasing identifier for the latest accepted operation. |
| `sessionId` | Diagnostic session identifier assigned to an accepted launch; `null` before the first launch. Retained after stop until another launch is accepted. |
| `operation` | Latest operation: `start-vanilla`, `start-neoforge`, `start-curseforge`, `options`, `connect`, `send-chat`, `screenshot`, or `stop`; otherwise `null`. |
| `operationState` | `none`, `running`, `succeeded`, `failed`, or `canceled`. |
| `processId` | Minecraft process identifier while it is running. |
| `exitCode` | Minecraft exit code after it exits, when available. |
| `server` | Connected `host` and `port`, otherwise `null`. |
| `message` | Short description of the current result. |
| `error` | Failure message, otherwise `null`. |
| `failure` | Structured runtime failure with `code`, `operation`, `stage`, `message`, `exceptionType`, and full `stackTrace`; otherwise `null`. |
| `warnings` | Non-fatal messages associated with the state. |
| `updatedAt` | ISO 8601 timestamp of the latest status change. |

Start operations return this status with `202 Accepted` and a `Location: /api/game/status` header.
Save its `operationId`, then poll until the same operation reports `ready` and `succeeded`.
Stop polling with a failure if the identifier changes or the operation becomes `failed` or `canceled`.

Runtime failures are also returned in the `failure` extension of HTTP Problem Details responses. An unexpected Minecraft process exit uses `client.process.exited`; a cgroup-confirmed OOM kill uses `client.process.out_of_memory` and reports the exit code and configured heap. Stack traces intentionally expose container implementation details and should not be forwarded to untrusted consumers.

## Live Players

`GET /api/game/players` reads players from the running Minecraft client. It does not require a mod, server plugin, or protocol-specific configuration.

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

Every returned player always contains finite `x`, `y`, and `z` coordinates plus finite body yaw, head yaw, and head pitch values in degrees. Profile identity is `null` when unavailable. A client without a current player world returns `409 Conflict`; tracker initialization or complete position or rotation failures return `503 Service Unavailable` rather than partial player data.

## Health

```bash
curl --fail http://localhost:8080/api/health
```

A ready API returns `200 OK` with `ok` as plain text. An unavailable client service returns `503 Service Unavailable`.

## Game Options

Send the complete `options.txt` contents as `text/plain`:

```bash
curl --fail-with-body \
  --request PUT \
  --header 'Content-Type: text/plain' \
  --data-binary $'fullscreen:false\nrenderDistance:2\nenableVsync:false' \
  http://localhost:8080/api/game/options
```

The options are retained in the configured Minecraft directory and used by later launches.

## Starting a Game

Only one game can run at a time. Every start request accepts an optional `arguments` array containing PortableMC start arguments and an optional positive `memoryMb` integer. When provided, `memoryMb` sets the Minecraft JVM maximum heap through `-Xmx`; it does not limit total container memory. Do not also provide a raw `-Xmx` argument.

Mojang used a 2 GiB default before 26.1 and changed it to 4 GiB for 26.1. See [**Minecraft Java Edition memory allocation**](https://help.minecraft.net/hc/en-us/articles/39083573916941) and the [**Minecraft Java Edition 26.1 release notes**](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-1).

### Vanilla

`version` is a Mojang release identifier:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.8","memoryMb":2048,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/vanilla
```

### NeoForge

`version` is an optional Minecraft release identifier, the same form the vanilla endpoint accepts. Omit it, or send a
blank value, to start the newest Mojang release that has a stable NeoForge build. While a new Minecraft release only has
NeoForge beta builds, the client falls back to the newest earlier release that has a stable build. Snapshots and
pre-releases are never selected.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"memoryMb":4096,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

`maximumMinecraftVersion` optionally caps that choice at a Mojang release identifier, for example when the server only
accepts versions up to that release. It applies only when `version` is omitted: sending both returns `400`, and an
identifier that is not a Mojang release fails the launch.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"maximumMinecraftVersion":"26.2","memoryMb":4096,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

Supply `version` to start NeoForge for a specific Minecraft version instead:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.1","memoryMb":2048,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

Sodium is installed automatically for the exact resolved Minecraft version: the newest build on Modrinth, including betas,
because older Sodium builds can target NeoForge internals that newer NeoForge builds no longer have. The launch continues
without Sodium when Modrinth has no build for that version.
On ARM64 images the client also selects the LWJGL natives and, when needed, downloads the Java runtime that the resolved
Minecraft version requires.

### CurseForge

Provide a project `slug` and positive CurseForge `fileId`. The container must have `CURSEFORGE_API_KEY` configured.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"slug":"all-the-mods-10","fileId":1234567,"memoryMb":4096,"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/curseforge
```

:::note
Replace the example CurseForge file identifier with the exact file you want to launch.
:::

## Connecting

The game must be running. `port` must be between `1` and `65535`. Connection navigation is idempotent for a given `host` and `port`: repeated callers join the same background operation, and an already-connected game returns its original result. A different target is rejected with `409 Conflict` while connecting or connected.

Canceling an HTTP caller stops only that caller's wait. The background navigation continues from whichever Minecraft screen is currently visible until the game connects, exits, or is stopped through the API.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"host":"minecraft-server","port":25565}' \
  http://localhost:8080/api/game/connect
```

The confirmed connection returns:

```json
{
  "server": {
    "host": "minecraft-server",
    "port": 25565
  },
  "connectedAt": "2026-08-12T12:01:00+00:00"
}
```

## Sending Chat

The game must be `connected`. A leading `/` sends a Minecraft command.

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"message":"Hello from the client"}' \
  http://localhost:8080/api/game/send-chat
```

## Taking a Screenshot

The game must be `ready` or `connected`.

```bash
curl --fail \
  --output minecraft.png \
  http://localhost:8080/api/game/screenshot
```

## Stopping

```bash
curl --fail-with-body \
  --request POST \
  http://localhost:8080/api/game/stop
```

The response contains `mode` and the final `status`:

```json
{
  "mode": "graceful",
  "status": {
    "state": "idle",
    "operationId": 5,
    "operation": "stop",
    "operationState": "succeeded",
    "processId": null,
    "exitCode": 0,
    "server": null,
    "message": "Game stopped",
    "error": null,
    "warnings": [],
    "updatedAt": "2026-08-12T12:02:00+00:00"
  }
}
```

`mode` is `alreadyStopped`, `graceful`, or `forced`.

## Errors

Errors use the standard Problem Details JSON format.

| Status | Meaning |
| :----- | :------ |
| `400 Bad Request` | A required value is missing or invalid. |
| `408 Request Timeout` | The request was canceled. |
| `409 Conflict` | The requested action is not valid for the current game state or another operation is running. |
| `500 Internal Server Error` | The operation failed unexpectedly. |
| `503 Service Unavailable` | The client service is not ready. |
| `504 Gateway Timeout` | A client operation exceeded its timeout. |


## Retained diagnostics

Each accepted launch creates a diagnostic session before preparation begins. A session includes early launch failures as well as evidence from a running game. Stopping Minecraft does not erase it, and subsequent launches receive different identifiers.

### List sessions

`GET /api/game/diagnostics` returns sessions newest first:

```json
[
  {
    "sessionId": "16721635-5993-47fc-827e-727262544c24",
    "launch": "start-vanilla:1.21.1:",
    "startedAt": "2026-09-05T12:00:00+00:00",
    "endedAt": "2026-09-05T12:02:00+00:00",
    "status": null,
    "lastFailure": null,
    "warnings": [],
    "downloadUrl": "/api/game/diagnostics/16721635-5993-47fc-827e-727262544c24"
  }
]
```

`status` contains the latest game status recorded for that session when available. `lastFailure` preserves the most recent structured failure even when a successful stop has cleared the current status error. `warnings` describes missing, truncated, or unavailable evidence. `endedAt` is `null` until the session is finalized.

### Download a session

`GET /api/game/diagnostics/{sessionId}` returns `200 OK` with `Content-Type: application/zip` and a download filename. An unknown or expired session returns `404 Not Found`.

Save the `sessionId` from the accepted start response and use that exact identifier during cleanup:

```sh
curl --fail --silent --show-error \
  --request POST http://localhost:8080/api/game/start/vanilla \
  --header 'Content-Type: application/json' \
  --data '{"version":"1.21.1","arguments":["--username","TestPlayer"]}' \
  --output launch.json

client_session_id=$(jq -r '.sessionId' launch.json)

# Poll readiness, connect, and perform the test before cleanup.
curl --fail --request POST http://localhost:8080/api/game/stop
curl --fail --silent --show-error \
  "http://localhost:8080/api/game/diagnostics/$client_session_id" \
  --output "client-diagnostics-$client_session_id.zip"
```

Download before removing the container. Downloads also work while Minecraft runs; they capture evidence available at that moment without changing the current operation or game state.

The ZIP contains available evidence:

| File | Contents |
| :--- | :--- |
| `session.json` | Session identity, launch description, timestamps, latest status, last failure, and collection warnings. |
| `operations.jsonl` | Timestamped game status transitions and operation outcomes. |
| `console-*.log` | Recent launcher, Minecraft, agent, and preparation output. |
| `previous-*.log`, `previous-operations.jsonl` | Previous retained segment after output rotation. |
| `logs-latest.log`, `logs-debug.log` | Recent Minecraft log contents changed during this session. |
| `debug-disconnect-*.txt` | Minecraft disconnect reports from this session. |
| `crash-reports-crash-*.txt` | Minecraft crash reports from this session. |
| `failure-*.png` | Best-effort failure screenshot, when the matching game window remains available. |

Missing reports or failed screenshots do not replace the original game error. Screenshot collection is limited to three seconds. Console output continues to appear in Docker logs. The bundle excludes credential stores and agent authentication tokens, but game logs and screenshots can still contain player names, server addresses, and chat.

### Retention configuration

Configure the container with Docker environment variables:

| Variable | Default | Meaning |
| :--- | :--- | :--- |
| `VOID_DIAGNOSTICS_DIRECTORY` | `/var/lib/void-client/diagnostics` | Session evidence directory. |
| `VOID_DIAGNOSTICS_MAXIMUM_SESSIONS` | `10` | Maximum retained sessions; oldest completed sessions expire first. |
| `VOID_DIAGNOSTICS_MAXIMUM_TOTAL_MB` | `256` | Total storage budget in MiB. |
| `VOID_DIAGNOSTICS_MAXIMUM_SESSION_MB` | `32` | Storage budget per session in MiB. |

Limits must be positive and the total budget must accommodate one session. Individual evidence files are capped at 2 MiB; console and operation logs rotate to retain recent output. Collection warnings identify truncation and omitted evidence. Active sessions are protected from eviction, so reaching a storage limit can omit additional evidence until space becomes available.

Storage survives game stop and start within the same container. For persistence after container removal, mount a volume at the diagnostics directory, for example `--volume client-diagnostics:/var/lib/void-client/diagnostics`. Stored sessions are discovered when the API starts again.
