using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>A SpaceAnalyzer that is "downloaded" and "started" in memory: tests set what the server answers.</summary>
public sealed class FakeSpaceAnalyzerService : ISpaceAnalyzerService
{
    public bool IsSupported { get; set; } = true;

    public string Folder { get; set; } = Path.Combine(Path.GetTempPath(), "SharpCommander-no-such-folder", "SpaceAnalyzer");

    public SpaceAnalyzerInstall? Installed { get; set; }

    /// <summary>What FindLatestAsync returns; null stands for "GitHub could not be reached".</summary>
    public SpaceAnalyzerRelease? Latest { get; set; }

    /// <summary>When set, InstallAsync fails with it, as a corrupt or interrupted download would.</summary>
    public Exception? InstallFailure { get; set; }

    public int Lookups { get; private set; }

    /// <summary>The releases InstallAsync was asked for, in order.</summary>
    public List<SpaceAnalyzerRelease> Installs { get; } = [];

    /// <summary>Every start, with the folder it was given (null for the start screen).</summary>
    public List<(SpaceAnalyzerInstall Install, string? Folder)> Launches { get; } = [];

    public static SpaceAnalyzerRelease Release(string version) => new()
    {
        Version = Version.Parse(version),
        Tag = "v" + version,
        AssetName = "SpaceAnalyzer-test",
        DownloadUrl = "https://example.invalid/SpaceAnalyzer-test",
        Size = 1024
    };

    public static SpaceAnalyzerInstall Install(string version) =>
        new(Version.Parse(version), Path.Combine(Path.GetTempPath(), "SpaceAnalyzer", version, "SpaceAnalyzer"));

    public Task<SpaceAnalyzerRelease?> FindLatestAsync(CancellationToken cancellationToken = default)
    {
        Lookups++;
        return Task.FromResult(Latest);
    }

    public Task<SpaceAnalyzerInstall> InstallAsync(SpaceAnalyzerRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        Installs.Add(release);

        if (InstallFailure is { } failure)
        {
            return Task.FromException<SpaceAnalyzerInstall>(failure);
        }

        progress?.Report(0.5);
        progress?.Report(1);

        Installed = Install(release.Version.ToString());
        return Task.FromResult(Installed);
    }

    public Task LaunchAsync(SpaceAnalyzerInstall install, string? folder)
    {
        Launches.Add((install, folder));
        return Task.CompletedTask;
    }
}
