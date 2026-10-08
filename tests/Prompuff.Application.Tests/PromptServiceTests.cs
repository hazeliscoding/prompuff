using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Application.Tests;

public class PromptServiceTests
{
    private readonly InMemoryPromptRepository _repository = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly PromptService _service;

    public PromptServiceTests()
    {
        _service = new PromptService(_repository, _time, NullLogger<PromptService>.Instance);
    }

    private static PromptContent Content(string body, string title = "Angular Upgrade Planner", string? notes = null) =>
        new(title, "Upgrade planning prompt", body, notes);

    [Fact]
    public async Task Creating_a_prompt_records_version_one()
    {
        var prompt = await _service.CreateAsync(Content("Upgrade {{repo_name}}."));

        var versions = await _service.GetVersionsAsync(prompt.Id);
        var only = Assert.Single(versions);
        Assert.Equal(1, only.VersionNumber);
        Assert.Equal(prompt.Content, only.Content);
        Assert.Equal(VersionNotes.FirstVersion, only.Note);
    }

    [Fact]
    public async Task Changed_content_creates_a_version()
    {
        var prompt = await _service.CreateAsync(Content("Plan the upgrade."));
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await _service.SaveContentAsync(prompt.Id, Content("Plan the upgrade.\nInspect the repo first."));

        Assert.True(result.VersionCreated);
        Assert.Equal(2, result.CurrentVersion);
        var latest = (await _service.GetVersionsAsync(prompt.Id))[0];
        Assert.Equal(2, latest.VersionNumber);
        Assert.Equal("Plan the upgrade.\nInspect the repo first.", latest.Body);
        Assert.Equal("Edited body (+1 −0 lines)", latest.Note);
        Assert.Equal(_time.Now, (await _service.GetAsync(prompt.Id))!.UpdatedAt);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("notes")]
    public async Task Each_content_field_counts_as_a_change(string field)
    {
        var original = Content("Body", notes: "It worked.");
        var prompt = await _service.CreateAsync(original);
        var changed = field switch
        {
            "title" => new PromptContent("New title", original.Description, original.Body, original.Notes),
            "description" => new PromptContent(original.Title, "New description", original.Body, original.Notes),
            _ => new PromptContent(original.Title, original.Description, original.Body, "It worked better."),
        };

        var result = await _service.SaveContentAsync(prompt.Id, changed);

        Assert.True(result.VersionCreated);
    }

    [Fact]
    public async Task No_op_save_does_not_create_a_version()
    {
        var prompt = await _service.CreateAsync(Content("Plan the upgrade.", notes: "Phasing helps."));
        var updatedAt = prompt.UpdatedAt;
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await _service.SaveContentAsync(prompt.Id, Content("Plan the upgrade.", notes: "Phasing helps.\r\n"));

        Assert.False(result.VersionCreated);
        Assert.Equal(1, result.CurrentVersion);
        Assert.Single(await _service.GetVersionsAsync(prompt.Id));
        Assert.Equal(updatedAt, (await _service.GetAsync(prompt.Id))!.UpdatedAt);
    }

    [Fact]
    public async Task Metadata_changes_do_not_create_versions()
    {
        var prompt = await _service.CreateAsync(Content("Body"));

        await _service.SetFavoriteAsync(prompt.Id, true);
        await _service.SetRatingAsync(prompt.Id, 5);
        await _service.SetTagsAsync(prompt.Id, ["Angular", "migration"]);

        var saved = await _service.GetAsync(prompt.Id);
        Assert.True(saved!.IsFavorite);
        Assert.Equal(5, saved.Rating);
        Assert.Equal(["angular", "migration"], saved.Tags);
        Assert.Single(await _service.GetVersionsAsync(prompt.Id));
    }

    [Fact]
    public async Task Versions_are_listed_newest_first()
    {
        var prompt = await _service.CreateAsync(Content("v1"));
        for (var i = 2; i <= 5; i++)
        {
            _time.Advance(TimeSpan.FromDays(1));
            await _service.SaveContentAsync(prompt.Id, Content($"v{i}"));
        }

        var versions = await _service.GetVersionsAsync(prompt.Id);

        Assert.Equal([5, 4, 3, 2, 1], versions.Select(version => version.VersionNumber));
        Assert.Equal(["v5", "v4", "v3", "v2", "v1"], versions.Select(version => version.Body));
        Assert.True(versions.Zip(versions.Skip(1)).All(pair => pair.First.SavedAt > pair.Second.SavedAt));
    }

    [Fact]
    public async Task Restoring_keeps_the_previous_state_in_history()
    {
        var prompt = await _service.CreateAsync(Content("first"));
        await _service.SaveContentAsync(prompt.Id, Content("second"));
        await _service.SaveContentAsync(prompt.Id, Content("third"));

        var result = await _service.RestoreVersionAsync(prompt.Id, 1);

        Assert.True(result.VersionCreated);
        Assert.Equal(4, result.CurrentVersion);
        Assert.Equal("first", (await _service.GetAsync(prompt.Id))!.Body);
        var versions = await _service.GetVersionsAsync(prompt.Id);
        Assert.Equal(["first", "third", "second", "first"], versions.Select(version => version.Body));
        Assert.Equal("Restored v1", versions[0].Note);
    }

