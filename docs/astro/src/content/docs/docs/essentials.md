---
title: Essentials
description: Built-in commands for server redirection, moderation, and debugging.
sidebar:
  order: 0
---

The [**Essentials plugin**](https://github.com/caunt/Void/tree/main/src/Plugins/Essentials) ships with Void and offers basic utilities for managing the proxy and connected players.

## Server Redirection

Use `/server [server-name]` to send yourself to another backend server. If no name is given, one is chosen at random. Register servers in the [**server configuration**](/docs/configuration/in-file/#proxy) or with the [**program argument**](/docs/configuration/program-arguments/#servers) `--server`.

## Platform Commands

- `/stop` — immediately stops the proxy.

The proxy itself provides [**`/plugins`**](/docs/configuration/in-file/#managing-plugins-at-runtime) to list, load, unload and reload plugins, so it stays available when Essentials is unloaded.

## Moderation

`/kick <name> [reason]` removes a player from the proxy with an optional message.

## Debugging

Essentials logs every network message at the trace level, helping diagnose issues during development or testing. See the [**logging argument example**](/docs/configuration/program-arguments/#logging) for how to start the proxy with `--logging Trace`.
