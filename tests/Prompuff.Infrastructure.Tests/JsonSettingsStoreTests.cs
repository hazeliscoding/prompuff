using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.Application.DTOs;
using Prompuff.Application.Settings;
using Prompuff.Infrastructure.Storage;

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
