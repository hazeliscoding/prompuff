using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Prompuff.Application;
using Prompuff.Application.Interfaces;
using Prompuff.Application.Settings;
using Velopack;
using Velopack.Sources;

namespace Prompuff.Infrastructure.Updates;

/// <summary>
/// Checks GitHub Releases for new versions with Velopack. This is the only network code in Prompuff,
/// and it sends nothing about the library.
/// </summary>
/// <remarks>
/// Every release goes to one channel per platform: "win", "linux", "linux-arm64", "osx-arm64" or "osx-x64" for stable
/// tags, and the same name with "-beta" for <c>-beta</c> tags, which are GitHub pre-releases. Stable installs only read
/// the stable channel. Beta installs read both and take the newer version, so a stable release that's newer than the
/// last beta reaches them too. Neither ever moves to an older version, so switching back to Stable from a beta waits
/// for the next stable release.
/// </remarks>
public sealed class VelopackUpdateService(ILogger<VelopackUpdateService> logger) : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/hazeliscoding/prompuff";

    private readonly Dictionary<UpdateChannel, UpdateManager> _managers = [];
    private UpdateManager? _activeManager;
    private UpdateInfo? _pending;

    public bool IsSupported
    {
        get
        {
            try
            {
                return Manager(UpdateChannel.Stable).IsInstalled;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Couldn't determine whether Prompuff is installed");
                return false;
            }
        }
    }

    public string CurrentVersion
    {
        get
        {
            if (IsSupported && Manager(UpdateChannel.Stable).CurrentVersion is { } version)
            {
                return version.ToString();
            }

            var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational?.Split('+')[0] ?? "0.0.0";
        }
    }

    public async Task<AvailableUpdate?> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return null;
        }

        try
        {
            UpdateManager? manager = null;
            UpdateInfo? info = null;
            UpdateChannel[] channels = channel == UpdateChannel.Beta ? [UpdateChannel.Beta, UpdateChannel.Stable] : [UpdateChannel.Stable];
            foreach (var candidate in channels)
            {
                var found = await Manager(candidate).CheckForUpdatesAsync();
                if (found is not null && (info is null || found.TargetFullRelease.Version > info.TargetFullRelease.Version))
                {
                    (manager, info) = (Manager(candidate), found);
                }
            }

            _activeManager = manager;
            _pending = info;
            logger.LogInformation("Update check on the {Channel} channel finished; update available: {Available}", channel, info is not null);
            return info is null
                ? null
                : new AvailableUpdate(info.TargetFullRelease.Version.ToString(), info.TargetFullRelease.NotesMarkdown);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Update check failed");
            throw new LibraryException("Prompuff couldn't check for updates. Check your connection and try again.", exception);
        }
    }

    public async Task DownloadUpdatesAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var (manager, info) = RequirePending(update);
        try
        {
            await manager.DownloadUpdatesAsync(info, percent => progress?.Report(percent), cancelToken: cancellationToken);
            logger.LogInformation("Downloaded update {Version}", update.Version);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Update download failed");
            throw new LibraryException("Prompuff couldn't download the update. Try again later.", exception);
        }
    }

    public void ApplyUpdatesAndRestart(AvailableUpdate update)
    {
        var (manager, info) = RequirePending(update);
        logger.LogInformation("Restarting to apply update {Version}", update.Version);
        manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
    }

    /// <summary>
    /// The Velopack channel a release workflow packs for this platform. The channel is always set explicitly, so an
    /// install that came from a beta follows the Stable setting once it's chosen, and the other way round.
    /// </summary>
    internal static string ChannelName(UpdateChannel channel, OSPlatform os, Architecture architecture)
    {
        var arm = architecture == Architecture.Arm64;
        var stable = os == OSPlatform.Windows ? "win"
            : os == OSPlatform.OSX ? (arm ? "osx-arm64" : "osx-x64")
            : arm ? "linux-arm64" : "linux";
        return channel == UpdateChannel.Beta ? stable + "-beta" : stable;
    }

    private (UpdateManager Manager, UpdateInfo Info) RequirePending(AvailableUpdate update)
    {
        if (_activeManager is null || _pending is null || _pending.TargetFullRelease.Version.ToString() != update.Version)
        {
            throw new LibraryException("That update isn't available anymore. Check for updates again.");
        }

        return (_activeManager, _pending);
    }

    private UpdateManager Manager(UpdateChannel channel)
    {
        lock (_managers)
        {
            if (!_managers.TryGetValue(channel, out var manager))
            {
                var os = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
                var options = new UpdateOptions { ExplicitChannel = ChannelName(channel, os, RuntimeInformation.ProcessArchitecture) };
                manager = new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: channel == UpdateChannel.Beta), options);
                _managers[channel] = manager;
            }

            return manager;
        }
    }
}
