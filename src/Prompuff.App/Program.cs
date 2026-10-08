using Avalonia;
using Avalonia.Media;
using Prompuff.App.Platform;
using Prompuff.Infrastructure.Storage;
using Velopack;

namespace Prompuff.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Velopack must run first: it handles install, update and uninstall hooks and may exit the process.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopack.OnBeforeUninstallFastCallback(_ => CommandLineInstaller.RemoveFromUserPathOnUninstall());
        }

        velopack.Run();

        // One Prompuff per library. A second launch, such as "Prompuff --quick-save" from a desktop shortcut, hands
        // its request to the copy that's already running and exits.
        var request = LaunchArguments.Parse(args);
        using var instance = SingleInstance.Claim(new AppDataPathProvider().GetAppDataDirectory());
        if (!instance.IsPrimary)
        {
            return instance.HandOff(request, TimeSpan.FromSeconds(5)) ? 0 : 1;
        }

        instance.Listen();
        App.Launch = new LaunchContext(instance, request);
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
