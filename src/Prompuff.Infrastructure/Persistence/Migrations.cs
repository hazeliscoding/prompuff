namespace Prompuff.Infrastructure.Persistence;

public sealed record Migration(int Version, string Name, string Sql);

/// <summary>
/// Schema history. The runner applies every migration above the file's <c>PRAGMA user_version</c>, in order.
/// Never edit a migration that has shipped; add a new one.
/// </summary>
public static class Migrations
{
    public static IReadOnlyList<Migration> All { get; } =
    [
        new(1, "Initial schema", """
            CREATE TABLE Collections (
                Id          TEXT PRIMARY KEY,
                Name        TEXT NOT NULL,
                CreatedAt   TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IX_Collections_Name ON Collections (Name COLLATE NOCASE);

            CREATE TABLE Prompts (
                Id            TEXT PRIMARY KEY,
                Title         TEXT NOT NULL,
                Description   TEXT NULL,
                Body          TEXT NOT NULL,
                Notes         TEXT NULL,
                IsFavorite    INTEGER NOT NULL DEFAULT 0,
                Rating        INTEGER NULL CHECK (Rating BETWEEN 1 AND 5),
                CollectionId  TEXT NULL REFERENCES Collections (Id) ON DELETE SET NULL,
                CreatedAt     TEXT NOT NULL,
                UpdatedAt     TEXT NOT NULL,
                LastOpenedAt  TEXT NULL
            );
            CREATE INDEX IX_Prompts_CollectionId ON Prompts (CollectionId);
            CREATE INDEX IX_Prompts_UpdatedAt ON Prompts (UpdatedAt);

            CREATE TABLE Tags (
                Id    TEXT PRIMARY KEY,
                Name  TEXT NOT NULL UNIQUE COLLATE NOCASE
            );

            CREATE TABLE PromptTags (
                PromptId  TEXT NOT NULL REFERENCES Prompts (Id) ON DELETE CASCADE,
                TagId     TEXT NOT NULL REFERENCES Tags (Id) ON DELETE CASCADE,
                Position  INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (PromptId, TagId)
            );
            CREATE INDEX IX_PromptTags_TagId ON PromptTags (TagId);

            CREATE TABLE PromptVersions (
                Id             TEXT PRIMARY KEY,
                PromptId       TEXT NOT NULL REFERENCES Prompts (Id) ON DELETE CASCADE,
                VersionNumber  INTEGER NOT NULL,
                Title          TEXT NOT NULL,
                Description    TEXT NULL,
                Body           TEXT NOT NULL,
                Notes          TEXT NULL,
                Note           TEXT NULL,
                SavedAt        TEXT NOT NULL,
                UNIQUE (PromptId, VersionNumber)
            );
            """),
        new(2, "Recently deleted", """
            ALTER TABLE Prompts ADD COLUMN DeletedAt TEXT NULL;
            CREATE INDEX IX_Prompts_DeletedAt ON Prompts (DeletedAt);
            """),

        // Full-text search. Rows are keyed by PromptId rather than rowid, because VACUUM may renumber the rowids of a
        // table without an INTEGER PRIMARY KEY. Triggers keep the index in step with prompts and their tags.
        new(3, "Full-text search", """
            CREATE VIRTUAL TABLE PromptSearch USING fts5(
                PromptId UNINDEXED, Title, Description, Body, Notes, Tags,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            INSERT INTO PromptSearch (PromptId, Title, Description, Body, Notes, Tags)
            SELECT p.Id, p.Title, p.Description, p.Body, p.Notes,
                   (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = p.Id)
            FROM Prompts p;

            CREATE TRIGGER Prompts_SearchInsert AFTER INSERT ON Prompts BEGIN
                INSERT INTO PromptSearch (PromptId, Title, Description, Body, Notes)
                VALUES (new.Id, new.Title, new.Description, new.Body, new.Notes);
            END;

            CREATE TRIGGER Prompts_SearchUpdate AFTER UPDATE OF Title, Description, Body, Notes ON Prompts BEGIN
                UPDATE PromptSearch SET Title = new.Title, Description = new.Description, Body = new.Body, Notes = new.Notes
                WHERE PromptId = new.Id;
            END;

            CREATE TRIGGER Prompts_SearchDelete AFTER DELETE ON Prompts BEGIN
                DELETE FROM PromptSearch WHERE PromptId = old.Id;
            END;

            CREATE TRIGGER PromptTags_SearchInsert AFTER INSERT ON PromptTags BEGIN
                UPDATE PromptSearch
                SET Tags = (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = new.PromptId)
                WHERE PromptId = new.PromptId;
            END;

            CREATE TRIGGER PromptTags_SearchDelete AFTER DELETE ON PromptTags BEGIN
                UPDATE PromptSearch
                SET Tags = (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = old.PromptId)
                WHERE PromptId = old.PromptId;
            END;
            """),
        new(4, "Remembered variable values", """
            CREATE TABLE RenderValues (
                PromptId  TEXT NOT NULL REFERENCES Prompts (Id) ON DELETE CASCADE,
                Name      TEXT NOT NULL,
                Value     TEXT NOT NULL,
                PRIMARY KEY (PromptId, Name)
            );
            """),

        // A copy keeps its lineage while the parent exists. Removing the parent for good clears the link, and the copy
        // lives on as an independent prompt.
        new(5, "Prompt lineage", """
            ALTER TABLE Prompts ADD COLUMN ParentPromptId TEXT NULL REFERENCES Prompts (Id) ON DELETE SET NULL;
            CREATE INDEX IX_Prompts_ParentPromptId ON Prompts (ParentPromptId);
            """),

        // A step points at a prompt in the library rather than copying it, so editing the prompt edits the workflow.
        // Removing a prompt for good removes its steps. Values filled in on the Run tab are kept per workflow, like
        // RenderValues are per prompt.
        new(6, "Workflows", """
            CREATE TABLE Workflows (
                Id           TEXT PRIMARY KEY,
                Name         TEXT NOT NULL,
                Description  TEXT NULL,
                CreatedAt    TEXT NOT NULL,
                UpdatedAt    TEXT NOT NULL
            );

            CREATE TABLE WorkflowSteps (
                Id          TEXT PRIMARY KEY,
                WorkflowId  TEXT NOT NULL REFERENCES Workflows (Id) ON DELETE CASCADE,
                Position    INTEGER NOT NULL,
                PromptId    TEXT NOT NULL REFERENCES Prompts (Id) ON DELETE CASCADE,
                Note        TEXT NULL
            );
            CREATE INDEX IX_WorkflowSteps_WorkflowId ON WorkflowSteps (WorkflowId, Position);
            CREATE INDEX IX_WorkflowSteps_PromptId ON WorkflowSteps (PromptId);

            CREATE TABLE WorkflowValues (
                WorkflowId  TEXT NOT NULL REFERENCES Workflows (Id) ON DELETE CASCADE,
                Name        TEXT NOT NULL,
                Value       TEXT NOT NULL,
                PRIMARY KEY (WorkflowId, Name)
            );
            """),

        // Large libraries. The search triggers found a prompt's index row by PromptId, which FTS5 can't index, so every
        // save, favorite and tag change scanned the whole index (over 100 ms a favorite with 10,000 prompts). PromptSearchRows
        // maps each prompt to its index row so the triggers, and search, go by rowid; FTS5 keeps rowids in INTEGER PRIMARY
        // KEY columns, which VACUUM leaves alone. An update that changes no indexed text no longer touches the index.
        // Listing prompts read their tags one prompt at a time and pulled whole rows, bodies included, off disk: TagNames
        // keeps each prompt's tags in order, joined by U+001F, and IX_Prompts_Summary holds everything a library card
        // shows, so a list never reads the bodies. It replaces IX_Prompts_DeletedAt, which it starts with.
        new(7, "Large libraries", """
            CREATE TABLE PromptSearchRows (
                SearchRowId  INTEGER PRIMARY KEY,
                PromptId     TEXT NOT NULL UNIQUE
            );

            -- Each prompt keeps its first index row; extra rows, and rows for prompts that are gone, are dropped. A prompt
            -- missing from the index gets a row.
            INSERT OR IGNORE INTO PromptSearchRows (SearchRowId, PromptId)
            SELECT rowid, PromptId FROM PromptSearch WHERE PromptId IN (SELECT Id FROM Prompts) ORDER BY rowid;
            DELETE FROM PromptSearch WHERE rowid NOT IN (SELECT SearchRowId FROM PromptSearchRows);
            INSERT INTO PromptSearch (PromptId, Title, Description, Body, Notes, Tags)
            SELECT p.Id, p.Title, p.Description, p.Body, p.Notes,
                   (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = p.Id)
            FROM Prompts p WHERE p.Id NOT IN (SELECT PromptId FROM PromptSearchRows);
            INSERT INTO PromptSearchRows (SearchRowId, PromptId)
            SELECT rowid, PromptId FROM PromptSearch WHERE rowid NOT IN (SELECT SearchRowId FROM PromptSearchRows);

            DROP TRIGGER Prompts_SearchInsert;
            DROP TRIGGER Prompts_SearchUpdate;
            DROP TRIGGER Prompts_SearchDelete;
            DROP TRIGGER PromptTags_SearchInsert;
            DROP TRIGGER PromptTags_SearchDelete;

            CREATE TRIGGER Prompts_SearchInsert AFTER INSERT ON Prompts BEGIN
                INSERT INTO PromptSearch (PromptId, Title, Description, Body, Notes)
                VALUES (new.Id, new.Title, new.Description, new.Body, new.Notes);
                INSERT OR REPLACE INTO PromptSearchRows (SearchRowId, PromptId) VALUES (last_insert_rowid(), new.Id);
            END;

            CREATE TRIGGER Prompts_SearchUpdate AFTER UPDATE OF Title, Description, Body, Notes ON Prompts
            WHEN old.Title IS NOT new.Title OR old.Description IS NOT new.Description
                 OR old.Body IS NOT new.Body OR old.Notes IS NOT new.Notes
            BEGIN
                UPDATE PromptSearch SET Title = new.Title, Description = new.Description, Body = new.Body, Notes = new.Notes
                WHERE rowid = (SELECT SearchRowId FROM PromptSearchRows WHERE PromptId = new.Id);
            END;

            CREATE TRIGGER Prompts_SearchDelete AFTER DELETE ON Prompts BEGIN
                DELETE FROM PromptSearch WHERE rowid = (SELECT SearchRowId FROM PromptSearchRows WHERE PromptId = old.Id);
                DELETE FROM PromptSearchRows WHERE PromptId = old.Id;
            END;

            CREATE TRIGGER PromptTags_SearchInsert AFTER INSERT ON PromptTags BEGIN
                UPDATE PromptSearch
                SET Tags = (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = new.PromptId)
                WHERE rowid = (SELECT SearchRowId FROM PromptSearchRows WHERE PromptId = new.PromptId);
            END;

            CREATE TRIGGER PromptTags_SearchDelete AFTER DELETE ON PromptTags BEGIN
                UPDATE PromptSearch
                SET Tags = (SELECT group_concat(t.Name, ' ') FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = old.PromptId)
                WHERE rowid = (SELECT SearchRowId FROM PromptSearchRows WHERE PromptId = old.PromptId);
            END;

            ALTER TABLE Prompts ADD COLUMN TagNames TEXT NULL;
            UPDATE Prompts SET TagNames = (SELECT group_concat(Name, char(31)) FROM (
                SELECT t.Name FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = Prompts.Id ORDER BY pt.Position, t.Name));

            CREATE TRIGGER PromptTags_NamesInsert AFTER INSERT ON PromptTags BEGIN
                UPDATE Prompts SET TagNames = (SELECT group_concat(Name, char(31)) FROM (
                    SELECT t.Name FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = new.PromptId ORDER BY pt.Position, t.Name))
                WHERE Id = new.PromptId;
            END;

            CREATE TRIGGER PromptTags_NamesDelete AFTER DELETE ON PromptTags BEGIN
                UPDATE Prompts SET TagNames = (SELECT group_concat(Name, char(31)) FROM (
                    SELECT t.Name FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = old.PromptId ORDER BY pt.Position, t.Name))
                WHERE Id = old.PromptId;
            END;

            DROP INDEX IX_Prompts_DeletedAt;
            CREATE INDEX IX_Prompts_Summary ON Prompts (
                DeletedAt, Id, UpdatedAt, Title, Description, IsFavorite, Rating, CollectionId, CreatedAt, LastOpenedAt, TagNames);
            """),
    ];

    public static int LatestVersion => All[^1].Version;
}
