using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.Application;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Services;
using Prompuff.Domain.Entities;

namespace Prompuff.App.ViewModels;

public sealed partial class WorkflowCardViewModel(WorkflowSummary summary, DateTimeOffset now, Navigator navigator)
{
    public Guid Id { get; } = summary.Id;
    public string Name { get; } = summary.Name;
    public string? Description { get; } = summary.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string StepCountLabel { get; } = Format.Count(summary.StepCount, "step");

    /// <summary>The steps at a glance: "Planner → Phase Runner → Checker".</summary>
    public string Chain { get; } = summary.StepCount == 0 ? "No steps yet" : string.Join("  →  ", summary.StepTitles);

    public string EditedLabel { get; } = Format.Relative(summary.UpdatedAt, now);

    [RelayCommand]
    private Task Open() => navigator.OpenWorkflowAsync(Id);
}

/// <summary>The Workflows page: every workflow, newest first.</summary>
public sealed partial class WorkflowsViewModel(
    WorkflowService workflows,
    Navigator navigator,
    DialogService dialogs,
    LibraryNotifier notifier,
    AppearanceState appearance,
    TimeProvider time) : ObservableObject
{
    public AppearanceState Appearance { get; } = appearance;
    public ObservableCollection<WorkflowCardViewModel> Items { get; } = [];

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    public async Task RefreshAsync()
    {
        var now = time.GetUtcNow();
        var summaries = await workflows.ListAsync();
        Items.Clear();
        foreach (var summary in summaries)
        {
            Items.Add(new WorkflowCardViewModel(summary, now, navigator));
        }

        Subtitle = Format.Count(Items.Count, "workflow") + " · prompts that run in order";
        IsEmpty = Items.Count == 0;
    }

    [RelayCommand]
    public async Task NewWorkflow()
    {
        var name = await dialogs.AskTextAsync(
            "New workflow",
            "Prompts that work best in order, like plan, build, then check. You copy each step into your model.",
            "Create",
            placeholder: "Angular upgrade, start to finish");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var workflow = await workflows.CreateAsync(name);
        notifier.Notify();
        await navigator.OpenWorkflowAsync(workflow.Id);
    }
}

public enum WorkflowTab
{
    Steps,
    Run,
}

public sealed partial class WorkflowStepViewModel : ObservableObject
{
    private readonly WorkflowViewModel _owner;

    public WorkflowStepViewModel(WorkflowViewModel owner, WorkflowStep step, Prompt? prompt, int number, IReadOnlyList<string> variables)
    {
        _owner = owner;
        Id = step.Id;
        PromptId = step.PromptId;
        Number = number;
        Title = prompt?.Title ?? "Missing prompt";
        Body = prompt?.Body ?? string.Empty;
        IsPromptDeleted = prompt?.DeletedAt is not null;
        Variables = variables.Select(name => "{{" + name + "}}").ToList();
        _note = step.Note ?? string.Empty;
    }

    public Guid Id { get; }
    public Guid PromptId { get; }
    public int Number { get; }
    public string NumberLabel => Number.ToString(CultureInfo.InvariantCulture);
    public string Title { get; }
    public string Body { get; }
    public bool IsPromptDeleted { get; }
    public IReadOnlyList<string> Variables { get; }
    public bool HasVariables => Variables.Count > 0;
    public bool IsFirst => Number == 1;
    public bool IsLast => Number == _owner.Steps.Count;
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note;

    /// <summary>The step the Run tab is showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>Copied on the Run tab during this visit.</summary>
    [ObservableProperty]
    private bool _isCopied;

    /// <summary>Called when steps are added or removed, since IsLast depends on the count.</summary>
    public void RefreshPosition() => OnPropertyChanged(nameof(IsLast));

    partial void OnNoteChanged(string value) => _owner.ScheduleSave();

    [RelayCommand]
    private Task MoveUp() => _owner.MoveStepAsync(this, -1);

    [RelayCommand]
    private Task MoveDown() => _owner.MoveStepAsync(this, 1);

