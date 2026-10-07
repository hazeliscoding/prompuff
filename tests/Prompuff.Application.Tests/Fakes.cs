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

    public Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PromptVersion>>(
            _versions.Where(version => version.PromptId == promptId).OrderByDescending(version => version.VersionNumber).ToList());

    public Task<int> GetLatestVersionNumberAsync(Guid promptId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_versions.Where(version => version.PromptId == promptId).Select(version => version.VersionNumber).DefaultIfEmpty(0).Max());

    public Task MarkOpenedAsync(Guid id, DateTimeOffset openedAt, CancellationToken cancellationToken = default)
    {
        _prompts[id].LastOpenedAt = openedAt;
        return Task.CompletedTask;
    }

    public Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new LibraryCounts(_prompts.Count, _prompts.Values.Count(p => p.IsFavorite), _prompts.Values.Count(p => p.CollectionId is null), _versions.Count));

    private static Prompt Copy(Prompt source)
    {
        var copy = new Prompt
        {
            Id = source.Id,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            LastOpenedAt = source.LastOpenedAt,
            IsFavorite = source.IsFavorite,
            Rating = source.Rating,
            CollectionId = source.CollectionId,
        };
        copy.SetContent(new PromptContent(source.Title, source.Description, source.Body, source.Notes));
        copy.SetTags(source.Tags);
        return copy;
    }
}
