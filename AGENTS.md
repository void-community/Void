# Repository Guidelines

## Structure

This repository is organized around these root components:

- `.github` — GitHub Actions workflows, issue templates, and repository automation.
- `demo` — sample browser-hosted Void proxy, Minecraft server, and client.
- `docs` — documentation site and generated documentation assets.
- `pdk` — plugin development kit sample project.
- `src` — production code, sample apps, debug harnesses, benchmarks, and built-in plugins.
- `tests` — xUnit tests and integration test infrastructure.

Project directories:

- `src/Api` — public proxy API abstractions.
- `src/Benchmarks` — benchmark projects for performance measurements.
- `src/Debug` — debug harness for running the proxy locally.
- `src/Minecraft` — Minecraft protocol, serialization, profile, and data libraries.
- `src/Platform` — main Void proxy application.
- `src/Playground` — sample playground app.
- `src/Plugins/Common` — shared infrastructure for built-in plugins.
- `src/Plugins/Essentials` — debugging and moderation plugin.
- `src/Plugins/ExamplePlugin` — minimal plugin API example.
- `src/Plugins/ForwardingSupport/Velocity` — Velocity forwarding support plugin.
- `src/Plugins/ModsSupport/CrossStitch` — CrossStitch mod integration plugin.
- `src/Plugins/ModsSupport/Forge` — Forge mod integration plugin.
- `src/Plugins/ProtocolSupport/Java/v1_7_2_to_1_12_2` — Java protocol support for 1.7.2 through 1.12.2.
- `src/Plugins/ProtocolSupport/Java/v1_13_to_1_20_1` — Java protocol support for 1.13 through 1.20.1.
- `src/Plugins/ProtocolSupport/Java/v1_20_2_to_latest` — Java protocol support for 1.20.2 through latest.
- `src/Plugins/Watchdog` — HTTP health monitoring plugin.
- `src/Terminal` — terminal UI for the proxy console.
- `tests/Void.IntegrationTests` — integration test suite.
- `tests/Void.IntegrationTests.Generators` — source generators for integration tests.
- `tests/Void.UnitTests` — unit test suite.
- `pdk/YourPlugin` — plugin development kit example project.

## Coding conventions

Follow the existing C# style rules:

- Indentation uses four spaces, with tab width set to four.
- Private fields prefixed with an underscore and method parameters use camelCase.
- Braces sit on their own lines when used, but never use braces for single-line statement bodies.
- Method signatures remain on a single line.
- Identifier names are fully descriptive and never abbreviated.
- Async methods carry an `Async` suffix.
- Pattern matching is preferred over explicit casts.
- `nameof` is used for parameter checks, logging, and exceptions.
- Never use the null-forgiving `!` operator to silence possible null reference warnings.

## Development

- Commit only hunks with actual code changes. Preserve existing whitespace and never reformat untouched lines.
- Run `dotnet test --project tests/Void.UnitTests/` to ensure unit tests pass whenever C# source files (`*.cs`) are modified. Skip this step if no C# files change.
- Do not run integration tests (`tests/Void.IntegrationTests/`) unless explicitly asked to do so.
- Be patient with long-running tests and avoid aborting them early; some may take several minutes to complete.
- After changing source code, run `dotnet build` from the repository root.
- Never add .NET-related JSON files such as `global.json` to the repository.
- `dotnet test` needs the .NET 11 SDK with `DOTNET_TEST_RUNNER=Microsoft.Testing.Platform`. If it is missing, install it with `dotnet-install.sh` (11.0 SDK plus 10.0 runtimes) and persist the variable in `/etc/sandbox-persistent.sh`.
- Conventional Commits are required for commit messages.
- Commit messages must include a scope after the type, e.g., `docs(readme): ...`.
- Use only the following Conventional Commit types:
  - `feat` — Features
  - `fix` — Bug Fixes
  - `perf` — Performance Improvements
  - `deps` — Dependencies
  - `revert` — Reverts
  - `docs` — Documentation
  - `style` — Styles
  - `chore` — Miscellaneous Chores
  - `refactor` — Code Refactoring
  - `test` — Tests
  - `build` — Build System
  - `ci` — Continuous Integration
- Commit bodies are required and must include a brief note about any observable behavior change.
- Use the `fix` or `feat` type only when your changes modify the proxy code in `./src`. For documentation, CI, or other unrelated updates, choose a more appropriate type such as `docs` or `chore`.
- Append a [gitmoji](https://gitmoji.dev/specification) after the commit scope, e.g., `feat(api): ✨ added new endpoint`.
- Use past tense for commit subjects when the change represents a completed action, e.g., `fixed bug` rather than `fix bug`.
- Pull request titles should follow the same Conventional Commits format.
- Pull request descriptions must use the following template:

  ```
  ## Summary
  One-sentence problem and outcome.

  ## Rationale
  Why this is needed; alternatives considered briefly.

  ## Changes
  Concise, high-signal description of what changed.

  ## Verification
  How it was tested; include commands or steps.

  ## Performance
  Before/after numbers if relevant; memory/alloc notes.

  ## Risks & Rollback
  Known risks, how to revert safely.

  ## Breaking/Migration
  Required actions for users, if any.

  ## Links
  Issues, discussions, specs.
  ```
- Never modify `CHANGELOG.md`; it is generated automatically by `release-please` from commit history.

## Documentation

- When adding links to documentation, make the link text bold. For example: `[**link text**](https://example.com)`.
- Never include inline code or backticks inside a link caption; keep code formatting outside the link text.

## Protocol guidance

When adding or modifying packet handling:

- Identify the Minecraft version whose packets require updates.
- Look up the release date of the next minor update (for example, if targeting 1.21, note that 1.21.1 released on August 8, 2024).
- Open the [Java Edition protocol/Packets](https://minecraft.wiki/w/Java_Edition_protocol/Packets?action=history&limit=500) page and view its revision history.
- Select the last revision made before that next update's release date (for 1.21, use the August 5, 2024 revision).
- Use that snapshot as your reference when altering the target version's protocol.
- For sub-versions, repeat this process using the next sub-version's release date and its preceding revision.
- Check the `src/Plugins/ProtocolSupport` directory to see how existing packets are implemented and update them as necessary.
- Base any protocol modifications on verified information from that official reference; avoid speculation.
- When communicating protocol-related changes, quote the relevant text and provide links to the exact protocol revision so reviewers can validate your reasoning.
