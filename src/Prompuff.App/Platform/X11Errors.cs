using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Catches X errors on Prompuff's own display connections, such as BadAccess for a taken hotkey or BadWindow for a
/// window that closed, instead of letting Xlib exit. Xlib keeps one handler for the whole process, so errors on any
/// other connection, such as Avalonia's, go on to the handler that was there before.
/// </summary>
[SupportedOSPlatform("linux")]
internal static class X11Errors
{
    public const int BadAccess = 10;

    private static readonly Lock Gate = new();
    private static readonly XErrorHandler Handler = OnXError;
    private static readonly Dictionary<IntPtr, int> LastErrors = [];
    private static IntPtr s_previousHandler;
    private static bool s_installed;

    /// <summary>Starts catching errors on a connection Prompuff opened.</summary>
    public static void Track(IntPtr display)
    {
        lock (Gate)
        {
            LastErrors[display] = 0;
            if (!s_installed)
            {
                s_previousHandler = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(Handler));
                s_installed = true;
            }
        }
    }

    public static void Forget(IntPtr display)
    {
        lock (Gate)
        {
            LastErrors.Remove(display);
        }
    }

    public static void Clear(IntPtr display)
    {
        lock (Gate)
        {
            LastErrors[display] = 0;
        }
    }

    /// <summary>The last error code on the connection. Errors arrive asynchronously, so call XSync first.</summary>
    public static int Last(IntPtr display)
    {
        lock (Gate)
        {
            return LastErrors.GetValueOrDefault(display);
        }
    }

    private static int OnXError(IntPtr display, IntPtr errorEvent)
    {
        IntPtr previous;
        lock (Gate)
        {
            if (LastErrors.ContainsKey(display))
            {
                // XErrorEvent: type, display, resource id and serial, then the error code.
                LastErrors[display] = Marshal.ReadByte(errorEvent, 32);
                return 0;
            }

            previous = s_previousHandler;
        }

        return previous == IntPtr.Zero ? 0 : Marshal.GetDelegateForFunctionPointer<XErrorHandler>(previous)(display, errorEvent);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(IntPtr handler);
}
