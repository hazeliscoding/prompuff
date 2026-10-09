# Contributing to Prompuff

Thanks for helping Puff out. Prompuff is a small, local-first app, and it stays that way on purpose, so this page is short.

## Before you start

- [README.md](README.md) says what Prompuff is and how it's built and packaged.
- [ROADMAP.md](ROADMAP.md) holds the decisions already made, the milestones and what's not planned. Check it before proposing a feature. Decisions stand unless the owner reopens them.
- [docs/user-guide.md](docs/user-guide.md) is how Prompuff works for the people using it. When you change behavior, change the guide with it.
- For anything bigger than a fix, open an issue first, so nobody builds something that's already decided against.

## Build, run and test

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). `global.json` asks for 10.0.100 or a later feature band. On Debian or Ubuntu, the app also needs `sudo apt install libx11-6 libice6 libsm6 libfontconfig1`.

```bash
dotnet build
dotnet test
```

Run the app against a scratch folder, so development never touches your real library:

```bash
PROMPUFF_DATA_DIR=/tmp/prompuff-dev dotnet run --project src/Prompuff.App
```

In PowerShell, set `$env:PROMPUFF_DATA_DIR = "$env:TEMP\prompuff-dev"` first. The prompts in [`samples/`](samples) and the workflow in [`samples/workflows/`](samples/workflows) import from Settings › Import / export.

The UI tests drive the real main window headlessly. After a UI change, save a PNG of every state they visit and look at them:

```bash
PROMPUFF_SCREENSHOTS=./screenshots dotnet test tests/Prompuff.App.Tests
```

Check both Prompuff Dark and Light, and attach before and after screenshots to the pull request.

## Project layout

| Project | What it holds |
|---|---|
| `src/Prompuff.Domain` | Entities and pure rules. No dependencies. |
| `src/Prompuff.Application` | Interfaces and services: templates, versioning, library operations, diff. No Avalonia, no SQLite. |
| `src/Prompuff.Infrastructure` | SQLite repositories and migrations, Markdown import and export, paths, settings, logging, Velopack, diagnostics. No Avalonia. |
| `src/Prompuff.App` | Avalonia views, view models, controls, themes, and platform services in `Platform/`. |
| `src/Prompuff.Cli` | The `prompuff` command and its MCP server. No Avalonia. |
| `tests/*` | xUnit v3 tests for each project, plus headless UI tests in `Prompuff.App.Tests`. |

Dependencies point inward: App and Cli use Infrastructure, which uses Application, which uses Domain. Add an interface only where it protects a platform boundary or a swap point the roadmap names; otherwise use a concrete class. Package versions live in `Directory.Packages.props`.

## Hard rules

These come from [AGENTS.md](AGENTS.md), and reviews hold every change to them.

**Privacy**

- No telemetry, analytics, crash reporting, accounts or AI API calls.
- The only network code is the Velopack update check in `Prompuff.Infrastructure/Updates`. Don't add any other.
- Never log prompt titles, bodies, notes or variable values. Log prompt IDs and counts.
- Fonts and icons are bundled. Nothing loads from a CDN at runtime.

**Cross-platform**

- Windows and Linux are both first-class, and macOS is verified through the CI runners. Every feature works on all three. Say plainly in the pull request when something can only be checked by hand.
- Every file location comes from `IAppDataPathProvider`. Nothing is written next to the executable, inside the AppImage, or in the home folder outside Prompuff's data and config folders. The one exception is "Install command-line tool", which the user starts.
- Registry, Win32 and other OS-specific APIs live only in `Prompuff.App/Platform`, behind an `OperatingSystem.Is…()` check.
- The CLI keeps SQLite's native library beside it. `scripts/smoke-cli.sh` fails the build if one gets unpacked into `~/.net`.
- The Velopack pack ID stays `Prompuff.Desktop`, because Velopack deletes `%LocalAppData%\{packId}` on uninstall.

**Growth and the 1.0 promise**

- Prompuff grows by adding and extending, never by shifting: nothing people rely on is removed, renamed or given a new meaning.
- That holds for 1.0 libraries, Markdown exports and settings, the `prompuff` command's commands, options, exit codes and JSON, and the MCP server's tools, resources and prompts. Anything new in them is optional.
- Files in `tests/*/Fixtures` record what releases wrote and answered, pinned by `tests/pinned-fixtures.sha256`. Never change one; when a test against one fails, fix the code.

**Database**

- Never edit a migration that has shipped. Add a new one to `Migrations.cs`.
- Times are ISO 8601 UTC text, and IDs are GUID text.
- Content changes (title, description, body, notes) create versions. Metadata changes (favorite, rating, collection, tags) don't.

## UI and copy

- Colors come from the tokens in `src/Prompuff.App/Themes/Tokens.axaml` through `DynamicResource`, so every theme restyles them.
- Manrope for the interface, IBM Plex Mono for data, variables and prompt bodies, and Lucide icons through the `Icon` control.
- Avalonia 12 compiles bindings, so every template needs `x:DataType`.
- The voice is cute, concise and lightly playful, never chatty. Personality goes in empty states, confirmations and the About page; buttons and labels stay plain ("Save", "Copy", "Delete").
- Errors say what happened and that the library is safe, with the technical details behind "Show details". Never show a raw exception.

## Code style

Match the code around you: `.editorconfig` sets file-scoped namespaces, four-space indents and LF line endings. Comments explain why, not what. Keep changes small and add tests beside the code they cover.

## Commits and pull requests

- Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `build:`, `ci:`, `chore:`, with a scope when it helps, such as `feat(library): …`.
- Keep each commit atomic: one logical change.
- Run `dotnet build` and `dotnet test` before every commit that touches code.
- Commit messages read like a person wrote them: no `Co-Authored-By` trailers or "Generated with" lines for AI tools.

[CI](.github/workflows/ci.yml) builds and tests every push to `main` and every pull request on Windows, Linux and macOS, smoke-tests the `prompuff` command, and uploads the UI screenshots as an artifact.

## Where decisions live

`ROADMAP.md` is the plan. Work goes from the next unchecked item; it's ticked off when it lands, and new decisions go under a dated "Decisions" heading with the reason. There are no separate plan, spec or backlog documents.

## How releases are cut

The owner cuts releases. In short:

1. The version goes in `Directory.Build.props`.
2. A `v*` tag starts [the release workflow](.github/workflows/release.yml). A tag with a pre-release suffix, such as `v0.9.0-beta.1`, makes a beta on the `-beta` channels.
3. The workflow packs Windows, Linux x64 and ARM64, and both Macs with Velopack, smoke-tests the app and the `prompuff` command from each package, signs the Windows build when the signing variables exist, and opens a draft GitHub Release with `SHA256SUMS`.
4. The owner reads the draft and publishes it. Installed copies only see published releases.

Running the workflow by hand from the Actions tab builds the same packages without publishing anything. The README's Packaging and Releases sections and `release.yml` are the source of truth.
