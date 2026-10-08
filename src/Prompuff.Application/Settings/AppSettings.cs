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

public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;
    public bool ShowMascot { get; init; } = true;
    public bool CheckForUpdatesAutomatically { get; init; } = true;
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public LibraryLayout LibraryLayout { get; init; } = LibraryLayout.Cards;
    public Density Density { get; init; } = Density.Cozy;

    /// <summary>Closing the window hides it and keeps Prompuff in the tray, so Quick save stays a keypress away.</summary>
    public bool KeepRunningInTray { get; init; }

    /// <summary>The system-wide Quick save hotkey, such as "Ctrl+Alt+P". Empty turns it off.</summary>
    public string QuickSaveHotkey { get; init; } = "Ctrl+Alt+P";

    /// <summary>Lets AI tools read the library through <c>prompuff mcp</c>. Off until the user turns it on.</summary>
    public bool AllowMcp { get; init; }

    /// <summary>Where "Install command-line tool" copied the CLI on Linux and macOS, so Prompuff can refresh that copy.</summary>
    public string? InstalledCommandLineTool { get; init; }
    public PromptSort LibrarySort { get; init; } = PromptSort.LastEdited;
    public WindowPlacement? Window { get; init; }

    public static AppSettings Default { get; } = new();
}
