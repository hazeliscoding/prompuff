using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Prompuff.Application.DTOs;
using LibraryPrompt = Prompuff.Domain.Entities.Prompt;
using McpPrompt = ModelContextProtocol.Protocol.Prompt;

namespace Prompuff.Cli;

/// <summary>
/// <c>prompuff mcp</c>: the library for AI tools over stdio, read-only. Three tools (search, get, render), every prompt
/// as a <c>prompuff://prompts/{id}</c> resource, and favorites as MCP prompts, which clients show as slash commands
/// with the prompt's variables as arguments. Nothing is served until Settings › Integrations allows it, checked on
/// every request so the switch works without restarting the client. Logs name tools and counts, never prompt text.
/// </summary>
internal static class McpServerHost
{
    public const string TurnedOff =
        "Prompuff's MCP access is turned off, so it can't share prompts. Ask the user to turn on " +
        "\"Let AI tools read my library\" in Prompuff › Settings › Integrations.";

    private const string ResourcePrefix = "prompuff://prompts/";
    private const int PageSize = 100;

    private static readonly JsonElement SearchSchema = Schema("""
        {
          "type": "object",
          "properties": {
            "query": { "type": "string", "description": "Words to find in titles, prompt text, notes and tags, or #tag for a tag." },
            "limit": { "type": "integer", "minimum": 1, "maximum": 50, "description": "How many prompts to return. Defaults to 10." }
          },
          "required": ["query"]
        }
        """);

    private static readonly JsonElement GetSchema = Schema("""
        {
          "type": "object",
          "properties": {
            "prompt": { "type": "string", "description": "The prompt's title or ID, as search_prompts returns them." }
          },
          "required": ["prompt"]
        }
        """);

    private static readonly JsonElement RenderSchema = Schema("""
        {
          "type": "object",
          "properties": {
            "prompt": { "type": "string", "description": "The prompt's title or ID, as search_prompts returns them." },
            "variables": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Values for the prompt's {{variables}}, by name. Any left out stay as {{tokens}}."
            }
          },
          "required": ["prompt"]
        }
        """);

    public static async Task<int> RunAsync(CliLibrary library, CancellationToken cancellationToken)
    {
        var options = CreateOptions(library);
        await using var transport = new StdioServerTransport(options, library.LoggerFactory);
        await using var server = McpServer.Create(transport, options, library.LoggerFactory);
        library.LoggerFactory.CreateLogger("Mcp").LogInformation("MCP server started; access is {State}", IsAllowed(library) ? "on" : "off");
        await server.RunAsync(cancellationToken);
        return CliApp.Ok;
    }

