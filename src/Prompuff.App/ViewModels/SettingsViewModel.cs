using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Platform;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.App.ViewModels;

public enum SettingsSection
{
    Appearance,
    Storage,
    ImportExport,
    Updates,
    Shortcuts,
    About,
}

public sealed partial class SettingsSectionItem(SettingsSection section, string label, string icon, Action<SettingsSection> select) : ObservableObject
{
    public SettingsSection Section { get; } = section;
    public string Label { get; } = label;
    public string Icon { get; } = icon;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private void Select() => select(Section);
}

public sealed record ShortcutRow(string Label, string Keys);

public sealed partial class BackupRow(LibraryBackup backup, Func<BackupRow, Task> restore)
{
    public LibraryBackup Backup { get; } = backup;
    public string When { get; } = Format.Timestamp(backup.CreatedAt);

    /// <summary>The time for a sentence: "Oct 7, 2026 at 16:57".</summary>
    public string WhenInWords { get; } = $"{Format.Date(backup.CreatedAt)} at {backup.CreatedAt.ToLocalTime():HH:mm}";

    public string Detail { get; } = backup.Kind switch
    {
        BackupKind.BeforeUpdate => "Before an update",
        BackupKind.BeforeRestore => "Before a restore",
        _ => "Daily",
    } + " · " + Format.Size(backup.SizeBytes);

    [RelayCommand]
    private Task Restore() => restore(this);
}

public sealed partial class SettingsViewModel : ObservableObject
{
    public const string ReleasesUrl = "https://github.com/hazeliscoding/prompuff/releases";
    public const string LicensesUrl = "https://github.com/hazeliscoding/prompuff/tree/main/licenses";

    private readonly ISettingsStore _settings;
    private readonly IAppDataPathProvider _paths;
    private readonly IPromptRepository _prompts;
    private readonly IPromptSearch _search;
    private readonly CollectionService _collections;
    private readonly IPromptTransferService _transfer;
    private readonly IFilePickerService _files;
    private readonly IPlatformLauncher _launcher;
    private readonly IUpdateService _updates;
    private readonly LibraryBackups _backups;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly LibraryNotifier _notifier;
    private readonly ILogger<SettingsViewModel> _logger;
    private AvailableUpdate? _availableUpdate;
    private bool _loading;

    public SettingsViewModel(
        ISettingsStore settings,
        IAppDataPathProvider paths,
        IPromptRepository prompts,
        IPromptSearch search,
        CollectionService collections,
        IPromptTransferService transfer,
        IFilePickerService files,
        IPlatformLauncher launcher,
        IUpdateService updates,
        LibraryBackups backups,
        DialogService dialogs,
        ToastService toasts,
        LibraryNotifier notifier,
        AppearanceState appearance,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _paths = paths;
        _prompts = prompts;
        _search = search;
        _collections = collections;
        _transfer = transfer;
        _files = files;
        _launcher = launcher;
        _updates = updates;
        _backups = backups;
        _dialogs = dialogs;
        _toasts = toasts;
        _notifier = notifier;
        _logger = logger;
        Appearance = appearance;

        Sections =
        [
            new(SettingsSection.Appearance, "Appearance", "Palette", Select),
            new(SettingsSection.Storage, "Storage", "HardDrive", Select),
            new(SettingsSection.ImportExport, "Import / export", "ArrowLeftRight", Select),
            new(SettingsSection.Updates, "Updates", "RefreshCw", Select),
            new(SettingsSection.Shortcuts, "Keyboard shortcuts", "Keyboard", Select),
            new(SettingsSection.About, "About", "Cloud", Select),
        ];
        Shortcuts = Platform.Shortcuts.Reference.Select(row => new ShortcutRow(row.Label, row.Keys)).ToList();
        Select(SettingsSection.Appearance);
    }

