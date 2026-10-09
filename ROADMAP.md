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
- **SmartScreen reputation isn't a release gate.** Signed builds name the publisher right away, but the "unrecognized app" warning only fades as people download and run them, and no certificate skips that since 2024. It gets a re-check before 1.0.

## Decisions: v0.4 (2026-10-08)

- **Library keys:** arrows, Home and End move a cursor that focus follows, and the cursor stays on its prompt across refreshes. After a delete it moves to the card that took the deleted one's place. Up and Down move a whole row of cards. Type-ahead matches the start of a title, resets after a second, and cycles when the same letter is typed again.
- **Esc goes back** from a prompt or Settings to the library, but only when nothing else used the key, so closing a dropdown or a dialog never leaves the page.
- **Density** is a setting in Settings › Appearance and changes only the library. Cozy is the old look. Compact trims padding and shows one line of description. Dense drops descriptions and card footers but keeps four columns at the default width, because five cut titles short.
- **Lineage** is `Prompts.ParentPromptId` (migration 5), set by Duplicate and by duplicating an old version. It's a foreign key with `ON DELETE SET NULL`: a parent in Recently deleted still shows as "Duplicated from" but must be restored to open, and removing it for good leaves the copy as an independent prompt. Copies in Recently deleted aren't listed. Lineage doesn't travel in Markdown, because imports are always independent copies.
- **Multi-select** is a mode: Select in the header, a Ctrl or Cmd click, a Shift click, Shift with the arrows, or Ctrl+A turns it on, and while it's on a plain click picks a card instead of opening it. The selection drops prompts that leave the screen, so a bulk action never touches one you can't see, and changing the view ends it. Tag and Export keep the selection; Move, Delete and Restore end it. Each bulk change runs prompt by prompt, and if one fails the dialog says how many went through.
- **Folder import** walks subfolders up to 32 levels deep and doesn't follow links, which could loop. Folders and files whose names start with a dot are passed over silently, and other files that aren't Markdown are listed in the result. Folder names don't become collections; the frontmatter decides, as with any import.
- **Opening a prompt** puts the cursor at the top of its body, so typing edits it right away.
- **Dialogs take focus** when they open, so Enter confirms the dialog instead of pressing the button behind it, and focus returns where it was when they close.

## Decisions: v0.5 (2026-10-08)

- **One copy per library.** The first copy holds an exclusive lock on `prompuff.lock` in the data folder (a sharing lock on Windows, `flock` elsewhere), which the operating system releases if it crashes. Later launches send `activate` or `quick-save` to it over a local pipe (a named pipe on Windows, a Unix socket elsewhere) that only the same user can open, then exit. The pipe name is a hash of the data folder, so a development run with `PROMPUFF_DATA_DIR` never talks to the real one. This is local IPC, not network code.
- **`--quick-save` and `quick-save`** both open Quick save, so a desktop shortcut can run the app executable or AppImage with either.
- **Tray:** always shown while Prompuff runs, with Quick save, Open and Quit; clicking it opens the window. Keeping Prompuff in the tray when the window closes is off by default. Quit, ⌘Q and shutting down always quit. Linux shows the icon through StatusNotifierItem, so desktops without a tray host (stock GNOME) don't show it; launching Prompuff again brings the window back. The macOS menu bar icon is a template image drawn at startup from Puff's outline.
- **Hotkey:** Ctrl+Alt+P by default (⌃⌥P on macOS, where ⌥⌘P belongs to Finder), on unless turned off. A hotkey needs Ctrl, Alt or Meta plus a letter, digit, F1–F12 or Space, which every backend can register. Windows uses `RegisterHotKey` with no window on a dedicated message-loop thread; X11 uses `XGrabKey` on its own display connection, repeated for Caps Lock and Num Lock, with an error handler chained in front of Avalonia's so a taken combination reports BadAccess instead of exiting; macOS uses Carbon's `RegisterEventHotKey`, which needs no Accessibility permission. Settings records a new combination from the keyboard and says when another app owns one.
- **After a hotkey Quick save**, the window goes back to the tray or the taskbar if it was there, so you land back in the app you came from.
- **Wayland** can't give an app a global hotkey, so Settings shows the desktop-shortcut steps for GNOME and KDE and the exact command (the AppImage path from `APPIMAGE`, plus `--quick-save`). Once v0.7 puts `prompuff` on the PATH, the command can become `prompuff quick-save`.
- **Verification:** the Windows hotkey was checked by hand on Windows 11. X11 is tested in CI under Xvfb (grab, BadAccess for a second client, a press through XTest), and the release smoke test copies text, presses the hotkey and Enter, and checks a prompt was stashed. macOS is checked on the runner by the app logging that Carbon registered the hotkey; pressing it there needs Accessibility rights the runner doesn't have.

