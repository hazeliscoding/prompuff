using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Infrastructure.Tests;

public class SqlitePromptSearchTests
{
    private static async Task<(TestLibrary Library, Dictionary<string, Guid> Ids)> SeedAsync()
    {
        var library = await TestLibrary.CreateAsync();
        var coding = await library.CollectionService.CreateAsync("Coding");
        var ids = new Dictionary<string, Guid>();

        async Task Add(string key, string title, string? description, string body, string? notes, PromptMetadata metadata)
        {
            library.Time.Advance(TimeSpan.FromMinutes(1));
            ids[key] = (await library.PromptService.CreateAsync(new PromptContent(title, description, body, notes), metadata)).Id;
        }

        await Add("angular", "Angular Upgrade Planner", "Phased migration plan", "Upgrade {{repo_name}} to Angular {{target_version}}.", "Phasing works.",
            new PromptMetadata(true, 5, coding.Id, ["angular", "migration"]));
        await Add("readme", "README Cleanup", "Rewrite a README", "Rewrite the README for {{repo_name}}.", null,
            new PromptMetadata(false, 3, null, ["writing"]));
        await Add("bug", "Bug Reproduction Request", null, "Turn this report into numbered steps.", "List the assumption instead of guessing silently.",
            new PromptMetadata(false, 4, coding.Id, ["review"]));
        await Add("percent", "Discount math", null, "Take 100% of the 50_50 split.", null, PromptMetadata.Empty);
        return (library, ids);
    }

    [Theory]
    [InlineData("angular", "angular")]
    [InlineData("PHASED", "angular")]
    [InlineData("numbered steps", "bug")]
    [InlineData("guessing", "bug")]
    [InlineData("writing", "readme")]
    public async Task Text_matches_title_description_body_notes_and_tags(string text, string expected)
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        var results = await library.Search.SearchAsync(new PromptQuery { Text = text });

