using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Infrastructure.Tests;

/// <summary>Prompt, tag, collection and version storage against a real SQLite file.</summary>
public class SqliteLibraryTests
{
    private static PromptContent Content(string body, string title = "Angular Upgrade Planner") =>
        new(title, "Upgrade planning prompt", body, "Phasing by major version works.");

    [Fact]
    public async Task A_prompt_round_trips_with_every_field()
    {
        await using var library = await TestLibrary.CreateAsync();
        var collection = await library.CollectionService.CreateAsync("Coding");

        var created = await library.PromptService.CreateAsync(
            Content("Upgrade {{repo_name}}.\r\nThen test."),
            new PromptMetadata(true, 5, collection.Id, ["Angular", "migration"]));
        var loaded = await library.Prompts.GetAsync(created.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Angular Upgrade Planner", loaded.Title);
        Assert.Equal("Upgrade planning prompt", loaded.Description);
        Assert.Equal("Upgrade {{repo_name}}.\nThen test.", loaded.Body);
        Assert.Equal("Phasing by major version works.", loaded.Notes);
        Assert.True(loaded.IsFavorite);
        Assert.Equal(5, loaded.Rating);
        Assert.Equal(collection.Id, loaded.CollectionId);
        Assert.Equal(["angular", "migration"], loaded.Tags);
        Assert.Equal(created.CreatedAt, loaded.CreatedAt);
        Assert.Equal(created.UpdatedAt, loaded.UpdatedAt);
    }

    [Fact]
    public async Task Tags_are_shared_between_prompts_and_never_duplicated()
    {
        await using var library = await TestLibrary.CreateAsync();

        await library.PromptService.CreateAsync(Content("a"), PromptMetadata.Empty with { Tags = ["Angular"] });
        await library.PromptService.CreateAsync(Content("b"), PromptMetadata.Empty with { Tags = [" angular", "#ANGULAR", "review"] });

        var tags = await library.Tags.ListAsync();
        Assert.Equal([new TagSummary("angular", 2), new TagSummary("review", 1)], tags);
    }

    [Fact]
    public async Task Removing_a_tag_from_its_last_prompt_deletes_the_tag()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(Content("a"), PromptMetadata.Empty with { Tags = ["angular", "review"] });

        await library.PromptService.RemoveTagAsync(prompt.Id, "Angular");

        Assert.Equal(["review"], (await library.Prompts.GetAsync(prompt.Id))!.Tags);
        Assert.Equal([new TagSummary("review", 1)], await library.Tags.ListAsync());
    }

    [Fact]
    public async Task Adding_tags_keeps_their_order()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(Content("a"));

        await library.PromptService.AddTagAsync(prompt.Id, "zeta");
        await library.PromptService.AddTagAsync(prompt.Id, "alpha");
        await library.PromptService.AddTagAsync(prompt.Id, "Zeta");