## Decisions: v0.6 (2026-10-08)

- **Steps point at prompts** in the library rather than copying them, so editing a prompt updates every workflow that uses it. A prompt can appear in more than one step. Removing a prompt for good removes its steps (migration 6); a prompt in Recently deleted keeps its step, marked, until then.
- **Saving:** adding, moving and removing steps save at once; the name, description and hand-off notes save a moment after typing stops, and when you leave the page. Workflows have no versions and no Recently deleted. Deleting one asks first and leaves its prompts in the library.
- **Shared variables** are every variable across the steps, listed once in order of first appearance with the steps that use it. Their values are remembered per workflow in `WorkflowValues`, like `RenderValues` for prompts: never exported or logged. The inputs take several lines, because a step's answer is often pasted into the next step's variable, and the hand-off note can say which.
- **Run:** Copy renders the current step with the shared values, marks it copied and moves to the next; Ctrl+Enter does the same. The copied marks last for the visit; Start over clears them.
- **Workflows in library zips (v0.6.1):** Export everything adds each workflow under `workflows/`; Export collection and the library's Export button stay prompts only, because a workflow can span collections. Imports read every prompt in a zip, folder or file list before any workflow, so steps find the prompts from the same export with their tags, notes and collection.
- **Markdown format:** frontmatter with `type: workflow`, `title` and `description`, then `## Step N: Title` per step, the prompt in a fenced `prompt` block longer than any backtick run inside it, and the note as a `> **Hands off:**` quote. Only each prompt's title and body travel. Importing one reuses the library's prompt with the same title and body, or creates it with just those, and a workflow with the same name and steps is skipped. The regular Import picks workflow documents out by their frontmatter.

## Decisions: v0.7 (2026-10-08)

