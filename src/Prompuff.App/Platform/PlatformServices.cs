using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Microsoft.Extensions.Logging;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;

namespace Prompuff.App.Platform;

/// <summary>Gives platform services the window they act on. Set once the main window exists.</summary>
public sealed class TopLevelAccessor
{
    public TopLevel? TopLevel { get; set; }

    public TopLevel Require() => TopLevel ?? throw new InvalidOperationException("The main window isn't ready yet.");
}

/// <summary>Clipboard through Avalonia, which covers Windows, X11 and XWayland.</summary>
public sealed class AvaloniaClipboardService(TopLevelAccessor accessor) : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        if (accessor.Require().Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        if (accessor.Require().Clipboard is not { } clipboard)
        {
            return null;
        }

        return await clipboard.TryGetTextAsync();
    }
}

public sealed class AvaloniaFilePickerService(TopLevelAccessor accessor) : IFilePickerService
{
    private static readonly FilePickerFileType Markdown = new("Markdown")
    {
        Patterns = ["*.md", "*.markdown", "*.txt"],
        MimeTypes = ["text/markdown", "text/plain"],
    };

    private static readonly FilePickerFileType Zip = new("Zip of prompts")
    {
        Patterns = ["*.zip"],
        MimeTypes = ["application/zip"],
    };

    private static readonly FilePickerFileType Importable = new("Markdown or zip")
    {
        Patterns = [.. Markdown.Patterns!, .. Zip.Patterns!],
        MimeTypes = [.. Markdown.MimeTypes!, .. Zip.MimeTypes!],
    };

    public async Task<IReadOnlyList<string>> PickFilesToImportAsync()
    {
        var files = await accessor.Require().StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import prompts",
            AllowMultiple = true,
            FileTypeFilter = [Importable, FilePickerFileTypes.All],
        });
        return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
    }

    public async Task<string?> PickExportFileAsync(string suggestedFileName)
    {
        var file = await accessor.Require().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export prompt",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "md",
            FileTypeChoices = [Markdown],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickArchiveExportFileAsync(string suggestedFileName)
    {
        var file = await accessor.Require().StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export prompts",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            FileTypeChoices = [Zip],
            ShowOverwritePrompt = true,
        });
        return file?.TryGetLocalPath();
    }
}

public sealed class AvaloniaLauncher(TopLevelAccessor accessor, ILogger<AvaloniaLauncher> logger) : IPlatformLauncher
{
    public async Task<bool> OpenFolderAsync(string path)
    {
        Directory.CreateDirectory(path);
        try
        {
            if (await accessor.Require().Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
            {
                return true;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The platform launcher couldn't open a folder");
        }

        return StartShell(path);
    }

    public async Task<bool> OpenUrlAsync(Uri uri)
    {
        try
        {
            return await accessor.Require().Launcher.LaunchUriAsync(uri);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The platform launcher couldn't open a link");
            return false;
        }
    }

    /// <summary>Fallback when the portal or shell integration isn't available.</summary>
    private bool StartShell(string path)
    {
        try
        {
            var command = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            using var process = Process.Start(new ProcessStartInfo(command) { ArgumentList = { path }, UseShellExecute = false });
            return process is not null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't open a folder with the system shell");
            return false;
        }
    }
}

public static class ThemeApplier
{
    public static void Apply(ThemePreference preference)
    {
        if (Avalonia.Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = preference switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.System => ThemeVariant.Default,
            _ => ThemeVariant.Dark,
        };
    }
}
