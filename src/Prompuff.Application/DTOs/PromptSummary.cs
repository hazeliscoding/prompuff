namespace Prompuff.Application.DTOs;

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
