using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Controls;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Application.Settings;

namespace Prompuff.App.ViewModels;

public sealed record TagChipItem(string Name, string Tone)
{
    public static TagChipItem For(string name) => new(name, Tones.For(name));
}

public sealed record SortOption(string Label, PromptSort Sort);

public sealed partial class PromptCardViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;

    public PromptCardViewModel(LibraryViewModel owner, PromptSummary summary, string? collectionName, DateTimeOffset now)
    {
        _owner = owner;
        Id = summary.Id;
        Title = summary.Title;
        Description = summary.Description;
        Tags = summary.Tags.Select(TagChipItem.For).ToList();
        Rating = summary.Rating;
        CollectionName = collectionName ?? "Uncategorized";
        CollectionTone = summary.CollectionId is { } collectionId ? Tones.For(collectionId) : Tones.None;
        _isFavorite = summary.IsFavorite;
        if (summary.DeletedAt is { } deletedAt)
        {
            IsDeleted = true;
            var daysLeft = (int)Math.Ceiling((deletedAt + PromptService.DeletedRetention - now).TotalDays);
            EditedLabel = daysLeft <= 1 ? "Last day" : $"{daysLeft} days left";
            EditedTooltip = "Deleted " + Format.Timestamp(deletedAt);
        }
        else
        {
            EditedLabel = Format.Relative(summary.UpdatedAt, now);
            EditedTooltip = "Edited " + Format.Timestamp(summary.UpdatedAt);
        }
    }

    public Guid Id { get; }
    public string Title { get; }
    public string? Description { get; }
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public IReadOnlyList<TagChipItem> Tags { get; }
    public int? Rating { get; }
    public string RatingLabel => Format.Rating(Rating);
    public string CollectionName { get; }
    public string CollectionTone { get; }
    public string EditedLabel { get; }
    public string EditedTooltip { get; }

    /// <summary>True in Recently deleted, where the card offers Restore instead of a favorite heart.</summary>
    public bool IsDeleted { get; }

    [ObservableProperty]
    private bool _isFavorite;

    [RelayCommand]
    private Task Open() => _owner.OpenAsync(this);

    [RelayCommand]
    private Task ToggleFavorite() => _owner.ToggleFavoriteAsync(this);

    [RelayCommand]
    private Task Restore() => _owner.RestoreAsync(this);
}

