using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Remembers the active window, asks for it back through <see cref="X11Activation"/>, and pastes with XTest and Ctrl+V.
/// Each step opens a display connection of its own, so it never touches Avalonia's.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class X11InsertTarget : IInsertTarget
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";

    private static readonly Lazy<bool> HasXTest = new(() => NativeLibrary.TryLoad(LibXtst, out _));

    private IntPtr _target;

    public bool IsSupported => true;

    /// <summary>XTest is part of every X desktop, but a minimal system might lack the library.</summary>
    public bool CanPaste => HasXTest.Value;

    public void Remember(IntPtr ownWindow)
    {
        X11Activation.WithDisplay(display =>
        {
            var front = X11Activation.ActiveWindow(display);
            if (front != IntPtr.Zero && front != ownWindow)
            {
                _target = front;
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
        return await X11Activation.WithDisplayAsync(async display =>
        {
            X11Activation.Activate(display, target);
            return X11Errors.Last(display) == 0
                   && await InsertTargets.WaitUntilAsync(() => X11Activation.ActiveWindow(display) == target, 500);
        });
    }

    public async Task<bool> PasteAsync()
    {
        if (!CanPaste)
        {
            return false;
        }

        await WaitForKeysUpAsync();
        return X11Activation.WithDisplay(display =>
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

    // Enter picks in Quick insert, and a held modifier would turn Ctrl+V into another shortcut.
    private static Task<bool> WaitForKeysUpAsync() => X11Activation.WithDisplayAsync(display =>
    {
        var keys = new byte[32];
        return InsertTargets.WaitUntilAsync(() =>
        {
            _ = XQueryKeymap(display, keys);
            return keys.All(bits => bits == 0);
        });
    });

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
