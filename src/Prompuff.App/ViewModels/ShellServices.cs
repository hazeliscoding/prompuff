using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.Application.DTOs;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Repositories;

namespace Prompuff.App.ViewModels;

/// <summary>Which slice of the library is showing.</summary>
public sealed record LibraryFilter(PromptFilterKind Kind, Guid? CollectionId = null, string? Tag = null)
{
    public static LibraryFilter All { get; } = new(PromptFilterKind.All);
}

/// <summary>Lets view models ask the shell to navigate without holding a reference to it.</summary>
public sealed class Navigator
{
    public event Func<Guid, Task>? OpenPromptRequested;
    public event Func<Guid?, Task>? NewPromptRequested;
    public event Func<LibraryFilter?, Task>? LibraryRequested;
    public event Func<Task>? SettingsRequested;

    public Task OpenPromptAsync(Guid id) => OpenPromptRequested?.Invoke(id) ?? Task.CompletedTask;

    public Task NewPromptAsync(Guid? collectionId = null) => NewPromptRequested?.Invoke(collectionId) ?? Task.CompletedTask;

    /// <param name="filter">Null keeps the current filter.</param>
    public Task ShowLibraryAsync(LibraryFilter? filter = null) => LibraryRequested?.Invoke(filter) ?? Task.CompletedTask;

    public Task ShowSettingsAsync() => SettingsRequested?.Invoke() ?? Task.CompletedTask;
}

/// <summary>Raised whenever prompts, collections or tags change, so lists and counts can refresh.</summary>
public sealed class LibraryNotifier
{
    public event EventHandler? Changed;

    public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Appearance settings that many views read.</summary>
public sealed partial class AppearanceState : ObservableObject
{
    [ObservableProperty]
    private bool _showMascot = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompact), nameof(IsDense))]
    private Density _density = Density.Cozy;

    public bool IsCompact => Density == Density.Compact;
    public bool IsDense => Density == Density.Dense;
}

/// <summary>Variable values typed in the Render tab, remembered per prompt in the local library. Never exported or logged.</summary>
public sealed class RenderValuesCache(SqliteRenderValues store, ILogger<RenderValuesCache> logger)
{
    private readonly Dictionary<Guid, Dictionary<string, string>> _values = [];
    private readonly SemaphoreSlim _writes = new(1, 1);

