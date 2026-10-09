# Markdown format

Prompts travel as Markdown files: a small metadata block, the prompt, then optional notes. A workflow travels as one Markdown document that holds its prompts. Export, import, the `.zip` files and the MCP server's prompt resources all use these formats, and they read well on GitHub or in Obsidian.

The 1.0 promise covers this format: it stays compatible, so a file exported by one version imports into every later one.

## A prompt

From [`samples/angular-upgrade-planner.md`](../samples/angular-upgrade-planner.md), shortened:

```markdown
---
title: Angular Upgrade Planner
description: Produces a phased migration plan from the current Angular version to a target, with breaking changes called out per phase.
tags:
  - angular
  - migration
  - copilot
collection: Engineering
favorite: true
rating: 5
---

# Prompt

You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.

For each phase, call out breaking changes and the `ng update` command to run. Package manager: {{package_manager}}. Keep the tone {{tone}}. End with a risk table.

# Notes

Phasing by major version stopped the model from skipping steps. Asking for the exact ng update command made the output immediately actionable.
```

### Frontmatter

The file starts with a `---` line, and the metadata block ends at the next `---` (or `...`) line. Keys ignore case.

| Key | Meaning | Written on export |
|---|---|---|
| `title` | The title. Without one, the file name minus its extension is used. | Always |
| `description` | One line. | When set |
| `tags` | Tag names. Import normalizes them, so `Angular`, `#angular` and ` angular ` are one tag. | When there are any |
| `collection` | The collection's name. Import uses the library's collection with that name, ignoring case, or creates it. | When set |
| `favorite` | `true`, `yes` or `on` make a favorite. Anything else doesn't. | Always |
| `rating` | Usefulness from 1 to 5. Any other value is ignored. | When set |
| `createdAt`, `updatedAt` | ISO 8601 times, such as `2026-10-05T12:00:00Z`. One without an offset is UTC. Written to the second. | Always |

Any other key is ignored, apart from `type: workflow`, which makes the file a workflow (see below).

The reader understands the small part of YAML these files need:

- plain values: `title: Angular Upgrade Planner`
- double quotes, where a backslash escapes the next character and `\n` and `\t` are a line break and a tab: `description: "Rewrites a README: install, run, test."`
- single quotes, with `''` for a quote: `title: 'It''s a title'`
- lists as `- item` lines under the key, or inline as `[angular, "code review"]`; `tags` also takes a comma-separated value, `tags: angular, review`
- block values with `|` (keeps line breaks) or `>` (joins lines with spaces), and `|-` and `>-`
- `# comment` lines, and ` # comment` after a value outside quotes

Indented lines that belong to nothing it reads, such as a nested map, are skipped. A line at the left margin that isn't `key: value` makes the file unreadable.

### Escaping

The writer leaves a value plain when it starts with an ASCII letter or digit, holds only ASCII letters, digits, spaces and `. , ( ) ' & + / ! ? @ -`, and isn't a word YAML would read as something else (`true`, `false`, `yes`, `no`, `on`, `off`, `null`, `~` or a number). Anything else is written in double quotes, with `\`, `"` and tabs escaped. So a title with an accent, a colon or an emoji is quoted:

```markdown
title: "Café menu translator ☕"
description: "Rewrites a README for a first-time contributor: install, run, test, contribute. Keeps the project voice."
```

Line breaks in a value become spaces.

### Sections

- The first `# Prompt` heading starts the body. Text between the metadata block and `# Prompt` is ignored. With no `# Prompt` heading, everything after the metadata block is the body, so a plain Markdown file imports as it is.
- `# Notes` starts the notes. The last `# Notes` heading counts, so a body can have its own `# Notes` heading. When it does, the writer adds another `# Notes` heading after the body, even when there are no notes.
- Both headings are matched ignoring case and spaces around them. Any other heading, `## Notes` included, is part of the body.
- Blank lines at the start and end of the body and the notes are dropped, and so is whitespace at the very end. `{{variables}}` are plain text in the body.

Files are UTF-8 with `\n` line endings. Export writes no byte order mark; import accepts one, and `\r\n` line endings too.

### Import

- Import reads `.md`, `.markdown` and `.txt` files: picked files, a folder and its subfolders (32 levels deep at most, without following links), or a `.zip`.
- Files and folders whose names start with a dot, such as `.obsidian`, and `__MACOSX`, are passed over. Other files that aren't Markdown are listed in the result of a folder import, and passed over in a zip.
- A file is refused, with the reason, when it's over 5 MB, empty, has a NUL character, has a metadata block that's never closed, or has a metadata line that isn't `key: value`. The rest of the import goes on.
- Import takes at most 10,000 files from one zip or folder.
- A prompt whose title and body match one in the library, outside Recently deleted, is skipped and counted. Line endings, blank lines around the body and whitespace at its end don't count as differences.
- Each imported prompt is new and independent: version 1, noted "Imported from Markdown", with the file's times. Version history, remembered values, lineage, IDs and Recently deleted don't travel in Markdown.

