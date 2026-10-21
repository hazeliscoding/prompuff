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
        AddHandler(TextInputEvent, OnPreviewTextInput, RoutingStrategies.Tunnel);
        viewModel.Settings.QuickSaveHotkey.Pressed += (_, _) => _ = HandleLaunchAsync(LaunchRequest.QuickSave);
        viewModel.Settings.QuickInsertHotkey.Pressed += (_, _) => _ = HandleLaunchAsync(LaunchRequest.QuickInsert);
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
        viewModel.QuickInsert.PropertyChanged += (_, e) =>
        {
            // Not select all: a key typed before this runs is already in the box, and the next key would replace it.
            if (e.PropertyName == nameof(QuickInsertViewModel.IsOpen) && viewModel.QuickInsert.IsOpen)
            {
                FocusLater(QuickInsertInput, selectAll: false);
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
            if (_focusBeforeDialog is null)
            {
                _focusBeforeDialog = FocusManager?.GetFocusedElement() as InputElement;
                _focusBeforeDialogContext = _focusBeforeDialog?.DataContext;
            }
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
    private object? _focusBeforeDialogContext;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>True while the window is hidden in the tray rather than closed.</summary>
    public bool IsInTray { get; private set; }

    // While a hotkey's overlay is open: where the window goes back to when it closes, and how to stop waiting for that.
    private bool _returnToTray;
    private Action? _cancelReturn;

    /// <summary>
    /// A second launch, the tray or a hotkey: brings the window forward, and opens Quick save with the clipboard or
    /// Quick insert. If the window was in the tray or minimized, it goes back there once that closes, so the hotkey
    /// returns you to whatever you were doing.
    /// </summary>
    public async Task HandleLaunchAsync(LaunchRequest request)
    {
        var wasInTray = IsInTray;
        var wasMinimized = WindowState == WindowState.Minimized;

        // A launch while an earlier one's overlay is open takes over going back, so switching from Quick insert to
        // Quick save doesn't send the window away as Quick insert closes.
        if (_cancelReturn is { } cancel)
        {
            cancel();
            (wasInTray, wasMinimized) = (_returnToTray, !_returnToTray);
        }

        WindowActivation.BringForward(this);
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        INotifyPropertyChanged overlay;
        Func<bool> isOpen;
        switch (request)
        {
            case LaunchRequest.QuickSave:
                await viewModel.QuickSaveFromOutsideAsync();
                (overlay, isOpen) = (viewModel.QuickSave, () => viewModel.QuickSave.IsOpen);
                break;
            case LaunchRequest.QuickInsert:
                await viewModel.QuickInsertFromOutsideAsync();
                (overlay, isOpen) = (viewModel.QuickInsert, () => viewModel.QuickInsert.IsOpen);
                break;
            default:
                return;
        }

        if (!isOpen() || !(wasInTray || wasMinimized))
        {
            return;
        }

        // Quick save and Quick insert both call it IsOpen.
        void OnOverlayChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(QuickSaveViewModel.IsOpen) || isOpen())
            {
                return;
            }

            StopWaiting();
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

        void StopWaiting()
        {
            overlay.PropertyChanged -= OnOverlayChanged;
            _cancelReturn = null;
        }

        overlay.PropertyChanged += OnOverlayChanged;
        _returnToTray = wasInTray;
        _cancelReturn = StopWaiting;
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
        if (ViewModel?.Settings.RecordingHotkey is { } recording)
        {
            e.Handled = recording.Record(e.Key, e.KeyModifiers);
            return;
        }

        if (ViewModel is { } viewModel && Shortcuts.Match(e) is { } action && viewModel.TryHandleShortcut(action))
        {
            e.Handled = true;
        }
    }

    // Quick insert opens as the window comes forward, so a key typed straight after its hotkey can arrive before the
    // search box has focus. It goes into the search rather than wherever focus was.
    private void OnPreviewTextInput(object? sender, TextInputEventArgs e)
    {
        if (ViewModel is not { QuickInsert.IsOpen: true } || QuickInsertInput.IsFocused || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        QuickInsertInput.Focus();
        QuickInsertInput.Text += e.Text;
        QuickInsertInput.CaretIndex = QuickInsertInput.Text?.Length ?? 0;
        e.Handled = true;
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
        var context = _focusBeforeDialogContext;
        _focusBeforeDialog = null;
        _focusBeforeDialogContext = null;
        if (previous is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } && TopLevel.GetTopLevel(previous) == this)
        {
            // The library reuses card controls for other prompts, as it does after a delete, and focus mustn't follow a
            // card to a prompt it wasn't on. The library puts the cursor back itself.
            Dispatcher.UIThread.Post(() =>
            {
                if (previous.DataContext == context)
                {
                    previous.Focus(UsingKeyboard ? NavigationMethod.Directional : NavigationMethod.Unspecified);
                }
            }, DispatcherPriority.Input);
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
        else if (overlay == "quick-insert")
        {
            viewModel.QuickInsert.IsOpen = false;
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
