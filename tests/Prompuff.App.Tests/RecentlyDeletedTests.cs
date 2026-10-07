using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.Services;

namespace Prompuff.App.Tests;

public class RecentlyDeletedTests
{
    [AvaloniaFact]
    public async Task A_deleted_prompt_can_be_restored_from_Recently_deleted()
    {
        await using var app = await AppHarness.StartAsync();
        Assert.False(app.ViewModel.Sidebar.ShowRecentlyDeleted);
        var card = app.ViewModel.Library.Items[0];

        await card.OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        var deleting = editor.DeleteCommand.ExecuteAsync(null);
        await app.SettleAsync();
        app.Screenshot("dialog-delete-prompt");
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await deleting;
        await app.SettleAsync();

        Assert.Equal(5, app.ViewModel.Sidebar.All.Count);
        Assert.True(app.ViewModel.Sidebar.ShowRecentlyDeleted);
        Assert.Equal(1, app.ViewModel.Sidebar.RecentlyDeleted.Count);

        await app.ViewModel.Sidebar.RecentlyDeleted.SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var library = app.ViewModel.Library;
        Assert.True(library.IsDeletedView);
        Assert.Equal("Recently deleted", library.Title);
        var deleted = Assert.Single(library.Items);
        Assert.Equal(card.Title, deleted.Title);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("30 days left", deleted.EditedLabel);
        app.Screenshot("library-recently-deleted");
        library.IsListLayout = true;
        await app.SettleAsync();
        app.Screenshot("library-recently-deleted-list");
        library.IsCardsLayout = true;

        await deleted.RestoreCommand.ExecuteAsync(null);
        await app.SettleAsync();

        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Equal(0, app.ViewModel.Sidebar.RecentlyDeleted.Count);
        Assert.Empty(library.Items);
        app.Screenshot("library-recently-deleted-empty");
    }

    [AvaloniaFact]
    public async Task Emptying_Recently_deleted_removes_prompts_for_good()
    {
        await using var app = await AppHarness.StartAsync();
        var prompts = app.Get<PromptService>();
        var gone = app.ViewModel.Library.Items.Take(2).Select(card => card.Id).ToList();
        foreach (var id in gone)
        {
            await prompts.DeleteAsync(id);
        }

        await app.ViewModel.Sidebar.RecentlyDeleted.SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var library = app.ViewModel.Library;
        Assert.Equal(2, library.Items.Count);
        Assert.True(library.CanEmpty);

        var emptying = library.EmptyRecentlyDeletedCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.NotNull(app.ViewModel.Dialogs.Current);
        app.Screenshot("dialog-empty-recently-deleted");
        app.ViewModel.Dialogs.Current!.ConfirmCommand.Execute(null);
        await emptying;
        await app.SettleAsync();

        Assert.Empty(library.Items);
        Assert.False(library.CanEmpty);
        Assert.Equal(0, app.ViewModel.Sidebar.RecentlyDeleted.Count);
        foreach (var id in gone)
        {
            Assert.Null(await prompts.GetAsync(id));
        }
    }
}