- **The CLI is its own program.** `Prompuff.Cli` is a small console app without Avalonia, published as one self-contained file and shipped inside every package beside the app. Prompuff.exe is a GUI program on Windows, so it can't write to a terminal, and a `prompuff.exe` can't sit beside it in the same folder because Windows ignores case. On Linux the app lives inside the AppImage, which a terminal can't reach while Prompuff is closed.
- **Installing it is the user's choice.** "Install command-line tool" in Settings adds the CLI's folder inside the install to the user PATH on Windows. On Linux and macOS it copies the CLI to `~/.local/bin/prompuff`, and Prompuff refreshes that copy whenever it starts as a newer version. This is the one place Prompuff writes outside its data and config folders, and only after the user asks.
- **MCP uses the official SDK's Core package** (`ModelContextProtocol.Core`) with its low-level handlers. Library prompts are rows that change, not compile-time types, so the attribute-based hosting package doesn't fit, and Core keeps the dependency small.
- **Favorites become MCP prompts.** Clients show them as slash commands, such as `/mcp__prompuff__…` in Claude Code or the + menu in Claude Desktop, with the prompt's `{{variables}}` as arguments. The search, get and render tools reach the whole library, and every prompt is also a resource.
- **MCP is off until turned on.** Settings › Integrations has "Let AI tools read my library (MCP)", off by default. "Copy MCP config" offers to turn it on. While it's off, `prompuff mcp` starts, says it's turned off, and serves nothing, so a config left in some client can't read the library. The CLI needs no switch, because the user runs it.
- **Read-only MCP; the CLI can save.** MCP never changes the library (write tools stay under Later, behind a setting). The CLI's `quick-save` adds a prompt, as Quick save does.
- **CLI conventions:** plain text by default and `--json` for scripts. A prompt can be named by ID or by title; a title that matches several prompts lists them and exits with code 2. `render` leaves unfilled `{{tokens}}` in place, as the app does, and lists them on stderr. Exit codes are 0 for success, 1 for errors and 2 for not found or ambiguous.
- **Same library, same rules.** The CLI and the MCP server open the library through `IAppDataPathProvider` (so `PROMPUFF_DATA_DIR` works), migrate it with a backup like the app, refuse a library from a newer version, and never log titles, bodies or values. The app checks `PRAGMA data_version` every two seconds and refreshes when another process has changed the library.
- **Found while building (2026-10-08):**
  - The CLI is one trimmed file with SQLite's native library beside it, not inside it. .NET unpacks bundled native libraries into `~/.net` on Linux and macOS, outside Prompuff's folders.
  - So on Linux and macOS, Install copies the `cli` folder into Prompuff's data folder and links `~/.local/bin/prompuff` to it, instead of copying one file into `~/.local/bin`. On macOS the copy drops the download's quarantine flag, or Terminal refuses to run it. That step can only be checked by hand on a Mac.
  - Uninstalling on Windows takes the PATH entry back out, through Velopack's uninstall hook.
  - The app ignores its own writes when it watches `data_version`: whenever it announces a change itself, it takes the current version as the new baseline.
  - Client setups: `claude mcp add`, `codex mcp add` and `copilot mcp add` each take `-- <command> mcp`, and Claude Desktop takes a JSON `mcpServers` entry. Claude Code and Codex were checked by hand on Windows: each searched a library and rendered a prompt with variables through MCP. Copilot CLI follows GitHub's docs, and the owner checked it by hand after the release.

## Decisions: v0.8 and 1.0 (2026-10-08)

- **Themes join v0.8.** They land before v0.9, so v0.9's contrast check covers every theme, not only Prompuff Dark and Light.
- **SmartScreen waits until after 1.0.** The signing certificate is new, and reputation only builds as people download signed builds, so the check moves from v0.9 to Later.
- **Themes, as built:**
  - Each theme is a palette in `ThemePalette.cs`: six backgrounds, three borders, four text colors, the accent and eight tones. `ThemeBuilder` derives the subtle fills, borders, shadows and control colors the same way for every theme, and registers each one as an Avalonia theme variant that inherits from Dark or Light. Prompuff Dark and Light stay in `Tokens.axaml`.
  - Fluent only takes palettes for Dark and Light, so each theme's dictionary also carries the `System*Color` values Fluent's stock controls read.
  - Picking a theme while the other mode shows switches to its mode, so the choice is visible at once. System keeps following the OS between the chosen dark and light theme.
  - A test checks every palette's contrast: Text1, Text2, the accent text and the tone texts reach 4.5:1 on the backgrounds they sit on, white or dark labels on the accent reach 4.5:1, and Text3 reaches 3:1 for now. It caught Prompuff Light's primary buttons at 3.74:1, so that accent moved one step darker, to teal-700 (`#0F766E`).
- **Beta channel, as built:** each platform's Velopack channel is set explicitly (`win`, `linux`, `linux-arm64`, `osx-arm64`, `osx-x64`, plus `-beta`), so an install that came from a beta follows the setting. Beta reads the beta and the stable channel and takes the newer version, so a release only ever packs to one channel. Velopack never goes back a version, so switching to Stable keeps a beta until the next stable release. A tag with a pre-release suffix packs to the `-beta` channels, downloads the previous beta for deltas, and becomes a GitHub pre-release; `releases/latest` stays on the last stable release.
- **Checked by hand (2026-10-08):** the Windows portable build of 0.8.0-beta.1, set to Beta, found 0.8.0-beta.2, downloaded the 233 KB delta, restarted and came back as beta.2.
- **Settings keep their defaults (fixed in 0.8.0):** since 0.7.0's source-generated settings reader, a key missing from `settings.json` came back as false or empty instead of its default, because the generator only keeps initializers for settable properties. `AppSettings` properties are settable again. No released setting was affected, since Prompuff writes every key, but each new setting would have been.
- **Fixed in 0.8.0:** a failed workflow import logged its reason, which can quote a step's title, a prompt title. The log now only says a workflow was skipped; the import result still names the step.
- **1.0 launches with a video and a landing page.** The video is made with the `/brag` skill. The landing page is a static site on GitHub Pages and keeps the app's promise: no analytics or trackers, and nothing loaded from other sites.

