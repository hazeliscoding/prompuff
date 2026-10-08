using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;

namespace Prompuff.App.Tests;

public class UpdateTests
{
    [AvaloniaFact]
    public async Task The_startup_check_runs_even_right_after_an_earlier_check()
    {
        var updates = new FakeUpdateService(new AvailableUpdate("0.1.1", null));
        await using var app = await AppHarness.StartAsync(importSamples: false, updates: updates);

        // Settings as 0.1.0 left them after checking a moment ago.
        File.WriteAllText(
            app.Get<IAppDataPathProvider>().GetSettingsPath(),
            $$"""{ "checkForUpdatesAutomatically": true, "lastUpdateCheck": "{{DateTimeOffset.UtcNow:O}}" }""");

        await app.ViewModel.Settings.CheckOnStartupAsync();

        Assert.Equal(1, updates.Checks);
        Assert.True(app.ViewModel.Settings.CanInstallUpdate);
    }

    [AvaloniaFact]
    public async Task A_found_update_stays_in_the_sidebar_and_opens_Settings()
    {
        var updates = new FakeUpdateService(new AvailableUpdate("0.1.1", null));
        await using var app = await AppHarness.StartAsync(updates: updates);
        var store = app.Get<ISettingsStore>();
        store.Save(store.Load() with { CheckForUpdatesAutomatically = true });

        await app.ViewModel.Settings.CheckOnStartupAsync();
        await app.SettleAsync(3000); // past the toast
        Assert.Equal("Update 0.1.1 is ready", app.ViewModel.Settings.AvailableUpdateLabel);
        app.Screenshot("sidebar-update-available");

        await app.ViewModel.OpenUpdatesCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        Assert.True(settings.IsUpdates);
        app.Screenshot("settings-updates-available");
    }

    [AvaloniaFact]
    public async Task Choosing_Beta_checks_the_beta_channel_from_then_on()
    {
        var updates = new FakeUpdateService(new AvailableUpdate("0.2.0-beta.1", null));
        await using var app = await AppHarness.StartAsync(importSamples: false, updates: updates);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.Updates);
        await app.SettleAsync();
        Assert.True(settings.IsStableChannel);

        settings.IsBetaChannel = true;
        Assert.Equal(UpdateChannel.Beta, app.Get<ISettingsStore>().Load().UpdateChannel);
        Assert.Contains("Beta gets new versions first", settings.ChannelNote);

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(UpdateChannel.Beta, updates.LastChannel);
        Assert.True(settings.CanInstallUpdate);
        app.Screenshot("settings-updates-beta");

        // Switching back drops the beta that was found, and checks stable again.
        settings.IsStableChannel = true;
        Assert.False(settings.CanInstallUpdate);
        await settings.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(UpdateChannel.Stable, updates.LastChannel);
    }
}

/// <summary>An installed copy that finds the given update, without going online.</summary>
internal sealed class FakeUpdateService(AvailableUpdate? update) : IUpdateService
{
    public int Checks { get; private set; }
    public UpdateChannel? LastChannel { get; private set; }

    public bool IsSupported => true;

    public string CurrentVersion => "0.1.0";

    public Task<AvailableUpdate?> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        Checks++;
        LastChannel = channel;
        return Task.FromResult(update);
    }

    public Task DownloadUpdatesAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void ApplyUpdatesAndRestart(AvailableUpdate update)
    {
    }
}
