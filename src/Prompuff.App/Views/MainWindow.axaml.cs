using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;

namespace Prompuff.App.Views;

public partial class MainWindow : Window
{
    private readonly ISettingsStore? _settings;
    private bool _closeConfirmed;
    private bool _quitting;

    // For the XAML previewer.
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, ISettingsStore settings)
    {
        _settings = settings;
        DataContext = viewModel;
        InitializeComponent();

        if (viewModel.ExtendsIntoTitleBar)
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 40;

            // Windows gets Avalonia's drawn caption buttons, restyled; macOS keeps its native traffic lights.
            if (OperatingSystem.IsWindows() && this.TryFindResource("PrompuffWindowDecorations", out var theme) && theme is ControlTheme decorations)
            {
                WindowDecorationsTheme = decorations;
            }
        }

        RestorePlacement(settings.Load().Window);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        viewModel.Settings.HotkeyPressed += (_, _) => _ = HandleLaunchAsync(LaunchRequest.QuickSave);
        AddHandler(KeyDownEvent, OnUnhandledKeyDown, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, (_, _) => UsingKeyboard = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewModel.FocusSearchRequested += (_, _) => FocusLater(SearchBox, selectAll: true);
        viewModel.Palette.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && viewModel.Palette.IsOpen)
            {
                FocusLater(PaletteInput, selectAll: true);
            }
        };
        viewModel.QuickSave.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(QuickSaveViewModel.IsOpen) && viewModel.QuickSave.IsOpen)
            {
                FocusLater(QuickSaveTitle, selectAll: true);
            }
        };
        viewModel.Dialogs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DialogService.Current))
            {
                return;
            }

            if (viewModel.Dialogs.Current is not { } dialog)
            {
                RestoreFocusAfterDialog();
                return;
            }

            // Focus moves into the dialog, so Enter confirms it rather than pressing the button behind the scrim.
            _focusBeforeDialog ??= FocusManager?.GetFocusedElement() as InputElement;
            if (dialog.HasInput)
            {
                FocusLater(DialogInput, selectAll: true);
            }
            else
            {
                Dispatcher.UIThread.Post(() => DialogConfirm.Focus(UsingKeyboard ? NavigationMethod.Tab : NavigationMethod.Unspecified), DispatcherPriority.Input);
            }
        };
    }

    /// <summary>True when the last input was a key rather than the pointer, so focus moves can show where they landed.</summary>
    public static bool UsingKeyboard { get; private set; }

    private InputElement? _focusBeforeDialog;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>True while the window is hidden in the tray rather than closed.</summary>
    public bool IsInTray { get; private set; }

    /// <summary>
    /// A second launch, the tray or the hotkey: brings the window forward, and for Quick save opens it with the
    /// clipboard. If the window was in the tray or minimized, it goes back there once Quick save closes, so the hotkey
    /// returns you to whatever you were doing.
    /// </summary>
    public async Task HandleLaunchAsync(LaunchRequest request)
    {
        var wasInTray = IsInTray;
        var wasMinimized = WindowState == WindowState.Minimized;
        WindowActivation.BringForward(this);
        if (request != LaunchRequest.QuickSave || ViewModel is not { } viewModel)
        {
            return;
        }

        await viewModel.QuickSaveFromOutsideAsync();
        if (!viewModel.QuickSave.IsOpen || !(wasInTray || wasMinimized))
        {
            return;
        }

        void OnQuickSaveChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(QuickSaveViewModel.IsOpen) || viewModel.QuickSave.IsOpen)
            {
                return;
            }

            viewModel.QuickSave.PropertyChanged -= OnQuickSaveChanged;
            if (wasInTray)
            {
                IsInTray = true;
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        viewModel.QuickSave.PropertyChanged += OnQuickSaveChanged;
    }

    /// <summary>Closes the window for good, even when closing it would normally keep Prompuff in the tray.</summary>
    public void Quit()
    {
        _quitting = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SavePlacement();
        if (_closeConfirmed || ViewModel is not { } viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (!await viewModel.PrepareToCloseAsync())
        {
            return;
        }

        // Quitting from the tray, ⌘Q, or the system shutting down really closes; the close button only hides.
        var quitting = _quitting || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown;
        if (!quitting && viewModel.Settings.KeepRunningInTray)
        {
            IsInTray = true;
            Hide();
            return;
        }

        _closeConfirmed = true;
        Close();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            IsInTray = false;
        }
    }

    // Runs before text boxes see the key, so shortcuts work while typing.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        UsingKeyboard = true;

        // Settings › Quick save is recording a new hotkey, so every key goes there first.
        if (ViewModel is { Settings.IsRecordingHotkey: true } recording)
        {
            e.Handled = recording.Settings.RecordHotkey(e.Key, e.KeyModifiers);
            return;
        }

        if (ViewModel is { } viewModel && Shortcuts.Match(e) is { } action && viewModel.TryHandleShortcut(action))
        {
            e.Handled = true;
        }
    }

    // Esc that nothing else wanted, such as a dropdown closing itself, goes back from a prompt to the library.
    private void OnUnhandledKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && ViewModel is { } viewModel && viewModel.TryHandleShortcut(ShortcutAction.Back))
        {
            e.Handled = true;
        }
    }

    private void RestoreFocusAfterDialog()
    {
        var previous = _focusBeforeDialog;
        _focusBeforeDialog = null;
        if (previous is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && TopLevel.GetTopLevel(previous) == this)
        {
            Dispatcher.UIThread.Post(() => previous.Focus(UsingKeyboard ? NavigationMethod.Directional : NavigationMethod.Unspecified), DispatcherPriority.Input);
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel?.Palette is not { } palette)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                palette.MoveSelection(1);
                PaletteList.ScrollIntoView(palette.Selected!);
                e.Handled = true;
                break;
            case Key.Up:
                palette.MoveSelection(-1);
                PaletteList.ScrollIntoView(palette.Selected!);
                e.Handled = true;
                break;
            case Key.Enter:
                palette.RunCommand.Execute(palette.Selected);
                e.Handled = true;
                break;
        }
    }

    private void OnPaletteItemActivated(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.Palette is { } palette && e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>() is not null)
        {
            palette.RunCommand.Execute(palette.Selected);
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Control { Tag: string overlay })
        {
            return;
        }

        if (overlay == "palette")
        {
            viewModel.Palette.IsOpen = false;
        }
        else if (overlay == "quick-save")
        {
            viewModel.QuickSave.IsOpen = false;
        }
    }

    private void OnModalPressed(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private static void FocusLater(TextBox target, bool selectAll) =>
        Dispatcher.UIThread.Post(() =>
        {
            target.Focus();
            if (selectAll)
            {
                target.SelectAll();
            }
        }, DispatcherPriority.Input);

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null)
        {
            return;
        }

        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        if (_settings is null)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        var size = maximized ? new Size(Width, Height) : ClientSize;
        _settings.Save(_settings.Load() with { Window = new WindowPlacement(size.Width, size.Height, maximized) });
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestorOfType<T>(this Visual visual)
        where T : Visual
    {
        for (var current = visual; current is not null; current = Avalonia.VisualTree.VisualExtensions.GetVisualParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
