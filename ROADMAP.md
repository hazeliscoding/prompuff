# Roadmap

Prompuff is a local-first desktop prompt vault (C#, .NET 10, Avalonia 12) for Windows and Linux. You save the prompts that worked, fill in their `{{variables}}`, copy the result, and keep the history of how each prompt evolved. This file tracks what gets built, in what order, and the decisions already made.

## Decisions (2026-10-06)

- **Stack:** .NET 10, Avalonia 12 with the Fluent theme as a base, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite with hand-written SQL (no ORM), Microsoft.Extensions.DependencyInjection and Logging, Velopack and xUnit v3. Package versions live in `Directory.Packages.props`.
- **Open source:** public repo under the MIT license. Velopack's GitHub Releases updater needs public releases to work without a token.
- **Layout follows the Prompuff design** (claude.ai/design, Quorum design system): a sidebar, then a main area that switches between the library (cards or list) and a prompt's detail view with Edit, Render and History tabs. The handoff's three-pane sketch is not used, because the editor and its side panel need the width.
- **Left out of the design for v0.1:** the Workflows view, "Works with" model chips, density modes, backups, moving the data folder, and JSON or plain-text export. They are listed under Later or Not planned.
- **Title bar:** on Windows the window extends into the native title bar and keeps the native caption buttons. Linux keeps its system decorations, because Avalonia's client-area extension has limited support there.
- **Data paths:** Windows uses `%LOCALAPPDATA%\Prompuff\`. Linux uses `$XDG_DATA_HOME/prompuff/` (or `~/.local/share/prompuff/`) for the database, logs, backups and exports, and `$XDG_CONFIG_HOME/prompuff/settings.json` (or `~/.config/prompuff/`) for settings. Nothing is written beside the executable.
- **Velopack pack ID is `Prompuff.Desktop`**, not `Prompuff`. Velopack installs to `%LocalAppData%\{packId}` and deletes that folder on uninstall, so it must never equal the data folder.
- **Versions:** every distinct saved state of title, description, body and notes is a version, and the current prompt is the latest one. A save that changes none of these four fields creates no version. Restoring an old version adds a new version with its content, so the previous state stays in the history. Each version stores a short generated note, such as "Edited body (+3 −1 lines)".
- **Metadata saves immediately:** favorite, rating, collection and tags are saved as soon as they change and never create versions. Content edits wait for Save, which also runs when you leave the prompt or close the app.
- **Schema additions beyond the handoff:** `Prompts.LastOpenedAt` so Recent can include opens, and `PromptVersions.Note` for the version note. Migrations run in order against `PRAGMA user_version`, and the database is copied to `backups/` before a migration touches an existing file.
- **Tags** are trimmed, lowercased, lose a leading `#`, and turn inner whitespace into `-`. `Angular`, `angular` and ` #angular` are one tag. Tags with no prompts are deleted.
- **Variables** are `{{ name }}` with letters, digits and underscores, starting with a letter or underscore. Names are case-sensitive. A missing or empty value leaves the original token in the output.
- **Search** is SQL `LIKE` over title, description, body, notes and tag names, behind `IPromptSearch` so FTS5 can replace it.
- **Markdown format** is YAML-style frontmatter followed by `# Prompt` and `# Notes` sections. The frontmatter reader and writer are hand-written, so there is no YAML dependency.
- **Quick save intent** maps onto existing fields: "Worked well" sets the rating to 4, "Template" adds the `template` tag, and "Idea" adds the `idea` tag.
- **Duplicate, not fork,** for v0.1. The copy starts its own history at v1. `ParentPromptId` and lineage come in v0.2.
- **Shortcuts** live in one table and use the platform's command modifier, so macOS can map them to Cmd later. The command palette opens with Ctrl+K (as in the design) and Ctrl+Shift+P.
- **Fonts and icons are bundled:** Manrope and IBM Plex Mono (OFL), and Lucide icons converted to geometry (ISC). Nothing loads from a CDN.
- **Logging** goes to daily files in `logs/` through a small built-in file logger, so no Serilog. Logs carry prompt IDs, never titles, bodies or notes.
- **Privacy:** no account, telemetry, analytics or AI calls. The only network request is Velopack's update check against GitHub Releases, which can be turned off in Settings.

## Decisions (2026-10-07)

- **Invariant globalization:** the app runs without ICU (`InvariantGlobalization`), so the AppImage doesn't depend on the host's ICU version. Dates use English month names, which matches the English UI.
- **Windows title bar:** Avalonia 12 draws the caption buttons when the window extends into the title bar. `PrompuffWindowDecorations` hides the drawn title text that would cover the brand. `TitleBarDecorations` would do this directly, but it isn't in 12.1.3.
- **Velopack CLI** is pinned in `dotnet-tools.json`, so local packing and CI use the same `vpk` as the NuGet package.
- **Releases start as drafts.** The tag workflow uploads everything to a draft, and installed copies only see published releases, so nothing reaches users until the owner publishes it.
- **Release files:** both platforms share one GitHub Release. Velopack's names don't collide (`*-full.nupkg` and `RELEASES` for Windows, `*-linux-full.nupkg` and `RELEASES-linux` for Linux), and the installers are renamed to `Prompuff-Setup.exe`, `Prompuff-win-x64-Portable.zip` and `Prompuff-linux-x64.AppImage`.
- **UI tests** drive the real main window with Avalonia Headless and Skia against a temporary data folder, on both CI runners. `PROMPUFF_SCREENSHOTS` saves a PNG of each state, and `PROMPUFF_DATA_DIR` keeps any run away from the real library.
- **Linux smoke test:** the release workflow starts the AppImage under Xvfb with temporary XDG folders, checks that it stays up and creates `prompuff.db` in the data folder, and uploads a screenshot.

## M0: Foundation

- [x] Solution with `Prompuff.App`, `.Domain`, `.Application`, `.Infrastructure` and a test project for each, plus headless UI tests.
- [x] Avalonia shell with MVVM, dependency injection and logging.
- [x] `IAppDataPathProvider` for Windows and Linux/XDG, with tests that don't depend on the host machine.
- [x] SQLite initialization and the migration runner.
- [x] CI that builds and tests on Windows and Linux for every push and pull request.

**Done when:** the app opens to an empty library on Windows, the database file appears in the platform data folder, and CI is green on both runners.

## M1: Library

- [x] Domain models: prompt, collection, tag, version.
- [x] Repositories and prompt CRUD: create, edit, delete with confirmation, duplicate, favorite and rating.
- [x] Collections: create, rename, delete (prompts become uncategorized), and an Uncategorized view.
- [x] Tags: implicit creation, normalization, remove from a prompt, filter by tag.
- [x] Search with All, Favorites, Recent, Collection and Tag filters.
- [x] Library UI from the design: sidebar, cards and list modes, sorting, empty states.
- [x] Quick save dialog that starts from the clipboard.

**Done when:** you can create, tag, file, favorite, search and delete prompts, close the app, and find them again after reopening.

## M2: Render and versions

- [x] `IPromptTemplateService`: extract and render `{{variables}}`.
- [x] Render tab: fill variables, live preview with filled and missing values marked, copy.
- [x] `IClipboardService` on Avalonia's clipboard.
- [x] Versioning on save, no versions for no-op saves.
- [x] History tab: version list, line diff and full text, restore, duplicate a version.

**Done when:** a prompt with variables renders and copies, and editing, saving and restoring builds a history you can read.

## M3: Portability and settings

- [x] Markdown export of one prompt, a collection and the whole library.
- [x] Markdown import of one or more files, with friendly errors for files it can't read.
- [x] Settings: Appearance (Dark, Light, System; Puff on or off), Storage (data folder, open folder), Import and export, Updates, Keyboard shortcuts, About.
- [x] Paths checked on Windows: a development run and the Velopack portable build both wrote to the data folder, and the update check ran.
- [x] Paths checked on Linux under Xvfb in CI: the AppImage created `prompuff.db` in `$XDG_DATA_HOME/prompuff` and its config folder in `$XDG_CONFIG_HOME/prompuff`.
- [ ] Paths checked on a physical Linux desktop.

**Done when:** a prompt survives an export, a delete and an import unchanged, and the settings persist across restarts.

## M4: Distribution

- [x] Velopack startup hook and `IUpdateService`.
- [x] Self-contained `win-x64` and `linux-x64` publishing.
- [x] `Prompuff-Setup.exe` from Velopack.
- [x] `Prompuff-linux-x64.AppImage` from Velopack.
- [x] Release workflow: a pushed `v*` tag builds both platforms and opens a draft GitHub Release with the Velopack update files. A manual run (2026-10-07) produced both artifacts.

**Done when:** a dry run of the release workflow produces both artifacts, and an installed build finds an update from GitHub Releases.

## M5: Polish and v0.1

- [x] Keyboard shortcuts and the command palette.
- [x] Empty states, confirmations and error messages in Prompuff's voice.
- [x] Tests for template rendering, versioning, tags, import/export and paths.
- [x] README: build, run, test, package, release, storage and privacy.
- [x] Automated smoke test on both CI runners: the headless UI tests save a prompt, render and copy it, restart, and find it again.
- [ ] Manual smoke test on a Windows install from `Prompuff-Setup.exe` and on a Linux desktop from the AppImage, including the real clipboard.
- [ ] Publish v0.1.0, then confirm an installed copy finds a later release through the update check.

**Done when:** a fresh clone builds from the README, and the core flow works on both Windows and Linux.

## Later

- **v0.2:** prompt lineage (`ParentPromptId`), automatic backups, bulk import and export, SQLite FTS5 search, better keyboard navigation, density modes, remembered variable values.
- **v0.3:** browser extension, quick capture from ChatGPT and Claude, a global hotkey, a system tray icon.
- **v0.4:** prompt workflows (the design's Workflows view), prompt relationships, recipes and chaining.
- **Beyond:** Git-backed vaults, an MCP server, a CLI, `prompt://` URLs, optional encrypted sync, model evaluation, macOS and Linux ARM64 builds, a Beta update channel.

## Not planned

- Accounts, authentication, teams or shared libraries.
- Cloud sync in v0.1, and any sync that isn't optional and encrypted.
- A prompt marketplace, AI prompt generation or built-in LLM execution.
- Telemetry, analytics or prompt performance tracking.
- Embedding or vector search.
- Electron, embedded Chromium or a web-app shell.
