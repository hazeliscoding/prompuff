using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Prompuff.App.Platform;

/// <summary>
/// The tray icon (the menu bar on macOS) with Quick save, Open and Quit. Linux shows it through the
/// StatusNotifierItem protocol, so desktops without a tray host, such as stock GNOME, simply don't show it.
/// </summary>
internal static class TrayMenu
{
    public static TrayIcon Install(Avalonia.Application application, Action quickSave, Action open, Action quit, string quickSaveHint)
    {
        var tray = new TrayIcon
        {
            ToolTipText = "Prompuff",
            Icon = CreateIcon(),
            Menu = new NativeMenu
            {
                Item("Quick save from clipboard", quickSave, quickSaveHint),
                Item("Open Prompuff", open),
                new NativeMenuItemSeparator(),
                Item("Quit Prompuff", quit),
            },
        };

        if (OperatingSystem.IsMacOS())
        {
            MacOSProperties.SetIsTemplateIcon(tray, true);
        }

        tray.Clicked += (_, _) => open();
        TrayIcon.SetIcons(application, [tray]);
        return tray;
    }

    private static NativeMenuItem Item(string header, Action action, string? hint = null)
    {
        var item = new NativeMenuItem(header) { ToolTip = hint };
        item.Click += (_, _) => action();
        return item;
    }

    private static WindowIcon CreateIcon()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://Prompuff/Assets/prompuff-256.png")));
        }

        using var png = new MemoryStream();
        RenderTemplateIcon(44).Save(png);
        png.Position = 0;
        return new WindowIcon(png);
    }

    /// <summary>
    /// The macOS menu bar wants a template image: black on transparent, which it tints for light and dark bars.
    /// Puff's face is cut out of the cloud so it still reads at 22 points.
    /// </summary>
    internal static Bitmap RenderTemplateIcon(int size)
    {
        var body = StreamGeometry.Parse("M6 16.5h10.5a4 4 0 0 0 .6-7.95A5.5 5.5 0 0 0 6.6 7.2 4.7 4.7 0 0 0 6 16.5Z");
        var face = new GeometryGroup
        {
            Children =
            {
                new EllipseGeometry(new Rect(8, 11, 2, 2)),
                new EllipseGeometry(new Rect(12.5, 11, 2, 2)),
                StreamGeometry.Parse("M10.2 14.1q1.05.6 2.1 0").GetWidenedGeometry(new Pen(Brushes.Black, 1.1, lineCap: PenLineCap.Round)),
            },
        };
        var silhouette = new CombinedGeometry(GeometryCombineMode.Exclude, body, face);

        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            // The cloud spans x 1.3 to 20.9 and y 3.4 to 16.5 in its 22-unit drawing; centre it in the square.
            var scale = size / 21.0;
            using (context.PushTransform(Matrix.CreateTranslation(-0.6, -3.6) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(0, (size - 13.6 * scale) / 2)))
            {
                context.DrawGeometry(Brushes.Black, null, silhouette);
            }
        }

        return bitmap;
    }
}
