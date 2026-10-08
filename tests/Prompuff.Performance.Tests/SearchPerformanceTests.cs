using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.Performance.Tests;

/// <summary>
/// Search, sidebar counts and saves against a library of 10,000 prompts. Each timing is the median of nine runs after
/// a warm-up, so one slow run on a busy CI machine doesn't fail the build.
/// </summary>
[Trait("Category", "Performance")]
public class SearchPerformanceTests(SeededLibrary seeded)
{
    /// <summary>The roadmap's promise for searching and filtering.</summary>
    private static readonly TimeSpan SearchLimit = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Listing every prompt, or a search nearly every prompt matches, reads all 10,000 summaries, and that dominates.
    /// These run off the UI thread, so the bound guards against regressions rather than promising a response time.
    /// </summary>
    private static readonly TimeSpan EverythingLimit = TimeSpan.FromMilliseconds(300);

    /// <summary>Counts were 55–100 ms when they scaled with the library; now they don't.</summary>
    private static readonly TimeSpan SidebarAndSaveLimit = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// A write commits to disk, which on a shared CI runner can take 40–60 ms whatever the library's size, so writes are
    /// compared with the same writes to a small library in the same run. Before v0.9 a favorite took about twenty times
    /// as long with 10,000 prompts as with 200; now it takes about the same. The ceiling only catches a disaster.
    /// </summary>
    private const double WriteScaleLimit = 3.0;

    private static readonly TimeSpan WriteSlack = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan WriteCeiling = TimeSpan.FromMilliseconds(250);

    public static TheoryData<string> Searches =>
    [
        "a word", "a prefix", "several words", "a #tag", "an accented word", "a collection", "Uncategorized", "Favorites",
        "Recent", "a tag in the sidebar", "Recently deleted",
    ];

