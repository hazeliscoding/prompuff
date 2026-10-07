using Prompuff.Domain.ValueObjects;

namespace Prompuff.Domain.Entities;

/// <summary>A saved state of a prompt's content. The highest version number is the current one.</summary>
public sealed record PromptVersion
{
    public required Guid Id { get; init; }
    public required Guid PromptId { get; init; }
    public required int VersionNumber { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Body { get; init; }
    public string? Notes { get; init; }

    /// <summary>A short, generated summary of what changed, such as "Edited body (+3 −1 lines)".</summary>
    public string? Note { get; init; }

    public required DateTimeOffset SavedAt { get; init; }

    public PromptContent Content => new(Title, Description, Body, Notes);

    public static PromptVersion Snapshot(Prompt prompt, int versionNumber, string? note, DateTimeOffset savedAt) => new()
    {
        Id = Guid.NewGuid(),
        PromptId = prompt.Id,
        VersionNumber = versionNumber,
        Title = prompt.Title,
        Description = prompt.Description,
        Body = prompt.Body,
        Notes = prompt.Notes,
        Note = note,
        SavedAt = savedAt,
    };
}
