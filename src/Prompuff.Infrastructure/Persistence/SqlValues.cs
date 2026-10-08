using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Prompuff.Infrastructure.Persistence;

/// <summary>How values are stored: GUIDs as text, times as ISO 8601 UTC text, booleans as 0 or 1.</summary>
internal static class SqlValues
{
    public static string Id(Guid id) => id.ToString("D");

    public static object IdOrNull(Guid? id) => id is { } value ? Id(value) : DBNull.Value;

    public static string Time(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public static object TimeOrNull(DateTimeOffset? value) => value is { } time ? Time(time) : DBNull.Value;

    public static object TextOrNull(string? value) => value is null ? DBNull.Value : value;

    public static object IntOrNull(int? value) => value is { } number ? number : DBNull.Value;

    public static Guid ReadId(this SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal));

    public static Guid? ReadIdOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    public static DateTimeOffset ReadTime(this SqliteDataReader reader, int ordinal) => ParseTime(reader.GetString(ordinal));

    /// <summary>
    /// Reads a stored time. Times written by <see cref="Time"/> are read field by field, which is many times faster than
    /// the general parser and adds up across a large library. Any other shape still goes through the general parser.
    /// </summary>
    public static DateTimeOffset ParseTime(string text) =>
        TryParseStoredTime(text, out var time)
            ? time
            : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // yyyy-MM-ddTHH:mm:ss.fffffffZ
    private static bool TryParseStoredTime(ReadOnlySpan<char> text, out DateTimeOffset time)
    {
        time = default;
        if (text.Length != 28 || text[4] != '-' || text[7] != '-' || text[10] != 'T' || text[13] != ':' || text[16] != ':' || text[19] != '.' || text[27] != 'Z'
            || !Digits(text[..4], out var year) || !Digits(text.Slice(5, 2), out var month) || !Digits(text.Slice(8, 2), out var day)
            || !Digits(text.Slice(11, 2), out var hour) || !Digits(text.Slice(14, 2), out var minute) || !Digits(text.Slice(17, 2), out var second)
            || !Digits(text.Slice(20, 7), out var fraction)
            || year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) || hour > 23 || minute > 59 || second > 59)
        {
            return false;
        }

        time = new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).Ticks + fraction, TimeSpan.Zero);
        return true;
    }

    private static bool Digits(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        foreach (var ch in text)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }

            value = value * 10 + (ch - '0');
        }

        return true;
    }

    public static DateTimeOffset? ReadTimeOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.ReadTime(ordinal);

    public static string? ReadTextOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? ReadIntOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static SqliteCommand With(this SqliteCommand command, string name, object value)
    {
        command.Parameters.AddWithValue(name, value);
        return command;
    }
}