    public static TheoryData<string> Everything => ["all, last edited", "all, by title", "all, by usefulness", "one letter", "a common word"];

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task Searching_10000_prompts_takes_under_100_ms(string search)
    {
        var (query, check) = Query(search);
        var results = await Search().SearchAsync(query);
        check(results);

        var median = await Timing.MedianAsync(() => Search().SearchAsync(query));

        Timing.Report($"{search}: {results.Count} results, median {median.TotalMilliseconds:F1} ms");
        Assert.True(median < SearchLimit, $"Searching for {search} took {median.TotalMilliseconds:F0} ms, over {SearchLimit.TotalMilliseconds} ms.");
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public async Task Listing_or_matching_every_prompt_stays_bounded(string search)
    {
        var (query, check) = Query(search);
        var results = await Search().SearchAsync(query);
        check(results);

        var median = await Timing.MedianAsync(() => Search().SearchAsync(query));

        Timing.Report($"{search}: {results.Count} results, median {median.TotalMilliseconds:F1} ms");
        Assert.True(median < EverythingLimit, $"{search} took {median.TotalMilliseconds:F0} ms, over {EverythingLimit.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task Sidebar_counts_stay_fast()
    {
        var database = Database(seeded.DatabasePath);
        var prompts = new SqlitePromptRepository(database);
        var collections = new SqliteCollectionRepository(database);
        var tags = new SqliteTagRepository(database);

        var counts = await prompts.GetCountsAsync();
        Assert.Equal(seeded.Info.Prompts, counts.All);
        Assert.Equal(seeded.Info.Favorites, counts.Favorites);
        Assert.Equal(seeded.Info.Uncategorized, counts.Uncategorized);
        Assert.Equal(seeded.Info.Deleted, counts.Deleted);
        Assert.Equal(seeded.Info.TagUses.Count, (await tags.ListAsync()).Count);
        Assert.Equal(seeded.Info.TagUses["review"], (await tags.ListAsync()).Single(tag => tag.Name == "review").PromptCount);
        Assert.Equal(seeded.Info.Prompts - seeded.Info.Uncategorized, (await collections.ListAsync()).Sum(collection => collection.PromptCount));

        foreach (var (name, run) in new (string, Func<Task>)[]
                 {
                     ("counts", () => prompts.GetCountsAsync()),
                     ("collections", () => collections.ListAsync()),
                     ("tags", () => tags.ListAsync()),
                 })
        {
            var median = await Timing.MedianAsync(run);
            Timing.Report($"{name}: median {median.TotalMilliseconds:F1} ms");
            Assert.True(median < SidebarAndSaveLimit, $"Sidebar {name} took {median.TotalMilliseconds:F0} ms, over {SidebarAndSaveLimit.TotalMilliseconds} ms.");
        }
    }

    [Fact]
    public async Task Saving_a_prompt_doesnt_scale_with_the_library()
    {
        var smallPath = Path.Combine(seeded.CopyToNewFolder(), "small", "prompuff.db");
        Directory.CreateDirectory(Path.GetDirectoryName(smallPath)!);
        await LibrarySeeder.SeedAsync(smallPath, 200);
        var small = await TimeWritesAsync(smallPath);
        var large = await TimeWritesAsync(Path.Combine(seeded.CopyToNewFolder(), "prompuff.db"));

        Timing.Report($"200 prompts: {small}");
        Timing.Report($"10,000 prompts: {large}");
        foreach (var (what, smallTime, largeTime) in new[] { ("A favorite", small.Favorite, large.Favorite), ("A save", small.Save, large.Save), ("Adding a tag", small.Tag, large.Tag) })
        {
            Assert.True(largeTime < (smallTime * WriteScaleLimit) + WriteSlack,
                $"{what} took {largeTime.TotalMilliseconds:F0} ms with 10,000 prompts and {smallTime.TotalMilliseconds:F0} ms with 200.");
            Assert.True(largeTime < WriteCeiling, $"{what} took {largeTime.TotalMilliseconds:F0} ms with 10,000 prompts.");
        }
    }

    private sealed record WriteTimes(TimeSpan Favorite, TimeSpan Save, TimeSpan Tag)
    {
        public override string ToString() =>
            $"favorite {Favorite.TotalMilliseconds:F1} ms, save {Save.TotalMilliseconds:F1} ms, tag {Tag.TotalMilliseconds:F1} ms";
    }

    /// <summary>Median times for a favorite, a content save and a new tag, then a check that search kept up with them.</summary>
    private static async Task<WriteTimes> TimeWritesAsync(string databasePath)
    {
        var database = Database(databasePath);
        var service = new PromptService(new SqlitePromptRepository(database), TimeProvider.System, NullLogger<PromptService>.Instance);
        var ids = (await new SqlitePromptSearch(database).SearchAsync(PromptQuery.All)).Take(20).Select(summary => summary.Id).ToList();
        var next = 0;

        var favorite = await Timing.MedianAsync(() => service.SetFavoriteAsync(ids[next++ % ids.Count], next % 2 == 0));
        var saved = new HashSet<Guid>();
        var save = await Timing.MedianAsync(async () =>
        {
            var prompt = (await service.GetAsync(ids[next++ % ids.Count]))!;
            saved.Add(prompt.Id);
            await service.SaveContentAsync(prompt.Id, new PromptContent(prompt.Title, prompt.Description, prompt.Body + "\nAsk about the zebrafish.", prompt.Notes));
        });
        var added = new List<(Guid Id, string Tag)>();
        var tag = await Timing.MedianAsync(() =>
        {
            var id = ids[next++ % ids.Count];
            added.Add((id, $"perf-{added.Count}"));
            return service.AddTagAsync(id, added[^1].Tag);
        });

        // The index kept up with all of it.
        var search = new SqlitePromptSearch(database);
        Assert.Equal([added[3].Id], (await search.SearchAsync(new PromptQuery { Text = "#" + added[3].Tag })).Select(result => result.Id));
        Assert.Equal(saved.Order(), (await search.SearchAsync(new PromptQuery { Text = "zebrafish" })).Select(result => result.Id).Order());
        return new WriteTimes(favorite, save, tag);
    }

    private SqlitePromptSearch Search() => new(Database(seeded.DatabasePath));

    private static SqliteDatabase Database(string path) =>
        new(path, Path.Combine(Path.GetDirectoryName(path)!, "backups"), NullLogger<SqliteDatabase>.Instance);

    private (PromptQuery Query, Action<IReadOnlyList<PromptSummary>> Check) Query(string search)
    {
        var info = seeded.Info;
        var engineering = info.Collections.Single(pair => pair.Value == "Engineering").Key;
        Action<IReadOnlyList<PromptSummary>> some = results => Assert.NotEmpty(results);
        Action<IReadOnlyList<PromptSummary>> everyLivePrompt = results => Assert.Equal(info.Prompts, results.Count);
        return search switch
        {
            "a word" => (new PromptQuery { Text = "angular" }, some),
            "a prefix" => (new PromptQuery { Text = "refac" }, results => Assert.All(results, result => Assert.Contains("Refactor", result.Title + result.Description + string.Join(' ', result.Tags), StringComparison.OrdinalIgnoreCase))),
            "several words" => (new PromptQuery { Text = "review pull request" }, some),
            "a #tag" => (new PromptQuery { Text = "#review" }, results => Assert.Equal(info.TagUses["review"], results.Count)),
            "an accented word" => (new PromptQuery { Text = "cafe" }, results => Assert.Contains(results, result => result.Title.Contains("Café", StringComparison.Ordinal))),
            "a collection" => (new PromptQuery { Filter = PromptFilterKind.Collection, CollectionId = engineering }, results => Assert.All(results, result => Assert.Equal(engineering, result.CollectionId))),
            "Uncategorized" => (new PromptQuery { Filter = PromptFilterKind.Uncategorized }, results => Assert.Equal(info.Uncategorized, results.Count)),
            "Favorites" => (new PromptQuery { Filter = PromptFilterKind.Favorites }, results => Assert.Equal(info.Favorites, results.Count)),
            "Recent" => (new PromptQuery { Filter = PromptFilterKind.Recent }, results => Assert.Equal(PromptQuery.RecentLimit, results.Count)),
            "a tag in the sidebar" => (new PromptQuery { Filter = PromptFilterKind.Tag, Tag = "review" }, results => Assert.Equal(info.TagUses["review"], results.Count)),
            "Recently deleted" => (new PromptQuery { Filter = PromptFilterKind.Deleted }, results => Assert.Equal(info.Deleted, results.Count)),
            "all, last edited" => (PromptQuery.All, everyLivePrompt),
            "all, by title" => (new PromptQuery { Sort = PromptSort.Title }, everyLivePrompt),
            "all, by usefulness" => (new PromptQuery { Sort = PromptSort.Usefulness }, everyLivePrompt),
            "one letter" => (new PromptQuery { Text = "a" }, results => Assert.True(results.Count > info.Prompts * 9 / 10)),
            "a common word" => (new PromptQuery { Text = "the" }, results => Assert.True(results.Count > info.Prompts * 9 / 10)),
            _ => throw new ArgumentOutOfRangeException(nameof(search)),
        };
    }
}
