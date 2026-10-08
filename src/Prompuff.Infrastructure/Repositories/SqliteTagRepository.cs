using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

public sealed class SqliteTagRepository(SqliteDatabase database) : ITagRepository
{
    public async Task<IReadOnlyList<TagSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Every use, less the uses by prompts in Recently deleted: an index count plus a few lookups, instead of a
        // lookup per use. CROSS JOIN keeps SQLite starting from the few deleted prompts.
        command.CommandText = """
            SELECT t.Name, used.n - COALESCE(deleted.n, 0) AS Uses
            FROM (SELECT TagId, COUNT(*) AS n FROM PromptTags GROUP BY TagId) used
            JOIN Tags t ON t.Id = used.TagId
            LEFT JOIN (SELECT pt.TagId, COUNT(*) AS n FROM Prompts p CROSS JOIN PromptTags pt ON pt.PromptId = p.Id
                       WHERE p.DeletedAt IS NOT NULL GROUP BY pt.TagId) deleted ON deleted.TagId = used.TagId
            WHERE used.n > COALESCE(deleted.n, 0)
            ORDER BY Uses DESC, t.Name;
            """;
        var result = new List<TagSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new TagSummary(reader.GetString(0), reader.GetInt32(1)));
        }

        return result;
    }
}
