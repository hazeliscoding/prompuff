using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Prompuff.App.ViewModels;

namespace Prompuff.App.Views;

public partial class PromptEditorView : UserControl
{
    private PromptEditorViewModel? _subscribed;

    public PromptEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is not null)
        {
            _subscribed.FocusRequested -= OnFocusRequested;
        }

        _subscribed = DataContext as PromptEditorViewModel;
        if (_subscribed is not null)
        {
            _subscribed.FocusRequested += OnFocusRequested;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_subscribed is { IsNew: true })
        {
            OnFocusRequested(this, "title");
        }
    }

    private void OnFocusRequested(object? sender, string target) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (target == "variable")
            {
                VariableInputs.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
            }
            else
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
}
