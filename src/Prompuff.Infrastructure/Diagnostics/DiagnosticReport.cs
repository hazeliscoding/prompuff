using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.Infrastructure.Diagnostics;

/// <summary>What only the app knows. Infrastructure has no Avalonia, so the app fills these in.</summary>
/// <param name="AvaloniaVersion">The UI framework's version, such as "12.1.3".</param>
/// <param name="IsCommandLineToolInstalled">Whether "Install command-line tool" has put <c>prompuff</c> on the PATH.</param>
/// <param name="Hotkey">How the Quick save hotkey is doing, such as "ready" or "taken by another app".</param>
public sealed record DiagnosticFacts(string AvaloniaVersion, bool IsCommandLineToolInstalled, string Hotkey);

/// <summary>
/// The text behind Settings › About › Copy diagnostic info, for pasting into a bug report: versions, folders, library
/// counts, the settings that change behavior, and the end of the logs. It's built on this machine and goes nowhere
/// until the user pastes it. Prompt text never goes in: the log lines are checked against the library on the way.
/// </summary>
public sealed partial class DiagnosticReport(
    IAppDataPathProvider paths,
    ISettingsStore settings,
    IUpdateService updates,
    LibraryBackups backups,
    TimeProvider time)
{
    public const int AppLogLines = 100;
    public const int CliLogLines = 30;

    /// <summary>Only the end of a big log file is read.</summary>
    private const int TailBytes = 256 * 1024;

    private const int AppLog = 0;
    private const int CliLog = 1;

    public async Task<string> BuildAsync(DiagnosticFacts facts, CancellationToken cancellationToken = default)
    {
        var current = settings.Load();
        var scrubber = new LogScrubber(ReadLog(AppLogName(), AppLogLines), ReadLog(CliLogName(), CliLogLines));
        var report = new StringBuilder();

        Heading(report, "# Prompuff diagnostic info", first: true);
        Line(report, $"Gathered {Utc(time.GetUtcNow())}. Versions, settings, folder paths and recent log lines, and no prompt text. "
                     + "The paths can show your user name, so read it over before you post it.");

        Heading(report, "## App");
        Item(report, "Prompuff", $"{updates.CurrentVersion}, {(updates.IsSupported ? "installed" : "development build")}, {current.UpdateChannel} channel");
        Item(report, ".NET", RuntimeInformation.FrameworkDescription);
        Item(report, "Avalonia", facts.AvaloniaVersion);
        Item(report, "OS", $"{RuntimeInformation.OSDescription}, {Lower(RuntimeInformation.OSArchitecture)}");
        Item(report, "Process", Lower(RuntimeInformation.ProcessArchitecture));
        if (OperatingSystem.IsLinux())
        {
            // X11 or Wayland decides whether the hotkey and the tray can work.
            Item(report, "Session", string.Join(", ", new[] { "XDG_SESSION_TYPE", "XDG_CURRENT_DESKTOP" }
                .Select(name => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : "unknown")));
        }

        Heading(report, "## Folders");
        var overridden = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppDataPathProvider.OverrideVariable));
        Item(report, "Data", paths.GetAppDataDirectory() + (overridden ? $", from {AppDataPathProvider.OverrideVariable}" : string.Empty));
        Item(report, "Library", paths.GetDatabasePath());
        Item(report, "Settings", paths.GetSettingsPath());
        Item(report, "Logs", paths.GetLogsDirectory());
        Item(report, "Backups", paths.GetBackupDirectory());

        Heading(report, "## Library");
        var checkedAgainstLibrary = await AppendLibraryAsync(report, scrubber, cancellationToken);

        Heading(report, "## Settings");
        Item(report, "Theme", $"{current.Theme}, with {current.DarkTheme} for dark and {current.LightTheme} for light");
        Item(report, "Density", current.Density.ToString());
        Item(report, "Library view", $"{current.LibraryLayout}, by {current.LibrarySort}");
        Item(report, "Puff", current.ShowMascot ? "shown" : "hidden");
        Item(report, "Keep running in the tray", OnOff(current.KeepRunningInTray));
        Item(report, "Quick save hotkey", string.IsNullOrWhiteSpace(current.QuickSaveHotkey) ? "off" : $"{current.QuickSaveHotkey}, {facts.Hotkey}");
        Item(report, "MCP", OnOff(current.AllowMcp));
        Item(report, "Command-line tool", facts.IsCommandLineToolInstalled ? "installed" : "not installed");
        Item(report, "Update check", current.CheckForUpdatesAutomatically ? "each time Prompuff opens" : "by hand");

        AppendLog(report, "## Recent log", scrubber, AppLog, checkedAgainstLibrary, always: true);
        AppendLog(report, "## Recent command-line log", scrubber, CliLog, checkedAgainstLibrary, always: false);
        return report.ToString();
    }

    /// <summary>Counts what's in the library, and hands its text to the scrubber. Returns false when it couldn't be read.</summary>
    private async Task<bool> AppendLibraryAsync(StringBuilder report, LogScrubber scrubber, CancellationToken cancellationToken)
    {
        var read = await AppendLibraryFileAsync(report, scrubber, cancellationToken);
        try
        {
            var all = backups.List();
            Item(report, "Backups", all.Count == 0 ? "none yet" : $"{Number(all.Count)}, newest {Utc(all[0].CreatedAt)}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Item(report, "Backups", $"couldn't list them: {Describe(exception)}");
        }

        return read;
    }

    private async Task<bool> AppendLibraryFileAsync(StringBuilder report, LogScrubber scrubber, CancellationToken cancellationToken)
    {
        var path = paths.GetDatabasePath();
        if (!File.Exists(path))
        {
            Item(report, "Library file", "not created yet");
            return true;
        }

        try
        {
            Item(report, "Size on disk", Size(new FileInfo(path).Length + (File.Exists(path + "-wal") ? new FileInfo(path + "-wal").Length : 0)));

            // Read-only, so a broken library is described as it is rather than created, migrated or locked.
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            await using var connection = new SqliteConnection(builder.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var schema = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            Item(report, "Schema", schema == Migrations.LatestVersion
                ? $"{schema}, as this version expects"
                : $"{schema}, and this version expects {Migrations.LatestVersion}");

            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM Prompts WHERE DeletedAt IS NULL),
                       (SELECT COUNT(*) FROM Prompts WHERE DeletedAt IS NOT NULL),
                       (SELECT COUNT(*) FROM PromptVersions),
                       (SELECT COUNT(*) FROM Collections),
                       (SELECT COUNT(*) FROM Tags),
                       (SELECT COUNT(*) FROM Workflows);
                """;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                Item(report, "Prompts", $"{Number(reader.GetInt64(0))}, and {Number(reader.GetInt64(1))} in Recently deleted");
                Item(report, "Versions", Number(reader.GetInt64(2)));
                Item(report, "Collections", Number(reader.GetInt64(3)));
                Item(report, "Tags", Number(reader.GetInt64(4)));
                Item(report, "Workflows", Number(reader.GetInt64(5)));
            }

            // Everything a log line must never hold. Old titles too, since a prompt may have been renamed since.
            command.CommandText = """
                SELECT Title FROM Prompts
                UNION ALL SELECT Title FROM PromptVersions
                UNION ALL SELECT Description FROM Prompts
                UNION ALL SELECT Body FROM Prompts
                UNION ALL SELECT Notes FROM Prompts
                UNION ALL SELECT Value FROM RenderValues
                UNION ALL SELECT Name FROM Workflows
                UNION ALL SELECT Description FROM Workflows
                UNION ALL SELECT Note FROM WorkflowSteps
                UNION ALL SELECT Value FROM WorkflowValues;
                """;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        scrubber.LeaveOutLinesHolding(reader.GetString(0));
                    }
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            Item(report, "Problem", $"couldn't read the library: {Describe(exception)}");
            return false;
        }
    }

    private static void AppendLog(StringBuilder report, string heading, LogScrubber scrubber, int log, bool checkedAgainstLibrary, bool always)
    {
        var kept = scrubber.Kept(log);
        var leftOut = scrubber.LeftOut(log);
        if (!always && kept.Count == 0 && leftOut == 0)
        {
            return;
        }

        Heading(report, heading);
        if (kept.Count > 0)
        {
            // Longer than any run of backticks inside, so a message that quotes a ```prompt block can't end it.
            var fence = new string('`', Math.Max(3, kept.Max(LongestBacktickRun) + 1));
            Line(report, fence + "text");
            foreach (var line in kept)
            {
                Line(report, line);
            }

            Line(report, fence);
        }
        else if (leftOut == 0)
        {
            Line(report, "Nothing logged yet.");
        }

        if (leftOut > 0)
        {
            if (kept.Count > 0)
            {
                Line(report);
            }

            Line(report, leftOut == 1 ? "Left out 1 line that might hold prompt text." : $"Left out {Number(leftOut)} lines that might hold prompt text.");
        }

        if (!checkedAgainstLibrary && kept.Count > 0)
        {
            Line(report);
            Line(report, "The library couldn't be read, so these lines weren't checked against it.");
        }
    }

    /// <summary>The last <paramref name="count"/> lines across the newest files whose names match, oldest first.</summary>
    private List<string> ReadLog(Regex name, int count)
    {
        var lines = new List<string>();
        var directory = paths.GetLogsDirectory();
        if (!Directory.Exists(directory))
        {
            return lines;
        }

        try
        {
            var files = Directory.EnumerateFiles(directory, "prompuff-*.log")
                .Where(file => name.IsMatch(Path.GetFileName(file)))
                .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal);
            foreach (var file in files)
            {
                var tail = ReadTail(file);
                lines.InsertRange(0, tail.Skip(Math.Max(0, tail.Count - (count - lines.Count))));
                if (lines.Count >= count)
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A log that can't be read leaves the report shorter, not missing.
        }

        return lines;
    }

    private static List<string> ReadTail(string file)
    {
        // The app and the command-line tool may be writing to it right now.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var cut = stream.Length > TailBytes;
        if (cut)
        {
            stream.Seek(-TailBytes, SeekOrigin.End);
        }

        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        var lines = reader.ReadToEnd().Split('\n').Select(line => line.TrimEnd('\r')).ToList();
        if (cut)
        {
            lines.RemoveAt(0); // Starts partway through a line.
        }

        lines.RemoveAll(string.IsNullOrWhiteSpace);
        return lines;
    }

    private static int LongestBacktickRun(string line)
    {
        int longest = 0, run = 0;
        foreach (var ch in line)
        {
            run = ch == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    private static void Heading(StringBuilder report, string heading, bool first = false)
    {
        if (!first)
        {
            Line(report);
        }

        Line(report, heading);
        Line(report);
    }

    private static void Item(StringBuilder report, string label, string value) => Line(report, $"- {label}: {value}");

    private static void Line(StringBuilder report, string line = "") => report.Append(line).Append('\n');

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Lower(Architecture architecture) => architecture.ToString().ToLowerInvariant();

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset when) => when.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{Number(bytes)} B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    };

    private static string Describe(Exception exception) => $"{exception.GetType().Name}: {exception.Message}";

    [GeneratedRegex(@"^prompuff-\d{8}\.log$")]
    private static partial Regex AppLogName();

    [GeneratedRegex(@"^prompuff-cli-\d{8}\.log$")]
    private static partial Regex CliLogName();
}
