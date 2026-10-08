using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Prompuff.App.ViewModels;
using Prompuff.Application.Services;

namespace Prompuff.App.Tests;

public class KeyboardTests
{
    private static void Press(AppHarness app, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None) =>
        app.Window.KeyPressQwerty(key, modifiers);

    [AvaloniaFact]
    public async Task Arrow_keys_move_through_the_cards_and_Enter_opens_one()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;

        Press(app, PhysicalKey.ArrowDown);
        await app.SettleAsync();
        Assert.Same(library.Items[0], library.CurrentItem);
        Assert.True(library.Items[0].IsCurrent);

        Press(app, PhysicalKey.ArrowRight);
        Press(app, PhysicalKey.ArrowRight);
        await app.SettleAsync();
        Assert.Same(library.Items[2], library.CurrentItem);
        var focused = app.Window.FocusManager!.GetFocusedElement() as Control;
        Assert.Same(library.Items[2], focused?.DataContext);
        app.Screenshot("library-keyboard-focus");

        Press(app, PhysicalKey.ArrowLeft);
        await app.SettleAsync();
        Assert.Same(library.Items[1], library.CurrentItem);

        Press(app, PhysicalKey.End);
        await app.SettleAsync();
        Assert.Same(library.Items[^1], library.CurrentItem);
        Press(app, PhysicalKey.Home);
        await app.SettleAsync();
        Assert.Same(library.Items[0], library.CurrentItem);

        // Down moves a whole row of cards.
        Press(app, PhysicalKey.ArrowDown);
        await app.SettleAsync();
        Assert.True(library.Items.IndexOf(library.CurrentItem!) > 1);

        var title = library.CurrentItem!.Title;
        Press(app, PhysicalKey.Enter);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(title, editor.Title);

        // Esc goes back, and the cursor is still on the prompt that was open.
        Press(app, PhysicalKey.Escape);
        await app.SettleAsync();
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(title, library.CurrentItem?.Title);
    }

    [AvaloniaFact]
    public async Task A_prompt_can_be_found_edited_rendered_and_copied_without_the_mouse()
    {
        await using var app = await AppHarness.StartAsync();
        var command = Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

        // Find and open.
        Press(app, PhysicalKey.F, command);
        await app.SettleAsync();
        app.Window.KeyTextInput("angular");
        Press(app, PhysicalKey.Enter);
        await app.SettleAsync(400);
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("Angular Upgrade Planner", editor.Title);

        // The cursor waits at the top of the body.
        app.Window.KeyTextInput("Context: monorepo. ");
        await app.SettleAsync();
        Assert.StartsWith("Context: monorepo. You are a senior Angular engineer", editor.Body);
        Press(app, PhysicalKey.S, command);
        await app.SettleAsync();
        Assert.False(editor.IsDirty);
        Assert.Equal(2, editor.CurrentVersion);

        // Render, fill the first variable, and copy.
        Press(app, PhysicalKey.Enter, command);
        await app.SettleAsync();
        Assert.Equal(EditorTab.Render, editor.Tab);
        app.Window.KeyTextInput("acme");
        await app.SettleAsync();
        Press(app, PhysicalKey.Enter, command);
        await app.SettleAsync();
        var copied = await app.Get<Application.Interfaces.IClipboardService>().GetTextAsync();
        Assert.StartsWith("Context: monorepo. You are a senior Angular engineer planning an upgrade of acme to Angular", copied);
        app.Screenshot("editor-render-keyboard");

        Press(app, PhysicalKey.Escape);
        await app.SettleAsync();
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
    }

    [AvaloniaFact]
    public async Task List_rows_move_with_up_and_down()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        library.IsListLayout = true;
        await app.SettleAsync();

        Press(app, PhysicalKey.ArrowDown);
        Press(app, PhysicalKey.ArrowDown);
        Press(app, PhysicalKey.ArrowRight);
        await app.SettleAsync();
        Assert.Same(library.Items[1], library.CurrentItem);
        app.Screenshot("library-list-keyboard-focus");

        Press(app, PhysicalKey.ArrowUp);
        await app.SettleAsync();
        Assert.Same(library.Items[0], library.CurrentItem);
    }

    [AvaloniaFact]
    public async Task Typing_jumps_to_a_title()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        Press(app, PhysicalKey.ArrowDown);
        await app.SettleAsync();

        app.Window.KeyTextInput("r");
        await app.SettleAsync();
        Assert.Equal("README Cleanup", library.CurrentItem?.Title);

        // A second word starts after the type-ahead pause.
        await Task.Delay(LibraryViewModel.TypeAheadReset);
        app.Window.KeyTextInput("u");
        app.Window.KeyTextInput("i");
        await app.SettleAsync();
        Assert.Equal("UI Mockup Generator", library.CurrentItem?.Title);

        // The same letter again cycles through titles that start with it.
        await Task.Delay(LibraryViewModel.TypeAheadReset);
        app.Window.KeyTextInput("d");
        await app.SettleAsync();
        Assert.Equal("Design System Generator", library.CurrentItem?.Title);
        Assert.False(library.TypeAhead("x"));
    }

    [AvaloniaFact]
    public async Task Delete_asks_first_and_moves_the_cursor_to_the_next_card()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        Press(app, PhysicalKey.ArrowDown);
        Press(app, PhysicalKey.ArrowRight);
        await app.SettleAsync();
        var doomed = library.CurrentItem!;
        var next = library.Items[2];

        Press(app, PhysicalKey.Delete);
        await app.SettleAsync();
        Assert.NotNull(app.ViewModel.Dialogs.Current);
        app.Screenshot("library-keyboard-delete");

        // Enter confirms the dialog, not the card behind it.
        Press(app, PhysicalKey.Enter);
        await app.SettleAsync(400);
        Assert.Null(app.ViewModel.Dialogs.Current);
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(5, library.Items.Count);
        Assert.DoesNotContain(library.Items, item => item.Id == doomed.Id);
        Assert.Equal(next.Id, library.CurrentItem?.Id);
        Assert.NotNull((await app.Get<PromptService>().GetAsync(doomed.Id))?.DeletedAt);

        // Esc cancels, and nothing is deleted.
        Press(app, PhysicalKey.Delete);
        await app.SettleAsync();
        Press(app, PhysicalKey.Escape);
        await app.SettleAsync();
        Assert.Equal(5, library.Items.Count);
    }

    [AvaloniaFact]
    public async Task The_search_box_hands_off_to_the_results()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        var command = Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

        Press(app, PhysicalKey.F, command);
        await app.SettleAsync();
        app.Window.KeyTextInput("design");
        await app.SettleAsync(400);
        Assert.NotEmpty(library.Items);

        Press(app, PhysicalKey.ArrowDown);
        await app.SettleAsync();
        Assert.Same(library.Items[0], library.CurrentItem);
        Assert.Same(library.Items[0], (app.Window.FocusManager!.GetFocusedElement() as Control)?.DataContext);

        // Enter right after typing opens the best match once the search catches up.
        Press(app, PhysicalKey.F, command);
        await app.SettleAsync();
        app.ViewModel.SearchText = "readme";
        Press(app, PhysicalKey.Enter);
        await app.SettleAsync(400);
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("README Cleanup", editor.Title);
    }
}
