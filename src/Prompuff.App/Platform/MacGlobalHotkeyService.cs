using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input;
using Avalonia.Threading;

namespace Prompuff.App.Platform;

/// <summary>
/// Carbon's <c>RegisterEventHotKey</c>, which macOS still offers for exactly this and which needs no Accessibility
/// permission. The handler sits on the application event target, so presses arrive on the main thread.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacGlobalHotkeyService : IGlobalHotkeyService
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint KeyboardEventClass = 0x6B657962; // 'keyb'
    private const uint HotKeyPressedKind = 5;
    private const uint Signature = 0x5046686B; // 'PFhk'
    private const uint CmdKey = 1 << 8;
    private const uint ShiftKey = 1 << 9;
    private const uint OptionKey = 1 << 11;
    private const uint ControlKey = 1 << 12;

    // From Events.h: the ANSI layout's virtual key codes.
    private static readonly Dictionary<Key, uint> KeyCodes = new()
    {
        [Key.A] = 0x00, [Key.S] = 0x01, [Key.D] = 0x02, [Key.F] = 0x03, [Key.H] = 0x04, [Key.G] = 0x05, [Key.Z] = 0x06,
        [Key.X] = 0x07, [Key.C] = 0x08, [Key.V] = 0x09, [Key.B] = 0x0B, [Key.Q] = 0x0C, [Key.W] = 0x0D, [Key.E] = 0x0E,
        [Key.R] = 0x0F, [Key.Y] = 0x10, [Key.T] = 0x11, [Key.D1] = 0x12, [Key.D2] = 0x13, [Key.D3] = 0x14,
        [Key.D4] = 0x15, [Key.D6] = 0x16, [Key.D5] = 0x17, [Key.D9] = 0x19, [Key.D7] = 0x1A, [Key.D8] = 0x1C,
        [Key.D0] = 0x1D, [Key.O] = 0x1F, [Key.U] = 0x20, [Key.I] = 0x22, [Key.P] = 0x23, [Key.L] = 0x25, [Key.J] = 0x26,
        [Key.K] = 0x28, [Key.N] = 0x2D, [Key.M] = 0x2E, [Key.Space] = 0x31,
        [Key.F1] = 0x7A, [Key.F2] = 0x78, [Key.F3] = 0x63, [Key.F4] = 0x76, [Key.F5] = 0x60, [Key.F6] = 0x61,
        [Key.F7] = 0x62, [Key.F8] = 0x64, [Key.F9] = 0x65, [Key.F10] = 0x6D, [Key.F11] = 0x67, [Key.F12] = 0x6F,
    };

    private readonly EventHandlerProc _handler;
    private IntPtr _handlerRef;
    private IntPtr _hotKeyRef;

    public MacGlobalHotkeyService()
    {
        _handler = OnHotKey;
    }

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    public event EventHandler? Pressed;

    public Task<bool> RegisterAsync(Hotkey? hotkey) =>
        Dispatcher.UIThread.CheckAccess()
            ? Task.FromResult(Register(hotkey))
            : Dispatcher.UIThread.InvokeAsync(() => Register(hotkey)).GetTask();

    public void Dispose()
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }

        if (_handlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(_handlerRef);
            _handlerRef = IntPtr.Zero;
        }
    }

    internal static uint KeyCode(Key key) => KeyCodes.TryGetValue(key, out var code) ? code : uint.MaxValue;

    private bool Register(Hotkey? hotkey)
    {
        if (_hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(_hotKeyRef);
            _hotKeyRef = IntPtr.Zero;
        }

        if (hotkey is null)
        {
            return true;
        }

        var code = KeyCode(hotkey.Key);
        if (code == uint.MaxValue)
        {
            return false;
        }

        var target = GetApplicationEventTarget();
        if (_handlerRef == IntPtr.Zero)
        {
            var spec = new[] { new EventTypeSpec { EventClass = KeyboardEventClass, EventKind = HotKeyPressedKind } };
            if (InstallEventHandler(target, Marshal.GetFunctionPointerForDelegate(_handler), 1, spec, IntPtr.Zero, out _handlerRef) != 0)
            {
                _handlerRef = IntPtr.Zero;
                return false;
            }
        }

        var modifiers = (hotkey.Modifiers.HasFlag(KeyModifiers.Meta) ? CmdKey : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Shift) ? ShiftKey : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Alt) ? OptionKey : 0)
                        | (hotkey.Modifiers.HasFlag(KeyModifiers.Control) ? ControlKey : 0);
        var id = new EventHotKeyId { Signature = Signature, Id = 1 };

        // Fails with eventHotKeyExistsErr when another app has the same combination.
        if (RegisterEventHotKey(code, modifiers, id, target, 0, out _hotKeyRef) != 0)
        {
            _hotKeyRef = IntPtr.Zero;
            return false;
        }

        return true;
    }

    private int OnHotKey(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        Pressed?.Invoke(this, EventArgs.Empty);
        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(IntPtr target, IntPtr handler, nuint typeCount, EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
