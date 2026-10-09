# Library format

Prompuff keeps the whole library in one SQLite 3 database, `prompuff.db`. This page describes the file at schema version 7, which every release from v0.9.0 on writes, and how older files are brought up to date. It's for anyone reading the file with other tools, and for whoever changes it next.

## Where it lives

| | Data folder | Settings |
|---|---|---|
| Windows | `%LOCALAPPDATA%\Prompuff\` | `%LOCALAPPDATA%\Prompuff\settings.json` |
| macOS | `~/Library/Application Support/Prompuff/` | `~/Library/Application Support/Prompuff/settings.json` |
| Linux | `$XDG_DATA_HOME/prompuff/` (default `~/.local/share/prompuff/`) | `$XDG_CONFIG_HOME/prompuff/settings.json` (default `~/.config/prompuff/`) |

`PROMPUFF_DATA_DIR` puts both in one folder of your choice, which keeps development runs and tests away from a real library. A relative path in `XDG_DATA_HOME` or `XDG_CONFIG_HOME` is ignored, as the XDG spec asks.

The data folder holds:

| Name | What it is |
|---|---|
| `prompuff.db` | The library. |
| `prompuff.db-wal`, `prompuff.db-shm` | SQLite's write-ahead log and its index. Recent changes can sit in the `-wal` file until SQLite folds them in, so while Prompuff runs, copy a backup rather than `prompuff.db` alone. |
| `backups/` | Copies of the library. Each is a single `.db` file with a rollback journal, so there's no `-wal` beside it. |
| `prompuff.lock` | An empty file the running app locks, so only one copy opens a library at a time. The operating system lets go of it when Prompuff exits or crashes. The `prompuff` command doesn't take it. |
| `logs/` | `prompuff-yyyyMMdd.log` from the app and `prompuff-cli-yyyyMMdd.log` from the command, kept for 14 days. They record prompt IDs and counts, never prompt text. |
| `cli/` | On Linux and macOS, the copy of the `prompuff` command that `~/.local/bin/prompuff` links to, once you install it. |

Backup names carry the kind and the UTC time:

- `prompuff-daily-20261007-091500.db`: made the first time Prompuff opens each day, and checked hourly while it runs. The newest 30 are kept.
- `prompuff-schema4-20261007-091500.db`: made before a migration changes a library at schema 4. Kept until you delete it.
- `prompuff-before-restore-20261007-091500.db`: made before Settings › Storage restores a backup. Kept until you delete it.

A `-2` or `-3` suffix keeps two backups from the same second apart.

## Values

- **IDs** are GUIDs stored as lowercase text with hyphens, such as `3f2504e0-4f89-11d3-9a0c-0305e82c3301`.
- **Times** are ISO 8601 UTC text with seven decimal places, such as `2026-10-07T09:05:17.4410382Z`. Prompuff reads any ISO 8601 time and treats one without an offset as UTC.
- **Booleans** are `0` or `1`.
- **Text** uses `\n` line endings. Titles and descriptions are one line: Prompuff turns line breaks in them into spaces. A blank description or blank notes are stored as `NULL`.

Prompuff opens the file with foreign keys on and in WAL mode (`PRAGMA journal_mode = WAL`). SQLite leaves foreign keys off unless each connection turns them on, and the cascades below rely on them, so another tool that writes to the library should run `PRAGMA foreign_keys = ON` first.

## Tables

### Prompts

One row per prompt, holding its current content, which always matches its newest version.

| Column | Type | Meaning |
|---|---|---|
| `Id` | `TEXT PRIMARY KEY` | |
| `Title` | `TEXT NOT NULL` | One line, never empty. A blank title is saved as "Untitled prompt". |
| `Description` | `TEXT NULL` | One line. |
| `Body` | `TEXT NOT NULL` | The prompt, with its `{{variable}}` placeholders. |
| `Notes` | `TEXT NULL` | "Why this worked", trimmed. |
| `IsFavorite` | `INTEGER NOT NULL DEFAULT 0` | |
| `Rating` | `INTEGER NULL CHECK (Rating BETWEEN 1 AND 5)` | Usefulness. `NULL` is unrated. |
| `CollectionId` | `TEXT NULL`, references `Collections (Id) ON DELETE SET NULL` | `NULL` is Uncategorized. |
| `CreatedAt` | `TEXT NOT NULL` | |
| `UpdatedAt` | `TEXT NOT NULL` | The last change to its content. Metadata changes leave it alone. |
| `LastOpenedAt` | `TEXT NULL` | The last time it was opened, for Recent. |
| `DeletedAt` | `TEXT NULL` | When it moved to Recently deleted. Added in schema 2. |
| `ParentPromptId` | `TEXT NULL`, references `Prompts (Id) ON DELETE SET NULL` | The prompt it was duplicated from. Added in schema 5. |
| `TagNames` | `TEXT NULL` | The prompt's tag names in order, joined by U+001F, so listing prompts needs no tag lookups. Triggers on `PromptTags` keep it current; don't write it yourself. Added in schema 7. |

Indexes: `IX_Prompts_CollectionId`, `IX_Prompts_UpdatedAt` and `IX_Prompts_ParentPromptId`, one column each, and `IX_Prompts_Summary` on `(DeletedAt, Id, UpdatedAt, Title, Description, IsFavorite, Rating, CollectionId, CreatedAt, LastOpenedAt, TagNames)`, which holds everything a library card shows, so a list never reads the bodies. Schema 7 replaced `IX_Prompts_DeletedAt` with it.

### PromptVersions

| Column | Type | Meaning |
|---|---|---|
| `Id` | `TEXT PRIMARY KEY` | |
| `PromptId` | `TEXT NOT NULL`, references `Prompts (Id) ON DELETE CASCADE` | |
| `VersionNumber` | `INTEGER NOT NULL`, `UNIQUE (PromptId, VersionNumber)` | Counts from 1. The highest is the current content. |
| `Title`, `Description`, `Body`, `Notes` | as in `Prompts` | The content as saved. |
| `Note` | `TEXT NULL` | A generated summary, such as "First version" or "Edited body (+3 −1 lines)". |
| `SavedAt` | `TEXT NOT NULL` | |

### Collections

| Column | Type | Meaning |
|---|---|---|
| `Id` | `TEXT PRIMARY KEY` | |
| `Name` | `TEXT NOT NULL` | Trimmed, with inner spaces collapsed, at most 60 characters. The unique index `IX_Collections_Name` uses `COLLATE NOCASE`, so names differ by more than case. |
| `CreatedAt` | `TEXT NOT NULL` | |

### Tags and PromptTags

`Tags` has `Id TEXT PRIMARY KEY` and `Name TEXT NOT NULL UNIQUE COLLATE NOCASE`. Names are normalized before they're stored: trimmed, lowercased, without a leading `#`, with whitespace and commas inside turned into `-`, and at most 40 characters. A tag that no prompt uses is deleted. Prompts in Recently deleted still count.

