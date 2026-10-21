using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.App.Tests;

/// <summary>Finding, picking and filling in a prompt in Quick insert, from the keyboard alone.</summary>
public class QuickInsertTests
{
    private static async Task<Guid> IdOf(AppHarness app, string title) =>
        (await app.Get<IPromptSearch>().SearchAsync(new PromptQuery())).Single(prompt => prompt.Title == title).Id;

    private static async Task OpenAndSearchAsync(AppHarness app, string query)
    {
        await app.ViewModel.QuickInsertFromOutsideAsync();
        await app.SettleAsync();
        app.Window.KeyTextInput(query);
        await app.SettleAsync();
    }

    private static TextBox Focused(AppHarness app) => Assert.IsType<TextBox>(app.Window.FocusManager!.GetFocusedElement());

    [AvaloniaFact]
    public async Task A_prompt_without_variables_is_copied_at_once()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Get<PromptService>().CreateAsync(new PromptContent("Tidy commit message", null, "Tidy this commit message: short subject, plain body.", null));

        await OpenAndSearchAsync(app, "tidy");
        Assert.Equal("Tidy commit message", app.ViewModel.QuickInsert.Selected?.Title);
        app.Screenshot("quick-insert-no-variables");

        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(app.ViewModel.QuickInsert.IsOpen);
        Assert.Equal("Tidy this commit message: short subject, plain body.", await app.Get<IClipboardService>().GetTextAsync());
    }

    [AvaloniaFact]
    public async Task A_prompt_with_two_variables_asks_for_them_and_remembers_them()
    {
        await using var app = await AppHarness.StartAsync();
        var id = await IdOf(app, "Claude Code Handoff");
        var cache = app.Get<RenderValuesCache>();
        (await cache.GetAsync(id))["project_name"] = "Lighthouse";
        await cache.SaveAsync(id);

        // Down and Up move through the results, wrapping round, and Enter picks.
        await OpenAndSearchAsync(app, "handoff");
        var quickInsert = app.ViewModel.QuickInsert;
        Assert.Equal("Claude Code Handoff", quickInsert.Selected?.Title);
        app.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        app.Window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal("Claude Code Handoff", quickInsert.Selected?.Title);
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();

        // One box per variable, in order of first appearance, filled with what was remembered.
        Assert.True(quickInsert.IsFilling);
        Assert.Equal(["project_name", "tone"], quickInsert.Fields.Select(field => field.Name));
        Assert.Equal("Lighthouse", quickInsert.Fields[0].Value);
        Assert.Same(quickInsert.Fields[0], Focused(app).DataContext);
        app.Screenshot("quick-insert-variables");

        app.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        await app.SettleAsync(30);
        Assert.Same(quickInsert.Fields[1], Focused(app).DataContext);
        app.Window.KeyTextInput("warm");
        await app.SettleAsync();
        Assert.Equal("warm", quickInsert.Fields[1].Value);
        app.Screenshot("quick-insert-variables-filled");

        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(quickInsert.IsOpen);
        var copied = await app.Get<IClipboardService>().GetTextAsync();
        Assert.Contains("Lighthouse", copied);
        Assert.Contains("warm", copied);
        Assert.DoesNotContain("{{", copied);

        await cache.FlushAsync();
        var remembered = await app.Get<SqliteRenderValues>().LoadAsync(id);
        Assert.Equal("Lighthouse", remembered["project_name"]);
        Assert.Equal("warm", remembered["tone"]);
    }

    [AvaloniaFact]
    public async Task An_empty_value_leaves_its_token_in_place()
    {
        await using var app = await AppHarness.StartAsync();
        await OpenAndSearchAsync(app, "handoff");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();

        app.Window.KeyTextInput("Lighthouse");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();

        var copied = await app.Get<IClipboardService>().GetTextAsync();
        Assert.Contains("Lighthouse", copied);
        Assert.Contains("{{tone}}", copied);
    }

    [AvaloniaFact]
    public async Task Esc_closes_Quick_insert_and_changes_nothing()
    {
        await using var app = await AppHarness.StartAsync();
        await app.Get<IClipboardService>().SetTextAsync("Something you copied earlier.");
        await OpenAndSearchAsync(app, "handoff");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.True(app.ViewModel.QuickInsert.IsFilling);

        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(app.ViewModel.QuickInsert.IsOpen);
        Assert.Equal("Something you copied earlier.", await app.Get<IClipboardService>().GetTextAsync());
        app.Screenshot("quick-insert-closed");

        // It opens on the search again, not the last prompt's boxes.
        await app.ViewModel.QuickInsertFromOutsideAsync();
        await app.SettleAsync();
        Assert.True(app.ViewModel.QuickInsert.IsSearching);
        Assert.True(app.Window.FindControl<TextBox>("QuickInsertInput")!.IsFocused);
    }

    /// <summary>Opens Quick insert with its hotkey from the tray, as from another app, and inserts the tidy prompt.</summary>
    private static async Task<AppHarness> InsertFromTrayAsync(FakeInsertTarget target, Action<AppHarness>? before = null)
    {
        var app = await AppHarness.StartAsync(insertTarget: target);
        await app.Get<PromptService>().CreateAsync(new PromptContent("Tidy commit message", null, "Tidy this commit message.", null));
        before?.Invoke(app);
        app.ViewModel.Settings.KeepRunningInTray = true;
        app.Window.Close();
        await app.SettleAsync();

        app.QuickInsertHotkeys.Press();
        await app.SettleAsync();
        Assert.True(target.Remembered);
        app.Window.KeyTextInput("tidy");
        await app.SettleAsync();
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.Equal("Tidy this commit message.", await app.Get<IClipboardService>().GetTextAsync());
        return app;
    }

    [AvaloniaFact]
    public async Task Inserting_steps_back_to_the_app_it_came_from_and_pastes_there()
    {
        var target = new FakeInsertTarget();
        await using var app = await InsertFromTrayAsync(target);

        Assert.Equal(1, target.ReturnCount);
        Assert.Equal(1, target.PasteCount);
        Assert.True(app.Window.IsInTray);
        Assert.False(app.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task With_Paste_after_inserting_off_the_result_is_only_copied()
    {
        var target = new FakeInsertTarget();
        await using var app = await InsertFromTrayAsync(target, app => app.ViewModel.Settings.PasteAfterInserting = false);

        Assert.False(app.Get<ISettingsStore>().Load().PasteAfterInserting);
        Assert.Equal(1, target.ReturnCount);
        Assert.Equal(0, target.PasteCount);
    }

    [AvaloniaFact]
    public async Task Nothing_is_pasted_when_the_app_doesnt_take_focus_back()
    {
        var target = new FakeInsertTarget { Returns = false };
        await using var app = await InsertFromTrayAsync(target);

        Assert.Equal(0, target.PasteCount);
        Assert.True(app.Window.IsInTray);
    }

    [AvaloniaFact]
    public async Task Esc_steps_back_without_pasting()
    {
        var target = new FakeInsertTarget();
        await using var app = await AppHarness.StartAsync(insertTarget: target);
        app.QuickInsertHotkeys.Press();
        await app.SettleAsync();

        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.Equal(1, target.ReturnCount);
        Assert.Equal(0, target.PasteCount);
    }

    [AvaloniaFact]
    public async Task Without_access_to_paste_the_result_is_copied_and_Settings_says_how_to_allow_it()
    {
        var target = new FakeInsertTarget { CanPaste = false };
        await using var app = await InsertFromTrayAsync(target);
        Assert.Equal(0, target.PasteCount);

        WindowActivation.BringForward(app.Window);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();
        Assert.True(settings.IsPasteSupported);
        Assert.True(settings.PasteNeedsAccess);

        // macOS asks for Accessibility access, with a button for its prompt; elsewhere it's a missing library.
        Assert.Equal(OperatingSystem.IsMacOS(), settings.CanAskForPasteAccess);
        Assert.Contains(OperatingSystem.IsMacOS() ? "Accessibility" : "XTest", settings.PasteAccessNote);
        app.Screenshot("settings-paste-needs-access");

        settings.AllowPastingCommand.Execute(null);
        Assert.Equal(1, target.AccessRequests);
    }

    [AvaloniaFact]
    public async Task Where_Prompuff_cant_reach_other_apps_it_only_copies()
    {
        await using var app = await AppHarness.StartAsync(insertTarget: new UnsupportedInsertTarget());
        await app.Get<PromptService>().CreateAsync(new PromptContent("Tidy commit message", null, "Tidy this commit message.", null));
        await OpenAndSearchAsync(app, "tidy");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.Equal("Tidy this commit message.", await app.Get<IClipboardService>().GetTextAsync());

        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        app.ViewModel.Settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();
        Assert.False(app.ViewModel.Settings.IsPasteSupported);
    }

    /// <summary>
    /// The real macOS target. GitHub's macOS runners grant Accessibility access, so there every native call runs for
    /// real: finding the app in front, activating it, ⌘V and the access check. Without access, Quick insert copies,
    /// presses nothing, and Settings explains how to allow pasting.
    /// </summary>
    [AvaloniaFact]
    public async Task The_real_macOS_target_pastes_with_access_and_only_copies_without()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("Uses macOS's own APIs.");
        }

        var target = InsertTargets.Create();
        if (target.CanPaste)
        {
            target.Remember(IntPtr.Zero);
            _ = await target.ReturnAsync();
            Assert.True(await target.PasteAsync());
            target.RequestPasteAccess();
            return;
        }

        await using var app = await AppHarness.StartAsync(insertTarget: target);
        await app.Get<PromptService>().CreateAsync(new PromptContent("Tidy commit message", null, "Tidy this commit message.", null));
        app.QuickInsertHotkeys.Press();
        await app.SettleAsync();
        app.Window.KeyTextInput("tidy");
        await app.SettleAsync();
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync(1500);
        Assert.Equal("Tidy this commit message.", await app.Get<IClipboardService>().GetTextAsync());

        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = app.ViewModel.Settings;
        settings.Select(SettingsSection.QuickSave);
        await app.SettleAsync();
        Assert.True(settings.PasteNeedsAccess);
        Assert.True(settings.CanAskForPasteAccess);
        Assert.Contains("Accessibility", settings.PasteAccessNote);
        app.Screenshot("settings-paste-macos-access");
    }

    [AvaloniaFact]
    public async Task Values_are_shared_with_the_Render_tab_both_ways()
    {
        await using var app = await AppHarness.StartAsync();
        var id = await IdOf(app, "Claude Code Handoff");
        await app.Get<Navigator>().OpenPromptAsync(id);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        editor.Tab = EditorTab.Render;
        editor.Variables[0].Value = "Lighthouse";
        await app.SettleAsync();

        await OpenAndSearchAsync(app, "handoff");
        app.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await app.SettleAsync();
        var quickInsert = app.ViewModel.QuickInsert;
        Assert.Equal("Lighthouse", quickInsert.Fields[0].Value);

        app.Window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        app.Window.KeyTextInput("warm");
        await app.SettleAsync();
        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();

        Assert.Equal("warm", editor.Variables[1].Value);
        Assert.Contains("warm", string.Concat(editor.Segments.Select(segment => segment.Text)));
    }
}

