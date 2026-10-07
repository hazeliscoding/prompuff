using System.Text;

namespace Prompuff.Domain.ValueObjects;

/// <summary>
/// Tag names are compared in normalized form: trimmed, lowercase, without a leading '#',
/// with inner whitespace turned into '-'. "Angular", "angular" and " #angular" are one tag.
/// </summary>
public static class TagName
{
    public const int MaxLength = 40;

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim().TrimStart('#').Trim();
        var builder = new StringBuilder(value.Length);
        var pendingDash = false;

        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == ',' || char.IsControl(ch))
            {
                pendingDash = builder.Length > 0;
                continue;
            }

            if (pendingDash)
            {
                builder.Append('-');
                pendingDash = false;
            }

            builder.Append(char.ToLowerInvariant(ch));
        }

        var normalized = builder.ToString().Trim('-');
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= MaxLength ? normalized : normalized[..MaxLength].TrimEnd('-');
    }

    /// <summary>Normalizes every name, drops blanks and duplicates, and keeps first-seen order.</summary>
    public static IReadOnlyList<string> NormalizeAll(IEnumerable<string?> raw)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var name in raw)
        {
            var normalized = Normalize(name);
            if (normalized is not null && seen.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result;
    }
}
