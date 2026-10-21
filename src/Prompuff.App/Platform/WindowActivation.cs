using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;

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
        else if (OperatingSystem.IsLinux() && X11Activation.IsAvailable && window.TryGetPlatformHandle()?.Handle is { } xid && xid != IntPtr.Zero)
        {
            // Avalonia's request can look like an app stealing focus, since the hotkey's keypress reached Prompuff on a
            // connection Avalonia doesn't see, and without a window manager nobody answers it. A pager's request is
            // honored. It goes after Avalonia's own requests, such as showing the window from the tray.
            Dispatcher.UIThread.Post(() => X11Activation.Activate(xid), DispatcherPriority.Background);
        }
    }

    [DllImport("user32.dll")]
    [SupportedOSPlatform("windows")]
    private static extern bool SetForegroundWindow(IntPtr window);
}
