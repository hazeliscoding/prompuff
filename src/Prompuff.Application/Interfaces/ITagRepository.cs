using Prompuff.Application.DTOs;

namespace Prompuff.Application.Interfaces;

public interface ITagRepository
{
    /// <summary>Tags in use with their prompt counts, most used first.</summary>
    Task<IReadOnlyList<TagSummary>> ListAsync(CancellationToken cancellationToken = default);
}
