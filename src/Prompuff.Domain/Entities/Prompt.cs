using Prompuff.Domain.ValueObjects;

namespace Prompuff.Domain.Entities;

public sealed class Prompt
{
    private int? _rating;

    public required Guid Id { get; init; }
    public string Title { get; private set; } = PromptContent.UntitledTitle;
    public string? Description { get; private set; }
    public string Body { get; private set; } = string.Empty;

    /// <summary>"Why this worked": notes to your future self.</summary>
    public string? Notes { get; private set; }

    public bool IsFavorite { get; set; }

    public int? Rating
    {
        get => _rating;
        set => _rating = ValueObjects.Rating.Validate(value);
    }

    public Guid? CollectionId { get; set; }

    /// <summary>Normalized tag names, in the order they were added.</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }

    /// <summary>When the prompt moved to Recently deleted, or null while it's in the library.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>The prompt this one was duplicated from. It goes back to null if the parent is removed for good.</summary>
    public Guid? ParentPromptId { get; init; }

    public PromptContent Content => new(Title, Description, Body, Notes);

    public void SetContent(PromptContent content)
    {
        Title = content.Title;
        Description = content.Description;
        Body = content.Body;
        Notes = content.Notes;
    }

    public void SetTags(IEnumerable<string?> tags) => Tags = TagName.NormalizeAll(tags);
}
