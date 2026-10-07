using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Controls;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;

namespace Prompuff.App.ViewModels;

public sealed partial class NavItemViewModel(string label, string icon, LibraryFilter filter, Navigator navigator) : ObservableObject
{
    public string Label { get; } = label;
    public string Icon { get; } = icon;
    public LibraryFilter Filter { get; } = filter;

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private Task Select() => navigator.ShowLibraryAsync(Filter);
}

public sealed partial class CollectionItemViewModel : ObservableObject
{
    private readonly SidebarViewModel _owner;

    public CollectionItemViewModel(SidebarViewModel owner, Guid? id, string name, int count)
    {
        _owner = owner;
        Id = id;
        Name = name;
        Count = count;
        Tone = id is { } value ? Tones.For(value) : Tones.None;
        Filter = id is { } collectionId
            ? new LibraryFilter(PromptFilterKind.Collection, collectionId)
            : new LibraryFilter(PromptFilterKind.Uncategorized);
    }

    public Guid? Id { get; }
    public string Name { get; }
    public int Count { get; }
    public string Tone { get; }
    public LibraryFilter Filter { get; }
    public bool IsUncategorized => Id is null;
    public bool CanEdit => Id is not null;

    [ObservableProperty]
    private bool _isActive;

    [RelayCommand]
    private Task Select() => _owner.Navigator.ShowLibraryAsync(Filter);

    [RelayCommand]
    private Task Rename() => _owner.RenameCollectionAsync(this);

    [RelayCommand]
    private Task Delete() => _owner.DeleteCollectionAsync(this);

    [RelayCommand]
    private Task NewPromptHere() => _owner.Navigator.NewPromptAsync(Id);
}

public sealed partial class TagItemViewModel(string name, int count, Navigator navigator) : ObservableObject
{
    public string Name { get; } = name;
    public int Count { get; } = count;
    public string Tone { get; } = Tones.For(name);
    public string Tooltip => Format.Count(Count, "prompt");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipTone))]
    private bool _isActive;

    public string ChipTone => IsActive ? Tone : Tones.None;

    [RelayCommand]
    private Task Select() =>
        navigator.ShowLibraryAsync(IsActive ? LibraryFilter.All : new LibraryFilter(PromptFilterKind.Tag, Tag: Name));
}

