using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Prompuff.App.Platform;
using Prompuff.App.ViewModels;
using Prompuff.App.Views;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Storage;

[assembly: AvaloniaTestApplication(typeof(Prompuff.App.Tests.TestAppBuilder))]

namespace Prompuff.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .With(new FontManagerOptions { DefaultFamilyName = Fonts.Sans });
}

/// <summary>
/// Runs the real main window headlessly against a temporary data folder. Set PROMPUFF_SCREENSHOTS to a folder
/// to save a PNG of each captured state.
/// </summary>
internal sealed class AppHarness : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private AppHarness(string folder, ServiceProvider services, MainWindow window, MainWindowViewModel viewModel)
    {
        Folder = folder;
        _services = services;
        Window = window;
        ViewModel = viewModel;
    }

    public string Folder { get; }
    /// <summary>Quick save's hotkey.</summary>
    public FakeGlobalHotkeyService Hotkeys { get; private init; } = null!;

    public FakeGlobalHotkeyService QuickInsertHotkeys { get; private init; } = null!;
    public MainWindow Window { get; }
    public MainWindowViewModel ViewModel { get; }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public static async Task<AppHarness> StartAsync(
        bool importSamples = true,
        ThemePreference theme = ThemePreference.Dark,
        string? folder = null,
        double width = 1280,
        double height = 800,
        IUpdateService? updates = null,
        IFilePickerService? files = null,
        FakeGlobalHotkeyService? hotkeys = null,
        FakeGlobalHotkeyService? quickInsertHotkeys = null,
        IInsertTarget? insertTarget = null,
        FakeCommandLineInstaller? installer = null,
        bool systemReducesMotion = false)
    {
        folder ??= Path.Combine(Path.GetTempPath(), "prompuff-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var paths = new AppDataPathProvider(new PlatformEnvironment(
            PlatformEnvironment.Current.Platform,
            PlatformEnvironment.Current.HomeDirectory,
            name => name == AppDataPathProvider.OverrideVariable ? folder : null));

        hotkeys ??= new FakeGlobalHotkeyService();
        quickInsertHotkeys ??= new FakeGlobalHotkeyService { IsSupported = hotkeys.IsSupported, UnsupportedReason = hotkeys.UnsupportedReason };
        var services = App.ConfigureServices(paths, replace: collection =>
        {
            // Tests never grab real system-wide keys.
            collection.AddSingleton(new GlobalHotkeyServices(hotkeys, quickInsertHotkeys));

            // Nor press keys in other apps.
            collection.AddSingleton(insertTarget ?? new FakeInsertTarget());

            // Nor touch the real PATH.
            collection.AddSingleton<ICommandLineInstaller>(installer ?? new FakeCommandLineInstaller());

            // Nor depend on the machine's animation setting.
            collection.AddSingleton<IMotionPreference>(new FakeMotionPreference(systemReducesMotion));

            if (updates is not null)
            {
                collection.AddSingleton(updates);
            }

            if (files is not null)
            {
                collection.AddSingleton(files);
            }
        });
        var settings = services.GetRequiredService<ISettingsStore>();
        settings.Save(settings.Load() with { Theme = theme, CheckForUpdatesAutomatically = false, Window = null });

        if (importSamples)
        {
            await services.GetRequiredService<Infrastructure.Persistence.SqliteDatabase>().InitializeAsync();
            var samples = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "samples"), "*.md").Order().ToList();
            var result = await services.GetRequiredService<IPromptTransferService>().ImportFilesAsync(samples);
            Assert.Empty(result.Failures);
        }

        var viewModel = services.GetRequiredService<MainWindowViewModel>();
        var window = new MainWindow(viewModel, settings) { Width = width, Height = height };
        services.GetRequiredService<TopLevelAccessor>().TopLevel = window;
        window.Show();
        await viewModel.InitializeAsync();
        var harness = new AppHarness(folder, services, window, viewModel) { Hotkeys = hotkeys, QuickInsertHotkeys = quickInsertHotkeys };
        await harness.SettleAsync();
        return harness;
    }

    /// <summary>Lets pending UI work, bindings and debounced refreshes finish.</summary>
    public async Task SettleAsync(int milliseconds = 250)
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(milliseconds);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Waits for background work, such as the startup backup, to reach a state.</summary>
    public async Task WaitForAsync(Func<bool> condition, int timeoutMilliseconds = 5000)
    {
        for (var waited = 0; !condition(); waited += 50)
        {
            Assert.True(waited < timeoutMilliseconds, "Timed out waiting for the condition.");
            await SettleAsync(50);
        }
    }

    /// <summary>Everything logged so far. The logger keeps its file open for writing, so it's read alongside it.</summary>
    public string ReadLog() => string.Concat(Directory.GetFiles(Get<IAppDataPathProvider>().GetLogsDirectory(), "*.log").Order().Select(path =>
    {
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        return reader.ReadToEnd();
    }));

    public void Screenshot(string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = Window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("PROMPUFF_SCREENSHOTS") is { Length: > 0 } output)
        {
            Directory.CreateDirectory(output);
            frame.Save(Path.Combine(output, name + ".png"));
        }
    }

    public async ValueTask DisposeAsync()
    {
        Window.Close();
        var database = _services.GetRequiredService<Infrastructure.Persistence.SqliteDatabase>();
        await _services.DisposeAsync();
        database.ClearPool();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Temp folders are cleaned up by the OS eventually.
        }
    }

    /// <summary>Closes the window and services but keeps the data folder, like quitting the app.</summary>
    public async Task QuitAsync()
    {
        Assert.True(await ViewModel.PrepareToCloseAsync());
        Window.Close();
        var database = _services.GetRequiredService<Infrastructure.Persistence.SqliteDatabase>();
        await _services.DisposeAsync();
        database.ClearPool();
    }
}

