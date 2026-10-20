---
title: Overview
description: Run and automate the Docker Minecraft client.
---

The Void client image runs a demo-mode Minecraft client that can be controlled over HTTP.
It is intended for automated workflows such as integration tests, demonstrations, and compatibility checks.
Void uses the same image for its own integration tests and [**online demo**](https://void-demo.caunt.world/).
The demo starts the newest Minecraft release that has a stable NeoForge build and that its Paper server accepts, and adds Sodium when a build for that version is available.

## Quick Start

Run the current image and publish its HTTP port:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  ghcr.io/void-community/portable-minecraft-client:latest
```

Wait until the client service is ready:

```bash
curl --fail http://localhost:8080/api/health
```

Start the newest Minecraft release that has a stable NeoForge build:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"arguments":["--username","TestPlayer"]}' \
  http://localhost:8080/api/game/start/neoforge
```

The start request returns `202 Accepted`. Poll the returned operation with:

```bash
curl --fail http://localhost:8080/api/game/status
```

When `state` is `ready` and `operationState` is `succeeded`, connect the game:

```bash
curl --fail-with-body \
  --request POST \
  --header 'Content-Type: application/json' \
  --data '{"host":"minecraft-server","port":25565}' \
  http://localhost:8080/api/game/connect
```

:::tip
Keep the `operationId` returned by the start request and match it while polling.
This ensures that you observe the operation you started.
:::

## Exposed Interfaces

The container exposes port `80` with two route groups:

| Route | Purpose |
| :---- | :------ |
| `/api/*` | Control Minecraft and inspect its current state. |
| `/vnc/*` | View and interact with the Minecraft window in a browser. |

Open the browser client at:

```text
http://localhost:8080/vnc/vnc.html?autoconnect=true&resize=scale&path=/vnc/websockify
```

See the [**API Reference**](/docs/client/api/) for every endpoint and [**Images**](/docs/client/images/) for available tags.

## Reusing a Container

One container runs one Minecraft game at a time. Stop the current game before starting another:

```bash
curl --fail-with-body --request POST http://localhost:8080/api/game/stop
```

After the status returns to `idle`, the same container can start another version or connect to another server.
The container itself does not need to be recreated between game sessions.

:::note[Demo Mode]
Minecraft is always launched in demo mode.
:::


## Collect diagnostics for CI failures

Save the `sessionId` returned by the start request. After the test, stop Minecraft and download `/api/game/diagnostics/{sessionId}` before removing the container. The ZIP retains the session's operation history, recent output, available Minecraft failure reports, and a failure screenshot when capture succeeds. It remains available when another Minecraft version starts in the same container, until retention limits expire it.

See the [**diagnostics API reference**](/docs/client/api/#diagnostics) for request examples, bundle contents, retention settings, and persistent-volume configuration.

Void's integration harness downloads a bundle at every game teardown, including failed launches. This also preserves evidence for assertions that fail after the game has stopped. Each client's bundle is stored at `steps/<test>/<protocol>/<username>/client-diagnostics-<sessionId>.zip` alongside existing screenshots and server/proxy logs. Forge and NeoForge games are stored under `steps/<test>/<loader>/<protocol>/<username>/`, with a `game-setup.json` that names the loader build and the mods every side ran. Download the `integration-steps-<os>-<shard>` artifact from the GitHub Actions run to inspect it.

Collection uses a separate timeout so a canceled test can still save evidence. If an older image lacks the endpoint, or the API cannot be reached, `client-diagnostics-error.txt` explains the collection failure without replacing the test's original error.
