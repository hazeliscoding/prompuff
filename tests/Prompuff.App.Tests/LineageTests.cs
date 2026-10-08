using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.Services;

namespace Prompuff.App.Tests;

public class LineageTests
{
    [AvaloniaFact]
    public async Task A_duplicate_links_back_to_its_parent_and_the_parent_lists_it()
    {
        await using var app = await AppHarness.StartAsync();
        var card = app.ViewModel.Library.Items.Single(item => item.Title == "Angular Upgrade Planner");
        await card.OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var original = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.False(original.HasParent);
        Assert.False(original.HasCopies);

        await original.DuplicateCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var copy = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("Angular Upgrade Planner (copy)", copy.Title);
        Assert.True(copy.HasParent);
        Assert.Equal("Angular Upgrade Planner", copy.Parent!.Title);
        app.Screenshot("editor-duplicated-from");

        await copy.OpenParentCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var parent = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.Equal("Angular Upgrade Planner", parent.Title);
        Assert.Equal("1 copy", parent.CopiesLabel);
        app.Screenshot("editor-copies");

        await parent.Copies.Single().OpenCommand.ExecuteAsync(null);
        await app.SettleAsync();
        Assert.Equal("Angular Upgrade Planner (copy)", Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage).Title);

        // A parent in Recently deleted still shows, but has to be restored before it opens.
        await app.Get<PromptService>().DeleteAsync(parent.Id!.Value);
        await app.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        await app.Get<Navigator>().OpenPromptAsync(copy.Id!.Value);
        await app.SettleAsync();
        var reopened = Assert.IsType<PromptEditorViewModel>(app.ViewModel.CurrentPage);
        Assert.True(reopened.Parent!.IsDeleted);
        await reopened.OpenParentCommand.ExecuteAsync(null);
        Assert.Same(reopened, app.ViewModel.CurrentPage);
        Assert.Equal("It's in Recently deleted.", app.ViewModel.Toasts.Current?.Title);
    }
}
