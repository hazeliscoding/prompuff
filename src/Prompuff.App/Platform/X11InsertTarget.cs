using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Remembers the active window from the window manager's <c>_NET_ACTIVE_WINDOW</c>, or the input focus where there's
/// no window manager, asks for it back the same way, and pastes with XTest and Ctrl+V. Each step opens a display
/// connection of its own, so it never touches Avalonia's.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11InsertTarget : IInsertTarget
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";
    private const int ClientMessage = 33;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const int RevertToParent = 2;
    private const IntPtr PointerRoot = 1;
    private const IntPtr WindowAtom = 33;

    private static readonly Lazy<bool> HasXTest = new(() => NativeLibrary.TryLoad(LibXtst, out _));

    private IntPtr _target;
    private bool _managed;

    public bool IsSupported => true;

    /// <summary>XTest is part of every X desktop, but a minimal system might lack the library.</summary>
    public bool CanPaste => HasXTest.Value;

    public void Remember(IntPtr ownWindow)
    {
        WithDisplay(display =>
        {
            var (front, managed) = ActiveWindow(display);
            if (front != IntPtr.Zero && front != ownWindow)
            {
                (_target, _managed) = (front, managed);
            }

            return true;
        });
    }

    public async Task<bool> ReturnAsync()
    {
        var target = _target;
        _target = IntPtr.Zero;
        if (target == IntPtr.Zero)
        {
            return false;
        }

        await WaitForKeysUpAsync();
        return await WithDisplayAsync(async display =>
        {
            var root = XDefaultRootWindow(display);
            if (_managed)
            {
                // EWMH: ask the window manager to activate it, as a pager would.
                var message = Marshal.AllocHGlobal(192);
                try
                {
                    // XClientMessageEvent: type, serial, send_event, display, window, message_type, format, data.
                    Marshal.Copy(new byte[192], 0, message, 192);
                    Marshal.WriteInt32(message, 0, ClientMessage);
                    Marshal.WriteIntPtr(message, 32, target);
                    Marshal.WriteIntPtr(message, 40, XInternAtom(display, "_NET_ACTIVE_WINDOW", false));
                    Marshal.WriteInt32(message, 48, 32);
                    Marshal.WriteInt64(message, 56, 2);
                    _ = XSendEvent(display, root, false, (IntPtr)(SubstructureRedirectMask | SubstructureNotifyMask), message);
                }
                finally
                {
                    Marshal.FreeHGlobal(message);
                }
            }
            else
            {
                _ = XRaiseWindow(display, target);
                _ = XSetInputFocus(display, target, RevertToParent, IntPtr.Zero);
            }

            _ = XSync(display, false);
            return X11Errors.Last(display) == 0
                   && await InsertTargets.WaitUntilAsync(() => ActiveWindow(display).Window == target, 500);
        });
    }

    public async Task<bool> PasteAsync()
    {
        if (!CanPaste)
        {
            return false;
        }

        await WaitForKeysUpAsync();
        return WithDisplay(display =>
        {
            var control = XKeysymToKeycode(display, XStringToKeysym("Control_L"));
            var v = XKeysymToKeycode(display, XStringToKeysym("v"));
            if (control == 0 || v == 0)
            {
                return false;
            }

            _ = XTestFakeKeyEvent(display, control, true, 0);
            _ = XTestFakeKeyEvent(display, v, true, 0);
            _ = XTestFakeKeyEvent(display, v, false, 0);
            _ = XTestFakeKeyEvent(display, control, false, 0);
            _ = XSync(display, false);
            return true;
        });
    }

    public void RequestPasteAccess()
    {
    }

    /// <summary>The active window, and whether a window manager reported it. Zero when there's none.</summary>
    private static (IntPtr Window, bool Managed) ActiveWindow(IntPtr display)
    {
        var root = XDefaultRootWindow(display);
        var atom = XInternAtom(display, "_NET_ACTIVE_WINDOW", true);
        if (atom != IntPtr.Zero
            && XGetWindowProperty(display, root, atom, 0, 1, false, WindowAtom, out _, out var format, out var count, out _, out var data) == 0
            && data != IntPtr.Zero)
        {
            var window = format == 32 && count == 1 ? Marshal.ReadIntPtr(data) : IntPtr.Zero;
            _ = XFree(data);
            if (window != IntPtr.Zero)
            {
                return (window, true);
            }
        }

        _ = XGetInputFocus(display, out var focus, out _);
        return (focus == PointerRoot ? IntPtr.Zero : focus, false);
    }

    // Enter picks in Quick insert, and a held modifier would turn Ctrl+V into another shortcut.
    private static Task<bool> WaitForKeysUpAsync() => WithDisplayAsync(display =>
    {
        var keys = new byte[32];
        return InsertTargets.WaitUntilAsync(() =>
        {
            _ = XQueryKeymap(display, keys);
            return keys.All(bits => bits == 0);
        });
    });

    private static bool WithDisplay(Func<IntPtr, bool> use) => WithDisplayAsync(display => Task.FromResult(use(display))).GetAwaiter().GetResult();

    private static async Task<bool> WithDisplayAsync(Func<IntPtr, Task<bool>> use)
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

    [DllImport(LibX11)]
    private static extern int XQueryKeymap(IntPtr display, byte[] keys);

    [DllImport(LibX11)]
    private static extern ulong XStringToKeysym(string name);

    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport(LibXtst)]
    private static extern int XTestFakeKeyEvent(IntPtr display, uint keyCode, bool isPress, ulong delay);
}
