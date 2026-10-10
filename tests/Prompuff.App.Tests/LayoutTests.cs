using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;

namespace Prompuff.App.Tests;

public class LayoutTests
{
    /// <summary>
    /// Text that runs past the space it was given, with nothing to trim or wrap it, and buttons pushed past the panel
    /// they sit in. Both are what a narrow window does to a layout that only fits a wide one.
    /// </summary>
    private static List<string> Overflowing(AppHarness app, string screen)
    {
        var failures = new List<string>();
        foreach (var control in app.Window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width < 1 || control.FindAncestorOfType<ScrollViewer>() is { HorizontalScrollBarVisibility: not Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled })
            {
                continue;
            }

            if (control is TextBlock { TextTrimming: var trimming, TextWrapping: TextWrapping.NoWrap } text && trimming == TextTrimming.None
                && text.TextLayout.WidthIncludingTrailingWhitespace > text.Bounds.Width + 1 && text.FindAncestorOfType<Control>() is not { ClipToBounds: true })
            {
                failures.Add($"{screen}: \"{text.Text}\" needs {text.TextLayout.WidthIncludingTrailingWhitespace:0} px but has {text.Bounds.Width:0}");
            }

            if (control is Button button && button.FindAncestorOfType<Border>() is { } panel
                && button.TranslatePoint(new Point(button.Bounds.Width, 0), panel) is { } right && right.X > panel.Bounds.Width + 1)
            {
                failures.Add($"{screen}: the button \"{Controls.AccessibleNames.Describe(button)}\" runs {right.X - panel.Bounds.Width:0} px past its panel");
            }
        }

        return failures;
    }

    /// <summary>At the window's minimum size, 960 × 600, the library and every Settings section fit without clipping.</summary>
    [AvaloniaFact]
    public async Task The_library_and_settings_fit_the_smallest_window()
    {
        await using var app = await AppHarness.StartAsync(width: 960, height: 600);
        var failures = new List<string>();

        failures.AddRange(Overflowing(app, "library cards"));
        app.Screenshot("min-size-library-cards");
        app.ViewModel.Library.IsListLayout = true;
        await app.SettleAsync();
        failures.AddRange(Overflowing(app, "library list"));
        app.Screenshot("min-size-library-list");

        app.ViewModel.Library.SelectAll();
        await app.SettleAsync();
        failures.AddRange(Overflowing(app, "library selecting"));
        app.Screenshot("min-size-library-selecting");
        app.ViewModel.Library.ClearSelection();

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            app.ViewModel.Settings.Select(section);
            await app.SettleAsync();
            failures.AddRange(Overflowing(app, $"settings {section}"));
            app.Screenshot($"min-size-settings-{section.ToString().ToLowerInvariant()}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} things don't fit at 960 × 600:\n" + string.Join("\n", failures.Distinct()));
    }
}
