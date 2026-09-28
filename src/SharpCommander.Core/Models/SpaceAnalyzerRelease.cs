namespace SharpCommander.Core.Models;

/// <summary>
/// A published SpaceAnalyzer release, reduced to the one download that runs on this system.
/// </summary>
public sealed record SpaceAnalyzerRelease
{
    /// <summary>The version of the release, parsed from its tag.</summary>
    public required Version Version { get; init; }

    /// <summary>The tag as published ("v1.0.0").</summary>
    public required string Tag { get; init; }

    /// <summary>The file for this system ("SpaceAnalyzer-windows-x64.exe").</summary>
    public required string AssetName { get; init; }

    /// <summary>Where that file is downloaded from.</summary>
    public required string DownloadUrl { get; init; }

    /// <summary>The published size in bytes; zero when the server did not say.</summary>
    public long Size { get; init; }

    /// <summary>The published SHA-256 of the file as lowercase hex, or null when the server published none.</summary>
    public string? Sha256 { get; init; }
}
