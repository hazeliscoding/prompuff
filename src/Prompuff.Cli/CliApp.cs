using System.Reflection;
using System.Text;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.Cli;

/// <summary>The words after the command: options and the rest, in order.</summary>
internal sealed class CommandLine
{
    private static readonly HashSet<string> Flags = ["--json", "--favorites"];
    private static readonly Dictionary<string, string> Valued = new()
    {
        ["--var"] = "--var",
        ["-v"] = "--var",
        ["--tag"] = "--tag",
        ["-t"] = "--tag",
        ["--title"] = "--title",
        ["--collection"] = "--collection",
        ["--limit"] = "--limit",
    };

    private readonly HashSet<string> _flags = [];
    private readonly Dictionary<string, List<string>> _values = [];

    public List<string> Words { get; } = [];

    public bool Has(string flag) => _flags.Contains(flag);

    public IReadOnlyList<string> All(string option) => _values.TryGetValue(option, out var values) ? values : [];

    public string? One(string option) => All(option) is { Count: > 0 } values ? values[^1] : null;

    /// <summary>Reads options anywhere among the words. Returns an error message for anything it can't read.</summary>
    public static (CommandLine Line, string? Error) Parse(IEnumerable<string> args)
    {
        var line = new CommandLine();
        var queue = new Queue<string>(args);
        while (queue.Count > 0)
        {
            var arg = queue.Dequeue();
            if (arg == "--")
            {
                line.Words.AddRange(queue);
                break;
            }

            var (name, inline) = arg.StartsWith("--", StringComparison.Ordinal) && arg.IndexOf('=') is > 2 and var equals
                ? (arg[..equals], arg[(equals + 1)..])
                : (arg, null);
            if (Flags.Contains(name) && inline is null)
            {
                line._flags.Add(name);
            }
            else if (Valued.TryGetValue(name, out var option))
            {
                var value = inline ?? (queue.Count > 0 ? queue.Dequeue() : null);
                if (value is null)
                {
                    return (line, $"{name} needs a value.");
                }

                if (!line._values.TryGetValue(option, out var values))
                {
                    line._values[option] = values = [];
                }

                values.Add(value);
            }
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                return (line, $"Prompuff doesn't know the option {arg}.");
            }
            else
            {
                line.Words.Add(arg);
            }
        }

        return (line, null);
    }
}

