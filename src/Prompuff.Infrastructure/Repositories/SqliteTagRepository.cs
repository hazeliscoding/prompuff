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
        command.CommandText = """
            SELECT t.Name, COUNT(pt.PromptId) AS Uses
            FROM Tags t JOIN PromptTags pt ON pt.TagId = t.Id JOIN Prompts p ON p.Id = pt.PromptId AND p.DeletedAt IS NULL
            GROUP BY t.Id, t.Name
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
