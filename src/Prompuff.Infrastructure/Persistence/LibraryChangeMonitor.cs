using Microsoft.Data.Sqlite;

namespace Prompuff.Infrastructure.Persistence;

/// <summary>
/// Tells when the library has changed under a connection, such as when the <c>prompuff</c> command saves a prompt.
/// SQLite's <c>data_version</c> moves whenever another connection commits, so this keeps one connection of its own
/// open and compares. Writes by this process count too; <see cref="ResetAsync"/> takes them as seen.
/// </summary>
public sealed class LibraryChangeMonitor(SqliteDatabase database) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SqliteConnection? _connection;
    private long? _version;

    /// <summary>True when the library changed since the last check or reset. The first check only takes a baseline.</summary>
    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var version = await ReadAsync(cancellationToken);
            var changed = _version is { } previous && previous != version;
            _version = version;
            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Takes the library as it is now as the baseline.</summary>
    public Task ResetAsync(CancellationToken cancellationToken = default) => CheckAsync(cancellationToken);

    private async Task<long> ReadAsync(CancellationToken cancellationToken)
    {
        _connection ??= await database.OpenAsync(cancellationToken);
        await using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA data_version";
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
