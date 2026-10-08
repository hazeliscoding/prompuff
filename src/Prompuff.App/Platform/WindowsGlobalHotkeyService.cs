using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Input;

namespace Prompuff.App.Platform;

/// <summary>
/// <c>RegisterHotKey</c> on a thread of its own. The hotkey isn't tied to a window, so Windows posts WM_HOTKEY to that
/// thread's queue, and its message loop raises <see cref="Pressed"/>. Registering and unregistering happen on the same
/// thread, as Windows requires.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalHotkeyService : IGlobalHotkeyService
{
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint WmReregister = 0x8000 + 0x50; // WM_APP + 'P'
    private const uint PmNoRemove = 0;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 0x5046;

    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _gate = new();
    private (Hotkey? Hotkey, TaskCompletionSource<bool> Done)? _request;
    private uint _threadId;
    private bool _disposed;

    public WindowsGlobalHotkeyService()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Prompuff hotkey" };
        _thread.Start();
    }

    public bool IsSupported => true;

    public string? UnsupportedReason => null;

    public event EventHandler? Pressed;

    public async Task<bool> RegisterAsync(Hotkey? hotkey)
    {
        await _started.Task;
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _request?.Done.TrySetResult(false);
            _request = (hotkey, done);
        }

        if (!PostThreadMessage(_threadId, WmReregister, IntPtr.Zero, IntPtr.Zero))
        {
            return false;
        }

        return await done.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started.Task.IsCompleted)
        {
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }

    internal static uint VirtualKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (uint)('A' + (key - Key.A)),
        >= Key.D0 and <= Key.D9 => (uint)('0' + (key - Key.D0)),
        >= Key.F1 and <= Key.F12 => 0x70 + (uint)(key - Key.F1),
        Key.Space => 0x20,
        _ => 0,
    };

    private void Run()
    {
        _threadId = GetCurrentThreadId();

        // The first Peek creates the thread's message queue, so posted messages can't be lost.
        PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove);
        _started.TrySetResult();

        var registered = false;
        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.Message == WmHotkey && message.WParam == HotkeyId)
            {
                Pressed?.Invoke(this, EventArgs.Empty);
            }
            else if (message.Message == WmReregister)
            {
                (Hotkey? Hotkey, TaskCompletionSource<bool> Done)? request;
                lock (_gate)
                {
                    request = _request;
                    _request = null;
                }

                if (request is not { } pending)
                {
                    continue;
                }

                if (registered)
                {
                    UnregisterHotKey(IntPtr.Zero, HotkeyId);
                    registered = false;
                }

                if (pending.Hotkey is { } hotkey)
                {
                    registered = RegisterHotKey(IntPtr.Zero, HotkeyId, Modifiers(hotkey.Modifiers) | ModNoRepeat, VirtualKey(hotkey.Key));
                    pending.Done.TrySetResult(registered);
                }
                else
                {
                    pending.Done.TrySetResult(true);
                }
            }
        }

        if (registered)
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    private static uint Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Alt) ? ModAlt : 0)
        | (modifiers.HasFlag(KeyModifiers.Control) ? ModControl : 0)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? ModShift : 0)
        | (modifiers.HasFlag(KeyModifiers.Meta) ? ModWin : 0);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out Msg message, IntPtr window, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
