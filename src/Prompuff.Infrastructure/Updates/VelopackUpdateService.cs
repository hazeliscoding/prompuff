using System.Reflection;
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
public sealed class VelopackUpdateService(ILogger<VelopackUpdateService> logger) : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/hazeliscoding/prompuff";

    private readonly Lazy<UpdateManager> _stableManager = new(() => CreateManager(UpdateChannel.Stable));
    private UpdateManager? _activeManager;
    private UpdateInfo? _pending;

    public bool IsSupported
    {
        get
        {
            try
            {
                return _stableManager.Value.IsInstalled;
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
            if (IsSupported && _stableManager.Value.CurrentVersion is { } version)
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
            var manager = channel == UpdateChannel.Stable ? _stableManager.Value : CreateManager(channel);
            var info = await manager.CheckForUpdatesAsync();
            _activeManager = manager;
            _pending = info;
            logger.LogInformation("Update check finished; update available: {Available}", info is not null);
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

    private (UpdateManager Manager, UpdateInfo Info) RequirePending(AvailableUpdate update)
    {
        if (_activeManager is null || _pending is null || _pending.TargetFullRelease.Version.ToString() != update.Version)
        {
            throw new LibraryException("That update isn't available anymore. Check for updates again.");
        }

        return (_activeManager, _pending);
    }

    private static UpdateManager CreateManager(UpdateChannel channel)
    {
        var prerelease = channel == UpdateChannel.Beta;
        var options = new UpdateOptions
        {
            // Stable uses Velopack's default channel for the platform ("win", "linux"); Beta appends "-beta".
            ExplicitChannel = prerelease ? DefaultChannel() + "-beta" : null,
        };
        return new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: prerelease), options);
    }

    private static string DefaultChannel() =>
        OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
}
