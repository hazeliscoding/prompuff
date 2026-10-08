using Microsoft.Extensions.Logging.Abstractions;
using Prompuff.App.Platform;
using Prompuff.Application;
using Prompuff.Infrastructure.Storage;

namespace Prompuff.App.Tests;

public sealed class CommandLineInstallerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "prompuff-installer-tests", Guid.NewGuid().ToString("N"));
    private readonly JsonSettingsStore _settings;

    public CommandLineInstallerTests()
    {
        var paths = new AppDataPathProvider(new PlatformEnvironment(
            PlatformEnvironment.Current.Platform,
            PlatformEnvironment.Current.HomeDirectory,
            name => name == AppDataPathProvider.OverrideVariable ? Path.Combine(_folder, "data") : null));
        _settings = new JsonSettingsStore(paths, NullLogger<JsonSettingsStore>.Instance);
    }

    private string Home => Path.Combine(_folder, "home");
    private string Data => Path.Combine(_folder, "data");
    private string Link => Path.Combine(Home, ".local", "bin", "prompuff");
    private string Copy => Path.Combine(Data, "cli", "prompuff");

    /// <summary>An installed Prompuff, with a CLI and its SQLite library beside it, whose contents say which version it is.</summary>
    private CommandLineInstaller Install(string contents = "v1")
    {
        var app = Path.Combine(_folder, "app");
        Directory.CreateDirectory(Path.Combine(app, "cli"));
        File.WriteAllText(Path.Combine(app, "cli", OperatingSystem.IsWindows() ? "prompuff.exe" : "prompuff"), contents);
        File.WriteAllText(Path.Combine(app, "cli", "libe_sqlite3.so"), "sqlite");
        return new CommandLineInstaller(_settings, NullLogger<CommandLineInstaller>.Instance, app, Home, Data);
    }

    [Fact]
    public void Windows_PATH_entries_are_added_once_and_removed_cleanly()
    {
        const string cli = @"C:\Users\Puff\AppData\Local\Prompuff.Desktop\current\cli";

        Assert.Equal(cli, CommandLineInstaller.AddFolder(null, cli));
        Assert.Equal(@"C:\Tools;" + cli, CommandLineInstaller.AddFolder(@"C:\Tools;", cli));
        Assert.Equal(@"C:\Tools;%USERPROFILE%\bin;" + cli, CommandLineInstaller.AddFolder(@"C:\Tools;%USERPROFILE%\bin", cli));

        var already = @"C:\Tools;" + cli.ToUpperInvariant() + @"\";
        Assert.True(CommandLineInstaller.ContainsFolder(already, cli));
        Assert.Equal(already, CommandLineInstaller.AddFolder(already, cli));

        Assert.Equal(@"C:\Tools;%USERPROFILE%\bin", CommandLineInstaller.RemoveFolder(@"C:\Tools;" + cli + @";%USERPROFILE%\bin", cli));
        Assert.Equal(string.Empty, CommandLineInstaller.RemoveFolder(cli, cli));
        Assert.False(CommandLineInstaller.ContainsFolder(@"C:\Tools;C:\Users\Puff\AppData\Local\Prompuff.Desktop\current", cli));
    }

    [Fact]
    public void A_development_build_has_nothing_to_install()
    {
        var installer = new CommandLineInstaller(_settings, NullLogger<CommandLineInstaller>.Instance, _folder, Home, Data);

        Assert.Null(installer.BundledPath);
        Assert.False(installer.IsInstalled);
        Assert.Throws<LibraryException>(installer.Install);
    }

    [Fact]
    public void Linux_and_macOS_link_it_from_local_bin_and_keep_it_current()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows installs through the user PATH, which a test mustn't change.");
        }

        var installer = Install("v1");
        Assert.Equal(Link, installer.CommandPath);
        Assert.False(installer.IsInstalled);

        installer.Install();
        Assert.True(installer.IsInstalled);
        Assert.Equal(Copy, new FileInfo(Link).LinkTarget);
        Assert.Equal("v1", File.ReadAllText(Link));
        Assert.Equal("sqlite", File.ReadAllText(Path.Combine(Data, "cli", "libe_sqlite3.so")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(Copy).HasFlag(UnixFileMode.UserExecute));
        }

        Assert.Equal(Link, _settings.Load().InstalledCommandLineTool);
        Assert.True(installer.NeedsPathSetup);

        // An update ships a newer CLI; the next start replaces the copy.
        installer = Install("v2");
        installer.RefreshIfInstalled();
        Assert.Equal("v2", File.ReadAllText(Link));

        // Installing again is harmless.
        installer.Install();
        Assert.True(installer.IsInstalled);

        installer.Remove();
        Assert.False(File.Exists(Link));
        Assert.False(Directory.Exists(Path.Combine(Data, "cli")));
        Assert.False(installer.IsInstalled);
        Assert.Null(_settings.Load().InstalledCommandLineTool);
    }

    [Fact]
    public void A_prompuff_Prompuff_didnt_put_there_is_left_alone()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows installs through the user PATH, which a test mustn't change.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Link)!);
        File.WriteAllText(Link, "someone else's script");
        var installer = Install();

        var refused = Assert.Throws<LibraryException>(installer.Install);
        Assert.Contains("wasn't put there by Prompuff", refused.Message);
        Assert.Equal("someone else's script", File.ReadAllText(Link));

        installer.RefreshIfInstalled();
        installer.Remove();
        Assert.Equal("someone else's script", File.ReadAllText(Link));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
