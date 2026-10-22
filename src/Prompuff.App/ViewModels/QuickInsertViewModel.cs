using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Platform;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;

namespace Prompuff.App.ViewModels;

/// <summary>A prompt in Quick insert's results.</summary>
public sealed record QuickInsertResult(PromptSummary Prompt)
{
    public string Title => Prompt.Title;
    public string Icon => Prompt.IsFavorite ? "Heart" : "FileText";
    public string Tags => string.Join(" · ", Prompt.Tags);
    public bool HasTags => Prompt.Tags.Count > 0;
}

/// <summary>
/// Quick insert: its hotkey opens a small search over the library from any app. Picking a prompt asks for its
/// variables, if it has any, and inserts the result: copies it, steps back to the app it came from, and pastes it there.
/// </summary>
public sealed partial class QuickInsertViewModel(
    IPromptSearch search,
    PromptService prompts,
    IPromptTemplateService templates,
    RenderValuesCache valuesCache,
    IClipboardService clipboard,
    IInsertTarget target,
    ISettingsStore settings,
    ToastService toasts,
    ILogger<QuickInsertViewModel> logger) : ObservableObject
{
    private const int MaxResults = 8;

    private IReadOnlyList<PromptSummary> _prompts = [];
    private bool _loaded;
    private Prompt? _picked;
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public ObservableCollection<QuickInsertResult> Results { get; } = [];

    /// <summary>One box per variable of the picked prompt, in order of first appearance.</summary>
    public ObservableCollection<VariableViewModel> Fields { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private QuickInsertResult? _selected;

    /// <summary>True once a prompt with variables is picked and its boxes are showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching))]
    private bool _isFilling;

    [ObservableProperty]
    private string _pickedTitle = string.Empty;

    public bool IsSearching => !IsFilling;

    /// <summary>True when the last open ended by inserting rather than Esc.</summary>
    public bool Inserted { get; private set; }

    public bool IsEmpty => _loaded && Results.Count == 0;

    public string EmptyText => _prompts.Count == 0
        ? "Nothing here yet. Quick save a prompt, and it'll be waiting."
        : "Nothing matches. Try a prompt title or a tag.";

    /// <summary>Raised with the prompt's ID as its remembered values change, so an open Render tab can show them.</summary>
    public event EventHandler<Guid>? ValuesChanged;

    /// <summary>Opens before the library is read, so the search box can take the first key typed straight away.</summary>
    public async Task OpenAsync()
    {
        _loaded = false;
        _picked = null;
        Inserted = false;
        Fields.Clear();
        IsFilling = false;
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

    /// <summary>Remembers the app the hotkey was pressed in. Runs before Prompuff comes forward.</summary>
    public void RememberWhereFrom(IntPtr ownWindow) => target.Remember(ownWindow);

    /// <summary>
    /// After Quick insert closes: gives focus back to the app it came from, then sends the window back with
    /// <paramref name="sendWindowBack"/>, and after an insert presses that app's paste key if Paste after inserting
    /// is on. Focus moves first, because Windows only lets the app in front hand it over.
    /// </summary>
    public async Task StepBackAsync(Action sendWindowBack)
    {
        var returned = await target.ReturnAsync();
        sendWindowBack();
        if (!Inserted || _picked is not { } prompt)
        {
            return;
        }

        if (!target.IsSupported)
        {
            // Under Wayland nothing failed: apps can't hand focus to another app or paste into it.
            logger.LogInformation("Quick insert left prompt {PromptId} on the clipboard to paste by hand; this desktop doesn't let apps paste into others", prompt.Id);
        }
        else if (!returned)
        {
            logger.LogInformation("Quick insert left prompt {PromptId} on the clipboard; the app it came from didn't take focus", prompt.Id);
        }
        else if (settings.Load().PasteAfterInserting && target.CanPaste)
        {
            var pasted = await target.PasteAsync();
            logger.LogInformation("Quick insert {Outcome} prompt {PromptId}", pasted ? "pasted" : "couldn't paste", prompt.Id);
        }
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? -1 : Results.IndexOf(Selected);
        Selected = Results[(index + delta + Results.Count) % Results.Count];
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>A prompt without variables is inserted at once; one with variables asks for them first.</summary>
    [RelayCommand]
    private async Task Pick(QuickInsertResult? result)
    {
        result ??= Selected;
        if (result is null || IsFilling)
        {
            return;
        }

        var prompt = await prompts.GetAsync(result.Prompt.Id);
        if (prompt is null)
        {
            return;
        }

        _picked = prompt;
        logger.LogInformation("Quick insert picked prompt {PromptId}", prompt.Id);
        var variables = templates.AnalyzeVariables(prompt.Body);
        if (variables.Count == 0)
        {
            _values = new Dictionary<string, string>(StringComparer.Ordinal);
            await Insert();
            return;
        }

        // The Render tab's own dictionary, so values typed in either place show up in the other.
        _values = await valuesCache.GetAsync(prompt.Id);
        Fields.Clear();
        foreach (var variable in variables)
        {
            Fields.Add(new VariableViewModel(variable.Name, variable.Occurrences, _values.GetValueOrDefault(variable.Name, string.Empty), FieldEdited));
        }

        PickedTitle = prompt.Title;
        IsFilling = true;
    }

    /// <summary>Renders the picked prompt, leaving the {{token}} of any empty value, and copies it.</summary>
    [RelayCommand]
    private async Task Insert()
    {
        if (_picked is not { } prompt)
        {
            return;
        }

        await clipboard.SetTextAsync(templates.Render(prompt.Body, _values));
        logger.LogInformation("Quick insert copied prompt {PromptId}", prompt.Id);
        Inserted = true;
        IsOpen = false;
        toasts.Show("Copied.", $"{prompt.Title} is on your clipboard.");
    }

    // Remembered as they're typed, as on the Render tab.
    private void FieldEdited()
    {
        if (_picked is not { } prompt)
        {
            return;
        }

        foreach (var field in Fields)
        {
            _values[field.Name] = field.Value;
        }

        _ = valuesCache.SaveAsync(prompt.Id);
        ValuesChanged?.Invoke(this, prompt.Id);
    }

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
