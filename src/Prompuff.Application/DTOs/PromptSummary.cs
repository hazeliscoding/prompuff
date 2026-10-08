namespace Prompuff.Application.DTOs;

/// <summary>Another prompt mentioned by name, such as a parent or a copy.</summary>
public sealed record PromptLink(Guid Id, string Title, bool IsDeleted);

/// <param name="Parent">The prompt this one was duplicated from, while it still exists.</param>
/// <param name="Copies">Prompts duplicated from this one.</param>
public sealed record PromptLineage(PromptLink? Parent, IReadOnlyList<PromptLink> Copies)
{
    public static PromptLineage None { get; } = new(null, []);
}

public sealed record PromptSummary
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsFavorite { get; init; }
    public int? Rating { get; init; }
    public Guid? CollectionId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastOpenedAt { get; init; }
    public DateTimeOffset? DeletedAt { get; init; }

    public DateTimeOffset LastActivity => LastOpenedAt is { } opened && opened > UpdatedAt ? opened : UpdatedAt;
}
