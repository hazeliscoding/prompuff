using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Prompuff.App.Controls;

public enum PuffMood
{
    Idle,
    Happy,
    Sleepy,
}

/// <summary>
/// Puff, the cloud mascot, drawn from the design's 22×18 geometry. Puff moves like a small companion: it can say
/// hello when it appears, blink now and then, squish when the pointer comes by, and hop for a confirmation. Each
/// reaction is short and ends at rest, and none of them plays while <see cref="IsStill"/> is set.
/// </summary>
public sealed class Puff : Control
{
    public static readonly StyledProperty<PuffMood> MoodProperty =
        AvaloniaProperty.Register<Puff, PuffMood>(nameof(Mood));

    public static readonly StyledProperty<IBrush?> BodyBrushProperty =
        AvaloniaProperty.Register<Puff, IBrush?>(nameof(BodyBrush));

    public static readonly StyledProperty<IBrush?> FaceBrushProperty =
        AvaloniaProperty.Register<Puff, IBrush?>(nameof(FaceBrush));

    public static readonly StyledProperty<IBrush?> OutlineBrushProperty =
        AvaloniaProperty.Register<Puff, IBrush?>(nameof(OutlineBrush));

    /// <summary>Squash and stretch about the middle of Puff's base: above zero is wider and shorter, below zero taller.</summary>
    public static readonly StyledProperty<double> SquashProperty =
        AvaloniaProperty.Register<Puff, double>(nameof(Squash));

    /// <summary>How far Puff is lifted, in the 22×18 design's units, so a hop scales with its size.</summary>
    public static readonly StyledProperty<double> LiftProperty =
        AvaloniaProperty.Register<Puff, double>(nameof(Lift));

    /// <summary>How open the round eyes are, from 0 shut to 1 open. Happy eyes are already closed arcs.</summary>
    public static readonly StyledProperty<double> EyesOpenProperty =
        AvaloniaProperty.Register<Puff, double>(nameof(EyesOpen), 1);

    /// <summary>Holds Puff still: no hello, blink, squish or hop, and any that's playing stops at rest.</summary>
    public static readonly StyledProperty<bool> IsStillProperty =
        AvaloniaProperty.Register<Puff, bool>(nameof(IsStill));

    /// <summary>Puff says hello with two gentle bobs each time this turns true, such as when an empty state appears.</summary>
    public static readonly StyledProperty<bool> GreetWhenProperty =
        AvaloniaProperty.Register<Puff, bool>(nameof(GreetWhen));

    /// <summary>Puff blinks every few seconds while it's on screen.</summary>
    public static readonly StyledProperty<bool> BlinksProperty =
        AvaloniaProperty.Register<Puff, bool>(nameof(Blinks));

    public static readonly StyledProperty<bool> SquishOnHoverProperty =
        AvaloniaProperty.Register<Puff, bool>(nameof(SquishOnHover));

    public static readonly StyledProperty<bool> CheerOnPressProperty =
        AvaloniaProperty.Register<Puff, bool>(nameof(CheerOnPress));

    /// <summary>Puff hops each time this changes to something new, such as the toast it sits in.</summary>
    public static readonly StyledProperty<object?> CheerForProperty =
        AvaloniaProperty.Register<Puff, object?>(nameof(CheerFor));

    private const double ViewWidth = 22;
    private const double ViewHeight = 18;

    // The middle of Puff's base, which squashes stay planted on.
    private const double BaseX = 11.34;
    private const double BaseY = 16.5;

    private static readonly Geometry Body = StreamGeometry.Parse("M6 16.5h10.5a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 6.6 7.2 4.7 4.7 0 0 0 6 16.5Z");
    private static readonly Geometry IdleMouth = StreamGeometry.Parse("M10.2 14.1q1.05.6 2.1 0");
    private static readonly Geometry HappyMouth = StreamGeometry.Parse("M10 13.9q1.25 1.3 2.5 0");
    private static readonly Geometry SleepyMouth = StreamGeometry.Parse("M10.2 14.2h2");
    private static readonly Geometry HappyEyes = StreamGeometry.Parse("M8.2 12.2q.8-.9 1.6 0M12.7 12.2q.8-.9 1.6 0");

    private static readonly Animation Hello = Keyframes(TimeSpan.FromSeconds(2.4),
        (0, LiftProperty, 0d), (0.25, LiftProperty, 1.8d), (0.5, LiftProperty, 0d), (0.75, LiftProperty, 1.8d), (1, LiftProperty, 0d));

