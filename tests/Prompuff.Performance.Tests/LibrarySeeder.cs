using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.Performance.Tests;

/// <summary>What the seeder put in the library, so tests can check against it.</summary>
internal sealed record SeededLibraryInfo(
    int Prompts,
    int Deleted,
    int Favorites,
    int Uncategorized,
    int Versions,
    IReadOnlyDictionary<string, int> TagUses,
    IReadOnlyDictionary<Guid, string> Collections,
    TimeSpan Elapsed);

/// <summary>
/// Fills a library with generated prompts through the real schema and its triggers, in one transaction: varied titles,
/// descriptions, bodies with variables, notes, tags, collections, favorites, ratings and versions, plus accented titles,
/// copies and prompts in Recently deleted. The same seed always makes the same prompts; only the times follow the clock,
/// so nothing ages out of Recently deleted.
/// </summary>
internal static class LibrarySeeder
{
    private static readonly string[] Verbs =
    [
        "Refactor", "Review", "Summarize", "Explain", "Draft", "Plan", "Debug", "Generate", "Translate", "Outline",
        "Rewrite", "Audit", "Design", "Test", "Document", "Compare", "Estimate", "Brainstorm", "Critique", "Migrate",
        "Optimize", "Profile", "Polish", "Triage", "Simplify", "Benchmark", "Label", "Prioritize", "Sketch", "Validate",
    ];

    // A subject, and the tag it usually carries.
    private static readonly (string Text, string? Tag)[] Subjects =
    [
        ("Angular Service Layer", "angular"), ("Angular Upgrade", "angular"), ("React Hooks", "react"), ("React Component Tree", "react"),
        ("SQL Query", "sql"), ("Database Schema", "sql"), ("Pull Request", "review"), ("Release Notes", "writing"),
        ("Onboarding Guide", "docs"), ("API Contract", "api"), ("REST Endpoints", "api"), ("Unit Tests", "testing"),
        ("Integration Tests", "testing"), ("Error Messages", "ux"), ("Landing Page Copy", "marketing"), ("Product Spec", "product"),
        ("Incident Report", "ops"), ("Interview Notes", "research"), ("Marketing Email", "marketing"), ("Data Pipeline", "data"),
        ("Kubernetes Manifest", "devops"), ("Terraform Module", "devops"), ("CSS Theme", "css"), ("Accessibility Audit", "a11y"),
        ("Commit Message", "git"), ("User Story", "product"), ("Design Tokens", "design"), ("Shell Script", "bash"),
        ("Regular Expression", "regex"), ("Changelog", "writing"), ("Meeting Notes", "writing"), ("Python Notebook", "python"),
        ("Rust Borrow Errors", "rust"), ("Go Concurrency Bug", "go"), ("C# Records", "dotnet"), ("Avalonia View", "dotnet"),
        ("Security Threat Model", "security"), ("Privacy Policy", "legal"), ("Customer Support Reply", "support"), ("Sales Pitch", "sales"),
        ("Résumé", "career"), ("Cover Letter", "career"), ("Café Menu", "food"), ("Crème Brûlée Recipe", "food"),
        ("Jalapeño Salsa Recipe", "food"), ("Naïve Bayes Classifier", "ml"), ("Façade Pattern", "patterns"), ("Piñata Party Plan", "personal"),
        ("Smörgåsbord Shopping List", "personal"), ("Über-Long Function", "refactoring"), ("Prompt Library", "meta"), ("System Prompt", "meta"),
    ];

    private static readonly string[] Qualifiers =
    [
        "for Beginners", "in Plain English", "Step by Step", "with Examples", "for a Monorepo", "Before Release", "v2",
        "Checklist", "Deep Dive", "for Stakeholders", "in Five Bullets", "for Code Review", "Quick Pass", "the Hard Way",
        "for Mobile", "at Scale", "with Edge Cases", "for the Team Wiki", "Template", "Cheat Sheet",
    ];

    private static readonly string[] DescriptionVerbs =
        ["Turns", "Produces", "Reviews", "Explains", "Drafts", "Checks", "Summarizes", "Plans", "Rewrites", "Breaks down"];

    private static readonly string[] Purposes =
    [
        "into numbered steps", "for a non-technical audience", "in under two hundred words", "and flags anything risky",
        "with a risk table at the end", "with before and after examples", "so a new teammate can follow it",
        "and lists the assumptions it made", "with the commands to run", "in the team's house style",
        "with one question per open issue", "and suggests a rollback plan", "as a short checklist", "with sources cited",
    ];

    private static readonly string[] Roles =
    [
        "senior Angular engineer", "technical writer", "staff backend engineer", "product designer", "data analyst",
        "security reviewer", "site reliability engineer", "patient teacher", "copy editor", "QA lead", "pastry chef",
        "marketing strategist", "database administrator", "accessibility specialist", "release manager", "career coach",
    ];

