namespace Prompuff.Application.DTOs;

public enum PromptFilterKind
{
    All,
    Favorites,
    Recent,
    Collection,
    Uncategorized,
    Tag,
}

public enum PromptSort
{
    LastEdited,
    Title,
    Usefulness,
    RecentActivity,
}

public sealed record PromptQuery
{
    /// <summary>How many prompts the Recent filter shows.</summary>
    public const int RecentLimit = 20;

    public string? Text { get; init; }
    public PromptFilterKind Filter { get; init; } = PromptFilterKind.All;
    public Guid? CollectionId { get; init; }
    public string? Tag { get; init; }
    public PromptSort Sort { get; init; } = PromptSort.LastEdited;

    public static PromptQuery All { get; } = new();
}
