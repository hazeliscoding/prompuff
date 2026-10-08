using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
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
    /// <summary>How long a deleted prompt waits in Recently deleted.</summary>
    public static readonly TimeSpan DeletedRetention = TimeSpan.FromDays(30);

    public Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        prompts.GetAsync(id, cancellationToken);

    /// <inheritdoc cref="IPromptRepository.FindPromptAsync"/>
    public Task<Guid?> FindAsync(string title, string body, CancellationToken cancellationToken = default) =>
        prompts.FindPromptAsync(title, body, cancellationToken);

    /// <inheritdoc cref="IPromptRepository.HasPromptAsync"/>
    public Task<bool> HasPromptAsync(string title, string body, CancellationToken cancellationToken = default) =>
        prompts.HasPromptAsync(title, body, cancellationToken);

    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid id, CancellationToken cancellationToken = default) =>
        prompts.GetVersionsAsync(id, cancellationToken);

    public async Task<Prompt> CreateAsync(
        PromptContent content,
        PromptMetadata? metadata = null,
        string? versionNote = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        Guid? parentPromptId = null,
        CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        var created = createdAt ?? now;
        var updated = updatedAt is { } value && value >= created ? value : created;
        var prompt = new Prompt { Id = Guid.NewGuid(), CreatedAt = created, UpdatedAt = updated, ParentPromptId = parentPromptId };
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

    /// <summary>Adds tags after the ones the prompt already has. Tags it already has stay where they are.</summary>
    public Task AddTagsAsync(Guid id, IEnumerable<string> tags, CancellationToken cancellationToken = default) =>
        ModifyAsync(id, prompt => prompt.SetTags(prompt.Tags.Concat(tags)), cancellationToken);

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
        return await CreateAsync(content, metadata, $"Duplicated from “{source.Title}”", parentPromptId: source.Id, cancellationToken: cancellationToken);
    }

    public async Task<Prompt> DuplicateVersionAsync(Guid id, int versionNumber, CancellationToken cancellationToken = default)
    {
        var source = await RequireAsync(id, cancellationToken);
        var versions = await prompts.GetVersionsAsync(id, cancellationToken);
        var version = versions.FirstOrDefault(candidate => candidate.VersionNumber == versionNumber)
                      ?? throw new LibraryException($"Version {versionNumber} of this prompt no longer exists.");
        var content = new PromptContent($"{version.Title} (v{versionNumber} copy)", version.Description, version.Body, version.Notes);
        var metadata = PromptMetadata.From(source) with { IsFavorite = false };
        return await CreateAsync(content, metadata, $"Duplicated from v{versionNumber} of “{source.Title}”", parentPromptId: source.Id, cancellationToken: cancellationToken);
    }

    /// <summary>Where the prompt was duplicated from, and the copies made from it.</summary>
    public async Task<PromptLineage> GetLineageAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var prompt = await RequireAsync(id, cancellationToken);
        PromptLink? parent = null;
        if (prompt.ParentPromptId is { } parentId && await prompts.GetAsync(parentId, cancellationToken) is { } source)
        {
            parent = new PromptLink(source.Id, source.Title, source.DeletedAt is not null);
        }

        return new PromptLineage(parent, await prompts.GetCopiesAsync(id, cancellationToken));
    }

    /// <summary>Moves the prompt to Recently deleted, where it waits <see cref="DeletedRetention"/> before it's removed for good.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        await ModifyAsync(id, prompt => prompt.DeletedAt = now, cancellationToken);
        logger.LogInformation("Moved prompt {PromptId} to Recently deleted", id);
    }

    public async Task RestoreDeletedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await ModifyAsync(id, prompt => prompt.DeletedAt = null, cancellationToken);
        logger.LogInformation("Restored prompt {PromptId} from Recently deleted", id);
    }

    /// <summary>Removes everything in Recently deleted for good. Returns how many prompts went.</summary>
    public async Task<int> EmptyRecentlyDeletedAsync(CancellationToken cancellationToken = default)
    {
        var purged = await prompts.PurgeDeletedAsync(null, cancellationToken);
        logger.LogInformation("Emptied Recently deleted: {Count} prompts", purged);
        return purged;
    }

    /// <summary>Removes prompts that have been in Recently deleted longer than <see cref="DeletedRetention"/>.</summary>
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var purged = await prompts.PurgeDeletedAsync(time.GetUtcNow() - DeletedRetention, cancellationToken);
        if (purged > 0)
        {
            logger.LogInformation("Removed {Count} prompts deleted more than {Days} days ago", purged, DeletedRetention.Days);
        }

        return purged;
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