## Decisions: v0.9 (2026-10-08)

- **Format docs:** `docs/library-format.md` covers the SQLite library (where it lives, its files, tables, FTS5, migrations, what makes a version, and the 1.0 promise). `docs/markdown-format.md` covers prompt and workflow Markdown and the `.zip` layout.
- **Released schemas,** read from each tag's `Migrations.cs`: 1 (v0.1.x), 4 (v0.2.0–v0.3.0), 5 (v0.4.0–v0.5.0) and 6 (v0.6.0 on). Schemas 2 and 3 never shipped on their own.
- **Fixtures:** `tests/Prompuff.Infrastructure.Tests/Fixtures/schema-N.db`, one per released schema, made by migrating up to N with today's migrations and filling it with raw SQL in that schema, with fixed IDs and times and a rollback journal. The normal test run only reads them; `PROMPUFF_WRITE_FIXTURES=7 dotnet test tests/Prompuff.Infrastructure.Tests --filter "FullyQualifiedName~Rewrite_fixtures"` writes a new one. A test fails when the newest migration has no fixture, and another when a shipped migration changes, because today's migrations must still build each fixture's exact schema.
- **Diagnostic info** is "Copy diagnostic info" in Settings › About: plain text for a GitHub issue with versions, install type and channel, OS and session, folders, the schema and library counts, the settings that change behavior, and the last 100 app and 30 command log lines. `DiagnosticReport` builds it and opens the library read-only, so a broken library is described rather than created or migrated. A note beside the button, not a dialog, says what it holds and that paths can show your user name.
- **Log lines get a second look** before they go in the report. Logs never hold prompt text by design, so this is a backstop: only lines shaped like log output stay, quoted text is blanked, and a line that holds a `{{variable}}`, a prompt or workflow title, or five words in a row from any library text is left out, with the count shown.
- **Found while building:** a failed workflow import logged a reason that could quote a step's title. Fixed and shipped in 0.8.0.
- **Large libraries, measured:** `Prompuff.Performance.Tests` fills a library with 10,000 generated prompts through the real schema. Before v0.9, 10,000 prompts never finished opening; at 1,000 a refresh took seven seconds and built 41,000 visuals, listing everything took 246 ms, a `#tag` search 104 ms and a favorite 106 ms.
- **The library builds only what's on screen.** `CardGrid` lays out rows of cards in a `VirtualizingStackPanel` with the same columns and Density sizes, and the list virtualizes too. Rows, cards and tag chips are reused while scrolling; keyboard moves scroll the cursor's card into view first, and Ctrl+A and bulk actions still cover every prompt in the view. At 10,000 prompts the window holds 12 cards and about 1,300 visuals, and a refresh takes about 150 ms, with the search off the UI thread.
- **A new search, filter or sort starts at the top;** refreshing the same view keeps its place. When a dialog closes, focus doesn't follow a reused card to another prompt.
- **Migration 7 ("Large libraries"):** `PromptSearchRows` lets the search triggers reach a prompt's index row by rowid instead of scanning the index (a favorite went from 106 to 5 ms), and an update that changes no indexed text skips the index. `Prompts.TagNames` keeps tags in order, so listing runs no query per prompt. `IX_Prompts_Summary` covers every column a card shows and replaces `IX_Prompts_DeletedAt`. The migration also repairs a search index that drifted from the prompts. Connections use a 16 MB page cache. `schema-7.db` joins the fixtures, and the older fixtures all migrate through it.
- **Performance thresholds,** each the median of nine runs after a warm-up: searches and filters under 100 ms (measured 0.4–23 ms); listing everything, or a one-letter search that matches nearly everything, under 300 ms (57–120 ms, off the UI thread); sidebar counts and saves under 50 ms (2–7 ms); the window at most 80 cards or rows and 5,000 visuals, a page jump under 250 ms and a refresh under 1 s. `SearchPlanTests` check SQLite's plan for each search shape, which doesn't depend on machine speed.
- **Accessibility:**
  - `AccessibleNames` names every button after the text it shows, or its tooltip when it shows only an icon, and every text box after its placeholder. Avalonia names a button only when its content is a string, so buttons with an icon and a label were read as "Avalonia.Controls.StackPanel". Names set in XAML win: library cards and rows are read by their title, with the description as help.
  - `AccessibilityTests` visits every screen and fails on any button, box or list without a readable name; `FocusOrderTests` checks that Tab follows reading order with focus showing at each stop. The sidebar became rows instead of a DockPanel, so Tab reaches Settings after the library links, as it looks.
  - Faint text (Text3) now reaches 4.5:1 like the rest, since it carries setting descriptions and timestamps. Each palette moved the least it could toward its Text2. `ThemeTests` holds every theme to it; Text4 stays decorative, beside a text equivalent.
