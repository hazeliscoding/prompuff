using Avalonia;
using Avalonia.Controls;

namespace Prompuff.App.Controls;

/// <summary>
/// Lays children out in as many equal columns as fit at <see cref="MinItemWidth"/>, like CSS
/// <c>repeat(auto-fill, minmax(272px, 1fr))</c>. Each row is as tall as its tallest child.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly StyledProperty<double> MinItemWidthProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(MinItemWidth), 272);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<AdaptiveGrid, double>(nameof(Spacing), 12);

    static AdaptiveGrid()
    {
        AffectsMeasure<AdaptiveGrid>(MinItemWidthProperty, SpacingProperty);
    }

    public double MinItemWidth
    {
        get => GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : MinItemWidth * 3 + Spacing * 2;
        var (columns, itemWidth) = Columns(width);
        var height = 0d;
        var visible = Children.Where(child => child.IsVisible).ToList();
        for (var start = 0; start < visible.Count; start += columns)
        {
            var rowHeight = 0d;
            foreach (var child in visible.Skip(start).Take(columns))
            {
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            height += rowHeight + (start > 0 ? Spacing : 0);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (columns, itemWidth) = Columns(finalSize.Width);
        var visible = Children.Where(child => child.IsVisible).ToList();
        var y = 0d;
        for (var start = 0; start < visible.Count; start += columns)
        {
            var row = visible.Skip(start).Take(columns).ToList();
            var rowHeight = row.Max(child => child.DesiredSize.Height);
            for (var i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(i * (itemWidth + Spacing), y, itemWidth, rowHeight));
            }

            y += rowHeight + Spacing;
        }

        return finalSize;
    }

    private (int Columns, double ItemWidth) Columns(double width)
    {
        var columns = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        return (columns, Math.Max(0, (width - Spacing * (columns - 1)) / columns));
    }
}
