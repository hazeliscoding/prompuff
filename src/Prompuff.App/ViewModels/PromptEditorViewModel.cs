using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Controls;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;
using Prompuff.Domain.ValueObjects;

namespace Prompuff.App.ViewModels;

public enum EditorTab
{
    Edit,
    Render,
    History,
}

public sealed record CollectionOption(Guid? Id, string Name)
{
    public static CollectionOption Uncategorized { get; } = new(null, "Uncategorized");

    public string Tone => Id is { } id ? Tones.For(id) : Tones.None;
}

public sealed partial class VariableViewModel : ObservableObject
{
    private readonly Action _changed;

    public VariableViewModel(string name, int occurrences, string value, Action changed)
    {
        Name = name;
        Occurrences = occurrences;
        _value = value;
        _changed = changed;
    }

    public string Name { get; }
    public int Occurrences { get; }
    public string Braced => "{{" + Name + "}}";
    public string UsesLabel => Occurrences.ToString(CultureInfo.InvariantCulture) + "×";
    public string Placeholder => $"Value for {Name}";

    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value) => _changed();
}

public sealed partial class VersionItemViewModel(PromptVersion version, bool isCurrent, DateTimeOffset now, Action<VersionItemViewModel> select) : ObservableObject
{
    public PromptVersion Version { get; } = version;
    public int Number => Version.VersionNumber;
    public string Label => "v" + Number.ToString(CultureInfo.InvariantCulture);
    public string When => Format.Relative(Version.SavedAt, now);
    public string Timestamp => Format.Timestamp(Version.SavedAt);
    public string Note => Version.Note ?? "Saved";
    public bool IsCurrent { get; } = isCurrent;

    [ObservableProperty]
    private bool _isSelected;

    [RelayCommand]
    private void Select() => select(this);
}

public sealed record DiffLineViewModel(string Number, string Mark, string Text, DiffLineKind Kind)
{
    public bool IsAdded => Kind == DiffLineKind.Added;
    public bool IsRemoved => Kind == DiffLineKind.Removed;
}

/// <summary>One prompt open in the detail view: editing, rendering and its history.</summary>
public sealed partial class PromptEditorViewModel : ObservableObject
{
    private readonly PromptService _prompts;
    private readonly CollectionService _collections;
    private readonly IPromptTemplateService _templates;
    private readonly IClipboardService _clipboard;
    private readonly IFilePickerService _files;
    private readonly IPromptTransferService _transfer;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly LibraryNotifier _notifier;
    private readonly Navigator _navigator;
    private readonly RenderValuesCache _valuesCache;
    private readonly TimeProvider _time;
    private readonly ILogger<PromptEditorViewModel> _logger;

    private PromptContent _saved = new(null, null, string.Empty, null);
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _loading;

    public PromptEditorViewModel(
        PromptService prompts,
        CollectionService collections,
        IPromptTemplateService templates,
        IClipboardService clipboard,
        IFilePickerService files,
        IPromptTransferService transfer,
        DialogService dialogs,
        ToastService toasts,
        LibraryNotifier notifier,
        Navigator navigator,
        RenderValuesCache valuesCache,
        TimeProvider time,
        ILogger<PromptEditorViewModel> logger)
    {
        _prompts = prompts;
        _collections = collections;
        _templates = templates;
        _clipboard = clipboard;
        _files = files;
        _transfer = transfer;
        _dialogs = dialogs;
        _toasts = toasts;
        _notifier = notifier;
        _navigator = navigator;
        _valuesCache = valuesCache;
        _time = time;
        _logger = logger;
    }

