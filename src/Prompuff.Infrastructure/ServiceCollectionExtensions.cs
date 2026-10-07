using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Infrastructure.ImportExport;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Repositories;
using Prompuff.Infrastructure.Storage;
using Prompuff.Infrastructure.Updates;

namespace Prompuff.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the application services and their SQLite, file and update implementations.</summary>
    public static IServiceCollection AddPrompuffCore(this IServiceCollection services, IAppDataPathProvider paths)
    {
        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new SqliteDatabase(
            paths.GetDatabasePath(),
            paths.GetBackupDirectory(),
            provider.GetRequiredService<ILogger<SqliteDatabase>>()));
        services.AddSingleton<LibraryBackups>();

        services.AddSingleton<IPromptRepository, SqlitePromptRepository>();
        services.AddSingleton<ICollectionRepository, SqliteCollectionRepository>();
        services.AddSingleton<ITagRepository, SqliteTagRepository>();
        services.AddSingleton<IPromptSearch, SqlitePromptSearch>();
        services.AddSingleton<SqliteRenderValues>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IPromptTransferService, MarkdownTransferService>();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();

        services.AddSingleton<IPromptTemplateService, PromptTemplateService>();
        services.AddSingleton<PromptService>();
        services.AddSingleton<CollectionService>();
        return services;
    }
}
