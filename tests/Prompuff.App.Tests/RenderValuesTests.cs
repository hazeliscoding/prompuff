using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.App.Tests;

public class RenderValuesTests
{
    [AvaloniaFact]
    public async Task Variable_values_are_remembered_across_restarts_until_cleared()
    {
        var folder = Path.Combine(Path.GetTempPath(), "prompuff-ui-tests", Guid.NewGuid().ToString("N"));
        var first = await AppHarness.StartAsync(importSamples: false, folder: folder);
        await first.ViewModel.NewPromptCommand.ExecuteAsync(null);
        await first.SettleAsync();
        var editor = Assert.IsType<PromptEditorViewModel>(first.ViewModel.CurrentPage);
        editor.Title = "Release notes drafter";
        editor.Body = "Draft release notes for {{version}} in a {{tone}} voice.";

        // Typed before the first save, then kept once the prompt exists.
        editor.Variables.Single(v => v.Name == "version").Value = "2.4.0";
        await editor.SaveCommand.ExecuteAsync(null);
        editor.Variables.Single(v => v.Name == "tone").Value = "cheerful";
        editor.IsRenderTab = true;
        await first.SettleAsync();
        first.Screenshot("render-values-filled");
        await first.QuitAsync();

        await using var second = await AppHarness.StartAsync(importSamples: false, folder: folder);
        await Assert.Single(second.ViewModel.Library.Items).OpenCommand.ExecuteAsync(null);
        await second.SettleAsync();
        var reopened = Assert.IsType<PromptEditorViewModel>(second.ViewModel.CurrentPage);
        Assert.Equal(["2.4.0", "cheerful"], reopened.Variables.Select(v => v.Value));
        Assert.True(reopened.HasValues);

        await reopened.ClearValuesCommand.ExecuteAsync(null);

        Assert.All(reopened.Variables, variable => Assert.Equal(string.Empty, variable.Value));
        Assert.False(reopened.HasValues);
        Assert.Empty(await second.Get<SqliteRenderValues>().LoadAsync(reopened.Id!.Value));
    }
}
