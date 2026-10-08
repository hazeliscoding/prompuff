using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Infrastructure.Tests;

internal sealed record FixtureCollection(Guid Id, string Name, DateTimeOffset CreatedAt);

/// <param name="Note">The note the app stored with the version, such as "First version".</param>
internal sealed record FixtureVersion(string Title, string? Description, string Body, string? Notes, string Note, DateTimeOffset SavedAt);

internal sealed record FixturePrompt
{
    public required Guid Id { get; init; }
    public bool IsFavorite { get; init; }
    public int? Rating { get; init; }
    public Guid? CollectionId { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? LastOpenedAt { get; init; }

    /// <summary>Oldest first. The last one is the prompt's current content.</summary>
    public required IReadOnlyList<FixtureVersion> Versions { get; init; }

    public DateTimeOffset? DeletedAt { get; init; }
    public Guid? ParentPromptId { get; init; }
    public IReadOnlyDictionary<string, string> RenderValues { get; init; } = new Dictionary<string, string>();

    public FixtureVersion Current => Versions[^1];
}

internal sealed record FixtureStep(Guid Id, Guid PromptId, string? Note);

internal sealed record FixtureWorkflow(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<FixtureStep> Steps,
    IReadOnlyDictionary<string, string> Values);

internal sealed record FixtureLibrary(
    int SchemaVersion,
    IReadOnlyList<FixtureCollection> Collections,
    IReadOnlyList<FixturePrompt> Prompts,
    IReadOnlyList<FixtureWorkflow> Workflows);

/// <summary>
/// Libraries as each released version of Prompuff left them, committed as <c>Fixtures/schema-N.db</c>. Each one is
/// built by running the migrations up to N and no further, then filling the file with raw SQL in that schema, with
/// what that version could store. The normal test run only reads them; <c>LibraryFixtureTests.Rewrite_fixtures</c>
/// writes them when <c>PROMPUFF_WRITE_FIXTURES</c> is set.
/// </summary>
internal static class SchemaFixtures
{
    // The migrations that brought in each part of the library.
    public const int RecentlyDeleted = 2;
    public const int RememberedValues = 4;
    public const int Lineage = 5;
    public const int Workflows = 6;

    /// <summary>
    /// The schema each release left behind, from the <c>Migrations.cs</c> its tag shipped: 1 in v0.1.0–v0.1.2, 4 in
    /// v0.2.0–v0.3.0, 5 in v0.4.0–v0.5.0, 6 in v0.6.0–v0.8.0, and 7 from v0.9.0 on. Schemas 2 and 3 never shipped on their own.
    /// </summary>
    public static IReadOnlyList<int> ReleasedVersions { get; } = [1, 4, 5, 6, 7];

    public static string FileName(int schemaVersion) => $"schema-{schemaVersion}.db";

    /// <summary>A committed fixture, as the build copies it beside the tests.</summary>
    public static string PathFor(int schemaVersion) => Path.Combine(AppContext.BaseDirectory, "Fixtures", FileName(schemaVersion));

    /// <summary>The Fixtures folder in the source tree, where rewritten fixtures go.</summary>
    public static string SourceFolder => Path.Combine(ThisFolder(), "Fixtures");

