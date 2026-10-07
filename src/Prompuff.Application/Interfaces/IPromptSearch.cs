using Prompuff.Application.DTOs;

namespace Prompuff.Application.Interfaces;

/// <summary>
/// Library search and filtering. The first implementation uses SQL LIKE; an FTS5 index can replace it
/// without touching callers.
/// </summary>
public interface IPromptSearch
{
    Task<IReadOnlyList<PromptSummary>> SearchAsync(PromptQuery query, CancellationToken cancellationToken = default);
}
