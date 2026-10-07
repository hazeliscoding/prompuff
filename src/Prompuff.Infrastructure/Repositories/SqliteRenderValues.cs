using Microsoft.Data.Sqlite;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

/// <summary>
/// The values last typed into a prompt's variables, so the Render tab can fill them in again. They stay in the local
/// library: they are never exported and never logged.
/// </summary>
public sealed class SqliteRenderValues(SqliteDatabase database)
{
    public async Task<Dictionary<string, string>> LoadAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name, Value FROM RenderValues WHERE PromptId = $id;";
        command.With("$id", SqlValues.Id(promptId));

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    /// <summary>Replaces the prompt's saved values. Empty values aren't stored.</summary>
    public async Task SaveAsync(Guid promptId, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ClearAsync(connection, transaction, promptId, cancellationToken);
        foreach (var (name, value) in values)
        {
            if (value.Length == 0)
            {
                continue;
            }

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO RenderValues (PromptId, Name, Value) VALUES ($id, $name, $value);";
            command.With("$id", SqlValues.Id(promptId)).With("$name", name).With("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ClearAsync(Guid promptId, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await ClearAsync(connection, null, promptId, cancellationToken);
    }

    private static async Task ClearAsync(SqliteConnection connection, SqliteTransaction? transaction, Guid promptId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM RenderValues WHERE PromptId = $id;";
        command.With("$id", SqlValues.Id(promptId));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
