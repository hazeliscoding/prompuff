using Microsoft.Extensions.Logging;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Application.Services;

/// <summary>Everything about a prompt that isn't its content and doesn't create versions.</summary>
public sealed record PromptMetadata(bool IsFavorite, int? Rating, Guid? CollectionId, IReadOnlyList<string> Tags)
{
    public static PromptMetadata Empty { get; } = new(false, null, null, []);

    public static PromptMetadata From(Prompt prompt) => new(prompt.IsFavorite, prompt.Rating, prompt.CollectionId, prompt.Tags);
}

/// <param name="VersionCreated">False when the save changed nothing.</param>
/// <param name="CurrentVersion">The version number the prompt is at after the save.</param>
public sealed record SaveResult(bool VersionCreated, int CurrentVersion);

/// <summary>Creates, edits, versions, duplicates and deletes prompts.</summary>
public sealed class PromptService(IPromptRepository prompts, TimeProvider time, ILogger<PromptService> logger)
{
    public Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        prompts.GetAsync(id, cancellationToken);

    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) =>
        prompts.GetVersionsAsync(id, cancellationToken);

    public async Task<Prompt> CreateAsync(
        PromptContent content,
        PromptMetadata? metadata = null,
        string? versionNote = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var created = createdAt ?? now;
        var updated = updatedAt is { } value && value >= created ? value : created;
        var prompt = new Prompt { Id = Guid.NewGuid(), CreatedAt = created, UpdatedAt = updated };
        prompt.SetContent(content);
        ApplyMetadata(prompt, metadata ?? PromptMetadata.Empty);

        var firstVersion = PromptVersion.Snapshot(prompt, 1, versionNote ?? VersionNotes.FirstVersion, now);
        await prompts.InsertAsync(prompt, firstVersion, cancellationToken);
        logger.LogInformation("Created prompt {PromptId}", prompt.Id);
        return prompt;
    }

    /// <summary>Saves new content. Creates a version only when title, description, body or notes changed.</summary>
    public async Task<SaveResult> SaveContentAsync(Guid id, PromptContent content, CancellationToken cancellationToken = default)
    {
        var prompt = await RequireAsync(id, cancellationToken);
        var latest = await prompts.GetLatestVersionNumberAsync(id, cancellationToken);
        var previous = prompt.Content;
        if (previous == content)
        {
            return new SaveResult(false, latest);
        }

        var now = time.GetUtcNow();
        var newVersions = new List<PromptVersion>(2);
        if (latest == 0)
        {
            // Every prompt starts with a version, but keep the old state if one is somehow missing.
            newVersions.Add(PromptVersion.Snapshot(prompt, ++latest, VersionNotes.FirstVersion, prompt.UpdatedAt));
        }

        prompt.SetContent(content);
        prompt.UpdatedAt = now;
        newVersions.Add(PromptVersion.Snapshot(prompt, latest + 1, VersionNotes.Describe(previous, content), now));

        await prompts.UpdateAsync(prompt, newVersions, cancellationToken);
        logger.LogInformation("Saved prompt {PromptId} as version {Version}", id, latest + 1);
        return new SaveResult(true, latest + 1);
    }

    /// <summary>
    /// Makes an old version current by saving its content as a new version. The state being replaced is
    /// already in the history, so nothing is lost.
    /// </summary>
    public async Task<SaveResult> RestoreVersionAsync(Guid id, int versionNumber, CancellationToken cancellationToken = default)
    {
        var prompt = await RequireAsync(id, cancellationToken);
        var versions = await prompts.GetVersionsAsync(id, cancellationToken);
        var target = versions.FirstOrDefault(version => version.VersionNumber == versionNumber)
                     ?? throw new LibraryException($"Version {versionNumber} of this prompt no longer exists.");
        var latest = versions.Count == 0 ? 0 : versions.Max(version => version.VersionNumber);

        if (target.Content == prompt.Content)
        {
            return new SaveResult(false, latest);
        }

        var now = time.GetUtcNow();
        var newVersions = new List<PromptVersion>(2);
        var latestVersion = versions.FirstOrDefault(version => version.VersionNumber == latest);
        if (latestVersion is null || latestVersion.Content != prompt.Content)
        {
            newVersions.Add(PromptVersion.Snapshot(prompt, ++latest, "Saved before restoring", now));
        }

        prompt.SetContent(target.Content);
        prompt.UpdatedAt = now;
        newVersions.Add(PromptVersion.Snapshot(prompt, latest + 1, $"Restored v{versionNumber}", now));

        await prompts.UpdateAsync(prompt, newVersions, cancellationToken);
        logger.LogInformation("Restored prompt {PromptId} to version {Version}", id, versionNumber);
        return new SaveResult(true, latest + 1);
    }

    public async Task UpdateMetadataAsync(Guid id, PromptMetadata metadata, CancellationToken cancellationToken = default)
    {
        var prompt = await RequireAsync(id, cancellationToken);
        ApplyMetadata(prompt, metadata);
        await prompts.UpdateAsync(prompt, [], cancellationToken);
    }

    public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.IsFavorite = isFavorite, cancellationToken);

    public Task SetRatingAsync(Guid id, int? rating, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.Rating = rating, cancellationToken);

    public Task SetCollectionAsync(Guid id, Guid? collectionId, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.CollectionId = collectionId, cancellationToken);

    public Task SetTagsAsync(Guid id, IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.SetTags(tags), cancellationToken);

    public Task AddTagAsync(Guid id, string tag, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.SetTags(prompt.Tags.Append(tag)), cancellationToken);

    public Task RemoveTagAsync(Guid id, string tag, CancellationToken cancellationToken = default)
    {
        var normalized = TagName.Normalize(tag);
        return ModifyAsync(id, prompt => prompt.SetTags(prompt.Tags.Where(existing => existing != normalized)), cancellationToken);
    }

    public async Task<Prompt> DuplicateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await RequireAsync(id, cancellationToken);
        var content = new PromptContent(source.Title + " (copy)", source.Description, source.Body, source.Notes);
        var metadata = PromptMetadata.From(source) with { IsFavorite = false };
        return await CreateAsync(content, metadata, $"Duplicated from “{source.Title}”", cancellationToken: cancellationToken);
    }

    public async Task<Prompt> DuplicateVersionAsync(Guid id, int versionNumber, CancellationToken cancellationToken = default)
    {
        var source = await RequireAsync(id, cancellationToken);
        var versions = await prompts.GetVersionsAsync(id, cancellationToken);
        var version = versions.FirstOrDefault(candidate => candidate.VersionNumber == versionNumber)
                      ?? throw new LibraryException($"Version {versionNumber} of this prompt no longer exists.");
        var content = new PromptContent($"{version.Title} (v{versionNumber} copy)", version.Description, version.Body, version.Notes);
        var metadata = PromptMetadata.From(source) with { IsFavorite = false };
        return await CreateAsync(content, metadata, $"Duplicated from v{versionNumber} of “{source.Title}”", cancellationToken: cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await prompts.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Deleted prompt {PromptId}", id);
    }

    public Task MarkOpenedAsync(Guid id, CancellationToken cancellationToken = default) =>
        prompts.MarkOpenedAsync(id, time.GetUtcNow(), cancellationToken);

    private async Task ModifyAsync(Guid id, Action<Prompt> change, CancellationToken cancellationToken)
    {
        var prompt = await RequireAsync(id, cancellationToken);
        change(prompt);
        await prompts.UpdateAsync(prompt, [], cancellationToken);
    }

    private async Task<Prompt> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await prompts.GetAsync(id, cancellationToken) ?? throw new LibraryException("That prompt no longer exists.");

    private static void ApplyMetadata(Prompt prompt, PromptMetadata metadata)
    {
        prompt.IsFavorite = metadata.IsFavorite;
        prompt.Rating = metadata.Rating;
        prompt.CollectionId = metadata.CollectionId;
        prompt.SetTags(metadata.Tags);
    }
}
