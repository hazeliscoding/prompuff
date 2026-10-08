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
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.ViewModels;

public sealed record TagChipItem(string Name, string Tone)
{
    public static TagChipItem For(string name) => new(name, Tones.For(name));
}

public sealed record SortOption(string Label, PromptSort Sort);

/// <summary>An entry in a menu built at runtime, such as a collection to move prompts to.</summary>
public sealed partial class MenuActionViewModel(string label, Func<Task> run)
{
    public string Label { get; } = label;

    [RelayCommand]
    private Task Run() => run();
}

/// <remarks>
/// A library can hold thousands of these while only the cards on screen are drawn, so anything only a drawn card
/// needs, such as the edit time in words, is worked out when it's asked for.
/// </remarks>
public sealed partial class PromptCardViewModel : ObservableObject
{
    private readonly LibraryViewModel _owner;
    private readonly PromptSummary _summary;
    private readonly DateTimeOffset _now;
    private string? _editedLabel;

    public PromptCardViewModel(LibraryViewModel owner, PromptSummary summary, string? collectionName, DateTimeOffset now)
    {
        _owner = owner;
        _summary = summary;
        _now = now;
        _isFavorite = summary.IsFavorite;
        CollectionName = collectionName ?? "Uncategorized";
    }

    public Guid Id => _summary.Id;
    public string Title => _summary.Title;
    public string? Description => _summary.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    /// <summary>The prompt's tag names, in order.</summary>
    public IReadOnlyList<string> TagNames => _summary.Tags;

    public int? Rating => _summary.Rating;
    public string RatingLabel => Format.Rating(Rating);
    public string CollectionName { get; }
    public string CollectionTone => _summary.CollectionId is { } collectionId ? Tones.For(collectionId) : Tones.None;

    public string EditedLabel => _editedLabel ??= _summary.DeletedAt is { } deletedAt ? TimeLeft(deletedAt) : Format.Relative(_summary.UpdatedAt, _now);

    public string EditedTooltip => _summary.DeletedAt is { } deletedAt
        ? "Deleted " + Format.Timestamp(deletedAt)
        : "Edited " + Format.Timestamp(_summary.UpdatedAt);

    /// <summary>True in Recently deleted, where the card offers Restore instead of a favorite heart.</summary>
    public bool IsDeleted => _summary.DeletedAt is not null;

    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>The card the arrow keys are on.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>Picked for a bulk action: tag, move, export or delete.</summary>
    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private Task Open() => _owner.OpenAsync(this);

    [RelayCommand]
    private Task ToggleFavorite() => _owner.ToggleFavoriteAsync(this);

    [RelayCommand]
    private Task Restore() => _owner.RestoreAsync(this);

    private string TimeLeft(DateTimeOffset deletedAt)
    {
        var daysLeft = (int)Math.Ceiling((deletedAt + PromptService.DeletedRetention - _now).TotalDays);
        return daysLeft <= 1 ? "Last day" : $"{daysLeft} days left";
    }
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
    private Guid? _currentId;
    private PromptQuery? _lastQuery;
    private string _typeAhead = string.Empty;
    private DateTimeOffset _lastTypedAt;
    private readonly HashSet<Guid> _selectedIds = [];
    private Guid? _anchorId;

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
    public BulkObservableCollection<PromptCardViewModel> Items { get; } = [];

    /// <summary>"Add tags…", then one "Remove" entry for each tag on the selected prompts.</summary>
    public ObservableCollection<MenuActionViewModel> TagActions { get; } = [];

    /// <summary>Every collection the selected prompts can move to, then "New collection…".</summary>
    public ObservableCollection<MenuActionViewModel> MoveActions { get; } = [];

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

    /// <summary>Selection mode: clicking a card picks it instead of opening it, and the header offers bulk actions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotSelecting))]
    private bool _isSelecting;

