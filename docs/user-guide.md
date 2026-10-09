# Prompuff user guide

Prompuff keeps the prompts that worked. You save them, find them again, fill in their `{{variables}}` and copy the result, and every saved change becomes a version you can go back to. Everything stays on your computer.

On macOS, use ⌘ Cmd wherever this guide says Ctrl.

- [Getting started](#getting-started)
- [The library](#the-library)
- [Search](#search)
- [Collections and tags](#collections-and-tags)
- [Editing and versions](#editing-and-versions)
- [Render with variables](#render-with-variables)
- [Quick save, the hotkey and the tray](#quick-save-the-hotkey-and-the-tray)
- [Workflows](#workflows)
- [Import and export](#import-and-export)
- [Backups and Recently deleted](#backups-and-recently-deleted)
- [Themes and density](#themes-and-density)
- [Updates and the Beta channel](#updates-and-the-beta-channel)
- [The prompuff command and MCP](#the-prompuff-command-and-mcp)
- [Where your data lives](#where-your-data-lives)
- [Privacy](#privacy)
- [Reporting a bug](#reporting-a-bug)

## Getting started

1. Download Prompuff for your computer from the [latest release](https://github.com/hazeliscoding/prompuff/releases/latest):
   - **Windows:** run `Prompuff-Setup.exe`. It installs for your user account.
   - **macOS:** open the `.pkg` for your Mac (Apple Silicon or Intel), or unzip the `.zip` to run the app without installing. The builds aren't notarized, so macOS blocks the first launch: close the warning, go to **System Settings › Privacy & Security**, choose **Open Anyway**, and confirm.
   - **Linux:** make the AppImage executable with `chmod +x Prompuff-linux-x64.AppImage` (or the `arm64` one) and run it. If your distribution has no FUSE 2, run it with `APPIMAGE_EXTRACT_AND_RUN=1` set.
2. Open Prompuff. The library starts empty.
3. Choose **New prompt** (Ctrl+N), give it a title, write the prompt, and choose **Save** (Ctrl+S). Or copy a prompt from anywhere and choose **Quick save from clipboard**.

To try Prompuff with some examples first, import the files in the repository's [`samples/`](../samples) folder from Settings › Import / export.

The **command palette** (Ctrl+K or Ctrl+Shift+P) opens prompts, copies them, and runs most commands, such as New workflow or Import a folder. Settings › Keyboard shortcuts lists every shortcut.

## The library

The sidebar picks what the library shows:

- **All prompts**, **Favorites**, and **Recent**: the 20 prompts you edited or opened last.
- **Workflows**, covered [below](#workflows).
- **Collections**, plus **Uncategorized** for prompts in none.
- **Tags**: click one to see its prompts.
- **Recently deleted**, which appears only while it holds prompts.

In the header, sort by **Last edited**, **Title** or **Usefulness**, and switch between **Cards** and **List**. Prompuff remembers both.

**With the keyboard:** the arrow keys, Home and End move between prompts, Enter opens one, and Delete deletes it after asking (⌘⌫ on macOS). Type the first letters of a title to jump to it. Esc goes back to the library from a prompt.

**Several at once:** choose **Select**, or Ctrl+click, Shift+click, Shift with the arrows, or Ctrl+A. Then **Tag** (add or remove tags), **Move to** a collection, **Export** or **Delete** the picked prompts. **Done** or Esc stops picking.

## Search

Type in the search box at the top (Ctrl+F). Results update as you type, best matches first.

- **Words:** every word must match the start of a word in the title, description, prompt, notes or tags. `ang plan` finds "Angular Upgrade Planner", but `gular` doesn't.
- **Case and accents** don't matter: `cafe` finds "Café".
- **`#tag`** finds prompts with exactly that tag. Combine it with words or more tags: `#review diff`.
- **Symbols** such as `->` are matched as written.

Search looks inside what the library is showing, so choose **All prompts** to search everything. From the search box, ↓ moves to the results and Enter opens the best match.

## Collections and tags

A prompt lives in one collection, or none, and can have any number of tags.

- **New collection:** the **+** beside Collections in the sidebar. A prompt you create while a collection is open starts in it.
- **Rename or delete:** right-click the collection. Deleting a collection keeps its prompts; they become uncategorized.
- **Move a prompt:** pick a collection in the editor, or use Select › **Move to** for several.
- **Tags:** type in **+ add tag** in the editor and press Enter. Commas add several at once. Tags are lowercase, lose a leading `#`, and turn spaces into `-`, so `Angular`, `angular` and `#angular` are one tag. A tag disappears when no prompt uses it.

## Editing and versions

A prompt has a **title**, a one-line **description**, the **prompt** itself (Markdown), and **Why this worked**, notes to your future self.

**Saving:** changes to those four fields wait for **Save** (Ctrl+S), and each save makes a new version. Prompuff also saves when you leave the prompt or close the window. The favorite heart (Ctrl+D), the usefulness rating (Meh, Okay, Solid, Great, Cooked), the collection and the tags save as soon as you change them, and don't make versions.

**Copying:** **Copy** copies the prompt with your variable values filled in. The **…** menu has **Copy template with variables** (Ctrl+Shift+C), which copies it as written.

**Duplicate** makes a copy with its own history, which shows "Duplicated from" and links back. The original lists its copies.

**History** (Ctrl+H) lists every version with a short note, such as "Edited body (+3 −1 lines)". Pick one to see what changed, or switch to **Full text**. **Restore** makes an old version current again as a new version, so nothing is lost. **Duplicate** there starts a new prompt from that version.

**Delete prompt…** is in the **…** menu. Deleted prompts go to [Recently deleted](#backups-and-recently-deleted).

## Render with variables

Wrap the parts that change in double braces: `{{repo_name}}`. Names use letters, digits and underscores, start with a letter or underscore, and are case-sensitive.

1. Open the **Render & copy** tab, or press Ctrl+Enter from the editor.
2. Fill in the values. The preview updates as you type.
3. Choose **Copy rendered prompt**, or press Ctrl+Enter again.

A variable you leave empty stays as `{{repo_name}}` in the copy, highlighted in the preview. Each prompt remembers its values for next time; **Clear values** forgets them.

## Quick save, the hotkey and the tray

Quick save stashes a prompt in a few seconds:

1. Copy the prompt in any app.
2. Press **Ctrl+Alt+P** (⌃⌥P on macOS) from anywhere, or Ctrl+Shift+S inside Prompuff, or choose **Quick save** at the top of the window.
3. Quick save opens with what you copied and a title guessed from it. Pick a collection and tags if you like, and **Worked well** (rates it Great), **Template** or **Idea** (add those tags). The **Why save this?** line becomes its notes.
4. Choose **Stash it**. After a hotkey Quick save, Prompuff goes back to the tray or the taskbar if that's where it was, so you land back in your app.

**Change the hotkey** in Settings › Quick save: choose **Change** and press the new keys, which need Ctrl, Alt or the Windows, Super or ⌘ key, plus a letter, digit, F1–F12 or Space. Settings says when another app already owns a combination. **Turn off** turns it off.

**On Wayland,** apps can't listen for keys while another app has focus, so bind a shortcut in your desktop's settings instead. Settings › Quick save shows the exact command (the AppImage followed by `--quick-save`), with the steps for GNOME and KDE Plasma. If Prompuff is running, the shortcut brings it forward with Quick save open; if not, it starts it.

**The tray icon** (the menu bar on macOS) has **Quick save from clipboard**, **Open Prompuff** and **Quit Prompuff**, and clicking it opens the window. Turn on **Keep running in the tray** in Settings › Quick save to keep Prompuff there when you close the window. Desktops without a tray, such as stock GNOME, don't show the icon; open Prompuff again to bring the window back. Only one copy of Prompuff runs at a time, and opening it again always hands over to that copy.

## Workflows

A workflow is prompts in order, such as plan, build, then check. You copy each step into your model and carry its answer to the next. Prompuff never runs a model itself.

1. Choose **Workflows** in the sidebar, then **New workflow**, and name it.
2. **Add a step** and find a prompt from your library. Steps point at your prompts, so editing a prompt updates every workflow that uses it.
3. Reorder steps with the arrows. Under each step, an optional **Hands off** note says what it passes to the next.
4. Choose **Run** (Ctrl+Enter). Under **Fill in once**, each variable appears once, however many steps use it. Paste a step's answer into the next step's variable.
5. **Copy step 1** copies it and moves on (Ctrl+Enter does the same). **Start over** clears the copied marks and keeps your values.

Name, description and notes save as you type. **Export** saves the workflow as one Markdown file with every step's prompt inside; import it from Settings › Import / export. **Delete workflow…** in the **…** menu keeps its prompts in your library.

## Import and export

Prompts travel as plain Markdown files with a small metadata block on top:

```markdown
---
title: README Cleanup
description: "Rewrites a README for a first-time contributor: install, run, test, contribute. Keeps the project voice."
tags:
  - writing
  - review
collection: Writing & docs
rating: 3
---

# Prompt

Rewrite the README for {{repo_name}} so a new contributor can go from clone to running tests in under ten minutes.

# Notes

Why it worked, for your future self.
```

**Export:**

- **Export** in the library header saves what the library is showing, search included, as one `.zip`. Select › **Export** saves just the picked prompts.
- The editor's **…** menu has **Export as Markdown…** for one prompt.
- Settings › Import / export has **Export everything**, which includes your workflows, and **Export collection**.

**Import** from Settings › Import / export:

- **Choose files** takes `.md` files or a `.zip` export.
- **Choose a folder** takes a whole folder, such as an Obsidian vault. It reads subfolders too, skips folders and files whose names start with a dot (like `.obsidian`), and lists the files it passed over.

Imports make new prompts with their own history, and workflows come in too. Prompts whose title and body you already have, and workflows you already have, are skipped. Favorites, ratings, tags and collections come along. Remembered variable values never leave your library.

## Backups and Recently deleted

**Recently deleted** keeps deleted prompts for 30 days. **Restore** puts one back; **Empty** removes everything there for good. A deleted prompt can't be opened until it's restored.

**Backups:** Prompuff copies the library to its `backups` folder once a day and keeps the last 30, plus a copy before an update changes how the library is stored. To go back to one, open Settings › Storage and choose **Restore** beside it. Prompuff copies your current library aside first, so you can change your mind.

## Themes and density

Settings › Appearance:

- **Theme:** Dark, Light, or System, which follows your computer.
- **Dark themes:** Prompuff Dark, Darcula, Gruvbox Dark, Dracula, Nord, One Dark, Tokyo Night, Solarized Dark and Catppuccin Mocha. **Light themes:** Prompuff Light, Gruvbox Light, Solarized Light and Catppuccin Latte. Pick one of each; System switches between them.
- **Density:** how much room each prompt gets in the library. Cozy shows everything, Compact trims the padding and shows one line of description, and Dense drops descriptions to fit the most.
- **Puff, the mascot:** on or off.

## Updates and the Beta channel

Installed copies check GitHub Releases each time Prompuff opens. When an update is ready, the sidebar says so; choose it, then **Download and restart**. Turn off **Check for updates automatically** in Settings › Updates to check only when you choose **Check for updates**.

The **Beta** channel in Settings › Updates gets new versions early, as GitHub pre-releases, and every stable release too. Updates never move to an older version, so switching back to **Stable** keeps the beta until a newer stable release comes out.

Updates replace the app, never your library. When a new version changes how the library is stored, Prompuff copies it to the backups folder first. From 1.0 on, Prompuff only grows. Every later version opens a 1.0 library and imports the Markdown 1.0 exports, as Settings › About promises, keeps your settings, and keeps the `prompuff` command and MCP server working the way scripts and AI tools use them.

## The prompuff command and MCP

**Install the command** from Settings › Integrations › **Install**. On Windows it adds the command to your PATH, so open a new terminal afterwards. On Linux and macOS it links `~/.local/bin/prompuff`; if your shell can't find it, add `~/.local/bin` to your PATH. **Remove** takes it away again.

```bash
prompuff search angular                 # one prompt per line; #tag works too
prompuff list --favorites               # or --collection Design, --tag review
prompuff get "README Cleanup"           # the prompt's text
prompuff render "Angular Upgrade Planner" --var repo_name=acme --var target_version=22
git diff | prompuff quick-save --title "Review this diff" --tag review
```

Name a prompt by its title, a few words only it matches, or its ID. `render` leaves unfilled variables as `{{tokens}}` and lists them on stderr. Add `--json` for scripts: the commands, options, exit codes and JSON fields stay the same in every later version, which can only add more. Run `prompuff help` for the rest. The app picks up changes within two seconds.

**MCP** lets AI tools such as Claude Code, Claude Desktop, GitHub Copilot CLI and Codex use your library. They can search, read and render your prompts, and use your favorites as prompts with their variables as arguments, such as slash commands in Claude Code. They can't change or delete anything.

1. Turn on **Let AI tools read my library (MCP)** in Settings › Integrations. Until you do, Prompuff shares nothing.
2. Under **Connect an AI tool**, pick your tool and choose **Copy**. The setup has the full path filled in, and if MCP is still off, Copy offers to turn it on.
3. Paste it where the steps say, such as a terminal for Claude Code or Claude Desktop's config file.

On Linux and macOS, install the command first, because AI tools start it to reach your library.

## Where your data lives

| | Library, logs and backups | Settings |
|---|---|---|
| Windows | `%LOCALAPPDATA%\Prompuff\` | `%LOCALAPPDATA%\Prompuff\settings.json` |
| macOS | `~/Library/Application Support/Prompuff/` | `~/Library/Application Support/Prompuff/settings.json` |
| Linux | `~/.local/share/prompuff/` (or `$XDG_DATA_HOME/prompuff/`) | `~/.config/prompuff/settings.json` (or `$XDG_CONFIG_HOME/prompuff/`) |

The library is one SQLite file, `prompuff.db`. Beside it are `logs/` (kept for 14 days) and `backups/`. Settings › Storage shows the folder and has **Open folder**. Prompuff never writes next to the app or inside the AppImage, so updates and reinstalls leave your prompts alone.

## Privacy

- No account, telemetry, analytics or AI calls.
- The only thing Prompuff asks the internet is whether there's a new version, and it sends nothing about your library. You can turn that off in Settings › Updates.
- The `prompuff` command and its MCP server never go online. The MCP server answers only the AI tool that started it, and only while Settings allows it. What it hands over is then up to that tool.
- Logs record prompt IDs and counts, never titles, prompts, notes or variable values.

## Reporting a bug

1. Open Settings › About and choose **Copy diagnostic info**. It copies a short report: versions, settings, folder paths, library counts and recent log lines. It never has your prompts, and any log line that looks like prompt text is left out.
2. Read it over. Folder paths can show your user name, so edit them if you'd rather.
3. Paste it into a [new issue](https://github.com/hazeliscoding/prompuff/issues/new) with what you did and what happened.

Nothing is sent anywhere until you paste it yourself.