- **Checked by hand (2026-10-08):** a Windows portable 0.8.0 install with a library of six prompts, set to Beta, updated itself to 0.9.0-beta.1, backed the library up as `prompuff-schema6-….db`, applied migration 7, and kept every prompt, tag link (in order) and favorite; its bundled `prompuff` searched the migrated library.
- **Test hygiene:** tests clear only their own library's SQLite pool (`SqliteDatabase.ClearPool`); `ClearAllPools()` closed connections that tests running in parallel were opening. Write timings in the performance tests are compared with a 200-prompt library in the same run, because a CI runner's disk makes any write take ~50 ms.
- **The user guide is `docs/user-guide.md`,** task by task and checked against the views. A change to behavior or a label updates it in the same change. `CONTRIBUTING.md` is the short version of AGENTS.md for people.

## Decisions: 1.0 (2026-10-08)

- **1.0 is the release candidate's code.** Nothing under `src/` changed after 0.9.0; 1.0 adds only the version number. The 1.0 promise went into Settings › About, the README and the user guide before the release candidate, so the candidate shipped it.
- **The launch video** was made with `/brag` in Hyperframes: 21.5 seconds at 1080p, built from Prompuff's own tokens, fonts and copy. It shows Puff's "Talk is cheap. Show me the prompts.", Quick save from any app, `{{variables}}` filling in, `prompuff render` in a terminal with Claude Code, Codex and Copilot CLI over MCP, and a flick through the themes. The working files live in the gitignored `brag-output/`; the video and its poster are in `site/assets/video/`.
- **The website** is `site/`, plain HTML and CSS with a little JavaScript, deployed by `.github/workflows/pages.yml` to hazeliscoding.github.io/prompuff. It loads nothing from another site: a Content-Security-Policy enforces it, and the workflow refuses to deploy a page that would. Fonts are the app's own TTFs, and downloads link to `releases/latest/download/<asset>`.
- **The website's polish (2026-10-08)** borrows the grammar of a product page: a frosted header, Puff and the headline arriving in one opening sequence, the launch film growing to full size as it scrolls in, a feature tour whose screenshot stays in view while the words scroll past, tiles that play their trick once, a theme showroom whose colors glide between palettes, a terminal that types its commands, and a privacy statement that lights up word by word. It's still plain CSS and `site.js` with nothing from another site, and every effect steps aside for reduced motion.
- **The website on phones (2026-10-08):** the header folds into a menu, screenshots open at full size in a viewer that scrolls sideways, longer copy reads from the left, and small print and tap targets grow. Phones and tablets get no download buttons, since Prompuff can't run there: the film leads, and Share link sends the page to a computer through the share sheet, or copies the link where there isn't one. Windows tablets and Chromebooks still get downloads.
- **Update check workflow:** `.github/workflows/update-check.yml`, run by hand after a release, starts the previous stable release on Windows, Ubuntu and macOS runners with a scratch library and checks that it finds the new one. It's the macOS part of checking an update, since there's no Mac to click through one; installing is checked by hand on Windows and in WSLg. Its first run found 0.9.0 from 0.8.0 on all three. GitHub allows 60 unauthenticated API calls an hour per address and macOS runners share theirs, so a check of 1.0.0 failed twice with "rate limit exceeded"; each attempt now waits for the allowance to reset when it's used up.

