using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.Infrastructure.Tests;

/// <summary>
/// Every later version opens a library from any earlier release. Each file in <c>Fixtures/</c> is a library as a
/// release left it (see <see cref="SchemaFixtures"/>). These tests copy one to a temporary folder, open it the way the
/// app does, and read everything back through the real repositories and services.
/// </summary>
public class LibraryFixtureTests
{
    public static TheoryData<int> ReleasedSchemas => new(SchemaFixtures.ReleasedVersions);

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Each_fixture_is_one_file_with_the_schema_its_release_shipped(int schemaVersion)
    {
        var path = SchemaFixtures.PathFor(schemaVersion);
        Assert.True(File.Exists(path), $"Fixtures/{SchemaFixtures.FileName(schemaVersion)} is missing.");
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var fixture = new SqliteConnection(ReadOnly(path));
            await fixture.OpenAsync();
            Assert.Equal((long)schemaVersion, await ScalarAsync(fixture, "PRAGMA user_version;"));
            Assert.Equal("delete", await ScalarAsync(fixture, "PRAGMA journal_mode;"));
            Assert.Equal("ok", await ScalarAsync(fixture, "PRAGMA integrity_check;"));

            // Shipped migrations never change, so today's migrations up to N still build exactly the fixture's schema.
            await using var fresh = await SchemaFixtures.CreateAsync(Path.Combine(folder, "fresh.db"), schemaVersion);
            Assert.Equal(await SchemaAsync(fixture), await SchemaAsync(fresh));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_newest_fixture_is_at_the_current_schema() =>
        Assert.True(
            SchemaFixtures.ReleasedVersions.Max() == Migrations.LatestVersion,
            $"Schema {Migrations.LatestVersion} has no fixture. Add it to SchemaFixtures.ReleasedVersions, then run "
            + $"Rewrite_fixtures with PROMPUFF_WRITE_FIXTURES={Migrations.LatestVersion} and commit the new file.");

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Opens_at_the_latest_schema_after_backing_up_the_old_file(int schemaVersion)
    {
        await using var library = await OpenAsync(schemaVersion);

        await using (var connection = await library.Database.OpenAsync())
        {
            Assert.Equal((long)Migrations.LatestVersion, await ScalarAsync(connection, "PRAGMA user_version;"));
        }

        using var backups = new LibraryBackups(library.Database, library.Time, NullLogger<LibraryBackups>.Instance);
        if (schemaVersion == Migrations.LatestVersion)
        {
            Assert.Empty(backups.List());
            return;
        }

        var backup = Assert.Single(backups.List());
        Assert.Equal(BackupKind.BeforeUpdate, backup.Kind);
        Assert.StartsWith($"prompuff-schema{schemaVersion}-", Path.GetFileName(backup.FilePath));
        await using var copy = new SqliteConnection(ReadOnly(backup.FilePath));
        await copy.OpenAsync();
        Assert.Equal((long)schemaVersion, await ScalarAsync(copy, "PRAGMA user_version;"));
        Assert.Equal((long)SchemaFixtures.Library(schemaVersion).Prompts.Count, await ScalarAsync(copy, "SELECT COUNT(*) FROM Prompts;"));
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Prompts_and_their_versions_read_back(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        await using var library = await OpenAsync(schemaVersion);

        foreach (var prompt in expected.Prompts)
        {
            var stored = await library.PromptService.GetAsync(prompt.Id);
            Assert.NotNull(stored);
            Assert.Equal(
                (prompt.Current.Title, prompt.Current.Description, prompt.Current.Body, prompt.Current.Notes),
                (stored.Title, stored.Description, stored.Body, stored.Notes));
            Assert.Equal((prompt.IsFavorite, prompt.Rating, prompt.CollectionId), (stored.IsFavorite, stored.Rating, stored.CollectionId));
            Assert.Equal(prompt.Tags, stored.Tags);
            Assert.Equal(
                (prompt.CreatedAt, prompt.UpdatedAt, prompt.LastOpenedAt, prompt.DeletedAt, prompt.ParentPromptId),
                (stored.CreatedAt, stored.UpdatedAt, stored.LastOpenedAt, stored.DeletedAt, stored.ParentPromptId));

            // Newest first, as History lists them.
            var versions = await library.PromptService.GetVersionsAsync(prompt.Id);
            Assert.Equal(Enumerable.Range(1, prompt.Versions.Count).Reverse(), versions.Select(version => version.VersionNumber));
            foreach (var version in versions)
            {
                var old = prompt.Versions[version.VersionNumber - 1];
                Assert.Equal(
                    (old.Title, old.Description, old.Body, old.Notes, old.Note, old.SavedAt),
                    (version.Title, version.Description, version.Body, version.Notes, version.Note, version.SavedAt));
            }
        }

        var live = expected.Prompts.Where(prompt => prompt.DeletedAt is null).ToList();
        Assert.Equal(
            new LibraryCounts(
                live.Count,
                live.Count(prompt => prompt.IsFavorite),
                live.Count(prompt => prompt.CollectionId is null),
                live.Sum(prompt => prompt.Versions.Count),
                expected.Prompts.Count - live.Count),
            await library.Prompts.GetCountsAsync());
        Assert.Equal(Ids(live), Ids(await SearchAsync(library, new PromptQuery())));
        Assert.Equal(Ids(live.Where(prompt => prompt.IsFavorite)), Ids(await SearchAsync(library, new PromptQuery { Filter = PromptFilterKind.Favorites })));
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Collections_and_tags_read_back(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        var live = expected.Prompts.Where(prompt => prompt.DeletedAt is null).ToList();
        await using var library = await OpenAsync(schemaVersion);

        Assert.Equal(
            expected.Collections
                .Select(collection => new CollectionSummary(collection.Id, collection.Name, live.Count(prompt => prompt.CollectionId == collection.Id)))
                .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase),
            await library.CollectionService.ListAsync());
        foreach (var collection in expected.Collections)
        {
            var filed = await SearchAsync(library, new PromptQuery { Filter = PromptFilterKind.Collection, CollectionId = collection.Id });
            Assert.Equal(Ids(live.Where(prompt => prompt.CollectionId == collection.Id)), Ids(filed));
        }

        // Tags that only prompts in Recently deleted use aren't listed.
        Assert.Equal(
            live.SelectMany(prompt => prompt.Tags).GroupBy(tag => tag).Select(group => new TagSummary(group.Key, group.Count())).OrderBy(tag => tag.Name, StringComparer.Ordinal),
            (await library.Tags.ListAsync()).OrderBy(tag => tag.Name, StringComparer.Ordinal));
        Assert.Equal(
            Ids(live.Where(prompt => prompt.Tags.Contains("bugs"))),
            Ids(await SearchAsync(library, new PromptQuery { Filter = PromptFilterKind.Tag, Tag = "bugs" })));
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Deleted_prompts_lineage_and_remembered_values_read_back(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        await using var library = await OpenAsync(schemaVersion);

        Assert.Equal(
            Ids(expected.Prompts.Where(prompt => prompt.DeletedAt is not null)),
            Ids(await SearchAsync(library, new PromptQuery { Filter = PromptFilterKind.Deleted })));
        Assert.Equal(schemaVersion >= SchemaFixtures.RecentlyDeleted, expected.Prompts.Any(prompt => prompt.DeletedAt is not null));

        var values = new SqliteRenderValues(library.Database);
        foreach (var prompt in expected.Prompts)
        {
            Assert.Equal(Sorted(prompt.RenderValues), Sorted(await values.LoadAsync(prompt.Id)));

            var lineage = await library.PromptService.GetLineageAsync(prompt.Id);
            Assert.Equal(prompt.ParentPromptId, lineage.Parent?.Id);
            Assert.Equal(expected.Prompts.Where(copy => copy.ParentPromptId == prompt.Id).Select(copy => copy.Id), lineage.Copies.Select(copy => copy.Id));
        }

        Assert.Equal(schemaVersion >= SchemaFixtures.RememberedValues, expected.Prompts.Any(prompt => prompt.RenderValues.Count > 0));
        Assert.Equal(schemaVersion >= SchemaFixtures.Lineage, expected.Prompts.Any(prompt => prompt.ParentPromptId is not null));
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Workflows_their_steps_and_values_read_back(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        await using var library = await OpenAsync(schemaVersion);

        var summaries = await library.WorkflowService.ListAsync();
        Assert.Equal(expected.Workflows.OrderByDescending(workflow => workflow.UpdatedAt).Select(workflow => workflow.Id), summaries.Select(summary => summary.Id));
        Assert.Equal(schemaVersion >= SchemaFixtures.Workflows, expected.Workflows.Count > 0);

        foreach (var workflow in expected.Workflows)
        {
            var titles = workflow.Steps.Select(step => expected.Prompts.Single(prompt => prompt.Id == step.PromptId).Current.Title);
            Assert.Equal(titles, summaries.Single(summary => summary.Id == workflow.Id).StepTitles);

            var stored = await library.WorkflowService.GetAsync(workflow.Id);
            Assert.NotNull(stored);
            Assert.Equal(
                (workflow.Name, workflow.Description, workflow.CreatedAt, workflow.UpdatedAt),
                (stored.Name, stored.Description, stored.CreatedAt, stored.UpdatedAt));
            Assert.Equal(workflow.Steps, stored.Steps.Select(step => new FixtureStep(step.Id, step.PromptId, step.Note)));
            Assert.Equal(Sorted(workflow.Values), Sorted(await library.WorkflowService.LoadValuesAsync(workflow.Id)));
        }
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Search_finds_unicode_titles_tags_and_words_without_accents(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        var summary = expected.Prompts.Single(prompt => prompt.Current.Title == "日本語の要約");
        var cafe = expected.Prompts.Single(prompt => prompt.Current.Title == "Café menu translator ☕");
        var bugs = expected.Prompts.Where(prompt => prompt.Current.Title.Contains("🐛", StringComparison.Ordinal)).ToList();
        await using var library = await OpenAsync(schemaVersion);

        // The title match ranks above the café prompt, whose notes mention 日本語.
        var ranked = await SearchAsync(library, new PromptQuery { Text = "日本語" });
        Assert.Equal([summary.Id, cafe.Id], ranked.Select(result => result.Id));
        Assert.Equal([summary.Id], Ids(await SearchAsync(library, new PromptQuery { Text = "#日本語" })));
        Assert.Equal([cafe.Id], Ids(await SearchAsync(library, new PromptQuery { Text = "cafe" })));

        // An emoji has no letters to index, so it's matched as written.
        Assert.Equal(2, bugs.Count);
        Assert.Equal(Ids(bugs), Ids(await SearchAsync(library, new PromptQuery { Text = "🐛" })));
    }

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public async Task Keeps_working_after_the_upgrade(int schemaVersion)
    {
        var expected = SchemaFixtures.Library(schemaVersion);
        var cafe = expected.Prompts.Single(prompt => prompt.Current.Title == "Café menu translator ☕");
        var planner = expected.Prompts.Single(prompt => prompt.Current.Title == "Angular Upgrade Planner");
        await using var library = await OpenAsync(schemaVersion);
        library.Time.Advance(TimeSpan.FromDays(20));

        var edited = new PromptContent(cafe.Current.Title, cafe.Current.Description, cafe.Current.Body + "\n\nEnd with the desserts.", cafe.Current.Notes);
        Assert.Equal(new SaveResult(true, cafe.Versions.Count + 1), await library.PromptService.SaveContentAsync(cafe.Id, edited));
        Assert.Equal([cafe.Id], Ids(await SearchAsync(library, new PromptQuery { Text = "desserts" })));

        var copy = await library.PromptService.DuplicateAsync(planner.Id);
        Assert.Equal(planner.Id, (await library.PromptService.GetLineageAsync(copy.Id)).Parent?.Id);

        var workflow = await library.WorkflowService.CreateAsync("Compare plans", steps: [(planner.Id, "The first plan."), (copy.Id, null)]);
        Assert.Equal([planner.Id, copy.Id], (await library.WorkflowService.GetAsync(workflow.Id))!.Steps.Select(step => step.PromptId));
    }

    [Fact]
    public async Task A_library_from_a_newer_version_is_refused_and_left_as_it_was()
    {
        var newest = SchemaFixtures.ReleasedVersions.Max();
        var newer = Migrations.LatestVersion + 1;
        await using var library = TestLibrary.FromCopy(SchemaFixtures.PathFor(newest));

        // What a later Prompuff might leave: a table this version doesn't know, and a higher schema number.
        await using (var later = new SqliteConnection(ReadWrite(library.Database.DatabasePath)))
        {
            await later.OpenAsync();
            await using var command = later.CreateCommand();
            command.CommandText = $"CREATE TABLE PromptRelations (FromId TEXT NOT NULL, ToId TEXT NOT NULL); PRAGMA user_version = {newer};";
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<LibraryException>(() => library.Database.InitializeAsync());

        Assert.Equal("This library was saved by a newer version of Prompuff. Update Prompuff to open it.", error.Message);
        Assert.False(Directory.Exists(library.Database.BackupDirectory) && Directory.EnumerateFileSystemEntries(library.Database.BackupDirectory).Any());
        await using var after = new SqliteConnection(ReadOnly(library.Database.DatabasePath));
        await after.OpenAsync();
        Assert.Equal((long)newer, await ScalarAsync(after, "PRAGMA user_version;"));
        Assert.Equal(1L, await ScalarAsync(after, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'PromptRelations';"));
        Assert.Equal((long)SchemaFixtures.Library(newest).Prompts.Count, await ScalarAsync(after, "SELECT COUNT(*) FROM Prompts;"));
    }

    /// <summary>
    /// Writes fixtures into the source tree. It's skipped unless <c>PROMPUFF_WRITE_FIXTURES</c> names the schema versions
    /// to write, such as <c>7</c> or <c>1,4,5,6</c>. A released fixture records what that release left behind, so rewrite
    /// one only when the data it holds has to change, never because a migration did.
    /// </summary>
    [Fact]
    public async Task Rewrite_fixtures()
    {
        var requested = Environment.GetEnvironmentVariable("PROMPUFF_WRITE_FIXTURES");
        if (string.IsNullOrWhiteSpace(requested))
        {
            Assert.Skip("Set PROMPUFF_WRITE_FIXTURES to the schema versions to write, such as 1,4,5,6.");
        }

        foreach (var version in requested.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var schemaVersion = int.Parse(version, CultureInfo.InvariantCulture);
            Assert.Contains(schemaVersion, SchemaFixtures.ReleasedVersions);
            await SchemaFixtures.WriteAsync(schemaVersion, Path.Combine(SchemaFixtures.SourceFolder, SchemaFixtures.FileName(schemaVersion)));
        }
    }

    private static async Task<TestLibrary> OpenAsync(int schemaVersion)
    {
        var library = TestLibrary.FromCopy(SchemaFixtures.PathFor(schemaVersion));
        try
        {
            await library.Database.InitializeAsync();
            return library;
        }
        catch
        {
            await library.DisposeAsync();
            throw;
        }
    }

    private static Task<IReadOnlyList<PromptSummary>> SearchAsync(TestLibrary library, PromptQuery query) => library.Search.SearchAsync(query);

    private static List<Guid> Ids(IEnumerable<FixturePrompt> prompts) => prompts.Select(prompt => prompt.Id).Order().ToList();

    private static List<Guid> Ids(IEnumerable<PromptSummary> summaries) => summaries.Select(summary => summary.Id).Order().ToList();

    private static List<KeyValuePair<string, string>> Sorted(IEnumerable<KeyValuePair<string, string>> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

    /// <summary>Every table, index, trigger and FTS5 shadow table, with the SQL that made it.</summary>
    private static async Task<List<string>> SchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name;";
        var schema = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            schema.Add($"{reader.GetString(0)} {reader.GetString(1)} on {reader.GetString(2)}: {(reader.IsDBNull(3) ? "(automatic)" : reader.GetString(3))}");
        }

        return schema;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static string ReadOnly(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();

    private static string ReadWrite(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
}
