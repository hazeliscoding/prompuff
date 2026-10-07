using Avalonia.Headless.XUnit;
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
}

/// <summary>An installed copy that finds the given update, without going online.</summary>
internal sealed class FakeUpdateService(AvailableUpdate? update) : IUpdateService
{
    public int Checks { get; private set; }

    public bool IsSupported => true;

    public string CurrentVersion => "0.1.0";

    public Task<AvailableUpdate?> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        Checks++;
        return Task.FromResult(update);
    }

    public Task DownloadUpdatesAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void ApplyUpdatesAndRestart(AvailableUpdate update)
    {
    }
}
