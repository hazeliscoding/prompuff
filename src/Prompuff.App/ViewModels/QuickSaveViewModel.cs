using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Controls;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.ViewModels;

public enum QuickSaveIntent
{
    WorkedWell,
    Template,
    Idea,
}

public sealed partial class SelectableTag(string name) : ObservableObject
{
    public string Name { get; } = name;
    public string Tone { get; } = Tones.For(name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipTone))]
    private bool _isSelected;

    public string ChipTone => IsSelected ? Tone : Tones.None;

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}

/// <summary>Fast capture: starts from the clipboard and saves in one step.</summary>
public sealed partial class QuickSaveViewModel : ObservableObject
{
    private readonly PromptService _prompts;
    private readonly CollectionService _collections;
    private readonly ITagRepository _tags;
    private readonly IClipboardService _clipboard;
    private readonly ToastService _toasts;
    private readonly DialogService _dialogs;
    private readonly LibraryNotifier _notifier;
    private readonly ILogger<QuickSaveViewModel> _logger;

    public QuickSaveViewModel(
        PromptService prompts,
        CollectionService collections,
        ITagRepository tags,
        IClipboardService clipboard,
        ToastService toasts,
        DialogService dialogs,
        LibraryNotifier notifier,
        ILogger<QuickSaveViewModel> logger)
    {
        _prompts = prompts;
        _collections = collections;
        _tags = tags;
        _clipboard = clipboard;
        _toasts = toasts;
        _dialogs = dialogs;
        _notifier = notifier;
        _logger = logger;
    }

    public ObservableCollection<CollectionOption> CollectionOptions { get; } = [];
    public ObservableCollection<SelectableTag> TagOptions { get; } = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceLabel))]
    private string _text = string.Empty;

    [ObservableProperty]
    private bool _fromClipboard;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private CollectionOption? _selectedCollection;

    [ObservableProperty]
    private string _newTag = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkedWell), nameof(IsTemplate), nameof(IsIdea))]
    private QuickSaveIntent _intent = QuickSaveIntent.WorkedWell;

    [ObservableProperty]
    private string _why = string.Empty;

    [ObservableProperty]
    private string? _error;

    public string SourceLabel => FromClipboard && Text.Length > 0
        ? $"From clipboard · {Text.Length.ToString("N0", CultureInfo.InvariantCulture)} chars"
        : "Paste or type the prompt below";

    public bool IsWorkedWell
    {
        get => Intent == QuickSaveIntent.WorkedWell;
        set
        {
            if (value)
            {
                Intent = QuickSaveIntent.WorkedWell;
            }
        }
    }

    public bool IsTemplate
    {
        get => Intent == QuickSaveIntent.Template;
        set
        {
            if (value)
            {
                Intent = QuickSaveIntent.Template;
            }
        }
    }

    public bool IsIdea
    {
        get => Intent == QuickSaveIntent.Idea;
        set
        {
            if (value)
            {
                Intent = QuickSaveIntent.Idea;
            }
        }
    }

    public async Task OpenAsync(Guid? collectionId = null)
    {
        var clipboardText = string.Empty;
        try
        {
            clipboardText = (await _clipboard.GetTextAsync())?.Trim() ?? string.Empty;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Couldn't read the clipboard for quick save");
        }

        FromClipboard = clipboardText.Length > 0;
        Text = clipboardText;
        Title = SuggestTitle(clipboardText);
        Why = string.Empty;
        NewTag = string.Empty;
        Error = null;
        Intent = QuickSaveIntent.WorkedWell;

        CollectionOptions.Clear();
        CollectionOptions.Add(CollectionOption.Uncategorized);
        foreach (var collection in await _collections.ListAsync())
        {
            CollectionOptions.Add(new CollectionOption(collection.Id, collection.Name));
        }

        // Clear first: a combo box that saw this value while its list was empty ignores setting it again.
        SelectedCollection = null;
        SelectedCollection = CollectionOptions.FirstOrDefault(option => option.Id == collectionId) ?? CollectionOption.Uncategorized;

        TagOptions.Clear();
        foreach (var tag in (await _tags.ListAsync()).Take(8))
        {
            TagOptions.Add(new SelectableTag(tag.Name));
        }

        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void AddTag()
    {
        foreach (var name in NewTag.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(TagName.Normalize).OfType<string>())
        {
            var existing = TagOptions.FirstOrDefault(tag => tag.Name == name);
            if (existing is null)
            {
                existing = new SelectableTag(name);
                TagOptions.Add(existing);
            }

            existing.IsSelected = true;
        }

        NewTag = string.Empty;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (string.IsNullOrWhiteSpace(Text))
        {
            Error = "Paste or type the prompt first.";
            return;
        }

        AddTag();
        var tags = TagOptions.Where(tag => tag.IsSelected).Select(tag => tag.Name).ToList();
        switch (Intent)
        {
            case QuickSaveIntent.Template:
                tags.Add("template");
                break;
            case QuickSaveIntent.Idea:
                tags.Add("idea");
                break;
        }

        try
        {
            var prompt = await _prompts.CreateAsync(
                new PromptContent(string.IsNullOrWhiteSpace(Title) ? SuggestTitle(Text) : Title, null, Text, Why),
                new PromptMetadata(false, Intent == QuickSaveIntent.WorkedWell ? 4 : null, SelectedCollection?.Id, tags),
                "Quick save");
            IsOpen = false;
            _notifier.Notify();
            _toasts.Show("Saved. This one cooked.", $"{prompt.Title} · {(SelectedCollection ?? CollectionOption.Uncategorized).Name}");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Quick save failed");
            await _dialogs.ShowErrorAsync("Couldn't save that prompt.", "Something went wrong. Your library hasn't been changed.", exception.ToString());
        }
    }

    partial void OnTextChanged(string value) => Error = null;

    /// <summary>The first meaningful line, trimmed to a title-sized length.</summary>
    internal static string SuggestTitle(string text)
    {
        var line = text.Split('\n').Select(candidate => candidate.Trim().TrimStart('#', '-', '*', '>').Trim())
            .FirstOrDefault(candidate => candidate.Length > 0) ?? string.Empty;
        if (line.Length <= 60)
        {
            return line;
        }

        var cut = line[..60];
        var space = cut.LastIndexOf(' ');
        return (space > 30 ? cut[..space] : cut).TrimEnd(',', '.', ':', ';') + "…";
    }
}