    /// <summary>What the fixture for <paramref name="schemaVersion"/> holds: each part only once its schema has it.</summary>
    public static FixtureLibrary Library(int schemaVersion)
    {
        var engineering = new FixtureCollection(Id("collection:engineering"), "Engineering", At("2026-10-07T08:58:41.5531204Z"));
        var writing = new FixtureCollection(Id("collection:writing"), "Writing & docs", At("2026-10-07T09:03:12.0081733Z"));
        var design = new FixtureCollection(Id("collection:design"), "Design ✨", At("2026-10-07T09:04:55.9120017Z"));

        const string plannerV1 =
            "You are a senior Angular engineer planning an upgrade of {{repo_name}}.\n\n" +
            "List the dependencies that will block the upgrade, then give a phased plan.";
        const string plannerV2 =
            "You are a senior Angular engineer planning an upgrade of {{repo_name}} to Angular {{target_version}}.\n\n" +
            "List the dependencies that will block the upgrade, then give a phased plan:\n\n" +
            "1. Pre-flight: tooling, Node and TypeScript versions\n" +
            "2. Framework bump, one major at a time\n" +
            "3. Verification: tests, build, bundle size";
        const string plannerDescription = "Produces a phased migration plan from the current Angular version to a target.";
        var planner = new FixturePrompt
        {
            Id = Id("prompt:planner"),
            IsFavorite = true,
            Rating = 5,
            CollectionId = engineering.Id,
            Tags = ["angular", "migration", "copilot"],
            CreatedAt = At("2026-10-07T09:05:17.4410382Z"),
            UpdatedAt = At("2026-10-07T13:47:58.6620913Z"),
            LastOpenedAt = At("2026-10-07T16:40:02.7310449Z"),
            Versions =
            [
                new("Angular Upgrade Planner", "Plans an Angular upgrade.", plannerV1, null, "First version", At("2026-10-07T09:05:17.4410382Z")),
                new("Angular Upgrade Planner", plannerDescription, plannerV2, null, "Edited description and body (+6 −2 lines)", At("2026-10-07T09:21:03.0937165Z")),
                new("Angular Upgrade Planner", plannerDescription, plannerV2, "Phasing by major version stopped the model from skipping steps.", "Edited notes", At("2026-10-07T13:47:58.6620913Z")),
            ],
            RenderValues = new Dictionary<string, string> { ["repo_name"] = "acme/web", ["target_version"] = "20" },
        };

        const string cafeV1 = "Translate the menu of {{cafe_name}} into {{language}}.";
        const string cafeV2 = cafeV1 + "\n\n" +
            "Keep prices exactly as written, such as “Café crème — 3,50 €”, and keep a dish's name when it has no common translation.";
        var cafe = new FixturePrompt
        {
            Id = Id("prompt:cafe"),
            Rating = 4,
            CollectionId = writing.Id,
            Tags = ["i18n", "café"],
            CreatedAt = At("2026-10-07T09:30:44.1180236Z"),
            UpdatedAt = At("2026-10-07T09:36:20.5007781Z"),
            Versions =
            [
                new("Menu translator", null, cafeV1, null, "First version", At("2026-10-07T09:30:44.1180236Z")),
                new("Café menu translator ☕", "Keeps prices, dish names and the café's tone.", cafeV2,
                    "Naming a sample price stopped it from converting currencies. Works in 日本語 too.",
                    "Edited title, description, body (+2 −0 lines) and notes", At("2026-10-07T09:36:20.5007781Z")),
            ],
            RenderValues = new Dictionary<string, string> { ["cafe_name"] = "Café Sól", ["language"] = "日本語" },
        };

        // Imported from Markdown, so it was created before it arrived. Restoring v1 made v3.
        const string summaryV1 = "次の文章を{{length}}で要約してください。\n\n{{text}}";
        const string summaryV2 = summaryV1 + "\n\n箇条書きで答えてください。";
        var summary = new FixturePrompt
        {
            Id = Id("prompt:summary"),
            IsFavorite = true,
            Tags = ["日本語", "summary"],
            CreatedAt = At("2026-10-02T11:00:00.0000000Z"),
            UpdatedAt = At("2026-10-07T10:19:02.0145598Z"),
            LastOpenedAt = At("2026-10-07T10:19:30.3302214Z"),
            Versions =
            [
                new("日本語の要約", "Summarizes Japanese text in plain Japanese.", summaryV1, "敬語を避けると短くなる。", "Imported from Markdown", At("2026-10-07T10:15:09.2734410Z")),
                new("日本語の要約", "Summarizes Japanese text in plain Japanese.", summaryV2, "敬語を避けると短くなる。", "Edited body (+2 −0 lines)", At("2026-10-07T10:18:31.8862054Z")),
                new("日本語の要約", "Summarizes Japanese text in plain Japanese.", summaryV1, "敬語を避けると短くなる。", "Restored v1", At("2026-10-07T10:19:02.0145598Z")),
            ],
        };

        const string bugBody =
            "Here is a bug report for {{project}}:\n\n{{issue}}\n\n" +
            "Ask me for anything missing, then write the smallest steps that reproduce it.";
        var bug = new FixturePrompt
        {
            Id = Id("prompt:bug"),
            Rating = 2,
            CollectionId = engineering.Id,
            Tags = ["review", "bugs"],
            CreatedAt = At("2026-10-07T11:02:36.7781903Z"),
            UpdatedAt = At("2026-10-07T11:02:36.7781903Z"),
            Versions = [new("Bug reproduction request 🐛", null, bugBody, null, "First version", At("2026-10-07T11:02:36.7781903Z"))],
        };

        // Duplicate kept the metadata but not the favorite. Before lineage (schema 5), nothing linked a copy to its source.
        var bugCopy = new FixturePrompt
        {
            Id = Id("prompt:bug-copy"),
            Rating = 2,
            CollectionId = engineering.Id,
            Tags = ["review", "bugs"],
            CreatedAt = At("2026-10-07T11:04:10.2290071Z"),
            UpdatedAt = At("2026-10-07T11:04:10.2290071Z"),
            Versions = [new("Bug reproduction request 🐛 (copy)", null, bugBody, null, "Duplicated from “Bug reproduction request 🐛”", At("2026-10-07T11:04:10.2290071Z"))],
            ParentPromptId = schemaVersion >= Lineage ? bug.Id : null,
        };

        // Its tags belong to no prompt outside Recently deleted, so the sidebar doesn't list them.
        var standup = new FixturePrompt
        {
            Id = Id("prompt:standup"),
            Rating = 3,
            CollectionId = writing.Id,
            Tags = ["standup", "writing"],
            CreatedAt = At("2026-10-07T12:00:05.0000318Z"),
            UpdatedAt = At("2026-10-07T12:05:41.9901276Z"),
            Versions =
            [
                new("Standup summary", "Turns yesterday's notes into three lines.", "Summarize {{notes}} for standup.", null, "First version", At("2026-10-07T12:00:05.0000318Z")),
                new("Standup summary", "Turns yesterday's notes into three lines.", "Summarize {{notes}} for standup in three lines: done, next, blocked.", null, "Edited body (+1 −1 lines)", At("2026-10-07T12:05:41.9901276Z")),
            ],
            DeletedAt = At("2026-10-07T17:30:12.4471020Z"),
        };

        List<FixturePrompt> prompts = [planner, cafe, summary, bug, bugCopy];
        if (schemaVersion >= RecentlyDeleted)
        {
            prompts.Add(standup);
        }

        if (schemaVersion < RememberedValues)
        {
            prompts = prompts.Select(prompt => prompt with { RenderValues = new Dictionary<string, string>() }).ToList();
        }

        List<FixtureWorkflow> workflows = [];
        if (schemaVersion >= Workflows)
        {
            workflows.Add(new FixtureWorkflow(
                Id("workflow:upgrade"),
                "Angular upgrade, start to finish",
                "Plan the upgrade, reproduce what breaks, then plan the next major. 🚀",
                At("2026-10-08T08:10:00.5120044Z"),
                At("2026-10-08T08:25:47.1062290Z"),
                [
                    new(Id("workflow:upgrade:1"), planner.Id, "The phased plan and its risk table.\nPaste the first failure into {{issue}}."),
                    new(Id("workflow:upgrade:2"), bug.Id, null),
                    new(Id("workflow:upgrade:3"), planner.Id, "Run it again for the next major."),
                ],
                new Dictionary<string, string>
                {
                    ["repo_name"] = "acme/web",
                    ["target_version"] = "21",
                    ["project"] = "acme/web",
                    ["issue"] = "ng build fails after the bump:\nNG0203: inject() must be called from an injection context…",
                }));

            // Made and never filled in.
            workflows.Add(new FixtureWorkflow(
                Id("workflow:untitled"), "Untitled workflow", null,
                At("2026-10-08T09:00:13.3301876Z"), At("2026-10-08T09:00:13.3301876Z"), [], new Dictionary<string, string>()));
        }

        return new FixtureLibrary(schemaVersion, [engineering, writing, design], prompts, workflows);
    }