/// <summary>Stands in for the system's animation setting.</summary>
internal sealed class FakeMotionPreference(bool reduced) : IMotionPreference
{
    public bool SystemPrefersReducedMotion() => reduced;
}

/// <summary>Stands in for the system-wide hotkey: records what was registered and presses it on request.</summary>
internal sealed class FakeGlobalHotkeyService : IGlobalHotkeyService
{
    public bool IsSupported { get; set; } = true;
    public string? UnsupportedReason { get; set; }
    public Hotkey? Registered { get; private set; }

    /// <summary>Combinations "another app" already owns.</summary>
    public HashSet<string> Taken { get; } = [];

    public event EventHandler? Pressed;

    public Task<bool> RegisterAsync(Hotkey? hotkey)
    {
        var ok = hotkey is null || !Taken.Contains(hotkey.ToString());
        Registered = ok ? hotkey : null;
        return Task.FromResult(ok);
    }

    public void Press() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
    }
}

/// <summary>Stands in for the app Quick insert was opened from: records stepping back to it and pasting there.</summary>
internal sealed class FakeInsertTarget : IInsertTarget
{
    public bool IsSupported { get; set; } = true;
    public bool CanPaste { get; set; } = true;

    /// <summary>Whether the app takes focus back.</summary>
    public bool Returns { get; set; } = true;

    public bool Remembered { get; private set; }
    public int ReturnCount { get; private set; }
    public int PasteCount { get; private set; }
    public int AccessRequests { get; private set; }

    public void Remember(IntPtr ownWindow) => Remembered = true;

    public Task<bool> ReturnAsync()
    {
        ReturnCount++;
        return Task.FromResult(Returns && Remembered);
    }

    public Task<bool> PasteAsync()
    {
        PasteCount++;
        return Task.FromResult(true);
    }

    public void RequestPasteAccess() => AccessRequests++;
}

/// <summary>Stands in for putting the CLI on the PATH. Starts as an installed build whose CLI isn't installed yet.</summary>
internal sealed class FakeCommandLineInstaller : ICommandLineInstaller
{
    public string? BundledPath { get; set; } = OperatingSystem.IsWindows()
        ? @"C:\Users\Puff Lover\AppData\Local\Prompuff.Desktop\current\cli\prompuff.exe"
        : "/opt/prompuff/cli/prompuff";

    public string CommandPath => OperatingSystem.IsWindows() ? BundledPath ?? "prompuff" : "/home/puff/.local/bin/prompuff";
    public bool IsInstalled { get; set; }
    public bool NeedsPathSetup { get; set; }

    public void Install() => IsInstalled = true;

    public void Remove() => IsInstalled = false;

    public void RefreshIfInstalled()
    {
    }
}
