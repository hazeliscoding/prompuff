using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.ViewModels;

/// <summary>A prompt in Quick insert's results.</summary>
public sealed record QuickInsertResult(PromptSummary Prompt)
{
    public string Title => Prompt.Title;
    public string Icon => Prompt.IsFavorite ? "Heart" : "FileText";
    public string Tags => string.Join(" · ", Prompt.Tags);
    public bool HasTags => Prompt.Tags.Count > 0;
}

/// <summary>Quick insert: its hotkey opens a small search over the library from any app.</summary>
public sealed partial class QuickInsertViewModel(IPromptSearch search, ILogger<QuickInsertViewModel> logger) : ObservableObject
{
    private const int MaxResults = 8;

    private IReadOnlyList<PromptSummary> _prompts = [];
    private bool _loaded;

    public ObservableCollection<QuickInsertResult> Results { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private QuickInsertResult? _selected;

    public bool IsEmpty => _loaded && Results.Count == 0;

    public string EmptyText => _prompts.Count == 0
        ? "Nothing here yet. Quick save a prompt, and it'll be waiting."
        : "Nothing matches. Try a prompt title or a tag.";

    /// <summary>Opens before the library is read, so the search box can take the first key typed straight away.</summary>
    public async Task OpenAsync()
    {
        _loaded = false;
        Query = string.Empty;
        IsOpen = true;
        try
        {
            _prompts = await search.SearchAsync(new PromptQuery { Sort = PromptSort.RecentActivity });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't read the library for Quick insert");
            _prompts = [];
        }

        _loaded = true;
        Rebuild();
        logger.LogInformation("Quick insert opened with {Count} prompts", _prompts.Count);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    partial void OnQueryChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var query = Query.Trim();
        var tag = query.TrimStart('#');
        var matches = query.Length == 0
            ? _prompts
            : _prompts.Where(prompt => CommandPaletteViewModel.Matches(prompt.Title, query)
                                       || prompt.Tags.Any(name => name.Contains(tag, StringComparison.OrdinalIgnoreCase)));

        Results.Clear();
        foreach (var prompt in matches.Take(MaxResults))
        {
            Results.Add(new QuickInsertResult(prompt));
        }

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyText));
    }
}
