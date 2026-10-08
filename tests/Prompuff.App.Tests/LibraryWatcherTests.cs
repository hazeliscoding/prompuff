using Avalonia.Headless.XUnit;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Prompuff.App.ViewModels;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Persistence;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.App.Tests;

public class LibraryWatcherTests
{
    [Fact]
    public async Task Quitting_closes_the_watchers_connection()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-ui-tests", Guid.NewGuid().ToString("N"));
        var paths = new AppDataPathProvider(new PlatformEnvironment(
            PlatformEnvironment.Current.Platform,
            PlatformEnvironment.Current.HomeDirectory,
            name => name == AppDataPathProvider.OverrideVariable ? folder : null));
        var services = App.ConfigureServices(paths);
        var database = services.GetRequiredService<SqliteDatabase>();
        await database.InitializeAsync();
        await services.GetRequiredService<LibraryChangeMonitor>().CheckAsync();

        // The app disposes its services synchronously when the window closes.
        services.Dispose();

        database.ClearPool();
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // The log file stays open until the process ends; temp folders are cleaned up by the OS eventually.
        }
    }

    [AvaloniaFact]
    public async Task A_prompt_saved_outside_the_app_shows_up_without_a_restart()
    {
        await using var app = await AppHarness.StartAsync();
        var watcher = app.Get<LibraryWatcher>();
        watcher.Stop();
        var notifications = 0;
        app.Get<LibraryNotifier>().Changed += (_, _) => notifications++;

        await watcher.CheckAsync();
        Assert.Equal(0, notifications);

        // What prompuff quick-save does: a write on another connection, which the app hears nothing about.
        await app.Get<PromptService>().CreateAsync(new PromptContent("From the terminal", null, "Saved by the CLI.", null));
        await watcher.CheckAsync();
        await app.SettleAsync();
        Assert.Equal(1, notifications);
        Assert.Equal(7, app.ViewModel.Sidebar.All.Count);
        Assert.Contains(app.ViewModel.Library.Items, card => card.Title == "From the terminal");

        // A change the app announces itself is already showing, so it doesn't refresh twice.
        await app.Get<PromptService>().CreateAsync(new PromptContent("From the app", null, "Saved in Prompuff.", null));
        app.Get<LibraryNotifier>().Notify();
        await app.SettleAsync();
        await watcher.CheckAsync();
        Assert.Equal(2, notifications);
    }
}
