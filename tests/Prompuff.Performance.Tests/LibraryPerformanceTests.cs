using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prompuff.App.Controls;
using Prompuff.App.Tests;
using Prompuff.App.ViewModels;
using Prompuff.App.Views;

namespace Prompuff.Performance.Tests;

/// <summary>
/// The real main window, headless, over a library of 10,000 prompts. Before v0.9 the library built a card for every
/// prompt: at 1,000 prompts that was 41,000 visuals and seven seconds a refresh, and at 10,000 it never finished.
/// </summary>
[Trait("Category", "Performance")]
public class LibraryPerformanceTests(SeededLibrary seeded)
{
    /// <summary>
    /// Card or row controls allowed to exist at once: a screen holds about 12 cards or 15 rows, and the panel keeps a
    /// few recycled ones. Building one per prompt would mean thousands.
    /// </summary>
    private const int ControlLimit = 80;

    /// <summary>Everything in the window: the sidebar, the header and a screen of cards came to about 1,400.</summary>
    private const int VisualLimit = 5_000;

    /// <summary>
    /// A refresh searches off the UI thread, then rebinds a screen of cards. It takes about 150 ms here; the bound is
    /// loose for slower CI machines and still far below the seconds a refresh took when every card was built.
    /// </summary>
    private static readonly TimeSpan RefreshLimit = TimeSpan.FromSeconds(1);

    /// <summary>Jumping a whole page rebinds about 12 cards, which takes 40–70 ms here, headless and on the CPU.</summary>
    private static readonly TimeSpan PageLimit = TimeSpan.FromMilliseconds(250);

    [AvaloniaFact]
    public async Task Ten_thousand_prompts_open_scroll_and_search_without_building_every_card()
    {
        var watch = Stopwatch.StartNew();
        await using var app = await AppHarness.StartAsync(importSamples: false, folder: seeded.CopyToNewFolder());
        Timing.Report($"opened in {watch.Elapsed.TotalMilliseconds:F0} ms");
        var library = app.ViewModel.Library;
        var view = app.Window.GetVisualDescendants().OfType<LibraryView>().Single();
        var grid = view.GetVisualDescendants().OfType<CardGrid>().Single();
        var scroller = grid.FindAncestorOfType<ScrollViewer>()!;
        Assert.Equal(seeded.Info.Prompts, library.Items.Count);
        AssertFew(view, "card");
        var visuals = app.Window.GetVisualDescendants().Count();
        Timing.Report($"{Cards(view, "card").Count} card controls, {visuals} visuals in the window, {grid.ColumnCount} columns");
        Assert.True(visuals < VisualLimit, $"The window holds {visuals} visuals.");
        app.Screenshot("perf-library-10000");

        // Scrolling a page at a time gives the cards already built other prompts rather than building more.
        var built = Cards(view, "card");
        var onScreen = built.Count;
        var pages = new List<TimeSpan>();
        for (var page = 0; page < 10; page++)
        {
            watch.Restart();
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + scroller.Viewport.Height);
            Dispatcher.UIThread.RunJobs();
            pages.Add(watch.Elapsed);
            built.UnionWith(Cards(view, "card"));
        }

        pages.Sort();
        Timing.Report($"page scroll median {pages[5].TotalMilliseconds:F1} ms; {built.Count - onScreen} more card controls over 10 pages");
        Assert.True(pages[5] < PageLimit, $"Scrolling a page took {pages[5].TotalMilliseconds:F0} ms.");
        Assert.True(built.Count - onScreen <= onScreen, $"Scrolling ten pages built {built.Count - onScreen} more cards.");
        var shown = grid.GetRealizedCards().Where(card => card.IsEffectivelyVisible).Select(card => library.Items.IndexOf((PromptCardViewModel)card.DataContext!)).ToList();
        Assert.True(shown.Min() >= 20 * grid.ColumnCount, $"Ten pages down, the first card on screen is prompt {shown.Min()}.");
        app.Screenshot("perf-library-10000-scrolled");

        // End and Home reach cards that were never built, and focus follows.
        Press(app, PhysicalKey.End);
        await app.SettleAsync();
        Assert.Same(library.Items[^1], library.CurrentItem);
        Assert.Same(library.Items[^1], Focused(app));
        AssertFew(view, "card");
        app.Screenshot("perf-library-10000-end");
        Press(app, PhysicalKey.Home);
        await app.SettleAsync();
        Assert.Same(library.Items[0], Focused(app));

        // A refresh, which follows every change to the library, keeps the cursor on its prompt.
        library.SetCurrent(library.Items[5_000], focus: true);
        await app.SettleAsync();
        var cursor = library.CurrentItem!.Id;
        var refresh = await Timing.MedianAsync(async () =>
        {
            await library.RefreshAsync();
            Dispatcher.UIThread.RunJobs();
        }, runs: 5);
        Timing.Report($"refresh median {refresh.TotalMilliseconds:F0} ms");
        Assert.True(refresh < RefreshLimit, $"Refreshing the library took {refresh.TotalMilliseconds:F0} ms.");
        Assert.Equal(cursor, library.CurrentItem?.Id);

        // Ctrl+A picks every prompt, on screen or not, for a bulk action.
        var command = App.Platform.Shortcuts.CommandModifier == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control;
        app.Window.KeyPressQwerty(PhysicalKey.A, command);
        await app.SettleAsync();
        Assert.Equal(seeded.Info.Prompts, library.SelectedCount);
        Assert.Contains(library.TagActions, action => action.Label == "Remove #review");
        Press(app, PhysicalKey.Escape);
        await app.SettleAsync();
        Assert.Equal(0, library.SelectedCount);

        // Searching narrows the library from the best match down, and only a screen of its results is built.
        app.ViewModel.SearchText = "angular";
        await app.WaitForAsync(() => library.Items.Count < seeded.Info.Prompts);
        await app.SettleAsync();
        Assert.NotEmpty(library.Items);
        Assert.All(library.Items.Take(20), card => Assert.Contains("angular", card.Title + card.Description + string.Join(' ', card.TagNames), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, scroller.Offset.Y);
        Assert.Contains(grid.GetRealizedCards(), card => card.DataContext == library.Items[0]);
        AssertFew(view, "card");
        app.Screenshot("perf-library-10000-search");
        app.ViewModel.SearchText = string.Empty;
        await app.WaitForAsync(() => library.Items.Count == seeded.Info.Prompts);

        // The list builds only the rows on screen too.
        library.IsListLayout = true;
        await app.SettleAsync();
        AssertFew(view, "row");
        Press(app, PhysicalKey.End);
        await app.SettleAsync();
        Assert.Same(library.Items[^1], Focused(app));
        AssertFew(view, "row");
        app.Screenshot("perf-library-10000-list");
    }

    private static void Press(AppHarness app, PhysicalKey key) => app.Window.KeyPressQwerty(key, RawInputModifiers.None);

    private static object? Focused(AppHarness app) => (app.Window.FocusManager!.GetFocusedElement() as Control)?.DataContext;

    /// <summary>Every card or row button in the view, on screen or waiting to be reused.</summary>
    private static HashSet<Button> Cards(LibraryView view, string kind) =>
        view.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains(kind)).ToHashSet();

    private static void AssertFew(LibraryView view, string kind)
    {
        var count = Cards(view, kind).Count;
        Assert.True(count is > 0 and <= ControlLimit, $"The library holds {count} {kind} controls for {SeededLibrary.PromptCount} prompts.");
    }
}