    public Guid? Id { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionLabel))]
    private bool _isNew = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(CharsLabel))]
    private string _body = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RatingLabel))]
    private int? _rating;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CollectionLabel), nameof(CollectionTone))]
    private CollectionOption _selectedCollection = CollectionOption.Uncategorized;

    [ObservableProperty]
    private string _newTag = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditTab), nameof(IsRenderTab), nameof(IsHistoryTab))]
    private EditorTab _tab = EditorTab.Edit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionLabel))]
    private int _currentVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionLabel))]
    private DateTimeOffset _updatedAt;

    [ObservableProperty]
    private IReadOnlyList<TemplateSegment> _segments = [];

    [ObservableProperty]
    private string _fillLabel = string.Empty;

    [ObservableProperty]
    private bool _isFullyFilled;

    [ObservableProperty]
    private VersionItemViewModel? _selectedVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangesMode))]
    private bool _isFullTextMode;

    [ObservableProperty]
    private string _compareTitle = string.Empty;

    [ObservableProperty]
    private bool _canRestore;

    [ObservableProperty]
    private string _fullTextTitle = string.Empty;

    [ObservableProperty]
    private string? _fullTextDescription;

    [ObservableProperty]
    private string? _fullTextNotes;

    public ObservableCollection<TagChipItem> Tags { get; } = [];
    public ObservableCollection<CollectionOption> CollectionOptions { get; } = [];
    public ObservableCollection<VariableViewModel> Variables { get; } = [];
    public ObservableCollection<VersionItemViewModel> Versions { get; } = [];
    public ObservableCollection<DiffLineViewModel> DiffLines { get; } = [];
    public ObservableCollection<string> ChangeSummaries { get; } = [];

    public PromptContent CurrentContent => new(Title, Description, Body, Notes);
    public bool IsDirty => CurrentContent != _saved;
    public bool HasVariables => Variables.Count > 0;
    public string VariableCountLabel => Format.Count(Variables.Count, "variable") + " detected";
    public string CharsLabel => $"{Body.Length.ToString("N0", CultureInfo.InvariantCulture)} chars · ~{(Body.Length / 4).ToString("N0", CultureInfo.InvariantCulture)} tokens";
    public string RatingLabel => Format.Rating(Rating);
    public string CollectionLabel => SelectedCollection.Name;
    public string CollectionTone => SelectedCollection.Tone;
    public string VersionLabel => IsNew ? "Not saved yet" : $"v{CurrentVersion} · edited {Format.Relative(UpdatedAt, _time.GetUtcNow())}";
    public string VersionCountLabel => Format.Count(Versions.Count, "version") + " saved";
    public bool IsChangesMode => !IsFullTextMode;

    public bool IsEditTab
    {
        get => Tab == EditorTab.Edit;
        set
        {
            if (value)
            {
                Tab = EditorTab.Edit;
            }
        }
    }

    public bool IsRenderTab
    {
        get => Tab == EditorTab.Render;
        set
        {
            if (value)
            {
                Tab = EditorTab.Render;
            }
        }
    }

    public bool IsHistoryTab
    {
        get => Tab == EditorTab.History;
        set
        {
            if (value)
            {
                Tab = EditorTab.History;
            }
        }
    }

    /// <summary>Raised when the view should put the cursor in the title (new prompts) or the first variable.</summary>
    public event EventHandler<string>? FocusRequested;

    public async Task LoadAsync(Guid id)
    {
        var prompt = await _prompts.GetAsync(id) ?? throw new LibraryException("That prompt no longer exists.");
        await LoadCollectionsAsync();
        Apply(prompt);
        await LoadVersionsAsync();
        await _prompts.MarkOpenedAsync(id);
    }

    public async Task InitializeNewAsync(Guid? collectionId)
    {
        await LoadCollectionsAsync();
        _loading = true;
        IsNew = true;
        Id = null;
        SelectedCollection = CollectionOptions.FirstOrDefault(option => option.Id == collectionId) ?? CollectionOption.Uncategorized;
        OnPropertyChanged(nameof(SelectedCollection));
        _saved = CurrentContent;
        _loading = false;
        RefreshVariables();
        OnPropertyChanged(nameof(IsDirty));
        FocusRequested?.Invoke(this, "title");
    }

    /// <summary>Saves content changes. New prompts are created on their first save.</summary>
    /// <param name="quiet">Skip the toast, as when saving on the way out of the editor.</param>
    public async Task<bool> SaveAsync(bool quiet = false)
    {
        try
        {
            var content = CurrentContent;
            if (IsNew)
            {
                if (string.IsNullOrWhiteSpace(Body) && string.IsNullOrWhiteSpace(Title) && quiet)
                {
                    return true; // An untouched new prompt is just discarded.
                }

                var created = await _prompts.CreateAsync(content, Metadata());
                _loading = true;
                Id = created.Id;
                IsNew = false;
                _loading = false;

                // Keep values typed before the first save for when the prompt is opened again.
                var cached = _valuesCache.For(created.Id);
                foreach (var (name, value) in _values)
                {
                    cached[name] = value;
                }

                _values = cached;
                AfterSave(created.Title, created.UpdatedAt, 1, created: true, quiet);
                await LoadVersionsAsync();
                return true;
            }

            var result = await _prompts.SaveContentAsync(Id!.Value, content);
            if (!result.VersionCreated)
            {
                _saved = content;
                OnPropertyChanged(nameof(IsDirty));
                if (!quiet)
                {
                    _toasts.Show("Already saved.", $"Nothing changed since v{result.CurrentVersion}.", isHappy: false);
                }

                return true;
            }

            AfterSave(content.Title, _time.GetUtcNow(), result.CurrentVersion, created: false, quiet);
            await LoadVersionsAsync();
            return true;
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't save this prompt.", exception);
            return false;
        }
    }

    [RelayCommand]
    private Task Save() => SaveAsync();

    [RelayCommand]
    private async Task CopyTemplate()
    {
        await _clipboard.SetTextAsync(Body);
        _toasts.Show("Copied.", HasVariables ? "Template with variables intact." : "The prompt is on your clipboard.");
    }

    [RelayCommand]
    private async Task CopyRendered()
    {
        var rendered = _templates.Render(Body, _values);
        await _clipboard.SetTextAsync(rendered);
        var filled = Variables.Count(variable => !string.IsNullOrEmpty(variable.Value));
        _toasts.Show(
            "Copied rendered prompt.",
            HasVariables
                ? $"{filled} of {Variables.Count} variables filled · {rendered.Length.ToString("N0", CultureInfo.InvariantCulture)} chars"
                : $"{rendered.Length.ToString("N0", CultureInfo.InvariantCulture)} chars");
    }

    /// <summary>Ctrl+Enter: from Edit, open Render; from Render, copy the result.</summary>
    [RelayCommand]
    private async Task RenderOrCopy()
    {
        if (Tab == EditorTab.Render)
        {
            await CopyRendered();
            return;
        }

        Tab = EditorTab.Render;
        if (Variables.Count > 0)
        {
            FocusRequested?.Invoke(this, "variable");
        }
    }

    [RelayCommand]
    private void ToggleFavorite() => IsFavorite = !IsFavorite;

    [RelayCommand]
    private void ShowRender() => Tab = EditorTab.Render;

    [RelayCommand]
    private void ShowHistory() => Tab = EditorTab.History;

    [RelayCommand]
    private Task Back() => _navigator.ShowLibraryAsync();

    [RelayCommand]
    private async Task AddTag()
    {
        var names = NewTag.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(TagName.Normalize)
            .OfType<string>()
            .Where(name => Tags.All(tag => tag.Name != name))
            .ToList();
        NewTag = string.Empty;
        if (names.Count == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            Tags.Add(TagChipItem.For(name));
        }

        await PersistMetadataAsync();
    }

    [RelayCommand]
    private async Task RemoveTag(string name)
    {
        var tag = Tags.FirstOrDefault(item => item.Name == name);
        if (tag is null)
        {
            return;
        }

        Tags.Remove(tag);
        await PersistMetadataAsync();
    }

    [RelayCommand]
    private async Task Duplicate()
    {
        if (!await EnsureSavedAsync())
        {
            return;
        }

        try
        {
            var copy = await _prompts.DuplicateAsync(Id!.Value);
            _notifier.Notify();
            _toasts.Show("Duplicated.", copy.Title);
            await _navigator.OpenPromptAsync(copy.Id);
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't duplicate this prompt.", exception);
        }
    }

    [RelayCommand]
    private async Task Export()
    {
        if (!await EnsureSavedAsync())
        {
            return;
        }

        var path = await _files.PickExportFileAsync(_transfer.SuggestFileName(Title));
        if (path is null)
        {
            return;
        }

        try
        {
            await _transfer.ExportPromptAsync(Id!.Value, path);
            _toasts.Show("Exported.", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't export this prompt.", exception);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (IsNew)
        {
            _saved = CurrentContent;
            await _navigator.ShowLibraryAsync();
            return;
        }

        var versions = Format.Count(Versions.Count, "version");
        if (!await _dialogs.ConfirmAsync(
                $"Delete “{Title}”?",
                $"It moves to Recently deleted with its {versions}, and you can restore it from there for 30 days.",
                "Delete prompt",
                isDanger: true))
        {
            return;
        }

        try
        {
            await _prompts.DeleteAsync(Id!.Value);
            _saved = CurrentContent;
            _notifier.Notify();
            _toasts.Show("Moved to Recently deleted.", Title, isHappy: false);
            await _navigator.ShowLibraryAsync();
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't delete this prompt.", exception);
        }
    }

    [RelayCommand]
    private async Task Restore()
    {
        if (SelectedVersion is not { } version || Id is not { } id)
        {
            return;
        }

        if (IsDirty && !await SaveAsync(quiet: true))
        {
            return;
        }

        try
        {
            var result = await _prompts.RestoreVersionAsync(id, version.Number);
            if (!result.VersionCreated)
            {
                _toasts.Show("Already current.", $"{version.Label} matches what you have now.", isHappy: false);
                return;
            }

            var prompt = await _prompts.GetAsync(id) ?? throw new LibraryException("That prompt no longer exists.");
            Apply(prompt);
            await LoadVersionsAsync();
            _notifier.Notify();
            _toasts.Show($"Restored {version.Label}.", $"Saved as v{result.CurrentVersion}, so nothing is lost.");
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't restore that version.", exception);
        }
    }

    [RelayCommand]
    private async Task DuplicateVersion()
    {
        if (SelectedVersion is not { } version || Id is not { } id)
        {
            return;
        }

        try
        {
            var copy = await _prompts.DuplicateVersionAsync(id, version.Number);
            _notifier.Notify();
            _toasts.Show("Duplicated.", copy.Title);
            await _navigator.OpenPromptAsync(copy.Id);
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't duplicate that version.", exception);
        }
    }

    [RelayCommand]
    private void ShowChanges() => IsFullTextMode = false;

    [RelayCommand]
    private void ShowFullText() => IsFullTextMode = true;

    partial void OnBodyChanged(string value) => RefreshVariables();

    partial void OnIsFavoriteChanged(bool value) => _ = PersistMetadataAsync();

    partial void OnRatingChanged(int? value) => _ = PersistMetadataAsync();

    partial void OnSelectedCollectionChanged(CollectionOption value) => _ = PersistMetadataAsync();

    partial void OnIsFullTextModeChanged(bool value) => RefreshComparison();

    partial void OnSelectedVersionChanged(VersionItemViewModel? value)
    {
        foreach (var item in Versions)
        {
            item.IsSelected = item == value;
        }

        RefreshComparison();
    }

    private PromptMetadata Metadata() => new(IsFavorite, Rating, SelectedCollection.Id, Tags.Select(tag => tag.Name).ToList());

    private async Task PersistMetadataAsync()
    {
        if (_loading || IsNew || Id is not { } id)
        {
            return;
        }

        try
        {
            await _prompts.UpdateMetadataAsync(id, Metadata());
            _notifier.Notify();
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't save that change.", exception);
        }
    }

    private async Task<bool> EnsureSavedAsync() => (!IsDirty && !IsNew) || await SaveAsync(quiet: true) && !IsNew;

    private void AfterSave(string title, DateTimeOffset updatedAt, int version, bool created, bool quiet)
    {
        _saved = CurrentContent;
        CurrentVersion = version;
        UpdatedAt = updatedAt;
        OnPropertyChanged(nameof(IsDirty));
        _notifier.Notify();
        if (!quiet)
        {
            _toasts.Show("Saved. This one cooked.", created ? $"{title} · {SelectedCollection.Name}" : $"{title} · v{version}");
        }
    }

    private async Task LoadCollectionsAsync()
    {
        CollectionOptions.Clear();
        CollectionOptions.Add(CollectionOption.Uncategorized);
        foreach (var collection in await _collections.ListAsync())
        {
            CollectionOptions.Add(new CollectionOption(collection.Id, collection.Name));
        }
    }

    private void Apply(Prompt prompt)
    {
        _loading = true;
        Id = prompt.Id;
        IsNew = false;
        Title = prompt.Title;
        Description = prompt.Description ?? string.Empty;
        Body = prompt.Body;
        Notes = prompt.Notes ?? string.Empty;
        IsFavorite = prompt.IsFavorite;
        Rating = prompt.Rating;
        SelectedCollection = CollectionOptions.FirstOrDefault(option => option.Id == prompt.CollectionId) ?? CollectionOption.Uncategorized;
        OnPropertyChanged(nameof(SelectedCollection));
        Tags.Clear();
        foreach (var tag in prompt.Tags)
        {
            Tags.Add(TagChipItem.For(tag));
        }

        UpdatedAt = prompt.UpdatedAt;
        _values = _valuesCache.For(prompt.Id);
        _saved = CurrentContent;
        _loading = false;
        RefreshVariables();
        OnPropertyChanged(nameof(IsDirty));
    }

    private async Task LoadVersionsAsync()
    {
        if (Id is not { } id)
        {
            return;
        }

        var versions = await _prompts.GetVersionsAsync(id);
        var now = _time.GetUtcNow();
        Versions.Clear();
        foreach (var version in versions)
        {
            Versions.Add(new VersionItemViewModel(version, version == versions[0], now, item => SelectedVersion = item));
        }

        CurrentVersion = versions.Count > 0 ? versions[0].VersionNumber : 0;
        OnPropertyChanged(nameof(VersionCountLabel));
        SelectedVersion = Versions.FirstOrDefault();
    }

    private void RefreshVariables()
    {
        var analyzed = _templates.AnalyzeVariables(Body);
        if (!analyzed.Select(variable => (variable.Name, variable.Occurrences))
                .SequenceEqual(Variables.Select(variable => (variable.Name, variable.Occurrences))))
        {
            Variables.Clear();
            foreach (var variable in analyzed)
            {
                Variables.Add(new VariableViewModel(
                    variable.Name,
                    variable.Occurrences,
                    _values.GetValueOrDefault(variable.Name, string.Empty),
                    RefreshRender));
            }

            OnPropertyChanged(nameof(HasVariables));
            OnPropertyChanged(nameof(VariableCountLabel));
        }

        RefreshRender();
    }

    private void RefreshRender()
    {
        foreach (var variable in Variables)
        {
            _values[variable.Name] = variable.Value;
        }

        Segments = _templates.RenderSegments(Body, _values);
        var filled = Variables.Count(variable => !string.IsNullOrEmpty(variable.Value));
        IsFullyFilled = filled == Variables.Count;
        FillLabel = Variables.Count == 0 ? "no variables" : $"{filled} of {Variables.Count} filled";
    }

    private void RefreshComparison()
    {
        DiffLines.Clear();
        ChangeSummaries.Clear();
        if (SelectedVersion is not { } selected)
        {
            CanRestore = false;
            return;
        }

        var version = selected.Version;
        var previous = Versions.Select(item => item.Version)
            .Where(candidate => candidate.VersionNumber < version.VersionNumber)
            .MaxBy(candidate => candidate.VersionNumber);

        CompareTitle = previous is null
            ? $"{selected.Label} · first version"
            : $"{selected.Label} · changes from v{previous.VersionNumber}";
        CanRestore = version.Content != _saved;
        FullTextTitle = version.Title;
        FullTextDescription = version.Description;
        FullTextNotes = version.Notes;

        if (IsFullTextMode)
        {
            var number = 1;
            foreach (var line in version.Body.Split('\n'))
            {
                DiffLines.Add(new DiffLineViewModel(number++.ToString(CultureInfo.InvariantCulture), string.Empty, line, DiffLineKind.Unchanged));
            }

            return;
        }

        if (previous is not null)
        {
            if (previous.Title != version.Title)
            {
                ChangeSummaries.Add($"Title was “{previous.Title}”");
            }

            if (previous.Description != version.Description)
            {
                ChangeSummaries.Add("Description changed");
            }

            if (previous.Notes != version.Notes)
            {
                ChangeSummaries.Add("Notes changed");
            }
        }

        foreach (var line in LineDiff.Compute(previous?.Body, version.Body))
        {
            DiffLines.Add(new DiffLineViewModel(
                (line.NewNumber ?? line.OldNumber)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                line.Kind switch { DiffLineKind.Added => "+", DiffLineKind.Removed => "−", _ => string.Empty },
                line.Text,
                line.Kind));
        }
    }

    private async Task ReportAsync(string title, Exception exception)
    {
        if (exception is LibraryException library)
        {
            await _dialogs.ShowErrorAsync(title, library.Message, library.InnerException?.ToString());
            return;
        }

        _logger.LogError(exception, "Editor action failed for prompt {PromptId}", Id);
        await _dialogs.ShowErrorAsync(title, "Something went wrong. Your library hasn't been changed.", exception.ToString());
    }
}