`PromptTags` links them: `PromptId` and `TagId`, each `TEXT NOT NULL` and `ON DELETE CASCADE`, with `PRIMARY KEY (PromptId, TagId)`, and `Position INTEGER NOT NULL DEFAULT 0`, the order the tags were added in, from 0. Index: `IX_PromptTags_TagId`. The triggers `PromptTags_NamesInsert` and `PromptTags_NamesDelete` keep `Prompts.TagNames` in step.

### RenderValues

Added in schema 4. The values last typed into a prompt's variables, so the Render tab can fill them in again: `PromptId` (`ON DELETE CASCADE`), `Name` and `Value`, all `TEXT NOT NULL`, with `PRIMARY KEY (PromptId, Name)`. Names are case-sensitive, empty values aren't stored, and values are never exported or logged.

### Workflows, WorkflowSteps and WorkflowValues

Added in schema 6.

`Workflows` has `Id TEXT PRIMARY KEY`, `Name TEXT NOT NULL` (one line, at most 80 characters, "Untitled workflow" when blank), `Description TEXT NULL` (one line), and `CreatedAt` and `UpdatedAt`.

`WorkflowSteps` has `Id TEXT PRIMARY KEY`, `WorkflowId` (`ON DELETE CASCADE`), `Position INTEGER NOT NULL` (from 0, rewritten whenever the workflow is saved), `PromptId TEXT NOT NULL` referencing `Prompts (Id) ON DELETE CASCADE`, and `Note TEXT NULL`, the hand-off note, which keeps its line breaks. A step points at a prompt instead of copying it, and a prompt can appear in several steps. Indexes: `IX_WorkflowSteps_WorkflowId` on `(WorkflowId, Position)` and `IX_WorkflowSteps_PromptId`.