    [RelayCommand]
    private Task Remove() => _owner.RemoveStepAsync(this);

    [RelayCommand]
    private Task OpenPrompt() => _owner.OpenPromptAsync(this);

    [RelayCommand]
    private void Select() => _owner.SelectStep(this);
}

public sealed partial class WorkflowVariableViewModel(string name, IReadOnlyList<int> steps, string value, Action changed) : ObservableObject
{
    public string Name { get; } = name;
    public string Braced => "{{" + Name + "}}";

    /// <summary>"Steps 1, 2 and 3", so it's clear one value fills them all.</summary>
    public string UsedIn { get; } = steps.Count == 1
        ? $"Step {steps[0]}"
        : $"Steps {string.Join(", ", steps.Take(steps.Count - 1))} and {steps[^1]}";

    public bool IsShared { get; } = steps.Count > 1;
    public string Placeholder => $"Value for {name}";

    [ObservableProperty]
    private string _value = value;

    partial void OnValueChanged(string value) => changed();
}

public sealed partial class PromptPickViewModel(PromptSummary prompt, Func<PromptSummary, Task> add)
{
    public string Title { get; } = prompt.Title;
    public string? Description { get; } = prompt.Description;

    [RelayCommand]
    private Task Add() => add(prompt);
}

/// <summary>
/// One workflow: its steps, edited on the Steps tab, and the Run tab, where shared variables are filled in once and
/// each step is copied in turn. Prompuff never runs a model; the person carries each answer to the next step.
/// </summary>
public sealed partial class WorkflowViewModel : ObservableObject
{
    private readonly WorkflowService _workflows;
    private readonly PromptService _prompts;
    private readonly IPromptSearch _search;
    private readonly IPromptTemplateService _templates;
    private readonly IClipboardService _clipboard;
    private readonly IFilePickerService _files;
    private readonly IPromptTransferService _transfer;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly LibraryNotifier _notifier;
    private readonly Navigator _navigator;
    private readonly ILogger<WorkflowViewModel> _logger;
    private readonly SemaphoreSlim _valueWrites = new(1, 1);
    private Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<PromptSummary> _library = [];
    private CancellationTokenSource? _saveDelay;
    private bool _dirty;
    private bool _loading;

    public WorkflowViewModel(
        WorkflowService workflows,
        PromptService prompts,
        IPromptSearch search,
        IPromptTemplateService templates,
        IClipboardService clipboard,
        IFilePickerService files,
        IPromptTransferService transfer,
        DialogService dialogs,
        ToastService toasts,
        LibraryNotifier notifier,
        Navigator navigator,
        ILogger<WorkflowViewModel> logger)
    {
        _workflows = workflows;
        _prompts = prompts;
        _search = search;
        _templates = templates;
        _clipboard = clipboard;
        _files = files;
        _transfer = transfer;
        _dialogs = dialogs;
        _toasts = toasts;
        _notifier = notifier;
        _navigator = navigator;
        _logger = logger;
    }