    private static readonly string[] Variables =
    [
        "repo_name", "target_version", "language", "tone", "audience", "input", "report", "diff", "schema", "error_log",
        "ticket", "notes", "draft", "framework", "deadline", "word_limit", "product_name", "persona", "context", "goal",
        "package_manager", "branch", "file_path", "metric", "dataset", "question", "style_guide", "region", "budget", "format",
    ];

    private static readonly string[] Steps =
    [
        "List the assumptions you are making before you start.",
        "Group the findings by severity, most serious first.",
        "Call out anything that would break existing callers.",
        "Suggest the smallest change that fixes the problem.",
        "Write the answer for someone who has never seen this code.",
        "Quote the exact line you are talking about.",
        "Give one example of good output and one of bad output.",
        "Point out duplicated logic and propose one place for it.",
        "Check the edge cases: empty input, huge input and unicode.",
        "Estimate the effort for each item as small, medium or large.",
        "Name the trade-offs instead of hiding them.",
        "Keep the public API unchanged unless I say otherwise.",
        "Prefer boring, well-known solutions over clever ones.",
        "End with the three questions you would ask the author.",
        "Mark anything you are unsure about with a question mark.",
        "Separate facts from opinions.",
        "Rewrite long sentences into short ones.",
        "Keep every heading under six words.",
        "Show the before and after side by side.",
        "Use the metric system and ISO dates.",
        "Translate jargon into plain words the first time it appears.",
        "Order the steps so each one can be verified on its own.",
        "Include the command to run for every step.",
        "Flag anything that touches personal data.",
        "Prefer composition over inheritance in your suggestions.",
        "Stop and ask if the request is ambiguous.",
        "Summarize the result in one sentence at the top.",
        "Leave the tests passing after every step.",
    ];

    private static readonly string[] Formats =
    [
        "Format the answer as a Markdown table.", "Answer with a numbered list.", "Reply in JSON with keys summary and items.",
        "Use short paragraphs with bold lead-ins.", "Return a unified diff and nothing else.", "Write it as a checklist with boxes.",
        "Answer in three sections: Context, Plan, Risks.", "Keep the whole answer under 300 words.",
    ];

    private static readonly string[] Filler =
    [
        "The codebase is a few years old and has grown in several directions at once.",
        "Most of the team works across time zones, so written context matters more than meetings.",
        "We deploy several times a day, and rollbacks need to be quick and boring.",
        "The audience skims, so the first paragraph has to carry the point.",
        "Performance matters on older laptops, not just on a fast workstation.",
        "Earlier attempts failed because the model skipped the verification step.",
        "Whatever you suggest has to work on Windows, macOS and Linux.",
        "Assume the reader knows the domain but not this particular project.",
        "We care more about clarity than cleverness.",
        "There is no budget for new infrastructure this quarter.",
        "Accessibility is a requirement, not a nice-to-have.",
        "The data is messy: missing values, duplicates and inconsistent casing.",
        "Legal asked that nothing leaves the machine.",
        "Our style guide prefers active voice and short sentences.",
        "The previous version of this document confused new hires.",
        "The service handles bursts of traffic every Monday morning.",
        "Some of the tests are flaky, so separate real failures from noise.",
        "Please keep the playful tone of the original where it fits.",
        "The café opens at seven and the menu changes with the season.",
        "Résumés are read in about six seconds, so lead with impact.",
    ];

    private static readonly string[] NotesBank =
    [
        "Asking for assumptions first stopped the model from guessing silently.",
        "Numbered steps made the output much easier to review.",
        "The word limit keeps it from rambling.",
        "Works better when the input is pasted between the dashes.",
        "Calling out the audience changed the tone completely.",
        "Without the example, it invented its own format every time.",
        "Good for first drafts; still needs a human pass.",
        "The rollback question caught two risky changes last week.",
        "Shorter context worked better than pasting the whole file.",
        "Asking for JSON made it easy to pipe into a script.",
    ];

    private static readonly string[] CollectionNames =
        ["Engineering", "Design", "Writing", "Research", "Marketing", "Data", "DevOps", "Support", "Product", "Learning", "Personal", "Café Ideas"];

    private static readonly string[] ExtraTags =
    [
        "review", "copilot", "claude", "template", "idea", "draft", "quick", "long-form", "checklist", "favorite-of-team",
        "work", "side-project", "onboarding", "performance", "cleanup", "brainstorm", "interview", "teaching", "summary", "planning",
    ];

    public static async Task<SeededLibraryInfo> SeedAsync(string databasePath, int count, int seed = 2026)
    {
        var watch = Stopwatch.StartNew();
        var folder = Path.GetDirectoryName(Path.GetFullPath(databasePath))!;
        var database = new SqliteDatabase(databasePath, Path.Combine(folder, "backups"), NullLogger<SqliteDatabase>.Instance);
        await database.InitializeAsync();

        var random = new Random(seed);
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero);

