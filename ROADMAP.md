# Roadmap

Prompuff is a local-first desktop prompt vault (C#, .NET 10, Avalonia 12) for Windows, macOS and Linux. You save the prompts that worked, fill in their `{{variables}}`, copy the result, and keep the history of how each prompt evolved. This file tracks what gets built, in what order, and the decisions already made.

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
- **The README and the website sign off** with "Made with ♥ by hazeliscoding". On the website the line links to the repository, and the heart uses a new `--heart` token, the pink tone of each mode.

## Decisions: Puff comes alive (2026-10-08)

- **Puff moves like a small companion, not an ad.** It reacts to people, and otherwise floats, blinks and rests. The artwork stays as it is: the body, both eyes and the smile are already separate shapes, so none of the motion needs a redraw.
- **On the website,** the hero Puff squishes when a pointer comes by or a finger taps it. The finale Puff, now inline SVG with the same paths, hops hello when it scrolls in and squishes too. The Puff in the showroom's mock title bar hops when the visitor picks a swatch, but not during the automatic tour, which would keep it jumping every 2.6 seconds. The blink became one blink and a later double on a 9-second loop, instead of one every 5.5 seconds. Puff's loops pause while it's off screen. It's still CSS keyframes and a few lines of `site.js`, and everything new sits behind the reduced-motion gate, where Puff stays still with its eyes open.
- **The launch video was re-cut with Puff's reactions (2026-10-08).** Puff squishes as Quick save lands and as Render opens, the toast's Puff hops, the theme window's Puff hops on the first palette flick, and the lockup's Puff glances aside after its blink. It's still 21.5 seconds, rendered at the default quality like the original, and the poster frame is unchanged.
- **Reduce motion** is a switch in Settings › Appearance (the owner's choice), stored as `reduceMotion`. Avalonia 12.1.3 has no reduced-motion API, so Prompuff reads Windows' "Animation effects" (`SPI_GETCLIENTAREAANIMATION`, in `Platform/MotionPreference.cs`). Until the switch is changed the setting stays unset and follows Windows each time Prompuff opens. Linux and macOS give no signal Prompuff can read reliably, so motion starts on there. It holds Puff still and stops the switch thumb sliding; color fades on buttons and switches stay, since they don't move anything. The window carries a `still` class, and `.still c|Puff` sets `Puff.IsStill`.
- **Puff moves inside its control.** Squash, lift and how open the eyes are, are properties `Puff` draws itself, so a reaction never fights the `bob` on its render transform. Each reaction is a short keyframe animation that ends at rest, and it's skipped while Puff is still, hidden or outside a window. Avalonia eases a whole animation at once, so each step between keyframes gets its own key spline.
- **Where Puff moves in the app:** the title bar logo bobs twice when Prompuff opens, then rests and squishes when the pointer passes over it (the owner's choice). It used to bob forever, which kept the app redrawing even in the tray. Empty states say hello with two bobs each time they appear, and blink every four to nine seconds on a timer that only runs while that Puff is shown; about one tick in four is a glance aside instead, half a unit and back. A toast's Puff hops for each new confirmation. The About page floats Puff only while About is open, where it used to float whenever Settings was, and adds a squish on hover and a hop on click. Quick save and the startup error stay still, and Show Puff hides Puff, motion and all, where it did before.
- **Happy eyes don't blink.** Puff's happy face has closed arcs for eyes, so only the round eyes of the idle and sleepy faces blink.
- **Checked for 1.1.0 (2026-10-09):** the release's AppImage, in WSLg with scratch folders, opened with Reduce motion off and saved `"reduceMotion": true` when the switch was flipped from the keyboard. A portable 1.0.0 on Windows found 1.1.0 in Settings › Updates, downloaded it, restarted into it and kept its prompt; its bundled `prompuff` moved to 1.1.0 too. CI passed on all three runners, the macOS UI tests included, after one fix: on the first run a slow macOS runner let the empty state's fidget timer cancel the glance a test was measuring, so the test now stops the timer and measures from the start. The update check workflow found 1.1.0 from 1.0.0 on Windows and Ubuntu, and on macOS after waiting about half an hour for the runners' shared API allowance to reset.
- **Checked by hand on Windows (2026-10-08):** a scratch copy opened with Reduce motion off, matching this machine's Animation effects, and saved nothing until the switch was flipped through UI Automation, which saved `"reduceMotion": true`. Windows' setting wasn't turned off to try the other side, since it's the owner's machine; `PuffTests` covers it with a stand-in. WSLg and the macOS runners come with the next push.

## Decisions: working on main (2026-10-09)

- **`main` is protected (the owner's choice).** A ruleset makes every change land through a pull request, needs the three "Build and test" jobs to pass on a branch that's up to date with `main`, keeps the history linear, and stops force pushes and deleting `main`. Nobody bypasses it, agents included; in an emergency the owner can switch it off. Pull requests need no approval, since GitHub doesn't let you approve your own. They merge by rebase only, so each atomic commit lands as it is, and merged branches are deleted.
- **Release tags are fixed.** A second ruleset stops `v*` tags from being moved or deleted. A tag starts a release that every install updates to, and later releases download the previous one to build deltas.
- **Releases go through a pull request too:** the `chore(release)` bump merges like any change, and the merged commit gets the tag. For 1.1.0, CI and the release built at the same time, and a macOS-only test failure showed up after the tag; with required checks it couldn't have reached `main`.
- **GitFlow was considered and left out.** It suits scheduled releases with several versions maintained at once. Prompuff ships from `main`, every install updates to the newest version, and the beta channel is where a release gets tried first, so a `develop` branch would add merges without catching anything.
- **GitHub Project boards were considered and left out; this file stays the plan.** The decisions here can't live on a board, this file changes in the same pull request as the code, and agents read it directly. Bugs and requests from people using Prompuff go in GitHub Issues.

## Decisions: after 1.1 (2026-10-09)

- **Integrations come next (the owner's choice).** v1.2 brings `prompuff://` links and MCP tools that save prompts, v1.3 a Markdown mirror, v1.4 the browser extension, and v1.5 Flatpak and `.deb` packages. The small steps extend what 1.0's command line and MCP server already do, and the biggest job, the extension, waits until the rest is settled. Encrypted sync, prompt relationships and recipes, evaluation notes and translations stay in Later. (Reordered on 2026-10-10: Paste anywhere comes first, and each of these moves down one. See below.)
- **The plans keep the hard rules.** Registering `prompuff://` and connecting a browser write outside Prompuff's folders, so each is a step you start in Settings, like installing the command-line tool. New commands and tools get contract recordings of their own, since whatever ships joins the promise.

## Decisions: market research (2026-10-10)

- **What the research found.** Four searches covered rival apps, what people say about keeping prompts, how AI coding tools read reusable prompts, and open-source peers and how they're installed. People mostly keep prompts where they already work: text expanders, notes apps, or their AI tool's own skills and projects. The thing they praise most is a prompt landing wherever they're typing, and Prompuff's hotkey only saves. Open-source rivals lost people's data in updates, broke when a chat site changed its page, or stalled with one maintainer.
- **Paste anywhere comes next (the owner's choice).** It becomes v1.2 and the 2026-10-09 plan moves down one: links and agent saves are v1.3, the mirror v1.4, the extension v1.5 and Linux packages v1.6. It reuses the hotkey backends and Quick save's return to the app you came from.
- **Prompts are turning into skills.** Codex deprecated custom prompts for skills, Claude Code merged commands into skills, and Cursor, VS Code and Devin each ship a migration to them. Codex and Copilot CLI use MCP tools but not MCP prompts, so favorites never become slash commands there. The Agent Skills format (`SKILL.md`) has no variable syntax, so `{{variables}}` with remembered values stay Prompuff's own. v1.4 exports prompts as skills and commands and reads them back in. Syncing a repo's rules and `AGENTS.md` between tools is left to rulesync and ruler.
- **The MCP server doesn't announce changes.** It never sends `list_changed`, so a client's slash commands only catch up with favorites after a restart. That joins v1.3, with wording that tells tools-only clients to search Prompuff when someone names a saved prompt. Claude Code's docs say it splits prompt arguments on spaces, so a multi-word value only works through `render_prompt`.
- **Argument completion waits.** It could suggest values from remembered values, but those are never exported, and handing them to an AI tool would be. It's in Later, drawing on choice lists, once a named client asks for completions.
- **Getting found is the bottleneck.** The repo is three days old with no stars and no issues. winget has no popularity bar, and the Microsoft Store has been free for individual developers since 2025-09 and takes the signed `Prompuff-Setup.exe`, so both come first, then the official MCP Registry and the lists that read from it. Homebrew disabled casks that fail Gatekeeper on 2026-09-01, so it stays out while macOS builds aren't notarized.
- **Flathub keeps agents out.** Its rules say manifests must not contain AI-generated or AI-assisted content, AI tools must not open or automate submissions, and reviewers may reject apps over how much generated code they hold. Prompuff is built with agents, so v1.6 leads with the `.deb` and an AUR package. A Flatpak is the owner's to write and submit by hand, and the milestone doesn't wait on Flathub's review.
- **Variables gain options without new syntax.** Defaults and choice lists, when they come, live in an optional metadata key, not inside `{{…}}`. A body holding `{{name|x}}` renders as plain text in 1.0, and `render` must keep printing it.
- **The extension works from the selection and the text box, not the chat site's page.** Extensions that hooked into ChatGPT's page broke with each redesign. Its listing names every permission it takes, since VPN extensions were caught collecting millions of people's AI chats.

## Shipped

Each release's notes say what it brought, and the decisions above say why. The checklists these milestones were built from are in this file as of 1.1: `git show 40fb99d:ROADMAP.md`. The patch releases, 0.1.1, 0.1.2, 0.6.1 and 0.6.2, are on the [releases page](https://github.com/hazeliscoding/prompuff/releases).

| Version | Released | What it brought |
|---|---|---|
| [1.1: Puff comes alive](https://github.com/hazeliscoding/prompuff/releases/tag/v1.1.0) | 2026-10-09 | Puff's reactions in the app, on the website and in the launch video, Reduce motion, and the 1.0 promise pinned by fixtures |
| [1.0](https://github.com/hazeliscoding/prompuff/releases/tag/v1.0.0) | 2026-10-08 | The release candidate's code, signed on Windows, with the launch video and the website |
| [0.9: Release candidate](https://github.com/hazeliscoding/prompuff/releases/tag/v0.9.0) | 2026-10-08 | Large libraries, accessibility, diagnostic info, the user guide and the format docs |
| [0.8: Beta channel and themes](https://github.com/hazeliscoding/prompuff/releases/tag/v0.8.0) | 2026-10-08 | A Beta update channel, `SHA256SUMS` on every release, and thirteen themes |
| [0.7: CLI and MCP server](https://github.com/hazeliscoding/prompuff/releases/tag/v0.7.0) | 2026-10-08 | The `prompuff` command, and an MCP server for Claude Code, Codex and Copilot CLI |
| [0.6: Workflows](https://github.com/hazeliscoding/prompuff/releases/tag/v0.6.0) | 2026-10-08 | Prompts chained in order, filled in once and copied step by step |
| [0.5: Capture anywhere](https://github.com/hazeliscoding/prompuff/releases/tag/v0.5.0) | 2026-10-08 | The Quick save hotkey, the tray, and a desktop shortcut on Wayland |
| [0.4: Keyboard-first and lineage](https://github.com/hazeliscoding/prompuff/releases/tag/v0.4.0) | 2026-10-08 | Keyboard navigation, density modes, lineage between copies, multi-select and folder import |
| [0.3: macOS and Linux ARM64](https://github.com/hazeliscoding/prompuff/releases/tag/v0.3.0) | 2026-10-08 | Builds for both Macs and Linux ARM64, and a signed Windows installer |
| [0.2: Safe, searchable and shareable](https://github.com/hazeliscoding/prompuff/releases/tag/v0.2.0) | 2026-10-07 | Backups, Recently deleted, FTS5 search, remembered values and `.zip` sharing |
| [0.1: MVP](https://github.com/hazeliscoding/prompuff/releases/tag/v0.1.0) | 2026-10-07 | The library, `{{variables}}`, versions, Markdown import and export, and updates |

## Getting found

These need no release. Each lands as its own pull request whenever it's ready, alongside the milestones below.

- [ ] winget: the release workflow opens the manifest pull request to `microsoft/winget-pkgs` for each stable release, installing `Prompuff-Setup.exe` per user and silently.
- [ ] A Microsoft Store listing for the signed `Prompuff-Setup.exe`. The owner registers the free individual developer account.
- [ ] The official MCP Registry lists the server as `io.github.hazeliscoding/prompuff`, with whatever package entry the registry needs, and says it comes with the Prompuff app. Glama and awesome-mcp-servers follow.
- [ ] Submissions to awesome-claude-code (open to repos 14 days old, so from 2026-10-21), awesome-avalonia, Awesome-Prompt-Engineering and awesome-privacy.
- [x] The README and the website lead with what rival apps got wrong: a backup before an update changes the library, a library every later version opens, and no network beyond the update check.
- [ ] Check that a fresh download of `Prompuff-Setup.exe` no longer gets SmartScreen's "unrecognized app" warning, now that the certificate has had time to build reputation.

**Done when:** `winget install Prompuff` installs the signed build, and Prompuff is listed in the Microsoft Store and the MCP Registry.

## v1.2: Paste anywhere

Find a prompt and drop it into the app you're typing in, without opening Prompuff.

- [ ] **Quick insert**, a second global hotkey beside Quick save, with its own default and the same recorder in Settings. It opens a small search over the library, and the first key typed after the hotkey lands in it.
- [ ] Arrows and Enter pick a prompt. One with `{{variables}}` asks for them in the same window, filled with its remembered values, before it goes.
- [ ] The result is copied and Prompuff steps back to the app you came from, as after a hotkey Quick save. **Paste after inserting** also presses the paste key there: `SendInput` on Windows and XTest on X11. On macOS it needs Accessibility access, which Settings asks for, and without it the result is only copied.
- [ ] Wayland has no global keys or pasting into other apps, so a desktop shortcut runs the app with `--quick-insert` (a new launch argument, like `--quick-save`) and the result is copied.
- [ ] `settings-1.2.0.json` records the new settings beside 1.0's recording.
- [ ] Publish v1.2.0.

**Done when:** on Windows and X11, Quick insert from another app finds a prompt by typing, fills its variables and pastes the result into that app; on macOS on CI it opens and copies; and on Wayland the desktop shortcut does the same through the clipboard.

## v1.3: Links and agents that save

Put a prompt one click away from anywhere, and let AI tools add to the library when you allow it.

- [ ] `prompuff://` links that open, copy or render a prompt by its ID, with **Copy link** in a prompt's menu and `prompuff link <prompt>` on the command line.
- [ ] Registering the link scheme is a step you start in Settings › Integrations on Windows and Linux, like installing the command-line tool. On macOS the app bundle declares it.
- [ ] MCP tools that save a new prompt, off until **Let AI tools add prompts** is turned on in Settings › Integrations, apart from read access. Each saved prompt's first version names the tool that saved it.
- [ ] The MCP server watches the library's `data_version`, like the app, and sends `list_changed` for prompts and resources when it changes, so a client's slash commands follow favorites without a restart.
- [ ] The server's instructions tell clients that can't show MCP prompts, such as Codex and Copilot CLI, to search Prompuff when someone names a saved prompt.
- [ ] `cli-1.3.0.json` and `mcp-1.3.0.json` record the new command and tools beside 1.0's recordings, so later versions keep them too.
- [ ] Publish v1.3.0.

**Done when:** a `prompuff://` link in a browser opens, copies or renders its prompt on Windows and Linux, and on macOS on CI, Claude Code can save a prompt over MCP only once the setting allows it, a favorite added in the app shows up in Claude Code's slash commands without restarting it, and the 1.0 and 1.3 contract tests both pass.

## v1.4: A Markdown mirror and agent skills

Keep the library as plain files too, for Git, Syncthing, any editor, or the skills folders AI tools read.

- [ ] A mirror folder, chosen in Settings › Storage, that Prompuff keeps in step with the library: one Markdown file per prompt in the 1.0 format, and workflows in `workflows/`.
- [ ] Saving a prompt rewrites its file, removing it for good deletes the file, and a file keeps its name when the prompt's title changes, so Git sees an edit rather than a new file.
- [ ] The mirror is one-way: Prompuff writes it, and Import stays the way files come in. A two-way mirror stays in Later.
- [ ] **Export as skill** for a prompt or a selection: an Agent Skills folder (`<name>/SKILL.md`, which Codex, Cursor and Copilot CLI read from `~/.agents/skills`), a Claude Code skill, a Copilot `.prompt.md` or a Gemini CLI `.toml` command. `{{variables}}` become each tool's own arguments where it has them (`$name` in Claude Code, `${input:name}` in Copilot, `{{args}}` in Gemini) and a short list of inputs where it doesn't. `prompuff export --format …` does the same.
- [ ] A skills mirror: favorites kept as skills in a folder you pick, such as `~/.claude/skills` or `~/.agents/skills`. A skill keeps its folder name when the title changes, and Prompuff only ever removes skills it wrote.
- [ ] Import reads Claude Code commands and skills, Copilot `.prompt.md` files and Gemini `.toml` commands, and turns their arguments back into `{{variables}}`.
- [ ] Fixtures record each export format as 1.4 writes it, and `cli-1.4.0.json` records `export`.
- [ ] Publish v1.4.0.

**Done when:** a mirror folder under Git shows each saved change as a readable diff, a renamed prompt keeps its file, importing the folder into a fresh library brings back the same prompts, a favorite in the skills mirror runs as a skill in Claude Code and Codex, and a folder of Claude Code commands imports with its arguments as `{{variables}}`.

## v1.5: Capture from the browser

Save a prompt from ChatGPT, Claude or any page without leaving the browser, and put one back.

- [ ] A browser extension for Chrome, Edge and Firefox, in `extension/`: **Save to Prompuff** on a selection, or on the text box you're typing in, hands the text to Quick save through native messaging. It reads the selection and the focused text box, never a chat site's own page structure.
- [ ] **Insert from Prompuff**: the extension's popup searches the library through the same host, fills a prompt's variables and puts the result in the focused text box. This is the way to paste on Wayland, where Quick insert can only copy.
- [ ] The native messaging host is the `prompuff` command. Connecting a browser is a step you start in Settings › Integrations, like installing the command-line tool, since it writes a manifest into the browser's own folders.
- [ ] The extension and its host make no network requests, and the extension asks only for the permissions saving and inserting need. Each listing names them.
- [ ] Listings on the Chrome Web Store (a one-time $5 developer fee), Microsoft Edge Add-ons and Firefox Add-ons.
- [ ] Publish v1.5.0.

**Done when:** a selection on any page, or what's typed in ChatGPT or Claude, lands in the library through Save to Prompuff, and a library prompt lands in their text box through Insert from Prompuff, on Windows, Linux and macOS, with no network traffic from the extension or its host.

## v1.6: More ways to install on Linux

- [ ] A `.deb`, built by the release workflow beside the AppImage, and a `prompuff-bin` package in the AUR that wraps the AppImage.
- [ ] Prompuff knows how it was installed: a `.deb` or AUR copy points to the new download or the package manager instead of updating itself, and a Flatpak leaves updates to Flathub.
- [ ] The `prompuff` command and the MCP setups in Settings › Integrations work from each package.
- [ ] A Flatpak on Flathub, which the owner writes and submits by hand under Flathub's AI rules. The milestone doesn't wait for its review.
- [ ] Publish v1.6.0.

**Done when:** Prompuff installs from a `.deb` on Ubuntu and from the AUR, keeps its library across an update, and each copy says how it updates.

## Later

- Optional end-to-end encrypted sync.
- A two-way Markdown mirror.
- Prompt relationships beyond lineage, and recipe templates, such as one prompt including another.
- Model notes, for prompts that stop working after a model update: the models a prompt works with (the design's "Works with" chips), and a note you write on a version.
- Variable defaults and choice lists, in an optional metadata key.
- MCP argument completion from choice lists, once a named client asks for completions. Remembered values stay out of it.
- Skills over MCP (`skill://`), once Claude Code or Codex supports it.
- Translations.

## Not planned

- Accounts, authentication, teams or shared libraries.
- Any sync that isn't optional and end-to-end encrypted.
- A prompt marketplace, AI prompt generation or built-in LLM execution.
- Telemetry, analytics or prompt performance tracking.
- Embedding or vector search.
- Electron, embedded Chromium or a web-app shell.
- Syncing a repo's rules or `AGENTS.md` between AI tools, which rulesync and ruler already do.
- A Homebrew cask while macOS builds aren't notarized.
