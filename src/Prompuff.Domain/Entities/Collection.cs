namespace Prompuff.Domain.Entities;

public sealed record Collection
{
    public const int MaxNameLength = 60;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Trims the name and collapses inner whitespace. Returns null when nothing is left.</summary>
    public static string? NormalizeName(string? raw)
    {
        var name = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            return null;
        }

        return name.Length <= MaxNameLength ? name : name[..MaxNameLength].TrimEnd();
    }
}