    public bool IsNotSelecting => !IsSelecting;
    public int SelectedCount => _selectedIds.Count;
    public bool HasSelection => SelectedCount > 0;
    public string SelectionLabel => HasSelection ? $"{Format.Count(SelectedCount, "prompt")} selected" : "Pick some prompts";
    public bool CanSelect => Items.Count > 0;

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
        var query = new PromptQuery
        {
            Text = SearchText,
            Filter = Filter.Kind,
            CollectionId = Filter.CollectionId,
            Tag = Filter.Tag,
            Sort = SelectedSort.Sort,
        };

        // The database work runs off the UI thread, so typing and scrolling carry on while a large library loads.
        var (collections, results, libraryEmpty) = await Task.Run(async () =>
        {
            var collections = (await _collections.ListAsync()).ToDictionary(collection => collection.Id);
            var results = await _search.SearchAsync(query);

            // Prompts on screen outside Recently deleted already prove the library isn't empty.
            var libraryEmpty = (results.Count == 0 || query.Filter == PromptFilterKind.Deleted) && (await _repository.GetCountsAsync()).All == 0;
            return (collections, results, libraryEmpty);
        });
        if (generation != _refreshGeneration)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var sameView = query == _lastQuery;
        var previousIndex = CurrentItem is { } previous ? Items.IndexOf(previous) : -1;
        _lastQuery = query;
        var cards = new List<PromptCardViewModel>(results.Count);
        foreach (var summary in results)
        {
            var collectionName = summary.CollectionId is { } id && collections.TryGetValue(id, out var collection) ? collection.Name : null;
            cards.Add(new PromptCardViewModel(this, summary, collectionName, now));
        }

        Items.ReplaceAll(cards);

        // The cursor stays on its prompt. If that prompt left this view, as after a delete, it moves to the card
        // that took its place.
        var current = Items.FirstOrDefault(item => item.Id == _currentId);
        if (current is null && sameView && previousIndex >= 0 && Items.Count > 0)
        {
            current = Items[Math.Min(previousIndex, Items.Count - 1)];
        }

        CurrentItem = null;
        SetCurrent(current);

        // The selection keeps only prompts still on screen, so a bulk action never touches one you can't see.
        if (_selectedIds.Count > 0)
        {
            _selectedIds.IntersectWith(Items.Select(item => item.Id));
            foreach (var item in Items)
            {
                item.IsSelected = _selectedIds.Contains(item.Id);
            }
        }

        RebuildMoveActions(collections.Values);
        OnSelectionChanged();

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

