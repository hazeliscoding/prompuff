using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Prompuff.App.Controls;

public enum PuffMood
{
    Idle,
    Happy,
    Sleepy,
}

/// <summary>Puff, the cloud mascot, drawn from the design's 22×18 geometry.</summary>
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

    private const double ViewWidth = 22;
    private const double ViewHeight = 18;

    private static readonly Geometry Body = StreamGeometry.Parse("M6 16.5h10.5a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 6.6 7.2 4.7 4.7 0 0 0 6 16.5Z");
    private static readonly Geometry IdleMouth = StreamGeometry.Parse("M10.2 14.1q1.05.6 2.1 0");
    private static readonly Geometry HappyMouth = StreamGeometry.Parse("M10 13.9q1.25 1.3 2.5 0");
    private static readonly Geometry SleepyMouth = StreamGeometry.Parse("M10.2 14.2h2");
    private static readonly Geometry HappyEyes = StreamGeometry.Parse("M8.2 12.2q.8-.9 1.6 0M12.7 12.2q.8-.9 1.6 0");

    static Puff()
    {
        AffectsRender<Puff>(MoodProperty, BodyBrushProperty, FaceBrushProperty, OutlineBrushProperty);
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

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
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
                context.DrawEllipse(face, null, new Point(9, 12), 1, 1);
                context.DrawEllipse(face, null, new Point(13.5, 12), 1, 1);
            }

            context.DrawGeometry(null, stroke, Mood switch
            {
                PuffMood.Happy => HappyMouth,
                PuffMood.Sleepy => SleepyMouth,
                _ => IdleMouth,
            });
        }
    }
}
