using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Diagnostics;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.Infrastructure.Tests;

public class LogScrubberTests
{
    private const string Entry = "2026-10-08 14:03:07.123 [INF] PromptService: ";

    [Fact]
    public void Only_lines_shaped_like_log_output_are_kept()
    {
        string[] log =
        [
            Entry + "Saved prompt 6f1c as version 3",
            "System.IO.IOException: The process cannot access the file because it is being used by another process.",
            " ---> Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 5: 'database is locked'.",
            "   at Prompuff.Infrastructure.Persistence.SqliteDatabase.OpenAsync(CancellationToken cancellationToken) in /src/SqliteDatabase.cs:line 34",
            "   --- End of inner exception stack trace ---",
            "1. Pre-flight: tooling, Node and TypeScript versions",
            "Keep the tone friendly.",
        ];

        var scrubber = new LogScrubber(log);

        Assert.Equal(log[..5], scrubber.Kept(0));
        Assert.Equal(2, scrubber.LeftOut(0));
    }

    [Fact]
    public void Quoted_names_are_blanked_out_and_variables_leave_the_line_out()
    {
        var scrubber = new LogScrubber([
            Entry + "Import skipped a workflow: Step 2 (“Plan the upgrade”) has no prompt in a ```prompt block.",
            Entry + "Rendered You are upgrading {{repo_name}}",
        ]);

        Assert.Equal([Entry + "Import skipped a workflow: Step 2 (“…”) has no prompt in a ```prompt block."], scrubber.Kept(0));
        Assert.Equal(1, scrubber.LeftOut(0));
    }

    [Fact]
    public void Lines_holding_library_text_are_left_out()
    {
        var scrubber = new LogScrubber(
            [
                Entry + "Saved prompt 6f1c as version 3",
                Entry + "Opened Angular Upgrade Planner",
                "System.IO.FileNotFoundException: Could not find file '/home/puff/angular-upgrade-planner.md'.",
                Entry + "Couldn't render: First, list the current major dependencies that will block it",
                Entry + "Update check on the Stable channel finished; update available: False",
            ],
            [Entry + "Exported 4 prompts and 0 workflows to a zip"]);

        scrubber.LeaveOutLinesHolding("Angular Upgrade Planner");
        scrubber.LeaveOutLinesHolding("You are a senior engineer.\n\nFirst, list the current major dependencies that will block the upgrade.");
        scrubber.LeaveOutLinesHolding("22"); // Too short to mean anything.
        scrubber.LeaveOutLinesHolding(null);

        Assert.Equal([Entry + "Saved prompt 6f1c as version 3", Entry + "Update check on the Stable channel finished; update available: False"], scrubber.Kept(0));
        Assert.Equal(3, scrubber.LeftOut(0));
        Assert.Single(scrubber.Kept(1));
    }

    [Fact]
    public void Words_ignore_case_and_punctuation_but_keep_accents() =>
        Assert.Equal("café au lait 2", LogScrubber.Words("  Café, au-LAIT #2! "));
}

public class DiagnosticReportTests
{
    private static readonly DiagnosticFacts Facts = new("12.1.3", IsCommandLineToolInstalled: true, Hotkey: "ready");

    private static (DiagnosticReport Report, AppDataPathProvider Paths, JsonSettingsStore Settings) Create(TestLibrary library, string version = "0.9.0")
    {
        var paths = new AppDataPathProvider(new PlatformEnvironment(
            PlatformEnvironment.Current.Platform,
            library.Folder,
            name => name == AppDataPathProvider.OverrideVariable ? library.Folder : null));
        var settings = new JsonSettingsStore(paths, NullLogger<JsonSettingsStore>.Instance);
        var backups = new LibraryBackups(library.Database, library.Time, NullLogger<LibraryBackups>.Instance);
        return (new DiagnosticReport(paths, settings, new InstalledCopy(version), backups, library.Time), paths, settings);
    }

