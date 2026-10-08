using System.Text;
using Microsoft.Data.Sqlite;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

/// <summary>
/// Full-text search over the <c>PromptSearch</c> FTS5 index. Every word in the query must start a word in the title,
/// description, body, notes or tags, ignoring case and accents, and results are ranked with title matches first.
/// A word starting with <c>#</c> must match a tag exactly. A word with no letters or digits, such as <c>-&gt;</c>,
/// gives the index nothing to look up, so it is matched literally with LIKE.
/// </summary>
/// <remarks>
/// Every column read comes from <c>IX_Prompts_Summary</c>, tags included, so listing even a large library reads no
/// bodies and runs no query per prompt. Index matches reach their prompts through <c>PromptSearchRows</c> by rowid.
/// </remarks>
public sealed class SqlitePromptSearch(SqliteDatabase database) : IPromptSearch
{
    private const char TagSeparator = '\u001F';

    public async Task<IReadOnlyList<PromptSummary>> SearchAsync(PromptQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, query);

        var results = new List<PromptSummary>();
        var tagLists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader, tagLists));
        }

        return results;
    }

    /// <summary>Builds the statement for <paramref name="query"/>. Tests read its plan to keep large libraries fast.</summary>
    internal static SqliteCommand CreateCommand(SqliteConnection connection, PromptQuery query)
    {
        var command = connection.CreateCommand();
        var terms = SplitTerms(query.Text);
        var words = terms.Where(term => !IsTagTerm(term) && term.Any(char.IsLetterOrDigit)).ToList();

        var sql = new StringBuilder("""
            SELECT p.Id, p.Title, p.Description, p.IsFavorite, p.Rating, p.CollectionId, p.CreatedAt, p.UpdatedAt, p.LastOpenedAt, p.DeletedAt, p.TagNames
            """);
        sql.Append(words.Count > 0
            ? " FROM PromptSearch JOIN PromptSearchRows r ON r.SearchRowId = PromptSearch.rowid JOIN Prompts p ON p.Id = r.PromptId"
            : " FROM Prompts p");

        sql.Append(query.Filter == PromptFilterKind.Deleted ? " WHERE p.DeletedAt IS NOT NULL" : " WHERE p.DeletedAt IS NULL");
        if (words.Count > 0)
        {
            // Each word becomes a quoted prefix phrase, so FTS5 syntax typed into the search box is never interpreted.
            sql.Append(" AND PromptSearch MATCH $match");
            command.With("$match", string.Join(' ', words.Select(word => $"\"{word.Replace("\"", "\"\"")}\"*")));
        }

        switch (query.Filter)
        {
            case PromptFilterKind.Favorites:
                sql.Append(" AND p.IsFavorite = 1");
                break;
            case PromptFilterKind.Collection when query.CollectionId is { } collectionId:
                sql.Append(" AND p.CollectionId = $collection");
                command.With("$collection", SqlValues.Id(collectionId));
                break;
            case PromptFilterKind.Uncategorized:
                sql.Append(" AND p.CollectionId IS NULL");
                break;
            case PromptFilterKind.Tag when TagName.Normalize(query.Tag) is { } tag:
                sql.Append(" AND p.Id IN (SELECT pt.PromptId FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE t.Name = $filterTag)");
                command.With("$filterTag", tag);
                break;
        }

        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            if (IsTagTerm(term))
            {
                sql.Append($" AND p.Id IN (SELECT pt.PromptId FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE t.Name = $tag{i})");
                command.With($"$tag{i}", TagName.Normalize(term)!);
            }
            else if (!words.Contains(term))
            {
                sql.Append($"""
                     AND (p.Title LIKE $term{i} ESCAPE '\' OR p.Description LIKE $term{i} ESCAPE '\'
                          OR p.Body LIKE $term{i} ESCAPE '\' OR p.Notes LIKE $term{i} ESCAPE '\')
                    """);
                command.With($"$term{i}", "%" + EscapeLike(term) + "%");
            }
        }

        sql.Append(" ORDER BY ");
        if (words.Count > 0)
        {
            // Column weights in table order: PromptId (not indexed), Title, Description, Body, Notes, Tags.
            sql.Append("bm25(PromptSearch, 0.0, 10.0, 4.0, 1.0, 2.0, 6.0), ");
        }

        const string activity = "MAX(p.UpdatedAt, COALESCE(p.LastOpenedAt, p.UpdatedAt)) DESC";
        sql.Append(query.Filter switch
        {
            PromptFilterKind.Recent => activity,
            PromptFilterKind.Deleted => "p.DeletedAt DESC",
            _ => query.Sort switch
            {
                PromptSort.Title => "p.Title COLLATE NOCASE, p.UpdatedAt DESC",
                PromptSort.Usefulness => "COALESCE(p.Rating, 0) DESC, p.UpdatedAt DESC",
                PromptSort.RecentActivity => activity,
                _ => "p.UpdatedAt DESC",
            },
        });

        if (query.Filter == PromptFilterKind.Recent)
        {
            sql.Append($" LIMIT {PromptQuery.RecentLimit}");
        }

        command.CommandText = sql.Append(';').ToString();
        return command;
    }

    private static PromptSummary Read(SqliteDataReader reader, Dictionary<string, IReadOnlyList<string>> tagLists) => new()
    {
        Id = reader.ReadId(0),
        Title = reader.GetString(1),
        Description = reader.ReadTextOrNull(2),
        IsFavorite = reader.GetInt64(3) != 0,
        Rating = reader.ReadIntOrNull(4),
        CollectionId = reader.ReadIdOrNull(5),
        CreatedAt = reader.ReadTime(6),
        UpdatedAt = reader.ReadTime(7),
        LastOpenedAt = reader.ReadTimeOrNull(8),
        DeletedAt = reader.ReadTimeOrNull(9),
        Tags = reader.ReadTextOrNull(10) is { } names ? TagList(names, tagLists) : [],
    };

    /// <summary>Prompts with the same tags share one list, which keeps a search of thousands of prompts small.</summary>
    private static IReadOnlyList<string> TagList(string names, Dictionary<string, IReadOnlyList<string>> tagLists)
    {
        if (!tagLists.TryGetValue(names, out var tags))
        {
            tags = names.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries);
            tagLists[names] = tags;
        }

        return tags;
    }

    private static bool IsTagTerm(string term) => term.StartsWith('#') && TagName.Normalize(term) is not null;

    private static List<string> SplitTerms(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