/// <summary>
/// The real X11 target under Xvfb, which CI runs the Linux tests in. Xvfb has no window manager, so this takes the
/// input-focus path rather than _NET_ACTIVE_WINDOW.
/// </summary>
public class SystemInsertTests
{
    private const long KeyPressMask = 1L << 0;
    private const long StructureNotifyMask = 1L << 17;
    private const int KeyPress = 2;
    private const int MapNotify = 19;
    private const int RevertToParent = 2;

    [Fact]
    public async Task X11_hands_focus_back_to_the_app_and_pastes_there()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || GlobalHotkeys.IsWaylandSession())
        {
            Assert.Skip("Needs an X11 display, such as Xvfb.");
        }

        var display = XOpenDisplay(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, display);
        X11Errors.Track(display);
        var xEvent = Marshal.AllocHGlobal(192);
        try
        {
            // Two windows stand in for the app the hotkey was pressed in and for Prompuff.
            var root = XDefaultRootWindow(display);
            var app = XCreateSimpleWindow(display, root, 0, 0, 200, 100, 0, 0, 0);
            var prompuff = XCreateSimpleWindow(display, root, 220, 0, 200, 100, 0, 0, 0);
            _ = XSelectInput(display, app, (IntPtr)(KeyPressMask | StructureNotifyMask));
            _ = XSelectInput(display, prompuff, (IntPtr)StructureNotifyMask);
            _ = XMapWindow(display, app);
            _ = XMapWindow(display, prompuff);
            var mapped = 0;
            await ReadEventsAsync(display, xEvent, () => Marshal.ReadInt32(xEvent) == MapNotify && ++mapped == 2);

            _ = XSetInputFocus(display, app, RevertToParent, IntPtr.Zero);
            _ = XSync(display, false);
            var target = new X11InsertTarget();
            target.Remember(prompuff);

            // Prompuff comes forward, then hands focus back.
            _ = XSetInputFocus(display, prompuff, RevertToParent, IntPtr.Zero);
            _ = XSync(display, false);
            Assert.True(await target.ReturnAsync());
            _ = XGetInputFocus(display, out var focus, out _);
            Assert.Equal(app, focus);

            // Ctrl+V lands in the app.
            Assert.True(target.CanPaste);
            Assert.True(await target.PasteAsync());
            var control = XKeysymToKeycode(display, XStringToKeysym("Control_L"));
            var v = XKeysymToKeycode(display, XStringToKeysym("v"));
            var pressed = new HashSet<uint>();
            Assert.True(await ReadEventsAsync(display, xEvent, () =>
            {
                // XKeyEvent: the window is at 32 and the keycode at 84.
                if (Marshal.ReadInt32(xEvent) == KeyPress && Marshal.ReadIntPtr(xEvent, 32) == app)
                {
                    pressed.Add((uint)Marshal.ReadInt32(xEvent, 84));
                }

                return pressed.Contains(control) && pressed.Contains(v);
            }), $"The app heard keycodes {string.Join(", ", pressed)}, not Ctrl ({control}) and V ({v}).");
        }
        finally
        {
            Marshal.FreeHGlobal(xEvent);
            _ = XCloseDisplay(display);
            X11Errors.Forget(display);
        }
    }

    /// <summary>Reads events until one satisfies <paramref name="done"/>, for up to five seconds.</summary>
    private static async Task<bool> ReadEventsAsync(IntPtr display, IntPtr xEvent, Func<bool> done)
    {
        for (var waited = 0; waited < 5000; waited += 20)
        {
            while (XPending(display) > 0)
            {
                _ = XNextEvent(display, xEvent);
                if (done())
                {
                    return true;
                }
            }

            await Task.Delay(20);
        }

        return false;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, ulong border, ulong background);

    [DllImport("libX11.so.6")]
    private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr eventMask);

    [DllImport("libX11.so.6")]
    private static extern int XMapWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

    [DllImport("libX11.so.6")]
    private static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, bool discard);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6")]
    private static extern ulong XStringToKeysym(string name);

    [DllImport("libX11.so.6")]
    private static extern byte XKeysymToKeycode(IntPtr display, ulong keysym);
}
