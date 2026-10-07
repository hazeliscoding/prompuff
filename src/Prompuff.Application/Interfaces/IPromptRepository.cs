using Prompuff.Application.DTOs;
using Prompuff.Domain.Entities;

namespace Prompuff.Application.Interfaces;

public interface IPromptRepository
{
    Task<Prompt?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Inserts the prompt, its tags and its first version in one transaction.</summary>
    Task InsertAsync(Prompt prompt, PromptVersion firstVersion, CancellationToken cancellationToken = default);

    /// <summary>Saves every prompt field and its tags, plus any new versions, in one transaction.</summary>
    Task UpdateAsync(Prompt prompt, IReadOnlyList<PromptVersion> newVersions, CancellationToken cancellationToken = default);

    /// <summary>Deletes the prompt for good, with its versions and tag links. Tags left with no prompts are deleted.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes prompts in Recently deleted for good: those deleted before <paramref name="deletedBefore"/>, or all of them. Returns how many.</summary>
    Task<int> PurgeDeletedAsync(DateTimeOffset? deletedBefore, CancellationToken cancellationToken = default);

    /// <summary>Versions of one prompt, newest first.</summary>
    Task<IReadOnlyList<PromptVersion>> GetVersionsAsync(Guid promptId, CancellationToken cancellationToken = default);

    Task<int> GetLatestVersionNumberAsync(Guid promptId, CancellationToken cancellationToken = default);

    Task MarkOpenedAsync(Guid id, DateTimeOffset openedAt, CancellationToken cancellationToken = default);

    Task<LibraryCounts> GetCountsAsync(CancellationToken cancellationToken = default);
}
