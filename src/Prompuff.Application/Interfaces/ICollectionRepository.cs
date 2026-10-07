using Prompuff.Application.DTOs;
using Prompuff.Domain.Entities;

namespace Prompuff.Application.Interfaces;

public interface ICollectionRepository
{
    /// <summary>All collections with their prompt counts, sorted by name.</summary>
    Task<IReadOnlyList<CollectionSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<Collection?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Finds a collection by name, ignoring case.</summary>
    Task<Collection?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task InsertAsync(Collection collection, CancellationToken cancellationToken = default);
    Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection. Its prompts stay and become uncategorized.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