/// <summary>The library: filtered, searched and sorted prompts as cards or a list.</summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IPromptSearch _search;
    private readonly IPromptRepository _repository;
    private readonly PromptService _prompts;
    private readonly CollectionService _collections;
    private readonly Navigator _navigator;
    private readonly LibraryNotifier _notifier;
    private readonly ISettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly IPromptTransferService _transfer;
    private readonly IFilePickerService _files;
    private readonly TimeProvider _time;
    private readonly ILogger<LibraryViewModel> _logger;
    private CancellationTokenSource? _searchDelay;
    private int _refreshGeneration;
    private int _libraryTotal;

    public LibraryViewModel(
        IPromptSearch search,
        IPromptRepository repository,
        PromptService prompts,
        CollectionService collections,
        Navigator navigator,
        LibraryNotifier notifier,
        ISettingsStore settings,
        DialogService dialogs,
        ToastService toasts,
        IPromptTransferService transfer,
        IFilePickerService files,
        AppearanceState appearance,
        TimeProvider time,
        ILogger<LibraryViewModel> logger)
    {
        _search = search;
        _repository = repository;
        _prompts = prompts;
        _collections = collections;
        _navigator = navigator;
        _notifier = notifier;
        _settings = settings;
        _dialogs = dialogs;
        _toasts = toasts;
        _transfer = transfer;
        _files = files;
        _time = time;
        _logger = logger;
        Appearance = appearance;

        var saved = settings.Load();
        _layout = saved.LibraryLayout;
        _selectedSort = SortOptions.FirstOrDefault(option => option.Sort == saved.LibrarySort) ?? SortOptions[0];
    }

    public AppearanceState Appearance { get; }
    public ObservableCollection<PromptCardViewModel> Items { get; } = [];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new("Last edited", PromptSort.LastEdited),
        new("Title", PromptSort.Title),
        new("Usefulness", PromptSort.Usefulness),
    ];

    [ObservableProperty]
    private LibraryFilter _filter = LibraryFilter.All;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SortOption _selectedSort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCards), nameof(IsList), nameof(IsCardsLayout), nameof(IsListLayout))]
    private LibraryLayout _layout;

    [ObservableProperty]
    private string _title = "All prompts";

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCards), nameof(IsList))]
    private bool _isEmpty;

    [ObservableProperty]
    private string _emptyTitle = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private bool _isLibraryEmpty;

    public bool HasFilter => Filter.Kind != PromptFilterKind.All || !string.IsNullOrWhiteSpace(SearchText);
    public bool CanSort => Filter.Kind is not PromptFilterKind.Recent and not PromptFilterKind.Deleted;
    public bool IsDeletedView => Filter.Kind == PromptFilterKind.Deleted;
    public bool CanEmpty => IsDeletedView && Items.Count > 0;
    public bool CanExport => !IsDeletedView && Items.Count > 0;
    public string EditedColumn => IsDeletedView ? "TIME LEFT" : "EDITED";
    public bool IsCards => !IsEmpty && Layout == LibraryLayout.Cards;
    public bool IsList => !IsEmpty && Layout == LibraryLayout.List;

    public bool IsCardsLayout
    {
        get => Layout == LibraryLayout.Cards;
        set
        {
            if (value)
            {
                Layout = LibraryLayout.Cards;
            }
        }
    }

    public bool IsListLayout
    {
        get => Layout == LibraryLayout.List;
        set
        {
            if (value)
            {
                Layout = LibraryLayout.List;
            }
        }
    }

    public async Task RefreshAsync()
    {
        var generation = ++_refreshGeneration;
        var collections = (await _collections.ListAsync()).ToDictionary(collection => collection.Id);
        var query = new PromptQuery
        {
            Text = SearchText,
            Filter = Filter.Kind,
            CollectionId = Filter.CollectionId,
            Tag = Filter.Tag,
            Sort = SelectedSort.Sort,
        };
        var results = await _search.SearchAsync(query);
        _libraryTotal = (await _repository.GetCountsAsync()).All;
        if (generation != _refreshGeneration)
        {
            return;
        }

        var now = _time.GetUtcNow();
        Items.Clear();
        foreach (var summary in results)
        {
            var collectionName = summary.CollectionId is { } id && collections.TryGetValue(id, out var collection) ? collection.Name : null;
            Items.Add(new PromptCardViewModel(this, summary, collectionName, now));
        }

        Title = Filter.Kind switch
        {
            PromptFilterKind.Favorites => "Favorites",
            PromptFilterKind.Recent => "Recent",
            PromptFilterKind.Uncategorized => "Uncategorized",
            PromptFilterKind.Collection when Filter.CollectionId is { } id && collections.TryGetValue(id, out var collection) => collection.Name,
            PromptFilterKind.Tag => $"Tagged {Filter.Tag}",
            PromptFilterKind.Deleted => "Recently deleted",
            _ => "All prompts",
        };

        Subtitle = string.IsNullOrWhiteSpace(SearchText)
            ? Format.Count(Items.Count, "prompt") + (IsDeletedView ? $" · removed for good after {PromptService.DeletedRetention.Days} days" : "")
            : $"{Format.Count(Items.Count, "match", "matches")} for “{SearchText.Trim()}”";

        IsLibraryEmpty = _libraryTotal == 0;
        IsEmpty = Items.Count == 0;
        (EmptyTitle, EmptyMessage) = (IsLibraryEmpty, Filter.Kind, string.IsNullOrWhiteSpace(SearchText)) switch
        {
            (true, _, _) => ("Your good prompts deserve a home.", "Nothing here yet. Time to stash something smart."),
            (_, PromptFilterKind.Deleted, true) => ("Nothing in here.", "Deleted prompts wait here for 30 days, in case you change your mind."),
            (_, _, false) => ("Nothing matches that.", "Try fewer words, or search a tag with #name."),
            (_, PromptFilterKind.Favorites, _) => ("No favorites yet.", "Tap the heart on a prompt that keeps earning its place."),
            (_, PromptFilterKind.Collection, _) => ("No prompts here yet.", "Give one a place to live from its editor, or make a new one here."),
            _ => ("No prompts here yet.", "Time to stash something smart."),
        };

        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(CanSort));
        OnPropertyChanged(nameof(IsDeletedView));
        OnPropertyChanged(nameof(CanEmpty));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(EditedColumn));
    }

    public Task OpenAsync(PromptCardViewModel card)
    {
        if (card.IsDeleted)
        {
            _toasts.Show("Restore it first to open it.", card.Title, isHappy: false);
            return Task.CompletedTask;
        }

        return _navigator.OpenPromptAsync(card.Id);
    }

    public async Task RestoreAsync(PromptCardViewModel card)
    {
        try
        {
            await _prompts.RestoreDeletedAsync(card.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Couldn't restore prompt {PromptId}", card.Id);
            await _dialogs.ShowErrorAsync("Couldn't restore that prompt.", "It's still in Recently deleted, and the rest of your library is safe.", exception.Message);
            return;
        }

        _notifier.Notify();
        _toasts.Show("Restored.", card.Title);
    }

    public async Task ToggleFavoriteAsync(PromptCardViewModel card)
    {
        try
        {
            card.IsFavorite = !card.IsFavorite;
            await _prompts.SetFavoriteAsync(card.Id, card.IsFavorite);
            _notifier.Notify();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Couldn't toggle favorite for prompt {PromptId}", card.Id);
            card.IsFavorite = !card.IsFavorite;
        }
    }

    /// <summary>Saves the prompts on screen, with the current filter and search, as one .zip to share.</summary>
    [RelayCommand]
    private async Task Export()
    {
        var ids = Items.Select(card => card.Id).ToList();
        var path = await _files.PickArchiveExportFileAsync(Format.ArchiveName(_transfer, Title, _time.GetUtcNow()));
        if (path is null)
        {
            return;
        }

        try
        {
            var result = await _transfer.ExportArchiveAsync(ids, path);
            _toasts.Show("Exported.", $"{Format.Count(result.ExportedCount, "prompt")} in {Path.GetFileName(path)}.");
        }
        catch (LibraryException exception)
        {
            await _dialogs.ShowErrorAsync("Couldn't export.", exception.Message, exception.InnerException?.Message);
        }
    }

    [RelayCommand]
    private async Task EmptyRecentlyDeleted()
    {
        if (!await _dialogs.ConfirmAsync(
                "Empty Recently deleted?",
                $"This removes {Format.Count(Items.Count, "prompt")} and their versions for good. You can't undo it.",
                "Empty",
                isDanger: true))
        {
            return;
        }

        try
        {
            await _prompts.EmptyRecentlyDeletedAsync();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Couldn't empty Recently deleted");
            await _dialogs.ShowErrorAsync("Couldn't empty Recently deleted.", "Nothing was removed, and the rest of your library is safe.", exception.Message);
            return;
        }

        _notifier.Notify();
        _toasts.Show("All cleared out.", isHappy: false);
    }

    [RelayCommand]
    private Task ClearFilter()
    {
        SearchText = string.Empty;
        return _navigator.ShowLibraryAsync(LibraryFilter.All);
    }

    [RelayCommand]
    private Task NewPrompt() => _navigator.NewPromptAsync(Filter.Kind == PromptFilterKind.Collection ? Filter.CollectionId : null);

    partial void OnSearchTextChanged(string value) => _ = RefreshAfterTypingAsync();

    partial void OnSelectedSortChanged(SortOption value)
    {
        _settings.Save(_settings.Load() with { LibrarySort = value.Sort });
        _ = RefreshAsync();
    }

    partial void OnLayoutChanged(LibraryLayout value) => _settings.Save(_settings.Load() with { LibraryLayout = value });

    private async Task RefreshAfterTypingAsync()
    {
        _searchDelay?.Cancel();
        var delay = _searchDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, delay.Token);
            await RefreshAsync();
        }
        catch (TaskCanceledException)
        {
            // A newer keystroke replaced this search.
        }
    }
}