    /// <summary>Creates a library at <paramref name="schemaVersion"/> by running the migrations up to it and no further.</summary>
    public static async Task<SqliteConnection> CreateAsync(string path, int schemaVersion)
    {
        if (Migrations.All.All(migration => migration.Version != schemaVersion))
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion, "There's no migration with that version.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.Delete(path);

        // A new file uses a rollback journal, so the fixture stays one file with no -wal beside it.
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString());
        await connection.OpenAsync();
        foreach (var migration in Migrations.All.Where(migration => migration.Version <= schemaVersion))
        {
            await using var transaction = connection.BeginTransaction();
            await ExecuteAsync(connection, transaction, migration.Sql + $"\nPRAGMA user_version = {migration.Version};");
            await transaction.CommitAsync();
        }

        return connection;
    }

    /// <summary>Writes the fixture for <paramref name="schemaVersion"/> to <paramref name="path"/>, replacing any file there.</summary>
    public static async Task WriteAsync(int schemaVersion, string path)
    {
        var library = Library(schemaVersion);
        await using var connection = await CreateAsync(path, schemaVersion);
        await using (var transaction = connection.BeginTransaction())
        {
            foreach (var collection in library.Collections)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO Collections (Id, Name, CreatedAt) VALUES ($id, $name, $created);",
                    ("$id", Text(collection.Id)), ("$name", collection.Name), ("$created", Text(collection.CreatedAt)));
            }

