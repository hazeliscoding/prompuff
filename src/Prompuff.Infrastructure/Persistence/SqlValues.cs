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

    public static DateTimeOffset ReadTime(this SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

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
