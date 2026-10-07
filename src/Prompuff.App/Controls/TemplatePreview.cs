using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Prompuff.Application.DTOs;

namespace Prompuff.App.Controls;

/// <summary>Selectable rendered prompt text with filled values and unresolved tokens highlighted.</summary>
public sealed class TemplatePreview : SelectableTextBlock
{
    public static readonly StyledProperty<IReadOnlyList<TemplateSegment>?> SegmentsProperty =
        AvaloniaProperty.Register<TemplatePreview, IReadOnlyList<TemplateSegment>?>(nameof(Segments));

    public static readonly StyledProperty<IBrush?> FilledForegroundProperty =
        AvaloniaProperty.Register<TemplatePreview, IBrush?>(nameof(FilledForeground));

    public static readonly StyledProperty<IBrush?> FilledBackgroundProperty =
        AvaloniaProperty.Register<TemplatePreview, IBrush?>(nameof(FilledBackground));

    public static readonly StyledProperty<IBrush?> MissingForegroundProperty =
        AvaloniaProperty.Register<TemplatePreview, IBrush?>(nameof(MissingForeground));

    public static readonly StyledProperty<IBrush?> MissingBackgroundProperty =
        AvaloniaProperty.Register<TemplatePreview, IBrush?>(nameof(MissingBackground));

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public IReadOnlyList<TemplateSegment>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public IBrush? FilledForeground
    {
        get => GetValue(FilledForegroundProperty);
        set => SetValue(FilledForegroundProperty, value);
    }

    public IBrush? FilledBackground
    {
        get => GetValue(FilledBackgroundProperty);
        set => SetValue(FilledBackgroundProperty, value);
    }

    public IBrush? MissingForeground
    {
        get => GetValue(MissingForegroundProperty);
        set => SetValue(MissingForegroundProperty, value);
    }

    public IBrush? MissingBackground
    {
        get => GetValue(MissingBackgroundProperty);
        set => SetValue(MissingBackgroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SegmentsProperty
            || change.Property == FilledForegroundProperty || change.Property == FilledBackgroundProperty
            || change.Property == MissingForegroundProperty || change.Property == MissingBackgroundProperty)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        var inlines = new InlineCollection();
        foreach (var segment in Segments ?? [])
        {
            var run = new Run(segment.Text);
            var (foreground, background) = segment.Kind switch
            {
                TemplateSegmentKind.FilledVariable => (FilledForeground, FilledBackground),
                TemplateSegmentKind.MissingVariable => (MissingForeground, MissingBackground),
                _ => (null, null),
            };

            // Leave unset brushes alone so the run inherits the block's foreground.
            if (foreground is not null)
            {
                run.Foreground = foreground;
            }

            if (background is not null)
            {
                run.Background = background;
            }

            inlines.Add(run);
        }

        Inlines = inlines;
    }
}
