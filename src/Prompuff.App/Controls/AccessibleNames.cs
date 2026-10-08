using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;

namespace Prompuff.App.Controls;

/// <summary>
/// Gives screen readers a name for every button and text box. Avalonia names a button only when its content is a
/// string, so a button holding an icon and a label was read out as "Avalonia.Controls.StackPanel". This names it after
/// the text it shows, or its tooltip when it shows only an icon, and a text box after its placeholder. The name is set
/// at style priority, so a name given in XAML always wins, and it follows the text as bindings change it, so recycled
/// list items stay right.
/// </summary>
public static class AccessibleNames
{
    private static readonly AttachedProperty<Action?> Unfollow =
        AvaloniaProperty.RegisterAttached<Control, Action?>("AccessibleNameUnfollow", typeof(AccessibleNames));

    public static void Install()
    {
        Control.LoadedEvent.AddClassHandler<Button>((button, _) => Follow(button));
        Control.UnloadedEvent.AddClassHandler<Button>((button, _) => Stop(button));
        Control.LoadedEvent.AddClassHandler<TextBox>((box, _) => Follow(box));
        Control.UnloadedEvent.AddClassHandler<TextBox>((box, _) => Stop(box));
    }

    /// <summary>The name this control's own content gives it: its text, its tooltip, or its placeholder.</summary>
    public static string? Describe(Control control) => control switch
    {
        TextBox box => Clean(box.Watermark?.ToString()) ?? Clean(ToolTip.GetTip(box) as string),
        ContentControl { Content: string text } => Clean(text),
        ContentControl content => Clean(string.Join(" ", Texts(content))) ?? Clean(ToolTip.GetTip(content) as string),
        _ => Clean(ToolTip.GetTip(control) as string),
    };

    private static void Follow(Control control)
    {
        Stop(control);
        void Update() => control.SetValue(AutomationProperties.NameProperty, Describe(control), BindingPriority.Style);

        // Describe it again whenever its text changes, such as a card showing a different prompt.
        void OnOwnChange(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ContentControl.ContentProperty)
            {
                // New content brings new text blocks; follow those once they're laid out.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Follow(control), Avalonia.Threading.DispatcherPriority.Loaded);
            }
            else if (e.Property == ToolTip.TipProperty || e.Property == TextBox.WatermarkProperty)
            {
                Update();
            }
        }

        void OnTextChange(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBlock.TextProperty || e.Property == TagChip.TextProperty || e.Property == Visual.IsVisibleProperty)
            {
                Update();
            }
        }

        var blocks = control is TextBox ? [] : Blocks(control).ToList();
        control.PropertyChanged += OnOwnChange;
        blocks.ForEach(block => block.PropertyChanged += OnTextChange);
        control.SetValue(Unfollow, () =>
        {
            control.PropertyChanged -= OnOwnChange;
            blocks.ForEach(block => block.PropertyChanged -= OnTextChange);
        });
        Update();
    }

    private static void Stop(Control control)
    {
        control.GetValue(Unfollow)?.Invoke();
        control.ClearValue(Unfollow);
    }

    private static IEnumerable<string> Texts(ContentControl control) =>
        Blocks(control).Where(block => block.IsVisible)
            .Select(block => block switch
            {
                TextBlock text => text.Text,
                TagChip { Text: { Length: > 0 } tag } => "#" + tag,
                _ => null,
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim());

    /// <summary>
    /// The text blocks and tag chips in the content. Content written in XAML is read through the logical tree, which exists before the
    /// button is ever laid out, such as in a Settings section that isn't showing yet; templated content needs the visual tree.
    /// </summary>
    private static IEnumerable<Control> Blocks(Control control) =>
        (control is ContentControl { Content: Control content } ? content.GetSelfAndLogicalDescendants() : control.GetVisualDescendants())
        .OfType<Control>().Where(block => block is TextBlock or TagChip);

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