    public AppearanceState Appearance { get; }
    public IReadOnlyList<SettingsSectionItem> Sections { get; }
    public IReadOnlyList<ShortcutRow> Shortcuts { get; }
    public ObservableCollection<CollectionOption> ExportCollections { get; } = [];
    public ObservableCollection<BackupRow> Backups { get; } = [];
    public bool HasBackups => Backups.Count > 0;
    public string DataDirectory => _paths.GetAppDataDirectory();
    public string SettingsPath => _paths.GetSettingsPath();
    public string CurrentVersion => _updates.CurrentVersion;
    public bool UpdatesSupported => _updates.IsSupported;
    public string AboutVersion => $"v{CurrentVersion} · Avalonia 12 · .NET {Environment.Version.Major}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAppearance), nameof(IsStorage), nameof(IsImportExport), nameof(IsUpdates), nameof(IsShortcuts), nameof(IsAbout))]
    private SettingsSection _section;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme), nameof(IsLightTheme), nameof(IsSystemTheme))]
    private ThemePreference _theme;

    [ObservableProperty]
    private bool _showMascot;

    [ObservableProperty]
    private bool _checkForUpdatesAutomatically;

    [ObservableProperty]
    private string _storageSummary = string.Empty;

    [ObservableProperty]
    private CollectionOption? _selectedExportCollection;

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private bool _canInstallUpdate;

    /// <summary>Shown in the sidebar footer while an update is waiting to be installed.</summary>
    [ObservableProperty]
    private string _availableUpdateLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloading))]
    private int _downloadProgress;

    public bool IsDownloading => DownloadProgress > 0;
    public bool HasExportCollections => ExportCollections.Count > 0;

    public bool IsAppearance => Section == SettingsSection.Appearance;
    public bool IsStorage => Section == SettingsSection.Storage;
    public bool IsImportExport => Section == SettingsSection.ImportExport;
    public bool IsUpdates => Section == SettingsSection.Updates;
    public bool IsShortcuts => Section == SettingsSection.Shortcuts;
    public bool IsAbout => Section == SettingsSection.About;

    public bool IsDarkTheme
    {
        get => Theme == ThemePreference.Dark;
        set
        {
            if (value)
            {
                Theme = ThemePreference.Dark;
            }
        }
    }

    public bool IsLightTheme
    {
        get => Theme == ThemePreference.Light;
        set
        {
            if (value)
            {
                Theme = ThemePreference.Light;
            }
        }
    }

    public bool IsSystemTheme
    {
        get => Theme == ThemePreference.System;
        set
        {
            if (value)
            {
                Theme = ThemePreference.System;
            }
        }
    }

    /// <summary>Loads saved settings and applies the ones that affect the whole app.</summary>
    public void LoadSettings()
    {
        _loading = true;
        var settings = _settings.Load();
        Theme = settings.Theme;
        ShowMascot = settings.ShowMascot;
        CheckForUpdatesAutomatically = settings.CheckForUpdatesAutomatically;
        Appearance.ShowMascot = settings.ShowMascot;
        ThemeApplier.Apply(settings.Theme);
        UpdateStatus = _updates.IsSupported
            ? "Prompuff checks GitHub Releases for new versions. Nothing about your library is sent."
            : "Updates work in installed builds. This copy is running from a development build.";
        _loading = false;
    }

    public async Task RefreshAsync()
    {
        var counts = await _prompts.GetCountsAsync();
        var size = 0L;
        foreach (var path in new[] { _paths.GetDatabasePath(), _paths.GetDatabasePath() + "-wal" })
        {
            if (File.Exists(path))
            {
                size += new FileInfo(path).Length;
            }
        }

        StorageSummary = $"{Format.Count(counts.All, "prompt")} · {Format.Count(counts.Versions, "version")} · {Format.Size(size)} on disk";

        Backups.Clear();
        foreach (var backup in _backups.List())
        {
            Backups.Add(new BackupRow(backup, RestoreBackupAsync));
        }

        OnPropertyChanged(nameof(HasBackups));

        var selected = SelectedExportCollection?.Id;
        ExportCollections.Clear();
        foreach (var collection in await _collections.ListAsync())
        {
            ExportCollections.Add(new CollectionOption(collection.Id, collection.Name));
        }

        SelectedExportCollection = ExportCollections.FirstOrDefault(option => option.Id == selected) ?? ExportCollections.FirstOrDefault();
        OnPropertyChanged(nameof(HasExportCollections));
    }

    /// <summary>Checks for updates in the background every time Prompuff opens, when the setting is on.</summary>
    public async Task CheckOnStartupAsync()
    {
        var settings = _settings.Load();
        if (!settings.CheckForUpdatesAutomatically || !_updates.IsSupported)
        {
            return;
        }

        try
        {
            var update = await _updates.CheckForUpdatesAsync(settings.UpdateChannel);
            if (update is not null)
            {
                ShowAvailable(update);
                _toasts.Show($"Prompuff {update.Version} is ready.", "Install it from Settings › Updates.");
            }
        }
        catch (LibraryException)
        {
            // Offline at startup is normal; the manual check reports errors.
        }
    }

    public void Select(SettingsSection section)
    {
        Section = section;
        foreach (var item in Sections)
        {
            item.IsActive = item.Section == section;
        }
    }

    [RelayCommand]
    private async Task OpenDataFolder()
    {
        if (!await _launcher.OpenFolderAsync(DataDirectory))
        {
            await _dialogs.ShowErrorAsync("Couldn't open the folder.", $"Your library lives in {DataDirectory}.");
        }
    }

    private async Task RestoreBackupAsync(BackupRow row)
    {
        if (!await _dialogs.ConfirmAsync(
                "Restore this backup?",
                $"Your library goes back to how it was on {row.WhenInWords}. Prompuff copies the current library to the backups folder first, so you can change your mind.",
                "Restore"))
        {
            return;
        }

        try
        {
            await _backups.RestoreAsync(row.Backup);
        }
        catch (LibraryException exception)
        {
            await _dialogs.ShowErrorAsync("Couldn't restore that backup.", exception.Message, exception.InnerException?.Message);
            return;
        }

        _notifier.Notify();
        await RefreshAsync();
        _toasts.Show("Restored.", $"Your library is back to {row.WhenInWords}.");
    }

    [RelayCommand]
    private async Task Import()
    {
        var files = await _files.PickFilesToImportAsync();
        if (files.Count == 0)
        {
            return;
        }

        var result = await _transfer.ImportFilesAsync(files);
        _notifier.Notify();
        await RefreshAsync();
        if (result.Failures.Count == 0)
        {
            _toasts.Show("Imported.", $"{Format.Count(result.ImportedCount, "prompt")} added to your library.");
            return;
        }

        var failed = string.Join("\n", result.Failures.Select(failure => $"{Path.GetFileName(failure.FilePath)}: {failure.Reason}"));
        await _dialogs.ShowErrorAsync(
            result.ImportedCount == 0 ? "Couldn't import that prompt." : $"Imported {Format.Count(result.ImportedCount, "prompt")}, skipped {result.Failures.Count}.",
            result.ImportedCount == 0
                ? "Prompuff couldn't understand the file format. Your existing library hasn't been changed."
                : "Some files couldn't be read. The rest are in your library, and nothing else changed.",
            failed);
    }

    [RelayCommand]
    private async Task ExportAll()
    {
        var prompts = await _search.SearchAsync(PromptQuery.All);
        await ExportAsync(prompts.Select(prompt => prompt.Id).ToList(), "your library");
    }

    [RelayCommand]
    private async Task ExportCollection()
    {
        if (SelectedExportCollection?.Id is not { } id)
        {
            return;
        }

        var prompts = await _search.SearchAsync(new PromptQuery { Filter = PromptFilterKind.Collection, CollectionId = id });
        await ExportAsync(prompts.Select(prompt => prompt.Id).ToList(), SelectedExportCollection.Name);
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        if (!_updates.IsSupported)
        {
            UpdateStatus = "Updates work in installed builds. This copy is running from a development build.";
            return;
        }

        IsCheckingForUpdates = true;
        UpdateStatus = "Checking…";
        try
        {
            var update = await _updates.CheckForUpdatesAsync(_settings.Load().UpdateChannel);
            if (update is null)
            {
                CanInstallUpdate = false;
                UpdateStatus = $"You're on the latest version (v{CurrentVersion}).";
            }
            else
            {
                ShowAvailable(update);
            }
        }
        catch (LibraryException exception)
        {
            UpdateStatus = exception.Message;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_availableUpdate is not { } update)
        {
            return;
        }

        try
        {
            UpdateStatus = $"Downloading v{update.Version}…";
            await _updates.DownloadUpdatesAsync(update, new Progress<int>(percent => DownloadProgress = percent));
            UpdateStatus = "Restarting…";
            _updates.ApplyUpdatesAndRestart(update);
        }
        catch (LibraryException exception)
        {
            UpdateStatus = exception.Message;
        }
    }

    [RelayCommand]
    private Task OpenReleaseNotes() => _launcher.OpenUrlAsync(new Uri(ReleasesUrl));

    [RelayCommand]
    private Task OpenLicenses() => _launcher.OpenUrlAsync(new Uri(LicensesUrl));

    partial void OnThemeChanged(ThemePreference value)
    {
        ThemeApplier.Apply(value);
        Persist(settings => settings with { Theme = value });
    }

    partial void OnShowMascotChanged(bool value)
    {
        Appearance.ShowMascot = value;
        Persist(settings => settings with { ShowMascot = value });
    }

    partial void OnCheckForUpdatesAutomaticallyChanged(bool value) =>
        Persist(settings => settings with { CheckForUpdatesAutomatically = value });

    private void Persist(Func<AppSettings, AppSettings> change)
    {
        if (!_loading)
        {
            _settings.Save(change(_settings.Load()));
        }
    }

    private void ShowAvailable(AvailableUpdate update)
    {
        _availableUpdate = update;
        CanInstallUpdate = true;
        AvailableUpdateLabel = $"Prompuff {update.Version} is available";
        UpdateStatus = $"Prompuff {update.Version} is available.";
    }

    private async Task ExportAsync(IReadOnlyList<Guid> ids, string what)
    {
        if (ids.Count == 0)
        {
            _toasts.Show("Nothing to export yet.", "Save a prompt first.", isHappy: false);
            return;
        }

        var folder = await _files.PickExportFolderAsync();
        if (folder is null)
        {
            return;
        }

        try
        {
            var result = await _transfer.ExportPromptsAsync(ids, folder);
            _toasts.Show("Exported.", $"{Format.Count(result.ExportedCount, "prompt")} from {what} as Markdown.");
        }
        catch (LibraryException exception)
        {
            await _dialogs.ShowErrorAsync("Couldn't export.", exception.Message, exception.InnerException?.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Export failed");
            await _dialogs.ShowErrorAsync("Couldn't export.", "Something went wrong while writing the files.", exception.ToString());
        }
    }
}
