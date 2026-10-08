namespace Prompuff.Domain.ValueObjects;

/// <summary>
/// The versioned part of a prompt. Two contents that are equal after normalization
/// are the same version, so saving one over the other is a no-op.
/// </summary>
public sealed record PromptContent
{
    public const string UntitledTitle = "Untitled prompt";

    public string Title { get; }
    public string? Description { get; }
    public string Body { get; }
    public string? Notes { get; }

    public PromptContent(string? title, string? description, string? body, string? notes)
    {
        var trimmedTitle = NormalizeLine(title);
        Title = string.IsNullOrEmpty(trimmedTitle) ? UntitledTitle : trimmedTitle;
        Description = NullIfBlank(NormalizeLine(description));
        Body = NormalizeNewlines(body ?? string.Empty);
        Notes = NullIfBlank(NormalizeNewlines(notes ?? string.Empty).Trim());
    }

    /// <summary>
    /// A title from a prompt's first line, for Quick save and <c>prompuff quick-save</c>: Markdown markers are dropped,
    /// and a long line is cut at a word near 60 characters.
    /// </summary>
    public static string SuggestTitle(string text)
    {
        var line = text.Split('\n').Select(candidate => candidate.Trim().TrimStart('#', '-', '*', '>').Trim())
            .FirstOrDefault(candidate => candidate.Length > 0) ?? string.Empty;
        if (line.Length <= 60)
        {
            return line;
        }

        var cut = line[..60];
        var space = cut.LastIndexOf(' ');
        return (space > 30 ? cut[..space] : cut).TrimEnd(',', '.', ':', ';') + "…";
    }

    private static string NormalizeLine(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
