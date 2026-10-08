using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;
using Prompuff.App.Views;

namespace Prompuff.App.Tests;

public class MultiSelectTests
{
    private static RawInputModifiers Command =>
        Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public async Task Picked_prompts_can_be_tagged_moved_and_exported_together()
    {
        var files = new FakeFilePicker();
        await using var app = await AppHarness.StartAsync(files: files);
        var library = app.ViewModel.Library;

        library.StartSelectingCommand.Execute(null);
        Assert.True(library.IsSelecting);
        var picked = library.Items.Where(item => item.Title is "README Cleanup" or "Bug Reproduction Request").ToList();
        foreach (var card in picked)
        {
            await card.OpenCommand.ExecuteAsync(null);
        }

        await app.SettleAsync();
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("2 prompts selected", library.SelectionLabel);
        Assert.Equal(["Add tags…", "Remove #copilot", "Remove #review", "Remove #writing"], library.TagActions.Select(action => action.Label));
        app.Screenshot("library-multi-select");
        library.IsListLayout = true;
        await app.SettleAsync();
        app.Screenshot("library-multi-select-list");

        // Tag both.
        var tagging = library.TagActions[0].RunCommand.ExecuteAsync(null);
        await app.SettleAsync();
        app.ViewModel.Dialogs.Current!.InputText = "Batch, #triage";
        app.Screenshot("dialog-tag-selected");
        app.ViewModel.Dialogs.Current.ConfirmCommand.Execute(null);
        await tagging;
        await app.SettleAsync();
        Assert.All(library.Items.Where(item => item.IsSelected), card => Assert.Contains(card.Tags, tag => tag.Name == "batch"));
        Assert.Contains(app.ViewModel.Sidebar.Tags, tag => tag is { Name: "triage", Count: 2 });

        // Remove a tag that only one of them has.
        await library.TagActions.Single(action => action.Label == "Remove #writing").RunCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.DoesNotContain(library.Items, card => card.Tags.Any(tag => tag.Name == "writing") && card.Title == "README Cleanup");

        // Export keeps the selection; Move ends it.
        files.ExportPath = Path.Combine(app.Folder, "picked.zip");
        await library.ExportSelectedCommand.ExecuteAsync(null);
        using (var archive = ZipFile.OpenRead(files.ExportPath))
        {
            Assert.Equal(2, archive.Entries.Count);
        }

        Assert.True(library.HasSelection);
        await library.MoveActions.Single(action => action.Label == "Design").RunCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.False(library.IsSelecting);
        Assert.Equal(4, app.ViewModel.Sidebar.Collections.Single(c => c.Name == "Design").Count);
        Assert.Equal("Moved.", app.ViewModel.Toasts.Current?.Title);
    }

    [AvaloniaFact]
    public async Task Picked_prompts_are_deleted_together_and_restored_together()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        library.SelectAll();
        library.ToggleSelection(library.Items[0]);
        library.ToggleSelection(library.Items[1]);
        library.ToggleSelection(library.Items[2]);
        Assert.Equal(3, library.SelectedCount);

        var deleting = library.DeleteSelectedCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("Delete 3 prompts?", app.ViewModel.Dialogs.Current?.Title);
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await deleting;
        await app.SettleAsync();
        Assert.Equal(3, app.ViewModel.Sidebar.All.Count);
        Assert.Equal(3, app.ViewModel.Sidebar.RecentlyDeleted.Count);

        await app.ViewModel.Sidebar.RecentlyDeleted.SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        library.SelectAll();
        await app.SettleAsync();
        app.Screenshot("library-recently-deleted-select");
        await library.RestoreSelectedCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Empty(library.Items);
    }

    [AvaloniaFact]
    public async Task Keys_and_modifier_clicks_pick_prompts()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        app.Window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        await app.SettleAsync();

        app.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        app.Window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.Equal(library.Items.Take(3).Select(item => item.Id), library.Items.Where(item => item.IsSelected).Select(item => item.Id));

        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();
        Assert.False(library.IsSelecting);
        Assert.Equal(0, library.SelectedCount);
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);

        app.Window.KeyPressQwerty(PhysicalKey.A, Command);
        await app.SettleAsync();
        Assert.Equal(6, library.SelectedCount);
        app.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await app.SettleAsync();

        // Ctrl or Cmd and click picks without opening; Shift and click picks the range in between.
        Click(app, library.Items[4], Command);
        await app.SettleAsync();
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
        Assert.True(library.IsSelecting);
        Assert.True(library.Items[4].IsSelected);
        Click(app, library.Items[1], RawInputModifiers.Shift);
        await app.SettleAsync();
        Assert.Equal([1, 2, 3, 4], library.Items.Select((item, index) => (item, index)).Where(pair => pair.item.IsSelected).Select(pair => pair.index));

        // In selection mode a plain click picks too.
        Click(app, library.Items[0], RawInputModifiers.None);
        await app.SettleAsync();
        Assert.Equal(5, library.SelectedCount);
        Assert.IsType<LibraryViewModel>(app.ViewModel.CurrentPage);
    }

    private static void Click(AppHarness app, PromptCardViewModel card, RawInputModifiers modifiers)
    {
        var button = app.Window.GetVisualDescendants().OfType<LibraryView>().Single()
            .GetVisualDescendants().OfType<Button>()
            .First(candidate => candidate.DataContext == card && candidate.Classes.Contains("card"));
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height - 6), app.Window)!.Value;
        app.Window.MouseDown(point, MouseButton.Left, modifiers);
        app.Window.MouseUp(point, MouseButton.Left, modifiers);
    }
}
