using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Prompuff.Infrastructure.ImportExport;

/// <summary>A prompt as it appears in a portable Markdown file.</summary>
public sealed record MarkdownPrompt
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Body { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Collection { get; init; }
    public bool IsFavorite { get; init; }
    public int? Rating { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>A file Prompuff can't read as a prompt. The message is safe to show the user.</summary>
public sealed class MarkdownFormatException(string message) : Exception(message);

/// <summary>
/// Reads and writes prompts as Markdown with YAML-style frontmatter, followed by <c># Prompt</c> and
/// <c># Notes</c> sections. Only the small subset of YAML that this format uses is supported.
/// </summary>
public static partial class MarkdownPromptFormat
{
    private const string PromptHeading = "# Prompt";
    private const string NotesHeading = "# Notes";

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 .,()'&+/!?@\-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainScalar();

    [GeneratedRegex(@"^(?<key>[A-Za-z_][A-Za-z0-9_\-]*)[ \t]*:(?:[ \t]+(?<value>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyLine();

    public static string Write(MarkdownPrompt prompt)
    {
        var text = new StringBuilder();
        text.Append("---\n");
        text.Append("title: ").Append(Quote(prompt.Title)).Append('\n');
        if (!string.IsNullOrWhiteSpace(prompt.Description))
        {
            text.Append("description: ").Append(Quote(prompt.Description)).Append('\n');
        }

        if (prompt.Tags.Count > 0)
        {
            text.Append("tags:\n");
            foreach (var tag in prompt.Tags)
            {
                text.Append("  - ").Append(Quote(tag)).Append('\n');
            }
        }

        if (!string.IsNullOrWhiteSpace(prompt.Collection))
        {
            text.Append("collection: ").Append(Quote(prompt.Collection)).Append('\n');
        }

        text.Append("favorite: ").Append(prompt.IsFavorite ? "true" : "false").Append('\n');
        if (prompt.Rating is { } rating)
        {
            text.Append("rating: ").Append(rating.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        if (prompt.CreatedAt is { } created)
        {
            text.Append("createdAt: ").Append(FormatTime(created)).Append('\n');
        }

        if (prompt.UpdatedAt is { } updated)
        {
            text.Append("updatedAt: ").Append(FormatTime(updated)).Append('\n');
        }

        text.Append("---\n\n");
        text.Append(PromptHeading).Append("\n\n");
        var body = Normalize(prompt.Body).Trim('\n');
        if (body.Length > 0)
        {
            text.Append(body).Append("\n\n");
        }

        // The reader takes the last "# Notes" heading as the start of the notes, so write one whenever the
        // body contains its own, even if there are no notes.
        var notes = Normalize(prompt.Notes ?? string.Empty).Trim('\n');
        if (notes.Length > 0 || HasHeadingLine(body, NotesHeading))
        {
            text.Append(NotesHeading).Append("\n\n");
            if (notes.Length > 0)
            {
                text.Append(notes).Append('\n');
            }
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    /// <param name="fallbackTitle">Used when the file has no title, usually the file name.</param>
    public static MarkdownPrompt Read(string text, string fallbackTitle)
    {
        if (text.Contains('\0'))
        {
            throw new MarkdownFormatException("This doesn't look like a text file.");
        }

        var lines = Normalize(text).TrimStart('﻿').Split('\n');
        var fields = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var contentStart = 0;

        if (lines.Length > 0 && lines[0].TrimEnd() == "---")
        {
            var end = Array.FindIndex(lines, 1, line => line.TrimEnd() is "---" or "...");
            if (end < 0)
            {
                throw new MarkdownFormatException("The metadata block at the top of the file is never closed with ---.");
            }

            ParseFrontmatter(lines[1..end], fields);
            contentStart = end + 1;
        }

        var content = lines[contentStart..];
        var promptStart = Array.FindIndex(content, line => IsHeading(line, PromptHeading));
        var bodyStart = promptStart < 0 ? 0 : promptStart + 1;
        var notesStart = Array.FindLastIndex(content, line => IsHeading(line, NotesHeading));
        if (notesStart < bodyStart)
        {
            notesStart = -1;
        }

        var bodyLines = notesStart < 0 ? content[bodyStart..] : content[bodyStart..notesStart];
        var body = string.Join('\n', bodyLines).Trim('\n').TrimEnd();
        var notes = notesStart < 0 ? null : string.Join('\n', content[(notesStart + 1)..]).Trim('\n').TrimEnd();

        var title = GetString(fields, "title");
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body))
        {
            throw new MarkdownFormatException("The file is empty.");
        }

        return new MarkdownPrompt
        {
            Title = string.IsNullOrWhiteSpace(title) ? fallbackTitle : title.Trim(),
            Description = GetString(fields, "description"),
            Body = body,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            Tags = GetList(fields, "tags"),
            Collection = GetString(fields, "collection"),
            IsFavorite = GetBool(fields, "favorite"),
            Rating = GetRating(fields),
            CreatedAt = GetTime(fields, "createdAt"),
            UpdatedAt = GetTime(fields, "updatedAt"),
        };
    }

    internal static void ParseFrontmatter(string[] lines, Dictionary<string, object> fields)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var match = KeyLine().Match(line.TrimEnd());
            if (!match.Success)
            {
                if (char.IsWhiteSpace(line[0]))
                {
                    continue; // Indented continuation of something we don't use.
                }

                throw new MarkdownFormatException($"Line {i + 2} of the metadata block isn't a “key: value” pair.");
            }

            var key = match.Groups["key"].Value;
            var raw = match.Groups["value"].Success ? StripComment(match.Groups["value"].Value).Trim() : string.Empty;

            if (raw.Length == 0)
            {
                var items = new List<string>();
                while (i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    i++;
                    items.Add(Unquote(StripComment(lines[i].TrimStart()[2..]).Trim()));
                }

                if (items.Count > 0)
                {
                    fields[key] = items;
                }

                continue;
            }

            if (raw is "|" or ">" or "|-" or ">-")
            {
                var block = new List<string>();
                while (i + 1 < lines.Length && (lines[i + 1].Length == 0 || char.IsWhiteSpace(lines[i + 1][0])))
                {
                    i++;
                    block.Add(lines[i].Trim());
                }

                fields[key] = string.Join(raw.StartsWith('|') ? "\n" : " ", block).Trim();
                continue;
            }

            if (raw.StartsWith('[') && raw.EndsWith(']'))
            {
                fields[key] = raw[1..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(Unquote)
                    .ToList();
                continue;
            }

            fields[key] = Unquote(raw);
        }
    }

    internal static string? GetString(Dictionary<string, object> fields, string key) =>
        fields.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static IReadOnlyList<string> GetList(Dictionary<string, object> fields, string key) => fields.TryGetValue(key, out var value)
        ? value switch
        {
            List<string> list => list,
            string text => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            _ => [],
        }
        : [];

    private static bool GetBool(Dictionary<string, object> fields, string key) =>
        GetString(fields, key)?.ToLowerInvariant() is "true" or "yes" or "on";

    private static int? GetRating(Dictionary<string, object> fields) =>
        int.TryParse(GetString(fields, "rating"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating) && rating is >= 1 and <= 5
            ? rating
            : null;

    private static DateTimeOffset? GetTime(Dictionary<string, object> fields, string key) =>
        DateTimeOffset.TryParse(GetString(fields, key), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            ? time.ToUniversalTime()
            : null;

    internal static string Quote(string value)
    {
        var single = Normalize(value).Replace('\n', ' ').Trim();
        if (PlainScalar().IsMatch(single) && !IsReservedWord(single) && !single.EndsWith(' '))
        {
            return single;
        }

        return "\"" + single.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t") + "\"";
    }

    private static bool IsReservedWord(string value) =>
        value.ToLowerInvariant() is "true" or "false" or "yes" or "no" or "on" or "off" or "null" or "~"
        || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var inner = value[1..^1];
            var result = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length)
                {
                    i++;
                    result.Append(inner[i] switch { 'n' => '\n', 't' => '\t', var other => other });
                }
                else
                {
                    result.Append(inner[i]);
                }
            }

            return result.ToString();
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'");
        }

        return value;
    }

    /// <summary>Drops a trailing " # comment" that isn't inside quotes.</summary>
    private static string StripComment(string value)
    {
        var inDouble = false;
        var inSingle = false;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '"' when !inSingle && (i == 0 || value[i - 1] != '\\'):
                    inDouble = !inDouble;
                    break;
                case '\'' when !inDouble:
                    inSingle = !inSingle;
                    break;
                case '#' when !inDouble && !inSingle && i > 0 && char.IsWhiteSpace(value[i - 1]):
                    return value[..i];
            }
        }

        return value;
    }

    private static bool IsHeading(string line, string heading) =>
        string.Equals(line.Trim(), heading, StringComparison.OrdinalIgnoreCase);

    private static bool HasHeadingLine(string text, string heading) =>
        text.Split('\n').Any(line => IsHeading(line, heading));

    private static string FormatTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    internal static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');
}
