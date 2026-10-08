using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input;

namespace Prompuff.App.Platform;

/// <summary>
/// <c>XGrabKey</c> on the root window, through a display connection of its own on a background thread, so it never
/// touches Avalonia's. The grab is repeated with Caps Lock and Num Lock, which X counts as modifiers. A grab another
/// app already holds fails with BadAccess, which a chained error handler catches instead of letting Xlib exit.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11GlobalHotkeyService : IGlobalHotkeyService
{
    private const string LibX11 = "libX11.so.6";
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int GrabModeAsync = 1;
    private const int BadAccess = 10;
    private const uint ShiftMask = 1 << 0;
    private const uint LockMask = 1 << 1;
    private const uint ControlMask = 1 << 2;
    private const uint Mod1Mask = 1 << 3; // Alt
    private const uint Mod2Mask = 1 << 4; // Num Lock
    private const uint Mod4Mask = 1 << 6; // Super
    private const short PollIn = 1;

    private static readonly uint[] IgnoredLocks = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];
    private static readonly Lock ErrorGate = new();
    private static readonly XErrorHandler ErrorHandler = OnXError;
    private static IntPtr s_previousHandler;
    private static bool s_handlerInstalled;

    // Every connection this class opened, with the last error X reported on it. Errors on any other connection,
    // such as Avalonia's, go on to the handler that was there before.
    private static readonly Dictionary<IntPtr, int> s_lastErrors = [];

    private readonly Thread _thread;
    private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private (Hotkey? Hotkey, TaskCompletionSource<bool> Done)? _request;
    private volatile bool _stopping;

    public X11GlobalHotkeyService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Prompuff hotkey" };
        _thread.Start();
    }

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    public event EventHandler? Pressed;

    public async Task<bool> RegisterAsync(Hotkey? hotkey)
    {
        if (!await _started.Task)
        {
            return hotkey is null;
        }

        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _request?.Done.TrySetResult(false);
            _request = (hotkey, done);
        }

        return await done.Task;
    }

    public void Dispose()
    {
        _stopping = true;
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    /// <summary>The X keysym name for a key: "p", "7", "F5" or "space".</summary>
    internal static string KeysymName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.F1 and <= Key.F12 => "F" + (key - Key.F1 + 1),
        Key.Space => "space",
        _ => string.Empty,
    };

    private void Run()
    {
        IntPtr display;
        try
        {
            display = XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            display = IntPtr.Zero;
        }

        if (display == IntPtr.Zero)
        {
            _started.TrySetResult(false);
            return;
        }

        InstallErrorHandler(display);
        var root = XDefaultRootWindow(display);
        var poll = new PollFd { Fd = XConnectionNumber(display), Events = PollIn };
        var xEvent = Marshal.AllocHGlobal(192);
        (int KeyCode, uint Modifiers)? grabbed = null;
        ulong lastReleaseTime = 0;
        _started.TrySetResult(true);

        try
        {
            while (!_stopping)
            {
                (Hotkey? Hotkey, TaskCompletionSource<bool> Done)? request;
                lock (_gate)
                {
                    request = _request;
                    _request = null;
                }

                if (request is { } pending)
                {
                    if (grabbed is { } old)
                    {
                        Ungrab(display, root, old.KeyCode, old.Modifiers);
                        grabbed = null;
                    }

                    if (pending.Hotkey is { } hotkey)
                    {
                        grabbed = Grab(display, root, hotkey);
                        pending.Done.TrySetResult(grabbed is not null);
                    }
                    else
                    {
                        pending.Done.TrySetResult(true);
                    }
                }

                while (XPending(display) > 0)
                {
                    XNextEvent(display, xEvent);
                    var type = Marshal.ReadInt32(xEvent);
                    var time = (ulong)Marshal.ReadInt64(xEvent, 56);
                    if (type == KeyRelease)
                    {
                        lastReleaseTime = time;
                    }
                    else if (type == KeyPress && time != lastReleaseTime)
                    {
                        // Auto-repeat sends a release and a press with the same time; only a real press counts.
                        Pressed?.Invoke(this, EventArgs.Empty);
                    }
                }

                poll.Revents = 0;
                _ = PollFds(ref poll, 1, 100);
            }
        }
        finally
        {
            if (grabbed is { } old)
            {
                Ungrab(display, root, old.KeyCode, old.Modifiers);
            }

            Marshal.FreeHGlobal(xEvent);
            XCloseDisplay(display);
            lock (ErrorGate)
            {
                s_lastErrors.Remove(display);
            }
        }
    }

    private static (int KeyCode, uint Modifiers)? Grab(IntPtr display, IntPtr root, Hotkey hotkey)
    {
        var keysym = XStringToKeysym(KeysymName(hotkey.Key));
        var keyCode = keysym == 0 ? 0 : XKeysymToKeycode(display, keysym);
        if (keyCode == 0)
        {
            return null;
        }

        var modifiers = (hotkey.Modifiers.HasFlag(KeyModifiers.Shift) ? ShiftMask : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Control) ? ControlMask : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Alt) ? Mod1Mask : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Meta) ? Mod4Mask : 0);

        lock (ErrorGate)
        {
            s_lastErrors[display] = 0;
        }

        foreach (var locks in IgnoredLocks)
        {
            XGrabKey(display, keyCode, modifiers | locks, root, false, GrabModeAsync, GrabModeAsync);
        }

        // Errors arrive asynchronously; a round trip makes sure any BadAccess has been seen.
        XSync(display, false);
        int error;
        lock (ErrorGate)
        {
            error = s_lastErrors.GetValueOrDefault(display);
        }

        if (error == BadAccess)
        {
            Ungrab(display, root, keyCode, modifiers);
            return null;
        }

        return (keyCode, modifiers);
    }

    private static void Ungrab(IntPtr display, IntPtr root, int keyCode, uint modifiers)
    {
        foreach (var locks in IgnoredLocks)
        {
            XUngrabKey(display, keyCode, modifiers | locks, root);
        }

        XSync(display, false);
    }

    private static void InstallErrorHandler(IntPtr display)
    {
        lock (ErrorGate)
        {
            s_lastErrors[display] = 0;
            if (!s_handlerInstalled)
            {
                // Xlib keeps one handler for the whole process. Avalonia's stays in charge of its own connection.
                s_previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorHandler));
                s_handlerInstalled = true;
            }
        }
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        IntPtr previous;
        lock (ErrorGate)
        {
            if (s_lastErrors.ContainsKey(display))
            {
                // XErrorEvent: type, display, resource id and serial, then the error code.
                s_lastErrors[display] = Marshal.ReadByte(errorEvent, 32);
                return 0;
            }

            previous = s_previousHandler;
        }

        return previous == IntPtr.Zero ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, errorEvent);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern ulong XStringToKeysym(string name);

    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

    [DllImport(LibX11)]
    private static extern int XGrabKey(IntPtr display, int keyCode, uint modifiers, IntPtr grabWindow, bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    private static extern int XUngrabKey(IntPtr display, int keyCode, uint modifiers, IntPtr grabWindow);

    [DllImport(LibX11)]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport(LibX11)]
    private static extern int XPending(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport(LibX11)]
    private static extern int XConnectionNumber(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);

    [DllImport("libc", EntryPoint = "poll")]
    private static extern int PollFds(ref PollFd fds, nuint count, int timeoutMilliseconds);
}
