using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Prompuff.Application;
using Prompuff.Application.Interfaces;

namespace Prompuff.App.Platform;

/// <summary>Puts the <c>prompuff</c> command-line tool on the user's PATH, and takes it off again.</summary>
public interface ICommandLineInstaller
{
    /// <summary>The CLI shipped with this copy of Prompuff, or null in a development build.</summary>
    string? BundledPath { get; }

    /// <summary>What an AI tool's config should run: the shipped file on Windows, the link in ~/.local/bin elsewhere.</summary>
    string CommandPath { get; }

    bool IsInstalled { get; }

    /// <summary>True on Linux and macOS when the CLI is installed but <c>~/.local/bin</c> isn't on Prompuff's PATH.</summary>
    bool NeedsPathSetup { get; }

    /// <summary>Adds the CLI's folder to the user PATH (Windows), or links it from <c>~/.local/bin</c> (Linux, macOS).</summary>
    void Install();

    void Remove();

    /// <summary>After an update, replaces an installed copy that's older than the one shipped. Linux and macOS only.</summary>
    void RefreshIfInstalled();
}

/// <summary>
/// The one place Prompuff writes outside its data and config folders, and only when the user asks. On Windows it adds
/// the CLI's folder inside the install to the user PATH; that folder stays put across updates. On Linux and macOS
/// it copies the CLI into Prompuff's data folder, because an AppImage can't be reached while Prompuff is closed, and
/// links <c>~/.local/bin/prompuff</c> to the copy. The CLI is a single file with SQLite's native library beside it,
/// rather than bundled, because a bundled one would be unpacked into <c>~/.net</c>. A file Prompuff didn't put in
/// <c>~/.local/bin</c> is never replaced.
/// </summary>
public sealed class CommandLineInstaller : ICommandLineInstaller
{
    private static readonly string ExecutableName = OperatingSystem.IsWindows() ? "prompuff.exe" : "prompuff";

    private readonly ISettingsStore _settings;
    private readonly ILogger<CommandLineInstaller> _logger;
    private readonly string _bundledFolder;
    private readonly string _copyFolder;
    private readonly string _homeDirectory;