        await using var connection = await database.OpenAsync();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        using var insertCollection = Command(connection, transaction, "INSERT INTO Collections (Id, Name, CreatedAt) VALUES ($id, $name, $created);", "$id", "$name", "$created");
        using var insertTag = Command(connection, transaction, "INSERT INTO Tags (Id, Name) VALUES ($id, $name);", "$id", "$name");
        using var insertPrompt = Command(connection, transaction, """
            INSERT INTO Prompts (Id, Title, Description, Body, Notes, IsFavorite, Rating, CollectionId, CreatedAt, UpdatedAt, LastOpenedAt, DeletedAt, ParentPromptId)
            VALUES ($id, $title, $description, $body, $notes, $favorite, $rating, $collection, $created, $updated, $opened, $deleted, $parent);
            """, "$id", "$title", "$description", "$body", "$notes", "$favorite", "$rating", "$collection", "$created", "$updated", "$opened", "$deleted", "$parent");
        using var insertPromptTag = Command(connection, transaction, "INSERT INTO PromptTags (PromptId, TagId, Position) VALUES ($prompt, $tag, $position);", "$prompt", "$tag", "$position");
        using var insertVersion = Command(connection, transaction, """
            INSERT INTO PromptVersions (Id, PromptId, VersionNumber, Title, Description, Body, Notes, Note, SavedAt)
            VALUES ($id, $prompt, $number, $title, $description, $body, $notes, $note, $saved);
            """, "$id", "$prompt", "$number", "$title", "$description", "$body", "$notes", "$note", "$saved");

        var collections = new Dictionary<Guid, string>();
        var collectionIds = new List<Guid>();
        foreach (var name in CollectionNames)
        {
            var id = NewId(random);
            collections[id] = name;
            collectionIds.Add(id);
            Run(insertCollection, SqlValues.Id(id), name, SqlValues.Time(now - TimeSpan.FromDays(800)));
        }

        var tagIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var tagUses = new Dictionary<string, int>(StringComparer.Ordinal);
        var promptIds = new List<Guid>(count);
        int deleted = 0, favorites = 0, uncategorized = 0, versions = 0;

        for (var i = 0; i < count; i++)
        {
            var id = NewId(random);
            var subject = Subjects[Skewed(random, Subjects.Length)];
            var title = $"{Verbs[random.Next(Verbs.Length)]} {subject.Text}" + (random.Next(2) == 0 ? " " + Qualifiers[random.Next(Qualifiers.Length)] : "");
            Guid? parent = null;
            if (i > 50 && random.Next(100) < 3)
            {
                parent = promptIds[random.Next(promptIds.Count)];
                title += " (copy)";
            }

            var description = random.Next(10) == 0
                ? null
                : $"{DescriptionVerbs[random.Next(DescriptionVerbs.Length)]} {Lower(subject.Text)} {Purposes[random.Next(Purposes.Length)]}.";
            var body = Body(random, subject.Text);
            var notes = random.Next(100) < 40 ? NotesBank[random.Next(NotesBank.Length)] : null;
            var isFavorite = random.Next(100) < 8;
            int? rating = random.Next(100) switch
            {
                < 25 => null,
                < 29 => 1,
                < 36 => 2,
                < 55 => 3,
                < 80 => 4,
                _ => 5,
            };
            Guid? collection = random.Next(100) < 15 ? null : collectionIds[Skewed(random, collectionIds.Count)];

            var created = now - TimeSpan.FromMinutes(random.Next(1, 730 * 24 * 60));
            var updated = random.Next(100) < 40 ? created : created + (now - created) * random.NextDouble();
            DateTimeOffset? opened = random.Next(100) < 35 ? updated + (now - updated) * random.NextDouble() : null;
            DateTimeOffset? deletedAt = random.Next(100) == 0 ? Max(updated, now - TimeSpan.FromHours(random.Next(1, 25 * 24))) : null;

            Run(insertPrompt,
                SqlValues.Id(id), title, SqlValues.TextOrNull(description), body, SqlValues.TextOrNull(notes),
                isFavorite ? 1 : 0, SqlValues.IntOrNull(rating), SqlValues.IdOrNull(collection),
                SqlValues.Time(created), SqlValues.Time(updated), SqlValues.TimeOrNull(opened), SqlValues.TimeOrNull(deletedAt), SqlValues.IdOrNull(parent));

            var tags = Tags(random, subject.Tag);
            for (var position = 0; position < tags.Count; position++)
            {
                if (!tagIds.TryGetValue(tags[position], out var tagId))
                {
                    tagId = SqlValues.Id(NewId(random));
                    tagIds[tags[position]] = tagId;
                    Run(insertTag, tagId, tags[position]);
                }

                Run(insertPromptTag, SqlValues.Id(id), tagId, position);
                if (deletedAt is null)
                {
                    tagUses[tags[position]] = tagUses.GetValueOrDefault(tags[position]) + 1;
                }
            }

            // Older versions had less of the body and sometimes another title; the last one is the prompt as it is now.
            var versionCount = 1;
            while (versionCount < 7 && random.Next(100) < 45)
            {
                versionCount++;
            }

            var lines = body.Split('\n');
            for (var number = 1; number <= versionCount; number++)
            {
                var latest = number == versionCount;
                var versionTitle = latest || random.Next(3) > 0 ? title : title + " (draft)";
                var versionBody = latest ? body : string.Join('\n', lines.Take(Math.Max(1, lines.Length * number / versionCount)));
                var savedAt = versionCount == 1 ? created : created + (updated - created) * ((number - 1) / (double)(versionCount - 1));
                var note = number == 1 ? "First version" : $"Edited body (+{random.Next(1, 9)} −{random.Next(0, 4)} lines)";
                Run(insertVersion,
                    SqlValues.Id(NewId(random)), SqlValues.Id(id), number, versionTitle, SqlValues.TextOrNull(description), versionBody,
                    SqlValues.TextOrNull(notes), note, SqlValues.Time(savedAt));
            }

            promptIds.Add(id);
            versions += versionCount;
            if (deletedAt is not null)
            {
                deleted++;
                continue;
            }

            favorites += isFavorite ? 1 : 0;
            uncategorized += collection is null ? 1 : 0;
        }