    public static McpServerOptions CreateOptions(CliLibrary library)
    {
        var logger = library.LoggerFactory.CreateLogger("Mcp");
        return new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "prompuff", Title = "Prompuff", Version = CliApp.Version },
            ServerInstructions = IsAllowed(library)
                ? "Prompuff is the user's local prompt library. Use search_prompts to find a prompt, get_prompt to read it, " +
                  "and render_prompt to fill in its {{variables}}. Prompuff never runs a model; it only returns text."
                : TurnedOff,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability(),
                Prompts = new PromptsCapability(),
                Resources = new ResourcesCapability(),
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = Tools() }),
                CallToolHandler = async (request, cancellationToken) =>
                {
                    var name = request.Params?.Name ?? string.Empty;
                    if (!IsAllowed(library))
                    {
                        logger.LogInformation("Refused tool {Tool}: MCP access is off", name);
                        return Error(TurnedOff);
                    }

                    var arguments = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
                    return name switch
                    {
                        "search_prompts" => await SearchAsync(library, arguments, logger, cancellationToken),
                        "get_prompt" => await GetAsync(library, arguments, logger, cancellationToken),
                        "render_prompt" => await RenderAsync(library, arguments, logger, cancellationToken),
                        _ => throw new McpProtocolException($"Prompuff has no tool called {name}.", McpErrorCode.InvalidParams),
                    };
                },
                ListResourcesHandler = async (request, cancellationToken) =>
                {
                    if (!IsAllowed(library))
                    {
                        return new ListResourcesResult { Resources = [] };
                    }

                    var start = int.TryParse(request.Params?.Cursor, out var offset) && offset > 0 ? offset : 0;
                    var prompts = await library.Search.SearchAsync(new PromptQuery { Sort = PromptSort.Title }, cancellationToken);
                    var page = prompts.Skip(start).Take(PageSize).Select(prompt => new Resource
                    {
                        Uri = ResourcePrefix + prompt.Id.ToString("D"),
                        Name = prompt.Title,
                        Title = prompt.Title,
                        Description = prompt.Description,
                        MimeType = "text/markdown",
                    }).ToList();
                    return new ListResourcesResult
                    {
                        Resources = page,
                        NextCursor = start + PageSize < prompts.Count ? (start + PageSize).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    };
                },
                ReadResourceHandler = async (request, cancellationToken) =>
                {
                    var uri = request.Params?.Uri ?? string.Empty;
                    if (!IsAllowed(library))
                    {
                        throw new McpProtocolException(TurnedOff, McpErrorCode.InvalidRequest);
                    }

                    if (!uri.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                        || !Guid.TryParse(uri[ResourcePrefix.Length..], out var id)
                        || await library.Prompts.GetAsync(id, cancellationToken) is not { DeletedAt: null })
                    {
                        throw new McpProtocolException($"There's no prompt at {uri}.", McpErrorCode.ResourceNotFound);
                    }

                    logger.LogInformation("Read resource for prompt {PromptId}", id);
                    return new ReadResourceResult
                    {
                        Contents = [new TextResourceContents { Uri = uri, MimeType = "text/markdown", Text = await library.Transfer.GetMarkdownAsync(id, cancellationToken) }],
                    };
                },
                ListPromptsHandler = async (_, cancellationToken) =>
                {
                    if (!IsAllowed(library))
                    {
                        return new ListPromptsResult { Prompts = [] };
                    }

                    var favorites = await FavoritesAsync(library, cancellationToken);
                    var prompts = new List<McpPrompt>();
                    foreach (var (name, summary) in favorites)
                    {
                        var prompt = await library.Prompts.GetAsync(summary.Id, cancellationToken);
                        if (prompt is null)
                        {
                            continue;
                        }

                        prompts.Add(new McpPrompt
                        {
                            Name = name,
                            Title = prompt.Title,
                            Description = prompt.Description,
                            Arguments = library.Templates.ExtractVariables(prompt.Body)
                                .Select(variable => new PromptArgument { Name = variable, Description = $"Value for {{{{{variable}}}}}", Required = false })
                                .ToList(),
                        });
                    }

                    return new ListPromptsResult { Prompts = prompts };
                },
                GetPromptHandler = async (request, cancellationToken) =>
                {
                    if (!IsAllowed(library))
                    {
                        throw new McpProtocolException(TurnedOff, McpErrorCode.InvalidRequest);
                    }

                    var name = request.Params?.Name ?? string.Empty;
                    var match = (await FavoritesAsync(library, cancellationToken)).FirstOrDefault(entry => entry.Name == name);
                    if (match.Summary is null || await library.Prompts.GetAsync(match.Summary.Id, cancellationToken) is not { } prompt)
                    {
                        throw new McpProtocolException($"Prompuff has no favorite prompt called {name}.", McpErrorCode.InvalidParams);
                    }

                    var values = (request.Params?.Arguments ?? new Dictionary<string, JsonElement>())
                        .ToDictionary(pair => pair.Key, pair => Text(pair.Value), StringComparer.Ordinal);
                    var (rendered, missing) = library.Render(prompt, values);
                    logger.LogInformation("Rendered prompt {PromptId} as an MCP prompt with {Missing} variables left", prompt.Id, missing.Count);
                    return new GetPromptResult
                    {
                        Description = prompt.Description,
                        Messages = [new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = rendered } }],
                    };
                },
            },
        };
    }

    /// <summary>The user's choice in Settings › Integrations, read fresh each time.</summary>
    public static bool IsAllowed(CliLibrary library) => library.Settings.Load().AllowMcp;

    private static List<Tool> Tools() =>
    [
        new()
        {
            Name = "search_prompts",
            Title = "Search prompts",
            Description = "Search the user's Prompuff prompt library. Returns matching prompts with their IDs, titles, descriptions, collections and tags, best match first.",
            InputSchema = SearchSchema,
            Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "get_prompt",
            Title = "Get a prompt",
            Description = "Read one prompt from the user's Prompuff library: its text, its {{variables}}, the notes on why it works, and its tags.",
            InputSchema = GetSchema,
            Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false },
        },
        new()
        {
            Name = "render_prompt",
            Title = "Render a prompt",
            Description = "Fill in a prompt's {{variables}} with the values given and return the finished text, ready to use. Variables without a value stay as {{tokens}} and are listed.",
            InputSchema = RenderSchema,
            Annotations = new ToolAnnotations { ReadOnlyHint = true, OpenWorldHint = false },
        },
    ];

    private static async ValueTask<CallToolResult> SearchAsync(CliLibrary library, IDictionary<string, JsonElement> arguments, ILogger logger, CancellationToken cancellationToken)
    {
        var query = arguments.TryGetValue("query", out var value) ? Text(value) : string.Empty;
        var limit = arguments.TryGetValue("limit", out var given) && given.ValueKind == JsonValueKind.Number && given.TryGetInt32(out var number)
            ? Math.Clamp(number, 1, 50)
            : 10;
        var results = (await library.Search.SearchAsync(new PromptQuery { Text = query }, cancellationToken)).Take(limit).ToList();
        var collections = await library.CollectionNamesAsync(cancellationToken);
        logger.LogInformation("search_prompts returned {Count} prompts", results.Count);
        return Result(CliJson.Serialize(results.Select(summary => PromptListItem.From(summary, collections)).ToList()));
    }

    private static async ValueTask<CallToolResult> GetAsync(CliLibrary library, IDictionary<string, JsonElement> arguments, ILogger logger, CancellationToken cancellationToken)
    {
        if (await FindAsync(library, arguments, cancellationToken) is not { } prompt)
        {
            return NotFound(arguments);
        }

        var collections = await library.CollectionNamesAsync(cancellationToken);
        var collection = prompt.CollectionId is { } id ? collections.GetValueOrDefault(id) : null;
        logger.LogInformation("get_prompt read prompt {PromptId}", prompt.Id);
        return Result(CliJson.Serialize(PromptDetails.From(prompt, library.Templates.ExtractVariables(prompt.Body), collection)));
    }

    private static async ValueTask<CallToolResult> RenderAsync(CliLibrary library, IDictionary<string, JsonElement> arguments, ILogger logger, CancellationToken cancellationToken)
    {
        if (await FindAsync(library, arguments, cancellationToken) is not { } prompt)
        {
            return NotFound(arguments);
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (arguments.TryGetValue("variables", out var given) && given.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in given.EnumerateObject())
            {
                values[property.Name] = Text(property.Value);
            }
        }

        var (rendered, missing) = library.Render(prompt, values);
        logger.LogInformation("render_prompt rendered prompt {PromptId} with {Missing} variables left", prompt.Id, missing.Count);
        var result = Result(rendered);
        if (missing.Count > 0)
        {
            result.Content.Add(new TextContentBlock { Text = $"Not filled, so left as {{{{tokens}}}}: {string.Join(", ", missing)}." });
        }

        return result;
    }

    private static async Task<LibraryPrompt?> FindAsync(CliLibrary library, IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken) =>
        arguments.TryGetValue("prompt", out var value) && Text(value) is { Length: > 0 } name
            ? (await library.FindAsync(name, cancellationToken)).Prompt
            : null;

    /// <summary>Favorites by their MCP prompt name: the title as a slug, numbered when two titles make the same one.</summary>
    private static async Task<List<(string Name, PromptSummary Summary)>> FavoritesAsync(CliLibrary library, CancellationToken cancellationToken)
    {
        var favorites = await library.Search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Favorites, Sort = PromptSort.Title }, cancellationToken);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var named = new List<(string, PromptSummary)>();
        foreach (var favorite in favorites)
        {
            var slug = Path.GetFileNameWithoutExtension(library.Transfer.SuggestFileName(favorite.Title));
            var name = slug;
            for (var n = 2; !taken.Add(name); n++)
            {
                name = $"{slug}-{n}";
            }

            named.Add((name, favorite));
        }

        return named;
    }

    private static CallToolResult NotFound(IDictionary<string, JsonElement> arguments) =>
        Error($"Prompuff has no prompt called “{(arguments.TryGetValue("prompt", out var value) ? Text(value) : string.Empty)}”. Use search_prompts to find it, and pass its title or ID.");

    private static CallToolResult Result(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static CallToolResult Error(string text) => new() { Content = [new TextContentBlock { Text = text }], IsError = true };

    private static string Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => value.GetRawText(),
    };

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
