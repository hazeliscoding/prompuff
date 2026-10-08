using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

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
}
