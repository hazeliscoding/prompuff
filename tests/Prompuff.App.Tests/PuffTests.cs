using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prompuff.App.Controls;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Tests;

/// <summary>
/// Puff moves like a small companion: hello in empty states, a hop for confirmations, a squish and a hop on the
/// About page, and rest the rest of the time. Reduce motion holds it still, and so does hiding it.
/// </summary>
public class PuffTests
{
    [AvaloniaFact]
    public async Task Puff_floats_squishes_and_hops_on_the_About_page()
    {
        await using var app = await AppHarness.StartAsync();
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        await app.SettleAsync();
        var puff = Puffs(app).Single(candidate => candidate.CheerOnPress);
        Assert.DoesNotContain("bob", puff.Classes);
        settings.Section = SettingsSection.About;
        await app.SettleAsync();
        Assert.Contains("bob", puff.Classes);

        puff.Squish();
        var squish = await MotionAsync(puff);
        Assert.True(squish > 0.5, $"Puff squishes when the pointer comes by, but it only moved {squish:0.00}.");
        puff.Cheer();
        var hop = await MotionAsync(puff);
        Assert.True(hop > 0.5, $"Puff hops when it's clicked, but it only moved {hop:0.00}.");
        Assert.Equal((0d, 0d), (puff.Lift, puff.Squash));

        puff.Cheer();
        await RunAsync(160);
        app.Screenshot("about-puff-hop");
    }

    [AvaloniaFact]
    public async Task Empty_states_say_hello_and_toasts_cheer_unless_Puff_is_hidden()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);

        // Coming back to the empty library builds its empty state again, and Puff says hello as it appears. Measuring
        // from there, rather than after startup, keeps a slow machine from missing the hello.
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        await app.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        var empty = Puffs(app).Single(candidate => candidate.Fidgets && candidate.IsEffectivelyVisible);
        Assert.True(empty.GreetWhen);
        var hello = await MotionAsync(empty, 1600);
        Assert.True(hello > 0.3, $"Puff says hello when the library is empty, but it only moved {hello:0.00}.");
        app.Screenshot("library-empty-hello");

        // The fidget timer would cut in with a blink at a random moment, so it stops while the glance is measured.
        empty.Fidgets = false;
        empty.Glance();
        var glance = await MotionAsync(empty, 1400);
        Assert.True(glance > 0.5, $"Puff glances aside now and then, but its eyes only moved {glance:0.00}.");
        empty.Glance();
        await RunAsync(600);
        app.Screenshot("library-empty-glance");

        app.Get<ToastService>().Show("Saved");
        Dispatcher.UIThread.RunJobs();
        var toast = Puffs(app).Single(candidate => candidate.CheerFor is not null);
        var cheer = await MotionAsync(toast);
        Assert.True(cheer > 0.5, $"Puff hops for a confirmation, but it only moved {cheer:0.00}.");
        toast.Cheer();
        await RunAsync(160);
        app.Screenshot("toast-puff-hop");

        // Hiding Puff stops the hop it was in, and the next toast doesn't start one.
        app.ViewModel.Appearance.ShowMascot = false;
        app.Get<ToastService>().Show("Saved again");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, await MotionAsync(toast));

        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        await app.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var hidden = Puffs(app).Single(candidate => candidate.Fidgets && candidate.GreetWhen);
        Assert.False(hidden.IsEffectivelyVisible);
        Assert.Equal(0, await MotionAsync(hidden));
    }

    [AvaloniaFact]
    public async Task Reduce_motion_follows_the_system_until_it_is_changed()
    {
        await using var app = await AppHarness.StartAsync(systemReducesMotion: true);

        Assert.True(app.ViewModel.Appearance.ReduceMotion);
        Assert.Contains("still", app.Window.Classes);
        Assert.All(Puffs(app), puff => Assert.True(puff.IsStill));

        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        Assert.True(settings.ReduceMotion);
        Assert.Null(app.Get<ISettingsStore>().Load().ReduceMotion);

        settings.ReduceMotion = false;
        await app.SettleAsync();

        Assert.DoesNotContain("still", app.Window.Classes);
        Assert.All(Puffs(app), puff => Assert.False(puff.IsStill));
        Assert.False(app.Get<ISettingsStore>().Load().ReduceMotion);
    }

    [AvaloniaFact]
    public async Task Reduce_motion_holds_Puff_still_and_stops_switches_sliding()
    {
        await using var app = await AppHarness.StartAsync();
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        await app.SettleAsync();
        var motionSwitch = app.Window.GetVisualDescendants().OfType<ToggleButton>()
            .Single(toggle => AutomationProperties.GetName(toggle) == "Reduce motion");
        motionSwitch.BringIntoView();
        await app.SettleAsync();
        app.Screenshot("settings-appearance-motion");
        var thumb = motionSwitch.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_Thumb");
        Assert.NotEmpty(thumb.Transitions!);

        motionSwitch.IsChecked = true;
        await app.SettleAsync();

        Assert.True(settings.ReduceMotion);
        Assert.Contains("still", app.Window.Classes);
        Assert.True(thumb.Transitions is null || thumb.Transitions.Count == 0);
        settings.Section = SettingsSection.About;
        await app.SettleAsync();
        var puff = Puffs(app).Single(candidate => candidate.CheerOnPress);
        Assert.True(puff.IsStill);
        puff.Cheer();
        puff.Squish();
        Assert.Equal(0, await MotionAsync(puff));
        app.Screenshot("about-puff-still");
    }

    private static List<Puff> Puffs(AppHarness app) => app.Window.GetVisualDescendants().OfType<Puff>().ToList();

    /// <summary>Lets animations run for a while. The headless clock only moves when the render timer ticks.</summary>
    private static async Task RunAsync(int milliseconds)
    {
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 40)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(40);
        }
    }

    /// <summary>How far Puff moved while animations ran: its biggest lift, or its biggest squash or glance scaled to match.</summary>
    private static async Task<double> MotionAsync(Puff puff, int milliseconds = 720)
    {
        var most = 0d;
        for (var elapsed = 0; elapsed < milliseconds; elapsed += 40)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            most = Math.Max(most, Math.Max(Math.Abs(puff.Lift), Math.Max(Math.Abs(puff.Squash) * 10, Math.Abs(puff.Gaze) * 2)));
            await Task.Delay(40);
        }

        return most;
    }
}
