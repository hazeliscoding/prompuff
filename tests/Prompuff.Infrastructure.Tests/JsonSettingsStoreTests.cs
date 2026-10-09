using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.DTOs;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Storage;
using Prompuff.Tests;

namespace Prompuff.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "prompuff-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonSettingsStore _store;

    public JsonSettingsStoreTests()
    {
        var paths = new AppDataPathProvider(new PlatformEnvironment(
            OperatingSystem.IsWindows() ? HostPlatform.Windows : HostPlatform.Linux,
            "unused",
            name => name == AppDataPathProvider.OverrideVariable ? _folder : null));
        _store = new JsonSettingsStore(paths, NullLogger<JsonSettingsStore>.Instance);
    }

    [Fact]
    public void Settings_files_from_earlier_versions_still_read_and_keep_their_spelling()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """
            {
              "theme": "light",
              "showMascot": false,
              "libraryLayout": "list",
              "librarySort": "lastEdited",
              "density": "compact",
              "updateChannel": "stable"
            }
            """);

        var settings = _store.Load();
        Assert.Equal(ThemePreference.Light, settings.Theme);
        Assert.False(settings.ShowMascot);
        Assert.Equal(LibraryLayout.List, settings.LibraryLayout);
        Assert.Equal(PromptSort.LastEdited, settings.LibrarySort);
        Assert.Equal(Density.Compact, settings.Density);
        Assert.False(settings.AllowMcp);

        _store.Save(settings with { AllowMcp = true });
        var written = File.ReadAllText(path);
        Assert.Contains("\"theme\": \"light\"", written);
        Assert.Contains("\"librarySort\": \"lastEdited\"", written);
        Assert.Contains("\"allowMcp\": true", written);
    }

    [Fact]
    public void Settings_missing_newer_keys_keep_their_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), """{ "updateChannel": "beta" }""");

        var settings = _store.Load();

        Assert.Equal(UpdateChannel.Beta, settings.UpdateChannel);
        Assert.Equal(AppSettings.Default with { UpdateChannel = UpdateChannel.Beta }, settings);
        Assert.Equal("Ctrl+Alt+P", settings.QuickSaveHotkey);
    }

    /// <summary>Every setting changed from its default, as 1.0 saved them in <c>Fixtures/settings-1.0.0.json</c>.</summary>
    private static readonly AppSettings SavedBy1_0 = new()
    {
        Theme = ThemePreference.System,
        DarkTheme = "tokyo-night",
        LightTheme = "solarized-light",
        ShowMascot = false,
        CheckForUpdatesAutomatically = false,
        UpdateChannel = UpdateChannel.Beta,
        LibraryLayout = LibraryLayout.List,
        Density = Density.Dense,
        KeepRunningInTray = true,
        QuickSaveHotkey = "Ctrl+Shift+Space",
        AllowMcp = true,
        InstalledCommandLineTool = "/home/puff/.local/share/prompuff/bin/prompuff",
        LibrarySort = PromptSort.RecentActivity,
        Window = new WindowPlacement(1440, 900, true),
    };

    /// <summary>
    /// An update keeps every choice: a renamed key or value would quietly reset it, and a file that no longer reads
    /// would reset them all.
    /// </summary>
    [Fact]
    public void Settings_saved_by_1_0_keep_every_choice()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings-1.0.0.json"), Path.Combine(_folder, "settings.json"));

        Assert.Equal(SavedBy1_0, _store.Load());
    }

    /// <summary>
    /// Writes <c>Fixtures/settings-1.0.0.json</c> with this version's store and pins it. It's skipped unless
    /// <c>PROMPUFF_WRITE_SETTINGS_FIXTURE</c> is set, and refuses to overwrite the pinned file.
    /// </summary>
    [Fact]
    public void Write_fixture()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PROMPUFF_WRITE_SETTINGS_FIXTURE")))
        {
            Assert.Skip("Set PROMPUFF_WRITE_SETTINGS_FIXTURE to write the settings fixture.");
        }

        var fixture = Path.Combine(SchemaFixtures.SourceFolder, "settings-1.0.0.json");
        PinnedFixtures.EnsureUnpinned(fixture);
        _store.Save(SavedBy1_0);
        File.Copy(Path.Combine(_folder, "settings.json"), fixture);
        PinnedFixtures.Pin(fixture);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(AppSettings.Default, _store.Load());
    }

    [Fact]
    public void Settings_round_trip()
    {
        var settings = new AppSettings
        {
            Theme = ThemePreference.Light,
            ShowMascot = false,
            CheckForUpdatesAutomatically = false,
            LibraryLayout = LibraryLayout.List,
            LibrarySort = PromptSort.Usefulness,
            Window = new WindowPlacement(1280, 800, true),
        };

        _store.Save(settings);

        Assert.Equal(settings, _store.Load());
        Assert.Contains("\"theme\": \"light\"", File.ReadAllText(Path.Combine(_folder, "settings.json")));
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "settings.json"), "{ not json");

        Assert.Equal(AppSettings.Default, _store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
