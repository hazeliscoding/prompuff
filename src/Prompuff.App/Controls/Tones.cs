using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
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
/// clicking the current rating clears it. When it's interactive it also takes the keyboard, and screen
/// readers read it as a slider whose value is the rating's word, such as "Solid".</summary>
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

    /// <summary>The ring drawn around the pips while they have keyboard focus; styles set it on :focus-visible.</summary>
    public static readonly StyledProperty<Avalonia.Media.IBrush?> FocusBrushProperty =
        AvaloniaProperty.Register<RatingPips, Avalonia.Media.IBrush?>(nameof(FocusBrush));

    private const double FocusInset = 3;

    private int? _hover;

    static RatingPips()
    {
        AffectsRender<RatingPips>(ValueProperty, FilledBrushProperty, EmptyBrushProperty, FocusBrushProperty);
        AffectsMeasure<RatingPips>(PipSizeProperty, SpacingProperty);
        IsInteractiveProperty.Changed.AddClassHandler<RatingPips>((pips, _) => pips.Focusable = pips.IsInteractive);
        ValueProperty.Changed.AddClassHandler<RatingPips>((pips, e) =>
            (ControlAutomationPeer.FromElement(pips) as RatingPipsAutomationPeer)?.RaiseValueChanged(e.OldValue as int?, e.NewValue as int?));
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

    public Avalonia.Media.IBrush? FocusBrush
    {
        get => GetValue(FocusBrushProperty);
        set => SetValue(FocusBrushProperty, value);
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

        if (FocusBrush is { } focus)
        {
            var ring = new Rect(Bounds.Size).Inflate(FocusInset);
            context.DrawRectangle(null, new Avalonia.Media.Pen(focus, 1.5), ring, radius + FocusInset, radius + FocusInset);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsInteractive || e.Handled || e.KeyModifiers != KeyModifiers.None || RatingForKey(e.Key, Value ?? 0) is not { } next)
        {
            return;
        }

        Value = next == 0 ? null : next;
        e.Handled = true;
    }

    /// <summary>The rating a key moves to, where 0 clears it: arrows step, Home and End jump to the ends, and 0 to 5
    /// pick one. Other keys return null.</summary>
    public static int? RatingForKey(Key key, int current) => key switch
    {
        Key.Right or Key.Up => Math.Min(current + 1, 5),
        Key.Left or Key.Down => Math.Max(current - 1, 0),
        Key.Home => 1,
        Key.End => 5,
        Key.Delete or Key.Back => 0,
        >= Key.D0 and <= Key.D5 => key - Key.D0,
        >= Key.NumPad0 and <= Key.NumPad5 => key - Key.NumPad0,
        _ => null,
    };

    protected override AutomationPeer OnCreateAutomationPeer() => new RatingPipsAutomationPeer(this);

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

/// <summary>Reads the rating as a slider whose value is its word ("Solid"), which screen readers say aloud, and
/// lets them set it by word or number.</summary>
public sealed class RatingPipsAutomationPeer(RatingPips owner) : ControlAutomationPeer(owner), IValueProvider
{
    private RatingPips Pips => (RatingPips)Owner;

    public bool IsReadOnly => !Pips.IsInteractive;

    public string? Value => ViewModels.Format.Rating(Pips.Value);

    public void SetValue(string? value)
    {
        var rating = int.TryParse(value, out var number)
            ? number
            : Enumerable.Range(0, 6).FirstOrDefault(i => string.Equals(ViewModels.Format.Rating(i), value?.Trim(), StringComparison.OrdinalIgnoreCase), -1);
        if (!IsReadOnly && rating is >= 0 and <= 5)
        {
            Pips.Value = rating == 0 ? null : rating;
        }
    }

    internal void RaiseValueChanged(int? oldValue, int? newValue) =>
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, ViewModels.Format.Rating(oldValue), ViewModels.Format.Rating(newValue));

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override bool IsContentElementCore() => Pips.IsInteractive;

    protected override bool IsControlElementCore() => Pips.IsInteractive;
}
