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
            if (e.PropertyName == nameof(DialogService.Current) && viewModel.Dialogs.Current is { HasInput: true })
            {
                FocusLater(DialogInput, selectAll: true);
            }
        };
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        SavePlacement();
        if (_closeConfirmed || ViewModel is not { } viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (await viewModel.PrepareToCloseAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // Runs before text boxes see the key, so shortcuts work while typing.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is { } viewModel && Shortcuts.Match(e) is { } action && viewModel.TryHandleShortcut(action))
        {
            e.Handled = true;
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
