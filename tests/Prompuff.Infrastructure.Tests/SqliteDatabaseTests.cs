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
        foreach (var table in new[] { "Prompts", "Collections", "Tags", "PromptTags", "PromptVersions", "PromptSearch", "RenderValues" })
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
        database.ClearPool();
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

        database.ClearPool();
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public async Task A_v0_8_library_upgrades_to_index_lookups_by_row_and_stored_tag_names()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "prompuff.db");
        var planner = Guid.NewGuid();
        var notes = Guid.NewGuid();

        // A library as v0.8 left it: schema 6, its search triggers filling the index as prompts and tags arrive.
        await using (var v08 = new SqliteConnection($"Data Source={path};Pooling=False;Foreign Keys=True"))
        {
            await v08.OpenAsync();
            await using var command = v08.CreateCommand();
            command.CommandText = string.Concat(Migrations.All.Where(migration => migration.Version <= 6).Select(migration => migration.Sql)) + $"""
                PRAGMA user_version = 6;
                INSERT INTO Prompts (Id, Title, Body, CreatedAt, UpdatedAt) VALUES
                    ('{planner}', 'Angular planner', 'Upgrade the workspace.', '2026-10-08T09:00:00.0000000Z', '2026-10-08T09:00:00.0000000Z'),
                    ('{notes}', 'Meeting notes', 'Summarize the call.', '2026-10-08T09:00:00.0000000Z', '2026-10-08T09:00:00.0000000Z');
                INSERT INTO Tags (Id, Name) VALUES ('t1', 'zeta'), ('t2', 'alpha'), ('t3', 'writing');
                INSERT INTO PromptTags (PromptId, TagId, Position) VALUES ('{planner}', 't1', 0), ('{planner}', 't2', 1), ('{notes}', 't3', 0);

                -- An index that drifted: a row for a prompt that's gone, and a prompt with no row.
                INSERT INTO PromptSearch (PromptId, Title, Body) VALUES ('{Guid.NewGuid()}', 'Ghost', 'Phantom words.');
                DELETE FROM PromptSearch WHERE PromptId = '{notes}';
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SqliteDatabase(path, Path.Combine(folder, "backups"), NullLogger<SqliteDatabase>.Instance);
        await database.InitializeAsync();
        var search = new Repositories.SqlitePromptSearch(database);
        var prompts = new Repositories.SqlitePromptRepository(database);

        // Tags keep their order, and the index maps each prompt to its own row, mended where it had drifted.
        var all = await search.SearchAsync(Application.DTOs.PromptQuery.All);
        Assert.Equal(["zeta", "alpha"], all.Single(summary => summary.Id == planner).Tags);
        Assert.Equal([planner], (await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "#alpha workspace" })).Select(summary => summary.Id));
        Assert.Equal([notes], (await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "summarize writing" })).Select(summary => summary.Id));
        Assert.Empty(await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "phantom" }));
        await using (var connection = await database.OpenAsync())
        {
            Assert.Equal(0L, await Scalar(connection, """
                SELECT COUNT(*) FROM PromptSearchRows r LEFT JOIN PromptSearch s ON s.rowid = r.SearchRowId
                WHERE s.PromptId IS NOT r.PromptId;
                """));
            Assert.Equal(2L, await Scalar(connection, "SELECT COUNT(*) FROM PromptSearchRows;"));
            Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Prompts_Summary';"));
            Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_Prompts_DeletedAt';"));
        }

        // The triggers carry on from the upgraded rows: edits, tags, new prompts and removals.
        var prompt = (await prompts.GetAsync(planner))!;
        prompt.SetContent(new PromptContent(prompt.Title, null, "Migrate the monorepo.", null));
        prompt.SetTags(["alpha", "migration"]);
        await prompts.UpdateAsync(prompt, []);
        Assert.Empty(await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "workspace" }));
        Assert.Equal([planner], (await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "monorepo #migration" })).Select(summary => summary.Id));
        Assert.Equal(["alpha", "migration"], (await search.SearchAsync(Application.DTOs.PromptQuery.All)).Single(summary => summary.Id == planner).Tags);

        await prompts.DeleteAsync(notes);
        Assert.Empty(await search.SearchAsync(new Application.DTOs.PromptQuery { Text = "summarize" }));
        await using (var connection = await database.OpenAsync())
        {
            Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM PromptSearch;"));
            Assert.Equal(1L, await Scalar(connection, "SELECT COUNT(*) FROM PromptSearchRows;"));
        }

        database.ClearPool();
        Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData("2026-10-08T09:41:12.1234567Z")]
    [InlineData("2024-02-29T23:59:59.9999999Z")]
    [InlineData("0001-01-01T00:00:00.0000000Z")]
    public void Stored_times_read_back_exactly(string stored)
    {
        var time = SqlValues.ParseTime(stored);

        Assert.Equal(TimeSpan.Zero, time.Offset);
        Assert.Equal(stored, SqlValues.Time(time));
        Assert.Equal(DateTimeOffset.Parse(stored, System.Globalization.CultureInfo.InvariantCulture), time);
    }

    [Theory]
    [InlineData("2026-10-08T09:41:12Z", "2026-10-08T09:41:12.0000000Z")]
    [InlineData("2026-10-08 11:41:12+02:00", "2026-10-08T09:41:12.0000000Z")]
    [InlineData("2026-02-30T09:41:12.1234567Z", null)]
    public void Other_time_shapes_go_through_the_general_parser(string stored, string? expected)
    {
        if (expected is null)
        {
            Assert.Throws<FormatException>(() => SqlValues.ParseTime(stored));
            return;
        }

        Assert.Equal(expected, SqlValues.Time(SqlValues.ParseTime(stored)));
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
