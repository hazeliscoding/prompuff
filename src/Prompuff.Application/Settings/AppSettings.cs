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

public sealed record WindowPlacement(double Width, double Height, bool IsMaximized);

public sealed record AppSettings
{
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;
    public bool ShowMascot { get; init; } = true;
    public bool CheckForUpdatesAutomatically { get; init; } = true;
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public LibraryLayout LibraryLayout { get; init; } = LibraryLayout.Cards;
    public PromptSort LibrarySort { get; init; } = PromptSort.LastEdited;
    public WindowPlacement? Window { get; init; }

    public static AppSettings Default { get; } = new();
}
