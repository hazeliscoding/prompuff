using Prompuff.Application.DTOs;

namespace Prompuff.Application.Settings;

public enum ThemePreference
{
    Dark,
    Light,
    System,
}

public enum UpdateChannel
{
    Stable,
    Beta,
}

public enum LibraryLayout
{
    Cards,
    List,
}

/// <summary>How much room the library gives each prompt: Cozy shows everything, Dense fits the most on screen.</summary>
public enum Density
{
    Cozy,
    Compact,
    Dense,
}

public sealed record WindowPlacement(double Width, double Height, bool IsMaximized);

/// <remarks>
/// The properties are settable, not init-only, because the source-generated JSON reader only keeps these defaults for
/// settable properties; with init-only ones, a key missing from an older settings file came back as false or empty.
/// Treat a loaded instance as a value and change it with <c>with</c>.
/// </remarks>
public sealed record AppSettings
{
    public ThemePreference Theme { get; set; } = ThemePreference.Dark;

    /// <summary>The theme for Dark, and for System while the OS is dark. An ID from Prompuff.App's theme catalog.</summary>
    public string DarkTheme { get; set; } = "prompuff-dark";

    /// <summary>The theme for Light, and for System while the OS is light.</summary>
    public string LightTheme { get; set; } = "prompuff-light";
    public bool ShowMascot { get; set; } = true;

    /// <summary>
    /// Holds Puff still and stops switches sliding. Null follows the system where Prompuff can read it, which is
    /// Windows' "Animation effects"; elsewhere null means motion stays on.
    /// </summary>
    public bool? ReduceMotion { get; set; }
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
    public LibraryLayout LibraryLayout { get; set; } = LibraryLayout.Cards;
    public Density Density { get; set; } = Density.Cozy;

    /// <summary>Closing the window hides it and keeps Prompuff in the tray, so Quick save stays a keypress away.</summary>
    public bool KeepRunningInTray { get; set; }

    /// <summary>The system-wide Quick save hotkey, such as "Ctrl+Alt+P". Empty turns it off.</summary>
    public string QuickSaveHotkey { get; set; } = "Ctrl+Alt+P";

    /// <summary>Lets AI tools read the library through <c>prompuff mcp</c>. Off until the user turns it on.</summary>
    public bool AllowMcp { get; set; }

    /// <summary>Where "Install command-line tool" copied the CLI on Linux and macOS, so Prompuff can refresh that copy.</summary>
    public string? InstalledCommandLineTool { get; set; }
    public PromptSort LibrarySort { get; set; } = PromptSort.LastEdited;
    public WindowPlacement? Window { get; set; }

    /// <summary>A fresh copy each time, so nothing can change the defaults by accident.</summary>
    public static AppSettings Default => new();
}