        IsLibraryEmpty = libraryEmpty;
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
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(CanSort));
        OnPropertyChanged(nameof(IsDeletedView));
        OnPropertyChanged(nameof(CanEmpty));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(EditedColumn));
    }

    public Task OpenAsync(PromptCardViewModel card)
    {
        SetCurrent(card);
        if (IsSelecting)
        {
            ToggleSelection(card);
            return Task.CompletedTask;
        }

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

    /// <summary>
    /// Moves the cursor by <paramref name="delta"/> cards, stopping at either end. With no cursor yet, starts at the
    /// first card. With <paramref name="extend"/> (Shift), the cards passed over join the selection.
    /// </summary>
    public void MoveCurrent(int delta, bool extend = false)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = CurrentItem is { } current ? Items.IndexOf(current) : -1;
        MoveCurrentTo(Items[index < 0 ? 0 : Math.Clamp(index + delta, 0, Items.Count - 1)], extend);
    }

    public void MoveCurrentToEnd(bool last, bool extend = false)
    {
        if (Items.Count > 0)
        {
            MoveCurrentTo(last ? Items[^1] : Items[0], extend);
        }
    }

    private void MoveCurrentTo(PromptCardViewModel target, bool extend)
    {
        var from = CurrentItem;
        SetCurrent(target, focus: true);
        if (extend)
        {
            _anchorId ??= from?.Id ?? target.Id;
            SelectRange(target);
        }
    }

    /// <summary>Ctrl or Cmd and click, or a click in selection mode: picks or unpicks one card.</summary>
    public void ToggleSelection(PromptCardViewModel card)
    {
        IsSelecting = true;
        card.IsSelected = !card.IsSelected;
        if (card.IsSelected)
        {
            _selectedIds.Add(card.Id);
        }
        else
        {
            _selectedIds.Remove(card.Id);
        }

        _anchorId = card.Id;
        SetCurrent(card);
        OnSelectionChanged();
    }

    /// <summary>Shift and click: picks every card from the last one picked to this one.</summary>
    public void SelectRange(PromptCardViewModel card)
    {
        IsSelecting = true;
        var end = Items.IndexOf(card);
        var start = Items.FirstOrDefault(item => item.Id == _anchorId) is { } anchor ? Items.IndexOf(anchor) : end;
        for (var i = Math.Min(start, end); i <= Math.Max(start, end); i++)
        {
            Items[i].IsSelected = true;
            _selectedIds.Add(Items[i].Id);
        }

        _anchorId ??= card.Id;
        SetCurrent(card);
        OnSelectionChanged();
    }

    [RelayCommand]
    public void SelectAll()
    {
        if (Items.Count == 0)
        {
            return;
        }

        IsSelecting = true;
        foreach (var item in Items)
        {
            item.IsSelected = true;
            _selectedIds.Add(item.Id);
        }

        OnSelectionChanged();
    }

    [RelayCommand]
    private void StartSelecting() => IsSelecting = true;

    /// <summary>Leaves selection mode and unpicks everything.</summary>
    [RelayCommand]
    public void ClearSelection()
    {
        foreach (var item in Items)
        {
            item.IsSelected = false;
        }

        _selectedIds.Clear();
        _anchorId = null;
        IsSelecting = false;
        OnSelectionChanged();
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

    public Task DeleteCurrentAsync() =>
        HasSelection ? DeleteSelectedAsync()
        : CurrentItem is { IsDeleted: false } card ? DeleteAsync(card)
        : Task.CompletedTask;

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
    private async Task DeleteSelectedAsync()
    {
        var ids = SelectedIds();
        if (ids.Count == 0 || IsDeletedView)
        {
            return;
        }

        if (!await _dialogs.ConfirmAsync(
                $"Delete {Format.Count(ids.Count, "prompt")}?",
                ids.Count == 1
                    ? "It moves to Recently deleted, and you can restore it from there for 30 days."
                    : "They move to Recently deleted, and you can restore them from there for 30 days.",
                "Delete",
                isDanger: true))
        {
            return;
        }

        var done = await RunBulkAsync("Couldn't delete those prompts.", ids, id => _prompts.DeleteAsync(id));
        if (done > 0)
        {
            ClearSelection();
            _toasts.Show("Moved to Recently deleted.", Format.Count(done, "prompt"), isHappy: false);
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RestoreSelected()
    {
        var done = await RunBulkAsync("Couldn't restore those prompts.", SelectedIds(), id => _prompts.RestoreDeletedAsync(id));
        if (done > 0)
        {
            ClearSelection();
            _toasts.Show("Restored.", Format.Count(done, "prompt") + " back in your library.");
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ExportSelected()
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            return;
        }

        var path = await _files.PickArchiveExportFileAsync(Format.ArchiveName(_transfer, "selection", _time.GetUtcNow()));
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

    private async Task AddTagsToSelectedAsync()
    {
        var ids = SelectedIds();
        var text = await _dialogs.AskTextAsync(
            $"Tag {Format.Count(ids.Count, "prompt")}",
            "Add one or more tags, separated by commas.",
            "Add tags",
            placeholder: "review, angular");
        var tags = TagName.NormalizeAll((text ?? string.Empty).Split(','));
        if (ids.Count == 0 || tags.Count == 0)
        {
            return;
        }

        var done = await RunBulkAsync("Couldn't tag those prompts.", ids, id => _prompts.AddTagsAsync(id, tags));
        if (done > 0)
        {
            _toasts.Show("Tagged.", $"{string.Join(", ", tags.Select(tag => "#" + tag))} on {Format.Count(done, "prompt")}.");
            await RefreshAsync();
        }
    }

    private async Task RemoveTagFromSelectedAsync(string tag)
    {
        var ids = Items.Where(item => item.IsSelected && item.TagNames.Contains(tag)).Select(item => item.Id).ToList();
        var done = await RunBulkAsync("Couldn't remove that tag.", ids, id => _prompts.RemoveTagAsync(id, tag));
        if (done > 0)
        {
            _toasts.Show("Tag removed.", $"#{tag} from {Format.Count(done, "prompt")}.", isHappy: false);
            await RefreshAsync();
        }
    }

    private async Task MoveSelectedAsync(Guid? collectionId, string name)
    {
        var done = await RunBulkAsync("Couldn't move those prompts.", SelectedIds(), id => _prompts.SetCollectionAsync(id, collectionId));
        if (done > 0)
        {
            ClearSelection();
            _toasts.Show("Moved.", $"{Format.Count(done, "prompt")} to {name}.");
            await RefreshAsync();
        }
    }

    private async Task MoveSelectedToNewCollectionAsync()
    {
        var name = await _dialogs.AskTextAsync(
            "New collection",
            $"Give {(SelectedCount == 1 ? "this prompt" : $"these {SelectedCount} prompts")} a place to live.",
            "Create and move",
            placeholder: "Coding, Design, Writing…");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var collection = await _collections.CreateAsync(name);
            await MoveSelectedAsync(collection.Id, collection.Name);
        }
        catch (LibraryException exception)
        {
            await _dialogs.ShowErrorAsync("Couldn't create that collection.", exception.Message);
        }
    }

    private List<Guid> SelectedIds() => Items.Where(item => item.IsSelected).Select(item => item.Id).ToList();

    /// <summary>Runs one change per prompt. If one fails, the ones before it stay done and the dialog says how many.</summary>
    private async Task<int> RunBulkAsync(string failureTitle, IReadOnlyList<Guid> ids, Func<Guid, Task> change)
    {
        var done = 0;
        try
        {
            foreach (var id in ids)
            {
                await change(id);
                done++;
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "A bulk change stopped after {Done} of {Count} prompts", done, ids.Count);
            await _dialogs.ShowErrorAsync(
                failureTitle,
                done == 0
                    ? "Nothing changed, and your library is safe."
                    : $"{Format.Count(done, "prompt")} changed before it stopped. The rest are as they were, and your library is safe.",
                exception.Message);
        }
        finally
        {
            if (done > 0)
            {
                _notifier.Notify();
            }
        }

        return done;
    }

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionLabel));

        TagActions.Clear();
        TagActions.Add(new MenuActionViewModel("Add tags…", AddTagsToSelectedAsync));
        var tags = Items.Where(item => item.IsSelected).SelectMany(item => item.TagNames).Distinct().Order(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            TagActions.Add(new MenuActionViewModel($"Remove #{tag}", () => RemoveTagFromSelectedAsync(tag)));
        }
    }

    private void RebuildMoveActions(IEnumerable<CollectionSummary> collections)
    {
        MoveActions.Clear();
        MoveActions.Add(new MenuActionViewModel("Uncategorized", () => MoveSelectedAsync(null, "Uncategorized")));
        foreach (var collection in collections.OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase))
        {
            MoveActions.Add(new MenuActionViewModel(collection.Name, () => MoveSelectedAsync(collection.Id, collection.Name)));
        }

        MoveActions.Add(new MenuActionViewModel("New collection…", MoveSelectedToNewCollectionAsync));
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

    partial void OnFilterChanged(LibraryFilter value) => ClearSelection();

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
