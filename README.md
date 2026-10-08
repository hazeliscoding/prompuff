<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/brand/lockup-dark.svg">
    <img alt="Prompuff" src="docs/brand/lockup.svg" height="40">
  </picture>
</h1>

> *Talk is cheap. Show me the prompts.*
>
> — Puff, with apologies to Linus Torvalds

**A local-first home for prompts worth keeping.**

Prompuff is a small desktop app for the prompts that actually worked. Save them, tag them, fill in their `{{variables}}`, copy the result, and keep a note on why each one worked. Every saved change becomes a version you can read back or restore.

Your prompts stay on your machine. There is no account, no cloud and no telemetry.

> **Status:** [v0.6.2 is out](https://github.com/hazeliscoding/prompuff/releases/latest) for Windows x64, macOS (Apple Silicon and Intel), and Linux x64 and ARM64. [ROADMAP.md](ROADMAP.md) has the road to 1.0.

![The library: a sidebar with collections and tags, and prompt cards with tags, usefulness and edit times](docs/screenshots/library.png)

![The Render tab: variables on the left, the rendered prompt on the right with filled values and one unfilled variable highlighted](docs/screenshots/render.png)

## What it does

- **Keeps** prompts with a title, description, body, notes ("why this worked"), a collection, tags, a favorite flag and a 1–5 usefulness rating.
- **Renders** `{{variable_name}}` placeholders. Fill in the values, watch the preview update, and copy the result. Unfilled variables stay as `{{tokens}}`, and each prompt remembers its values until you clear them.
- **Chains** prompts into workflows: plan, build, then check. Fill in the shared details once, and copy each step into your model in turn, carrying its answer to the next. A workflow travels as one Markdown document, and exporting everything from Settings takes your workflows along.
- **Versions** every saved change to the title, description, body or notes. The History tab shows a line diff, and Restore brings an old version back as a new one.
- **Keeps up with the keyboard:** arrow keys, type-to-jump and Enter in the library, and Select to tag, move, export or delete many prompts at once. Duplicates remember where they came from.
- **Finds** prompts fast: ranked search across titles, descriptions, bodies, notes and tags that ignores case and accents ("cafe" finds "Café"), or type `#tag`. Filter by Favorites, Recent, a collection or a tag.
- **Captures** from anywhere: copy a prompt in any app and press **Ctrl+Alt+P** (⌃⌥P on macOS). Quick save opens with your clipboard, Enter stashes it, and you're back where you were. The tray icon does the same, and Prompuff can keep running there when you close the window.
- **Travels** as plain Markdown with a small metadata block. Export what you're looking at as one `.zip` for another machine or a teammate; importing it skips prompts you already have. Import a whole folder, such as an Obsidian vault, and Prompuff walks its subfolders and leaves `.obsidian` alone.
- **Forgives**: deleted prompts wait 30 days in Recently deleted, and the library is backed up once a day.

## Supported platforms

| Platform | Package |
|---|---|
| Windows x64 (10 and 11) | `Prompuff-Setup.exe`, signed, installed per user with automatic updates |
| macOS on Apple Silicon | `Prompuff-macOS-arm64.pkg`, or `Prompuff-macOS-arm64.zip` to run the app without installing |
| macOS on Intel | `Prompuff-macOS-x64.pkg` or `Prompuff-macOS-x64.zip` |
| Linux x64 (X11, or Wayland through XWayland) | `Prompuff-linux-x64.AppImage` |
| Linux ARM64 | `Prompuff-linux-arm64.AppImage` |

All of them are self-contained, so you don't need to install .NET, and all of them update themselves.

On macOS, Prompuff is checked on GitHub's macOS runners rather than by hand, because the developer has no Mac. If something looks wrong, please [open an issue](https://github.com/hazeliscoding/prompuff/issues). The builds aren't notarized by Apple, so macOS blocks the first launch:

1. Open Prompuff (or the `.pkg`) once and close the warning.
2. Go to **System Settings › Privacy & Security**, scroll down to Security, and choose **Open Anyway**.
3. Confirm once more. macOS remembers the choice after that.

macOS 15 removed the old right-click › Open shortcut, so the steps above are the way through.

On Linux:

```bash
chmod +x Prompuff-linux-x64.AppImage    # or Prompuff-linux-arm64.AppImage
./Prompuff-linux-x64.AppImage
```

If your distribution doesn't ship FUSE 2, run it with `APPIMAGE_EXTRACT_AND_RUN=1` set.

## Keyboard shortcuts

On macOS, use ⌘ Cmd wherever the table says Ctrl.

| Keys | Action |
|---|---|
| Ctrl+K or Ctrl+Shift+P | Command palette: open, copy, or run a command |
| Ctrl+N | New prompt |
| Ctrl+S | Save |
| Ctrl+F | Search |
| Ctrl+Enter | Open Render, then copy the rendered prompt |
| Ctrl+Shift+C | Copy the template with its variables |
| Ctrl+Shift+S | Quick save from the clipboard |
| Ctrl+Alt+P, from any app | Quick save from the clipboard (change it in Settings › Quick save) |
| Ctrl+D | Toggle favorite |
| Ctrl+H | Version history |
| Esc | Close a dialog or the palette, or go back from a prompt to the library |

In the library, the arrow keys, Home and End move between prompts, Enter opens one, and Delete (⌘⌫ on macOS) deletes it after asking. Typing the first letters of a title jumps to it. From the search box, ↓ moves to the results and Enter opens the best match. To work on several prompts at once, choose Select, or Ctrl+click (⌘-click), Shift+click or press Ctrl+A (⌘A); then tag, move, export or delete them together.

### Quick save from a desktop shortcut

Wayland doesn't let apps own a global hotkey, so bind a shortcut in your desktop's settings instead. Settings › Quick save shows the exact command, which is the AppImage followed by `--quick-save`. Running Prompuff again always hands off to the copy that's already open.

## Development

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (10.0.100 or later).
- Windows 10 or 11, or a Linux desktop with X11 libraries. On Debian or Ubuntu: `sudo apt install libx11-6 libice6 libsm6 libfontconfig1`.
- Packaging only: the Velopack CLI, pinned as a local tool. `dotnet tool restore` installs it.

### Build, test and run

```bash
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Prompuff.App
```

To keep a development run away from your real library, point Prompuff at another folder:

```bash
PROMPUFF_DATA_DIR=/tmp/prompuff-dev dotnet run --project src/Prompuff.App
```

In PowerShell, use `$env:PROMPUFF_DATA_DIR = "$env:TEMP\prompuff-dev"`. The prompts in [`samples/`](samples), and the workflow in [`samples/workflows/`](samples/workflows), can be imported from Settings › Import / export.

The UI tests drive the real main window headlessly. Set `PROMPUFF_SCREENSHOTS` to a folder to save a PNG of each state they visit:

```bash
PROMPUFF_SCREENSHOTS=./screenshots dotnet test tests/Prompuff.App.Tests
```

### Project structure

| Project | What it holds |
|---|---|
| `src/Prompuff.Domain` | Entities and pure rules: prompts, versions, collections, tag normalization. No dependencies. |
| `src/Prompuff.Application` | Interfaces and services: template rendering, versioning, collections, line diff. No UI or database code. |
| `src/Prompuff.Infrastructure` | SQLite repositories and migrations, FTS5 search, backups, Markdown import and export, data paths, settings, file logging, Velopack updates. |
| `src/Prompuff.App` | The Avalonia app: views, view models, controls, theme tokens, and platform services for the clipboard, file pickers and launcher. |
| `tests/*` | xUnit tests for each layer, plus headless UI tests for the app. |

## Local storage

| | Library, logs and backups | Settings |
|---|---|---|
| Windows | `%LOCALAPPDATA%\Prompuff\` | `%LOCALAPPDATA%\Prompuff\settings.json` |
| Linux | `$XDG_DATA_HOME/prompuff/` (default `~/.local/share/prompuff/`) | `$XDG_CONFIG_HOME/prompuff/settings.json` (default `~/.config/prompuff/`) |

The library is one SQLite file, `prompuff.db`. Prompuff never writes next to its executable or inside the AppImage, so updates and reinstalls leave your prompts alone. Prompuff copies the library to `backups/` once a day and keeps the last 30, plus a copy before a new version changes the database format. Settings › Storage shows the folder and lists the backups, and restoring one copies the current library aside first.

## Packaging

Both platforms publish self-contained and are packed with [Velopack](https://velopack.io). The pack ID is `Prompuff.Desktop`: Velopack installs to `%LocalAppData%\Prompuff.Desktop`, which keeps the app apart from the data folder.

Windows (run on Windows):

```powershell
dotnet tool restore
dotnet publish src/Prompuff.App -c Release -r win-x64 --self-contained true -p:Version=0.1.0 -o artifacts/publish/win-x64
dotnet vpk pack --packId Prompuff.Desktop --packVersion 0.1.0 --packDir artifacts/publish/win-x64 `
  --mainExe Prompuff.exe --packTitle Prompuff --icon src/Prompuff.App/Assets/prompuff.ico `
  --channel win --outputDir artifacts/releases/win
```

This writes `Prompuff.Desktop-win-Setup.exe`, a portable zip, and the update files (`*-full.nupkg`, `releases.win.json`, `RELEASES`).

Linux AppImage (run on Linux):

```bash
dotnet tool restore
dotnet publish src/Prompuff.App -c Release -r linux-x64 --self-contained true -p:Version=0.1.0 -o artifacts/publish/linux-x64
dotnet vpk pack --packId Prompuff.Desktop --packVersion 0.1.0 --packDir artifacts/publish/linux-x64 \
  --mainExe Prompuff --packTitle Prompuff --icon src/Prompuff.App/Assets/prompuff.png \
  --categories "Utility;Development" --channel linux --outputDir artifacts/releases/linux
```

This writes the `.AppImage` and its update files (`*-full.nupkg`, `releases.linux.json`).

## Releases

1. Set the version in `Directory.Build.props` and commit.
2. Tag and push: `git tag v0.1.0 && git push origin v0.1.0`.
3. The [Release workflow](.github/workflows/release.yml) tests and packs both platforms, starts the AppImage under Xvfb as a smoke test, and opens a **draft** GitHub Release. It holds `Prompuff-Setup.exe`, `Prompuff-linux-x64.AppImage`, a portable Windows zip and the Velopack update files.
4. Read the draft and publish it. Installed copies only see published releases.

Running the workflow by hand from the Actions tab builds the same artifacts without publishing anything.

## Privacy

Prompuff stores prompts locally and does not upload prompt content.

- No account, telemetry, analytics or AI calls.
- The only network request is the update check against this repository's GitHub Releases. It sends nothing about your library, and you can turn it off in Settings › Updates.
- Logs record prompt IDs and counts, never titles, bodies, notes or variable values.

## License

[MIT](LICENSE). Manrope and IBM Plex Mono are under the SIL Open Font License, and the icons are from [Lucide](https://lucide.dev) (ISC). See [`licenses/`](licenses).
