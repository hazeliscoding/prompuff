using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.App.Views;
using Prompuff.Application.Interfaces;
using Prompuff.Infrastructure;
using Prompuff.Infrastructure.Logging;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.App;

public sealed class App : Avalonia.Application
{
    private ILogger<App>? _logger;

    public IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless UI tests have no desktop lifetime; they build their own services against a temporary folder.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var paths = new AppDataPathProvider();
            Services = ConfigureServices(paths);
            _logger = Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Prompuff {Version} starting on {OS}", Services.GetRequiredService<IUpdateService>().CurrentVersion, Environment.OSVersion.Platform);
            HookUnhandledErrors();

            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow(viewModel, Services.GetRequiredService<ISettingsStore>());
            Services.GetRequiredService<TopLevelAccessor>().TopLevel = window;
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();
            Dispatcher.UIThread.Post(() => _ = viewModel.InitializeAsync());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Builds the service container. Used by the app and by the headless UI tests.</summary>
    public static ServiceProvider ConfigureServices(AppDataPathProvider paths)
    {
        try
        {
            paths.EnsureDirectories();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The database step reports a friendly error if the folder really can't be used.
        }

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddProvider(new FileLoggerProvider(paths.GetLogsDirectory()));
#if DEBUG
            logging.AddDebug();
#endif
        });
        services.AddPrompuffCore(paths);

        services.AddSingleton<TopLevelAccessor>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<IPlatformLauncher, AvaloniaLauncher>();

        services.AddSingleton<Navigator>();
        services.AddSingleton<LibraryNotifier>();
        services.AddSingleton<AppearanceState>();
        services.AddSingleton<RenderValuesCache>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<QuickSaveViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<PromptEditorViewModel>();
        services.AddSingleton<Func<PromptEditorViewModel>>(provider => provider.GetRequiredService<PromptEditorViewModel>);
        return services.BuildServiceProvider();
    }

    private void HookUnhandledErrors()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger?.LogError(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            _logger?.LogError(e.Exception, "Unhandled UI exception");
            e.Handled = true;
            if (Services?.GetService<DialogService>() is { } dialogs)
            {
                _ = dialogs.ShowErrorAsync(
                    "Something went wrong.",
                    "Prompuff hit an unexpected problem. Your library is safe on disk.",
                    e.Exception.ToString());
            }
        };
    }
}
