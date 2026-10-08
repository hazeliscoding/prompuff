using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Headless;
using Prompuff.App.ViewModels;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Application.Settings;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Library_shows_the_imported_prompts()
    {
        await using var app = await AppHarness.StartAsync();

        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(6, app.ViewModel.Library.Items.Count);
        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Equal(3, app.ViewModel.Sidebar.Favorites.Count);
        Assert.Equal(["Design", "Engineering", "Handoffs", "Writing & docs"], app.ViewModel.Sidebar.Collections.Select(c => c.Name));
        Assert.Contains(app.ViewModel.Sidebar.Tags, tag => tag.Name == "angular");
        app.Screenshot("library-cards-dark");

        app.ViewModel.Library.IsListLayout = true;
        await app.SettleAsync();
        app.Screenshot("library-list-dark");
    }

    [AvaloniaFact]
    public async Task Filters_and_search_narrow_the_library()
    {
        await using var app = await AppHarness.StartAsync();

        await app.ViewModel.Sidebar.Favorites.SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("Favorites", app.ViewModel.Library.Title);
        Assert.Equal(3, app.ViewModel.Library.Items.Count);

        var design = app.ViewModel.Sidebar.Collections.Single(c => c.Name == "Design");
        await design.SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(2, app.ViewModel.Library.Items.Count);
        Assert.True(design.IsActive);

        await app.ViewModel.Library.ClearFilterCommand.ExecuteAsync(null);
        app.ViewModel.SearchText = "assumption";
        await app.SettleAsync(400);
        Assert.Equal(["Bug Reproduction Request"], app.ViewModel.Library.Items.Select(item => item.Title));
        app.Screenshot("library-search");
    }

    [AvaloniaFact]
    public async Task A_prompt_renders_its_variables_and_copies_the_result()
    {
        await using var app = await AppHarness.StartAsync();
        var card = app.ViewModel.Library.Items.Single(item => item.Title == "Angular Upgrade Planner");

        await card.OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal(["repo_name", "target_version", "package_manager", "tone"], editor.Variables.Select(v => v.Name));
        app.Screenshot("editor-edit-dark");

        editor.Tab = EditorTab.Render;
        editor.Variables[0].Value = "mv-dashboard";
        editor.Variables[1].Value = "22";
        editor.Variables[2].Value = "pnpm";
        await app.SettleAsync();
        Assert.Equal("3 of 4 filled", editor.FillLabel);
        Assert.Contains(editor.Segments, segment => segment is { Kind: TemplateSegmentKind.MissingVariable, Text: "{{tone}}" });
        app.Screenshot("editor-render-dark");

        await editor.CopyRenderedCommand.ExecuteAsync(null);
        var copied = await app.Get<IClipboardService>().GetTextAsync();
        Assert.StartsWith("You are a senior Angular engineer planning an upgrade of mv-dashboard to Angular 22.", copied);
        Assert.Contains("Package manager: pnpm. Keep the tone {{tone}}.", copied);
    }

    [AvaloniaFact]
    public async Task Editing_builds_a_history_that_can_be_restored()
    {
        await using var app = await AppHarness.StartAsync();
        var card = app.ViewModel.Library.Items.Single(item => item.Title == "README Cleanup");
        await card.OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);

        editor.Body += "\n\nInspect the repository before rewriting anything.";
        Assert.True(editor.IsDirty);
        await editor.SaveCommand.ExecuteAsync(null);
        editor.Title = "README Cleanup Prompt";
        await editor.SaveCommand.ExecuteAsync(null);
        await editor.SaveCommand.ExecuteAsync(null);
        await app.SettleAsync();

        Assert.Equal(3, editor.Versions.Count);
        Assert.False(editor.IsDirty);
        editor.Tab = EditorTab.History;
        editor.SelectedVersion = editor.Versions.Single(version => version.Number == 2);
        await app.SettleAsync();
        Assert.Contains(editor.DiffLines, line => line is { IsAdded: true, Text: "Inspect the repository before rewriting anything." });
        app.Screenshot("editor-history-dark");

        editor.SelectedVersion = editor.Versions.Single(version => version.Number == 1);
        await editor.RestoreCommand.ExecuteAsync(null);
        await app.SettleAsync();

        Assert.Equal(4, editor.Versions.Count);
        Assert.Equal("README Cleanup", editor.Title);
        Assert.DoesNotContain("Inspect the repository", editor.Body);
        Assert.Equal("Restored v1", editor.Versions[0].Note);
    }

    [AvaloniaFact]
    public async Task A_new_prompt_survives_a_restart()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-ui-tests", Guid.NewGuid().ToString("N"));
        var first = await AppHarness.StartAsync(importSamples: false, folder: folder);
        Assert.True(first.ViewModel.Library.IsLibraryEmpty);
        first.Screenshot("library-empty-dark");

        await first.ViewModel.NewPromptCommand.ExecuteAsync(null);
        await first.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(first.ViewModel.CurrentPage);
        editor.Title = "Release notes drafter";
        editor.Body = "Draft release notes for {{version}} in a {{tone}} voice.";
        editor.Notes = "Short sections beat long ones.";
        editor.NewTag = "Writing, release";
        await editor.AddTagCommand.ExecuteAsync(null);
        await editor.SaveCommand.ExecuteAsync(null);
        await first.QuitAsync();

        await using var second = await AppHarness.StartAsync(importSamples: false, folder: folder);
        var card = Assert.Single(second.ViewModel.Library.Items);
        Assert.Equal("Release notes drafter", card.Title);
        Assert.Equal(["writing", "release"], card.Tags.Select(tag => tag.Name));
        await card.OpenCommand.ExecuteAsync(null);
        await second.SettleAsync();
        var reopened = Assert.IsType<PromptEditorViewModel>(second.ViewModel.CurrentPage);
        Assert.Equal(["version", "tone"], reopened.Variables.Select(v => v.Name));
        Assert.Equal("Short sections beat long ones.", reopened.Notes);
    }

    [AvaloniaFact]
    public async Task Unsaved_edits_are_saved_when_leaving_the_editor()
    {
        await using var app = await AppHarness.StartAsync();
        var card = app.ViewModel.Library.Items.Single(item => item.Title == "Claude Code Handoff");
        await card.OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        editor.Notes = "Planning first keeps the diff reviewable.";

        await app.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        await app.SettleAsync();

        var prompt = await app.Get<PromptService>().GetAsync(editor.Id!.Value);
        Assert.Equal("Planning first keeps the diff reviewable.", prompt!.Notes);
    }

    [AvaloniaFact]
    public async Task Shortcuts_open_the_palette_and_quick_save()
    {
        await using var app = await AppHarness.StartAsync();

        // Cmd on macOS, Ctrl elsewhere, the same way the app reads shortcuts.
        var command = Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        app.Window.KeyPressQwerty(PhysicalKey.K, command);
        await app.SettleAsync();
        Assert.True(app.ViewModel.Palette.IsOpen);
        app.ViewModel.Palette.Query = "angular";
        await app.SettleAsync();
        Assert.Equal("Angular Upgrade Planner", app.ViewModel.Palette.Items[0].Label);
        app.Screenshot("palette-dark");

        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(app.ViewModel.Palette.IsOpen);

        await app.Get<IClipboardService>().SetTextAsync("Review this pull request for {{repo_name}}. Focus on behaviour changes, not style.");
        app.Window.KeyPressQwerty(PhysicalKey.S, command | RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.True(app.ViewModel.QuickSave.IsOpen);
        Assert.Equal("Review this pull request for {{repo_name}}. Focus on…", app.ViewModel.QuickSave.Title);
        app.ViewModel.QuickSave.Title = "PR review, behaviour first";
        app.ViewModel.QuickSave.Why = "Asking for one blocking change keeps it short.";
        app.Screenshot("quick-save-dark");

        await app.ViewModel.QuickSave.SaveCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.False(app.ViewModel.QuickSave.IsOpen);
        Assert.Equal(7, app.ViewModel.Sidebar.All.Count);
        app.Screenshot("toast-dark");
    }

    [AvaloniaFact]
    public async Task The_app_menu_commands_open_Settings_and_About()
    {
        await using var app = await AppHarness.StartAsync(importSamples: false);

        await app.ViewModel.OpenAboutCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        Assert.True(settings.IsAbout);

        await app.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        await app.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
    }

    [AvaloniaFact]
    public async Task Deleting_a_collection_keeps_its_prompts()
    {
        await using var app = await AppHarness.StartAsync();
        var handoffs = app.ViewModel.Sidebar.Collections.Single(c => c.Name == "Handoffs");

        var deleting = handoffs.DeleteCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.NotNull(app.ViewModel.Dialogs.Current);
        app.Screenshot("dialog-delete-collection");
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await deleting;
        await app.SettleAsync();

        Assert.DoesNotContain(app.ViewModel.Sidebar.Collections, c => c.Name == "Handoffs");
        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Contains(app.ViewModel.Sidebar.Collections, c => c is { IsUncategorized: true, Count: 1 });
    }

    [AvaloniaFact]
    public async Task Settings_and_light_theme_render()
    {
        await using var app = await AppHarness.StartAsync(theme: ThemePreference.Light);
        app.Screenshot("library-cards-light");

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        app.Screenshot("settings-appearance-light");

        settings.IsDarkTheme = true;
        settings.Select(SettingsSection.Storage);
        await app.SettleAsync();
        Assert.Contains("6 prompts", settings.StorageSummary);
        app.Screenshot("settings-storage-dark");

        settings.Select(SettingsSection.About);
        await app.SettleAsync();
        app.Screenshot("settings-about-dark");

        settings.Select(SettingsSection.Shortcuts);
        await app.SettleAsync();
        app.Screenshot("settings-shortcuts-dark");
        Assert.Equal(ThemePreference.Dark, app.Get<ISettingsStore>().Load().Theme);
    }
}
