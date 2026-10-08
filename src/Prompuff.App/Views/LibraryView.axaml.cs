using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;

namespace Prompuff.App.Views;

/// <summary>
/// Keyboard navigation for the library: arrow keys, Home and End move between cards, Enter opens, Delete deletes after
/// asking, and typing jumps to a title. Down or Enter in the search box moves on to the results.
/// </summary>
public partial class LibraryView : UserControl
{
    private LibraryViewModel? _subscribed;
    private TopLevel? _topLevel;

    public LibraryView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnCardPointerPressed, RoutingStrategies.Tunnel);
    }

    private LibraryViewModel? ViewModel => DataContext as LibraryViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusCurrentRequested -= OnFocusCurrentRequested;
            _subscribed.ScrollToTopRequested -= OnScrollToTopRequested;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusCurrentRequested += OnFocusCurrentRequested;
            _subscribed.ScrollToTopRequested += OnScrollToTopRequested;
        }
    }

    private void OnScrollToTopRequested(object? sender, EventArgs e)
    {
        CardsScroller.ScrollToHome();
        RowsScroller.ScrollToHome();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(TextInputEvent, OnWindowTextInput, RoutingStrategies.Tunnel);

        // Coming back from a prompt, focus lands on its card so the keyboard can carry on from there.
        Dispatcher.UIThread.Post(() =>
        {
            if (_topLevel?.FocusManager?.GetFocusedElement() is null && ViewModel?.CurrentItem is not null)
            {
                FocusCurrent(MainWindow.UsingKeyboard ? NavigationMethod.Directional : NavigationMethod.Unspecified);
            }
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
        _topLevel?.RemoveHandler(TextInputEvent, OnWindowTextInput);
        _topLevel = null;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || ViewModel is not { } library || !CanUseKeys())
        {
            return;
        }

        var modifiers = e.KeyModifiers & ~KeyModifiers.Shift;
        if (IsSearchBox(e.Source))
        {
            if (modifiers == KeyModifiers.None && e.Key == Key.Down)
            {
                library.FocusCards();
                e.Handled = true;
            }
            else if (modifiers == KeyModifiers.None && e.Key == Key.Enter)
            {
                _ = library.OpenTopResultAsync();
                e.Handled = true;
            }

            return;
        }

        if (!IsLibraryFocus(e.Source))
        {
            return;
        }

        var columns = library.IsCards ? CardsList.ColumnCount : 1;
        var command = Platform.Shortcuts.CommandModifier;
        var extend = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.Down when modifiers == KeyModifiers.None:
                library.MoveCurrent(columns, extend);
                break;
            case Key.Up when modifiers == KeyModifiers.None:
                library.MoveCurrent(-columns, extend);
                break;
            case Key.Right when modifiers == KeyModifiers.None && library.IsCards:
                library.MoveCurrent(1, extend);
                break;
            case Key.Left when modifiers == KeyModifiers.None && library.IsCards:
                library.MoveCurrent(-1, extend);
                break;
            case Key.Home when modifiers == KeyModifiers.None:
                library.MoveCurrentToEnd(last: false, extend);
                break;
            case Key.End when modifiers == KeyModifiers.None:
                library.MoveCurrentToEnd(last: true, extend);
                break;
            case Key.A when modifiers == command:
                library.SelectAll();
                break;
            case Key.Escape when modifiers == KeyModifiers.None && library.IsSelecting:
                library.ClearSelection();
                break;
            case Key.Enter when modifiers == KeyModifiers.None && e.Source is not Button:
                // A focused card is a button and opens itself.
                _ = library.OpenCurrentAsync();
                break;
            case Key.Delete when modifiers == KeyModifiers.None:
            case Key.Back when modifiers == command:
                _ = library.DeleteCurrentAsync();
                break;
            case Key.Space when modifiers == KeyModifiers.None && library.IsTypingAhead:
                // Part of a title being typed, so it shouldn't press the focused card. The text arrives as TextInput.
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnWindowTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Handled || string.IsNullOrEmpty(e.Text) || ViewModel is not { } library || !CanUseKeys() || !IsLibraryFocus(e.Source))
        {
            return;
        }

        if (library.TypeAhead(e.Text) || library.IsTypingAhead)
        {
            e.Handled = true;
        }
    }

    private void OnFocusCurrentRequested(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => FocusCurrent(NavigationMethod.Directional), DispatcherPriority.Loaded);

    private void FocusCurrent(NavigationMethod method)
    {
        if (ViewModel is not { CurrentItem: { } current } library)
        {
            return;
        }

        // Only the cards near the screen exist, so the one with the cursor may need scrolling to first.
        Control? container = null;
        if (library.IsCards)
        {
            container = CardsList.BringCardIntoView(current);
        }
        else if (library.Items.IndexOf(current) is var index and >= 0)
        {
            RowsList.ScrollIntoView(index);
            container = RowsList.ContainerFromIndex(index);
        }

        if (container is null)
        {
            return;
        }

        var target = container as Button ?? container.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        if (target is not null)
        {
            target.Focus(method);
            target.BringIntoView();
        }
    }

    /// <summary>Keys belong to the library only while no dialog, palette or quick save is open.</summary>
    private bool CanUseKeys() =>
        _topLevel?.DataContext is MainWindowViewModel { Dialogs.Current: null, Palette.IsOpen: false, QuickSave.IsOpen: false };

    private bool IsLibraryFocus(object? source) => source switch
    {
        TopLevel => true,
        Visual visual when visual.FindAncestorOfType<TextBox>(includeSelf: true) is not null
                           || visual.FindAncestorOfType<ComboBox>(includeSelf: true) is not null => false,
        Visual visual => visual == this || this.IsVisualAncestorOf(visual),
        _ => false,
    };

    private static bool IsSearchBox(object? source) => source is TextBox { Name: "SearchBox" };

    // Ctrl or Cmd and click picks one card, Shift and click picks a range, without opening anything.
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var command = Platform.Shortcuts.CommandModifier;
        if (ViewModel is not { } library
            || (e.KeyModifiers & (command | KeyModifiers.Shift)) == KeyModifiers.None
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || CardAt(e.Source) is not { } card)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            library.SelectRange(card);
        }
        else
        {
            library.ToggleSelection(card);
        }

        e.Handled = true;
    }

    private static PromptCardViewModel? CardAt(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button { DataContext: PromptCardViewModel card } button && (button.Classes.Contains("card") || button.Classes.Contains("row")))
            {
                return card;
            }
        }

        return null;
    }

    private void OnCardGotFocus(object? sender, FocusChangedEventArgs e)
    {
        // Tab and clicks move the cursor too, so the arrow keys carry on from the focused card.
        if (sender is Control { DataContext: PromptCardViewModel card } && ViewModel is { } library && e.Source == sender)
        {
            library.SetCurrent(card);
        }
    }
}
