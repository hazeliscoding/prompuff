namespace Prompuff.Application.DTOs;

public enum DiffLineKind
{
    Unchanged,
    Added,
    Removed,
}

/// <param name="OldNumber">1-based line number in the old text, when the line exists there.</param>
/// <param name="NewNumber">1-based line number in the new text, when the line exists there.</param>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldNumber, int? NewNumber);
