using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.Tests;

public class LibraryWatcherTests
{
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
