using Prompuff.Application.DTOs;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.Infrastructure.Tests;

/// <summary>
/// Keeps large libraries fast without timing anything: SQLite is bundled, so the plan it picks for each search is the
/// same on every machine, and these check that listing prompts never goes back to the plans that took 250 ms for
/// 10,000 prompts.
/// </summary>
public class SearchPlanTests
{
    public static TheoryData<string> Searches =>
    [
        "all", "by title", "by usefulness", "Recent", "Favorites", "Uncategorized", "a collection", "a tag filter",
        "Recently deleted", "a word", "a #tag", "a word in Favorites",
    ];

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Searches_read_the_summary_index_and_run_nothing_per_prompt(string search)
    {
        await using var library = await TestLibrary.CreateAsync();
        await using var connection = await library.Database.OpenAsync();
        await using var command = SqlitePromptSearch.CreateCommand(connection, Query(search));
        command.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(3));
            }
        }

        var steps = string.Join(Environment.NewLine, plan);

        // A subquery per prompt, for its tags, was the slowest part of listing a large library.
        Assert.DoesNotContain("CORRELATED", steps);

        // Prompts come from IX_Prompts_Summary, never as whole rows with their bodies. A collection may use its own
        // index, which reads only that collection's rows.
        Assert.Contains(plan, step => step.Contains("IX_Prompts_Summary", StringComparison.Ordinal) || step.Contains("IX_Prompts_CollectionId", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, step => step.StartsWith("SCAN p", StringComparison.Ordinal) && !step.Contains("COVERING INDEX", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, step => step.StartsWith("SEARCH p USING INDEX", StringComparison.Ordinal) && !step.Contains("IX_Prompts_CollectionId", StringComparison.Ordinal));
        Assert.DoesNotContain("sqlite_autoindex_Prompts_1", steps);

        // Index matches reach their prompts by rowid, not by scanning the index for a PromptId.
        if (search.Contains("word", StringComparison.Ordinal))
        {
            Assert.Contains(plan, step => step.StartsWith("SEARCH r USING INTEGER PRIMARY KEY", StringComparison.Ordinal));
        }
    }

    private static PromptQuery Query(string search) => search switch
    {
        "all" => PromptQuery.All,
        "by title" => new PromptQuery { Sort = PromptSort.Title },
        "by usefulness" => new PromptQuery { Sort = PromptSort.Usefulness },
        "Recent" => new PromptQuery { Filter = PromptFilterKind.Recent },
        "Favorites" => new PromptQuery { Filter = PromptFilterKind.Favorites },
        "Uncategorized" => new PromptQuery { Filter = PromptFilterKind.Uncategorized },
        "a collection" => new PromptQuery { Filter = PromptFilterKind.Collection, CollectionId = Guid.NewGuid() },
        "a tag filter" => new PromptQuery { Filter = PromptFilterKind.Tag, Tag = "review" },
        "Recently deleted" => new PromptQuery { Filter = PromptFilterKind.Deleted },
        "a word" => new PromptQuery { Text = "angular" },
        "a #tag" => new PromptQuery { Text = "#review" },
        "a word in Favorites" => new PromptQuery { Text = "angular", Filter = PromptFilterKind.Favorites },
        _ => throw new ArgumentOutOfRangeException(nameof(search)),
    };
}