        await transaction.CommitAsync();

        // One file, with nothing left in the write-ahead log, so a test can copy it.
        await using (var checkpoint = connection.CreateCommand())
        {
            checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await checkpoint.ExecuteNonQueryAsync();
        }

        return new SeededLibraryInfo(count - deleted, deleted, favorites, uncategorized, versions, tagUses, collections, watch.Elapsed);
    }

    private static string Body(Random random, string subject)
    {
        var body = new StringBuilder();
        var variable = Variables[random.Next(Variables.Length)];
        body.Append(random.Next(3) switch
        {
            0 => $"You are a {Roles[random.Next(Roles.Length)]}. Help me with the {Lower(subject)} for {{{{{variable}}}}}.",
            1 => $"Act as a {Roles[random.Next(Roles.Length)]} and work through this {Lower(subject)} with me.",
            _ => $"I need a second pair of eyes on a {Lower(subject)}. You are a {Roles[random.Next(Roles.Length)]}.",
        });
        body.Append("\n\nHere is what I have:\n\n---\n{{").Append(Variables[random.Next(Variables.Length)]).Append("}}\n---\n\n");

        var steps = random.Next(3, 9);
        for (var step = 1; step <= steps; step++)
        {
            body.Append(step).Append(". ").Append(Steps[random.Next(Steps.Length)]).Append('\n');
        }

        body.Append("\nKeep the tone {{tone}} and write for {{audience}}. ").Append(Formats[random.Next(Formats.Length)]);

        // Most prompts are short; a few carry pages of context.
        var paragraphs = random.Next(100) < 5 ? random.Next(6, 14) : random.Next(0, 4);
        for (var paragraph = 0; paragraph < paragraphs; paragraph++)
        {
            body.Append("\n\n");
            var sentences = random.Next(2, 5);
            for (var sentence = 0; sentence < sentences; sentence++)
            {
                body.Append(sentence == 0 ? "" : " ").Append(Filler[random.Next(Filler.Length)]);
            }
        }

        return body.ToString();
    }

    private static List<string> Tags(Random random, string? subjectTag)
    {
        var tags = new List<string>();
        if (subjectTag is not null && random.Next(100) < 70)
        {
            tags.Add(subjectTag);
        }

        var extra = random.Next(100) switch
        {
            < 15 => 0,
            < 45 => 1,
            < 75 => 2,
            < 92 => 3,
            _ => 4,
        };
        for (var i = 0; i < extra; i++)
        {
            var tag = TagName.Normalize(ExtraTags[Skewed(random, ExtraTags.Length)])!;
            if (!tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>A few items are common and the rest rarer, as in a real library.</summary>
    private static int Skewed(Random random, int length) => (int)(length * Math.Pow(random.NextDouble(), 1.8));

    private static string Lower(string text) => char.ToLowerInvariant(text[0]) + text[1..];

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static Guid NewId(Random random)
    {
        Span<byte> bytes = stackalloc byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var name in parameters)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = name });
        }

        command.Prepare();
        return command;
    }

    private static void Run(SqliteCommand command, params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            command.Parameters[i].Value = values[i];
        }

        command.ExecuteNonQuery();
    }
}
