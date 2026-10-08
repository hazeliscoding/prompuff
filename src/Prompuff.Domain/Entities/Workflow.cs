namespace Prompuff.Domain.Entities;

/// <summary>
/// Prompts that run in order, with a person copying each one into a model and carrying the answer to the next step.
/// Prompuff never runs a model itself.
/// </summary>
public sealed class Workflow
{
    public const int MaxNameLength = 80;
    public const string UntitledName = "Untitled workflow";

    public required Guid Id { get; init; }
    public string Name { get; set; } = UntitledName;
    public string? Description { get; set; }

    /// <summary>The steps in order. A prompt may appear in more than one step.</summary>
    public List<WorkflowStep> Steps { get; } = [];

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Trims the name, collapses inner whitespace and caps the length. A blank name becomes "Untitled workflow".</summary>
    public static string NormalizeName(string? raw)
    {
        var name = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            return UntitledName;
        }

        return name.Length <= MaxNameLength ? name : name[..MaxNameLength].TrimEnd();
    }

    /// <summary>One line, trimmed; null when blank.</summary>
    public static string? NormalizeDescription(string? raw)
    {
        var text = string.Join(' ', (raw ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length == 0 ? null : text;
    }
}

/// <param name="Note">What this step hands to the next, such as "the phased plan, pasted into {{plan}}".</param>
public sealed record WorkflowStep(Guid Id, Guid PromptId, string? Note = null)
{
    /// <summary>Trims the note and keeps its line breaks; null when blank.</summary>
    public static string? NormalizeNote(string? raw)
    {
        var text = (raw ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length == 0 ? null : text;
    }
}