    public Guid Id { get; private set; }
    public ObservableCollection<WorkflowStepViewModel> Steps { get; } = [];
    public ObservableCollection<WorkflowVariableViewModel> Variables { get; } = [];
    public ObservableCollection<PromptPickViewModel> PickerResults { get; } = [];

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepsTab), nameof(IsRunTab))]
    private WorkflowTab _tab = WorkflowTab.Steps;

    [ObservableProperty]
    private WorkflowStepViewModel? _currentStep;

    [ObservableProperty]
    private IReadOnlyList<TemplateSegment> _segments = [];

    [ObservableProperty]
    private string _fillLabel = string.Empty;

    [ObservableProperty]
    private bool _isFullyFilled;

    [ObservableProperty]
    private string _progressLabel = string.Empty;

    [ObservableProperty]
    private string _pickerQuery = string.Empty;

    public bool HasSteps => Steps.Count > 0;
    public bool HasVariables => Variables.Count > 0;
    public string StepCountLabel => Format.Count(Steps.Count, "step");
    public string VariableCountLabel => Variables.Count == 0 ? "no variables" : $"{Format.Count(Variables.Count, "variable")}, each filled once";
    public bool CanGoBack => CurrentStep is { Number: > 1 };
    public bool CanGoForward => CurrentStep is { } step && step.Number < Steps.Count;
    public string CopyLabel => CurrentStep is { } step ? $"Copy step {step.Number}" : "Copy";

    public bool IsStepsTab
    {
        get => Tab == WorkflowTab.Steps;
        set
        {
            if (value)
            {
                Tab = WorkflowTab.Steps;
            }
        }
    }

    public bool IsRunTab
    {
        get => Tab == WorkflowTab.Run;
        set
        {
            if (value)
            {
                Tab = WorkflowTab.Run;
            }
        }
    }

    public async Task LoadAsync(Guid id)
    {
        var workflow = await _workflows.GetAsync(id) ?? throw new LibraryException("That workflow no longer exists.");
        Id = id;
        _values = await _workflows.LoadValuesAsync(id);
        _library = await _search.SearchAsync(new PromptQuery { Sort = PromptSort.Title });
        _loading = true;
        Name = workflow.Name;
        Description = workflow.Description ?? string.Empty;
        _loading = false;
        await ApplyStepsAsync(workflow);
        RefreshPicker();
    }

    /// <summary>Saves a pending name, description or note change now, as when leaving the page.</summary>
    public async Task<bool> FlushAsync()
    {
        _saveDelay?.Cancel();
        if (!_dirty)
        {
            return true;
        }

        try
        {
            _dirty = false;
            await _workflows.RenameAsync(Id, Name, Description);
            foreach (var step in Steps)
            {
                await _workflows.SetStepNoteAsync(Id, step.Id, step.Note);
            }

            _notifier.Notify();
            return true;
        }
        catch (Exception exception)
        {
            _dirty = true;
            await ReportAsync("Couldn't save this workflow.", exception);
            return false;
        }
    }

    /// <summary>Name, description and notes save a moment after typing stops.</summary>
    public async void ScheduleSave()
    {
        if (_loading)
        {
            return;
        }

        _dirty = true;
        _saveDelay?.Cancel();
        var delay = _saveDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(600, delay.Token);
            await FlushAsync();
        }
        catch (TaskCanceledException)
        {
            // A newer keystroke or leaving the page took over.
        }
    }

    public async Task MoveStepAsync(WorkflowStepViewModel step, int offset)
    {
        // Steps reload from the library after a change, so notes still being typed are saved first.
        if (await FlushAsync())
        {
            await RunAsync("Couldn't move that step.", async () => await ApplyStepsAsync(await _workflows.MoveStepAsync(Id, step.Id, offset)));
        }
    }

    public async Task RemoveStepAsync(WorkflowStepViewModel step)
    {
        if (!await FlushAsync())
        {
            return;
        }

        await RunAsync("Couldn't remove that step.", async () =>
        {
            await ApplyStepsAsync(await _workflows.RemoveStepAsync(Id, step.Id));
            _toasts.Show("Step removed.", $"{step.Title} is still in your library.", isHappy: false);
        });
    }

    public async Task OpenPromptAsync(WorkflowStepViewModel step)
    {
        if (step.IsPromptDeleted)
        {
            _toasts.Show("It's in Recently deleted.", "Restore it first to open it.", isHappy: false);
            return;
        }

        await _navigator.OpenPromptAsync(step.PromptId);
    }

    public void SelectStep(WorkflowStepViewModel step)
    {
        foreach (var item in Steps)
        {
            item.IsCurrent = item == step;
        }

        CurrentStep = step;
    }

    /// <summary>Ctrl+Enter: from Steps, open Run; on Run, copy the current step.</summary>
    [RelayCommand]
    public async Task RunOrCopy()
    {
        if (Tab == WorkflowTab.Run)
        {
            await CopyStep();
            return;
        }

        Tab = WorkflowTab.Run;
    }

    /// <summary>Copies the current step with the shared values filled in, marks it copied and moves on.</summary>
    [RelayCommand]
    private async Task CopyStep()
    {
        if (CurrentStep is not { } step)
        {
            return;
        }

        await _clipboard.SetTextAsync(_templates.Render(step.Body, _values));
        step.IsCopied = true;
        var next = Steps.FirstOrDefault(item => item.Number == step.Number + 1);
        if (next is not null)
        {
            SelectStep(next);
        }

        RefreshProgress();
        _toasts.Show(
            $"Copied step {step.Number} of {Steps.Count}.",
            next is null ? "That's the last one. Nice run." : step.HasNote ? "Hands off: " + step.Note : $"Paste it, then come back for step {next.Number}.");
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep is { } step && Steps.FirstOrDefault(item => item.Number == step.Number - 1) is { } previous)
        {
            SelectStep(previous);
        }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep is { } step && Steps.FirstOrDefault(item => item.Number == step.Number + 1) is { } next)
        {
            SelectStep(next);
        }
    }

    /// <summary>Clears the copied marks and goes back to step 1. The values stay.</summary>
    [RelayCommand]
    private void StartOver()
    {
        foreach (var step in Steps)
        {
            step.IsCopied = false;
        }

        if (Steps.FirstOrDefault() is { } first)
        {
            SelectStep(first);
        }

        RefreshProgress();
    }

    [RelayCommand]
    private async Task ClearValues()
    {
        _values.Clear();
        _loading = true;
        foreach (var variable in Variables)
        {
            variable.Value = string.Empty;
        }

        _loading = false;
        RefreshRender();
        await SaveValuesAsync();
    }

    [RelayCommand]
    private Task ShowRun()
    {
        Tab = WorkflowTab.Run;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task Back() => _navigator.ShowWorkflowsAsync();

    [RelayCommand]
    private async Task Export()
    {
        if (!await FlushAsync())
        {
            return;
        }

        var path = await _files.PickExportFileAsync(_transfer.SuggestFileName(Name), "Export workflow");
        if (path is null)
        {
            return;
        }

        try
        {
            await _transfer.ExportWorkflowAsync(Id, path);
            _toasts.Show("Exported.", $"{Path.GetFileName(path)}, with every step's prompt inside.");
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't export this workflow.", exception);
        }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (!await _dialogs.ConfirmAsync(
                $"Delete “{Name}”?",
                $"Its {StepCountLabel} go with it, but the prompts stay in your library.",
                "Delete workflow",
                isDanger: true))
        {
            return;
        }

        try
        {
            _saveDelay?.Cancel();
            _dirty = false;
            await _workflows.DeleteAsync(Id);
            _notifier.Notify();
            _toasts.Show("Workflow deleted.", "Its prompts are still in your library.", isHappy: false);
            await _navigator.ShowWorkflowsAsync();
        }
        catch (Exception exception)
        {
            await ReportAsync("Couldn't delete this workflow.", exception);
        }
    }

    partial void OnNameChanged(string value) => ScheduleSave();

    partial void OnDescriptionChanged(string value) => ScheduleSave();

    partial void OnPickerQueryChanged(string value) => RefreshPicker();

    partial void OnCurrentStepChanged(WorkflowStepViewModel? value)
    {
        RefreshRender();
        RefreshProgress();
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        OnPropertyChanged(nameof(CopyLabel));
    }

    private async Task AddStepAsync(PromptSummary prompt)
    {
        if (!await FlushAsync())
        {
            return;
        }

        await RunAsync("Couldn't add that step.", async () =>
        {
            await ApplyStepsAsync(await _workflows.AddStepAsync(Id, prompt.Id));
            PickerQuery = string.Empty;
            _toasts.Show($"Added as step {Steps.Count}.", prompt.Title);
        });
    }

    private async Task ApplyStepsAsync(Workflow workflow)
    {
        var copied = Steps.Where(step => step.IsCopied).Select(step => step.Id).ToHashSet();
        var current = CurrentStep?.Id;
        var bodies = new List<string>();
        var steps = new List<WorkflowStepViewModel>();
        _loading = true;
        for (var i = 0; i < workflow.Steps.Count; i++)
        {
            var step = workflow.Steps[i];
            var prompt = await _prompts.GetAsync(step.PromptId);
            var body = prompt?.Body ?? string.Empty;
            bodies.Add(body);
            steps.Add(new WorkflowStepViewModel(this, step, prompt, i + 1, _templates.ExtractVariables(body)) { IsCopied = copied.Contains(step.Id) });
        }

        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        Variables.Clear();
        foreach (var variable in _workflows.CollectVariables(bodies))
        {
            Variables.Add(new WorkflowVariableViewModel(variable.Name, variable.Steps, _values.GetValueOrDefault(variable.Name, string.Empty), VariableChanged));
        }

        _loading = false;
        if ((Steps.FirstOrDefault(step => step.Id == current) ?? Steps.FirstOrDefault()) is { } selected)
        {
            SelectStep(selected);
        }
        else
        {
            CurrentStep = null;
        }

        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(HasVariables));
        OnPropertyChanged(nameof(StepCountLabel));
        OnPropertyChanged(nameof(VariableCountLabel));
        RefreshRender();
        RefreshProgress();
    }

    private void VariableChanged()
    {
        if (_loading)
        {
            return;
        }

        RefreshRender();
        _ = SaveValuesAsync();
    }

    private async Task SaveValuesAsync()
    {
        var snapshot = new Dictionary<string, string>(_values, StringComparer.Ordinal);
        await _valueWrites.WaitAsync();
        try
        {
            await _workflows.SaveValuesAsync(Id, snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Couldn't remember values for workflow {WorkflowId}", Id);
        }
        finally
        {
            _valueWrites.Release();
        }
    }

    private void RefreshRender()
    {
        foreach (var variable in Variables)
        {
            _values[variable.Name] = variable.Value;
        }

        if (CurrentStep is not { } step)
        {
            Segments = [];
            FillLabel = string.Empty;
            IsFullyFilled = true;
            return;
        }

        Segments = _templates.RenderSegments(step.Body, _values);
        var names = _templates.ExtractVariables(step.Body);
        var filled = names.Count(name => !string.IsNullOrEmpty(_values.GetValueOrDefault(name)));
        IsFullyFilled = filled == names.Count;
        FillLabel = names.Count == 0 ? "no variables" : $"{filled} of {names.Count} filled";
    }

    private void RefreshProgress()
    {
        var copied = Steps.Count(step => step.IsCopied);
        ProgressLabel = CurrentStep is { } step
            ? $"Step {step.Number} of {Steps.Count} · {copied} copied"
            : "Add a step to run this workflow";
        foreach (var item in Steps)
        {
            item.RefreshPosition();
        }
    }

    private void RefreshPicker()
    {
        var query = PickerQuery.Trim();
        PickerResults.Clear();
        foreach (var prompt in _library.Where(prompt => query.Length == 0 || prompt.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            PickerResults.Add(new PromptPickViewModel(prompt, AddStepAsync));
        }
    }

    private async Task RunAsync(string failureTitle, Func<Task> action)
    {
        try
        {
            await action();
            _notifier.Notify();
        }
        catch (Exception exception)
        {
            await ReportAsync(failureTitle, exception);
        }
    }

    private async Task ReportAsync(string title, Exception exception)
    {
        if (exception is LibraryException library)
        {
            await _dialogs.ShowErrorAsync(title, library.Message, library.InnerException?.ToString());
            return;
        }

        _logger.LogError(exception, "Workflow action failed for workflow {WorkflowId}", Id);
        await _dialogs.ShowErrorAsync(title, "Something went wrong. Your library hasn't been changed.", exception.ToString());
    }
}
