using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Domain.Entities;

namespace Prompuff.Application.Services;

public sealed class CollectionService(ICollectionRepository collections, TimeProvider time, ILogger<CollectionService> logger)
{
    public Task<IReadOnlyList<CollectionSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        collections.ListAsync(cancellationToken);

    public async Task<Collection> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = RequireName(name);
        if (await collections.FindByNameAsync(normalized, cancellationToken) is not null)
        {
            throw new LibraryException($"There's already a collection called “{normalized}”.");
        }

        var collection = new Collection { Id = Guid.NewGuid(), Name = normalized, CreatedAt = time.GetUtcNow() };
        await collections.InsertAsync(collection, cancellationToken);
        logger.LogInformation("Created collection {CollectionId}", collection.Id);
        return collection;
    }

    /// <summary>Returns the collection with this name (ignoring case), creating it when it doesn't exist.</summary>
    public async Task<Collection> GetOrCreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = RequireName(name);
        return await collections.FindByNameAsync(normalized, cancellationToken) ?? await CreateAsync(normalized, cancellationToken);
    }

    public async Task RenameAsync(Guid id, string name, CancellationToken cancellationToken = default)
    {
        var normalized = RequireName(name);
        var existing = await collections.FindByNameAsync(normalized, cancellationToken);
        if (existing is not null && existing.Id != id)
        {
            throw new LibraryException($"There's already a collection called “{normalized}”.");
        }

        await collections.RenameAsync(id, normalized, cancellationToken);
    }

    /// <summary>Deletes the collection. Its prompts become uncategorized.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await collections.DeleteAsync(id, cancellationToken);
        logger.LogInformation("Deleted collection {CollectionId}", id);
    }

    private static string RequireName(string name) =>
        Collection.NormalizeName(name) ?? throw new LibraryException("Give the collection a name.");
}
