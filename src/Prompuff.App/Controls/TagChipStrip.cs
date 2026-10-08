using Avalonia;
using Avalonia.Controls;

namespace Prompuff.App.Controls;

/// <summary>
/// Read-only tag chips in a row that wraps, or in one line with <see cref="Wrap"/> off. Library cards are reused while
/// a large library scrolls, and the chips they already have take the next prompt's tags, which costs a fraction of
/// building new ones.
/// </summary>
public sealed class TagChipStrip : Panel
{
    public static readonly StyledProperty<IReadOnlyList<string>?> NamesProperty =
        AvaloniaProperty.Register<TagChipStrip, IReadOnlyList<string>?>(nameof(Names));

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<TagChipStrip, double>(nameof(Spacing), 4);

    public static readonly StyledProperty<bool> WrapProperty =
        AvaloniaProperty.Register<TagChipStrip, bool>(nameof(Wrap), true);

    static TagChipStrip()
    {
        AffectsMeasure<TagChipStrip>(SpacingProperty, WrapProperty);
    }

    public IReadOnlyList<string>? Names
    {
        get => GetValue(NamesProperty);
        set => SetValue(NamesProperty, value);
    }

    /// <summary>The gap between chips, and between lines when they wrap.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public bool Wrap
    {
        get => GetValue(WrapProperty);
        set => SetValue(WrapProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NamesProperty)
        {
            ShowNames(Names ?? []);
        }
    }

    private void ShowNames(IReadOnlyList<string> names)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (i < Children.Count)
            {
                var chip = (TagChip)Children[i];
                chip.Text = names[i];
                chip.Tone = Tones.For(names[i]);
                chip.IsVisible = true;
            }
            else
            {
                Children.Add(new TagChip { Text = names[i], Tone = Tones.For(names[i]) });
            }
        }

        for (var i = names.Count; i < Children.Count; i++)
        {
            Children[i].IsVisible = false;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var lineWidth = Wrap ? availableSize.Width : double.PositiveInfinity;
        double x = 0, y = 0, lineHeight = 0, width = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            child.Measure(new Size(lineWidth, availableSize.Height));
            var size = child.DesiredSize;
            if (x > 0 && x + Spacing + size.Width > lineWidth)
            {
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
            }

            x += (x > 0 ? Spacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            width = Math.Max(width, x);
        }

        return new Size(width, y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var lineWidth = Wrap ? finalSize.Width : double.PositiveInfinity;
        double x = 0, y = 0, lineHeight = 0;
        var line = new List<Control>();
        foreach (var child in Children)
        {
            if (!child.IsVisible)
            {
                continue;
            }

            var size = child.DesiredSize;
            if (x > 0 && x + Spacing + size.Width > lineWidth)
            {
                ArrangeLine(line, y, lineHeight);
                y += lineHeight + Spacing;
                x = 0;
                lineHeight = 0;
                line.Clear();
            }

            x += (x > 0 ? Spacing : 0) + size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
            line.Add(child);
        }

        ArrangeLine(line, y, lineHeight);
        return finalSize;
    }

    private void ArrangeLine(List<Control> line, double y, double height)
    {
        var x = 0d;
        foreach (var child in line)
        {
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, height));
            x += child.DesiredSize.Width + Spacing;
        }
    }
}
