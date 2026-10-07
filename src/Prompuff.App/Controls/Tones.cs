using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.Windows.Input;

namespace Prompuff.App.Controls;

/// <summary>
/// Soft accent colors for collections and tags. A name always maps to the same tone, so a tag keeps
/// its color across sessions.
/// </summary>
public static class Tones
{
    public const string None = "none";

    private static readonly string[] Palette = ["sky", "pink", "peach", "lavender", "mint", "blue", "amber"];

    public static string For(string key)
    {
        // FNV-1a: string.GetHashCode is randomized per process, and colors must stay put.
        var hash = 2166136261u;
        foreach (var ch in key)
        {
            hash = (hash ^ ch) * 16777619u;
        }

        return Palette[hash % (uint)Palette.Length];
    }

    public static string For(Guid id) => For(id.ToString("N"));
}

/// <summary>A small rounded square in a tone color, used for collections.</summary>
public sealed class ToneDot : Border
{
    public static readonly StyledProperty<string> ToneProperty =
        AvaloniaProperty.Register<ToneDot, string>(nameof(Tone), Tones.None);

    public string Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
}

/// <summary>A tag label in a tone color, optionally with a remove button.</summary>
public sealed class TagChip : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<TagChip, string?>(nameof(Text));

    public static readonly StyledProperty<string> ToneProperty =
        AvaloniaProperty.Register<TagChip, string>(nameof(Tone), Tones.None);

    public static readonly StyledProperty<bool> IsRemovableProperty =
        AvaloniaProperty.Register<TagChip, bool>(nameof(IsRemovable));

    public static readonly StyledProperty<ICommand?> RemoveCommandProperty =
        AvaloniaProperty.Register<TagChip, ICommand?>(nameof(RemoveCommand));

    public static readonly StyledProperty<object?> RemoveCommandParameterProperty =
        AvaloniaProperty.Register<TagChip, object?>(nameof(RemoveCommandParameter));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public bool IsRemovable
    {
        get => GetValue(IsRemovableProperty);
        set => SetValue(IsRemovableProperty, value);
    }

    public ICommand? RemoveCommand
    {
        get => GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public object? RemoveCommandParameter
    {
        get => GetValue(RemoveCommandParameterProperty);
        set => SetValue(RemoveCommandParameterProperty, value);
    }
}

/// <summary>Five small squares showing usefulness from 1 to 5. Clicking a square sets the rating;
/// clicking the current rating clears it.</summary>
public sealed class RatingPips : Control
{
    public static readonly StyledProperty<int?> ValueProperty =
        AvaloniaProperty.Register<RatingPips, int?>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> PipSizeProperty =
        AvaloniaProperty.Register<RatingPips, double>(nameof(PipSize), 6);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<RatingPips, double>(nameof(Spacing), 3);

    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<RatingPips, bool>(nameof(IsInteractive));

    public static readonly StyledProperty<Avalonia.Media.IBrush?> FilledBrushProperty =
        AvaloniaProperty.Register<RatingPips, Avalonia.Media.IBrush?>(nameof(FilledBrush));

    public static readonly StyledProperty<Avalonia.Media.IBrush?> EmptyBrushProperty =
        AvaloniaProperty.Register<RatingPips, Avalonia.Media.IBrush?>(nameof(EmptyBrush));

    private int? _hover;

    static RatingPips()
    {
        AffectsRender<RatingPips>(ValueProperty, FilledBrushProperty, EmptyBrushProperty);
        AffectsMeasure<RatingPips>(PipSizeProperty, SpacingProperty);
    }

    public int? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double PipSize
    {
        get => GetValue(PipSizeProperty);
        set => SetValue(PipSizeProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public Avalonia.Media.IBrush? FilledBrush
    {
        get => GetValue(FilledBrushProperty);
        set => SetValue(FilledBrushProperty, value);
    }

    public Avalonia.Media.IBrush? EmptyBrush
    {
        get => GetValue(EmptyBrushProperty);
        set => SetValue(EmptyBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(PipSize * 5 + Spacing * 4, PipSize);

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        var shown = _hover ?? Value ?? 0;
        var radius = PipSize >= 10 ? 3 : 2;
        for (var i = 0; i < 5; i++)
        {
            var rect = new Rect(i * (PipSize + Spacing), (Bounds.Height - PipSize) / 2, PipSize, PipSize);
            context.DrawRectangle(i < shown ? FilledBrush : EmptyBrush, null, rect, radius, radius);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (IsInteractive)
        {
            _hover = IndexAt(e.GetPosition(this));
            Cursor = new Cursor(StandardCursorType.Hand);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractive || IndexAt(e.GetPosition(this)) is not { } index)
        {
            return;
        }

        Value = Value == index ? null : index;
        _hover = null;
        e.Handled = true;
    }

    private int? IndexAt(Point point)
    {
        var index = (int)(point.X / (PipSize + Spacing)) + 1;
        return index is >= 1 and <= 5 ? index : null;
    }
}
