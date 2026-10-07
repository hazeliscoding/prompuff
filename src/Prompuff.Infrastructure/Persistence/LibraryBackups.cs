using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Prompuff.Application;

namespace Prompuff.Infrastructure.Persistence;

public enum BackupKind
{
    Daily,
    BeforeUpdate,
    BeforeRestore,
}

public sealed record LibraryBackup(string FilePath, BackupKind Kind, DateTimeOffset CreatedAt, long SizeBytes);

/// <summary>
/// Copies of the library in the backups folder: one a day while Prompuff runs (the newest 30 are kept), one before an
/// update changes the schema, and one before a restore. File names carry the kind and the UTC time.
/// </summary>
public sealed partial class LibraryBackups(SqliteDatabase database, TimeProvider time, ILogger<LibraryBackups> logger) : IDisposable
{
    public const int DailyBackupsKept = 30;

    private const string StampFormat = "yyyyMMdd-HHmmss";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private int _scheduled;

    /// <summary>Every backup in the folder, newest first.</summary>
    public IReadOnlyList<LibraryBackup> List()
    {
        if (!Directory.Exists(database.BackupDirectory))
        {
            return [];
        }

        var backups = new List<LibraryBackup>();
        foreach (var path in Directory.EnumerateFiles(database.BackupDirectory, "prompuff-*.db"))
        {
            var match = BackupName().Match(Path.GetFileName(path));
            if (!match.Success
                || !DateTime.TryParseExact(match.Groups["stamp"].Value, StampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created))
            {
                continue;
            }

            var kind = match.Groups["kind"].Value switch
            {
                "daily" => BackupKind.Daily,
                "before-restore" => BackupKind.BeforeRestore,
                _ => BackupKind.BeforeUpdate,
            };
            backups.Add(new LibraryBackup(path, kind, new DateTimeOffset(created, TimeSpan.Zero), new FileInfo(path).Length));
        }

        return backups.OrderByDescending(backup => backup.CreatedAt).ThenByDescending(backup => backup.FilePath, StringComparer.Ordinal).ToList();
    }

    /// <summary>Makes today's daily backup if there isn't one yet, then drops the oldest beyond 30. Returns the new backup, if any.</summary>
    public async Task<LibraryBackup?> BackUpIfDueAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var daily = List().Where(backup => backup.Kind == BackupKind.Daily).ToList();
            var today = LocalDate(time.GetUtcNow());
            if (daily.Count > 0 && LocalDate(daily[0].CreatedAt) >= today)
            {
                return null;
            }

            var backup = await CreateAsync(BackupKind.Daily, cancellationToken);
            foreach (var old in daily.Skip(DailyBackupsKept - 1))
            {
                File.Delete(old.FilePath);
            }

            logger.LogInformation("Made the daily backup; {Count} daily backups kept", Math.Min(daily.Count + 1, DailyBackupsKept));
            return backup;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Replaces the library with <paramref name="backup"/>, after checking it can be read and copying the current library
    /// aside. A backup from an older version is migrated like any old library.
    /// </summary>
    public async Task RestoreAsync(LibraryBackup backup, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Check(backup.FilePath);
            await CreateAsync(BackupKind.BeforeRestore, cancellationToken);
            await using (var source = new SqliteConnection(ReadOnly(backup.FilePath)))
            await using (var library = await database.OpenAsync(cancellationToken))
            {
                await source.OpenAsync(cancellationToken);
                source.BackupDatabase(library);
            }

            await database.InitializeAsync(cancellationToken);
            logger.LogInformation("Restored the {Kind} backup from {CreatedAt:O}", backup.Kind, backup.CreatedAt);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Restoring a backup failed");
            throw new LibraryException("Prompuff couldn't restore that backup. Your library hasn't been changed.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Makes the daily backup now if it's due, then checks again every hour while Prompuff runs.</summary>
    public void StartDailySchedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            _ = RunScheduleAsync(_stop.Token);
        }
    }

    public void Dispose() => _stop.Cancel();

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
            do
            {
                try
                {
                    await BackUpIfDueAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "The daily backup failed");
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // Prompuff is closing.
        }
    }

    private async Task<LibraryBackup> CreateAsync(BackupKind kind, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var prefix = kind == BackupKind.Daily ? "daily" : "before-restore";
        var stem = $"prompuff-{prefix}-{now.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture)}";
        var path = Path.Combine(database.BackupDirectory, stem + ".db");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(database.BackupDirectory, $"{stem}-{n}.db");
        }

        await database.BackUpToAsync(path, cancellationToken);
        return new LibraryBackup(path, kind, now, new FileInfo(path).Length);
    }

    /// <summary>Refuses a file that isn't a readable Prompuff library this version can open.</summary>
    private static void Check(string path)
    {
        const string refused = "That backup can't be read, so it wasn't restored. Your library hasn't been changed.";
        try
        {
            using var connection = new SqliteConnection(ReadOnly(path));
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var check = command.ExecuteScalar() as string;
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Prompts';";
            var hasPrompts = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
            if (check != "ok" || !hasPrompts || version < 1)
            {
                throw new LibraryException(refused);
            }

            if (version > Migrations.LatestVersion)
            {
                throw new LibraryException("That backup was made by a newer version of Prompuff. Update Prompuff to restore it. Your library hasn't been changed.");
            }
        }
        catch (SqliteException exception)
        {
            throw new LibraryException(refused, exception);
        }
    }

    private static string ReadOnly(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();

    private DateTime LocalDate(DateTimeOffset when) => TimeZoneInfo.ConvertTime(when, time.LocalTimeZone).Date;

    [GeneratedRegex(@"^prompuff-(?<kind>daily|before-restore|schema\d+)-(?<stamp>\d{8}-\d{6})(-\d+)?\.db$")]
    private static partial Regex BackupName();
}
