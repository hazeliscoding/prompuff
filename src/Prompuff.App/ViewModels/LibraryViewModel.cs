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

    /// <summary>The card the arrow keys are on.</summary>
    [ObservableProperty]
    private bool _isCurrent;

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
    private Task _pendingSearch = Task.CompletedTask;
    private int _refreshGeneration;
    private int _libraryTotal;
    private Guid? _currentId;
    private PromptQuery? _lastQuery;
    private string _typeAhead = string.Empty;
    private DateTimeOffset _lastTypedAt;

    /// <summary>How long after the last key type-ahead starts a new word.</summary>
    public static readonly TimeSpan TypeAheadReset = TimeSpan.FromSeconds(1);

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

    /// <summary>The card the arrow keys are on. It follows the prompt across refreshes, and focus follows it in the view.</summary>
    public PromptCardViewModel? CurrentItem { get; private set; }

    /// <summary>Raised when the view should move keyboard focus to <see cref="CurrentItem"/>.</summary>
    public event EventHandler? FocusCurrentRequested;

    /// <summary>True while letters typed in the library extend a type-ahead word instead of starting one.</summary>
    public bool IsTypingAhead => _typeAhead.Length > 0 && _time.GetUtcNow() - _lastTypedAt < TypeAheadReset;

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
        var sameView = query == _lastQuery;
        var previousIndex = CurrentItem is { } previous ? Items.IndexOf(previous) : -1;
        _lastQuery = query;
        Items.Clear();
        foreach (var summary in results)
        {
            var collectionName = summary.CollectionId is { } id && collections.TryGetValue(id, out var collection) ? collection.Name : null;
            Items.Add(new PromptCardViewModel(this, summary, collectionName, now));
        }

        // The cursor stays on its prompt. If that prompt left this view, as after a delete, it moves to the card
        // that took its place.
        var current = Items.FirstOrDefault(item => item.Id == _currentId);
        if (current is null && sameView && previousIndex >= 0 && Items.Count > 0)
        {
            current = Items[Math.Min(previousIndex, Items.Count - 1)];
        }

        CurrentItem = null;
        SetCurrent(current);

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
        SetCurrent(card);
        if (card.IsDeleted)
        {
            _toasts.Show("Restore it first to open it.", card.Title, isHappy: false);
            return Task.CompletedTask;
        }

        return _navigator.OpenPromptAsync(card.Id);
    }

    /// <summary>Puts the keyboard cursor on a card. With <paramref name="focus"/>, the view moves focus there too.</summary>
    public void SetCurrent(PromptCardViewModel? card, bool focus = false)
    {
        if (CurrentItem != card)
        {
            if (CurrentItem is { } old)
            {
                old.IsCurrent = false;
            }

            CurrentItem = card;
            if (card is not null)
            {
                card.IsCurrent = true;
            }

            OnPropertyChanged(nameof(CurrentItem));
        }

        _currentId = card?.Id ?? _currentId;
        if (focus && card is not null)
        {
            FocusCurrentRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves the cursor by <paramref name="delta"/> cards, stopping at either end. With no cursor yet, starts at the first card.</summary>
    public void MoveCurrent(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = CurrentItem is { } current ? Items.IndexOf(current) : -1;
        SetCurrent(Items[index < 0 ? 0 : Math.Clamp(index + delta, 0, Items.Count - 1)], focus: true);
    }

    public void MoveCurrentToEnd(bool last)
    {
        if (Items.Count > 0)
        {
            SetCurrent(last ? Items[^1] : Items[0], focus: true);
        }
    }

    /// <summary>
    /// Type-ahead: moves the cursor to the next title starting with what was typed in the last second. Typing the
    /// same letter again cycles through the titles that start with it. Returns false when nothing matches.
    /// </summary>
    public bool TypeAhead(string text)
    {
        var now = _time.GetUtcNow();
        if (now - _lastTypedAt >= TypeAheadReset)
        {
            _typeAhead = string.Empty;
        }

        if (_typeAhead.Length == 0 && string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        _lastTypedAt = now;
        _typeAhead += text;
        if (Items.Count == 0)
        {
            return false;
        }

        var cycling = _typeAhead.Length > 1 && _typeAhead.All(ch => char.ToUpperInvariant(ch) == char.ToUpperInvariant(_typeAhead[0]));
        var prefix = cycling ? _typeAhead[..1] : _typeAhead;
        var index = CurrentItem is { } current ? Items.IndexOf(current) : -1;

        // A new letter looks past the current card, so pressing it again moves on. A longer word may stay put.
        var from = index < 0 ? 0 : index + (_typeAhead.Length == 1 || cycling ? 1 : 0);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[(from + i) % Items.Count];
            if (item.Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                SetCurrent(item, focus: true);
                return true;
            }
        }

        return false;
    }

    /// <summary>Puts focus on the cards, starting at the first one when there's no cursor yet.</summary>
    public void FocusCards()
    {
        if (CurrentItem is null)
        {
            MoveCurrent(0);
        }
        else
        {
            SetCurrent(CurrentItem, focus: true);
        }
    }

    public Task OpenCurrentAsync() => CurrentItem is { } card ? OpenAsync(card) : Task.CompletedTask;

    /// <summary>Enter in the search box: opens the best match once the search has caught up with the typing.</summary>
    public async Task OpenTopResultAsync()
    {
        await _pendingSearch;
        if (Items.FirstOrDefault() is { } first)
        {
            await OpenAsync(first);
        }
    }

    public Task DeleteCurrentAsync() => CurrentItem is { IsDeleted: false } card ? DeleteAsync(card) : Task.CompletedTask;

    /// <summary>Moves the prompt to Recently deleted after asking, and keeps the cursor where the card was.</summary>
    public async Task DeleteAsync(PromptCardViewModel card)
    {
        if (!await _dialogs.ConfirmAsync(
                $"Delete “{card.Title}”?",
                "It moves to Recently deleted, and you can restore it from there for 30 days.",
                "Delete prompt",
                isDanger: true))
        {
            return;
        }

        try
        {
            await _prompts.DeleteAsync(card.Id);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Couldn't delete prompt {PromptId}", card.Id);
            await _dialogs.ShowErrorAsync("Couldn't delete that prompt.", "It's still in your library, and nothing else changed.", exception.Message);
            return;
        }

        _notifier.Notify();
        _toasts.Show("Moved to Recently deleted.", card.Title, isHappy: false);
        await RefreshAsync();
        if (CurrentItem is not null)
        {
            FocusCurrentRequested?.Invoke(this, EventArgs.Empty);
        }
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

    partial void OnSearchTextChanged(string value) => _pendingSearch = RefreshAfterTypingAsync();

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
