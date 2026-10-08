using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Prompuff.App.Controls;
using Prompuff.App.ViewModels;
using Prompuff.App.Views;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;

namespace Prompuff.App.Tests;

public class DensityTests
{
    [AvaloniaFact]
    public async Task Denser_modes_fit_more_prompts_and_are_remembered()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-ui-tests", Guid.NewGuid().ToString("N"));
        var first = await AppHarness.StartAsync(folder: folder);
        Assert.Equal(3, Columns(first));

        await first.ViewModel.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(first.ViewModel.CurrentPage);
        settings.IsCompactDensity = true;
        await first.SettleAsync();
        first.Screenshot("settings-appearance-density");
        await first.ViewModel.GoToLibraryCommand.ExecuteAsync(null);
        await first.SettleAsync();
        Assert.True(first.ViewModel.Appearance.IsCompact);
        Assert.Equal(4, Columns(first));
        var compactHeight = CardHeight(first);
        first.Screenshot("library-cards-compact");
        first.ViewModel.Library.IsListLayout = true;
        await first.SettleAsync();
        first.Screenshot("library-list-compact");

        settings.IsDenseDensity = true;
        first.ViewModel.Library.IsCardsLayout = true;
        await first.SettleAsync();
        Assert.Equal(4, Columns(first));
        Assert.True(CardHeight(first) < compactHeight * 0.75, "Dense cards drop the description and footer.");
        first.Screenshot("library-cards-dense");
        first.ViewModel.Library.IsListLayout = true;
        await first.SettleAsync();
        first.Screenshot("library-list-dense");
        Assert.Equal(Density.Dense, first.Get<ISettingsStore>().Load().Density);
        await first.QuitAsync();

        await using var second = await AppHarness.StartAsync(importSamples: false, folder: folder);
        Assert.Equal(Density.Dense, second.ViewModel.Appearance.Density);
    }

    private static double CardHeight(AppHarness app) =>
        app.Window.GetVisualDescendants().OfType<LibraryView>().Single()
            .GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(button => button.Classes.Contains("card")).Bounds.Height;

    private static int Columns(AppHarness app)
    {
        var view = app.Window.GetVisualDescendants().OfType<LibraryView>().Single();
        var grid = view.GetVisualDescendants().OfType<CardGrid>().Single();
        return grid.ColumnCount;
    }
}