`WorkflowValues` is `RenderValues` for a workflow: `WorkflowId` (`ON DELETE CASCADE`), `Name` and `Value`, with `PRIMARY KEY (WorkflowId, Name)`.

### PromptSearch

Added in schema 3. An FTS5 index of every prompt, including those in Recently deleted:

```sql
CREATE VIRTUAL TABLE PromptSearch USING fts5(
    PromptId UNINDEXED, Title, Description, Body, Notes, Tags,
    tokenize = 'unicode61 remove_diacritics 2'
);
```

- `Tags` holds the prompt's tag names, separated by spaces. The `PromptSearch_data`, `_idx`, `_content`, `_docsize` and `_config` tables belong to FTS5.
- `PromptSearchRows` (added in schema 7) maps each prompt to its index row: `SearchRowId INTEGER PRIMARY KEY` and `PromptId TEXT NOT NULL UNIQUE`. The triggers and search reach a prompt's row by rowid through it, instead of scanning the index for a `PromptId`. FTS5 keeps its rowids in `INTEGER PRIMARY KEY` columns, which `VACUUM` leaves alone.
- Five triggers keep the index current, so anything that writes to `Prompts` or `PromptTags` updates it: `Prompts_SearchInsert`, `Prompts_SearchUpdate` (only when the title, description, body or notes actually change), `Prompts_SearchDelete`, `PromptTags_SearchInsert` and `PromptTags_SearchDelete`.
- The tokenizer ignores case and accents, so "cafe" finds "Café". Search makes each word a quoted prefix phrase and ranks with `bm25(PromptSearch, 0.0, 10.0, 4.0, 1.0, 2.0, 6.0)`, which favors the title, then tags, then the description. A `#tag` word must match a tag exactly, and a word with no letters or digits, such as an emoji, is matched as written with `LIKE`.

## Schema versions and migrations

`PRAGMA user_version` holds the schema version. Whenever the app, the `prompuff` command or its MCP server opens the library, `SqliteDatabase.InitializeAsync`:

1. Creates the file if there isn't one, and switches it to WAL.
2. Reads `user_version`. A number above the newest migration it knows means a newer Prompuff saved the library, so it stops with "This library was saved by a newer version of Prompuff. Update Prompuff to open it." and doesn't migrate or back up anything.
3. If migrations are pending and the file isn't new, copies it to `backups/prompuff-schemaN-….db` with SQLite's backup API.
4. Applies each pending migration in its own `BEGIN IMMEDIATE` transaction, with `PRAGMA user_version = N` in the same transaction. It reads the version again once it holds the write lock, because the app, the command and the MCP server can open the library at the same moment, and only one of them may migrate it.

Any other failure ends with "Prompuff couldn't open your library.", with the details behind Show details.

The migrations are in `src/Prompuff.Infrastructure/Persistence/Migrations.cs`. A migration never changes once it has shipped; a schema change is a new migration at the end of the list.

| Version | Migration | First shipped in |
|---|---|---|
| 1 | Initial schema: `Collections`, `Prompts`, `Tags`, `PromptTags` and `PromptVersions` | v0.1.0 |
| 2 | Recently deleted: `Prompts.DeletedAt` | v0.2.0 |
| 3 | Full-text search: `PromptSearch` and its triggers, filled from the prompts already there | v0.2.0 |
| 4 | Remembered values: `RenderValues` | v0.2.0 |
| 5 | Lineage: `Prompts.ParentPromptId` | v0.4.0 |
| 6 | Workflows: `Workflows`, `WorkflowSteps` and `WorkflowValues` | v0.6.0 |
| 7 | Large libraries: `PromptSearchRows`, `Prompts.TagNames` and `IX_Prompts_Summary`, new search triggers, and a repair of any search rows that drifted from the prompts | v0.9.0 |

