using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.Cli.Tests;

/// <summary>A library in a temporary folder, with the sample prompts, and a way to run prompuff against it.</summary>
internal sealed class CliHarness : IAsyncDisposable
{
    private CliHarness(string folder)
    {
        Folder = folder;
        Paths = new AppDataPathProvider(new PlatformEnvironment(
            PlatformEnvironment.Current.Platform,
            PlatformEnvironment.Current.HomeDirectory,
            name => name == AppDataPathProvider.OverrideVariable ? folder : null));
    }

    public string Folder { get; }
    public AppDataPathProvider Paths { get; }

    public static async Task<CliHarness> CreateAsync(bool samples = true, bool allowMcp = false)
    {
        var harness = new CliHarness(Path.Combine(Path.GetTempPath(), "prompuff-cli-tests", Guid.NewGuid().ToString("N")));
        await using (var library = await CliLibrary.OpenAsync(harness.Paths))
        {
            if (samples)
            {
                var files = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples"), "*.md").Order().ToList();
                Assert.Empty((await library.Transfer.ImportFilesAsync(files)).Failures);
            }

            library.Settings.Save(library.Settings.Load() with { AllowMcp = allowMcp });
        }

        return harness;
    }

    public async Task<(int Exit, string Output, string Error)> RunWithInputAsync(string input, params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exit = await CliApp.RunAsync(args, new StringReader(input), output, error, Paths);
        return (exit, output.ToString(), error.ToString());
    }

    public Task<(int Exit, string Output, string Error)> RunAsync(params string[] args) => RunWithInputAsync(string.Empty, args);

    public ValueTask DisposeAsync()
    {
        // Only this library's pool: clearing every pool would close connections other tests are about to use.
        new SqliteDatabase(Paths.GetDatabasePath(), Paths.GetBackupDirectory(), NullLogger<SqliteDatabase>.Instance).ClearPool();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folders are cleaned up by the OS eventually.
        }

        return ValueTask.CompletedTask;
    }
}

public class CliTests
{
    [Fact]
    public async Task Render_fills_variables_and_lists_the_ones_left()
    {
        await using var cli = await CliHarness.CreateAsync();

        var (exit, output, error) = await cli.RunAsync("render", "Angular Upgrade Planner", "--var", "repo_name=acme", "--var=target_version=22");

        Assert.Equal(CliApp.Ok, exit);
        Assert.StartsWith("You are a senior Angular engineer planning an upgrade of acme to Angular 22.", output);
        Assert.Contains("Keep the tone {{tone}}.", output);
        Assert.Contains("package_manager, tone", error);
    }

    [Fact]
    public async Task Render_as_JSON_carries_the_missing_variables()
    {
        await using var cli = await CliHarness.CreateAsync();

        var (exit, output, _) = await cli.RunAsync("render", "angular upgrade planner", "--json", "-v", "repo_name=acme");

        Assert.Equal(CliApp.Ok, exit);
        Assert.Contains("\"rendered\": \"You are a senior Angular engineer planning an upgrade of acme", output);
        Assert.Contains("\"missing\": [", output);
        Assert.Contains("\"target_version\"", output);
    }

    [Fact]
    public async Task Search_and_list_print_one_prompt_per_line()
    {
        await using var cli = await CliHarness.CreateAsync();

        var (exit, output, _) = await cli.RunAsync("search", "#design");
        Assert.Equal(CliApp.Ok, exit);
        Assert.Equal(2, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("· Design · #design", output);

        var favorites = await cli.RunAsync("list", "--favorites");
        Assert.Equal(["Angular Upgrade Planner", "Design System Generator", "UI Mockup Generator"],
            favorites.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(row => row.Split("  ·")[0]));

        var collection = await cli.RunAsync("list", "--collection", "design", "--json");
        Assert.Contains("\"collection\": \"Design\"", collection.Output);

        var none = await cli.RunAsync("search", "zzzz");
        Assert.Equal(CliApp.Ok, none.Exit);
        Assert.Empty(none.Output);
        Assert.Contains("Nothing matches", none.Error);
    }

    [Fact]
    public async Task Get_prints_the_body_or_everything_as_JSON()
    {
        await using var cli = await CliHarness.CreateAsync();

        var plain = await cli.RunAsync("get", "README", "Cleanup");
        Assert.Equal(CliApp.Ok, plain.Exit);
        Assert.DoesNotContain("title", plain.Output);

        var json = await cli.RunAsync("get", "README Cleanup", "--json");
        Assert.Contains("\"title\": \"README Cleanup\"", json.Output);
        Assert.Contains("\"variables\": [", json.Output);
        Assert.Contains("\"collection\": \"Writing & docs\"", json.Output);
    }

    [Fact]
    public async Task Missing_and_ambiguous_names_exit_with_2_and_say_why()
    {
        await using var cli = await CliHarness.CreateAsync();
        await cli.RunWithInputAsync("Copy of a prompt", "quick-save", "--title", "Twin");
        await cli.RunWithInputAsync("Copy of a prompt, again", "quick-save", "--title", "Twin");

        var missing = await cli.RunAsync("get", "Generator");
        Assert.Equal(CliApp.NotFound, missing.Exit);
        Assert.Contains("Did you mean one of these?", missing.Error);
        Assert.Contains("Design System Generator", missing.Error);

        var twins = await cli.RunAsync("render", "Twin");
        Assert.Equal(CliApp.NotFound, twins.Exit);
        Assert.Contains("Several prompts are called “Twin”", twins.Error);

        var id = twins.Error.Split('\n').First(line => line.Contains('(')).Split('(', ')')[1];
        var byId = await cli.RunAsync("get", id);
        Assert.Equal(CliApp.Ok, byId.Exit);
    }

    [Fact]
    public async Task Quick_save_reads_stdin_and_names_the_prompt_from_its_first_line()
    {
        await using var cli = await CliHarness.CreateAsync(samples: false);

        var saved = await cli.RunWithInputAsync("# Summarize {{thread}} as three bullets\nKeep names out.\n", "quick-save", "--tag", "Slack", "--tag", "#triage", "--collection", "Comms", "--json");

        Assert.Equal(CliApp.Ok, saved.Exit);
        Assert.Contains("\"title\": \"Summarize {{thread}} as three bullets\"", saved.Output);
        var listed = await cli.RunAsync("list");
        Assert.Equal("Summarize {{thread}} as three bullets  · Comms · #slack #triage\n", listed.Output);

        var empty = await cli.RunWithInputAsync("   \n", "quick-save");
        Assert.Equal(CliApp.Failed, empty.Exit);
        Assert.Contains("Nothing to save", empty.Error);
    }

    [Fact]
    public async Task Usage_mistakes_explain_themselves()
    {
        await using var cli = await CliHarness.CreateAsync(samples: false);

        Assert.Equal(CliApp.Failed, (await cli.RunAsync()).Exit);
        var help = await cli.RunAsync("help");
        Assert.Equal(CliApp.Ok, help.Exit);
        Assert.Contains("prompuff render <prompt> [--var name=value ...]", help.Output);
        Assert.Equal($"prompuff {CliApp.Version}\n", (await cli.RunAsync("--version")).Output);
        Assert.Contains("doesn't know the option --frob", (await cli.RunAsync("list", "--frob")).Error);
        Assert.Contains("--var needs name=value", (await cli.RunAsync("render", "x", "--var", "oops")).Error);
        Assert.Contains("--limit needs a positive number", (await cli.RunAsync("search", "x", "--limit", "0")).Error);
        Assert.Contains("Run prompuff help", (await cli.RunAsync("frobnicate")).Error);
    }
}
