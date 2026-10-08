using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Prompuff.App.Themes;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;

namespace Prompuff.App.Tests;

public class ThemeTests
{
    public static TheoryData<string> ThemeIds => new(ThemeCatalog.All.Select(theme => theme.Id));

    private static Color Resolve(string key, ThemeVariant variant)
    {
        Assert.True(Avalonia.Application.Current!.TryGetResource(key, variant, out var value), $"{key} is missing for {variant}");
        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => throw new InvalidOperationException($"{key} is a {value?.GetType().Name}"),
        };
    }

    [Fact]
    public void Every_palette_is_complete_and_has_a_unique_ID()
    {
        Assert.Equal(ThemeCatalog.All.Count, ThemeCatalog.All.Select(theme => theme.Id).Distinct().Count());
        foreach (var theme in ThemeCatalog.All)
        {
            Assert.Equal(6, theme.Backgrounds.Length);
            Assert.Equal(3, theme.Borders.Length);
            Assert.Equal(4, theme.Texts.Length);
            Assert.Equal(4, theme.Accent.Length);
        }

        Assert.Equal(ThemeCatalog.PrompuffDark, ThemeCatalog.Find("no-such-theme", dark: true).Id);
        Assert.Equal(ThemeCatalog.PrompuffLight, ThemeCatalog.Find("gruvbox-dark", dark: false).Id);
    }

    /// <summary>Prompuff's own themes live in Tokens.axaml; the catalog's copies, used for the previews, must match.</summary>
    [AvaloniaFact]
    public void Prompuff_themes_in_the_catalog_match_Tokens_axaml()
    {
        foreach (var (palette, variant) in new[] { (ThemeCatalog.Find(ThemeCatalog.PrompuffDark, true), ThemeVariant.Dark), (ThemeCatalog.Find(ThemeCatalog.PrompuffLight, false), ThemeVariant.Light) })
        {
            for (var level = 0; level <= 4; level++)
            {
                Assert.Equal(palette.Bg(level), Resolve($"Bg{level}Brush", variant));
            }

            for (var level = 1; level <= 4; level++)
            {
                Assert.Equal(palette.Text(level), Resolve($"Text{level}Brush", variant));
            }

            Assert.Equal(palette.Inset, Resolve("BgInsetBrush", variant));
            Assert.Equal(palette.AccentColor, Resolve("AccentBrush", variant));
            Assert.Equal(palette.AccentText, Resolve("AccentTextBrush", variant));
            Assert.Equal(palette.Lavender.Text, Resolve("LavenderTextBrush", variant));
            Assert.Equal(palette.Critical.Base, Resolve("CriticalBrush", variant));
        }
    }

    /// <summary>Every key Prompuff's dark theme defines, every other theme defines too, so nothing falls back by accident.</summary>
    [AvaloniaFact]
    public void Every_theme_defines_every_token()
    {
        var merged = Avalonia.Application.Current!.Resources.MergedDictionaries.Single();
        var tokens = (ResourceDictionary)(merged is ResourceInclude include ? include.Loaded : merged);
        var keys = ((ResourceDictionary)tokens.ThemeDictionaries[ThemeVariant.Dark]).Keys.ToList();
        Assert.Contains("FocusRing", keys);
        foreach (var palette in ThemeCatalog.All.Where(theme => !theme.IsBuiltIn))
        {
            var built = ThemeBuilder.Tokens(palette);
            var missing = keys.Where(key => !built.ContainsKey(key)).ToList();
            Assert.True(missing.Count == 0, $"{palette.Name} is missing {string.Join(", ", missing)}");
        }
    }

    /// <summary>WCAG relative luminance and contrast, as in https://www.w3.org/TR/WCAG21/#dfn-contrast-ratio.</summary>
    internal static double Contrast(Color a, Color b)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));

        var (light, dark) = (Luminance(a), Luminance(b));
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    /// <summary>
    /// Body, secondary and faint text (Text1 to Text3), tone text such as tags, and labels on the accent reach WCAG AA
    /// (4.5:1) on every background they sit on. Text4 is only for decoration that has a text equivalent beside it, such
    /// as empty rating dots next to "Great", and the editor's line numbers, so it has no bar.
    /// </summary>
    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Text_is_readable_on_every_background(string id)
    {
        var theme = ThemeCatalog.All.Single(palette => palette.Id == id);
        var failures = new List<string>();

        void Check(string what, Color text, Color background, string on, double minimum)
        {
            var ratio = Contrast(text, background);
            if (ratio < minimum)
            {
                failures.Add($"{what} on {on}: {ratio:0.00} < {minimum}");
            }
        }

        var surfaces = new[] { ("Bg0", theme.Bg(0)), ("Bg1", theme.Bg(1)), ("Bg2", theme.Bg(2)), ("Bg3", theme.Bg(3)), ("Inset", theme.Inset) };
        foreach (var (name, background) in surfaces)
        {
            Check("Text1", theme.Text(1), background, name, 4.5);
            Check("Text2", theme.Text(2), background, name, 4.5);
            Check("Text3", theme.Text(3), background, name, 4.5);
        }

        foreach (var (name, background) in new[] { ("Bg1", theme.Bg(1)), ("Bg2", theme.Bg(2)) })
        {
            Check("AccentText", theme.AccentText, background, name, 4.5);
            foreach (var (tone, color) in new[]
                     {
                         ("Lavender", theme.Lavender), ("Sky", theme.Sky), ("Pink", theme.Pink), ("Peach", theme.Peach),
                         ("Mint", theme.Mint), ("Blue", theme.Blue), ("Amber", theme.Amber), ("Critical", theme.Critical),
                     })
            {
                Check(tone + "Text", color.Text, background, name, 4.5);
            }
        }

        Check("TextInverse", theme.Inverse, theme.AccentColor, "Accent", 4.5);
        Assert.True(failures.Count == 0, $"{theme.Name}:\n" + string.Join("\n", failures));
    }

    [AvaloniaFact]
    public async Task Picking_a_theme_restyles_the_app_at_once_and_is_remembered()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.Appearance);
        await app.SettleAsync();
        Assert.True(settings.DarkThemes.Single(card => card.Palette.Id == ThemeCatalog.PrompuffDark).IsSelected);

        var gruvbox = settings.DarkThemes.Single(card => card.Palette.Id == "gruvbox-dark");
        gruvbox.SelectCommand.Execute(null);
        await app.SettleAsync();

        var variant = Avalonia.Application.Current!.ActualThemeVariant;
        Assert.Equal("gruvbox-dark", variant.Key);
        Assert.Equal(gruvbox.Palette.Bg(2), Resolve("Bg2Brush", variant));
        Assert.Equal(gruvbox.Palette.Text(1), Resolve("SystemBaseHighColor", variant));
        Assert.True(gruvbox.IsSelected);
        Assert.Equal("gruvbox-dark", app.Get<ISettingsStore>().Load().DarkTheme);
        app.Screenshot("settings-appearance-gruvbox-dark");

        // A light theme picked while Dark is showing switches to Light, so the choice shows straight away.
        settings.LightThemes.Single(card => card.Palette.Id == "catppuccin-latte").SelectCommand.Execute(null);
        await app.SettleAsync();
        Assert.True(settings.IsLightTheme);
        Assert.Equal("catppuccin-latte", Avalonia.Application.Current!.ActualThemeVariant.Key);
        app.Screenshot("settings-appearance-catppuccin-latte");

        // Dark again brings back the dark theme that was picked.
        settings.IsDarkTheme = true;
        await app.SettleAsync();
        Assert.Equal("gruvbox-dark", Avalonia.Application.Current!.ActualThemeVariant.Key);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public async Task Every_theme_dresses_the_library_the_editor_and_dialogs(string id)
    {
        await using var app = await AppHarness.StartAsync();
        var settings = app.ViewModel.Settings;
        var card = settings.DarkThemes.Concat(settings.LightThemes).Single(theme => theme.Palette.Id == id);
        card.SelectCommand.Execute(null);
        await app.SettleAsync();
        Assert.Equal(ThemeBuilder.Variant(card.Palette).Key, Avalonia.Application.Current!.ActualThemeVariant.Key);
        app.Screenshot($"theme-{id}-library");

        var prompt = (await app.Get<IPromptSearch>().SearchAsync(Application.DTOs.PromptQuery.All)).First(p => p.Title == "Angular Upgrade Planner");
        await app.Get<Navigator>().OpenPromptAsync(prompt.Id);
        await app.SettleAsync();
        app.Screenshot($"theme-{id}-editor");

        var asking = app.ViewModel.Dialogs.ConfirmAsync("Delete “Angular Upgrade Planner”?", "It moves to Recently deleted for 30 days.", "Delete", isDanger: true);
        await app.SettleAsync();
        app.Screenshot($"theme-{id}-dialog");
        app.ViewModel.Dialogs.CancelCurrent();
        Assert.False(await asking);
    }
}
