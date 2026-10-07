using Avalonia;
using Avalonia.Media;
using Velopack;

namespace Prompuff.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must run first: it handles install, update and uninstall hooks and may exit the process.
        VelopackApp.Build().Run();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the Avalonia designer and the headless UI tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = Fonts.Sans })
            .LogToTrace();
}

internal static class Fonts
{
    public const string Sans = "avares://Prompuff/Assets/Fonts#Manrope";
    public const string Mono = "avares://Prompuff/Assets/Fonts#IBM Plex Mono";
}
