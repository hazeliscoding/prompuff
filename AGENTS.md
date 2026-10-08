# AGENTS.md

These are the working rules for agents in this repo. Prompuff is a local-first desktop prompt vault (C#, .NET 10, Avalonia 12, SQLite, Velopack, MIT) for Windows x64 and Linux x64.

## Sources of truth

- `README.md`: what Prompuff is, how to build, run, test and package it, and the privacy promise.
- `ROADMAP.md`: decisions already made, the milestones, and what is out of scope. Check it before proposing features, and respect its decisions unless the owner reopens them.
- Work from the next unchecked item in `ROADMAP.md`. Tick items off as they land and record new decisions there under a dated heading. Don't create separate plan, spec or backlog documents.

## Privacy (hard rules)

- No telemetry, analytics, crash reporting, accounts or AI API calls.
- The only network code is the Velopack update check in `Prompuff.Infrastructure/Updates`. Don't add any other.
- Never log prompt titles, bodies, notes or variable values. Log prompt IDs and counts.
- Fonts and icons are bundled. Nothing loads from a CDN at runtime.

## Cross-platform (hard rules)

- Windows and Linux are both first-class. Every feature must work on both, and on macOS once it lands in v0.3.
- The owner has no Mac. Verify macOS through the CI runners (UI tests and the launch smoke test), and say plainly when something can only be checked by hand. Hands-on Linux checks run in WSLg.
- All file locations come from `IAppDataPathProvider`. Never write next to the executable, inside the AppImage, or anywhere in the home directory outside the Prompuff data and config folders. The one exception is "Install command-line tool", which the user starts: on Linux and macOS it links `~/.local/bin/prompuff` to a copy in the data folder, and on Windows it adds a user PATH entry.
- No registry, Win32 or other OS-specific API outside `Prompuff.App/Platform`, and there only behind an `OperatingSystem.Is…()` check.
- The CLI keeps SQLite's native library beside it instead of bundling it. A bundled native library gets unpacked into `~/.net`, and `scripts/smoke-cli.sh` fails the build if that happens.
- The Velopack pack ID stays `Prompuff.Desktop`. Velopack deletes `%LocalAppData%\{packId}` on uninstall, and the data folder is `%LocalAppData%\Prompuff`.

## Architecture

- `Prompuff.Domain`: entities and pure rules. No dependencies.
- `Prompuff.Application`: interfaces and services (templates, versioning, library operations, diff). No Avalonia, no SQLite.
- `Prompuff.Infrastructure`: SQLite repositories and migrations, Markdown import/export, paths, settings, logging, Velopack. No Avalonia.
- `Prompuff.App`: Avalonia views, view models, controls, themes, and platform services (clipboard, file pickers, launcher).
- Add an interface only where it protects a platform boundary or a swap point the roadmap names. Prefer a concrete class otherwise.

## Database

- Never edit a migration that has shipped. Add a new one to `Migrations.cs`; the runner applies them in order using `PRAGMA user_version`.
- Store times as ISO 8601 UTC text and IDs as GUID text.
- Content changes (title, description, body, notes) create versions. Metadata changes (favorite, rating, collection, tags) don't.

## UI and copy

- The look follows the Prompuff design on the Quorum design system: tokens live in `src/Prompuff.App/Themes/Tokens.axaml`, with Dark as the primary theme and a full Light theme. Use `DynamicResource` for theme colors.
- Manrope for interface text, IBM Plex Mono for data, variables and prompt bodies. Lucide icons through the `Icon` control.
- Voice: cute, concise, lightly playful, never chatty. Personality goes in empty states, confirmations and the About page. Buttons and labels stay plain ("Save", "Copy", "Delete").
- Errors say what happened and that the library is safe, with technical details behind "Show details". Never show a raw exception.

## Commands

- `dotnet build` and `dotnet test` from the repo root.
- `dotnet run --project src/Prompuff.App` runs the app. Set `PROMPUFF_DATA_DIR` to a scratch folder so development never touches the real library.
- `PROMPUFF_SCREENSHOTS=<folder> dotnet test tests/Prompuff.App.Tests` saves a PNG of every state the headless UI tests visit. Look at them after UI changes.
- `dotnet tool restore` installs the pinned Velopack CLI (`dotnet vpk …`). The README has the publish and pack commands; `.github/workflows/release.yml` is the source of truth.

## Avalonia notes

- Avalonia 12: compiled bindings are on, so every template needs `x:DataType`. Don't put `x:DataType` on the same element as the `DataContext` binding that switches to that type; wrap it instead.
- Clipboard goes through `IClipboardService` (Avalonia's `SetTextAsync` / `TryGetTextAsync`), never a platform API.
- The Windows title bar uses `PrompuffWindowDecorations` in `Themes/Controls.axaml`, which hides Avalonia's drawn title text. Keep `TitleBarInset` in sync with the caption buttons.

## Working style

- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`, `chore:`, `test:`, `ci:`, `build:`, `refactor:`). Keep each commit atomic, and use a scope when it helps (`feat(library): …`).
- **No AI attribution** in commits or PRs: no `Co-Authored-By` trailers, no "Generated with" lines, no session links.
- Run `dotnet build` and `dotnet test` before every commit that touches code.
- **Docs:** short and plain. Prefer editing `ROADMAP.md` over adding planning documents.
