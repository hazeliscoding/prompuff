using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Prompuff.App.Platform;

/// <summary>Brings the main window forward when another launch, the tray or the hotkey asks for it.</summary>
internal static class WindowActivation
{
    public static void BringForward(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } handle && handle != IntPtr.Zero)
        {
            // Activate alone can leave the window flashing in the taskbar. The launch, tray click or hotkey that got
            // here gives this process the right to come to the front.
            _ = SetForegroundWindow(handle);
        }
    }

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
