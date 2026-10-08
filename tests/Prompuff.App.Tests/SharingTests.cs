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

public class FolderImportTests
{
    [AvaloniaFact]
    public async Task Importing_a_folder_reports_the_files_it_passed_over()
    {
        var files = new FakeFilePicker();
        await using var app = await AppHarness.StartAsync(importSamples: false, files: files);
        var vault = Path.Combine(app.Folder, "vault");
        Directory.CreateDirectory(Path.Combine(vault, "Coding"));
        Directory.CreateDirectory(Path.Combine(vault, ".obsidian"));
        await File.WriteAllTextAsync(Path.Combine(vault, "Standup.md"), "Summarize {{notes}} as three bullets.");
        await File.WriteAllTextAsync(Path.Combine(vault, "Coding", "Review.md"), "Review {{diff}} for behavior changes.");
        await File.WriteAllTextAsync(Path.Combine(vault, "Coding", "diagram.png"), "png");
        await File.WriteAllTextAsync(Path.Combine(vault, ".obsidian", "app.md"), "state");
        files.ImportFolder = vault;

        await app.ViewModel.Sidebar.OpenSettingsCommand.ExecuteAsync(null);
        var settings = Assert.IsType<SettingsViewModel>(app.ViewModel.CurrentPage);
        settings.Select(SettingsSection.ImportExport);
        var importing = settings.ImportFolderCommand.ExecuteAsync(null);
        await app.SettleAsync();

        var dialog = app.ViewModel.Dialogs.Current;
        Assert.NotNull(dialog);
        Assert.Equal("Imported 2 prompts.", dialog.Title);
        Assert.Equal("Passed over 1 file that Prompuff doesn't import.", dialog.Message);
        Assert.Contains("diagram.png", dialog.Details);
        Assert.False(dialog.IsError);
        dialog.ToggleDetailsCommand.Execute(null);
        app.Screenshot("dialog-import-folder");
        dialog.ConfirmCommand.Execute(null);
        await importing;
        await app.SettleAsync();

        Assert.Equal(2, app.ViewModel.Sidebar.All.Count);
        app.Screenshot("settings-import-export");
    }
}

/// <summary>Answers file dialogs with set paths, since the headless platform has no dialogs.</summary>
internal sealed class FakeFilePicker : IFilePickerService
{
    public IReadOnlyList<string> ImportPaths { get; set; } = [];
    public string? ExportPath { get; set; }
    public string? SuggestedName { get; private set; }

    public Task<IReadOnlyList<string>> PickFilesToImportAsync() => Task.FromResult(ImportPaths);

    public string? ImportFolder { get; set; }

    public Task<string?> PickFolderToImportAsync() => Task.FromResult(ImportFolder);

    public Task<string?> PickExportFileAsync(string suggestedFileName, string title = "Export prompt")
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