So a library left by a release is at one of five schemas: 1 (v0.1.0 to v0.1.2), 4 (v0.2.0 to v0.3.0), 5 (v0.4.0 to v0.5.0), 6 (v0.6.0 to v0.8.0) or 7 (v0.9.0 on).

## What creates a version

- Saving a change to the title, description, body or notes adds a version. A save that changes none of them, once line endings and blank fields are normalized, adds nothing.
- Restoring an old version adds a new one with its content, noted "Restored v2". If the current content isn't already the newest version, it's saved first as "Saved before restoring".
- Duplicating a prompt, or one of its versions, makes a new prompt at version 1, noted "Duplicated from “Title”" or "Duplicated from v2 of “Title”", with `ParentPromptId` set.
- Importing makes a new prompt at version 1, noted "Imported from Markdown", or "Imported with the “Name” workflow" for a workflow step's prompt.
- Favorite, rating, collection and tags are metadata. They save at once, add no version and leave `UpdatedAt` alone. So do moving a prompt to Recently deleted and back, which only sets `DeletedAt`, and opening it, which sets `LastOpenedAt`.
- Workflows have no versions.

## Removing things

- Deleting a prompt sets `DeletedAt`. Prompuff removes the row for good once it has waited 30 days, the next time the app opens, or when you empty Recently deleted.
- Removing a prompt row takes its versions, tag links, remembered values and workflow steps with it, and a trigger drops its search row. Copies of it stay, with `ParentPromptId` set to `NULL`, and tags no prompt uses any more are deleted.
- Deleting a collection leaves its prompts Uncategorized. Deleting a workflow removes its steps and values, and leaves its prompts in the library.

## Compatibility

The 1.0 promise is format stability:

- Every later version opens a 1.0 library, and a library from any release since v0.1.0, by migrating it forward after a backup.
- Every migration is tested from every released schema version, using the fixtures below.
- A library from a newer version is refused, not changed, so you can update Prompuff and open it.

## Test fixtures

`tests/Prompuff.Infrastructure.Tests/Fixtures/` holds one library for each released schema: `schema-1.db`, `schema-4.db`, `schema-5.db`, `schema-6.db` and `schema-7.db`. Each was made by running the migrations up to that version and no further, then filling the file with raw SQL in that schema: prompts with Unicode titles and `{{variables}}`, several versions each, tags, collections, favorites and ratings, and, where the schema has them, a prompt in Recently deleted, remembered values, a duplicate with lineage, and workflows with steps and values. Each uses a rollback journal, so it's one file.

`LibraryFixtureTests` copies each fixture to a temporary folder, opens it with `SqliteDatabase.InitializeAsync` as the app does, and checks the schema version, the backup, and every piece of data through the real repositories and services, search included. It also checks that today's migrations up to N still build exactly the schema in `schema-N.db`, which catches an edited migration, and that a library from a newer version is refused.

The normal test run only reads the fixtures. `SchemaFixtures.cs` says what each one holds, and writes them. When a new schema version ships:

1. Add it to `SchemaFixtures.ReleasedVersions`, and add whatever its migration makes possible to `SchemaFixtures.Library`, only for that version and later.
2. Write its fixture. In PowerShell, set `$env:PROMPUFF_WRITE_FIXTURES = "8"` instead, and remove it afterwards.

   ```bash
   PROMPUFF_WRITE_FIXTURES=8 dotnet test tests/Prompuff.Infrastructure.Tests --filter "FullyQualifiedName~Write_fixtures"
   ```

3. Commit the new `schema-8.db` and its line in `tests/pinned-fixtures.sha256` with the migration.

A test fails while the newest migration has no fixture.

### Pinned for good

A fixture records what a release left behind, so it never changes. Every file in a test project's `Fixtures` folder is pinned by its SHA-256 in `tests/pinned-fixtures.sha256`, which `sha256sum -c` can check from the `tests` folder too. `PinnedFixtureTests` fails when a pinned file changes or goes missing, or when a fixture isn't pinned. `Write_fixtures` pins each file it writes and refuses to overwrite a pinned one. If a later version can't read an old fixture, the fix goes in the code, never in the fixture.