    /// <summary>The prompt's values, read from the library the first time and shared after that.</summary>
    public async Task<Dictionary<string, string>> GetAsync(Guid promptId)
    {
        if (_values.TryGetValue(promptId, out var values))
        {
            return values;
        }

        try
        {
            values = await store.LoadAsync(promptId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't read remembered values for prompt {PromptId}", promptId);
            values = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _values.TryAdd(promptId, values) ? values : _values[promptId];
    }

    /// <summary>Keeps values typed into a new prompt once it's saved for the first time.</summary>
    public void Adopt(Guid promptId, Dictionary<string, string> values) => _values[promptId] = values;

    /// <summary>Writes the prompt's current values. Writes run one at a time, so the last one wins.</summary>
    public async Task SaveAsync(Guid promptId)
    {
        if (!_values.TryGetValue(promptId, out var values))
        {
            return;
        }

        var snapshot = new Dictionary<string, string>(values, StringComparer.Ordinal);
        await _writes.WaitAsync();
        try
        {
            await store.SaveAsync(promptId, snapshot);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Couldn't remember values for prompt {PromptId}", promptId);
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task ClearAsync(Guid promptId)
    {
        if (_values.TryGetValue(promptId, out var values))
        {
            values.Clear();
        }

        await SaveAsync(promptId);
    }

    /// <summary>Waits for writes in progress, so closing the window doesn't cut one short.</summary>
    public async Task FlushAsync()
    {
        await _writes.WaitAsync();
        _writes.Release();
    }
}

public sealed partial class ToastViewModel(string title, string? subtitle, bool isHappy) : ObservableObject
{
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);
    public bool IsHappy { get; } = isHappy;
}

public sealed partial class ToastService(AppearanceState appearance) : ObservableObject
{
    [ObservableProperty]
    private ToastViewModel? _current;

    public AppearanceState Appearance { get; } = appearance;

    public async void Show(string title, string? subtitle = null, bool isHappy = true)
    {
        var toast = new ToastViewModel(title, subtitle, isHappy);
        Current = toast;
        await Task.Delay(TimeSpan.FromSeconds(2.8));
        if (Current == toast)
        {
            Current = null;
        }
    }
}

public enum DialogKind
{
    Confirm,
    Input,
    Error,
}

public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<(bool Confirmed, string? Input)> _completion = new();

    public DialogViewModel(DialogKind kind, string title, string message, string confirmText, string? cancelText)
    {
        Kind = kind;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
    }

    public DialogKind Kind { get; }
    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string? CancelText { get; }
    public bool HasCancel => CancelText is not null;
    public bool IsDanger { get; init; }
    public bool IsError => Kind == DialogKind.Error;
    public bool HasInput => Kind == DialogKind.Input;
    public string? InputPlaceholder { get; init; }
    public string? Details { get; init; }
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _showDetails;

    public Task<(bool Confirmed, string? Input)> Result => _completion.Task;

    [RelayCommand]
    private void Confirm()
    {
        if (HasInput && string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

        _completion.TrySetResult((true, InputText));
    }

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult((false, null));

    [RelayCommand]
    private void ToggleDetails() => ShowDetails = !ShowDetails;
}

public sealed partial class DialogService : ObservableObject
{
    [ObservableProperty]
    private DialogViewModel? _current;

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDanger = false)
    {
        var dialog = new DialogViewModel(DialogKind.Confirm, title, message, confirmText, "Cancel") { IsDanger = isDanger };
        return (await ShowAsync(dialog)).Confirmed;
    }

    public async Task<string?> AskTextAsync(string title, string message, string confirmText, string? initial = null, string? placeholder = null)
    {
        var dialog = new DialogViewModel(DialogKind.Input, title, message, confirmText, "Cancel")
        {
            InputPlaceholder = placeholder,
            InputText = initial ?? string.Empty,
        };
        var (confirmed, input) = await ShowAsync(dialog);
        return confirmed ? input?.Trim() : null;
    }

    public Task ShowErrorAsync(string title, string message, string? details = null) =>
        ShowAsync(new DialogViewModel(DialogKind.Error, title, message, "OK", null) { Details = details });

    /// <summary>Closes the open dialog as if Cancel was pressed. Returns false when none is open.</summary>
    public bool CancelCurrent()
    {
        if (Current is null)
        {
            return false;
        }

        Current.CancelCommand.Execute(null);
        return true;
    }

    private async Task<(bool Confirmed, string? Input)> ShowAsync(DialogViewModel dialog)
    {
        Current = dialog;
        try
        {
            return await dialog.Result;
        }
        finally
        {
            if (Current == dialog)
            {
                Current = null;
            }
        }
    }
}

internal static class Format
{
    private static readonly string[] RatingLabels = ["Unrated", "Meh", "Okay", "Solid", "Great", "Cooked"];

    public static string Rating(int? rating) => RatingLabels[Math.Clamp(rating ?? 0, 0, 5)];

    public static string Count(int count, string singular, string? plural = null) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>A file name for a zip of prompts, such as "prompuff-design-2026-10-07.zip".</summary>
    public static string ArchiveName(IPromptTransferService transfer, string label, DateTimeOffset now) =>
        $"prompuff-{Path.GetFileNameWithoutExtension(transfer.SuggestFileName(label))}-{now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip";

    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var elapsed = now - when;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes}m ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours}h ago";
        }

        if (elapsed < TimeSpan.FromDays(2))
        {
            return "yesterday";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays}d ago";
        }

        if (elapsed < TimeSpan.FromDays(35))
        {
            return $"{(int)(elapsed.TotalDays / 7)}w ago";
        }

        var local = when.ToLocalTime();
        return local.Year == now.ToLocalTime().Year
            ? local.ToString("MMM d", CultureInfo.InvariantCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    public static string Date(DateTimeOffset when) => when.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string Timestamp(DateTimeOffset when) => when.ToLocalTime().ToString("MMM d, yyyy  HH:mm", CultureInfo.InvariantCulture);

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes / (1024d * 1024d):0.#} MB",
    };
}