## Decisions: after 1.0 (2026-10-08)

- **Prompuff grows by adding and extending, never by shifting.** The owner's rule for every version after 1.0: new features join and existing ones gain more, but nothing people rely on is removed, renamed or given a new meaning. It covers the library, Markdown, settings, the `prompuff` command, the MCP server and the `--quick-save` launch argument that desktop shortcuts call. A change that would break it is a bug to fix, not a reason for 2.0.
- **The 1.0 promise covers the command line and MCP.** `prompuff` keeps its commands, options, exit codes and JSON fields, and `get` and `render` print the same text. The MCP server keeps its tools, what they take and answer, their read-only hints, every prompt as a `prompuff://prompts/{id}` resource, and favorites as prompts with their arguments. Wording for people can change: messages on stderr, the plain-text lists, and MCP titles, descriptions, instructions and errors. The README, the user guide and the website state the wider promise; Settings › About keeps its text about the library and Markdown, which is still true.
- **Promises are tests, not specs.** OpenSpec was considered and left out: its validator checks that specs are written correctly, not that the code keeps them, and it's the separate spec layer this repo doesn't keep. A promise is a fixture that records what a release wrote or answered, and a test that holds every later version to it.
- **Fixtures are pinned.** Every file in a test project's `Fixtures` folder is pinned by its SHA-256 in `tests/pinned-fixtures.sha256`, and `PinnedFixtureTests` fails when one changes, goes missing or isn't pinned. `Write_fixtures` (was `Rewrite_fixtures`) pins what it writes and refuses to overwrite a pinned file. This replaces the v0.9 rule that let a released fixture be rewritten when the data it should hold changed: when a later version can't read a fixture, the code changes, never the fixture. Fixtures keep their exact bytes (`-text` in `.gitattributes`).
- **Fixtures from 1.0,** written by 1.0's own code, since nothing under `src/` has changed since the v1.0.0 tag:
  - `markdown-1.0.0/`: seven prompts, one file each, a workflow, and all of them in `library.zip`. They use every metadata key, Unicode and emoji, values that need quoting, a body with its own `# Notes` heading and a code fence, and a bare prompt. The round-trip tests couldn't catch a format break, since they pass when the writer and reader change together; renaming `favorite` in both failed three of the new tests.
  - `settings-1.0.0.json`: every setting changed from its default, as 1.0 saves it on Windows. A renamed key falls back to its default, and a file that no longer reads resets every setting, so the test compares every value.
  - `cli-1.0.0.json`: 39 cases run against `schema-7.db`, with each exit code, the JSON of every `--json` case and the exact text of `get` and `render`. A later answer passes when everything recorded is still there with the same value, so added fields are fine. Renaming a JSON field, an option and an MCP input failed 15 tests.
  - `mcp-1.0.0.json`: the tools with their input types, required inputs and read-only hints, eight tool calls, the five resources with their Markdown, and the favorites as prompts with two prompt calls. A resource is held to the prompt its Markdown carries, not its exact text, so the Markdown can gain metadata.

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
- [x] Windows signing with Azure Artifact Signing in the release workflow, checked on the runner with `signtool verify /pa`.
- [x] Signing switches on only when the Azure variables exist; forks and manual runs still build unsigned.
- [x] README: list macOS as CI-verified, and explain System Settings › Privacy & Security › Open Anyway, since the builds aren't notarized. macOS 15 removed the right-click › Open shortcut.
- [x] Publish v0.3.0 (2026-10-08). The installer's signature checks out as valid, signed by Hazel Granados.
- [x] Download the published `Prompuff-Setup.exe` and check SmartScreen (2026-10-08): it names Hazel Granados as the publisher, and still says "unrecognized app" until downloads build reputation.

