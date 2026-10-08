using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.Application.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Keeps prompts in memory and copies them in and out, like a real store would.</summary>
internal sealed class InMemoryPromptRepository : IPromptRepository
{
    private readonly Dictionary<Guid, Prompt> _prompts = [];
    private readonly List<PromptVersion> _versions = [];

    public IReadOnlyList<PromptVersion> AllVersions => _versions;

    public Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_prompts.TryGetValue(id, out var prompt) ? Copy(prompt) : null);

    public Task InsertAsync(Prompt prompt, PromptVersion firstVersion, CancellationToken cancellationToken = default)
    {
        _prompts.Add(prompt.Id, Copy(prompt));
        _versions.Add(firstVersion);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Prompt prompt, IReadOnlyList<PromptVersion> newVersions, CancellationToken cancellationToken = default)
    {
        _prompts[prompt.Id] = Copy(prompt);
        _versions.AddRange(newVersions);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _prompts.Remove(id);
        _versions.RemoveAll(version => version.PromptId == id);
        return Task.CompletedTask;
    }

    public Task<bool> HasPromptAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        static string Normalize(string text) => text.Replace("\r", string.Empty).Trim('\n').TrimEnd();
        return Task.FromResult(_prompts.Values.Any(p => p.DeletedAt is null && p.Title == title.Trim() && Normalize(p.Body) == Normalize(body)));
    }

    public Task<Guid?> FindPromptAsync(string title, string body, CancellationToken cancellationToken = default)
    {
        static string Normalize(string text) => text.Replace("\r", string.Empty).Trim('\n').TrimEnd();
        return Task.FromResult(_prompts.Values
            .Where(p => p.DeletedAt is null && p.Title == title.Trim() && Normalize(p.Body) == Normalize(body))
            .OrderBy(p => p.CreatedAt)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefault());
    }

    public Task<int> PurgeDeletedAsync(DateTimeOffset? deletedBefore, CancellationToken cancellationToken = default)
    {
        var purged = _prompts.Values.Where(p => p.DeletedAt is { } deleted && (deletedBefore is null || deleted < deletedBefore)).Select(p => p.Id).ToList();
        foreach (var id in purged)
        {
            DeleteAsync(id, cancellationToken);
        }

        return Task.FromResult(purged.Count);
    }

    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PromptVersion>>(
            _versions.Where(version => version.PromptId == promptId).OrderByDescending(version => version.VersionNumber).ToList());

    public Task<IReadOnlyList<PromptLink>> GetCopiesAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PromptLink>>(_prompts.Values
            .Where(p => p.ParentPromptId == promptId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PromptLink(p.Id, p.Title, p.DeletedAt is not null))
            .ToList());

    public Task<int> GetLatestVersionNumberAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_versions.Where(version => version.PromptId == promptId).Select(version => version.VersionNumber).DefaultIfEmpty(0).Max());

    public Task MarkOpenedAsync(Guid id, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        _prompts[id].LastOpenedAt = openedAt;
        return Task.CompletedTask;
    }

    public Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LibraryCounts(
            _prompts.Values.Count(p => p.DeletedAt is null),
            _prompts.Values.Count(p => p.DeletedAt is null && p.IsFavorite),
            _prompts.Values.Count(p => p.DeletedAt is null && p.CollectionId is null),
            _versions.Count(v => _prompts[v.PromptId].DeletedAt is null),
            _prompts.Values.Count(p => p.DeletedAt is not null)));

    private static Prompt Copy(Prompt source)
    {
        var copy = new Prompt
        {
            Id = source.Id,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            LastOpenedAt = source.LastOpenedAt,
            DeletedAt = source.DeletedAt,
            IsFavorite = source.IsFavorite,
            Rating = source.Rating,
            CollectionId = source.CollectionId,
            ParentPromptId = source.ParentPromptId,
        };
        copy.SetContent(new PromptContent(source.Title, source.Description, source.Body, source.Notes));
        copy.SetTags(source.Tags);
        return copy;
    }
}
