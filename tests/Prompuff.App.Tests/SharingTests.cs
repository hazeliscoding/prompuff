using System.Globalization;
using System.IO.Compression;
using Avalonia.Headless.XUnit;
using Prompuff.App.ViewModels;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Tests;

public class SharingTests
{
    [AvaloniaFact]
    public async Task The_library_exports_what_it_shows_as_one_zip()
    {
        var files = new FakeFilePicker();
        await using var app = await AppHarness.StartAsync(files: files);
        files.ExportPath = Path.Combine(app.Folder, "design.zip");

        await app.ViewModel.Sidebar.Collections.Single(c => c.Name == "Design").SelectCommand.ExecuteAsync(null);
        await app.SettleAsync();
        var library = app.ViewModel.Library;
        Assert.True(library.CanExport);
        app.Screenshot("library-export");

        await library.ExportCommand.ExecuteAsync(null);

        Assert.Equal($"prompuff-design-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip", files.SuggestedName);
        using var archive = ZipFile.OpenRead(files.ExportPath);
        Assert.Equal(library.Items.Count, archive.Entries.Count);
        Assert.Equal("Exported.", app.ViewModel.Toasts.Current?.Title);
    }

    [AvaloniaFact]
    public async Task Importing_a_zip_skips_prompts_already_in_the_library()
    {
        var files = new FakeFilePicker();
        await using var app = await AppHarness.StartAsync(files: files);
        var zip = Path.Combine(app.Folder, "everything.zip");
        var all = await app.Get<IPromptSearch>().SearchAsync(PromptQuery.All);
        await app.Get<IPromptTransferService>().ExportArchiveAsync(all.Select(p => p.Id).ToList(), zip);
        files.ImportPaths = [zip];

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        await settings.ImportCommand.ExecuteAsync(null);
        await app.SettleAsync();

        Assert.Equal(6, app.ViewModel.Sidebar.All.Count);
        Assert.Equal("Nothing new.", app.ViewModel.Toasts.Current?.Title);
        Assert.Equal("You already have all 6 of those prompts.", app.ViewModel.Toasts.Current?.Subtitle);
    }
}

/// <summary>Answers file dialogs with set paths, since the headless platform has no dialogs.</summary>
internal sealed class FakeFilePicker : IFilePickerService
{
    public IReadOnlyList<string> ImportPaths { get; set; } = [];
    public string? ExportPath { get; set; }
    public string? SuggestedName { get; private set; }

    public Task<IReadOnlyList<string>> PickFilesToImportAsync() => Task.FromResult(ImportPaths);

    public Task<string?> PickExportFileAsync(string suggestedFileName)
    {
        SuggestedName = suggestedFileName;
        return Task.FromResult(ExportPath);
    }

    public Task<string?> PickArchiveExportFileAsync(string suggestedFileName)
    {
        SuggestedName = suggestedFileName;
        return Task.FromResult(ExportPath);
    }
}
