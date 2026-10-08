namespace Prompuff.App.Platform;

/// <summary>
/// A system-wide hotkey that works while another app has focus. Implemented for Windows, X11 and macOS. Wayland
/// doesn't let apps listen for keys in other apps, so there the service reports why and Settings explains how to bind
/// a desktop shortcut to <c>--quick-save</c> instead.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    bool IsSupported { get; }

    /// <summary>Why the hotkey can't work here, for Settings to show. Null when it's supported.</summary>
    string? UnsupportedReason { get; }

    /// <summary>Replaces the registered hotkey; null turns it off. Returns false when another app already has it.</summary>
    Task<bool> RegisterAsync(Hotkey? hotkey);

    /// <summary>Raised when the hotkey is pressed, possibly on a background thread.</summary>
    event EventHandler? Pressed;
}

public static class GlobalHotkeys
{
    /// <summary>The implementation for this platform and session.</summary>
    public static IGlobalHotkeyService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsGlobalHotkeyService();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacGlobalHotkeyService();
        }

        if (OperatingSystem.IsLinux())
        {
            if (IsWaylandSession())
            {
                return new UnsupportedGlobalHotkeyService(
                    "Wayland doesn't let apps listen for keys while another app has focus, so bind a shortcut in your desktop's settings instead.");
            }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                return new X11GlobalHotkeyService();
            }
        }

        return new UnsupportedGlobalHotkeyService("This desktop doesn't offer system-wide hotkeys.");
    }

    public static bool IsWaylandSession() =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
}

/// <summary>Where system-wide hotkeys aren't possible, such as under Wayland.</summary>
public sealed class UnsupportedGlobalHotkeyService(string reason) : IGlobalHotkeyService
{
    public bool IsSupported => false;

    public string? UnsupportedReason { get; } = reason;

    public Task<bool> RegisterAsync(Hotkey? hotkey) => Task.FromResult(hotkey is null);

    public event EventHandler? Pressed
    {
        add { }
        remove { }
    }

    public void Dispose()
    {
    }
}
