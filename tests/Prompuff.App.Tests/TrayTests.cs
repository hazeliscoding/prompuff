using Avalonia.Headless.XUnit;
using Prompuff.App.Platform;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Tests;

public class TrayTests
{
    [AvaloniaFact]
    public async Task Closing_the_window_keeps_Prompuff_in_the_tray_when_asked()
    {
        await using var app = await AppHarness.StartAsync();
        app.ViewModel.Settings.KeepRunningInTray = true;
        Assert.True(app.Get<ISettingsStore>().Load().KeepRunningInTray);

        app.Window.Close();
        await app.SettleAsync();
        Assert.True(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);

        WindowActivation.BringForward(app.Window);
        await app.SettleAsync();
        Assert.True(app.Window.IsVisible);
        Assert.False(app.Window.IsInTray);
        await app.ViewModel.QuickSaveFromOutsideAsync();
        Assert.True(app.ViewModel.QuickSave.IsOpen);

        app.Window.Quit();
        await app.SettleAsync();
        Assert.False(app.Window.IsVisible);
        Assert.False(app.Window.IsInTray);
    }

    [AvaloniaFact]
    public async Task Without_the_setting_closing_quits()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);

        app.Window.Close();
        await app.SettleAsync();

        Assert.False(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public void The_menu_bar_icon_is_a_black_template()
    {
        using var icon = TrayMenu.RenderTemplateIcon(44);
        Assert.Equal(44, icon.PixelSize.Width);
        if (Environment.GetEnvironmentVariable("PROMPUFF_SCREENSHOTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            icon.Save(Path.Combine(output, "tray-template-icon.png"));
        }
    }
}