    private static readonly Animation SquishMotion = Keyframes(TimeSpan.FromMilliseconds(650),
        (0, SquashProperty, 0d), (0.18, SquashProperty, 0.14), (0.42, SquashProperty, -0.08), (0.66, SquashProperty, 0.03),
        (0.84, SquashProperty, -0.01), (1, SquashProperty, 0d));

    private static readonly Animation CheerMotion = Keyframes(TimeSpan.FromMilliseconds(620),
        (0, LiftProperty, 0d), (0, SquashProperty, 0d),
        (0.3, LiftProperty, 2.2), (0.3, SquashProperty, -0.08),
        (0.58, LiftProperty, 0d), (0.58, SquashProperty, 0.12),
        (0.78, LiftProperty, 0.5), (0.78, SquashProperty, -0.03),
        (1, LiftProperty, 0d), (1, SquashProperty, 0d));

    private static readonly Animation BlinkMotion = Keyframes(TimeSpan.FromMilliseconds(180),
        (0, EyesOpenProperty, 1d), (0.45, EyesOpenProperty, 0.1), (1, EyesOpenProperty, 1d));

    private CancellationTokenSource? _bodyMotion;
    private CancellationTokenSource? _eyeMotion;
    private DispatcherTimer? _blinkTimer;

    static Puff()
    {
        AffectsRender<Puff>(MoodProperty, BodyBrushProperty, FaceBrushProperty, OutlineBrushProperty, SquashProperty, LiftProperty, EyesOpenProperty);
    }

