using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P", "Ctrl+Alt+P")]
    [InlineData("alt + ctrl + p", "Ctrl+Alt+P")]
    [InlineData("Ctrl+Shift+Space", "Ctrl+Shift+Space")]
    [InlineData("Meta+Alt+7", "Alt+Meta+7")]
    [InlineData("Cmd+F5", "Meta+F5")]
    public void Hotkeys_read_and_write_as_text(string text, string stored) =>
        Assert.Equal(stored, Hotkey.Parse(text)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("P")]
    [InlineData("Shift+P")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Alt+P+Q")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Ctrl+F13")]
    public void Hotkeys_need_a_modifier_and_a_key_every_platform_can_register(string text) =>
        Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void Every_supported_key_maps_to_each_platform()
    {
        var keys = Enum.GetValues<Key>().Where(Hotkey.IsSupportedKey).Distinct().ToList();
        Assert.Equal(26 + 10 + 12 + 1, keys.Count);
        Assert.All(keys, key => Assert.NotEqual(0u, WindowsGlobalHotkeyService.VirtualKey(key)));
        Assert.All(keys, key => Assert.NotEqual(string.Empty, X11GlobalHotkeyService.KeysymName(key)));
        Assert.All(keys, key => Assert.NotEqual(uint.MaxValue, MacGlobalHotkeyService.KeyCode(key)));
        Assert.Equal(keys.Count, keys.Select(MacGlobalHotkeyService.KeyCode).Distinct().Count());
        Assert.Equal(0x50u, WindowsGlobalHotkeyService.VirtualKey(Key.P));
        Assert.Equal("p", X11GlobalHotkeyService.KeysymName(Key.P));
        Assert.Equal(0x23u, MacGlobalHotkeyService.KeyCode(Key.P));
    }

    [AvaloniaFact]
    public async Task The_default_hotkey_is_registered_at_startup_and_opens_Quick_save()
    {
        await using var app = await AppHarness.StartAsync();
        Assert.Equal(Hotkey.QuickSaveDefault, app.Hotkeys.Registered);
        await app.Get<IClipboardService>().SetTextAsync("Explain {{concept}} like I'm new to it.");

        app.Hotkeys.Press();
        await app.SettleAsync();

        Assert.True(app.ViewModel.QuickSave.IsOpen);
        Assert.Equal("Explain {{concept}} like I'm new to it.", app.ViewModel.QuickSave.Text);
        await app.ViewModel.QuickSave.SaveCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(7, app.ViewModel.Sidebar.All.Count);
    }

    [AvaloniaFact]
    public async Task After_a_hotkey_save_the_window_goes_back_where_it_was()
    {
        await using var app = await AppHarness.StartAsync();
        app.ViewModel.Settings.KeepRunningInTray = true;
        app.Window.Close();
        await app.SettleAsync();
        Assert.True(app.Window.IsInTray);
        await app.Get<IClipboardService>().SetTextAsync("Write a haiku about {{topic}}.");

        app.Hotkeys.Press();
        await app.SettleAsync();
        Assert.True(app.Window.IsVisible);
        Assert.True(app.ViewModel.QuickSave.IsOpen);
        app.Screenshot("quick-save-from-hotkey");

        await app.ViewModel.QuickSave.SaveCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.True(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);

        // A minimized window goes back to the taskbar.
        WindowActivation.BringForward(app.Window);
        app.Window.WindowState = WindowState.Minimized;
        app.Hotkeys.Press();
        await app.SettleAsync();
        Assert.NotEqual(WindowState.Minimized, app.Window.WindowState);
        app.ViewModel.QuickSave.IsOpen = false;
        await app.SettleAsync();
        Assert.Equal(WindowState.Minimized, app.Window.WindowState);
    }

    [AvaloniaFact]
    public async Task Settings_records_a_new_hotkey_from_the_keyboard()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();
        Assert.Equal(Hotkey.QuickSaveDefault.Display, settings.QuickSaveHotkey.Label);
        app.Screenshot("settings-quick-save");

        await settings.QuickSaveHotkey.StartRecordingCommand.ExecuteAsync(null);
        Assert.Null(app.Hotkeys.Registered);
        await app.SettleAsync();
        app.Screenshot("settings-quick-save-recording");

        // A key without Ctrl, Alt or Meta explains itself and keeps listening.
        app.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.True(settings.QuickSaveHotkey.IsRecording);
        Assert.True(settings.QuickSaveHotkey.Problem);

        app.Window.KeyPressQwerty(PhysicalKey.J, RawInputModifiers.Control | RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.False(settings.QuickSaveHotkey.IsRecording);
        Assert.Equal("Ctrl+Shift+J", app.Hotkeys.Registered?.ToString());
        Assert.Equal("Ctrl+Shift+J", app.Get<ISettingsStore>().Load().QuickSaveHotkey);
        Assert.False(settings.QuickSaveHotkey.Problem);

        // Esc cancels a recording and keeps the old one.
        await settings.QuickSaveHotkey.StartRecordingCommand.ExecuteAsync(null);
        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.Equal("Ctrl+Shift+J", app.Hotkeys.Registered?.ToString());
        Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);

        await settings.QuickSaveHotkey.TurnOffCommand.ExecuteAsync(null);
        Assert.Null(app.Hotkeys.Registered);
        Assert.Equal(string.Empty, app.Get<ISettingsStore>().Load().QuickSaveHotkey);
        Assert.Equal("Off", settings.QuickSaveHotkey.Label);
    }

    [AvaloniaFact]
    public async Task A_hotkey_another_app_owns_is_reported()
    {
        var hotkeys = new FakeGlobalHotkeyService();
        hotkeys.Taken.Add(Hotkey.QuickSaveDefaultText);
        var quickInsertHotkeys = new FakeGlobalHotkeyService();
        quickInsertHotkeys.Taken.Add(Hotkey.QuickInsertDefaultText);
        await using var app = await AppHarness.StartAsync(importSamples: false, hotkeys: hotkeys, quickInsertHotkeys: quickInsertHotkeys);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.QuickSave);
        await settings.ApplyHotkeysAsync();
        await app.SettleAsync();

        Assert.True(settings.QuickSaveHotkey.Problem);
        Assert.StartsWith("Another app already uses", settings.QuickSaveHotkey.Status);
        Assert.True(settings.QuickInsertHotkey.Problem);
        Assert.Equal($"Another app already uses {Hotkey.QuickInsertDefault.Display}. Pick another combination.", settings.QuickInsertHotkey.Status);
        app.Screenshot("settings-quick-save-taken");
    }

    [AvaloniaFact]
    public async Task The_Quick_insert_hotkey_opens_a_search_from_another_app()
    {
        await using var app = await AppHarness.StartAsync();
        Assert.Equal(Hotkey.QuickSaveDefault, app.Hotkeys.Registered);
        Assert.Equal(Hotkey.QuickInsertDefault, app.QuickInsertHotkeys.Registered);
        app.ViewModel.Settings.KeepRunningInTray = true;
        app.Window.Close();
        await app.SettleAsync();
        Assert.True(app.Window.IsInTray);

        app.QuickInsertHotkeys.Press();
        await app.SettleAsync();
        var quickInsert = app.ViewModel.QuickInsert;
        Assert.True(app.Window.IsVisible);
        Assert.True(quickInsert.IsOpen);
        Assert.False(app.ViewModel.QuickSave.IsOpen);
        var search = app.Window.FindControl<TextBox>("QuickInsertInput")!;
        Assert.True(search.IsFocused);
        Assert.Equal(6, quickInsert.Results.Count);
        app.Screenshot("quick-insert-from-hotkey");

        app.Window.KeyTextInput("mockup");
        await app.SettleAsync();
        Assert.Equal("mockup", quickInsert.Query);
        Assert.Equal("UI Mockup Generator", quickInsert.Selected?.Title);
        app.Screenshot("quick-insert-search");

        app.Window.KeyTextInput("zzzz");
        await app.SettleAsync();
        Assert.True(quickInsert.IsEmpty);
        app.Screenshot("quick-insert-no-match");

        // Esc closes it, and the window goes back to the tray it came from.
        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(quickInsert.IsOpen);
        Assert.True(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Switching_from_Quick_insert_to_Quick_save_keeps_the_window_until_Quick_save_closes()
    {
        await using var app = await AppHarness.StartAsync();
        app.ViewModel.Settings.KeepRunningInTray = true;
        app.Window.Close();
        await app.SettleAsync();
        await app.Get<IClipboardService>().SetTextAsync("Summarize {{doc}} in three bullets.");

        app.QuickInsertHotkeys.Press();
        await app.SettleAsync();
        Assert.True(app.ViewModel.QuickInsert.IsOpen);

        // Opening Quick save closes Quick insert, which mustn't send the window back to the tray.
        app.Hotkeys.Press();
        await app.SettleAsync();
        Assert.False(app.ViewModel.QuickInsert.IsOpen);
        Assert.True(app.ViewModel.QuickSave.IsOpen);
        Assert.True(app.Window.IsVisible);

        app.ViewModel.QuickSave.IsOpen = false;
        await app.SettleAsync();
        Assert.True(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_key_typed_before_the_search_has_focus_still_lands_in_it()
    {
        await using var app = await AppHarness.StartAsync();
        await app.ViewModel.QuickInsertFromOutsideAsync();
        await app.SettleAsync();

        // As if the key arrived while focus was still on the library's search box behind Quick insert.
        app.Window.FindControl<TextBox>("SearchBox")!.Focus();
        app.Window.KeyTextInput("r");
        app.Window.KeyTextInput("e");
        await app.SettleAsync();

        Assert.Equal("re", app.ViewModel.QuickInsert.Query);
        Assert.Equal(string.Empty, app.ViewModel.SearchText);
        Assert.True(app.Window.FindControl<TextBox>("QuickInsertInput")!.IsFocused);
    }

    [AvaloniaFact]
    public async Task Settings_records_and_turns_off_each_hotkey_on_its_own()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        var quickInsert = settings.QuickInsertHotkey;
        settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();
        Assert.Equal(Hotkey.QuickInsertDefault.Display, quickInsert.Label);

        // Recording either lets go of both, so pressing Quick save's keys reaches the recorder instead of opening it.
        await quickInsert.StartRecordingCommand.ExecuteAsync(null);
        Assert.Null(app.Hotkeys.Registered);
        Assert.Null(app.QuickInsertHotkeys.Registered);
        Assert.Same(quickInsert, settings.RecordingHotkey);

        app.Window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Alt);
        await app.SettleAsync();
        Assert.True(quickInsert.IsRecording);
        Assert.True(quickInsert.Problem);
        Assert.StartsWith("Quick save already uses", quickInsert.Status);
        Assert.False(app.ViewModel.QuickSave.IsOpen);
        app.Screenshot("settings-quick-insert-clash");

        app.Window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.False(quickInsert.IsRecording);
        Assert.Equal("Ctrl+Alt+Shift+I", app.QuickInsertHotkeys.Registered?.ToString());
        Assert.Equal(Hotkey.QuickSaveDefault, app.Hotkeys.Registered);
        var saved = app.Get<ISettingsStore>().Load();
        Assert.Equal("Ctrl+Alt+Shift+I", saved.QuickInsertHotkey);
        Assert.Equal(Hotkey.QuickSaveDefaultText, saved.QuickSaveHotkey);

        await quickInsert.TurnOffCommand.ExecuteAsync(null);
        Assert.Null(app.QuickInsertHotkeys.Registered);
        Assert.Equal(Hotkey.QuickSaveDefault, app.Hotkeys.Registered);
        Assert.Equal(string.Empty, app.Get<ISettingsStore>().Load().QuickInsertHotkey);
        Assert.Equal("Off", quickInsert.Label);
        app.Screenshot("settings-quick-insert-off");

        await quickInsert.TurnOnCommand.ExecuteAsync(null);
        Assert.Equal(Hotkey.QuickInsertDefault, app.QuickInsertHotkeys.Registered);
        Assert.Equal(Hotkey.QuickInsertDefaultText, app.Get<ISettingsStore>().Load().QuickInsertHotkey);
    }

    [AvaloniaFact]
    public async Task Where_hotkeys_are_impossible_Settings_offers_a_desktop_shortcut()
    {
        var hotkeys = new FakeGlobalHotkeyService { IsSupported = false, UnsupportedReason = "Wayland doesn't let apps listen for keys while another app has focus, so bind a shortcut in your desktop's settings instead." };
        await using var app = await AppHarness.StartAsync(importSamples: false, hotkeys: hotkeys);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();

        Assert.False(settings.IsHotkeySupported);
        Assert.EndsWith(" --quick-save", settings.QuickSaveCommand);
        await settings.CopyQuickSaveCommandCommand.ExecuteAsync(null);
        Assert.Equal(settings.QuickSaveCommand, await app.Get<IClipboardService>().GetTextAsync());
        app.Screenshot("settings-quick-save-wayland");
    }
}

/// <summary>
/// The real system-wide hotkeys. X11 runs whenever a display is available (CI runs the Linux tests under Xvfb).
/// Windows registers for real only with PROMPUFF_HOTKEY_TESTS=1, because it grabs keys on the machine running the
/// tests, and it presses the keys too.
/// </summary>
public class SystemHotkeyTests
{
    private static readonly Hotkey Probe = new(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, Key.F11);

    // Not F12: Windows keeps it for debuggers.
    private static readonly Hotkey OtherProbe = new(KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift, Key.F10);

    [Fact]
    public async Task X11_grabs_the_hotkey_and_hears_it()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || GlobalHotkeys.IsWaylandSession())
        {
            Assert.Skip("Needs an X11 display, such as Xvfb.");
        }

        using var first = new X11GlobalHotkeyService();
        using var second = new X11GlobalHotkeyService();
        Assert.True(await first.RegisterAsync(Probe));

        // Another client can't grab the same keys: X answers BadAccess, and the handler turns that into false.
        Assert.False(await second.RegisterAsync(Probe));

        var pressed = new TaskCompletionSource();
        var firstPresses = 0;
        first.Pressed += (_, _) =>
        {
            Interlocked.Increment(ref firstPresses);
            pressed.TrySetResult();
        };
        XTest.Press("Control_L", "Alt_L", "Shift_L", "F11");
        Assert.Same(pressed.Task, await Task.WhenAny(pressed.Task, Task.Delay(TimeSpan.FromSeconds(5))));

        // Quick save and Quick insert each have an instance, and each hears only its own keys.
        Assert.True(await second.RegisterAsync(OtherProbe));
        var secondPressed = new TaskCompletionSource();
        second.Pressed += (_, _) => secondPressed.TrySetResult();
        XTest.Press("Control_L", "Alt_L", "Shift_L", "F10");
        Assert.Same(secondPressed.Task, await Task.WhenAny(secondPressed.Task, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(1, firstPresses);

        Assert.True(await first.RegisterAsync(null));
        Assert.True(await second.RegisterAsync(Probe));
    }

    [Fact]
    public async Task Windows_registers_the_hotkey_and_hears_it()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("PROMPUFF_HOTKEY_TESTS") != "1")
        {
            Assert.Skip("Set PROMPUFF_HOTKEY_TESTS=1 on Windows to grab and press real keys.");
        }

        using var first = new WindowsGlobalHotkeyService();
        using var second = new WindowsGlobalHotkeyService();
        Assert.True(await first.RegisterAsync(Probe));
        Assert.False(await second.RegisterAsync(Probe));

        var pressed = new TaskCompletionSource();
        var firstPresses = 0;
        first.Pressed += (_, _) =>
        {
            Interlocked.Increment(ref firstPresses);
            pressed.TrySetResult();
        };
        Win32Input.Press(0x11, 0x12, 0x10, 0x7A); // Ctrl, Alt, Shift, F11
        Assert.Same(pressed.Task, await Task.WhenAny(pressed.Task, Task.Delay(TimeSpan.FromSeconds(5))));

        // Quick save and Quick insert each have an instance, and each hears only its own keys.
        Assert.True(await second.RegisterAsync(OtherProbe));
        var secondPressed = new TaskCompletionSource();
        second.Pressed += (_, _) => secondPressed.TrySetResult();
        Win32Input.Press(0x11, 0x12, 0x10, 0x79); // Ctrl, Alt, Shift, F10
        Assert.Same(secondPressed.Task, await Task.WhenAny(secondPressed.Task, Task.Delay(TimeSpan.FromSeconds(5))));
        Assert.Equal(1, firstPresses);

        Assert.True(await first.RegisterAsync(null));
    }

    private static class XTest
    {
        public static void Press(params string[] keysyms)
        {
            var display = XOpenDisplay(IntPtr.Zero);
            Assert.NotEqual(IntPtr.Zero, display);
            try
            {
                var codes = keysyms.Select(name => XKeysymToKeycode(display, XStringToKeysym(name))).ToList();
                foreach (var code in codes)
                {
                    XTestFakeKeyEvent(display, code, true, 0);
                }

                foreach (var code in Enumerable.Reverse(codes))
                {
                    XTestFakeKeyEvent(display, code, false, 0);
                }

                XFlush(display);
            }
            finally
            {
                XCloseDisplay(display);
            }
        }

        [DllImport("libX11.so.6")]
        private static extern IntPtr XOpenDisplay(IntPtr name);

        [DllImport("libX11.so.6")]
        private static extern int XCloseDisplay(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern int XFlush(IntPtr display);

        [DllImport("libX11.so.6")]
        private static extern ulong XStringToKeysym(string name);

        [DllImport("libX11.so.6")]
        private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);

        [DllImport("libXtst.so.6")]
        private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);
    }

    private static class Win32Input
    {
        private const uint KeyUp = 0x2;

        public static void Press(params byte[] virtualKeys)
        {
            foreach (var key in virtualKeys)
            {
                keybd_event(key, 0, 0, UIntPtr.Zero);
            }

            foreach (var key in virtualKeys.Reverse())
            {
                keybd_event(key, 0, KeyUp, UIntPtr.Zero);
            }
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    }
}
