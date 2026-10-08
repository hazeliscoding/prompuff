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

        // A new prompt starts at its title. An existing one opens with the cursor at the top of its body, so the
        // keyboard can carry on editing straight away.
        if (_subscribed is not null)
        {
            OnFocusRequested(this, _subscribed.IsNew ? "title" : "body");
        }
    }

    private void OnFocusRequested(object? sender, string target) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (target == "variable")
            {
                VariableInputs.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
            }
            else if (target == "body")
            {
                BodyBox.Focus();
                BodyBox.CaretIndex = 0;
            }
            else
            {
                TitleBox.Focus();
                TitleBox.SelectAll();
            }
        }, DispatcherPriority.Loaded);
}