    public PuffMood Mood
    {
        get => GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public IBrush? BodyBrush
    {
        get => GetValue(BodyBrushProperty);
        set => SetValue(BodyBrushProperty, value);
    }

    public IBrush? FaceBrush
    {
        get => GetValue(FaceBrushProperty);
        set => SetValue(FaceBrushProperty, value);
    }

    public IBrush? OutlineBrush
    {
        get => GetValue(OutlineBrushProperty);
        set => SetValue(OutlineBrushProperty, value);
    }

    public double Squash
    {
        get => GetValue(SquashProperty);
        set => SetValue(SquashProperty, value);
    }

    public double Lift
    {
        get => GetValue(LiftProperty);
        set => SetValue(LiftProperty, value);
    }

    public double EyesOpen
    {
        get => GetValue(EyesOpenProperty);
        set => SetValue(EyesOpenProperty, value);
    }

    public bool IsStill
    {
        get => GetValue(IsStillProperty);
        set => SetValue(IsStillProperty, value);
    }

    public bool GreetWhen
    {
        get => GetValue(GreetWhenProperty);
        set => SetValue(GreetWhenProperty, value);
    }

    public bool Blinks
    {
        get => GetValue(BlinksProperty);
        set => SetValue(BlinksProperty, value);
    }

    public bool SquishOnHover
    {
        get => GetValue(SquishOnHoverProperty);
        set => SetValue(SquishOnHoverProperty, value);
    }

    public bool CheerOnPress
    {
        get => GetValue(CheerOnPressProperty);
        set => SetValue(CheerOnPressProperty, value);
    }

    public object? CheerFor
    {
        get => GetValue(CheerForProperty);
        set => SetValue(CheerForProperty, value);
    }

    /// <summary>Two gentle bobs, then rest.</summary>
    public void Greet() => Play(Hello, ref _bodyMotion);

    /// <summary>A quick squash and stretch, settling back like a jelly.</summary>
    public void Squish() => Play(SquishMotion, ref _bodyMotion);

    /// <summary>A small happy hop that lands with a squish.</summary>
    public void Cheer() => Play(CheerMotion, ref _bodyMotion);

    public void Blink() => Play(BlinkMotion, ref _eyeMotion);

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(Width) ? Width : 22;
        return new Size(width, width * ViewHeight / ViewWidth);
    }

    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / ViewWidth, Bounds.Height / ViewHeight);
        var offsetX = (Bounds.Width - ViewWidth * scale) / 2;
        var offsetY = (Bounds.Height - ViewHeight * scale) / 2;
        var motion = Matrix.CreateTranslation(-BaseX, -BaseY)
                     * Matrix.CreateScale(1 + Squash, 1 - Squash)
                     * Matrix.CreateTranslation(BaseX, BaseY - Lift);

        using (context.PushTransform(motion * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            var outline = OutlineBrush is { } outlineBrush ? new Pen(outlineBrush, 0.5) : null;
            context.DrawGeometry(BodyBrush, outline, Body);

            var face = FaceBrush;
            if (face is null)
            {
                return;
            }

            var stroke = new Pen(face, Mood == PuffMood.Happy ? 0.9 : 1.1, lineCap: PenLineCap.Round);
            if (Mood == PuffMood.Happy)
            {
                context.DrawGeometry(null, new Pen(face, 0.9, lineCap: PenLineCap.Round), HappyEyes);
            }
            else
            {
                var eyeHeight = Math.Clamp(EyesOpen, 0.12, 1);
                context.DrawEllipse(face, null, new Point(9, 12), 1, eyeHeight);
                context.DrawEllipse(face, null, new Point(13.5, 12), 1, eyeHeight);
            }

            context.DrawGeometry(null, stroke, Mood switch
            {
                PuffMood.Happy => HappyMouth,
                PuffMood.Sleepy => SleepyMouth,
                _ => IdleMouth,
            });
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Posted, so whatever shows Puff, such as the toast or the empty state it sits in, is visible by then.
        // Before Puff has loaded, loading greets instead.
        if (change.Property == GreetWhenProperty && GreetWhen && IsLoaded)
        {
            Dispatcher.UIThread.Post(Greet);
        }
        else if (change.Property == CheerForProperty && change.NewValue is not null && !Equals(change.NewValue, change.OldValue))
        {
            Dispatcher.UIThread.Post(Cheer);
        }
        else if ((change.Property == IsStillProperty && IsStill) || (change.Property == IsVisibleProperty && !IsVisible))
        {
            Stop(ref _bodyMotion);
            Stop(ref _eyeMotion);
            SyncBlinking();
        }
        else if (change.Property == IsStillProperty || change.Property == BlinksProperty || change.Property == IsVisibleProperty)
        {
            SyncBlinking();
        }
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SyncBlinking();
        if (GreetWhen)
        {
            Greet();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop(ref _bodyMotion);
        Stop(ref _eyeMotion);
        SyncBlinking();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (SquishOnHover)
        {
            Squish();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CheerOnPress)
        {
            Cheer();
        }
    }

    /// <summary>
    /// Plays a reaction from the start, replacing whatever that part of Puff was doing. It's skipped while Puff is
    /// still, hidden or not in a window, so nobody pays for motion they can't see.
    /// </summary>
    private void Play(Animation animation, ref CancellationTokenSource? current)
    {
        if (IsStill || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
        {
            return;
        }

        Stop(ref current);
        current = new CancellationTokenSource();
        _ = animation.RunAsync(this, current.Token);
    }

    private static void Stop(ref CancellationTokenSource? current)
    {
        current?.Cancel();
        current?.Dispose();
        current = null;
    }

    /// <summary>A blink every four to nine seconds, so it never ticks like a clock. The timer only runs while it can matter.</summary>
    private void SyncBlinking()
    {
        var blinking = Blinks && !IsStill && IsVisible && TopLevel.GetTopLevel(this) is not null;
        if (blinking && _blinkTimer is null)
        {
            _blinkTimer = new DispatcherTimer { Interval = NextBlink() };
            _blinkTimer.Tick += (_, _) =>
            {
                Blink();
                _blinkTimer!.Interval = NextBlink();
            };
            _blinkTimer.Start();
        }
        else if (!blinking && _blinkTimer is not null)
        {
            _blinkTimer.Stop();
            _blinkTimer = null;
        }
    }

    private static TimeSpan NextBlink() => TimeSpan.FromMilliseconds(Random.Shared.Next(4000, 9000));

    /// <summary>
    /// An animation through the given values. Avalonia eases a whole animation at once, so each step between
    /// keyframes gets its own gentle ease instead, the way a bounce slows at the top of every rise.
    /// </summary>
    private static Animation Keyframes(TimeSpan duration, params (double Cue, AvaloniaProperty Property, double Value)[] frames)
    {
        var animation = new Animation { Duration = duration };
        foreach (var cue in frames.GroupBy(frame => frame.Cue))
        {
            var keyFrame = new KeyFrame { Cue = new Cue(cue.Key), KeySpline = new KeySpline(0.37, 0, 0.63, 1) };
            foreach (var (_, property, value) in cue)
            {
                keyFrame.Setters.Add(new Setter(property, value));
            }

            animation.Children.Add(keyFrame);
        }

        return animation;
    }
}
