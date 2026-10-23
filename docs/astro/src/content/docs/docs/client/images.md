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

Minecraft data is stored in `/root/.minecraft` by default, and a [CurseForge modpack](/docs/client/api/#curseforge) in its `modpack` folder. Mount a volume to keep downloaded assets, options, mods, and other game files when the container is replaced:

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

## Preparing Games

An image built on the client can prepare its games, for example to add a mod for the exact Minecraft version. Once Minecraft and its mod loader are installed, and before the game launches, the client runs every file in `/etc/portableminecraftclient/prepare.d` in name order, with:

| Variable | Value |
| :------- | :---- |
| `MINECRAFT_DIRECTORY` | The [game directory](#persistent-game-directory). |
| `MINECRAFT_VERSION` | The Minecraft version, such as `1.21.1`. |
| `MINECRAFT_LOADER` | The start: `vanilla`, `neoforge`, `forge`, `fabric`, `quilt`, or `legacyfabric`. |

```dockerfile
FROM ghcr.io/void-community/portable-minecraft-client:latest

COPY --chmod=0755 prepare /etc/portableminecraftclient/prepare.d/10-prepare
```

Each file must be executable. A script that exits with a code other than `0`, can't run, or runs longer than 5 minutes fails the start with `client.install.failed`, and the scripts after it don't run. Their output appears in the container log and the start's [**diagnostic session**](/docs/client/api/#diagnostics). CurseForge starts don't run them, because a modpack brings its own files. To change a modpack's files, [patch](#patching-files) them instead.

## Patching Files

`VOID_PATCH_DEFINITIONS` changes the game's files on every start, before the game launches, with patch definitions in the format of itzg's [**`PATCH_DEFINITIONS`**](https://github.com/itzg/docker-minecraft-server/blob/master/docs/configuration/interpolating.md#patching-existing-files), so a definition written for a server works for a client too. Like `JAVA_AGENTS`, it's a comma or newline separated list. Each entry is an HTTP or HTTPS URL or a file that holds a patch definition or a patch set (`{"patches": [...]}`), or a directory whose `*.json` files each hold a patch definition.

A relative `file` is resolved against the directory the game runs in: the [game directory](#persistent-game-directory), or its `modpack` directory for a CurseForge start. Patches run after the [preparation scripts](#preparing-games), and for a CurseForge start after the modpack and its overrides are installed, so they apply on every start.

Text values can name environment variables as `${NAME}`. Only names that start with `VOID_PATCH_ENV_PREFIX`, which defaults to `CFG_`, are replaced, and `NAME_FILE` reads the value from a file instead. For example, some All the Mods 10 releases freeze the game unless FML's config watcher is off:

```json
{
  "file": "config/fml.toml",
  "ops": [
    {
      "$set": {
        "path": "$.disableConfigWatcher",
        "value": "${CFG_FML_DISABLE_CONFIG_WATCHER}",
        "value-type": "bool"
      }
    }
  ]
}
```

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --env VOID_PATCH_DEFINITIONS=https://example.org/config--fml-toml.patch.json \
  --env CFG_FML_DISABLE_CONFIG_WATCHER=true \
  ghcr.io/void-community/portable-minecraft-client:latest
```

| Supported | |
| :-------- | :- |
| Files | JSON, TOML and properties files, detected by their suffix or named by `file-format`. |
| Operations | `$set` replaces a value, or adds it to an existing object. `$put` sets a `key` on an object. `$add` appends to a list. |
| Paths | `$.a.b`, `$['a.b']` and `$.list[0]`. |
| Value types | `int`, `float`, `bool` (`true`, `yes`, `false` or `no`), `auto`, and `list of` any of them, for text values. |

A patched file loses its comments, as it does on itzg's image. YAML and JSON5 files aren't supported, unlike on itzg's image.

Each applied operation appears in the container log and the start's [**diagnostic session**](/docs/client/api/#diagnostics), and so does each patch skipped because its file doesn't exist, can't be read or has an unsupported format, and each operation skipped because a variable it names is missing. A source that can't be read and an operation that fails, such as one whose path doesn't exist, fail the start with `client.install.failed`, or with `client.install.unavailable` when a URL's server stayed unavailable after retries. Either way, the failure's stage is `minecraft`.

## Memory

The container's memory holds the game's Java heap, set with `memoryMb`, and everything else: the rest of the game's memory and the client itself. Give it at least 1 GiB more than `memoryMb`, and more for large modpacks:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --memory 3g \
  ghcr.io/void-community/portable-minecraft-client:latest
```

A start with a `memoryMb` that isn't lower than the container's limit is rejected with `400 Bad Request`.

When the container runs out of memory, the system kills the game, and the client reports `client.process.out_of_memory`.

On Kubernetes 1.28 or later with cgroup v2, the kubelet kills every process in the container instead, so the client restarts without reporting it. The pod's `status.containerStatuses[].lastState.terminated.reason` is then `OOMKilled`, and with a volume at the [**diagnostics directory**](/docs/client/api/#retention), such as an `emptyDir`, the game's diagnostic session survives and says it was cut off. Setting `singleProcessOOMKill: true` in the kubelet configuration (Kubernetes 1.32 or later) kills only the game, so the client can report it.

## JVM Agents

`JAVA_AGENTS` loads Java agents, which are jars, into every game the client starts, as Java's `-javaagent` option does. `JVM_AGENTS` loads native JVM agents, as Java's `-agentpath` option does. Each is a comma or newline separated list, as in itzg's image: text after `#` is a comment, and blank entries are ignored. Each agent is an absolute path in the container or an HTTP or HTTPS URL. The client downloads a URL at the first start that needs it and reuses the download for later starts. For example, [**ModLoaderFixes**](https://github.com/void-community/ModLoaderFixes) lets Forge for Minecraft 1.7.2 start on Java 8u20 and later:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --env JAVA_AGENTS=https://github.com/void-community/ModLoaderFixes/releases/latest/download/modloaderfixes.jar \
  ghcr.io/void-community/portable-minecraft-client:latest
```

An agent that doesn't exist or can't be downloaded fails the start with `client.install.failed`, or with `client.install.unavailable` when its server stayed unavailable after retries. Either way, the failure's stage is `agent`.

## CurseForge Configuration

Starting a CurseForge modpack requires `CURSEFORGE_API_KEY`:

```bash
docker run --name void-client --rm -d -p 8080:80 \
  --env CURSEFORGE_API_KEY=your-api-key \
  ghcr.io/void-community/portable-minecraft-client:latest
```

`CURSEFORGE_API_BASE_URL` optionally replaces the default `https://api.curseforge.com` API base URL.

`CURSEFORGE_PARALLEL_DOWNLOADS` sets how many modpack files download at the same time and defaults to `4`, like itzg's `CF_PARALLEL_DOWNLOADS`. It must be a positive integer.

[Patch definitions](#patching-files) apply to the modpack's files on every start.


## Launcher Retry Configuration

`PORTABLEMC_DRY_RUN_ATTEMPTS` controls preparation attempts and defaults to `5`. It must be a positive integer. `PORTABLEMC_DRY_RUN_RETRY_DELAY_SECONDS` sets the base retry delay, defaults to `5`, and accepts non-negative integers.

For diagnostic storage and retention variables, see the [**diagnostics configuration**](/docs/client/api/#retention).
