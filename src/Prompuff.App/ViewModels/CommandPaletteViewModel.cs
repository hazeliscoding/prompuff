using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Prompuff.App.Platform;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;

namespace Prompuff.App.ViewModels;

public sealed record PaletteItem(string Label, string Group, string Icon, Func<Task> Run, string? Hint = null, string? Shortcut = null)
{
    public bool HasHint => !string.IsNullOrEmpty(Hint);
    public bool HasShortcut => !string.IsNullOrEmpty(Shortcut);
    public bool StartsGroup { get; init; }
    public string GroupLabel => Group.ToUpperInvariant();
}

/// <summary>Ctrl+K: jump to a prompt, copy it, or run a command.</summary>
public sealed partial class CommandPaletteViewModel(
    IPromptSearch search,
    ITagRepository tags,
    PromptService prompts,
    IClipboardService clipboard,
    Navigator navigator,
    ToastService toasts) : ObservableObject
{
    private IReadOnlyList<PromptSummary> _prompts = [];
    private IReadOnlyList<TagSummary> _tags = [];

    public ObservableCollection<PaletteItem> Items { get; } = [];

    /// <summary>Set by the shell: new prompt, quick save, import and so on.</summary>
    public Func<string, Task>? RunAction { get; set; }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteItem? _selected;

    public bool IsEmpty => Items.Count == 0;

    public async Task OpenAsync()
    {
        _prompts = await search.SearchAsync(new PromptQuery { Sort = PromptSort.RecentActivity });
        _tags = await tags.ListAsync();
        Query = string.Empty;
        Rebuild();
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task Run(PaletteItem? item)
    {
        item ??= Selected;
        if (item is null)
        {
            return;
        }

        IsOpen = false;
        await item.Run();
    }

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Items.IndexOf(Selected);
        Selected = Items[(index + delta + Items.Count) % Items.Count];
    }

    partial void OnQueryChanged(string value) => Rebuild();

    private void Rebuild()
    {
        var query = Query.Trim();
        var items = new List<PaletteItem>();

        var matches = (query.Length == 0
                ? _prompts.Take(5)
                : _prompts.Where(prompt => Matches(prompt.Title, query) || prompt.Tags.Any(tag => tag.Contains(query.TrimStart('#'), StringComparison.OrdinalIgnoreCase))))
            .Take(6)
            .ToList();

        foreach (var prompt in matches)
        {
            items.Add(new PaletteItem(prompt.Title, "Prompts", prompt.IsFavorite ? "Heart" : "FileText",
                () => navigator.OpenPromptAsync(prompt.Id), Hint: string.Join(" · ", prompt.Tags)));
        }

        foreach (var prompt in matches.Take(query.Length == 0 ? 3 : 6))
        {
            items.Add(new PaletteItem("Copy " + prompt.Title, "Copy", "Copy", () => CopyAsync(prompt)));
        }

        if (query.Length > 0)
        {
            items.Add(new PaletteItem($"Search the library for “{query}”", "Actions", "Search", () => RunAction?.Invoke("search:" + query) ?? Task.CompletedTask));
        }

        var actions = new (string Label, string Icon, string Id, string? Shortcut)[]
        {
            ("New prompt", "Plus", "new", Shortcuts.Display(ShortcutAction.NewPrompt)),
            ("Quick save from clipboard", "ClipboardPlus", "quick-save", Shortcuts.Display(ShortcutAction.QuickSave)),
            ("Go to favorites", "Heart", "favorites", null),
            ("Go to recent", "Clock", "recent", null),
            ("New collection", "FolderPlus", "new-collection", null),
            ("Import Markdown", "Download", "import", null),
            ("Import a folder", "FolderOpen", "import-folder", null),
            ("Open settings", "Settings", "settings", null),
        };
        foreach (var action in actions.Where(action => query.Length == 0 || Matches(action.Label, query)))
        {
            var id = action.Id;
            items.Add(new PaletteItem(action.Label, "Actions", action.Icon, () => RunAction?.Invoke(id) ?? Task.CompletedTask, Shortcut: action.Shortcut));
        }

        if (query.Length > 0)
        {
            foreach (var tag in _tags.Where(tag => tag.Name.Contains(query.TrimStart('#'), StringComparison.OrdinalIgnoreCase)).Take(4))
            {
                items.Add(new PaletteItem($"Filter by #{tag.Name}", "Tags", "Hash",
                    () => navigator.ShowLibraryAsync(new LibraryFilter(PromptFilterKind.Tag, Tag: tag.Name)),
                    Hint: Format.Count(tag.PromptCount, "prompt")));
            }
        }

        Items.Clear();
        string? group = null;
        foreach (var item in items)
        {
            Items.Add(item with { StartsGroup = item.Group != group });
            group = item.Group;
        }

        Selected = Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task CopyAsync(PromptSummary summary)
    {
        var prompt = await prompts.GetAsync(summary.Id);
        if (prompt is null)
        {
            return;
        }

        await clipboard.SetTextAsync(prompt.Body);
        toasts.Show("Copied.", $"{prompt.Title} is on your clipboard.");
    }

    /// <summary>Case-insensitive substring match, or every query letter in order ("aup" finds "Angular Upgrade Planner").</summary>
    private static bool Matches(string text, string query)
    {
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var position = 0;
        foreach (var ch in query.Where(ch => !char.IsWhiteSpace(ch)))
        {
            position = text.IndexOf(ch.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (position < 0)
            {
                return false;
            }

            position++;
        }

        return query.Length >= 2;
    }
}
