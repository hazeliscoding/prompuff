using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Prompuff.Infrastructure.ImportExport;
using Prompuff.Tests;

namespace Prompuff.Cli.Tests;

/// <summary>
/// The MCP server 1.0 promised: its tools and what they take, what they answer, every prompt as a
/// <c>prompuff://prompts/{id}</c> resource, and favorites as prompts with their variables as arguments.
/// <c>Fixtures/mcp-1.0.0.json</c> recorded how 1.0 answered, run against the 1.0 library fixture. Every later version
/// answers the same, and may add tools, inputs, fields and resources. Titles, descriptions, instructions and error
/// wording are for people and models to read, so they can change.
/// </summary>
public class McpContractTests
{
    private const string FileName = "mcp-1.0.0.json";

    private static readonly (string Tool, JsonObject Arguments)[] ToolCalls =
    [
        ("search_prompts", new() { ["query"] = "angular" }),
        ("search_prompts", new() { ["query"] = "#日本語", ["limit"] = 5 }),
        ("get_prompt", new() { ["prompt"] = "Angular Upgrade Planner" }),
        ("get_prompt", new() { ["prompt"] = Contract.FixtureId("prompt:cafe") }),
        ("render_prompt", new() { ["prompt"] = "Angular Upgrade Planner", ["variables"] = new JsonObject { ["repo_name"] = "acme", ["target_version"] = "22" } }),
        ("render_prompt", new() { ["prompt"] = "Café menu translator ☕" }),
        ("get_prompt", new() { ["prompt"] = "No such prompt" }),
        ("render_prompt", new() { ["prompt"] = "No such prompt" }),
    ];

    private static readonly (string Prompt, JsonObject Arguments)[] PromptCalls =
    [
        ("angular-upgrade-planner", new() { ["repo_name"] = "acme" }),
        ("angular-upgrade-planner", new()),
    ];