**Done when:** one tag produces Windows, Linux x64, Linux ARM64 and both macOS packages, each one launches and creates its library on a CI runner, and `Prompuff-Setup.exe` is signed, so SmartScreen names the publisher.

## v0.4: Keyboard-first and lineage (tagged 2026-10-08)

Make daily use fast, and keep track of where prompts came from.

- [x] Library keyboard navigation: arrow keys move through cards and rows, Enter opens, Delete deletes after confirming, and typing jumps to a title.
- [x] Density modes from the design (Cozy, Compact, Dense).
- [x] Prompt lineage: Duplicate records `ParentPromptId`, and the detail view shows "Duplicated from" and a list of copies.
- [x] Multi-select in the library to tag, move, export or delete several prompts.
- [x] Folder import that walks subfolders, skips `.obsidian` and other dot folders, and reports skipped files.
- [x] Publish v0.4.0 (2026-10-08).

**Done when:** you can find, open, edit, render and copy a prompt without the mouse, and a duplicate links back to its parent.

## v0.5: Capture anywhere (tagged 2026-10-08)

Stash a good prompt from any app in a few seconds.

- [x] Single instance: launching Prompuff again, or running `Prompuff --quick-save`, hands off to the running copy.
- [x] Tray icon with Quick save, Open and Quit, and an option to keep running in the tray when the window closes.
- [x] A configurable system-wide Quick save hotkey behind `IGlobalHotkeyService`, implemented for Windows, X11 and macOS.
- [x] Wayland: Settings explains how to bind a desktop shortcut to `prompuff quick-save`.
- [x] Publish v0.5.0 (2026-10-08). The release smoke tests copied text, pressed Ctrl+Alt+P and stashed it on both Linux AppImages, and both Macs logged the Carbon hotkey as ready.

**Done when:** you can copy text in a browser, press the hotkey, and find the prompt stashed in under five seconds on Windows, on Linux under X11, and on macOS (CI-verified).

## v0.6: Workflows (tagged 2026-10-08)

The design's Workflows view: prompts that run in order, with a person copying between steps. Prompuff never runs a model.

- [x] A workflow is a named, ordered list of prompts, with a note per step on what it hands to the next.
- [x] Shared variables: a variable that appears in several steps is filled once.
- [x] Render and copy step by step, with progress through the steps.
- [x] Export a workflow as one Markdown document, and import it back.
- [x] Publish v0.6.0 (2026-10-08). Every smoke-tested build applied migration 6 and logged the hotkey as ready.
- [x] Publish v0.6.1 (2026-10-08): Settings › Export everything includes workflows, each as its own document under `workflows/` in the zip, and imports name the workflows they add.
- [x] Publish v0.6.2 (2026-10-08): imports keep only where workflow files are, not their text, until the prompts are in, so the 5 MB cap per file bounds the whole import again.

**Done when:** the design's "Angular upgrade, start to finish" workflow can be built, filled once and copied step by step.

## v0.7: CLI and MCP server (tagged 2026-10-08)

Let scripts and coding agents use the vault without opening the app.

- [x] `prompuff` CLI: `search`, `list`, `get`, `render` with `--var name=value`, and `quick-save` from stdin.
- [x] Ship the CLI in every package, and add an "Install command-line tool" button to Settings that puts it on the user's PATH (`~/.local/bin` on Linux and macOS, a user PATH entry on Windows).
- [x] `prompuff mcp`: a read-only MCP server over stdio with search, get and render tools, every prompt as a resource, and favorites as MCP prompts with their variables as arguments.
- [x] Settings › Integrations: "Let AI tools read my library (MCP)", off by default; while it's off, `prompuff mcp` serves nothing.
- [x] "Copy MCP config" in Settings, with a ready-to-paste setup for each client: Claude Code, Claude Desktop, GitHub Copilot CLI and Codex. The README shows the same setups.
- [x] The app picks up changes the CLI makes without a restart, using SQLite's `data_version`.
- [x] CI and the release workflow smoke-test the packaged CLI on every platform: quick-save, render, and an MCP session before and after the switch.
- [x] Publish v0.7.0 (2026-10-08).
- [x] Copilot CLI checked by hand by the owner: it searched the library and rendered a prompt through MCP (2026-10-08).

