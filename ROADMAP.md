# Roadmap

Prompuff is a local-first desktop prompt vault (C#, .NET 10, Avalonia 12) for Windows and Linux, with macOS coming before 1.0. You save the prompts that worked, fill in their `{{variables}}`, copy the result, and keep the history of how each prompt evolved. This file tracks what gets built, in what order, and the decisions already made.

## Decisions (2026-10-06)

- **Stack:** .NET 10, Avalonia 12 with the Fluent theme as a base, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite with hand-written SQL (no ORM), Microsoft.Extensions.DependencyInjection and Logging, Velopack and xUnit v3. Package versions live in `Directory.Packages.props`.
- **Open source:** public repo under the MIT license. Velopack's GitHub Releases updater needs public releases to work without a token.
- **Layout follows the Prompuff design** (claude.ai/design, Quorum design system): a sidebar, then a main area that switches between the library (cards or list) and a prompt's detail view with Edit, Render and History tabs. The handoff's three-pane sketch is not used, because the editor and its side panel need the width.
- **Left out of the design for v0.1:** the Workflows view, "Works with" model chips, density modes, backups, moving the data folder, and JSON or plain-text export. The road to 1.0 below schedules some of them; the rest are under Later or Not planned.
- **Title bar:** on Windows the window extends into the native title bar and keeps the native caption buttons. Linux keeps its system decorations, because Avalonia's client-area extension has limited support there.
- **Data paths:** Windows uses `%LOCALAPPDATA%\Prompuff\`. Linux uses `$XDG_DATA_HOME/prompuff/` (or `~/.local/share/prompuff/`) for the database, logs, backups and exports, and `$XDG_CONFIG_HOME/prompuff/settings.json` (or `~/.config/prompuff/`) for settings. Nothing is written beside the executable.
- **Velopack pack ID is `Prompuff.Desktop`**, not `Prompuff`. Velopack installs to `%LocalAppData%\{packId}` and deletes that folder on uninstall, so it must never equal the data folder.
- **Versions:** every distinct saved state of title, description, body and notes is a version, and the current prompt is the latest one. A save that changes none of these four fields creates no version. Restoring an old version adds a new version with its content, so the previous state stays in the history. Each version stores a short generated note, such as "Edited body (+3 −1 lines)".
- **Metadata saves immediately:** favorite, rating, collection and tags are saved as soon as they change and never create versions. Content edits wait for Save, which also runs when you leave the prompt or close the app.
- **Schema additions beyond the handoff:** `Prompts.LastOpenedAt` so Recent can include opens, and `PromptVersions.Note` for the version note. Migrations run in order against `PRAGMA user_version`, and the database is copied to `backups/` before a migration touches an existing file.
- **Tags** are trimmed, lowercased, lose a leading `#`, and turn inner whitespace into `-`. `Angular`, `angular` and ` #angular` are one tag. Tags with no prompts are deleted.
- **Variables** are `{{ name }}` with letters, digits and underscores, starting with a letter or underscore. Names are case-sensitive. A missing or empty value leaves the original token in the output.
- **Search** is SQL `LIKE` over title, description, body, notes and tag names, behind `IPromptSearch` so FTS5 can replace it. (Replaced by FTS5 in v0.2; see below.)
- **Markdown format** is YAML-style frontmatter followed by `# Prompt` and `# Notes` sections. The frontmatter reader and writer are hand-written, so there is no YAML dependency.
- **Quick save intent** maps onto existing fields: "Worked well" sets the rating to 4, "Template" adds the `template` tag, and "Idea" adds the `idea` tag.
- **Duplicate, not fork,** for v0.1. The copy starts its own history at v1. `ParentPromptId` and lineage come in v0.4.
- **Shortcuts** live in one table and use the platform's command modifier, so macOS can map them to Cmd later. The command palette opens with Ctrl+K (as in the design) and Ctrl+Shift+P.
- **Fonts and icons are bundled:** Manrope and IBM Plex Mono (OFL), and Lucide icons converted to geometry (ISC). Nothing loads from a CDN.
- **Logging** goes to daily files in `logs/` through a small built-in file logger, so no Serilog. Logs carry prompt IDs, never titles, bodies or notes.
- **Privacy:** no account, telemetry, analytics or AI calls. The only network request is Velopack's update check against GitHub Releases, which can be turned off in Settings.

## Decisions (2026-10-07)

- **Invariant globalization:** the app runs without ICU (`InvariantGlobalization`), so the AppImage doesn't depend on the host's ICU version. Dates use English month names, which matches the English UI.
- **Windows title bar:** Avalonia 12 draws the caption buttons when the window extends into the title bar. `PrompuffWindowDecorations` hides the drawn title text that would cover the brand, and the full-screen button, so the caption buttons fit `TitleBarInset`. `TitleBarDecorations` would do this directly, but it isn't in 12.1.3.
- **Velopack CLI** is pinned in `dotnet-tools.json`, so local packing and CI use the same `vpk` as the NuGet package.
- **Releases start as drafts.** The tag workflow uploads everything to a draft, and installed copies only see published releases, so nothing reaches users until the owner publishes it.
- **Release files:** both platforms share one GitHub Release. Velopack's names don't collide (`*-full.nupkg` and `RELEASES` for Windows, `*-linux-full.nupkg` and `RELEASES-linux` for Linux), and the installers are renamed to `Prompuff-Setup.exe`, `Prompuff-win-x64-Portable.zip` and `Prompuff-linux-x64.AppImage`.
- **UI tests** drive the real main window with Avalonia Headless and Skia against a temporary data folder, on both CI runners. `PROMPUFF_SCREENSHOTS` saves a PNG of each state, and `PROMPUFF_DATA_DIR` keeps any run away from the real library.
- **Linux smoke test:** the release workflow starts the AppImage under Xvfb with temporary XDG folders, checks that it stays up and creates `prompuff.db` in the data folder, and uploads a screenshot.
- **Update checks** run every time Prompuff opens while the setting is on, with no daily limit. A found update stays in the sidebar footer until it's installed, because a toast is easy to miss at startup.
- **Sharing is file-based.** Prompts move between machines and people as one `.zip` of the same Markdown files. An import always makes independent copies with their own history, skips prompts whose title and body match one already in the library, and keeps the favorites and ratings in the files. Settings exports use the same `.zip`, which replaced the folder export. There are no share links or shared libraries.
- **Backups** are made the first time Prompuff opens each local day, and checked again every hour while it runs. Only daily backups are pruned to 30; copies made before an update or a restore stay until you delete them. Each backup uses a rollback journal, so it's one `.db` file with no `-wal` or `-shm` beside it. A restore checks the file first, then copies it over the open library with SQLite's backup API and migrates it if it's older.
- **Recently deleted** is a `DeletedAt` time on the prompt. Deleting and restoring are metadata changes: no version, and `UpdatedAt` stays put. The sidebar shows Recently deleted only while it holds prompts. A deleted prompt can't be opened until it's restored, and prompts deleted more than 30 days ago are removed for good when Prompuff opens.
- **Search uses FTS5** with the `unicode61 remove_diacritics 2` tokenizer, so matching ignores case and accents. Each word is a quoted prefix phrase, and `bm25` ranks results with weights favoring the title, then tags, then description. Infix matches ("gular" finding "Angular") are gone, which is the trade for ranking and accents. The index is keyed by `PromptId`, not rowid, and triggers keep it current. Words with no letters or digits still match literally with `LIKE`.
- **Remembered values** live in a `RenderValues` table in the library, so backups include them and they go when a prompt is removed for good. Each change is saved as it's typed. Empty values aren't stored, values are never exported or logged, and Clear values forgets them for one prompt.

## Decisions: the road to 1.0 (2026-10-07)

- **1.0 platforms:** Windows x64, Linux x64 and ARM64, and macOS on Intel and Apple Silicon.
- **macOS is CI-verified only.** The owner has no Mac, so macOS builds are packaged, UI-tested and launch-tested on GitHub's macOS runners. Hands-on testing comes from users' issue reports, and the README says so plainly.
- **Hands-on Linux checks run in WSLg** (Ubuntu-24.04 under WSL), which also covers the XWayland path. AppImages there need `APPIMAGE_EXTRACT_AND_RUN=1`, because the distribution has no FUSE 2.
- **macOS comes early (v0.3)**, so the hotkey, tray, CLI and MCP work after it is built for all three platforms from the start.
- **1.0 installers are signed.** Windows uses Azure Trusted Signing (about $10 a month). macOS uses a Developer ID and notarization from CI with an App Store Connect API key (Apple Developer account, $99 a year). Signing switches on only when the secrets exist, so forks and dry runs still build unsigned. (Changed in v0.3: there's no Apple Developer account, so only Windows is signed; see the v0.3 decisions.)
- **Capture at 1.0** is a system-wide Quick save hotkey and a tray icon. Wayland doesn't let apps grab global keys, so Wayland users bind a desktop shortcut to `prompuff quick-save` instead. The browser extension comes after 1.0.
- **Integrations at 1.0:** a `prompuff` CLI and a local MCP server over stdio, so Claude Code and similar tools can search and render prompts. (2026-10-08: GitHub Copilot CLI and Codex are named clients too.) Both read the same SQLite library, and neither opens a network connection.
- **The 1.0 promise is format stability.** Every later version opens a 1.0 library, the Markdown format stays compatible, and every schema migration is tested from every released version.

## Decisions: v0.3 (2026-10-07)

- **macOS title bar:** the window extends into the title bar like it does on Windows. The native traffic-light buttons sit at the top left of Prompuff's header, which leaves room for them, and the CI screenshot checks the layout.
- **Bundle ID** is `io.github.hazeliscoding.prompuff`. macOS ties settings and permissions to it, so it doesn't change.
- **Windows signing moves up from v0.8.** The release workflow signs with Azure Artifact Signing (formerly Trusted Signing) through `vpk pack --azureTrustedSignFile`. GitHub Actions signs in to Azure with OIDC (`azure/login`), so no Azure secret is stored, and signing switches on only when the Azure variables exist. The app registration `prompuff-release-signing` trusts only jobs in the repo's `release` environment, which only `v*` tags can use, and it can only sign with the `EZMoneyCert` profile.
- **No Apple Developer account,** now or planned. macOS builds are ad-hoc signed and never notarized, through 1.0. Gatekeeper blocks them on first launch, so the README explains Open Anyway, and Mac signing leaves the roadmap.

## Decisions (2026-10-08)

- **MCP clients:** besides Claude Code and Claude Desktop, GitHub Copilot CLI and Codex are supported clients. The stdio server is the same for all of them; each client gets its own ready-to-paste setup, since each reads a different config (`claude mcp add`, Claude Desktop's JSON, Copilot CLI's `mcp-config.json`, Codex's `config.toml`). Check each format against the client's docs when building v0.7.
- **GitHub's OIDC subject includes immutable IDs.** The signing app's federated credential trusts `repo:hazeliscoding@23608664/prompuff@1408214273:environment:release`. The plain `repo:hazeliscoding/prompuff:...` form doesn't match, and the IDs mean a renamed or transferred repo can't inherit the trust.

## v0.1: MVP (tagged 2026-10-07)

### M0: Foundation

- [x] Solution with `Prompuff.App`, `.Domain`, `.Application`, `.Infrastructure` and a test project for each, plus headless UI tests.
- [x] Avalonia shell with MVVM, dependency injection and logging.
- [x] `IAppDataPathProvider` for Windows and Linux/XDG, with tests that don't depend on the host machine.
- [x] SQLite initialization and the migration runner.
- [x] CI that builds and tests on Windows and Linux for every push and pull request.

**Done when:** the app opens to an empty library on Windows, the database file appears in the platform data folder, and CI is green on both runners.

### M1: Library

- [x] Domain models: prompt, collection, tag, version.
- [x] Repositories and prompt CRUD: create, edit, delete with confirmation, duplicate, favorite and rating.
- [x] Collections: create, rename, delete (prompts become uncategorized), and an Uncategorized view.
- [x] Tags: implicit creation, normalization, remove from a prompt, filter by tag.
- [x] Search with All, Favorites, Recent, Collection and Tag filters.
- [x] Library UI from the design: sidebar, cards and list modes, sorting, empty states.
- [x] Quick save dialog that starts from the clipboard.

**Done when:** you can create, tag, file, favorite, search and delete prompts, close the app, and find them again after reopening.

### M2: Render and versions

- [x] `IPromptTemplateService`: extract and render `{{variables}}`.
- [x] Render tab: fill variables, live preview with filled and missing values marked, copy.
- [x] `IClipboardService` on Avalonia's clipboard.
- [x] Versioning on save, no versions for no-op saves.
- [x] History tab: version list, line diff and full text, restore, duplicate a version.

**Done when:** a prompt with variables renders and copies, and editing, saving and restoring builds a history you can read.

### M3: Portability and settings

- [x] Markdown export of one prompt, a collection and the whole library.
- [x] Markdown import of one or more files, with friendly errors for files it can't read.
- [x] Settings: Appearance (Dark, Light, System; Puff on or off), Storage (data folder, open folder), Import and export, Updates, Keyboard shortcuts, About.
- [x] Paths checked on Windows: a development run and the Velopack portable build both wrote to the data folder, and the update check ran.
- [x] Paths checked on Linux under Xvfb in CI: the AppImage created `prompuff.db` in `$XDG_DATA_HOME/prompuff` and its config folder in `$XDG_CONFIG_HOME/prompuff`.

**Done when:** a prompt survives an export, a delete and an import unchanged, and the settings persist across restarts.

### M4: Distribution

- [x] Velopack startup hook and `IUpdateService`.
- [x] Self-contained `win-x64` and `linux-x64` publishing.
- [x] `Prompuff-Setup.exe` from Velopack.
- [x] `Prompuff-linux-x64.AppImage` from Velopack.
- [x] Release workflow: a pushed `v*` tag builds both platforms and opens a draft GitHub Release with the Velopack update files. The v0.1.0 tag produced all nine assets.

**Done when:** a dry run of the release workflow produces both artifacts. Confirming that an installed copy finds an update moved to v0.2, because it needs a second release.

### M5: Polish

- [x] Keyboard shortcuts and the command palette.
- [x] Empty states, confirmations and error messages in Prompuff's voice.
- [x] Tests for template rendering, versioning, tags, import/export and paths.
- [x] README: build, run, test, package, release, storage and privacy.
- [x] Automated smoke test on both CI runners: the headless UI tests save a prompt, render and copy it, restart, and find it again.

**Done when:** a fresh clone builds from the README, and the core flow works on both Windows and Linux. The hands-on install checks moved to v0.2.

## v0.2: Safe, searchable and shareable (tagged 2026-10-07)

Trust Prompuff with more than a few prompts: nothing is lost by accident, search scales, and prompts move between machines and people.

- [x] Publish v0.1.0 (2026-10-07).
- [x] Publish v0.1.1 (2026-10-07): hides the full-screen caption button that covered Quick save on Windows.
- [x] Publish v0.1.2 (2026-10-07): checks for updates every time Prompuff opens and keeps a found update in the sidebar.
- [x] Install v0.1.0 from `Prompuff-Setup.exe` on Windows and from the AppImage in WSLg, and run the core flow by hand, real clipboard included. Windows is partly done: an install of 0.1.0 updated itself to 0.1.2 (2026-10-07).
- [x] Automatic backups: a daily copy of the library in `backups/`, keeping the last 30. Restore one from Settings › Storage, after copying the current library aside.
- [x] Recently deleted: deleting a prompt keeps it for 30 days with Restore and Empty. This is the first real schema migration (`DeletedAt`), with a test that upgrades a v0.1 database.
- [x] SQLite FTS5 search behind `IPromptSearch`: ranked results, prefix matches, and case- and accent-insensitive matching for non-ASCII text, which `LIKE` can't do.
- [x] Remembered variable values per prompt, stored locally, with a Clear values button.
- [x] Share: an Export button in the library header saves what the library shows (all, a collection, a tag, Favorites, Recent or a search) as one `.zip` of Markdown files. Settings › Import and export saves the whole library the same way.
- [x] Import a `.zip` as well as `.md` files. Entries are read in memory with the same 5 MB limit per prompt and never unpacked to disk. Prompts whose title and body match one in the library are skipped, and the result says how many.
- [x] Publish v0.2.0 (2026-10-07).
- [x] Confirm the installs from the first item update to 0.2.0 with the library intact, on Windows and in WSLg.

**Done when:** an installed 0.1 updates to 0.2 on Windows and Linux with every prompt and version intact, a deleted prompt comes back, searching "cafe" finds "Café", and a `.zip` exported on one machine imports on another with duplicates skipped.

## v0.3: macOS and Linux ARM64 (tagged 2026-10-08)

Ship the remaining platforms early, so every later feature is built for all of them.

- [x] macOS in the CI matrix: build plus the headless UI tests on a macOS runner.
- [x] Release jobs for `osx-arm64` and `osx-x64`, one Velopack channel each, producing a `.pkg` and a zipped `.app` with an ad-hoc signature, which Apple Silicon needs to run them.
- [x] macOS conventions: Cmd shortcuts (already in the shortcut table), a native app menu with About, Settings and Quit, and a title bar that keeps the traffic-light buttons.
- [x] macOS smoke test on the runner: launch the app, take a screenshot, and check that `prompuff.db` lands in `~/Library/Application Support/Prompuff`.
- [x] `linux-arm64` AppImage, built and smoke-tested under Xvfb on GitHub's ARM runner.
- [ ] Windows signing with Azure Artifact Signing in the release workflow, checked on the runner with `signtool verify /pa`.
- [ ] Signing switches on only when the Azure variables exist; forks and manual runs still build unsigned.
- [x] README: list macOS as CI-verified, and explain System Settings › Privacy & Security › Open Anyway, since the builds aren't notarized. macOS 15 removed the right-click › Open shortcut.

**Done when:** one tag produces Windows, Linux x64, Linux ARM64 and both macOS packages, each one launches and creates its library on a CI runner, and `Prompuff-Setup.exe` is signed and installs without a SmartScreen warning.

## v0.4: Keyboard-first and lineage

Make daily use fast, and keep track of where prompts came from.

- [ ] Library keyboard navigation: arrow keys move through cards and rows, Enter opens, Delete deletes after confirming, and typing jumps to a title.
- [ ] Density modes from the design (Cozy, Compact, Dense).
- [ ] Prompt lineage: Duplicate records `ParentPromptId`, and the detail view shows "Duplicated from" and a list of copies.
- [ ] Multi-select in the library to tag, move, export or delete several prompts.
- [ ] Folder import that walks subfolders, skips `.obsidian` and other dot folders, and reports skipped files.

**Done when:** you can find, open, edit, render and copy a prompt without the mouse, and a duplicate links back to its parent.

## v0.5: Capture anywhere

Stash a good prompt from any app in a few seconds.

- [ ] Single instance: launching Prompuff again, or running `Prompuff --quick-save`, hands off to the running copy.
- [ ] Tray icon with Quick save, Open and Quit, and an option to keep running in the tray when the window closes.
- [ ] A configurable system-wide Quick save hotkey behind `IGlobalHotkeyService`, implemented for Windows, X11 and macOS.
- [ ] Wayland: Settings explains how to bind a desktop shortcut to `prompuff quick-save`.

**Done when:** you can copy text in a browser, press the hotkey, and find the prompt stashed in under five seconds on Windows, on Linux under X11, and on macOS (CI-verified).

## v0.6: Workflows

The design's Workflows view: prompts that run in order, with a person copying between steps. Prompuff never runs a model.

- [ ] A workflow is a named, ordered list of prompts, with a note per step on what it hands to the next.
- [ ] Shared variables: a variable that appears in several steps is filled once.
- [ ] Render and copy step by step, with progress through the steps.
- [ ] Export a workflow as one Markdown document, and import it back.

**Done when:** the design's "Angular upgrade, start to finish" workflow can be built, filled once and copied step by step.

## v0.7: CLI and MCP server

Let scripts and coding agents use the vault without opening the app.

- [ ] `prompuff` CLI: `search`, `list`, `get`, `render` with `--var name=value`, and `quick-save` from stdin.
- [ ] Ship the CLI in every package, and add an "Install command-line tool" button to Settings that puts it on the user's PATH (`~/.local/bin` on Linux and macOS, a user PATH entry on Windows).
- [ ] `prompuff mcp`: a read-only MCP server over stdio with search, get and render tools, and prompts as resources.
- [ ] "Copy MCP config" in Settings, with a ready-to-paste setup for each client: Claude Code, Claude Desktop, GitHub Copilot CLI and Codex. The README shows the same setups.
- [ ] The app picks up changes the CLI makes without a restart, using SQLite's `data_version`.

**Done when:** Claude Code, GitHub Copilot CLI and Codex can each search the vault and render a prompt with variables through MCP, and `prompuff render "Angular Upgrade Planner" --var repo_name=acme` prints the result on all three platforms.

## v0.8: Beta channel

- [ ] A `SHA256SUMS` file with every release.
- [ ] A Beta channel: `-beta` tags publish pre-releases to `*-beta` Velopack channels, and the Beta option in Settings › Updates works.

**Done when:** a beta install updates from the beta channel, and every release carries a `SHA256SUMS` file.

## v0.9: Release candidate

- [ ] The library and Markdown formats are documented in `docs/`, with a fixture database from every released schema version and a test that opens each one.
- [ ] Accessibility: every icon-only button has an accessible name, focus is visible and in order, and a CI script checks WCAG AA contrast for the theme tokens.
- [ ] Performance: 10,000 generated prompts load, scroll and search without lag, with search under 100 ms.
- [ ] Settings › About › "Copy diagnostic info" gathers versions, paths and recent log lines for bug reports, still with no telemetry.
- [ ] A user guide and `CONTRIBUTING.md`.
- [ ] Two beta releases in a row with no data-loss or crash reports.

**Done when:** the release candidate updates cleanly from 0.8 on every platform, and no open issue is labeled data loss or crash.

## v1.0

- [ ] Tag 1.0 from the final release candidate, signed on Windows.
- [ ] Update from 0.9 to 1.0 checked by hand on Windows and in WSLg, and on macOS CI runners.
- [ ] README and the About page state the 1.0 promise: later versions open 1.0 libraries.

**Done when:** 1.0 is published for Windows, Linux x64 and ARM64, and macOS, and every earlier 0.x install updates to it with its library intact.

## Later

- A browser extension for capturing from ChatGPT and Claude through native messaging.
- `prompuff://` links to open, copy or render a prompt.
- A Markdown mirror folder: a live export you can keep in Git or Syncthing.
- Optional end-to-end encrypted sync.
- MCP write tools, such as saving a prompt from an agent, behind a setting.
- Prompt relationships beyond lineage, and recipe templates.
- Model evaluation notes.
- Flatpak and `.deb` packages, and translations.

## Not planned

- Accounts, authentication, teams or shared libraries.
- Any sync that isn't optional and end-to-end encrypted.
- A prompt marketplace, AI prompt generation or built-in LLM execution.
- Telemetry, analytics or prompt performance tracking.
- Embedding or vector search.
- Electron, embedded Chromium or a web-app shell.
