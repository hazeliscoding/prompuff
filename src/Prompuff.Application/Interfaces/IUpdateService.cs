using Prompuff.Application.Settings;

namespace Prompuff.Application.Interfaces;

public sealed record AvailableUpdate(string Version, string? ReleaseNotesMarkdown);

/// <summary>Wraps the installer's updater so nothing else depends on it.</summary>
public interface IUpdateService
{
    /// <summary>False when running from a build that wasn't installed (for example <c>dotnet run</c>).</summary>
    bool IsSupported { get; }

    string CurrentVersion { get; }

    Task<AvailableUpdate?> CheckForUpdatesAsync(UpdateChannel channel, CancellationToken cancellationToken = default);
    Task DownloadUpdatesAsync(AvailableUpdate update, IProgress<int>? progress = null, CancellationToken cancellationToken = default);
    void ApplyUpdatesAndRestart(AvailableUpdate update);
}