## A workflow

From [`samples/workflows/angular-upgrade-start-to-finish.md`](../samples/workflows/angular-upgrade-start-to-finish.md), shortened:

````markdown
---
type: workflow
title: Angular upgrade, start to finish
description: Plan the upgrade, carry it out one phase at a time, then check nothing broke.
---

# Angular upgrade, start to finish

Plan the upgrade, carry it out one phase at a time, then check nothing broke.

## Step 1: Angular Upgrade Planner

```prompt
You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.
```

> **Hands off:** the phased plan. Paste it into {{upgrade_plan}} for the next step.

## Step 2: Angular Upgrade Phase Runner

```prompt
You are upgrading {{repo_name}} to Angular {{target_version}} with {{package_manager}}. This is the plan we agreed on:

{{upgrade_plan}}
```
````

- `type: workflow` in the metadata block marks a workflow. Import picks workflows out by it, wherever they are. `title` and `description` work as they do for a prompt. Without a `title`, the first `# ` heading before the first step is the title, then the file name.
- The `# Title` heading and the paragraph after it are for people reading the file. Prompuff writes them, but reads the description from the metadata block only.
- Each `## ` heading outside a fenced block starts a step. The writer uses `## Step N: Title`, where Title is the step prompt's title. The reader also takes `.`, `-`, `–` or `—` after the number, or a heading with no `Step N` at all. Steps keep the order they're in; the numbers aren't checked.
- A step's prompt is the first fenced block after its heading. The writer fences it with backticks and the word `prompt`, using one more backtick than the longest run of backticks in the prompt, and at least three, so prompts with their own code blocks survive. The reader takes a fence of three or more backticks or tildes, indented up to three spaces, with at most one word after it, and closes it at a fence of the same character that's at least as long.
- The hand-off note is a quote. The writer starts it with `> **Hands off:**` and writes each further line as a `>` line. The reader joins every quoted line in the step, outside the fence, and drops a leading `**Hands off:**`.
- Anything else in a step is ignored.
- A document with no steps, a step with no fenced prompt, or a fence that's never closed is refused.

Only each step prompt's title and body travel. On import, a step uses the library's prompt with the same title and body, outside Recently deleted, or creates one with just those, noted "Imported with the “Name” workflow". A workflow with the same name and the same prompts in the same order is skipped. Remembered workflow values never travel.

## The .zip export

```text
prompuff-library-2026-10-08.zip
├── angular-upgrade-planner.md
├── readme-cleanup.md
├── prompt.md
├── prompt-2.md
└── workflows/
    └── angular-upgrade-start-to-finish.md
```

- Prompts sit at the top level, one file each, and those in Recently deleted are left out. Workflows go in `workflows/`, and only Settings › Export everything includes them. The library's Export button and collection exports hold prompts only, because a workflow can span collections.
- A file is named after its title: lowercase ASCII letters and digits, with every other run of characters turned into one `-`, at most 60 characters. A title with no ASCII letters or digits, such as 日本語の要約, becomes `prompt.md`. A name that's already taken, ignoring case, gets `-2`, `-3` and so on.
- Import reads the Markdown files anywhere in the zip, in memory and never unpacked to disk, with the same 5 MB limit for each. It imports every prompt before any workflow, so steps find the prompts from the same zip with their tags, notes and collection. It tells workflows apart by their metadata block, not their folder.

## Compatibility

A file exported by 1.0 imports into every later version with everything it held. A later version can extend the format, such as with a new metadata key, but a new key is optional, and an existing key, heading or section keeps its name and meaning.

`tests/Prompuff.Infrastructure.Tests/Fixtures/markdown-1.0.0/` holds what 1.0's export wrote: seven prompts, one file each, the workflow above under `workflows/`, and all of them in `library.zip`. Between them they use every metadata key, Unicode and emoji, values that need quoting, a body with its own `# Notes` heading and a code fence, and a prompt with only a title and a body. `MarkdownFixtures.cs` says what each one holds, and `MarkdownFixtureTests` imports each file and the zip and checks every field. Like the library fixtures, they're pinned in `tests/pinned-fixtures.sha256` and never change; a release that adds to the format adds its own folder beside this one.
