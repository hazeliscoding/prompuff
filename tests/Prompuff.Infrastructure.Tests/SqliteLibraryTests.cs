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
    public async Task Deleting_a_prompt_removes_its_versions_and_unused_tags()
    {
        await using var library = await TestLibrary.CreateAsync();
        var keep = await library.PromptService.CreateAsync(Content("keep"), PromptMetadata.Empty with { Tags = ["shared"] });
        var remove = await library.PromptService.CreateAsync(Content("remove"), PromptMetadata.Empty with { Tags = ["shared", "only-here"] });
        await library.PromptService.SaveContentAsync(remove.Id, Content("remove v2"));

        await library.PromptService.DeleteAsync(remove.Id);

        Assert.Null(await library.Prompts.GetAsync(remove.Id));
        Assert.Empty(await library.Prompts.GetVersionsAsync(remove.Id));
        Assert.Equal([new TagSummary("shared", 1)], await library.Tags.ListAsync());
        Assert.NotNull(await library.Prompts.GetAsync(keep.Id));
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

        Assert.Equal(new LibraryCounts(2, 1, 2, 3), await library.Prompts.GetCountsAsync());
    }
}
