using Prompuff.Infrastructure.Storage;

namespace Prompuff.Infrastructure.Tests;

public class AppDataPathProviderTests
{
    private static AppDataPathProvider For(HostPlatform platform, string home, Dictionary<string, string?> variables) =>
        new(new PlatformEnvironment(platform, home, name => variables.GetValueOrDefault(name)));

    [Fact]
    public void Windows_uses_local_app_data()
    {
        var paths = For(HostPlatform.Windows, @"C:\Users\ada", new() { ["LOCALAPPDATA"] = @"C:\Users\ada\AppData\Local" });

        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff", paths.GetAppDataDirectory());
        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff\prompuff.db", paths.GetDatabasePath());
        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff\settings.json", paths.GetSettingsPath());
        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff\logs", paths.GetLogsDirectory());
        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff\backups", paths.GetBackupDirectory());
        Assert.Equal(@"C:\Users\ada\AppData\Local\Prompuff\exports", paths.GetExportDirectory());
    }

    [Fact]
    public void Windows_falls_back_to_the_profile_when_local_app_data_is_missing()
    {
        var paths = For(HostPlatform.Windows, @"D:\Profiles\ada", []);

        Assert.Equal(@"D:\Profiles\ada\AppData\Local\Prompuff", paths.GetAppDataDirectory());
    }

    [Fact]
    public void Linux_uses_xdg_data_and_config_homes()
    {
        var paths = For(HostPlatform.Linux, "/home/ada", new()
        {
            ["XDG_DATA_HOME"] = "/data/ada",
            ["XDG_CONFIG_HOME"] = "/config/ada/",
        });

        Assert.Equal("/data/ada/prompuff", paths.GetAppDataDirectory());
        Assert.Equal("/data/ada/prompuff/prompuff.db", paths.GetDatabasePath());
        Assert.Equal("/data/ada/prompuff/logs", paths.GetLogsDirectory());
        Assert.Equal("/data/ada/prompuff/backups", paths.GetBackupDirectory());
        Assert.Equal("/data/ada/prompuff/exports", paths.GetExportDirectory());
        Assert.Equal("/config/ada/prompuff", paths.GetConfigDirectory());
        Assert.Equal("/config/ada/prompuff/settings.json", paths.GetSettingsPath());
    }

    [Fact]
    public void Linux_falls_back_to_the_xdg_defaults()
    {
        var paths = For(HostPlatform.Linux, "/home/ada", []);

        Assert.Equal("/home/ada/.local/share/prompuff", paths.GetAppDataDirectory());
        Assert.Equal("/home/ada/.config/prompuff/settings.json", paths.GetSettingsPath());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void Linux_ignores_empty_or_relative_xdg_values(string value)
    {
        var paths = For(HostPlatform.Linux, "/home/ada", new() { ["XDG_DATA_HOME"] = value, ["XDG_CONFIG_HOME"] = value });

        Assert.Equal("/home/ada/.local/share/prompuff", paths.GetAppDataDirectory());
        Assert.Equal("/home/ada/.config/prompuff", paths.GetConfigDirectory());
    }

    [Fact]
    public void MacOS_uses_application_support()
    {
        var paths = For(HostPlatform.MacOS, "/Users/ada", []);

        Assert.Equal("/Users/ada/Library/Application Support/Prompuff/prompuff.db", paths.GetDatabasePath());
    }

    [Fact]
    public void Override_variable_puts_everything_in_one_folder()
    {
        var paths = For(HostPlatform.Linux, "/home/ada", new()
        {
            [AppDataPathProvider.OverrideVariable] = "/tmp/prompuff-dev/",
            ["XDG_DATA_HOME"] = "/data/ada",
        });

        Assert.Equal("/tmp/prompuff-dev/prompuff.db", paths.GetDatabasePath());
        Assert.Equal("/tmp/prompuff-dev/settings.json", paths.GetSettingsPath());
    }

    [Fact]
    public void Data_never_lands_next_to_the_executable()
    {
        var paths = new AppDataPathProvider();

        Assert.False(paths.GetDatabasePath().StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
                     && Environment.GetEnvironmentVariable(AppDataPathProvider.OverrideVariable) is null);
    }
}
