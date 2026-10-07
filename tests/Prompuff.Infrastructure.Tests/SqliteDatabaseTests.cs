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
    public async Task A_v0_1_library_upgrades_with_its_prompts_and_a_backup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "prompuff.db");
        var id = Guid.NewGuid();

        // A library exactly as v0.1 left it: schema 1, one prompt with a version.
        await using (var v01 = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            await v01.OpenAsync();
            await using var command = v01.CreateCommand();
            command.CommandText = Migrations.All.Single(migration => migration.Version == 1).Sql + $"""
                PRAGMA user_version = 1;
                INSERT INTO Prompts (Id, Title, Body, IsFavorite, CreatedAt, UpdatedAt)
                VALUES ('{id}', 'From v0.1', 'body', 1, '2026-10-07T09:00:00.0000000Z', '2026-10-07T09:00:00.0000000Z');
                INSERT INTO PromptVersions (Id, PromptId, VersionNumber, Title, Body, SavedAt)
                VALUES ('{Guid.NewGuid()}', '{id}', 1, 'From v0.1', 'body', '2026-10-07T09:00:00.0000000Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SqliteDatabase(path, Path.Combine(folder, "backups"), NullLogger<SqliteDatabase>.Instance);
        await database.InitializeAsync();

        await using (var connection = await database.OpenAsync())
        {
            Assert.Equal((long)Migrations.LatestVersion, await Scalar(connection, "PRAGMA user_version;"));
        }

        var prompt = await new Repositories.SqlitePromptRepository(database).GetAsync(id);
        Assert.NotNull(prompt);
        Assert.Equal("From v0.1", prompt.Title);
        Assert.True(prompt.IsFavorite);
        Assert.Null(prompt.DeletedAt);
        var found = await new Repositories.SqlitePromptSearch(database).SearchAsync(new Application.DTOs.PromptQuery { Text = "from" });
        Assert.Equal([id], found.Select(summary => summary.Id));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "backups"), "prompuff-schema1-*.db"));

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
