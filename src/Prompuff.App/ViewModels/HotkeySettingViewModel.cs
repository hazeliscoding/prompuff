using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Prompuff.App.Platform;

namespace Prompuff.App.ViewModels;

/// <summary>
/// One system-wide hotkey in Settings › Quick save, with the recorder that changes it. Quick save and Quick insert
/// each have one. While either records, both let go of their keys, so pressing one records it instead of opening it.
/// </summary>
public sealed partial class HotkeySettingViewModel : ObservableObject
{
    private readonly IGlobalHotkeyService _service;
    private readonly Hotkey _default;
    private readonly Action<string> _save;
    private readonly Func<Hotkey, string> _readyStatus;
    private readonly string _offStatus;
    private readonly ILogger _logger;

    /// <param name="feature">"Quick save" or "Quick insert", for the row, its buttons and the log.</param>
    /// <param name="save">Stores the hotkey as settings text; empty means off.</param>
    /// <param name="readyStatus">What the row says once the hotkey is registered.</param>
    /// <param name="offStatus">What the row says while it's turned off.</param>
    public HotkeySettingViewModel(
        string feature,
        Hotkey defaultHotkey,
        IGlobalHotkeyService service,
        Action<string> save,
        Func<Hotkey, string> readyStatus,
        string offStatus,
        ILogger logger)
    {
        Feature = feature;
        _default = defaultHotkey;
        _service = service;
        _save = save;
        _readyStatus = readyStatus;
        _offStatus = offStatus;
        _logger = logger;
        service.Pressed += (_, _) => Dispatcher.UIThread.Post(() => Pressed?.Invoke(this, EventArgs.Empty));
    }

    public string Feature { get; }
    public string Title => Feature + " hotkey";

    // Settings shows two of these rows, so their buttons say which hotkey they change.
    public string ChangeName => $"Change the {Feature} hotkey";
    public string TurnOffName => $"Turn off the {Feature} hotkey";
    public string TurnOnName => $"Turn on the {Feature} hotkey";

    /// <summary>The other hotkey. It can't have the same combination, and it lets go of its keys while this one records.</summary>
    public HotkeySettingViewModel? Sibling { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(HasHotkey))]
    private Hotkey? _hotkey;

    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>True when the hotkey couldn't be registered, usually because another app has it.</summary>
    [ObservableProperty]
    private bool _problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label))]
    private bool _isRecording;

    /// <summary>Raised on the UI thread when the hotkey is pressed in any app.</summary>
    public event EventHandler? Pressed;

    public bool IsSupported => _service.IsSupported;
    public string? UnsupportedReason => _service.UnsupportedReason;
    public bool HasHotkey => Hotkey is not null;
    public string Label => IsRecording ? "Press keys…" : Hotkey?.Display ?? "Off";

    /// <summary>How it's doing, for the diagnostic report.</summary>
    public string Health => !IsSupported ? "unsupported in this session" : Problem ? "taken by another app" : "ready";

    /// <summary>Reads the saved hotkey; anything it can't use counts as off.</summary>
    public void Load(string text) => Hotkey = Platform.Hotkey.Parse(text);

    /// <summary>Registers the saved hotkey with the system. Runs at startup and after each change.</summary>
    public async Task ApplyAsync()
    {
        if (!_service.IsSupported)
        {
            Problem = false;
            Status = _service.UnsupportedReason ?? string.Empty;
            return;
        }

        var hotkey = Hotkey;
        bool registered;
        try
        {
            registered = await _service.RegisterAsync(hotkey);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Couldn't register the {Feature} hotkey", Feature);
            registered = false;
        }

        Problem = hotkey is not null && !registered;
        _logger.LogInformation("{Feature} hotkey {Hotkey} is {State}", Feature, hotkey?.ToString() ?? "off", hotkey is null ? "off" : registered ? "ready" : "taken");
        Status = hotkey switch
        {
            null => _offStatus,
            _ when registered => _readyStatus(hotkey),
            _ when hotkey == Sibling?.Hotkey => $"{Sibling.Feature} already uses {hotkey.Display}. Pick another combination.",
            _ => $"Another app already uses {hotkey.Display}. Pick another combination.",
        };
    }

    /// <summary>
    /// While recording, the window hands every key here. Returns true when it was used: Esc cancels, a valid
    /// combination is saved, and anything else explains what's missing.
    /// </summary>
    public bool Record(Key key, KeyModifiers modifiers)
    {
        if (!IsRecording)
        {
            return false;
        }

        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            CancelRecordingCommand.Execute(null);
            return true;
        }

        if (Platform.Hotkey.IsModifierKey(key))
        {
            return true;
        }

        var candidate = new Hotkey(modifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift | KeyModifiers.Meta), key);
        if (!candidate.IsValid)
        {
            Problem = true;
            Status = $"Hold Ctrl, Alt or {MetaName}, then press a letter, a digit, F1–F12 or Space. Esc cancels.";
            return true;
        }

        if (candidate == Sibling?.Hotkey)
        {
            Problem = true;
            Status = $"{Sibling.Feature} already uses {candidate.Display}. Press another combination, or Esc to cancel.";
            return true;
        }

        IsRecording = false;
        Hotkey = candidate;
        _save(candidate.ToString());
        _ = FinishRecordingAsync();
        return true;
    }

    [RelayCommand]
    private async Task StartRecording()
    {
        if (Sibling is { IsRecording: true } recording)
        {
            recording.IsRecording = false;
            recording.Problem = false;
            recording.Status = string.Empty;
        }

        // Let go of both hotkeys, so pressing either records it instead of opening Quick save or Quick insert.
        await _service.RegisterAsync(null);
        if (Sibling is { IsSupported: true } sibling)
        {
            await sibling._service.RegisterAsync(null);
        }

        IsRecording = true;
        Problem = false;
        Status = $"Press the new combination: Ctrl, Alt or {MetaName}, then a letter, a digit, F1–F12 or Space. Esc cancels.";
    }

    [RelayCommand]
    private Task CancelRecording()
    {
        IsRecording = false;
        return FinishRecordingAsync();
    }

    [RelayCommand]
    private Task TurnOff()
    {
        var wasRecording = IsRecording;
        IsRecording = false;
        Hotkey = null;
        _save(string.Empty);
        return wasRecording ? FinishRecordingAsync() : ApplyAsync();
    }

    [RelayCommand]
    private Task TurnOn()
    {
        Hotkey = _default;
        _save(_default.ToString());
        return ApplyAsync();
    }

    private static string MetaName => OperatingSystem.IsMacOS() ? "⌘" : OperatingSystem.IsWindows() ? "Win" : "Super";

    // Recording let go of both hotkeys, so both are registered again.
    private async Task FinishRecordingAsync()
    {
        await ApplyAsync();
        if (Sibling is { } sibling)
        {
            await sibling.ApplyAsync();
        }
    }
}
