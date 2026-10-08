using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Platform;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Infrastructure.Persistence;

namespace Prompuff.App.ViewModels;

/// <summary>Shown instead of the library when the database can't be opened.</summary>
public sealed partial class StartupErrorViewModel(string message, string? details, string dataDirectory, IPlatformLauncher launcher) : ObservableObject
{
    public string Message { get; } = message;
    public string? Details { get; } = details;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public string DataDirectory { get; } = dataDirectory;

    [ObservableProperty]
    private bool _showDetails;

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;

    [RelayCommand]
    private Task OpenDataFolder() => launcher.OpenFolderAsync(DataDirectory);
}

/// <summary>The window shell: navigation between pages, overlays, and keyboard shortcuts.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SqliteDatabase _database;
    private readonly LibraryBackups _backups;
    private readonly PromptService _prompts;
    private readonly RenderValuesCache _renderValues;
    private readonly Func<PromptEditorViewModel> _createEditor;
    private readonly IPlatformLauncher _launcher;
    private readonly IAppDataPathProvider _paths;
    private readonly LibraryNotifier _notifier;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _refreshQueued;
    private bool _ready;

    public MainWindowViewModel(
        SqliteDatabase database,
        LibraryBackups backups,
        PromptService prompts,
        RenderValuesCache renderValues,
        SidebarViewModel sidebar,
        LibraryViewModel library,
        SettingsViewModel settings,
        QuickSaveViewModel quickSave,
        CommandPaletteViewModel palette,
        DialogService dialogs,
        ToastService toasts,
        AppearanceState appearance,
        Navigator navigator,
        LibraryNotifier notifier,
        Func<PromptEditorViewModel> createEditor,
        IPlatformLauncher launcher,
        IAppDataPathProvider paths,
        ILogger<MainWindowViewModel> logger)
    {
        _database = database;
        _backups = backups;
        _prompts = prompts;
        _renderValues = renderValues;
        Sidebar = sidebar;
        Library = library;
        Settings = settings;
        QuickSave = quickSave;
        Palette = palette;
        Dialogs = dialogs;
        Toasts = toasts;
        Appearance = appearance;
        _createEditor = createEditor;
        _launcher = launcher;
        _paths = paths;
        _notifier = notifier;
        _logger = logger;

        navigator.OpenPromptRequested += OpenPromptAsync;
        navigator.NewPromptRequested += NewPromptAsync;
        navigator.LibraryRequested += ShowLibraryAsync;
        navigator.SettingsRequested += ShowSettingsAsync;
        notifier.Changed += (_, _) => QueueRefresh();
        palette.RunAction = RunPaletteActionAsync;
        _currentPage = library;
    }

    public SidebarViewModel Sidebar { get; }
    public LibraryViewModel Library { get; }
    public SettingsViewModel Settings { get; }
    public QuickSaveViewModel QuickSave { get; }
    public CommandPaletteViewModel Palette { get; }
    public DialogService Dialogs { get; }
    public ToastService Toasts { get; }
    public AppearanceState Appearance { get; }
    public string PaletteShortcut => Shortcuts.Display(ShortcutAction.CommandPalette);
    public string QuickSaveShortcut => Shortcuts.Display(ShortcutAction.QuickSave);

    public bool ExtendsIntoTitleBar { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Space to leave for the window buttons when the window extends into the title bar: on Windows, three 45px caption
    /// buttons 2px apart on the right; on macOS, the traffic lights on the left.
    /// </summary>
    public Avalonia.Thickness TitleBarInset { get; } =
        OperatingSystem.IsWindows() ? new Avalonia.Thickness(0, 0, 144, 0)
        : OperatingSystem.IsMacOS() ? new Avalonia.Thickness(78, 0, 0, 0)
        : default;

    [ObservableProperty]
    private object _currentPage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public PromptEditorViewModel? Editor => CurrentPage as PromptEditorViewModel;

    /// <summary>Raised when Ctrl+F should move focus to the search box.</summary>
    public event EventHandler? FocusSearchRequested;

    public async Task InitializeAsync()
    {
        Settings.LoadSettings();
        try
        {
            await _database.InitializeAsync();
        }
        catch (LibraryException exception)
        {
            CurrentPage = new StartupErrorViewModel(exception.Message, exception.InnerException?.ToString(), _paths.GetAppDataDirectory(), _launcher);
            return;
        }

        _ready = true;
        _backups.StartDailySchedule();
        await PurgeExpiredAsync();
        await RefreshAllAsync();
        _ = Settings.CheckOnStartupAsync();
    }

    /// <summary>Removes prompts that have waited in Recently deleted longer than 30 days.</summary>
    private async Task PurgeExpiredAsync()
    {
        try
        {
            await _prompts.PurgeExpiredAsync();
        }
        catch (Exception exception)
        {
            // Trying again next time is fine; the prompts stay in Recently deleted until then.
            _logger.LogWarning(exception, "Couldn't remove expired prompts from Recently deleted");
        }
    }

    /// <summary>Starts a keyboard shortcut if it applies right now. Returns false to let the key through.</summary>
    public bool TryHandleShortcut(ShortcutAction action)
    {
        var applies = action switch
        {
            ShortcutAction.Close => Dialogs.Current is not null || Palette.IsOpen || QuickSave.IsOpen,
            _ when !_ready || Dialogs.Current is not null => false,
            ShortcutAction.CommandPalette or ShortcutAction.QuickSave => true,
            _ when Palette.IsOpen || QuickSave.IsOpen => false,
            ShortcutAction.NewPrompt or ShortcutAction.FocusSearch => true,
            ShortcutAction.ShowHistory => Editor is { IsNew: false },
            ShortcutAction.Back => CurrentPage is PromptEditorViewModel or SettingsViewModel,
            _ => Editor is not null,
        };

        if (applies)
        {
            _ = HandleShortcutAsync(action);
        }

        return applies;
    }

    /// <summary>Runs a keyboard shortcut. Returns true when it was handled.</summary>
    public async Task<bool> HandleShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.Close)
        {
            if (Dialogs.CancelCurrent())
            {
                return true;
            }

            if (Palette.IsOpen)
            {
                Palette.IsOpen = false;
                return true;
            }

            if (QuickSave.IsOpen)
            {
                QuickSave.IsOpen = false;
                return true;
            }

            return false;
        }

        if (!_ready || Dialogs.Current is not null)
        {
            return false;
        }

        switch (action)
        {
            case ShortcutAction.CommandPalette:
                QuickSave.IsOpen = false;
                await Palette.OpenAsync();
                return true;
            case ShortcutAction.QuickSave:
                Palette.IsOpen = false;
                await QuickSave.OpenAsync(CurrentCollectionId());
                return true;
            case ShortcutAction.NewPrompt:
                await NewPromptAsync(CurrentCollectionId());
                return true;
            case ShortcutAction.FocusSearch:
                FocusSearchRequested?.Invoke(this, EventArgs.Empty);
                return true;
            case ShortcutAction.Save when Editor is { } editor:
                await editor.SaveAsync();
                return true;
            case ShortcutAction.RenderOrCopy when Editor is { } editor:
                await editor.RenderOrCopyCommand.ExecuteAsync(null);
                return true;
            case ShortcutAction.CopyPrompt when Editor is { } editor:
                await editor.CopyTemplateCommand.ExecuteAsync(null);
                return true;
            case ShortcutAction.ToggleFavorite when Editor is { } editor:
                editor.ToggleFavoriteCommand.Execute(null);
                return true;
            case ShortcutAction.ShowHistory when Editor is { IsNew: false } editor:
                editor.Tab = EditorTab.History;
                return true;
            case ShortcutAction.Back when CurrentPage is PromptEditorViewModel or SettingsViewModel:
                await ShowLibraryAsync(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Saves pending edits before the window closes. Returns false if saving failed.</summary>
    public async Task<bool> PrepareToCloseAsync()
    {
        if (Editor is { IsDirty: true } editor && !await editor.SaveAsync(quiet: true))
        {
            return false;
        }

        await _renderValues.FlushAsync();
        return true;
    }

    [RelayCommand]
    private Task NewPrompt() => NewPromptAsync(CurrentCollectionId());

    [RelayCommand]
    private Task OpenQuickSave() => HandleShortcutAsync(ShortcutAction.QuickSave);

    [RelayCommand]
    private Task OpenPalette() => HandleShortcutAsync(ShortcutAction.CommandPalette);

    [RelayCommand]
    private Task GoToLibrary() => ShowLibraryAsync(null);

    [RelayCommand]
    private async Task OpenUpdates()
    {
        await ShowSettingsAsync();
        Settings.Select(SettingsSection.Updates);
    }

    [RelayCommand]
    private Task OpenSettings() => ShowSettingsAsync();

    [RelayCommand]
    private async Task OpenAbout()
    {
        await ShowSettingsAsync();
        Settings.Select(SettingsSection.About);
    }

    partial void OnSearchTextChanged(string value)
    {
        Library.SearchText = value;
        if (CurrentPage is not LibraryViewModel && !string.IsNullOrWhiteSpace(value))
        {
            _ = ShowLibraryAsync(null);
        }
    }

    partial void OnCurrentPageChanged(object value)
    {
        OnPropertyChanged(nameof(Editor));
        Sidebar.SetActive(value is LibraryViewModel ? Library.Filter : null, value is SettingsViewModel);
    }

    private async Task OpenPromptAsync(Guid id)
    {
        if (!await LeaveCurrentPageAsync())
        {
            return;
        }

        try
        {
            var editor = _createEditor();
            await editor.LoadAsync(id);
            CurrentPage = editor;
        }
        catch (LibraryException exception)
        {
            await Dialogs.ShowErrorAsync("Couldn't open that prompt.", exception.Message);
            _notifier.Notify();
        }
    }

    private async Task NewPromptAsync(Guid? collectionId)
    {
        if (!await LeaveCurrentPageAsync())
        {
            return;
        }

        var editor = _createEditor();
        await editor.InitializeNewAsync(collectionId);
        CurrentPage = editor;
    }

    private async Task ShowLibraryAsync(LibraryFilter? filter)
    {
        if (!await LeaveCurrentPageAsync())
        {
            return;
        }

        if (filter is not null)
        {
            Library.Filter = filter;
            if (filter.Kind != PromptFilterKind.All && !string.IsNullOrEmpty(SearchText))
            {
                SearchText = string.Empty;
            }
        }

        CurrentPage = Library;
        Sidebar.SetActive(Library.Filter, settingsActive: false);
        await Library.RefreshAsync();
    }

    private async Task ShowSettingsAsync()
    {
        if (!await LeaveCurrentPageAsync())
        {
            return;
        }

        await Settings.RefreshAsync();
        CurrentPage = Settings;
    }

    private async Task<bool> LeaveCurrentPageAsync() =>
        CurrentPage is not PromptEditorViewModel { IsDirty: true } editor || await editor.SaveAsync(quiet: true);

    private Guid? CurrentCollectionId() => CurrentPage switch
    {
        LibraryViewModel { Filter.Kind: PromptFilterKind.Collection } library => library.Filter.CollectionId,
        PromptEditorViewModel editor => editor.SelectedCollection.Id,
        _ => null,
    };

    private async Task RunPaletteActionAsync(string id)
    {
        switch (id)
        {
            case "new":
                await NewPromptAsync(CurrentCollectionId());
                break;
            case "quick-save":
                await QuickSave.OpenAsync(CurrentCollectionId());
                break;
            case "favorites":
                await ShowLibraryAsync(new LibraryFilter(PromptFilterKind.Favorites));
                break;
            case "recent":
                await ShowLibraryAsync(new LibraryFilter(PromptFilterKind.Recent));
                break;
            case "new-collection":
                await Sidebar.NewCollectionCommand.ExecuteAsync(null);
                break;
            case "import":
                await ShowSettingsAsync();
                Settings.Select(SettingsSection.ImportExport);
                await Settings.ImportCommand.ExecuteAsync(null);
                break;
            case "import-folder":
                await ShowSettingsAsync();
                Settings.Select(SettingsSection.ImportExport);
                await Settings.ImportFolderCommand.ExecuteAsync(null);
                break;
            case "settings":
                await ShowSettingsAsync();
                break;
            case var search when search.StartsWith("search:", StringComparison.Ordinal):
                await ShowLibraryAsync(LibraryFilter.All);
                SearchText = search["search:".Length..];
                break;
        }
    }

    private async void QueueRefresh()
    {
        if (_refreshQueued || !_ready)
        {
            return;
        }

        _refreshQueued = true;
        await Task.Yield();
        _refreshQueued = false;
        try
        {
            await RefreshAllAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing the library failed");
        }
    }

    private async Task RefreshAllAsync()
    {
        await Sidebar.RefreshAsync();
        Sidebar.SetActive(CurrentPage is LibraryViewModel ? Library.Filter : null, CurrentPage is SettingsViewModel);
        if (CurrentPage is LibraryViewModel)
        {
            await Library.RefreshAsync();
        }
        else if (CurrentPage is SettingsViewModel)
        {
            await Settings.RefreshAsync();
        }
    }
}