    [Fact]
    public async Task Tools_keep_their_names_and_take_what_1_0_took()
    {
        var contract = Contract.Read(FileName);
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Text(contract["library"]), allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        Assert.Equal(Contract.Text(contract["server"]), mcp.Client.ServerInfo.Name);
        var tools = Tools(await mcp.Client.ListToolsAsync());
        var differences = new List<string>();
        foreach (var (name, recorded) in contract["tools"]!.AsObject())
        {
            if (tools[name] is not JsonObject current)
            {
                differences.Add($"the {name} tool is gone");
                continue;
            }

            differences.AddRange(Contract.Differences(recorded!["inputs"], current["inputs"]).Select(difference => $"{name} inputs: {difference}"));
            differences.AddRange(Names(current["required"]).Except(Names(recorded["required"])).Select(input => $"{name} now requires {input}"));
            if (current["readOnly"]!.GetValue<bool>() != recorded["readOnly"]!.GetValue<bool>())
            {
                differences.Add($"{name} is no longer read-only");
            }
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    [Fact]
    public async Task Tool_calls_answer_as_1_0_did()
    {
        var contract = Contract.Read(FileName);
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Text(contract["library"]), allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var differences = new List<string>();
        foreach (var recorded in contract["toolCalls"]!.AsArray().Select(call => call!.AsObject()))
        {
            var tool = Contract.Text(recorded["tool"]);
            var current = await CallAsync(mcp.Client, tool, recorded["arguments"]!.AsObject(), recorded.ContainsKey("text"));
            var call = $"{tool} {recorded["arguments"]!.ToJsonString()}";
            differences.AddRange(Contract.Differences(recorded, current).Select(difference => $"{call}: {difference}"));
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    [Fact]
    public async Task Every_1_0_resource_still_reads_as_the_same_prompt()
    {
        var contract = Contract.Read(FileName);
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Text(contract["library"]), allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var listed = (await mcp.Client.ListResourcesAsync()).ToDictionary(resource => resource.Uri, resource => resource.ProtocolResource.MimeType);
        var differences = new List<string>();
        foreach (var (uri, recorded) in contract["resources"]!.AsObject())
        {
            if (!listed.TryGetValue(uri, out var mimeType))
            {
                differences.Add($"{uri} isn't listed");
                continue;
            }

            var read = Assert.IsType<TextResourceContents>(Assert.Single((await mcp.Client.ReadResourceAsync(uri)).Contents));
            var expected = Contract.Text(recorded!["mimeType"]);
            if (mimeType != expected || read.MimeType != expected)
            {
                differences.Add($"{uri} is {mimeType} when listed and {read.MimeType} when read, where 1.0 said {expected}");
            }

            // The Markdown may gain metadata, so the resource is held to the prompt it carries, not its exact text.
            if (Prompt(read.Text) != Prompt(Contract.Text(recorded["text"])))
            {
                differences.Add($"{uri} now reads as {Prompt(read.Text)}, where 1.0 gave {Prompt(Contract.Text(recorded["text"]))}");
            }
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    [Fact]
    public async Task Favorites_are_still_prompts_with_their_arguments()
    {
        var contract = Contract.Read(FileName);
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Text(contract["library"]), allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var prompts = Prompts(await mcp.Client.ListPromptsAsync());
        var differences = new List<string>();
        foreach (var (name, recorded) in contract["prompts"]!.AsObject())
        {
            if (prompts[name] is not JsonObject current)
            {
                differences.Add($"the {name} prompt is gone");
                continue;
            }

            differences.AddRange(Names(recorded!["arguments"]).Except(Names(current["arguments"])).Select(argument => $"{name} lost its {argument} argument"));
            differences.AddRange(Names(current["required"]).Except(Names(recorded["required"])).Select(argument => $"{name} now requires {argument}"));
        }

        foreach (var recorded in contract["promptCalls"]!.AsArray().Select(call => call!.AsObject()))
        {
            var name = Contract.Text(recorded["prompt"]);
            var current = await GetPromptAsync(mcp.Client, name, recorded["arguments"]!.AsObject());
            var call = $"{name} {recorded["arguments"]!.ToJsonString()}";
            differences.AddRange(Contract.Differences(recorded, current).Select(difference => $"{call}: {difference}"));
        }

        Assert.True(differences.Count == 0, string.Join("; ", differences));
    }

    /// <summary>
    /// Records the server into a new file and pins it. It's skipped unless <c>PROMPUFF_WRITE_CONTRACT</c> is set, and
    /// refuses to overwrite a pinned file, so the 1.0 recording never changes.
    /// </summary>
    [Fact]
    public async Task Write_contract()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPUFF_WRITE_CONTRACT")))
        {
            Assert.Skip("Set PROMPUFF_WRITE_CONTRACT to record the MCP server's contract.");
        }

        var path = Path.Combine(PinnedFixtures.TestsFolder, "Prompuff.Cli.Tests", "Fixtures", FileName);
        PinnedFixtures.EnsureUnpinned(path);
        await using var cli = await CliHarness.FromLibraryAsync(Contract.Library, allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var toolCalls = new JsonArray();
        foreach (var (tool, arguments) in ToolCalls)
        {
            toolCalls.Add(await CallAsync(mcp.Client, tool, arguments, asText: tool == "render_prompt"));
        }

        var resources = new JsonObject();
        foreach (var resource in await mcp.Client.ListResourcesAsync())
        {
            var read = Assert.IsType<TextResourceContents>(Assert.Single((await mcp.Client.ReadResourceAsync(resource.Uri)).Contents));
            resources[resource.Uri] = new JsonObject { ["mimeType"] = resource.ProtocolResource.MimeType, ["text"] = read.Text };
        }

        var promptCalls = new JsonArray();
        foreach (var (prompt, arguments) in PromptCalls)
        {
            promptCalls.Add(await GetPromptAsync(mcp.Client, prompt, arguments));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Contract.Write(path, new JsonObject
        {
            ["release"] = "1.0.0",
            ["library"] = Contract.Library,
            ["server"] = mcp.Client.ServerInfo.Name,
            ["tools"] = Tools(await mcp.Client.ListToolsAsync()),
            ["toolCalls"] = toolCalls,
            ["resources"] = resources,
            ["prompts"] = Prompts(await mcp.Client.ListPromptsAsync()),
            ["promptCalls"] = promptCalls,
        });
        PinnedFixtures.Pin(path);
    }

    /// <summary>Each tool by name: the type of each input, the inputs it requires, and whether it only reads.</summary>
    private static JsonObject Tools(IEnumerable<McpClientTool> tools)
    {
        var result = new JsonObject();
        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.InputSchema;
            var inputs = new JsonObject();
            foreach (var property in schema.GetProperty("properties").EnumerateObject())
            {
                inputs[property.Name] = property.Value.GetProperty("type").GetString();
            }

            result[tool.Name] = new JsonObject
            {
                ["inputs"] = inputs,
                ["required"] = schema.TryGetProperty("required", out var required)
                    ? new JsonArray([.. required.EnumerateArray().Select(input => JsonValue.Create(input.GetString()))])
                    : new JsonArray(),
                ["readOnly"] = tool.ProtocolTool.Annotations?.ReadOnlyHint ?? false,
            };
        }

        return result;
    }

    /// <summary>Each prompt by name, with its arguments and the ones it requires.</summary>
    private static JsonObject Prompts(IEnumerable<McpClientPrompt> prompts)
    {
        var result = new JsonObject();
        foreach (var prompt in prompts)
        {
            var arguments = prompt.ProtocolPrompt.Arguments ?? [];
            result[prompt.Name] = new JsonObject
            {
                ["arguments"] = new JsonArray([.. arguments.Select(argument => JsonValue.Create(argument.Name))]),
                ["required"] = new JsonArray([.. arguments.Where(argument => argument.Required == true).Select(argument => JsonValue.Create(argument.Name))]),
            };
        }

        return result;
    }

    /// <summary>A tool's answer: whether it's an error, and otherwise its JSON, or its first block of text.</summary>
    private static async Task<JsonObject> CallAsync(McpClient client, string tool, JsonObject arguments, bool asText)
    {
        var result = await client.CallToolAsync(tool, Contract.Arguments(arguments));
        var answer = new JsonObject { ["tool"] = tool, ["arguments"] = arguments.DeepClone(), ["isError"] = result.IsError == true };
        if (result.IsError != true)
        {
            var text = Assert.IsType<TextContentBlock>(result.Content[0]).Text;
            answer[asText ? "text" : "json"] = asText ? text : JsonNode.Parse(text);
        }

        return answer;
    }

    private static async Task<JsonObject> GetPromptAsync(McpClient client, string name, JsonObject arguments)
    {
        var result = await client.GetPromptAsync(name, Contract.Arguments(arguments));
        var message = Assert.Single(result.Messages);
        return new JsonObject
        {
            ["prompt"] = name,
            ["arguments"] = arguments.DeepClone(),
            ["role"] = message.Role == Role.User ? "user" : "assistant",
            ["text"] = Assert.IsType<TextContentBlock>(message.Content).Text,
        };
    }

    private static IEnumerable<string> Names(JsonNode? names) => names?.AsArray().Select(Contract.Text) ?? [];

    private static (string Title, string? Description, string Body, string? Notes, string Tags, string? Collection, bool IsFavorite, int? Rating, DateTimeOffset? CreatedAt, DateTimeOffset? UpdatedAt) Prompt(string markdown)
    {
        var prompt = MarkdownPromptFormat.Read(markdown, "untitled");
        return (prompt.Title, prompt.Description, prompt.Body, prompt.Notes, string.Join(", ", prompt.Tags), prompt.Collection, prompt.IsFavorite, prompt.Rating, prompt.CreatedAt, prompt.UpdatedAt);
    }
}
