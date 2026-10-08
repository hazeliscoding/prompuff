using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Prompuff.Cli.Tests;

/// <summary>The real MCP server and the SDK's client, talking over in-memory pipes instead of stdio.</summary>
internal sealed class McpSession : IAsyncDisposable
{
    private readonly CliLibrary _library;
    private readonly McpServer _server;
    private readonly Task _running;

    private McpSession(CliLibrary library, McpServer server, Task running, McpClient client)
    {
        _library = library;
        _server = server;
        _running = running;
        Client = client;
    }

    public McpClient Client { get; }

    public static async Task<McpSession> StartAsync(CliHarness harness)
    {
        var library = await CliLibrary.OpenAsync(harness.Paths);
        Pipe toServer = new(), toClient = new();
        var options = McpServerHost.CreateOptions(library);
        var server = McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()), options);
        var running = server.RunAsync();
        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));
        return new McpSession(library, server, running, client);
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await _server.DisposeAsync();
        await _library.DisposeAsync();
    }
}

public class McpTests
{
    private static string Text(CallToolResult result, int block = 0) => Assert.IsType<TextContentBlock>(result.Content[block]).Text;

    [Fact]
    public async Task Tools_search_get_and_render_the_library()
    {
        await using var cli = await CliHarness.CreateAsync(allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        Assert.Equal(["search_prompts", "get_prompt", "render_prompt"], (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name));
        Assert.Contains("prompt library", mcp.Client.ServerInstructions);

        var search = await mcp.Client.CallToolAsync("search_prompts", new Dictionary<string, object?> { ["query"] = "angular" });
        Assert.NotEqual(true, search.IsError);
        Assert.Contains("\"title\": \"Angular Upgrade Planner\"", Text(search));

        var get = await mcp.Client.CallToolAsync("get_prompt", new Dictionary<string, object?> { ["prompt"] = "Angular Upgrade Planner" });
        Assert.Contains("\"variables\": [", Text(get));
        Assert.Contains("\"repo_name\"", Text(get));

        var render = await mcp.Client.CallToolAsync("render_prompt", new Dictionary<string, object?>
        {
            ["prompt"] = "Angular Upgrade Planner",
            ["variables"] = new Dictionary<string, string> { ["repo_name"] = "acme", ["target_version"] = "22" },
        });
        Assert.StartsWith("You are a senior Angular engineer planning an upgrade of acme to Angular 22.", Text(render));
        Assert.Equal("Not filled, so left as {{tokens}}: package_manager, tone.", Text(render, 1));

        var missing = await mcp.Client.CallToolAsync("get_prompt", new Dictionary<string, object?> { ["prompt"] = "Nope" });
        Assert.True(missing.IsError);
        Assert.Contains("search_prompts", Text(missing));
    }

    [Fact]
    public async Task Favorites_are_prompts_with_their_variables_as_arguments()
    {
        await using var cli = await CliHarness.CreateAsync(allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var prompts = await mcp.Client.ListPromptsAsync();
        Assert.Equal(["angular-upgrade-planner", "design-system-generator", "ui-mockup-generator"], prompts.Select(prompt => prompt.Name));
        var planner = prompts.First();
        Assert.Equal(["repo_name", "target_version", "package_manager", "tone"], planner.ProtocolPrompt.Arguments!.Select(argument => argument.Name));

        var result = await mcp.Client.GetPromptAsync("angular-upgrade-planner", new Dictionary<string, object?> { ["repo_name"] = "acme" });
        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        Assert.StartsWith("You are a senior Angular engineer planning an upgrade of acme to Angular {{target_version}}.", Assert.IsType<TextContentBlock>(message.Content).Text);
    }

    [Fact]
    public async Task Every_prompt_is_a_resource_in_Markdown()
    {
        await using var cli = await CliHarness.CreateAsync(allowMcp: true);
        await using var mcp = await McpSession.StartAsync(cli);

        var resources = await mcp.Client.ListResourcesAsync();
        Assert.Equal(6, resources.Count);
        var readme = resources.Single(resource => resource.Name == "README Cleanup");
        Assert.StartsWith("prompuff://prompts/", readme.Uri);

        var read = await mcp.Client.ReadResourceAsync(readme.Uri);
        var text = Assert.IsType<TextResourceContents>(Assert.Single(read.Contents)).Text;
        Assert.StartsWith("---\ntitle: README Cleanup\n", text);

        await Assert.ThrowsAsync<McpProtocolException>(() => mcp.Client.ReadResourceAsync("prompuff://prompts/" + Guid.NewGuid()).AsTask());
    }

    [Fact]
    public async Task Nothing_is_served_until_Settings_allows_it()
    {
        await using var cli = await CliHarness.CreateAsync(allowMcp: false);
        await using var mcp = await McpSession.StartAsync(cli);

        Assert.Equal(McpServerHost.TurnedOff, mcp.Client.ServerInstructions);
        Assert.Empty(await mcp.Client.ListPromptsAsync());
        Assert.Empty(await mcp.Client.ListResourcesAsync());
        var refused = await mcp.Client.CallToolAsync("search_prompts", new Dictionary<string, object?> { ["query"] = "angular" });
        Assert.True(refused.IsError);
        Assert.Equal(McpServerHost.TurnedOff, Text(refused));

        // Turning it on works without restarting the client.
        await using (var library = await CliLibrary.OpenAsync(cli.Paths))
        {
            library.Settings.Save(library.Settings.Load() with { AllowMcp = true });
        }

        var allowed = await mcp.Client.CallToolAsync("search_prompts", new Dictionary<string, object?> { ["query"] = "angular" });
        Assert.NotEqual(true, allowed.IsError);
        Assert.Equal(3, (await mcp.Client.ListPromptsAsync()).Count);
    }
}