            // Parents go in first: lineage is a foreign key.
            foreach (var prompt in library.Prompts.OrderBy(prompt => prompt.ParentPromptId is null ? 0 : 1))
            {
                await InsertPromptAsync(connection, transaction, prompt, schemaVersion);
            }

            await InsertTagsAsync(connection, transaction, library.Prompts);
            foreach (var prompt in library.Prompts)
            {
                for (var i = 0; i < prompt.Versions.Count; i++)
                {
                    var version = prompt.Versions[i];
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO PromptVersions (Id, PromptId, VersionNumber, Title, Description, Body, Notes, Note, SavedAt)
                        VALUES ($id, $prompt, $number, $title, $description, $body, $notes, $note, $saved);
                        """,
                        ("$id", Text(Id($"{prompt.Id}:v{i + 1}"))), ("$prompt", Text(prompt.Id)), ("$number", i + 1),
                        ("$title", version.Title), ("$description", version.Description), ("$body", version.Body),
                        ("$notes", version.Notes), ("$note", version.Note), ("$saved", Text(version.SavedAt)));
                }

                foreach (var (name, value) in prompt.RenderValues)
                {
                    await ExecuteAsync(connection, transaction, "INSERT INTO RenderValues (PromptId, Name, Value) VALUES ($prompt, $name, $value);",
                        ("$prompt", Text(prompt.Id)), ("$name", name), ("$value", value));
                }
            }

            foreach (var workflow in library.Workflows)
            {
                await InsertWorkflowAsync(connection, transaction, workflow);
            }

            await transaction.CommitAsync();
        }

        await ExecuteAsync(connection, null, "VACUUM;");
    }

    private static async Task InsertPromptAsync(SqliteConnection connection, SqliteTransaction transaction, FixturePrompt prompt, int schemaVersion)
    {
        // The columns that schema has. Times are ISO 8601 UTC text with seven decimals and IDs are GUID text, as every
        // release so far wrote them.
        var columns = new List<(string Column, object? Value)>
        {
            ("Id", Text(prompt.Id)),
            ("Title", prompt.Current.Title),
            ("Description", prompt.Current.Description),
            ("Body", prompt.Current.Body),
            ("Notes", prompt.Current.Notes),
            ("IsFavorite", prompt.IsFavorite ? 1 : 0),
            ("Rating", prompt.Rating),
            ("CollectionId", prompt.CollectionId is { } collection ? Text(collection) : null),
            ("CreatedAt", Text(prompt.CreatedAt)),
            ("UpdatedAt", Text(prompt.UpdatedAt)),
            ("LastOpenedAt", prompt.LastOpenedAt is { } opened ? Text(opened) : null),
        };
        if (schemaVersion >= RecentlyDeleted)
        {
            columns.Add(("DeletedAt", prompt.DeletedAt is { } deleted ? Text(deleted) : null));
        }

        if (schemaVersion >= Lineage)
        {
            columns.Add(("ParentPromptId", prompt.ParentPromptId is { } parent ? Text(parent) : null));
        }

        var sql = $"INSERT INTO Prompts ({string.Join(", ", columns.Select(column => column.Column))}) " +
                  $"VALUES ({string.Join(", ", columns.Select(column => "$" + column.Column))});";
        await ExecuteAsync(connection, transaction, sql, columns.Select(column => ("$" + column.Column, column.Value)).ToArray());
    }

    private static async Task InsertTagsAsync(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<FixturePrompt> prompts)
    {
        foreach (var name in prompts.SelectMany(prompt => prompt.Tags).Distinct(StringComparer.Ordinal))
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO Tags (Id, Name) VALUES ($id, $name);", ("$id", Text(Id("tag:" + name))), ("$name", name));
        }

        foreach (var prompt in prompts)
        {
            for (var position = 0; position < prompt.Tags.Count; position++)
            {
                await ExecuteAsync(connection, transaction, "INSERT INTO PromptTags (PromptId, TagId, Position) VALUES ($prompt, $tag, $position);",
                    ("$prompt", Text(prompt.Id)), ("$tag", Text(Id("tag:" + prompt.Tags[position]))), ("$position", position));
            }
        }
    }

    private static async Task InsertWorkflowAsync(SqliteConnection connection, SqliteTransaction transaction, FixtureWorkflow workflow)
    {
        await ExecuteAsync(connection, transaction, """
            INSERT INTO Workflows (Id, Name, Description, CreatedAt, UpdatedAt) VALUES ($id, $name, $description, $created, $updated);
            """,
            ("$id", Text(workflow.Id)), ("$name", workflow.Name), ("$description", workflow.Description),
            ("$created", Text(workflow.CreatedAt)), ("$updated", Text(workflow.UpdatedAt)));

        for (var position = 0; position < workflow.Steps.Count; position++)
        {
            var step = workflow.Steps[position];
            await ExecuteAsync(connection, transaction, """
                INSERT INTO WorkflowSteps (Id, WorkflowId, Position, PromptId, Note) VALUES ($id, $workflow, $position, $prompt, $note);
                """,
                ("$id", Text(step.Id)), ("$workflow", Text(workflow.Id)), ("$position", position), ("$prompt", Text(step.PromptId)), ("$note", step.Note));
        }

        foreach (var (name, value) in workflow.Values)
        {
            await ExecuteAsync(connection, transaction, "INSERT INTO WorkflowValues (WorkflowId, Name, Value) VALUES ($workflow, $name, $value);",
                ("$workflow", Text(workflow.Id)), ("$name", name), ("$value", value));
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static string ThisFolder([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;

    /// <summary>A fixed ID for a key, so a rewritten fixture holds the same IDs.</summary>
    private static Guid Id(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    private static string Text(Guid id) => id.ToString("D");

    private static string Text(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(string time) =>
        DateTimeOffset.Parse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