    [Fact]
    public async Task Restoring_the_current_content_changes_nothing()
    {
        var prompt = await _service.CreateAsync(Content("first"));
        await _service.SaveContentAsync(prompt.Id, Content("second"));

        var result = await _service.RestoreVersionAsync(prompt.Id, 2);

        Assert.False(result.VersionCreated);
        Assert.Equal(2, (await _service.GetVersionsAsync(prompt.Id)).Count);
    }

    [Fact]
    public async Task Restoring_a_missing_version_fails_with_a_friendly_message()
    {
        var prompt = await _service.CreateAsync(Content("first"));

        var error = await Assert.ThrowsAsync<LibraryException>(() => _service.RestoreVersionAsync(prompt.Id, 9));
        Assert.Contains("no longer exists", error.Message);
    }

    [Fact]
    public async Task Duplicating_copies_content_and_metadata_into_a_new_history()
    {
        var collectionId = Guid.NewGuid();
        var prompt = await _service.CreateAsync(
            Content("body", notes: "why"),
            new PromptMetadata(true, 4, collectionId, ["angular"]));
        await _service.SaveContentAsync(prompt.Id, Content("body v2", notes: "why"));

        var copy = await _service.DuplicateAsync(prompt.Id);

        Assert.NotEqual(prompt.Id, copy.Id);
        Assert.Equal("Angular Upgrade Planner (copy)", copy.Title);
        Assert.Equal("body v2", copy.Body);
        Assert.Equal("why", copy.Notes);
        Assert.False(copy.IsFavorite);
        Assert.Equal(4, copy.Rating);
        Assert.Equal(collectionId, copy.CollectionId);
        Assert.Equal(["angular"], copy.Tags);
        Assert.Single(await _service.GetVersionsAsync(copy.Id));
    }

    [Fact]
    public async Task Duplicating_an_old_version_uses_that_version()
    {
        var prompt = await _service.CreateAsync(Content("old body"));
        await _service.SaveContentAsync(prompt.Id, Content("new body"));

        var copy = await _service.DuplicateVersionAsync(prompt.Id, 1);

        Assert.Equal("old body", copy.Body);
        Assert.Equal("Angular Upgrade Planner (v1 copy)", copy.Title);
    }

    [Fact]
    public async Task Copies_remember_their_parent_and_the_parent_lists_them()
    {
        var prompt = await _service.CreateAsync(Content("body"));
        await _service.SaveContentAsync(prompt.Id, Content("body v2"));

        var copy = await _service.DuplicateAsync(prompt.Id);
        var versionCopy = await _service.DuplicateVersionAsync(prompt.Id, 1);

        Assert.Equal(prompt.Id, copy.ParentPromptId);
        Assert.Equal(prompt.Id, versionCopy.ParentPromptId);
        var lineage = await _service.GetLineageAsync(copy.Id);
        Assert.Equal(new PromptLink(prompt.Id, "Angular Upgrade Planner", false), lineage.Parent);
        Assert.Empty(lineage.Copies);
        Assert.Equal([copy.Id, versionCopy.Id], (await _service.GetLineageAsync(prompt.Id)).Copies.Select(link => link.Id));
        Assert.Null((await _service.GetLineageAsync(prompt.Id)).Parent);
    }

    [Fact]
    public async Task Adding_and_removing_tags_normalizes_names()
    {
        var prompt = await _service.CreateAsync(Content("body"));

        await _service.AddTagAsync(prompt.Id, " Angular");
        await _service.AddTagAsync(prompt.Id, "angular");
        await _service.AddTagAsync(prompt.Id, "#Review");
        await _service.RemoveTagAsync(prompt.Id, "ANGULAR");

        Assert.Equal(["review"], (await _service.GetAsync(prompt.Id))!.Tags);
    }

    [Fact]
    public async Task Deleting_keeps_the_prompt_and_its_versions_until_emptied()
    {
        var prompt = await _service.CreateAsync(Content("body"));
        await _service.SaveContentAsync(prompt.Id, Content("body 2"));
        var updated = (await _service.GetAsync(prompt.Id))!.UpdatedAt;

        await _service.DeleteAsync(prompt.Id);

        var deleted = await _service.GetAsync(prompt.Id);
        Assert.NotNull(deleted?.DeletedAt);
        Assert.Equal(updated, deleted.UpdatedAt);
        Assert.Equal(2, _repository.AllVersions.Count);

        Assert.Equal(1, await _service.EmptyRecentlyDeletedAsync());
        Assert.Null(await _service.GetAsync(prompt.Id));
        Assert.Empty(_repository.AllVersions);
    }

    [Fact]
    public async Task Imported_timestamps_are_kept()
    {
        var created = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var updated = created.AddMinutes(30);

        var prompt = await _service.CreateAsync(Content("body"), createdAt: created, updatedAt: updated);

        Assert.Equal(created, prompt.CreatedAt);
        Assert.Equal(updated, prompt.UpdatedAt);
    }
}
