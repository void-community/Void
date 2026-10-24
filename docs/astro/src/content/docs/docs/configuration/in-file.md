---
title: In-file
description: Learn how to configure Void Proxy from files.
sidebar:
  order: 0
---

File configurations are the primary way to configure Void.
However, many options can be overridden at runtime using the API in plugins.

## Proxy

```toml
// settings.toml
# Defines the network interface to bind on
Address = "0.0.0.0"

# Defines the port
Port = 25565

# Packet size threshold before compression
CompressionThreshold = 256

# General per player timeout for many operations (in milliseconds)
KickTimeout = 10000

# Allow players to connect without Mojang authentication
Offline = false

# Logging level (valid values are Trace, Debug, Information, Warning, Error, Critical)
LogLevel = "Information"

# Predefined list of servers. 
# Players will be connected to the first one, if not specified otherwise from plugins or not selected by overridden hostname.
[[Servers]]
Name = "lobby"
Host = "127.0.0.1"
Port = 25566
Override = "lobby.example.org"

[[Servers]]
Name = "minigames"
Host = "127.0.0.1"
Port = 25567

[[Servers]]
Name = "limbo"
Host = "127.0.0.1"
Port = 25568
```

### Server Overrides
The `Override` property allows you to redirect players to specific servers based on the hostname they use to connect to the proxy. This enables you to route players to different backend servers depending on the domain or subdomain they use.

When a player connects using a hostname that matches a server's `Override` value, they will be automatically redirected to that server instead of the default first server in the list.

```toml title="Example with Overrides"
# Default server for players connecting via any non-matching hostname
[[Servers]]
Name = "lobby"
Host = "127.0.0.1"
Port = 25566

# Players connecting via minigames.example.org will be sent to the minigames server
[[Servers]]
Name = "minigames"
Host = "127.0.0.1"
Port = 25567
Override = "minigames.example.org"

# Players connecting via limbo.example.org will be sent to the limbo server
[[Servers]]
Name = "limbo"
Host = "127.0.0.1"
Port = 25568
Override = "limbo.example.org"
```

:::tip
You can also configure overrides via [**program arguments**](/docs/configuration/program-arguments/#servers) using the `--override` flag, which allows you to specify overrides at runtime without modifying configuration files.
:::

## Watchdog
[**Watchdog**](/docs/watchdog) is a built-in HTTP server that allows you to check status or control the proxy.

```toml
// configs/Watchdog/Settings.toml
# Enables the watchdog
Enabled = false

# Defines the network interface to bind on
Address = "*"

# Defines the port
Port = 80
```

## Forwarding
[**Forwarding**](/docs/forwardings/forwarding-overview) helps to forward player data (IP, UUID, Skin, etc.) to the backend server.

### Modern (Velocity)
Read more in [**Modern (Velocity)**](/docs/forwardings/modern) forwarding.

```toml
// configs/Velocity/Settings.toml
# Enables Velocity forwarding
Enabled = true

# The secret key (should be the same as on the backend server)
Secret = "YourSecretKeyHere"
```

## Slow Join
The proxy forwards keep-alives between players and servers, so servers decide when a player has timed out. Large modpacks can keep a client too busy to answer for minutes after it joins a server. The built-in Slow Join plugin answers the server's keep-alives for such a player and probes the player with keep-alives of its own. Mods such as JEI load in steps and answer promptly between them, so the plugin forwards keep-alives again only once the player has answered several probes in a row promptly. A slow answer starts the count over.

```toml
// configs/SlowJoin/Settings.toml
# Answers the server's keep-alives for players that are still loading after joining
Enabled = true

# How long a player may take to answer a keep-alive while loading after joining a server, in seconds
FirstKeepAliveTimeout = 180

# How many keep-alives in a row a loading player must answer promptly before keep-alives are forwarded again
PromptAnswers = 8

# How soon a loading player must answer a keep-alive for the answer to count as prompt, in seconds
PromptAnswerTime = 5
```

The plugin sends a probe with each keep-alive from the server, which vanilla servers send every 15 seconds, so the defaults need about two minutes of prompt answers.

:::tip
You can also set these via [**program arguments**](/docs/configuration/program-arguments/#slow-join) using the `--slow-join-timeout`, `--slow-join-prompt-answers` and `--slow-join-prompt-answer-time` flags, which take precedence over the file.
:::

## Plugin Installation

Plugins are compiled with the `.dll` extension in any .NET compatible language.
See the [**Plugin Development Kit**](/docs/developing-plugins/development-kit) section for more details.

- Directory `plugins` is the default location to install plugins.
- [**Environment variable**](/docs/configuration/environment-variables/#plugins) `VOID_PLUGINS` might be used to add URLs or local paths to run plugins, separated by comma or semicolon.
- [**Program argument**](/docs/configuration/program-arguments/#plugins) `--plugin` (alias `-p`) might be used to add URL or local path to a plugin.

```bash title="Program Argument Example"
$ ./void-linux-x64 --plugin "/home/YourPlugin1.dll" --plugin "https://example.org/download/YourPlugin2.dll"
```
```bash title="Environment Variable Example"
$ VOID_PLUGINS="https://example.org/download/YourPlugin1.dll;/home/YourPlugin2.dll" ./void-linux-x64
```

### Managing plugins at runtime

The `/plugins` command manages plugins while the proxy runs, from the console or from a player's chat, and answers whoever ran it.

- `/plugins` lists the loaded plugin containers.
- `/plugins load <name>` loads a plugin from the `plugins` directory or one of those built into the proxy. A file in `plugins` is loaded in place of the built-in plugin of the same name.
- `/plugins unload <name>` unloads a loaded plugin container.
- `/plugins reload <name>` unloads a loaded plugin container and loads it again.

A name matches the container named exactly so, ignoring case, or else the only container whose name contains it, so `/plugins reload essentials` reloads `Void.Proxy.Plugins.Essentials.dll`. Pressing Tab suggests the names each subcommand takes.

## Plugin Configurations (configs/\<Plugin\>/*.toml)

Each plugin may [**define one or a subset of keyed configuration files**](/docs/developing-plugins/configuration/) in its own directory. 
Plugins are not required to save or load configurations manually. 

All changes on the disk are automatically loaded into an existing instance in memory.
Vice versa, all changes in the memory are automatically saved to the disk. This automatic saving can be disabled via [**program argument**](/docs/configuration/program-arguments/#file-configuration).

Currently, only TOML format is supported.
