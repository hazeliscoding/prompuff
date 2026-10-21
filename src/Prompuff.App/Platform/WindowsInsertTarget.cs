using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Prompuff.App.Platform;

/// <summary>
/// Remembers the foreground window, hands it the foreground back with <c>SetForegroundWindow</c> while Prompuff still
/// holds it (Windows only lets the foreground app give it away), and pastes with <c>SendInput</c> and Ctrl+V.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsInsertTarget : IInsertTarget
{
    private const ushort VkReturn = 0x0D;
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLeftWindows = 0x5B;
    private const ushort VkRightWindows = 0x5C;
    private const ushort VkV = 0x56;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x2;

    // Enter picks in Quick insert, and a held modifier would turn Ctrl+V into another shortcut.
    private static readonly ushort[] KeysToLetGo = [VkReturn, VkShift, VkControl, VkMenu, VkLeftWindows, VkRightWindows];

    private IntPtr _target;

    public bool IsSupported => true;
    public bool CanPaste => true;

    public void Remember(IntPtr ownWindow)
    {
        var front = GetForegroundWindow();
        _ = GetWindowThreadProcessId(front, out var processId);
        if (front != IntPtr.Zero && front != ownWindow && processId != Environment.ProcessId)
        {
            _target = front;
        }
    }

    public async Task<bool> ReturnAsync()
    {
        var target = _target;
        _target = IntPtr.Zero;
        if (target == IntPtr.Zero || !IsWindow(target))
        {
            return false;
        }

        await WaitForKeysUpAsync();
        _ = SetForegroundWindow(target);
        return await InsertTargets.WaitUntilAsync(() => GetForegroundWindow() == target, 500);
    }

    public async Task<bool> PasteAsync()
    {
        await WaitForKeysUpAsync();
        Input[] inputs = [Key(VkControl, up: false), Key(VkV, up: false), Key(VkV, up: true), Key(VkControl, up: true)];
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    public void RequestPasteAccess()
    {
    }

    private static Task<bool> WaitForKeysUpAsync() =>
        InsertTargets.WaitUntilAsync(() => KeysToLetGo.All(key => (GetAsyncKeyState(key) & 0x8000) == 0));

    private static Input Key(ushort virtualKey, bool up) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = up ? KeyEventKeyUp : 0 } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    // The mouse member is the largest, so it sets the size SendInput checks.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}