/// <summary>The left column: library views, collections, tags and the settings link.</summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly IPromptRepository _prompts;
    private readonly CollectionService _collections;
    private readonly ITagRepository _tags;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly LibraryNotifier _notifier;
    private readonly ILogger<SidebarViewModel> _logger;
    private LibraryFilter? _activeFilter = LibraryFilter.All;

    public SidebarViewModel(
        IPromptRepository prompts,
        CollectionService collections,
        ITagRepository tags,
        Navigator navigator,
        DialogService dialogs,
        ToastService toasts,
        LibraryNotifier notifier,
        ILogger<SidebarViewModel> logger)
    {
        _prompts = prompts;
        _collections = collections;
        _tags = tags;
        Navigator = navigator;
        _dialogs = dialogs;
        _toasts = toasts;
        _notifier = notifier;
        _logger = logger;

        All = new NavItemViewModel("All prompts", "Library", LibraryFilter.All, navigator);
        Favorites = new NavItemViewModel("Favorites", "Heart", new LibraryFilter(PromptFilterKind.Favorites), navigator);
        Recent = new NavItemViewModel("Recent", "Clock", new LibraryFilter(PromptFilterKind.Recent), navigator);
        NavItems = [All, Favorites, Recent];
        RecentlyDeleted = new NavItemViewModel("Recently deleted", "Trash2", new LibraryFilter(PromptFilterKind.Deleted), navigator);
    }

    public Navigator Navigator { get; }
    public NavItemViewModel All { get; }
    public NavItemViewModel Favorites { get; }
    public NavItemViewModel Recent { get; }
    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    /// <summary>Shown at the bottom of the sidebar while it holds prompts or is open.</summary>
    public NavItemViewModel RecentlyDeleted { get; }
    public ObservableCollection<CollectionItemViewModel> Collections { get; } = [];
    public ObservableCollection<TagItemViewModel> Tags { get; } = [];
    public bool HasTags => Tags.Count > 0;

    [ObservableProperty]
    private bool _isSettingsActive;

    [ObservableProperty]
    private bool _showRecentlyDeleted;

    [ObservableProperty]
    private string _footer = "Stored on this device";

    public async Task RefreshAsync()
    {
        var counts = await _prompts.GetCountsAsync();
        All.Count = counts.All;
        Favorites.Count = counts.Favorites;
        Recent.Count = Math.Min(counts.All, PromptQuery.RecentLimit);
        RecentlyDeleted.Count = counts.Deleted;
        Footer = $"Stored on this device · {Format.Count(counts.All, "prompt")}";

        Collections.Clear();
        foreach (var collection in await _collections.ListAsync())
        {
            Collections.Add(new CollectionItemViewModel(this, collection.Id, collection.Name, collection.PromptCount));
        }

        if (counts.Uncategorized > 0)
        {
            Collections.Add(new CollectionItemViewModel(this, null, "Uncategorized", counts.Uncategorized));
        }

        Tags.Clear();
        foreach (var tag in await _tags.ListAsync())
        {
            Tags.Add(new TagItemViewModel(tag.Name, tag.PromptCount, Navigator));
        }

        OnPropertyChanged(nameof(HasTags));
        ApplyActive();
    }

    /// <param name="filter">The library filter on screen, or null when the library isn't showing.</param>
    public void SetActive(LibraryFilter? filter, bool settingsActive)
    {
        _activeFilter = filter;
        IsSettingsActive = settingsActive;
        ApplyActive();
    }

    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        var name = await _dialogs.AskTextAsync(
            "New collection",
            "Give your prompts a place to live.",
            "Create",
            placeholder: "Coding, Design, Writing…");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunAsync("Couldn't create that collection.", async () =>
        {
            var collection = await _collections.CreateAsync(name);
            _notifier.Notify();
            await Navigator.ShowLibraryAsync(new LibraryFilter(PromptFilterKind.Collection, collection.Id));
        });
    }

    [RelayCommand]
    private Task OpenSettings() => Navigator.ShowSettingsAsync();

    internal async Task RenameCollectionAsync(CollectionItemViewModel item)
    {
        if (item.Id is not { } id)
        {
            return;
        }

        var name = await _dialogs.AskTextAsync("Rename collection", $"Pick a new name for “{item.Name}”.", "Rename", item.Name);
        if (string.IsNullOrWhiteSpace(name) || name == item.Name)
        {
            return;
        }

        await RunAsync("Couldn't rename that collection.", async () =>
        {
            await _collections.RenameAsync(id, name);
            _notifier.Notify();
        });
    }

    internal async Task DeleteCollectionAsync(CollectionItemViewModel item)
    {
        if (item.Id is not { } id)
        {
            return;
        }

        var message = item.Count switch
        {
            0 => "It's empty, so nothing else changes.",
            1 => "Its one prompt stays in your library as uncategorized.",
            _ => $"Its {item.Count} prompts stay in your library as uncategorized.",
        };
        if (!await _dialogs.ConfirmAsync($"Delete “{item.Name}”?", message, "Delete collection", isDanger: true))
        {
            return;
        }

        await RunAsync("Couldn't delete that collection.", async () =>
        {
            await _collections.DeleteAsync(id);
            _notifier.Notify();
            _toasts.Show("Collection deleted.", item.Count == 0 ? item.Name : $"{Format.Count(item.Count, "prompt")} moved to Uncategorized.", isHappy: false);
            if (_activeFilter?.CollectionId == id)
            {
                await Navigator.ShowLibraryAsync(LibraryFilter.All);
            }
        });
    }

    private void ApplyActive()
    {
        foreach (var item in NavItems)
        {
            item.IsActive = _activeFilter is { } filter && filter.Kind == item.Filter.Kind && filter.Kind is not PromptFilterKind.Collection and not PromptFilterKind.Uncategorized and not PromptFilterKind.Tag;
        }

        RecentlyDeleted.IsActive = _activeFilter is { Kind: PromptFilterKind.Deleted };
        ShowRecentlyDeleted = RecentlyDeleted.Count > 0 || RecentlyDeleted.IsActive;

        foreach (var collection in Collections)
        {
            collection.IsActive = _activeFilter is { } filter && filter == collection.Filter;
        }

        foreach (var tag in Tags)
        {
            tag.IsActive = _activeFilter is { Kind: PromptFilterKind.Tag } filter && filter.Tag == tag.Name;
        }
    }

    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (LibraryException exception)
        {
            await _dialogs.ShowErrorAsync(failureTitle, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Collection action failed");
            await _dialogs.ShowErrorAsync(failureTitle, "Something went wrong. Your library hasn't been changed.", exception.ToString());
        }
    }
}