**Done when:** Claude Code, GitHub Copilot CLI and Codex can each search the vault and render a prompt with variables through MCP, and `prompuff render "Angular Upgrade Planner" --var repo_name=acme` prints the result on all three platforms.

## v0.8: Beta channel and themes (tagged 2026-10-08)

- [x] A `SHA256SUMS` file with every release.
- [x] A Beta channel: `-beta` tags publish pre-releases to `*-beta` Velopack channels, and the Beta option in Settings › Updates works.
- [x] Themes in Settings › Appearance. Prompuff Dark and Light stay the defaults, joined by palettes developers know from their editors: Darcula, Gruvbox Dark and Light, Dracula, Nord, One Dark, Tokyo Night, Solarized Dark and Light, and Catppuccin Mocha and Latte.
- [x] Pick a dark theme and a light theme, and System switches between them with the OS. Each theme fills the same tokens in `Tokens.axaml` (backgrounds, text, borders, accent and status colors), so views don't change.
- [x] Each palette's license and authors are credited in `licenses/Themes.md`.
- [x] The headless UI tests capture the library, the editor and a dialog in every theme.
- [x] Publish v0.8.0-beta.1 and v0.8.0-beta.2 as pre-releases (2026-10-08).
- [x] Publish v0.8.0 (2026-10-08).

**Done when:** a beta install updates from the beta channel, every release carries a `SHA256SUMS` file, and switching themes restyles the whole window, dialogs included, without a restart.

## v0.9: Release candidate (tagged 2026-10-08)

- [x] The library and Markdown formats are documented in `docs/`, with a fixture database from every released schema version and a test that opens each one.
- [x] Accessibility: every icon-only button has an accessible name, focus is visible and in order, and a CI script checks WCAG AA contrast for every theme's tokens.
- [x] Performance: 10,000 generated prompts load, scroll and search without lag, with search under 100 ms.
- [x] Settings › About › "Copy diagnostic info" gathers versions, paths and recent log lines for bug reports, still with no telemetry.
- [x] A user guide and `CONTRIBUTING.md`.
- [x] Two beta releases in a row with no data-loss or crash reports: 0.9.0-beta.1 and beta.2 (2026-10-08), with no open issues. The betas were hours apart rather than weeks, so this rests on the automated checks and the hand-checked 0.8.0 update more than on user reports.
- [x] Publish v0.9.0 as the release candidate (2026-10-08). Every stable channel built its delta from the real 0.8.0 package; the update itself was checked by hand on Windows (0.8.0 to 0.9.0-beta.1, the same code).

**Done when:** the release candidate updates cleanly from 0.8 on every platform, and no open issue is labeled data loss or crash.

## v1.0 (tagged 2026-10-08)

- [x] Tag 1.0 from the final release candidate, signed on Windows. 1.0.0 is 0.9.0's code with the version number changed.
- [x] Update from 0.9 to 1.0 checked by hand on Windows and in WSLg, and on macOS CI runners. An installed 0.9.0 on Windows and the 0.9.0 AppImage in WSLg each downloaded 1.0.0 from Settings › Updates, restarted into it, and kept every prompt, tag and favorite. The update check workflow found 1.0.0 from 0.9.0 on Windows, Ubuntu and macOS.
- [x] README and the About page state the 1.0 promise: later versions open 1.0 libraries.
- [x] A short launch video of the 1.0 app, made with the `/brag` skill.
- [x] A landing page on GitHub Pages: what Prompuff is, the launch video, screenshots, downloads for every platform, and the privacy promise. Fonts, images and the video are served from the site itself.
- [x] Publish v1.0.0 (2026-10-08), with deltas from 0.9.0 on every channel.

**Done when:** 1.0 is published for Windows, Linux x64 and ARM64, and macOS, every earlier 0.x install updates to it with its library intact, and the landing page is live with the launch video.

## Later

- Check that a fresh download of `Prompuff-Setup.exe` no longer gets SmartScreen's "unrecognized app" warning, once the certificate has built reputation.
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
