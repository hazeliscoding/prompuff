using Microsoft.Data.Sqlite;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

public sealed class SqlitePromptRepository(SqliteDatabase database) : IPromptRepository
{
    private const string PromptColumns =
        "Id, Title, Description, Body, Notes, IsFavorite, Rating, CollectionId, CreatedAt, UpdatedAt, LastOpenedAt, DeletedAt";

    private const string VersionColumns =
        "Id, PromptId, VersionNumber, Title, Description, Body, Notes, Note, SavedAt";

    public async Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        Prompt prompt;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT {PromptColumns} FROM Prompts WHERE Id = $id;";
            command.With("$id", SqlValues.Id(id));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            prompt = ReadPrompt(reader);
        }

        prompt.SetTags(await ReadTagsAsync(connection, id, cancellationToken));
        return prompt;
    }

    public async Task InsertAsync(Prompt prompt, PromptVersion firstVersion, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                INSERT INTO Prompts ({PromptColumns})
                VALUES ($id, $title, $description, $body, $notes, $favorite, $rating, $collection, $created, $updated, $opened, $deleted);
                """;
            AddPromptParameters(command, prompt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteTagsAsync(connection, transaction, prompt, cancellationToken);
        await InsertVersionAsync(connection, transaction, firstVersion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Prompt prompt, IReadOnlyList<PromptVersion> newVersions, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Prompts
                SET Title = $title, Description = $description, Body = $body, Notes = $notes,
                    IsFavorite = $favorite, Rating = $rating, CollectionId = $collection,
                    CreatedAt = $created, UpdatedAt = $updated, LastOpenedAt = $opened, DeletedAt = $deleted
                WHERE Id = $id;
                """;
            AddPromptParameters(command, prompt);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                throw new Application.LibraryException("That prompt no longer exists.");
            }
        }

        await WriteTagsAsync(connection, transaction, prompt, cancellationToken);
        foreach (var version in newVersions)
        {
            await InsertVersionAsync(connection, transaction, version, cancellationToken);
        }

        await DeleteOrphanTagsAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Prompts WHERE Id = $id;";
            command.With("$id", SqlValues.Id(id));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteOrphanTagsAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> PurgeDeletedAsync(DateTimeOffset? deletedBefore, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        int purged;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM Prompts WHERE DeletedAt IS NOT NULL AND ($before IS NULL OR DeletedAt < $before);";
            command.With("$before", SqlValues.TimeOrNull(deletedBefore));
            purged = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await DeleteOrphanTagsAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purged;
    }

    public async Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {VersionColumns} FROM PromptVersions WHERE PromptId = $id ORDER BY VersionNumber DESC;";
        command.With("$id", SqlValues.Id(promptId));

        var versions = new List<PromptVersion>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(new PromptVersion
            {
                Id = reader.ReadId(0),
                PromptId = reader.ReadId(1),
                VersionNumber = reader.GetInt32(2),
                Title = reader.GetString(3),
                Description = reader.ReadTextOrNull(4),
                Body = reader.GetString(5),
                Notes = reader.ReadTextOrNull(6),
                Note = reader.ReadTextOrNull(7),
                SavedAt = reader.ReadTime(8),
            });
        }

        return versions;
    }

    public async Task<int> GetLatestVersionNumberAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(VersionNumber), 0) FROM PromptVersions WHERE PromptId = $id;";
        command.With("$id", SqlValues.Id(promptId));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task MarkOpenedAsync(Guid id, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Prompts SET LastOpenedAt = $opened WHERE Id = $id;";
        command.With("$opened", SqlValues.Time(openedAt)).With("$id", SqlValues.Id(id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(CASE WHEN DeletedAt IS NULL THEN 1 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DeletedAt IS NULL THEN IsFavorite ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN DeletedAt IS NULL AND CollectionId IS NULL THEN 1 ELSE 0 END), 0),
                   (SELECT COUNT(*) FROM PromptVersions v JOIN Prompts p ON p.Id = v.PromptId WHERE p.DeletedAt IS NULL),
                   COALESCE(SUM(CASE WHEN DeletedAt IS NOT NULL THEN 1 ELSE 0 END), 0)
            FROM Prompts;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new LibraryCounts(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
    }

    private static Prompt ReadPrompt(SqliteDataReader reader)
    {
        var prompt = new Prompt
        {
            Id = reader.ReadId(0),
            CreatedAt = reader.ReadTime(8),
            UpdatedAt = reader.ReadTime(9),
            LastOpenedAt = reader.ReadTimeOrNull(10),
            DeletedAt = reader.ReadTimeOrNull(11),
            IsFavorite = reader.GetInt64(5) != 0,
            Rating = reader.ReadIntOrNull(6),
            CollectionId = reader.ReadIdOrNull(7),
        };
        prompt.SetContent(new PromptContent(reader.GetString(1), reader.ReadTextOrNull(2), reader.GetString(3), reader.ReadTextOrNull(4)));
        return prompt;
    }

    private static void AddPromptParameters(SqliteCommand command, Prompt prompt) =>
        command.With("$id", SqlValues.Id(prompt.Id))
            .With("$title", prompt.Title)
            .With("$description", SqlValues.TextOrNull(prompt.Description))
            .With("$body", prompt.Body)
            .With("$notes", SqlValues.TextOrNull(prompt.Notes))
            .With("$favorite", prompt.IsFavorite ? 1 : 0)
            .With("$rating", SqlValues.IntOrNull(prompt.Rating))
            .With("$collection", SqlValues.IdOrNull(prompt.CollectionId))
            .With("$created", SqlValues.Time(prompt.CreatedAt))
            .With("$updated", SqlValues.Time(prompt.UpdatedAt))
            .With("$opened", SqlValues.TimeOrNull(prompt.LastOpenedAt))
            .With("$deleted", SqlValues.TimeOrNull(prompt.DeletedAt));

    private static async Task<IReadOnlyList<string>> ReadTagsAsync(SqliteConnection connection, Guid promptId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.Name FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId
            WHERE pt.PromptId = $id ORDER BY pt.Position, t.Name;
            """;
        command.With("$id", SqlValues.Id(promptId));
        var tags = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tags.Add(reader.GetString(0));
        }

        return tags;
    }

    private static async Task WriteTagsAsync(SqliteConnection connection, SqliteTransaction transaction, Prompt prompt, CancellationToken cancellationToken)
    {
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM PromptTags WHERE PromptId = $id;";
            clear.With("$id", SqlValues.Id(prompt.Id));
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var position = 0; position < prompt.Tags.Count; position++)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Tags (Id, Name) VALUES ($tagId, $name) ON CONFLICT (Name) DO NOTHING;
                INSERT OR IGNORE INTO PromptTags (PromptId, TagId, Position)
                SELECT $promptId, Id, $position FROM Tags WHERE Name = $name;
                """;
            command.With("$tagId", SqlValues.Id(Guid.NewGuid()))
                .With("$name", prompt.Tags[position])
                .With("$promptId", SqlValues.Id(prompt.Id))
                .With("$position", position);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertVersionAsync(SqliteConnection connection, SqliteTransaction transaction, PromptVersion version, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO PromptVersions ({VersionColumns})
            VALUES ($id, $promptId, $number, $title, $description, $body, $notes, $note, $saved);
            """;
        command.With("$id", SqlValues.Id(version.Id))
            .With("$promptId", SqlValues.Id(version.PromptId))
            .With("$number", version.VersionNumber)
            .With("$title", version.Title)
            .With("$description", SqlValues.TextOrNull(version.Description))
            .With("$body", version.Body)
            .With("$notes", SqlValues.TextOrNull(version.Notes))
            .With("$note", SqlValues.TextOrNull(version.Note))
            .With("$saved", SqlValues.Time(version.SavedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task DeleteOrphanTagsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken) =>
        SqliteDatabase.ExecuteAsync(connection, "DELETE FROM Tags WHERE Id NOT IN (SELECT TagId FROM PromptTags);", cancellationToken, transaction);
}
