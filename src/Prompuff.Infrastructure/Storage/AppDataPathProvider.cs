using Prompuff.Application.Interfaces;

namespace Prompuff.Infrastructure.Storage;

public enum HostPlatform
{
    Windows,
    Linux,
    MacOS,
}

/// <summary>The parts of the host that decide where files go. Tests pass their own values.</summary>
public sealed record PlatformEnvironment(HostPlatform Platform, string HomeDirectory, Func<string, string?> GetVariable)
{
    public static PlatformEnvironment Current { get; } = new(
        OperatingSystem.IsWindows() ? HostPlatform.Windows
            : OperatingSystem.IsMacOS() ? HostPlatform.MacOS
            : HostPlatform.Linux,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable);
}

/// <summary>
/// Windows: <c>%LOCALAPPDATA%\Prompuff</c>. Linux: <c>$XDG_DATA_HOME/prompuff</c> (or <c>~/.local/share/prompuff</c>)
/// for data and <c>$XDG_CONFIG_HOME/prompuff</c> (or <c>~/.config/prompuff</c>) for settings.
/// <c>PROMPUFF_DATA_DIR</c> overrides both, which keeps development and test runs away from real data.
/// </summary>
public sealed class AppDataPathProvider : IAppDataPathProvider
{
    public const string OverrideVariable = "PROMPUFF_DATA_DIR";

    private readonly char _separator;
    private readonly string _dataDirectory;
    private readonly string _configDirectory;

    public AppDataPathProvider(PlatformEnvironment environment)
    {
        _separator = environment.Platform == HostPlatform.Windows ? '\\' : '/';

        var overrideDirectory = environment.GetVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            _dataDirectory = _configDirectory = TrimEnd(overrideDirectory.Trim());
            return;
        }

        switch (environment.Platform)
        {
            case HostPlatform.Windows:
                var localAppData = environment.GetVariable("LOCALAPPDATA");
                var root = string.IsNullOrWhiteSpace(localAppData)
                    ? Join(environment.HomeDirectory, "AppData", "Local")
                    : localAppData;
                _dataDirectory = _configDirectory = Join(root, "Prompuff");
                break;

            case HostPlatform.MacOS:
                _dataDirectory = _configDirectory = Join(environment.HomeDirectory, "Library", "Application Support", "Prompuff");
                break;

            default:
                _dataDirectory = Join(XdgBase(environment, "XDG_DATA_HOME", ".local", "share"), "prompuff");
                _configDirectory = Join(XdgBase(environment, "XDG_CONFIG_HOME", ".config"), "prompuff");
                break;
        }
    }

    public AppDataPathProvider()
        : this(PlatformEnvironment.Current)
    {
    }

    public string GetAppDataDirectory() => _dataDirectory;
    public string GetDatabasePath() => Join(_dataDirectory, "prompuff.db");
    public string GetLogsDirectory() => Join(_dataDirectory, "logs");
    public string GetBackupDirectory() => Join(_dataDirectory, "backups");
    public string GetExportDirectory() => Join(_dataDirectory, "exports");
    public string GetConfigDirectory() => _configDirectory;
    public string GetSettingsPath() => Join(_configDirectory, "settings.json");

    /// <summary>Creates the data, config, logs and backup folders if they don't exist yet.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(GetAppDataDirectory());
        Directory.CreateDirectory(GetConfigDirectory());
        Directory.CreateDirectory(GetLogsDirectory());
        Directory.CreateDirectory(GetBackupDirectory());
    }

    /// <summary>The XDG spec says relative paths in these variables are invalid and must be ignored.</summary>
    private string XdgBase(PlatformEnvironment environment, string variable, params string[] fallback)
    {
        var value = environment.GetVariable(variable);
        return !string.IsNullOrWhiteSpace(value) && value.StartsWith('/')
            ? value
            : Join([environment.HomeDirectory, .. fallback]);
    }

    private string Join(params string[] parts)
    {
        var result = TrimEnd(parts[0]);
        for (var i = 1; i < parts.Length; i++)
        {
            result += _separator + parts[i].Trim('/', '\\');
        }

        return result;
    }

    private string TrimEnd(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        return trimmed.Length == 0 ? _separator.ToString() : trimmed;
    }
}
