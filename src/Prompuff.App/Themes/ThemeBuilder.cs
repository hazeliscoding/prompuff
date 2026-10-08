using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Prompuff.App.Themes;

/// <summary>
/// Turns the catalog's palettes into theme variants. Each one inherits from Dark or Light, so anything it doesn't set
/// falls back there, and carries the same token keys as <c>Tokens.axaml</c> plus a Fluent palette for the stock
/// controls. Views use DynamicResource, so switching variants restyles everything live.
/// </summary>
public static class ThemeBuilder
{
    private static readonly Dictionary<string, ThemeVariant> Variants = [];

    /// <summary>Prompuff's own themes are the built-in Dark and Light variants; the rest get one of their own.</summary>
    public static ThemeVariant Variant(ThemePalette palette)
    {
        if (palette.IsBuiltIn)
        {
            return palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        lock (Variants)
        {
            if (!Variants.TryGetValue(palette.Id, out var variant))
            {
                variant = new ThemeVariant(palette.Id, palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);
                Variants[palette.Id] = variant;
            }

            return variant;
        }
    }

    /// <summary>Adds every catalog theme to the app. Runs once, as the app loads.</summary>
    public static void Register(Avalonia.Application application)
    {
        foreach (var palette in ThemeCatalog.All.Where(theme => !theme.IsBuiltIn))
        {
            application.Resources.ThemeDictionaries[Variant(palette)] = Tokens(palette);
        }
    }

    public static ResourceDictionary Tokens(ThemePalette p)
    {
        var dark = p.IsDark;
        var raised = dark ? p.Bg(3) : p.Bg(2);
        var shadow = dark ? Colors.Black : p.Text(1);
        var resources = new ResourceDictionary();

        void Brush(string key, Color color) => resources[key] = new SolidColorBrush(color);

        Brush("Bg0Brush", p.Bg(0));
        Brush("Bg1Brush", p.Bg(1));
        Brush("Bg2Brush", p.Bg(2));
        Brush("Bg3Brush", p.Bg(3));
        Brush("Bg4Brush", p.Bg(4));
        Brush("BgInsetBrush", p.Inset);
        Brush("Border1Brush", p.Border(1));
        Brush("Border2Brush", p.Border(2));
        Brush("Border3Brush", p.Border(3));
        Brush("Text1Brush", p.Text(1));
        Brush("Text2Brush", p.Text(2));
        Brush("Text3Brush", p.Text(3));
        Brush("Text4Brush", p.Text(4));
        Brush("TextInverseBrush", p.Inverse);
        Brush("ScrimBrush", dark ? WithAlpha(p.Inset, 0xA6) : WithAlpha(p.Text(1), 0x73));
        resources["HighlightInset"] = new BoxShadows(new BoxShadow { OffsetY = dark ? 1 : 0, IsInset = true, Color = dark ? Color.Parse("#0AFFFFFF") : Colors.Transparent });
        resources["Shadow1"] = Shadow(0, 1, 2, WithAlpha(shadow, dark ? (byte)0x59 : (byte)0x0F));
        resources["Shadow3"] = Shadow(0, 8, 24, WithAlpha(shadow, dark ? (byte)0x80 : (byte)0x1F));
        resources["Shadow4"] = Shadow(0, 24, 64, WithAlpha(shadow, dark ? (byte)0x99 : (byte)0x2E));
        resources["FocusRing"] = new BoxShadows(new BoxShadow { Spread = 3, Color = WithAlpha(p.AccentColor, 0x1A) });

        Brush("AccentBrush", p.AccentColor);
        Brush("AccentHoverBrush", p.AccentHover);
        Brush("AccentActiveBrush", p.AccentActive);
        Brush("AccentTextBrush", p.AccentText);
        Brush("AccentSubtleBrush", WithAlpha(p.AccentColor, 0x1A));
        Brush("AccentBorderBrush", WithAlpha(p.AccentColor, 0x59));

        foreach (var (name, tone) in new[]
                 {
                     ("Lavender", p.Lavender), ("Sky", p.Sky), ("Pink", p.Pink), ("Peach", p.Peach),
                     ("Mint", p.Mint), ("Blue", p.Blue), ("Amber", p.Amber), ("Critical", p.Critical),
                 })
        {
            Brush(name + "Brush", tone.Base);
            Brush(name + "TextBrush", tone.Text);
            Brush(name + "SubtleBrush", WithAlpha(tone.Base, dark ? (byte)0x1F : (byte)0x1A));
            Brush(name + "BorderBrush", WithAlpha(tone.Base, dark ? (byte)0x66 : (byte)0x59));
        }

        Brush("TextControlBackground", dark ? p.Inset : p.Bg(2));
        Brush("TextControlBackgroundPointerOver", dark ? p.Inset : p.Bg(2));
        Brush("TextControlBackgroundFocused", dark ? p.Inset : p.Bg(2));
        Brush("TextControlBorderBrush", p.Border(2));
        Brush("TextControlBorderBrushPointerOver", p.Border(3));
        Brush("TextControlBorderBrushFocused", p.AccentColor);
        Brush("TextControlForeground", p.Text(1));
        Brush("TextControlForegroundPointerOver", p.Text(1));
        Brush("TextControlForegroundFocused", p.Text(1));
        Brush("TextControlPlaceholderForeground", p.Text(3));
        Brush("TextControlPlaceholderForegroundPointerOver", p.Text(3));
        Brush("TextControlPlaceholderForegroundFocused", p.Text(3));
        Brush("TextControlSelectionHighlightColor", WithAlpha(p.AccentColor, 0x59));
        Brush("ComboBoxBackground", raised);
        Brush("ComboBoxBackgroundPointerOver", dark ? p.Bg(4) : p.Bg(3));
        Brush("ComboBoxBackgroundPressed", p.Bg(4));
        Brush("ComboBoxBackgroundFocused", raised);
        Brush("ComboBoxBorderBrush", p.Border(2));
        Brush("ComboBoxBorderBrushPointerOver", p.Border(3));
        Brush("ComboBoxBorderBrushPressed", p.Border(3));
        Brush("ComboBoxForeground", p.Text(1));
        Brush("ComboBoxDropDownBackground", raised);
        Brush("ComboBoxDropDownBorderBrush", p.Border(2));
        Brush("ComboBoxDropDownGlyphForeground", p.Text(2));
        Brush("ComboBoxItemBackgroundPointerOver", dark ? p.Bg(4) : p.Bg(3));
        Brush("ComboBoxItemBackgroundSelected", p.Bg(4));
        Brush("ComboBoxItemBackgroundSelectedPointerOver", dark ? p.Border(2) : p.Border(1));
        Brush("MenuFlyoutPresenterBackground", raised);
        Brush("MenuFlyoutPresenterBorderBrush", p.Border(2));
        Brush("MenuFlyoutItemBackgroundPointerOver", dark ? p.Bg(4) : p.Bg(3));
        Brush("MenuFlyoutItemBackgroundPressed", dark ? p.Border(2) : p.Bg(4));
        Brush("ToolTipBackground", raised);
        Brush("ToolTipForeground", p.Text(1));
        Brush("ToolTipBorderBrush", p.Border(2));

        foreach (var (key, color) in FluentColors(p))
        {
            resources[key] = color;
        }

        return resources;
    }

    /// <summary>
    /// The colors Fluent's stock controls read, mapped the way <c>App.axaml</c> maps Prompuff's own palettes. Fluent
    /// only takes palettes for Dark and Light, so the theme's dictionary carries them, and wins because the app's
    /// resources are searched before Fluent's.
    /// </summary>
    public static IEnumerable<(string Key, Color Color)> FluentColors(ThemePalette p)
    {
        var dark = p.IsDark;
        var alt = dark ? p.Bg(0) : p.Bg(2);
        var baseMediumHigh = Mix(p.Text(1), p.Text(2));
        return
        [
            ("SystemAccentColor", p.AccentColor),
            ("SystemAccentColorDark1", p.AccentActive), ("SystemAccentColorDark2", p.AccentActive), ("SystemAccentColorDark3", p.AccentActive),
            ("SystemAccentColorLight1", p.AccentHover), ("SystemAccentColorLight2", p.AccentHover), ("SystemAccentColorLight3", p.AccentHover),
            ("SystemAltHighColor", alt), ("SystemAltLowColor", alt), ("SystemAltMediumColor", alt), ("SystemAltMediumHighColor", alt), ("SystemAltMediumLowColor", alt),
            ("SystemBaseHighColor", p.Text(1)), ("SystemBaseLowColor", p.Border(2)), ("SystemBaseMediumColor", p.Text(2)),
            ("SystemBaseMediumHighColor", baseMediumHigh), ("SystemBaseMediumLowColor", p.Text(3)),
            ("SystemChromeAltLowColor", baseMediumHigh),
            ("SystemChromeBlackHighColor", Colors.Black),
            ("SystemChromeBlackLowColor", dark ? baseMediumHigh : p.Border(2)),
            ("SystemChromeBlackMediumColor", dark ? Colors.Black : p.Text(2)),
            ("SystemChromeBlackMediumLowColor", dark ? Colors.Black : p.Text(3)),
            ("SystemChromeDisabledHighColor", p.Border(2)), ("SystemChromeDisabledLowColor", p.Text(3)),
            ("SystemChromeGrayColor", dark ? p.Text(2) : p.Text(3)),
            ("SystemChromeHighColor", p.Border(3)), ("SystemChromeLowColor", p.Bg(1)),
            ("SystemChromeMediumColor", dark ? p.Bg(2) : p.Bg(3)),
            ("SystemChromeMediumLowColor", dark ? p.Bg(3) : p.Bg(2)),
            ("SystemChromeWhiteColor", Colors.White),
            ("SystemListLowColor", p.Bg(3)), ("SystemListMediumColor", p.Bg(4)),
            ("SystemRegionColor", p.Bg(0)), ("SystemErrorTextColor", p.Critical.Text),
        ];
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color a, Color b) =>
        Color.FromRgb((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    private static BoxShadows Shadow(double x, double y, double blur, Color color) =>
        new(new BoxShadow { OffsetX = x, OffsetY = y, Blur = blur, Color = color });
}
