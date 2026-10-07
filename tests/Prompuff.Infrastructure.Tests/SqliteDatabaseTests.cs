using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Tests;

public class SqliteDatabaseTests
{
    [Fact]
    public async Task A_new_database_gets_every_table_and_the_latest_schema_version()
    {
        await using var library = await TestLibrary.CreateAsync();

        await using var connection = await library.Database.OpenAsync();
        Assert.Equal((long)Migrations.LatestVersion, await Scalar(connection, "PRAGMA user_version;"));
        foreach (var table in new[] { "Prompts", "Collections", "Tags", "PromptTags", "PromptVersions" })
        {
            Assert.Equal(1L, await Scalar(connection, $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';"));
        }
    }

    [Fact]
    public async Task Initializing_twice_keeps_the_data()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(new PromptContent("Keep me", null, "body", null));

        await library.Database.InitializeAsync();

        Assert.NotNull(await library.Prompts.GetAsync(prompt.Id));
    }

    [Fact]
    public async Task A_database_from_a_newer_version_is_refused_and_left_alone()
    {
        await using var library = await TestLibrary.CreateAsync();
        await using (var connection = await library.Database.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {Migrations.LatestVersion + 1};";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<LibraryException>(() => library.Database.InitializeAsync());

        Assert.Contains("newer version", error.Message);
    }

    [Fact]
    public async Task An_unreadable_file_fails_with_a_friendly_message()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "prompuff.db");
        await File.WriteAllTextAsync(path, "this is not a database, just some text that is long enough to have a header");
        var database = new SqliteDatabase(path, Path.Combine(folder, "backups"), NullLogger<SqliteDatabase>.Instance);

        var error = await Assert.ThrowsAsync<LibraryException>(() => database.InitializeAsync());

        Assert.Equal("Prompuff couldn't open your library.", error.Message);
        SqliteConnection.ClearAllPools();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced()
    {
        await using var library = await TestLibrary.CreateAsync();
        await using var connection = await library.Database.OpenAsync();

        Assert.Equal(1L, await Scalar(connection, "PRAGMA foreign_keys;"));
    }

    private static async Task<object?> Scalar(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
