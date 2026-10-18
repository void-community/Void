---
title: Images
description: Choose a Docker client image and configure its storage.
---

Client images are hosted in the GitHub Container Registry as `ghcr.io/void-community/portable-minecraft-client`.
Every published image supports `linux/amd64` and `linux/arm64`.

Browse the [**published client images**](https://github.com/void-community/Void/pkgs/container/portable-minecraft-client) for the current tags and digests.

## Image Tags

| Tag | Contents | Recommended use |
| :-- | :------- | :-------------- |
| `latest` | Current client runtime. Game assets are prepared when requested. | General use and the Void demo. |
| `offline` | Mojang release assets from `1.7.2` through the latest release available when the image was published. | Integration suites that exercise many versions. |
| `<minecraft-version>` | Assets prepared for one Mojang release, such as `1.7.2` or `1.21.8`. | Workflows fixed to one game version. |

The `latest` and `offline` tags are replaced when new images are published.
Version tags are also rebuilt by the publishing workflow and identify prepared game assets, not a permanent image build.
Pin an image digest when a workflow must use identical image contents.

:::note
`offline` describes the preloaded Minecraft release assets. Other requested content or first-run updates may still require external downloads.
:::

## Pulling Images

Pull the current runtime image:

```bash
docker pull ghcr.io/void-community/portable-minecraft-client:latest
```

Pull the multi-version image used by Void integration tests:

```bash
docker pull ghcr.io/void-community/portable-minecraft-client:offline
```

Pull an image prepared for a specific release:

```bash
docker pull ghcr.io/void-community/portable-minecraft-client:1.21.8
```

Pin exact image contents with a digest from the package page:

```bash
docker pull ghcr.io/void-community/portable-minecraft-client@sha256:<digest>
```

## Persistent Game Directory

Minecraft data is stored in `/root/.minecraft` by default. Mount a volume to keep downloaded assets, options, mods, and other game files when the container is replaced:

```bash
docker volume create void-client-minecraft

docker run --name void-client --rm -d -p 8080:80 \
  --volume void-client-minecraft:/root/.minecraft \
  ghcr.io/void-community/portable-minecraft-client:latest
```

Use `MINECRAFT_DIRECTORY` when mounting the data at another container path:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --env MINECRAFT_DIRECTORY=/minecraft \
  --volume void-client-minecraft:/minecraft \
  ghcr.io/void-community/portable-minecraft-client:latest
```

## CurseForge Configuration

Starting a CurseForge modpack requires `CURSEFORGE_API_KEY`:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --env CURSEFORGE_API_KEY=your-api-key \
  ghcr.io/void-community/portable-minecraft-client:latest
```

`CURSEFORGE_API_BASE_URL` optionally replaces the default `https://api.curseforge.com` API base URL.


## Launcher Retry Configuration

`PORTABLEMC_DRY_RUN_ATTEMPTS` controls preparation attempts and defaults to `5`. It must be a positive integer. `PORTABLEMC_DRY_RUN_RETRY_DELAY_SECONDS` sets the base retry delay, defaults to `5`, and accepts non-negative integers.

For diagnostic storage and retention variables, see the [**diagnostics configuration**](/docs/client/api/#retention-configuration).