    [Fact]
    public async Task The_report_has_versions_folders_counts_and_settings()
    {
        await using var library = await TestLibrary.CreateAsync();
        var prompt = await library.PromptService.CreateAsync(new PromptContent("Secret Plan", null, "Plan {{repo}}.", null));
        await library.PromptService.SaveContentAsync(prompt.Id, new PromptContent("Secret Plan", null, "Plan {{repo}} twice.", null));
        await library.CollectionService.CreateAsync("Design");
        var (report, paths, settings) = Create(library);
        settings.Save(AppSettings.Default with { Theme = ThemePreference.System, DarkTheme = "nord", Density = Density.Dense, UpdateChannel = UpdateChannel.Beta, AllowMcp = true });

        var text = await report.BuildAsync(Facts);

        foreach (var heading in new[] { "# Prompuff diagnostic info", "## App", "## Folders", "## Library", "## Settings", "## Recent log" })
        {
            Assert.Contains("\n" + heading + "\n", "\n" + text);
        }

        Assert.Contains("- Prompuff: 0.9.0, installed, Beta channel\n", text);
        Assert.Contains("- Avalonia: 12.1.3\n", text);
        Assert.Contains($"- Library: {paths.GetDatabasePath()}\n", text);
        Assert.Contains($"- Backups: {paths.GetBackupDirectory()}\n", text);
        Assert.Contains($"- Schema: {Migrations.LatestVersion}, as this version expects\n", text);
        Assert.Contains("- Prompts: 1, and 0 in Recently deleted\n", text);
        Assert.Contains("- Versions: 2\n", text);
        Assert.Contains("- Collections: 1\n", text);
        Assert.Contains("- Tags: 0\n", text);
        Assert.Contains("- Workflows: 0\n", text);
        Assert.Contains("- Backups: none yet\n", text);
        Assert.Contains("- Theme: System, with nord for dark and prompuff-light for light\n", text);
        Assert.Contains("- Density: Dense\n", text);
        Assert.Contains("- Quick save hotkey: Ctrl+Alt+P, ready\n", text);
        Assert.Contains("- MCP: on\n", text);
        Assert.Contains("- Command-line tool: installed\n", text);
        Assert.Contains("Nothing logged yet.", text);
        Assert.DoesNotContain("Secret Plan", text);
        Assert.DoesNotContain("## Recent command-line log", text);
    }

    [Fact]
    public async Task The_log_is_the_end_of_the_newest_files_with_prompt_text_left_out()
    {
        await using var library = await TestLibrary.CreateAsync();
        await library.PromptService.CreateAsync(new PromptContent("Secret Plan", null, "Plan the whole thing out step by step.", null));
        var (report, paths, _) = Create(library);
        var logs = paths.GetLogsDirectory();
        Directory.CreateDirectory(logs);
        static string Entry(int n) => $"2026-10-0{n / 1000} 09:00:00.{n % 1000:000} [INF] App: Line {n}";
        await File.WriteAllLinesAsync(Path.Combine(logs, "prompuff-20261006.log"), Enumerable.Range(1001, 80).Select(Entry));
        await File.WriteAllLinesAsync(Path.Combine(logs, "prompuff-20261007.log"), Enumerable.Range(2001, 60).Select(Entry).Append(Entry(2061) + " Secret Plan"));
        await File.WriteAllLinesAsync(Path.Combine(logs, "prompuff-cli-20261007.log"), ["2026-10-07 09:00:00.000 [INF] Mcp: MCP server started; access is off"]);
        await File.WriteAllLinesAsync(Path.Combine(logs, "prompuff-20261005.log"), ["2026-10-05 09:00:00.000 [INF] App: Too old to reach"]);

        var text = await report.BuildAsync(Facts);

        // 100 lines: the last 39 of the older file, then all 61 of the newer one, minus the one naming a prompt.
        Assert.DoesNotContain("Line 1041\n", text);
        Assert.Contains("Line 1042\n", text);
        Assert.Contains("Line 2060\n", text);
        Assert.DoesNotContain("Too old to reach", text);
        Assert.DoesNotContain("Secret Plan", text);
        Assert.Contains("Left out 1 line that might hold prompt text.", text);
        Assert.Contains("## Recent command-line log\n\n```text\n2026-10-07 09:00:00.000 [INF] Mcp: MCP server started; access is off\n```\n", text);
    }

    [Fact]
    public async Task A_library_that_can_not_be_read_is_described_rather_than_hidden()
    {
        await using var library = await TestLibrary.CreateAsync();
        var (report, paths, _) = Create(library);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(paths.GetDatabasePath(), "this isn't a database, it's a cake");

        var text = await report.BuildAsync(Facts);

        Assert.Contains("- Problem: couldn't read the library: SqliteException:", text);
        Assert.Contains("## Settings", text);
    }

    private sealed class InstalledCopy(string version) : IUpdateService
    {
        public bool IsSupported => true;

        public string CurrentVersion => version;

        public Task<AvailableUpdate?> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default) =>
            Task.FromResult<AvailableUpdate?>(null);

        public Task DownloadUpdatesAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ApplyUpdatesAndRestart(AvailableUpdate update)
        {
        }
    }
}
