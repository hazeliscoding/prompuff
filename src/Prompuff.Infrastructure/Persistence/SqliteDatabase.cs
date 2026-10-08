using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Prompuff.Application;

namespace Prompuff.Infrastructure.Persistence;

/// <summary>Opens connections to the library database and brings its schema up to date.</summary>
public sealed class SqliteDatabase
{
    private readonly ILogger<SqliteDatabase> _logger;
    private readonly string _connectionString;

    public SqliteDatabase(string databasePath, string backupDirectory, ILogger<SqliteDatabase> logger)
    {
        DatabasePath = databasePath;
        BackupDirectory = backupDirectory;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true,
        }.ToString();
    }

    public string DatabasePath { get; }
    public string BackupDirectory { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Creates the database if needed and applies pending migrations, each in its own transaction.
    /// An existing database is copied to the backup folder before it is migrated.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!);
            await using var connection = await OpenAsync(cancellationToken);
            await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);

            var current = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken), CultureInfo.InvariantCulture);
            if (current > Migrations.LatestVersion)
            {
                throw new LibraryException(
                    "This library was saved by a newer version of Prompuff. Update Prompuff to open it.");
            }

            var pending = Migrations.All.Where(migration => migration.Version > current).ToList();
            if (pending.Count == 0)
            {
                return;
            }

            if (current > 0)
            {
                BackUp(connection, current);
            }

            foreach (var migration in pending)
            {
                // BEGIN IMMEDIATE takes the write lock, and the version is checked again under it: the app, the CLI and
                // the MCP server can open the library at the same moment, and only one of them may apply a migration.
                await using var transaction = connection.BeginTransaction(deferred: false);
                if (Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken, transaction), CultureInfo.InvariantCulture) >= migration.Version)
                {
                    continue;
                }

                await ExecuteAsync(connection, migration.Sql, cancellationToken, transaction);
                await ExecuteAsync(connection, $"PRAGMA user_version = {migration.Version};", cancellationToken, transaction);
                await transaction.CommitAsync(cancellationToken);
                _logger.LogInformation("Applied migration {Version} ({Name})", migration.Version, migration.Name);
            }
        }
        catch (LibraryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Database initialization failed");
            throw new LibraryException("Prompuff couldn't open your library.", exception);
        }
    }

    /// <summary>Copies the library to <paramref name="targetPath"/> with SQLite's online backup, so it's consistent while in use.</summary>
    public async Task BackUpToAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        CopyTo(connection, targetPath);
    }

    private void BackUp(SqliteConnection connection, int schemaVersion)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        CopyTo(connection, Path.Combine(BackupDirectory, $"prompuff-schema{schemaVersion}-{stamp}.db"));
        _logger.LogInformation("Backed up the library before migrating from schema {Version}", schemaVersion);
    }

    private static void CopyTo(SqliteConnection connection, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath))!);
        using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Pooling = false }.ToString());
        backup.Open();
        connection.BackupDatabase(backup);

        // The copy inherits WAL mode, which leaves -wal and -shm files beside it whenever it's opened. A rollback
        // journal keeps each backup a single file.
        using var command = backup.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = DELETE;";
        command.ExecuteNonQuery();
    }

    internal static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
