using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Prompuff.App.Controls;
using Prompuff.App.Views;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.Tests;

public class LibraryScrollTests
{
    [AvaloniaFact]
    public async Task A_new_search_starts_at_its_best_match_and_a_refresh_keeps_its_place()
    {
        await using var app = await AppHarness.StartAsync();
        var library = app.ViewModel.Library;
        for (var i = 0; i < 60; i++)
        {
            await app.Get<PromptService>().CreateAsync(new PromptContent($"Filler prompt {i:00}", null, "Nothing to see here.", null));
        }

        await library.RefreshAsync();
        await app.SettleAsync();
        var scroller = app.Window.GetVisualDescendants().OfType<LibraryView>().Single()
            .GetVisualDescendants().OfType<CardGrid>().Single().FindAncestorOfType<ScrollViewer>()!;
        scroller.Offset = new Vector(0, 2000);
        await app.SettleAsync();
        Assert.Equal(2000, scroller.Offset.Y);

        // The same view, refreshed after a change, stays where it was.
        await library.RefreshAsync();
        await app.SettleAsync();
        Assert.Equal(2000, scroller.Offset.Y);

        // Another search starts from the top, where its best matches are.
        app.ViewModel.SearchText = "filler";
        await app.WaitForAsync(() => library.Items.Count == 60);
        await app.SettleAsync();
        Assert.Equal(0, scroller.Offset.Y);
        app.Screenshot("library-new-search-at-top");
    }
}