    public CommandLineInstaller(ISettingsStore settings, IAppDataPathProvider paths, ILogger<CommandLineInstaller> logger)
        : this(settings, logger, AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), paths.GetAppDataDirectory())
    {
    }

    internal CommandLineInstaller(ISettingsStore settings, ILogger<CommandLineInstaller> logger, string baseDirectory, string homeDirectory, string dataDirectory)
    {
        _settings = settings;
        _logger = logger;
        _homeDirectory = homeDirectory;
        _bundledFolder = Path.Combine(baseDirectory, "cli");
        _copyFolder = Path.Combine(dataDirectory, "cli");
        var bundled = Path.Combine(_bundledFolder, ExecutableName);
        BundledPath = File.Exists(bundled) ? bundled : null;
    }

    public string? BundledPath { get; }

    public string CommandPath => OperatingSystem.IsWindows() ? BundledPath ?? "prompuff" : LinkPath;

    /// <summary><c>~/.local/bin/prompuff</c>, where Linux and macOS installs go.</summary>
    public string LinkPath => Path.Combine(_homeDirectory, ".local", "bin", "prompuff");

    private string CopyPath => Path.Combine(_copyFolder, ExecutableName);

    public bool IsInstalled => OperatingSystem.IsWindows()
        ? BundledPath is not null && ContainsFolder(ReadUserPath(), _bundledFolder)
        : _settings.Load().InstalledCommandLineTool == LinkPath && File.Exists(LinkPath) && File.Exists(CopyPath);

    public bool NeedsPathSetup => !OperatingSystem.IsWindows() && IsInstalled
        && !(Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Any(entry => entry.TrimEnd('/') == Path.GetDirectoryName(LinkPath));

    public void Install()
    {
        if (BundledPath is null)
        {
            throw new LibraryException("This build of Prompuff doesn't include the command-line tool. Installed copies do.");
        }

        if (OperatingSystem.IsWindows())
        {
            WriteUserPath(AddFolder(ReadUserPath(), _bundledFolder));
            _logger.LogInformation("Added the command-line tool's folder to the user PATH");
            return;
        }

        var link = LinkPath;
        if (File.Exists(link) && !IsOurs(link))
        {
            throw new LibraryException($"{link} already exists and wasn't put there by Prompuff, so it's been left alone.");
        }

        CopyChangedFiles();
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.Delete(link);
        File.CreateSymbolicLink(link, CopyPath);
        _settings.Save(_settings.Load() with { InstalledCommandLineTool = link });
        _logger.LogInformation("Installed the command-line tool in ~/.local/bin");
    }

    public void Remove()
    {
        if (OperatingSystem.IsWindows())
        {
            if (ContainsFolder(ReadUserPath(), _bundledFolder))
            {
                WriteUserPath(RemoveFolder(ReadUserPath(), _bundledFolder));
                _logger.LogInformation("Removed the command-line tool's folder from the user PATH");
            }

            return;
        }

        if (File.Exists(LinkPath) && IsOurs(LinkPath))
        {
            File.Delete(LinkPath);
        }

        if (Directory.Exists(_copyFolder))
        {
            Directory.Delete(_copyFolder, recursive: true);
        }

        _settings.Save(_settings.Load() with { InstalledCommandLineTool = null });
        _logger.LogInformation("Removed the command-line tool");
    }

    public void RefreshIfInstalled()
    {
        if (OperatingSystem.IsWindows() || BundledPath is null || !IsInstalled)
        {
            return;
        }

        try
        {
            if (CopyChangedFiles() > 0)
            {
                _logger.LogInformation("Updated the installed command-line tool");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "Couldn't update the installed command-line tool");
        }
    }

    /// <summary>Velopack runs this when Prompuff is uninstalled on Windows, so the PATH doesn't point at a deleted folder.</summary>
    public static void RemoveFromUserPathOnUninstall()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "cli");
        if (OperatingSystem.IsWindows() && ContainsFolder(ReadUserPath(), folder))
        {
            WriteUserPath(RemoveFolder(ReadUserPath(), folder));
        }
    }

    /// <summary>The user PATH with the folder added once at the end. Comparison ignores case and a trailing slash.</summary>
    internal static string AddFolder(string? path, string folder) =>
        ContainsFolder(path, folder) ? path! : string.IsNullOrEmpty(path) ? folder : path.TrimEnd(';') + ";" + folder;

    internal static string RemoveFolder(string? path, string folder) =>
        string.Join(';', (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Where(entry => !SameFolder(entry, folder)));

    internal static bool ContainsFolder(string? path, string folder) =>
        (path ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries).Any(entry => SameFolder(entry, folder));

    private static bool SameFolder(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('\\', '/'), b.Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A link to Prompuff's copy, or whatever Prompuff recorded installing there.</summary>
    private bool IsOurs(string link) =>
        _settings.Load().InstalledCommandLineTool == link || new FileInfo(link).LinkTarget == CopyPath;

    /// <summary>Copies the shipped CLI's files into the data folder, skipping ones that are already the same.</summary>
    private int CopyChangedFiles()
    {
        Directory.CreateDirectory(_copyFolder);
        var copied = 0;
        foreach (var source in Directory.EnumerateFiles(_bundledFolder))
        {
            var target = Path.Combine(_copyFolder, Path.GetFileName(source));
            if (File.Exists(target) && SameContents(source, target))
            {
                continue;
            }

            var temporary = target + ".tmp";
            File.Copy(source, temporary, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                                                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            if (OperatingSystem.IsMacOS())
            {
                // The copy inherits the download's quarantine flag. The user already let Prompuff run, and asked for
                // its command; without this, macOS refuses to start it from Terminal.
                _ = RemoveExtendedAttribute(temporary, "com.apple.quarantine", 0);
            }

            File.Move(temporary, target, overwrite: true);
            copied++;
        }

        return copied;
    }

    private static bool SameContents(string a, string b)
    {
        var first = new FileInfo(a);
        var second = new FileInfo(b);
        if (first.Length != second.Length)
        {
            return false;
        }

        using var left = first.OpenRead();
        using var right = second.OpenRead();
        Span<byte> one = stackalloc byte[8192];
        Span<byte> two = stackalloc byte[8192];
        int read;
        while ((read = left.Read(one)) > 0)
        {
            right.ReadExactly(two[..read]);
            if (!one[..read].SequenceEqual(two[..read]))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadUserPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private static void WriteUserPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using (var key = Registry.CurrentUser.CreateSubKey("Environment", writable: true))
        {
            key.SetValue("Path", path, RegistryValueKind.ExpandString);
        }

        // Tell Explorer, so terminals opened from now on see the new PATH.
        _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [SupportedOSPlatform("windows")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("libc", EntryPoint = "removexattr", CharSet = CharSet.Ansi)]
    [SupportedOSPlatform("macos")]
    private static extern int RemoveExtendedAttribute(string path, string name, int options);
}