/// <summary>
/// <c>prompuff</c>: the library from a terminal or a script. Exit codes are 0 for success, 1 for errors and 2 for a
/// prompt that isn't there or a name that fits several.
/// </summary>
internal static class CliApp
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int NotFound = 2;
    private const long MaxQuickSaveBytes = 5 * 1024 * 1024;

    public static string Version { get; } =
        typeof(CliApp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public const string Usage = """
        prompuff: your prompt library, from the terminal.

        Usage:
          prompuff search <words>              Find prompts by title, text, notes or #tag
          prompuff list [--favorites] [--collection NAME] [--tag TAG]
                                               List prompts by title
          prompuff get <prompt>                Print a prompt
          prompuff render <prompt> [--var name=value ...]
                                               Fill in its {{variables}} and print the result
          prompuff quick-save [--title TITLE] [--tag TAG ...] [--collection NAME]
                                               Save text from stdin as a new prompt
          prompuff mcp                         Serve the library to AI tools over MCP (stdio)

        Options:
          --json       Print JSON instead of text
          --limit N    Show at most N prompts (search shows 20 unless you say)
          --version    Print the version

        A <prompt> is its title, a few words that only it matches, or its ID.
        Prompuff reads the same library as the app, or the folder in PROMPUFF_DATA_DIR.

        """;

    public static async Task<int> RunAsync(
        string[] args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        IAppDataPathProvider? paths = null,
        CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            output.Write(Usage.ReplaceLineEndings("\n"));
            return args.Length == 0 ? Failed : Ok;
        }

        if (args[0] is "--version" or "version")
        {
            output.WriteLine($"prompuff {Version}");
            return Ok;
        }

        var command = args[0];
        if (command is not ("search" or "list" or "get" or "render" or "quick-save" or "mcp"))
        {
            error.WriteLine($"Prompuff doesn't know the command “{command}”. Run prompuff help to see them all.");
            return Failed;
        }

        var (line, problem) = CommandLine.Parse(args[1..]);
        if (problem is not null)
        {
            error.WriteLine(problem);
            return Failed;
        }

        try
        {
            await using var library = await CliLibrary.OpenAsync(paths ?? new AppDataPathProvider(), cancellationToken);
            return command switch
            {
                "search" => await SearchAsync(library, line, output, error, cancellationToken),
                "list" => await ListAsync(library, line, output, error, cancellationToken),
                "get" => await GetAsync(library, line, output, error, cancellationToken),
                "render" => await RenderAsync(library, line, output, error, cancellationToken),
                "quick-save" => await QuickSaveAsync(library, line, input, output, error, cancellationToken),
                _ => await McpServerHost.RunAsync(library, cancellationToken),
            };
        }
        catch (LibraryException exception)
        {
            error.WriteLine(exception.Message);
            return Failed;
        }
    }

    private static async Task<int> SearchAsync(CliLibrary library, CommandLine line, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var text = string.Join(' ', line.Words);
        if (text.Trim().Length == 0)
        {
            error.WriteLine("Search for what? For example: prompuff search angular upgrade");
            return Failed;
        }

        if (!TryLimit(line, 20, error, out var limit))
        {
            return Failed;
        }

        var results = (await library.Search.SearchAsync(new PromptQuery { Text = text }, cancellationToken)).Take(limit).ToList();
        if (results.Count == 0 && !line.Has("--json"))
        {
            error.WriteLine($"Nothing matches “{text.Trim()}”.");
            return Ok;
        }

        await WriteListAsync(library, results, line.Has("--json"), output, cancellationToken);
        return Ok;
    }

    private static async Task<int> ListAsync(CliLibrary library, CommandLine line, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (!TryLimit(line, int.MaxValue, error, out var limit))
        {
            return Failed;
        }

        var query = new PromptQuery { Sort = PromptSort.Title };
        if (line.One("--collection") is { } name)
        {
            var collection = (await library.Collections.ListAsync(cancellationToken))
                .FirstOrDefault(candidate => string.Equals(candidate.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (collection is null)
            {
                error.WriteLine($"There's no collection called “{name.Trim()}”.");
                return NotFound;
            }

            query = query with { Filter = PromptFilterKind.Collection, CollectionId = collection.Id };
        }
        else if (line.One("--tag") is { } tag)
        {
            query = query with { Filter = PromptFilterKind.Tag, Tag = tag };
        }
        else if (line.Has("--favorites"))
        {
            query = query with { Filter = PromptFilterKind.Favorites };
        }

        var results = (await library.Search.SearchAsync(query, cancellationToken)).Take(limit).ToList();
        await WriteListAsync(library, results, line.Has("--json"), output, cancellationToken);
        return Ok;
    }

    private static async Task<int> GetAsync(CliLibrary library, CommandLine line, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (await FindAsync(library, line, error, cancellationToken) is not { } prompt)
        {
            return NotFound;
        }

        if (line.Has("--json"))
        {
            var collections = await library.CollectionNamesAsync(cancellationToken);
            var collection = prompt.CollectionId is { } id ? collections.GetValueOrDefault(id) : null;
            output.WriteLine(CliJson.Serialize(PromptDetails.From(prompt, library.Templates.ExtractVariables(prompt.Body), collection)));
        }
        else
        {
            WriteText(output, prompt.Body);
        }

        return Ok;
    }

    private static async Task<int> RenderAsync(CliLibrary library, CommandLine line, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in line.All("--var"))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                error.WriteLine($"--var needs name=value, as in --var repo_name=acme, not “{pair}”.");
                return Failed;
            }

            values[pair[..equals].Trim()] = pair[(equals + 1)..];
        }

        if (await FindAsync(library, line, error, cancellationToken) is not { } prompt)
        {
            return NotFound;
        }

        var (rendered, missing) = library.Render(prompt, values);
        var unknown = values.Keys.Except(library.Templates.ExtractVariables(prompt.Body), StringComparer.Ordinal).ToList();
        if (line.Has("--json"))
        {
            output.WriteLine(CliJson.Serialize(new RenderedPrompt(prompt.Id.ToString("D"), prompt.Title, rendered, missing)));
        }
        else
        {
            WriteText(output, rendered);
        }

        if (missing.Count > 0)
        {
            error.WriteLine($"Not filled, so left as {{{{tokens}}}}: {string.Join(", ", missing)}. Pass --var name=value to fill one.");
        }

        if (unknown.Count > 0)
        {
            error.WriteLine($"“{prompt.Title}” has no variable called {string.Join(", ", unknown)}.");
        }

        return Ok;
    }

    private static async Task<int> QuickSaveAsync(CliLibrary library, CommandLine line, TextReader input, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var text = await ReadLimitedAsync(input, cancellationToken);
        if (text is null)
        {
            error.WriteLine("That's more than 5 MB, which is too large to be a prompt.");
            return Failed;
        }

        if (text.Trim().Length == 0)
        {
            error.WriteLine("Nothing to save. Pipe a prompt in, for example: pbpaste | prompuff quick-save");
            return Failed;
        }

        Guid? collectionId = null;
        if (line.One("--collection") is { } collectionName && collectionName.Trim().Length > 0)
        {
            collectionId = (await library.Collections.GetOrCreateAsync(collectionName, cancellationToken)).Id;
        }

        var title = line.One("--title") is { Length: > 0 } given ? given : PromptContent.SuggestTitle(text);
        var prompt = await library.Prompts.CreateAsync(
            new PromptContent(title, null, text.TrimEnd(), null),
            new PromptMetadata(false, null, collectionId, TagName.NormalizeAll(line.All("--tag"))),
            "Saved from the command line",
            cancellationToken: cancellationToken);

        if (line.Has("--json"))
        {
            output.WriteLine(CliJson.Serialize(new SavedPrompt(prompt.Id.ToString("D"), prompt.Title)));
        }
        else
        {
            output.WriteLine($"Saved “{prompt.Title}”.");
        }

        return Ok;
    }

    /// <summary>Finds the prompt the words name, or explains on stderr why there isn't one.</summary>
    private static async Task<Domain.Entities.Prompt?> FindAsync(CliLibrary library, CommandLine line, TextWriter error, CancellationToken cancellationToken)
    {
        var name = string.Join(' ', line.Words).Trim();
        if (name.Length == 0)
        {
            error.WriteLine("Which prompt? Give its title or its ID, for example: prompuff get \"Angular Upgrade Planner\"");
            return null;
        }

        var lookup = await library.FindAsync(name, cancellationToken);
        if (lookup.Prompt is { } prompt)
        {
            return prompt;
        }

        if (lookup.IsAmbiguous)
        {
            error.WriteLine($"Several prompts are called “{name}”. Use one of their IDs:");
        }
        else
        {
            error.WriteLine($"No prompt is called “{name}”." + (lookup.Candidates.Count > 0 ? " Did you mean one of these?" : string.Empty));
        }

        foreach (var candidate in lookup.Candidates)
        {
            error.WriteLine($"  {candidate.Title}  ({candidate.Id:D})");
        }

        return null;
    }

    private static async Task WriteListAsync(CliLibrary library, IReadOnlyList<PromptSummary> results, bool json, TextWriter output, CancellationToken cancellationToken)
    {
        var collections = await library.CollectionNamesAsync(cancellationToken);
        if (json)
        {
            output.WriteLine(CliJson.Serialize(results.Select(summary => PromptListItem.From(summary, collections)).ToList()));
            return;
        }

        foreach (var summary in results)
        {
            var details = new List<string>();
            if (summary.CollectionId is { } id && collections.TryGetValue(id, out var collection))
            {
                details.Add(collection);
            }

            if (summary.Tags.Count > 0)
            {
                details.Add(string.Join(' ', summary.Tags.Select(tag => "#" + tag)));
            }

            output.WriteLine(details.Count == 0 ? summary.Title : $"{summary.Title}  · {string.Join(" · ", details)}");
        }
    }

    private static bool TryLimit(CommandLine line, int fallback, TextWriter error, out int limit)
    {
        limit = fallback;
        if (line.One("--limit") is not { } text)
        {
            return true;
        }

        if (int.TryParse(text, out limit) && limit > 0)
        {
            return true;
        }

        error.WriteLine($"--limit needs a positive number, not “{text}”.");
        return false;
    }

    private static void WriteText(TextWriter output, string text)
    {
        output.Write(text);
        if (!text.EndsWith('\n'))
        {
            output.WriteLine();
        }
    }

    /// <summary>Reads stdin, stopping past 5 MB. Returns null when the input is larger than that.</summary>
    private static async Task<string?> ReadLimitedAsync(TextReader input, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            text.Append(buffer, 0, read);
            if (text.Length > MaxQuickSaveBytes)
            {
                return null;
            }
        }

        return text.ToString();
    }
}
