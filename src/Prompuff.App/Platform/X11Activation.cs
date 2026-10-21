using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Activates X11 windows the way a taskbar does: a <c>_NET_ACTIVE_WINDOW</c> request marked as coming from a pager,
/// which window managers honor even when they'd stop an app stealing focus. Without a window manager it raises the
/// window and gives it the input focus. Each call opens a display connection of its own, so it never touches Avalonia's.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11Activation
{
    private const string LibX11 = "libX11.so.6";
    private const int ClientMessage = 33;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const int RevertToParent = 2;
    private const int PagerSource = 2;
    private const IntPtr PointerRoot = 1;
    private const IntPtr WindowAtom = 33;

    /// <summary>An X11 session. Under Wayland, X11 calls only reach other X11 apps, so they're left alone.</summary>
    public static bool IsAvailable =>
        !GlobalHotkeys.IsWaylandSession() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>Brings one of Prompuff's own windows forward.</summary>
    public static void Activate(IntPtr window) => WithDisplay(display =>
    {
        Activate(display, window);
        return true;
    });

    /// <summary>Asks for <paramref name="window"/> to become active. Check <see cref="X11Errors.Last"/> for a window that's gone.</summary>
    public static void Activate(IntPtr display, IntPtr window)
    {
        var root = XDefaultRootWindow(display);
        if (WindowProperty(display, root, "_NET_SUPPORTING_WM_CHECK") != IntPtr.Zero)
        {
            // XClientMessageEvent: type, serial, send_event, display, window, message_type, format, then the data.
            var message = Marshal.AllocHGlobal(192);
            try
            {
                Marshal.Copy(new byte[192], 0, message, 192);
                Marshal.WriteInt32(message, 0, ClientMessage);
                Marshal.WriteIntPtr(message, 32, window);
                Marshal.WriteIntPtr(message, 40, XInternAtom(display, "_NET_ACTIVE_WINDOW", false));
                Marshal.WriteInt32(message, 48, 32);
                Marshal.WriteInt64(message, 56, PagerSource);
                _ = XSendEvent(display, root, false, (IntPtr)(SubstructureRedirectMask | SubstructureNotifyMask), message);
            }
            finally
            {
                Marshal.FreeHGlobal(message);
            }
        }
        else
        {
            _ = XRaiseWindow(display, window);
            _ = XSetInputFocus(display, window, RevertToParent, IntPtr.Zero);
        }

        _ = XSync(display, false);
    }

    /// <summary>The active window: the window manager's, or the input focus without one. Zero when there's none.</summary>
    public static IntPtr ActiveWindow(IntPtr display)
    {
        if (WindowProperty(display, XDefaultRootWindow(display), "_NET_ACTIVE_WINDOW") is var active && active != IntPtr.Zero)
        {
            return active;
        }

        _ = XGetInputFocus(display, out var focus, out _);
        return focus == PointerRoot ? IntPtr.Zero : focus;
    }

    public static bool WithDisplay(Func<IntPtr, bool> use) =>
        WithDisplayAsync(display => Task.FromResult(use(display))).GetAwaiter().GetResult();

    /// <summary>Runs <paramref name="use"/> on a new display connection whose X errors are caught. False when there's no display.</summary>
    public static async Task<bool> WithDisplayAsync(Func<IntPtr, Task<bool>> use)
    {
        IntPtr display;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return false;
        }

        if (display == IntPtr.Zero)
        {
            return false;
        }

        X11Errors.Track(display);
        try
        {
            return await use(display);
        }
        finally
        {
            _ = XCloseDisplay(display);
            X11Errors.Forget(display);
        }
    }

    private static IntPtr WindowProperty(IntPtr display, IntPtr window, string name)
    {
        var atom = XInternAtom(display, name, true);
        if (atom == IntPtr.Zero
            || XGetWindowProperty(display, window, atom, 0, 1, false, WindowAtom, out _, out var format, out var count, out _, out var data) != 0
            || data == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var value = format == 32 && count == 1 ? Marshal.ReadIntPtr(data) : IntPtr.Zero;
        _ = XFree(data);
        return value;
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, long offset, long length, bool delete,
        IntPtr requestedType, out IntPtr actualType, out int actualFormat, out ulong count, out ulong bytesAfter, out IntPtr data);

    [DllImport(LibX11)]
    private static extern int XFree(IntPtr data);

    [DllImport(LibX11)]
    private static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);

    [DllImport(LibX11)]
    private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

    [DllImport(LibX11)]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport(LibX11)]
    private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr eventMask, IntPtr eventSend);

    [DllImport(LibX11)]
    private static extern int XSync(IntPtr display, bool discard);
}
