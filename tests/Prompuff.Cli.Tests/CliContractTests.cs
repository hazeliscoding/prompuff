using System.Text.Json.Nodes;
using Prompuff.Tests;

namespace Prompuff.Cli.Tests;

/// <summary>
/// The command line 1.0 promised: its commands, options and exit codes, the fields of its JSON, and the text
/// <c>get</c> and <c>render</c> print. <c>Fixtures/cli-1.0.0.json</c> recorded how 1.0 answered each case in
/// <see cref="Cases"/>, run against the 1.0 library fixture. Every later version answers each case the same way, and
/// may add commands, options and JSON fields. Messages on stderr and the plain-text lists are for people, so they can
/// change.
/// </summary>
public class CliContractTests
{
    private const string FileName = "cli-1.0.0.json";

    private enum Check
    {
        /// <summary>Only the exit code.</summary>
        Exit,

        /// <summary>The exit code and stdout, exactly.</summary>
        Text,

        /// <summary>The exit code and every JSON field 1.0 printed, with the same value.</summary>
        Json,

        /// <summary>The exit code and every JSON field 1.0 printed, with a value of the same kind.</summary>
        Shape,

        /// <summary>The exit code and how stdout starts.</summary>
        Prefix,
    }

    private sealed record Case(Check Check, string[] Args, string? Stdin = null);

    private const string VersionPrefix = "prompuff ";

    /// <summary>What <c>Write_contract</c> recorded for 1.0. A later release with more to promise records its own file.</summary>
    private static readonly Case[] Cases =
    [
        // Finding prompts. Each search here has one answer, so ranking stays free to improve.
        new(Check.Json, ["search", "angular", "--json"]),
        new(Check.Json, ["search", "#日本語", "--json"]),
        new(Check.Json, ["search", "cafe", "--limit", "5", "--json"]),
        new(Check.Json, ["search", "zzzz", "--json"]),
        new(Check.Json, ["list", "--json"]),
        new(Check.Json, ["list", "--favorites", "--json"]),
        new(Check.Json, ["list", "--collection", "engineering", "--json"]),
        new(Check.Json, ["list", "--tag", "bugs", "--json"]),
        new(Check.Json, ["list", "-t", "café", "--json"]),
        new(Check.Json, ["list", "--limit", "2", "--json"]),
        new(Check.Json, ["list", "--limit=2", "--json"]),

        // Reading and rendering: the prompt's own text on stdout.
        new(Check.Text, ["get", "Angular Upgrade Planner"]),
        new(Check.Text, ["get", "angular", "upgrade", "planner"]),
        new(Check.Text, ["get", Contract.FixtureId("prompt:bug")]),
        new(Check.Text, ["get", "--", "Angular Upgrade Planner"]),
        new(Check.Json, ["get", "Café menu translator ☕", "--json"]),
        new(Check.Json, ["get", Contract.FixtureId("prompt:cafe"), "--json"]),
        new(Check.Text, ["render", "Café menu translator ☕", "--var", "cafe_name=Café Sól", "--var=language=日本語"]),
        new(Check.Text, ["render", "Angular Upgrade Planner"]),
        new(Check.Json, ["render", "Angular Upgrade Planner", "-v", "repo_name=acme", "--json"]),

        // Saving from stdin. The new prompt's ID differs every time.
        new(Check.Shape, ["quick-save", "--title", "From a script", "--tag", "Slack", "-t", "#triage", "--collection", "Comms", "--json"], "Summarize {{thread}} as three bullets.\n"),
        new(Check.Exit, ["quick-save"], "Summarize {{thread}}.\n"),

        // 2: no prompt by that name, or several.
        new(Check.Exit, ["get", "No such prompt"]),
        new(Check.Exit, ["render", "No such prompt", "--json"]),
        new(Check.Exit, ["get", "Bug reproduction request"]),
        new(Check.Exit, ["list", "--collection", "No such collection"]),

        // 1: anything else that goes wrong.
        new(Check.Exit, []),
        new(Check.Exit, ["frobnicate"]),
        new(Check.Exit, ["list", "--frob"]),
        new(Check.Exit, ["search"]),
        new(Check.Exit, ["search", "angular", "--limit", "0"]),
        new(Check.Exit, ["render", "Angular Upgrade Planner", "--var", "oops"]),
        new(Check.Exit, ["get", "Angular Upgrade Planner", "--title"]),
        new(Check.Exit, ["quick-save"], "   \n"),

        // Help and the version.
        new(Check.Exit, ["help"]),
        new(Check.Exit, ["--help"]),
        new(Check.Exit, ["-h"]),
        new(Check.Prefix, ["--version"]),
        new(Check.Prefix, ["version"]),
    ];

