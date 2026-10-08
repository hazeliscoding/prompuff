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
using Prompuff.Infrastructure.Diagnostics;
using Prompuff.Infrastructure.Logging;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.App;

/// <summary>How this copy was started, and the lock that makes it the one copy for its library.</summary>
internal sealed record LaunchContext(SingleInstance Instance, LaunchRequest Request);

public sealed class App : Avalonia.Application
{
    private ILogger<App>? _logger;

    public IServiceProvider? Services { get; private set; }

    /// <summary>Set by <see cref="Program"/> before the app starts. Null in the designer and in tests.</summary>
    internal static LaunchContext? Launch { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Themes.ThemeBuilder.Register(this);
    }

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
            MacAppMenu.Install(this, viewModel);
            var window = new MainWindow(viewModel, Services.GetRequiredService<ISettingsStore>());
            Services.GetRequiredService<TopLevelAccessor>().TopLevel = window;
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => (Services as IDisposable)?.Dispose();
            Dispatcher.UIThread.Post(() => _ = viewModel.InitializeAsync());
            var installer = Services.GetRequiredService<ICommandLineInstaller>();
            _ = Task.Run(installer.RefreshIfInstalled);

            if (Launch is { } launch)
            {
                if (launch.Request == LaunchRequest.QuickSave)
                {
                    Dispatcher.UIThread.Post(() => _ = viewModel.QuickSaveFromOutsideAsync());
                }

                // Later launches arrive on a background thread.
                launch.Instance.SetHandler(request => Dispatcher.UIThread.Post(() => _ = HandleLaunchAsync(window, viewModel, request)));
            }

            InstallTray(window, viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InstallTray(MainWindow window, MainWindowViewModel viewModel)
    {
        try
        {
            TrayMenu.Install(
                this,
                quickSave: () => _ = HandleLaunchAsync(window, viewModel, LaunchRequest.QuickSave),
                open: () => WindowActivation.BringForward(window),
                quit: window.Quit,
                quickSaveHint: viewModel.QuickSaveShortcut);
        }
        catch (Exception exception)
        {
            // A desktop without a tray still has the window, the shortcuts and a second launch to bring it back.
            _logger?.LogWarning(exception, "Couldn't add the tray icon");
        }

        // macOS: clicking the Dock icon while the window is hidden in the menu bar brings it back.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Reopen && window.IsInTray)
                {
                    WindowActivation.BringForward(window);
                }
            };
        }
    }

    /// <summary>Builds the service container. Used by the app and by the headless UI tests, which can replace services.</summary>
    public static ServiceProvider ConfigureServices(AppDataPathProvider paths, Action<IServiceCollection>? replace = null)
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
        services.AddSingleton<IGlobalHotkeyService>(_ => GlobalHotkeys.Create());
        services.AddSingleton<ICommandLineInstaller, CommandLineInstaller>();
        services.AddSingleton<DiagnosticReport>();

        services.AddSingleton<Navigator>();
        services.AddSingleton<LibraryNotifier>();
        services.AddSingleton<LibraryWatcher>();
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
        services.AddSingleton<WorkflowsViewModel>();
        services.AddTransient<WorkflowViewModel>();
        services.AddSingleton<Func<WorkflowViewModel>>(provider => provider.GetRequiredService<WorkflowViewModel>);
        replace?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private Task HandleLaunchAsync(MainWindow window, MainWindowViewModel viewModel, LaunchRequest request)
    {
        _logger?.LogInformation("Asked from outside the window to {Request}", request);
        return window.HandleLaunchAsync(request);
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
