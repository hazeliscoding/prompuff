using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;

namespace Prompuff.App.Tests;

public class FocusOrderTests
{
    /// <summary>
    /// Tab walks the window the way it reads: the title bar, the sidebar from top to bottom with Settings last, then
    /// the library's toolbar and cards. Every stop is on screen and shows that it has focus.
    /// </summary>
    [AvaloniaFact]
    public async Task Tab_moves_through_the_window_in_reading_order()
    {
        await using var app = await AppHarness.StartAsync();
        var focus = app.Window.FocusManager!;
        var stops = new List<string>();
        for (var i = 0; i < 30; i++)
        {
            app.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            await app.SettleAsync(30);
            var focused = Assert.IsAssignableFrom<Control>(focus.GetFocusedElement());
            Assert.True(focused.IsEffectivelyVisible, $"Stop {i} is hidden: {focused.GetType().Name}");
            Assert.Contains(":focus-visible", focused.Classes);
            stops.Add(ControlAutomationPeer.CreatePeerForElement(focused).GetName());
            if (i is 0 or 5 or 14)
            {
                app.Screenshot($"focus-stop-{i:00}");
            }
        }

        Assert.Equal(["Library", "Search prompts, notes and tags, or #tag", "Command palette", "Quick save", "New prompt", "All prompts 6"], stops.Take(6));
        var settings = stops.IndexOf("Settings");
        Assert.True(settings > stops.IndexOf("#mockup"), "Settings comes after the sidebar's tags");
        Assert.True(settings < stops.IndexOf("Select"), "and before the library's toolbar");
        Assert.True(stops.IndexOf("Sort by") < stops.FindIndex(name => name.StartsWith("UI Mockup Generator", StringComparison.Ordinal)), "The toolbar comes before the cards");
    }

    /// <summary>Tab goes round Quick insert's own controls and never reaches the window behind it.</summary>
    [AvaloniaFact]
    public async Task Tab_stays_inside_Quick_insert()
    {
        await using var app = await AppHarness.StartAsync();
        await app.ViewModel.QuickInsertFromOutsideAsync();
        await app.SettleAsync();
        var focus = app.Window.FocusManager!;
        static Border? ModalOf(Visual control) => control.GetVisualAncestors().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("modal"));
        var modal = ModalOf(app.Window.FindControl<TextBox>("QuickInsertInput")!);
        Assert.NotNull(modal);

        async Task<List<object?>> Walk(int steps)
        {
            var seen = new List<object?>();
            for (var i = 0; i < steps; i++)
            {
                app.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                await app.SettleAsync(30);
                var focused = Assert.IsAssignableFrom<Control>(focus.GetFocusedElement());
                Assert.True(focused.IsEffectivelyVisible);
                Assert.Same(modal, ModalOf(focused));
                seen.Add(focused);
            }

            return seen;
        }

        await Walk(4);

        // Picking a prompt with two variables: Tab goes between its two boxes.
        app.Window.FindControl<TextBox>("QuickInsertInput")!.Focus();
        app.Window.KeyTextInput("handoff");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        var boxes = await Walk(4);
        Assert.Equal(2, boxes.Distinct().Count());
        Assert.All(boxes, box => Assert.IsType<VariableViewModel>(Assert.IsType<TextBox>(box).DataContext));
        app.Screenshot("quick-insert-focus");
    }
}
