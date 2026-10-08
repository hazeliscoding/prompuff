using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Prompuff.App.Views;

public partial class WorkflowView : UserControl
{
    public WorkflowView()
    {
        InitializeComponent();
        if (AddStepButton.Flyout is { } flyout)
        {
            // The search box is ready to type in as soon as the picker opens.
            flyout.Opened += (_, _) => Dispatcher.UIThread.Post(() => PickerBox.Focus(), DispatcherPriority.Input);
        }
    }

    // Picking a prompt adds it as a step; the picker closes so the new step is in view.
    private void OnPickerItemClick(object? sender, RoutedEventArgs e) => AddStepButton.Flyout?.Hide();
}
