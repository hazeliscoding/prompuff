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
    ];

    public static int LatestVersion => All[^1].Version;
}
