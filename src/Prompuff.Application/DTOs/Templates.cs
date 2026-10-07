namespace Prompuff.Application.DTOs;

public sealed record TemplateVariable(string Name, int Occurrences);

public enum TemplateSegmentKind
{
    Text,
    FilledVariable,
    MissingVariable,
}

/// <param name="Text">The literal text, the substituted value, or the original token when unresolved.</param>
/// <param name="VariableName">Set for variable segments.</param>
public sealed record TemplateSegment(TemplateSegmentKind Kind, string Text, string? VariableName = null);
