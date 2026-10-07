using System.Text;
using Microsoft.Data.Sqlite;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Repositories;

/// <summary>
/// Search with SQL LIKE. Every word in the query must appear in the title, description, body, notes or a tag.
/// A word starting with <c>#</c> must match a tag exactly. Titles that match the first word rank first.
/// </summary>
public sealed class SqlitePromptSearch(SqliteDatabase database) : IPromptSearch
{
    private const char TagSeparator = '\u001F';

    public async Task<IReadOnlyList<PromptSummary>> SearchAsync(PromptQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder($"""
            SELECT p.Id, p.Title, p.Description, p.IsFavorite, p.Rating, p.CollectionId, p.CreatedAt, p.UpdatedAt, p.LastOpenedAt,
                   (SELECT group_concat(Name, char(31)) FROM (
                        SELECT t.Name FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId
                        WHERE pt.PromptId = p.Id ORDER BY pt.Position, t.Name)) AS TagNames
            FROM Prompts p
            WHERE 1 = 1
            """);

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
                sql.Append(" AND EXISTS (SELECT 1 FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = p.Id AND t.Name = $filterTag)");
                command.With("$filterTag", tag);
                break;
        }

        var terms = SplitTerms(query.Text);
        for (var i = 0; i < terms.Count; i++)
        {
            var term = terms[i];
            if (term.StartsWith('#') && TagName.Normalize(term) is { } tagTerm)
            {
                sql.Append($" AND EXISTS (SELECT 1 FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId WHERE pt.PromptId = p.Id AND t.Name = $tag{i})");
                command.With($"$tag{i}", tagTerm);
                continue;
            }

            sql.Append($"""
                 AND (p.Title LIKE $term{i} ESCAPE '\' OR p.Description LIKE $term{i} ESCAPE '\'
                      OR p.Body LIKE $term{i} ESCAPE '\' OR p.Notes LIKE $term{i} ESCAPE '\'
                      OR EXISTS (SELECT 1 FROM PromptTags pt JOIN Tags t ON t.Id = pt.TagId
                                 WHERE pt.PromptId = p.Id AND t.Name LIKE $term{i} ESCAPE '\'))
                """);
            command.With($"$term{i}", "%" + EscapeLike(term) + "%");
        }

        sql.Append(" ORDER BY ");
        if (terms.Count > 0 && !terms[0].StartsWith('#'))
        {
            sql.Append("CASE WHEN p.Title LIKE $term0 ESCAPE '\\' THEN 0 ELSE 1 END, ");
        }

        const string activity = "MAX(p.UpdatedAt, COALESCE(p.LastOpenedAt, p.UpdatedAt)) DESC";
        sql.Append(query.Filter == PromptFilterKind.Recent ? activity : query.Sort switch
        {
            PromptSort.Title => "p.Title COLLATE NOCASE, p.UpdatedAt DESC",
            PromptSort.Usefulness => "COALESCE(p.Rating, 0) DESC, p.UpdatedAt DESC",
            PromptSort.RecentActivity => activity,
            _ => "p.UpdatedAt DESC",
        });

        if (query.Filter == PromptFilterKind.Recent)
        {
            sql.Append($" LIMIT {PromptQuery.RecentLimit}");
        }

        command.CommandText = sql.Append(';').ToString();

        var results = new List<PromptSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Read(reader));
        }

        return results;
    }

    private static PromptSummary Read(SqliteDataReader reader) => new()
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
        Tags = reader.ReadTextOrNull(9)?.Split(TagSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [],
    };

    private static List<string> SplitTerms(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
