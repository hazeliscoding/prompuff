using Microsoft.Data.Sqlite;

[assembly: AssemblyFixture(typeof(Prompuff.Performance.Tests.SeededLibrary))]

// Timings mean little while other tests in this assembly compete for the same cores.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Prompuff.Performance.Tests;

/// <summary>One library of 10,000 generated prompts, seeded once for the whole run. Tests that change it work on a copy.</summary>
public sealed class SeededLibrary : IAsyncLifetime
{
    public const int PromptCount = 10_000;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "prompuff-perf-tests", Guid.NewGuid().ToString("N"));

    public string DatabasePath => Path.Combine(_folder, "seed", "prompuff.db");

    internal SeededLibraryInfo Info { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Info = await LibrarySeeder.SeedAsync(DatabasePath, PromptCount);
        SqliteConnection.ClearAllPools();
    }

    /// <summary>A new data folder holding a copy of the seeded library.</summary>
    public string CopyToNewFolder()
    {
        var folder = Path.Combine(_folder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.Copy(DatabasePath, Path.Combine(folder, "prompuff.db"));
        return folder;
    }

    public ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is harmless.
        }

        return ValueTask.CompletedTask;
    }
}