        Assert.Equal([ids[expected]], results.Select(result => result.Id));
    }

    [Fact]
    public async Task Every_word_must_match()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        Assert.Equal([ids["readme"]], (await library.Search.SearchAsync(new PromptQuery { Text = "repo_name readme" })).Select(r => r.Id));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "angular readme" }));
    }

    [Fact]
    public async Task Title_matches_rank_first()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        var results = await library.Search.SearchAsync(new PromptQuery { Text = "readme" });

        Assert.Equal(ids["readme"], results[0].Id);
    }

    [Fact]
    public async Task Like_wildcards_in_the_query_are_literal()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        Assert.Equal([ids["percent"]], (await library.Search.SearchAsync(new PromptQuery { Text = "100%" })).Select(r => r.Id));
        Assert.Equal([ids["percent"]], (await library.Search.SearchAsync(new PromptQuery { Text = "50_50" })).Select(r => r.Id));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "%%" }));
    }

    [Theory]
    [InlineData("cafe", "Café au lait")]
    [InlineData("CAFÉ", "cafe order")]
    [InlineData("naive", "Naïve résumé")]
    [InlineData("resume", "Naïve résumé")]
    public async Task Matching_ignores_case_and_accents(string text, string title)
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(new PromptContent(title, null, "body", null));
        await library.PromptService.CreateAsync(new PromptContent("Something else", null, "body", null));

        Assert.Equal([prompt.Id], (await library.Search.SearchAsync(new PromptQuery { Text = text })).Select(r => r.Id));
    }

    [Fact]
    public async Task Words_match_by_prefix()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        Assert.Equal([ids["angular"]], (await library.Search.SearchAsync(new PromptQuery { Text = "upgr" })).Select(r => r.Id));
        Assert.Equal([ids["angular"]], (await library.Search.SearchAsync(new PromptQuery { Text = "migr" })).Select(r => r.Id));
    }

    [Fact]
    public async Task Results_are_ranked_with_titles_first()
    {
        await using var library = await TestLibrary.CreateAsync();
        var body = await library.PromptService.CreateAsync(new PromptContent("Other", null, "testing testing testing", null));
        library.Time.Advance(TimeSpan.FromMinutes(1));
        var title = await library.PromptService.CreateAsync(new PromptContent("Notes on testing", null, "x", null));
        library.Time.Advance(TimeSpan.FromMinutes(1));
        await library.PromptService.SaveContentAsync(body.Id, new PromptContent("Other", null, "testing testing testing!", null));

        var results = await library.Search.SearchAsync(new PromptQuery { Text = "testing", Sort = PromptSort.LastEdited });

        Assert.Equal([title.Id, body.Id], results.Select(r => r.Id));
    }

    [Fact]
    public async Task The_index_follows_edits_tags_and_deletes()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(new PromptContent("Planner", null, "first draft", null));

        await library.PromptService.SaveContentAsync(prompt.Id, new PromptContent("Planner", null, "second version", null));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "draft" }));
        Assert.Single(await library.Search.SearchAsync(new PromptQuery { Text = "second" }));

        await library.PromptService.AddTagAsync(prompt.Id, "kubernetes");
        Assert.Single(await library.Search.SearchAsync(new PromptQuery { Text = "kube" }));
        await library.PromptService.RemoveTagAsync(prompt.Id, "kubernetes");
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "kube" }));

        await library.PromptService.DeleteAsync(prompt.Id);
        await library.PromptService.EmptyRecentlyDeletedAsync();
        await using var connection = await library.Database.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PromptSearch;";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Symbol_only_words_match_literally()
    {
        await using var library = await TestLibrary.CreateAsync();
        var arrow = await library.PromptService.CreateAsync(new PromptContent("Pipeline", null, "parse -> render", null));
        await library.PromptService.CreateAsync(new PromptContent("Plain", null, "nothing here", null));

        Assert.Equal([arrow.Id], (await library.Search.SearchAsync(new PromptQuery { Text = "->" })).Select(r => r.Id));
    }

    [Fact]
    public async Task Hash_words_match_tags_exactly()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        Assert.Equal([ids["angular"]], (await library.Search.SearchAsync(new PromptQuery { Text = "#Migration" })).Select(r => r.Id));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "#migr" }));
    }

    [Fact]
    public async Task Filters_narrow_the_library()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;
        var coding = (await library.Collections.ListAsync()).Single();

        Assert.Equal([ids["angular"]], (await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Favorites })).Select(r => r.Id));
        Assert.Equal(
            [ids["bug"], ids["angular"]],
            (await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Collection, CollectionId = coding.Id })).Select(r => r.Id));
        Assert.Equal(
            [ids["percent"], ids["readme"]],
            (await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Uncategorized })).Select(r => r.Id));
        Assert.Equal([ids["bug"]], (await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Tag, Tag = "Review" })).Select(r => r.Id));
    }

    [Fact]
    public async Task Filters_combine_with_text()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        var results = await library.Search.SearchAsync(new PromptQuery { Text = "repo_name", Filter = PromptFilterKind.Favorites });

        Assert.Equal([ids["angular"]], results.Select(r => r.Id));
    }

    [Fact]
    public async Task Sorting_by_title_and_usefulness()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        Assert.Equal(
            [ids["angular"], ids["bug"], ids["percent"], ids["readme"]],
            (await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Title })).Select(r => r.Id));
        Assert.Equal(
            [ids["angular"], ids["bug"], ids["readme"], ids["percent"]],
            (await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Usefulness })).Select(r => r.Id));
    }

    [Fact]
    public async Task Recent_includes_opened_prompts_and_edits()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        library.Time.Advance(TimeSpan.FromHours(1));
        await library.PromptService.MarkOpenedAsync(ids["readme"]);
        library.Time.Advance(TimeSpan.FromHours(1));
        await library.PromptService.SaveContentAsync(ids["bug"], new PromptContent("Bug Reproduction Request", null, "Edited.", null));

        var recent = await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Recent });

        Assert.Equal([ids["bug"], ids["readme"], ids["percent"], ids["angular"]], recent.Select(r => r.Id));
    }

    [Fact]
    public async Task Summaries_carry_tags_in_order()
    {
        var (library, ids) = await SeedAsync();
        await using var _ = library;

        var angular = (await library.Search.SearchAsync(PromptQuery.All)).Single(result => result.Id == ids["angular"]);

        Assert.Equal(["angular", "migration"], angular.Tags);
        Assert.True(angular.IsFavorite);
        Assert.Equal(5, angular.Rating);
    }

    [Fact]
    public async Task Summaries_follow_tag_changes_in_order()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(new PromptContent("Planner", null, "body", null), new PromptMetadata(false, null, null, ["zeta", "alpha"]));
        async Task<IReadOnlyList<string>> Tags() => (await library.Search.SearchAsync(PromptQuery.All)).Single().Tags;

        Assert.Equal(["zeta", "alpha"], await Tags());
        await library.PromptService.AddTagsAsync(prompt.Id, ["mid"]);
        Assert.Equal(["zeta", "alpha", "mid"], await Tags());
        await library.PromptService.RemoveTagAsync(prompt.Id, "zeta");
        Assert.Equal(["alpha", "mid"], await Tags());

        // A save that leaves the tags alone keeps them, and the index with them.
        await library.PromptService.SetFavoriteAsync(prompt.Id, true);
        Assert.Equal(["alpha", "mid"], await Tags());
        Assert.Single(await library.Search.SearchAsync(new PromptQuery { Text = "#mid" }));
        Assert.Single(await library.Search.SearchAsync(new PromptQuery { Text = "mid" }));

        await library.PromptService.SetTagsAsync(prompt.Id, []);
        Assert.Empty(await Tags());
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "mid" }));
    }
}
