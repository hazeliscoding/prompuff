using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;
using Prompuff.Infrastructure;
using Prompuff.Infrastructure.Logging;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.Cli;

/// <summary>A prompt found by title or ID, or the reason it wasn't.</summary>
/// <param name="Candidates">When nothing was found, close matches; when the name was ambiguous, every match.</param>
internal sealed record PromptLookup(Prompt? Prompt, bool IsAmbiguous, IReadOnlyList<PromptSummary> Candidates);

/// <summary>
/// The library as the app sees it: the same data folder (or <c>PROMPUFF_DATA_DIR</c>), the same migrations with a
/// backup first, and the same services. Logs go to <c>prompuff-cli-yyyyMMdd.log</c> and never to the terminal,
/// because stdout belongs to the command's output and, for <c>prompuff mcp</c>, to the protocol.
/// </summary>
internal sealed class CliLibrary : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private CliLibrary(ServiceProvider services, IAppDataPathProvider paths)
    {
        _services = services;
        Paths = paths;
    }

    public IAppDataPathProvider Paths { get; }
    public PromptService Prompts => _services.GetRequiredService<PromptService>();
    public IPromptSearch Search => _services.GetRequiredService<IPromptSearch>();
    public CollectionService Collections => _services.GetRequiredService<CollectionService>();
    public IPromptTemplateService Templates => _services.GetRequiredService<IPromptTemplateService>();
    public IPromptTransferService Transfer => _services.GetRequiredService<IPromptTransferService>();
    public ISettingsStore Settings => _services.GetRequiredService<ISettingsStore>();
    public ILoggerFactory LoggerFactory => _services.GetRequiredService<ILoggerFactory>();

    public static async Task<CliLibrary> OpenAsync(IAppDataPathProvider paths, CancellationToken cancellationToken = default)
    {
        if (paths is AppDataPathProvider appPaths)
        {
            try
            {
                appPaths.EnsureDirectories();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Opening the library reports a folder that can't be used.
            }
        }

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);

            // The MCP SDK can log request details; only its warnings are kept, so prompt text never reaches a log.
            logging.AddFilter("ModelContextProtocol", LogLevel.Warning);
            logging.AddProvider(new FileLoggerProvider(paths.GetLogsDirectory(), filePrefix: "prompuff-cli"));
        });
        services.AddPrompuffCore(paths);
        var provider = services.BuildServiceProvider();
        try
        {
            await provider.GetRequiredService<SqliteDatabase>().InitializeAsync(cancellationToken);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }

        return new CliLibrary(provider, paths);
    }

    /// <summary>
    /// Finds a prompt by ID, by its exact title (ignoring case), or by a word or two that match only one prompt. A
    /// title that several prompts share is ambiguous. Prompts in Recently deleted aren't found.
    /// </summary>
    public async Task<PromptLookup> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        name = name.Trim();
        if (Guid.TryParse(name, out var id))
        {
            var byId = await Prompts.GetAsync(id, cancellationToken);
            return new PromptLookup(byId is { DeletedAt: null } ? byId : null, false, []);
        }

        var matches = await Search.SearchAsync(new PromptQuery { Text = name }, cancellationToken);
        var exact = matches.Where(match => string.Equals(match.Title, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 1)
        {
            return new PromptLookup(null, true, exact);
        }

        var only = exact.Count == 1 ? exact[0] : matches.Count == 1 ? matches[0] : null;
        if (only is not null)
        {
            return new PromptLookup(await Prompts.GetAsync(only.Id, cancellationToken), false, []);
        }

        return new PromptLookup(null, false, matches.Take(5).ToList());
    }

    /// <summary>The prompt with the values filled in, and the variables still waiting for one.</summary>
    public (string Rendered, IReadOnlyList<string> Missing) Render(Prompt prompt, IReadOnlyDictionary<string, string> values)
    {
        var missing = Templates.ExtractVariables(prompt.Body).Where(name => string.IsNullOrEmpty(values.GetValueOrDefault(name))).ToList();
        return (Templates.Render(prompt.Body, values), missing);
    }

    /// <summary>Collection names by ID, for showing where prompts live.</summary>
    public async Task<Dictionary<Guid, string>> CollectionNamesAsync(CancellationToken cancellationToken = default) =>
        (await Collections.ListAsync(cancellationToken)).ToDictionary(collection => collection.Id, collection => collection.Name);

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
