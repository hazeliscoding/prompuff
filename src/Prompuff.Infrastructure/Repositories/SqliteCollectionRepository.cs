using Microsoft.Data.Sqlite;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

public sealed class SqliteCollectionRepository(SqliteDatabase database) : ICollectionRepository
{
    public async Task<IReadOnlyList<CollectionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.Name, COUNT(p.Id)
            FROM Collections c LEFT JOIN Prompts p ON p.CollectionId = c.Id AND p.DeletedAt IS NULL
            GROUP BY c.Id, c.Name
            ORDER BY c.Name COLLATE NOCASE;
            """;
        var result = new List<CollectionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CollectionSummary(reader.ReadId(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return result;
    }

    public Task<Collection?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        QuerySingleAsync("SELECT Id, Name, CreatedAt FROM Collections WHERE Id = $value;", SqlValues.Id(id), cancellationToken);

    public Task<Collection?> FindByNameAsync(string name, CancellationToken cancellationToken = default) =>
        QuerySingleAsync("SELECT Id, Name, CreatedAt FROM Collections WHERE Name = $value COLLATE NOCASE;", name, cancellationToken);

    public async Task InsertAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Collections (Id, Name, CreatedAt) VALUES ($id, $name, $created);";
        command.With("$id", SqlValues.Id(collection.Id))
            .With("$name", collection.Name)
            .With("$created", SqlValues.Time(collection.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Collections SET Name = $name WHERE Id = $id;";
        command.With("$name", name).With("$id", SqlValues.Id(id));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE Prompts SET CollectionId = NULL WHERE CollectionId = $id;
                DELETE FROM Collections WHERE Id = $id;
                """;
            command.With("$id", SqlValues.Id(id));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Collection?> QuerySingleAsync(string sql, string value, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.With("$value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new Collection { Id = reader.ReadId(0), Name = reader.GetString(1), CreatedAt = reader.ReadTime(2) };
    }
}