    public static TheoryData<int> RecordedCases => new(Enumerable.Range(0, Contract.Read(FileName)["cases"]!.AsArray().Count));

    [Theory]
    [MemberData(nameof(RecordedCases))]
    public async Task Each_case_answers_as_1_0_did(int index)
    {
        var contract = Contract.Read(FileName);
        var recorded = contract["cases"]![index]!.AsObject();
        var args = recorded["args"]!.AsArray().Select(Contract.Text).ToArray();
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Text(contract["library"]));

        var (exit, output, _) = await cli.RunWithInputAsync(recorded["stdin"]?.GetValue<string>() ?? string.Empty, args);

        var differences = new List<string>();
        if (exit != recorded["exit"]!.GetValue<int>())
        {
            differences.Add($"exit code {exit}, where 1.0 gave {recorded["exit"]}");
        }

        if (recorded["stdout"] is { } stdout && output != Contract.Text(stdout))
        {
            differences.Add($"stdout was “{Contract.Text(stdout)}” in 1.0 and is now “{output}”");
        }

        if (recorded["stdoutPrefix"] is { } prefix && !output.StartsWith(Contract.Text(prefix), StringComparison.Ordinal))
        {
            differences.Add($"stdout no longer starts with “{Contract.Text(prefix)}”: “{output}”");
        }

        if (recorded["json"] is { } json)
        {
            differences.AddRange(Contract.Differences(json, ParseJson(output)));
        }

        if (recorded["jsonShape"] is { } shape)
        {
            differences.AddRange(Contract.Differences(shape, ParseJson(output), shapeOnly: true));
        }

        Assert.True(differences.Count == 0, $"prompuff {Display(args)}: {string.Join("; ", differences)}");
    }

    /// <summary>
    /// Records <see cref="Cases"/> into a new file and pins it. It's skipped unless <c>PROMPUFF_WRITE_CONTRACT</c> is
    /// set, and refuses to overwrite a pinned file, so the 1.0 recording never changes.
    /// </summary>
    [Fact]
    public async Task Write_contract()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPUFF_WRITE_CONTRACT")))
        {
            Assert.Skip("Set PROMPUFF_WRITE_CONTRACT to record the command line's contract.");
        }

        var path = Path.Combine(PinnedFixtures.TestsFolder, "Prompuff.Cli.Tests", "Fixtures", FileName);
        PinnedFixtures.EnsureUnpinned(path);
        var cases = new JsonArray();
        foreach (var @case in Cases)
        {
            await using var cli = await CliHarness.FromLibraryAsync(Contract.Library);
            var (exit, output, _) = await cli.RunWithInputAsync(@case.Stdin ?? string.Empty, @case.Args);
            var recorded = new JsonObject { ["args"] = new JsonArray([.. @case.Args.Select(arg => JsonValue.Create(arg))]) };
            if (@case.Stdin is not null)
            {
                recorded["stdin"] = @case.Stdin;
            }

            recorded["exit"] = exit;
            switch (@case.Check)
            {
                case Check.Text:
                    recorded["stdout"] = output;
                    break;
                case Check.Json:
                    recorded["json"] = JsonNode.Parse(output);
                    break;
                case Check.Shape:
                    recorded["jsonShape"] = JsonNode.Parse(output);
                    break;
                case Check.Prefix:
                    Assert.StartsWith(VersionPrefix, output);
                    recorded["stdoutPrefix"] = VersionPrefix;
                    break;
            }

            cases.Add(recorded);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Contract.Write(path, new JsonObject { ["release"] = "1.0.0", ["library"] = Contract.Library, ["cases"] = cases });
        PinnedFixtures.Pin(path);
    }

    private static JsonNode? ParseJson(string output)
    {
        try
        {
            return JsonNode.Parse(output);
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create($"(not JSON) {output}");
        }
    }

    private static string Display(IEnumerable<string> args) =>
        string.Join(' ', args.Select(arg => arg.Length == 0 || arg.Contains(' ') ? $"\"{arg}\"" : arg));
}
