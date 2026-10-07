using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.Services;
using Prompuff.Infrastructure.ImportExport;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.Infrastructure.Tests;

/// <summary>A real SQLite library in a temporary folder, deleted after the test.</summary>
internal sealed class TestLibrary : IAsyncDisposable
{
    private TestLibrary(string folder)
    {
        Folder = folder;
        Database = new SqliteDatabase(Path.Combine(folder, "prompuff.db"), Path.Combine(folder, "backups"), NullLogger<SqliteDatabase>.Instance);
        Prompts = new SqlitePromptRepository(Database);
        Collections = new SqliteCollectionRepository(Database);
        Tags = new SqliteTagRepository(Database);
        Search = new SqlitePromptSearch(Database);
        PromptService = new PromptService(Prompts, Time, NullLogger<PromptService>.Instance);
        CollectionService = new CollectionService(Collections, Time, NullLogger<CollectionService>.Instance);
        Transfer = new MarkdownTransferService(PromptService, CollectionService, Collections, NullLogger<MarkdownTransferService>.Instance);
    }

    public string Folder { get; }
    public SqliteDatabase Database { get; }
    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public SqlitePromptRepository Prompts { get; }
    public SqliteCollectionRepository Collections { get; }
    public SqliteTagRepository Tags { get; }
    public SqlitePromptSearch Search { get; }
    public PromptService PromptService { get; }
    public CollectionService CollectionService { get; }
    public MarkdownTransferService Transfer { get; }

    public static async Task<TestLibrary> CreateAsync()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var library = new TestLibrary(folder);
        await library.Database.InitializeAsync();
        return library;
    }

    public string TempFile(string name) => Path.Combine(Folder, name);

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    // Day boundaries in tests shouldn't depend on the machine's time zone.
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public void Advance(TimeSpan by) => Now += by;
}
