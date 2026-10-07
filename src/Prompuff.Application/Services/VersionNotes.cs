using Prompuff.Domain.ValueObjects;

namespace Prompuff.Application.Services;

/// <summary>Builds the short note stored with each version, such as "Edited title and body (+3 −1 lines)".</summary>
public static class VersionNotes
{
    public const string FirstVersion = "First version";

    public static string Describe(PromptContent previous, PromptContent current)
    {
        var parts = new List<string>();
        if (previous.Title != current.Title)
        {
            parts.Add("title");
        }

        if (previous.Description != current.Description)
        {
            parts.Add("description");
        }

        if (previous.Body != current.Body)
        {
            var (added, removed) = LineDiff.CountChanges(previous.Body, current.Body);
            parts.Add($"body (+{added} −{removed} lines)");
        }

        if (previous.Notes != current.Notes)
        {
            parts.Add("notes");
        }

        return parts.Count == 0 ? "No content changes" : "Edited " + JoinWithAnd(parts);
    }

    private static string JoinWithAnd(IReadOnlyList<string> parts) => parts.Count switch
    {
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