        Assert.Equal(["zeta", "alpha"], (await library.Prompts.GetAsync(prompt.Id))!.Tags);
    }

    [Fact]
    public async Task A_deleted_prompt_waits_in_recently_deleted_out_of_sight()
    {
        await using var library = await TestLibrary.CreateAsync();
        var coding = await library.CollectionService.CreateAsync("Coding");
        await library.PromptService.CreateAsync(Content("keep", "Keep"), PromptMetadata.Empty with { Tags = ["shared"] });
        var remove = await library.PromptService.CreateAsync(Content("remove", "Remove"), new PromptMetadata(true, null, coding.Id, ["shared", "only-here"]));
        await library.PromptService.SaveContentAsync(remove.Id, Content("remove v2", "Remove"));

        await library.PromptService.DeleteAsync(remove.Id);

        Assert.Equal(library.Time.Now, (await library.Prompts.GetAsync(remove.Id))!.DeletedAt);
        Assert.Equal(2, (await library.Prompts.GetVersionsAsync(remove.Id)).Count);
        Assert.Equal(["Keep"], (await library.Search.SearchAsync(PromptQuery.All)).Select(p => p.Title));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Favorites }));
        Assert.Empty(await library.Search.SearchAsync(new PromptQuery { Text = "remove" }));
        var deleted = Assert.Single(await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Deleted }));
        Assert.Equal(library.Time.Now, deleted.DeletedAt);
        Assert.Equal(new LibraryCounts(1, 0, 1, 1, 1), await library.Prompts.GetCountsAsync());
        Assert.Equal(0, (await library.Collections.ListAsync()).Single().PromptCount);
        Assert.Equal([new TagSummary("shared", 1)], await library.Tags.ListAsync());
    }

    [Fact]
    public async Task Restoring_a_deleted_prompt_brings_it_back()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(Content("body"), PromptMetadata.Empty with { Tags = ["angular"] });
        await library.PromptService.DeleteAsync(prompt.Id);

        await library.PromptService.RestoreDeletedAsync(prompt.Id);

        Assert.Null((await library.Prompts.GetAsync(prompt.Id))!.DeletedAt);
        Assert.Single(await library.Search.SearchAsync(PromptQuery.All));
        Assert.Equal([new TagSummary("angular", 1)], await library.Tags.ListAsync());
        Assert.Single(await library.Prompts.GetVersionsAsync(prompt.Id));
    }

    [Fact]
    public async Task Emptying_recently_deleted_removes_prompts_versions_and_unused_tags()
    {
        await using var library = await TestLibrary.CreateAsync();
        var keep = await library.PromptService.CreateAsync(Content("keep"), PromptMetadata.Empty with { Tags = ["shared"] });
        var remove = await library.PromptService.CreateAsync(Content("remove"), PromptMetadata.Empty with { Tags = ["shared", "only-here"] });
        await library.PromptService.DeleteAsync(remove.Id);

        Assert.Equal(1, await library.PromptService.EmptyRecentlyDeletedAsync());

        Assert.Null(await library.Prompts.GetAsync(remove.Id));
        Assert.Empty(await library.Prompts.GetVersionsAsync(remove.Id));
        Assert.Equal([new TagSummary("shared", 1)], await library.Tags.ListAsync());
        Assert.NotNull(await library.Prompts.GetAsync(keep.Id));
        await using var connection = await library.Database.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Tags WHERE Name = 'only-here';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Prompts_deleted_more_than_30_days_ago_are_removed_for_good()
    {
        await using var library = await TestLibrary.CreateAsync();
        var old = await library.PromptService.CreateAsync(Content("old", "Old"));
        var recent = await library.PromptService.CreateAsync(Content("recent", "Recent"));
        await library.PromptService.DeleteAsync(old.Id);
        library.Time.Advance(TimeSpan.FromDays(20));
        await library.PromptService.DeleteAsync(recent.Id);
        library.Time.Advance(TimeSpan.FromDays(11));

        Assert.Equal(1, await library.PromptService.PurgeExpiredAsync());

        Assert.Null(await library.Prompts.GetAsync(old.Id));
        Assert.NotNull(await library.Prompts.GetAsync(recent.Id));
    }

    [Fact]
    public async Task Versioning_works_end_to_end()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(Content("v1"));
        library.Time.Advance(TimeSpan.FromDays(1));
        await library.PromptService.SaveContentAsync(prompt.Id, Content("v2"));
        library.Time.Advance(TimeSpan.FromDays(1));
        var noOp = await library.PromptService.SaveContentAsync(prompt.Id, Content("v2"));
        library.Time.Advance(TimeSpan.FromDays(1));
        await library.PromptService.RestoreVersionAsync(prompt.Id, 1);

        var versions = await library.Prompts.GetVersionsAsync(prompt.Id);

        Assert.False(noOp.VersionCreated);
        Assert.Equal([3, 2, 1], versions.Select(version => version.VersionNumber));
        Assert.Equal(["v1", "v2", "v1"], versions.Select(version => version.Body));
        Assert.Equal("Restored v1", versions[0].Note);
        Assert.Equal(library.Time.Now, versions[0].SavedAt);
        Assert.Equal("v1", (await library.Prompts.GetAsync(prompt.Id))!.Body);
        Assert.Equal(3, await library.Prompts.GetLatestVersionNumberAsync(prompt.Id));
    }

    [Fact]
    public async Task Deleting_a_collection_keeps_its_prompts_as_uncategorized()
    {
        await using var library = await TestLibrary.CreateAsync();
        var collection = await library.CollectionService.CreateAsync("Design");
        var prompt = await library.PromptService.CreateAsync(Content("a"), PromptMetadata.Empty with { CollectionId = collection.Id });

        await library.CollectionService.DeleteAsync(collection.Id);

        Assert.Empty(await library.Collections.ListAsync());
        var loaded = await library.Prompts.GetAsync(prompt.Id);
        Assert.NotNull(loaded);
        Assert.Null(loaded.CollectionId);
        Assert.Equal(1, (await library.Prompts.GetCountsAsync()).Uncategorized);
    }

    [Fact]
    public async Task Collections_list_with_counts_and_reject_duplicate_names()
    {
        await using var library = await TestLibrary.CreateAsync();
        var writing = await library.CollectionService.CreateAsync("Writing");
        var coding = await library.CollectionService.CreateAsync("coding");
        await library.PromptService.CreateAsync(Content("a"), PromptMetadata.Empty with { CollectionId = coding.Id });
        await library.PromptService.CreateAsync(Content("b"), PromptMetadata.Empty with { CollectionId = coding.Id });

        var error = await Assert.ThrowsAsync<LibraryException>(() => library.CollectionService.CreateAsync(" WRITING "));
        await library.CollectionService.RenameAsync(coding.Id, "Coding");

        Assert.Contains("already a collection", error.Message);
        Assert.Equal(
            [new CollectionSummary(coding.Id, "Coding", 2), new CollectionSummary(writing.Id, "Writing", 0)],
            await library.Collections.ListAsync());
        Assert.Equal(coding.Id, (await library.CollectionService.GetOrCreateAsync("CODING")).Id);
    }

    [Fact]
    public async Task Counts_cover_favorites_uncategorized_and_versions()
    {
        await using var library = await TestLibrary.CreateAsync();
        var first = await library.PromptService.CreateAsync(Content("a"), PromptMetadata.Empty with { IsFavorite = true });
        await library.PromptService.CreateAsync(Content("b"));
        await library.PromptService.SaveContentAsync(first.Id, Content("a2"));

        Assert.Equal(new LibraryCounts(2, 1, 2, 3, 0), await library.Prompts.GetCountsAsync());
    }

    [Fact]
    public async Task A_copy_keeps_its_lineage_until_the_parent_is_removed_for_good()
    {
        await using var library = await TestLibrary.CreateAsync();
        var parent = await library.PromptService.CreateAsync(new PromptContent("Parent", null, "body", null));
        var copy = await library.PromptService.DuplicateAsync(parent.Id);

        Assert.Equal(parent.Id, (await library.Prompts.GetAsync(copy.Id))!.ParentPromptId);
        Assert.Equal([copy.Id], (await library.PromptService.GetLineageAsync(parent.Id)).Copies.Select(link => link.Id));

        // Editing the copy doesn't touch its lineage.
        await library.PromptService.SaveContentAsync(copy.Id, new PromptContent("Copy", null, "body v2", null));
        await library.PromptService.SetFavoriteAsync(copy.Id, true);
        Assert.Equal(parent.Id, (await library.Prompts.GetAsync(copy.Id))!.ParentPromptId);

        await library.PromptService.DeleteAsync(parent.Id);
        Assert.True((await library.PromptService.GetLineageAsync(copy.Id)).Parent?.IsDeleted);

        await library.PromptService.EmptyRecentlyDeletedAsync();
        var orphan = await library.Prompts.GetAsync(copy.Id);
        Assert.NotNull(orphan);
        Assert.Null(orphan.ParentPromptId);
        Assert.Null((await library.PromptService.GetLineageAsync(copy.Id)).Parent);
    }
}
