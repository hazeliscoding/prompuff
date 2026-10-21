using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Remembers <c>NSWorkspace</c>'s frontmost app, activates it again, and pastes with a ⌘V CGEvent. Posting key events
/// needs Accessibility access, which the person grants in System Settings; until then the result is only copied.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacInsertTarget : IInsertTarget
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const ushort KeyV = 9;
    private const ushort KeyReturn = 36;
    private const ulong CommandFlag = 0x100000;
    private const ulong ModifierFlags = 0x20000 | 0x40000 | 0x80000 | 0x100000; // Shift, Control, Option, Command
    private const int HidSystemState = 1;
    private const int HidEventTap = 0;

    // A retained NSRunningApplication, or zero.
    private IntPtr _target;

    public bool IsSupported => true;
    public bool CanPaste => AXIsProcessTrusted();

    public void Remember(IntPtr ownWindow)
    {
        var workspace = Send(Class("NSWorkspace"), Selector("sharedWorkspace"));
        var front = Send(workspace, Selector("frontmostApplication"));
        if (front == IntPtr.Zero || SendInt(front, Selector("processIdentifier")) == Environment.ProcessId)
        {
            return;
        }

        Release(_target);
        _target = Send(front, Selector("retain"));
    }

    public async Task<bool> ReturnAsync()
    {
        var target = _target;
        _target = IntPtr.Zero;
        if (target == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            await WaitForKeysUpAsync();

            // Since macOS 14 the active app hands activation over before the other one asks for it.
            var application = Send(Class("NSApplication"), Selector("sharedApplication"));
            if (SendByte(application, Selector("respondsToSelector:"), Selector("yieldActivationToApplication:")) != 0)
            {
                _ = Send(application, Selector("yieldActivationToApplication:"), target);
            }

            _ = SendByte(target, Selector("activateWithOptions:"), (nuint)0);
            return await InsertTargets.WaitUntilAsync(() => SendByte(target, Selector("isActive")) != 0, 500);
        }
        finally
        {
            Release(target);
        }
    }

    public async Task<bool> PasteAsync()
    {
        if (!AXIsProcessTrusted())
        {
            return false;
        }

        await WaitForKeysUpAsync();
        foreach (var down in new[] { true, false })
        {
            var keyEvent = CGEventCreateKeyboardEvent(IntPtr.Zero, KeyV, down);
            if (keyEvent == IntPtr.Zero)
            {
                return false;
            }

            CGEventSetFlags(keyEvent, CommandFlag);
            CGEventPost(HidEventTap, keyEvent);
            CFRelease(keyEvent);
        }

        return true;
    }

    /// <summary>Shows the system's Accessibility prompt, which offers to open System Settings.</summary>
    public void RequestPasteAccess()
    {
        var services = NativeLibrary.Load(ApplicationServices);
        var foundation = NativeLibrary.Load(CoreFoundation);
        var keys = new[] { Marshal.ReadIntPtr(NativeLibrary.GetExport(services, "kAXTrustedCheckOptionPrompt")) };
        var values = new[] { Marshal.ReadIntPtr(NativeLibrary.GetExport(foundation, "kCFBooleanTrue")) };
        var options = CFDictionaryCreate(IntPtr.Zero, keys, values, 1,
            NativeLibrary.GetExport(foundation, "kCFTypeDictionaryKeyCallBacks"),
            NativeLibrary.GetExport(foundation, "kCFTypeDictionaryValueCallBacks"));
        _ = AXIsProcessTrustedWithOptions(options);
        CFRelease(options);
    }

    // Enter picks in Quick insert, and a held modifier would turn ⌘V into another shortcut.
    private static Task<bool> WaitForKeysUpAsync() => InsertTargets.WaitUntilAsync(() =>
        (CGEventSourceFlagsState(HidSystemState) & ModifierFlags) == 0 && !CGEventSourceKeyState(HidSystemState, KeyReturn));

    private static IntPtr Class(string name) => objc_getClass(name);

    private static IntPtr Selector(string name) => sel_registerName(name);

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            _ = Send(instance, Selector("release"));
        }
    }

    [DllImport(ObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern int SendInt(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendByte(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendByte(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern byte SendByte(IntPtr receiver, IntPtr selector, nuint argument);

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(CoreGraphics)]
    private static extern void CGEventSetFlags(IntPtr keyEvent, ulong flags);

    [DllImport(CoreGraphics)]
    private static extern void CGEventPost(int tap, IntPtr keyEvent);

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventSourceFlagsState(int stateId);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGEventSourceKeyState(int stateId, ushort key);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values, nint count, IntPtr keyCallBacks, IntPtr valueCallBacks);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr instance);
}
