using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Controls;
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
        EditedLabel = Format.Relative(summary.UpdatedAt, now);
        EditedTooltip = "Edited " + Format.Timestamp(summary.UpdatedAt);
        _isFavorite = summary.IsFavorite;
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

    [ObservableProperty]
    private bool _isFavorite;

    [RelayCommand]
    private Task Open() => _owner.OpenAsync(this);

    [RelayCommand]
    private Task ToggleFavorite() => _owner.ToggleFavoriteAsync(this);
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
    public bool CanSort => Filter.Kind != PromptFilterKind.Recent;
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
            _ => "All prompts",
        };

        Subtitle = string.IsNullOrWhiteSpace(SearchText)
            ? Format.Count(Items.Count, "prompt")
            : $"{Format.Count(Items.Count, "match", "matches")} for “{SearchText.Trim()}”";

        IsLibraryEmpty = _libraryTotal == 0;
        IsEmpty = Items.Count == 0;
        (EmptyTitle, EmptyMessage) = (IsLibraryEmpty, Filter.Kind, string.IsNullOrWhiteSpace(SearchText)) switch
        {
            (true, _, _) => ("Your good prompts deserve a home.", "Nothing here yet. Time to stash something smart."),
            (_, _, false) => ("Nothing matches that.", "Try fewer words, or search a tag with #name."),
            (_, PromptFilterKind.Favorites, _) => ("No favorites yet.", "Tap the heart on a prompt that keeps earning its place."),
            (_, PromptFilterKind.Collection, _) => ("No prompts here yet.", "Give one a place to live from its editor, or make a new one here."),
            _ => ("No prompts here yet.", "Time to stash something smart."),
        };

        OnPropertyChanged(nameof(HasFilter));
        OnPropertyChanged(nameof(CanSort));
    }

    public Task OpenAsync(PromptCardViewModel card) => _navigator.OpenPromptAsync(card.Id);

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
