using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// SpaceAnalyzer, the disk space visualizer published at github.com/aurgo/SpaceAnalyzer, run as a companion
/// program. It is not shipped with SharpCommander: the first use downloads the build for this system from the
/// latest release, and that copy is kept, so every later use starts at once and needs no network.
/// </summary>
public interface ISpaceAnalyzerService
{
    /// <summary>True when a build is published for this operating system and processor.</summary>
    bool IsSupported { get; }

    /// <summary>The newest copy kept so far, or null when nothing has been downloaded yet.</summary>
    SpaceAnalyzerInstall? Installed { get; }

    /// <summary>
    /// Asks the server for the latest release and its download for this system. Returns null when the server
    /// could not be reached or has nothing for this system.
    /// </summary>
    Task<SpaceAnalyzerRelease?> FindLatestAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads <paramref name="release"/>, checks it against its published size and SHA-256, unpacks it and keeps
    /// it next to the copies already there. <paramref name="progress"/> receives the fraction downloaded, 0 to 1.
    /// Throws when the download fails or does not match; nothing half-written is ever left where it would be run.
    /// </summary>
    Task<SpaceAnalyzerInstall> InstallAsync(SpaceAnalyzerRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Starts a kept copy on <paramref name="folder"/>, or on its start screen when that is null.</summary>
    void Launch(SpaceAnalyzerInstall install, string? folder);
}
