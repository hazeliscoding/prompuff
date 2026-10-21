using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
