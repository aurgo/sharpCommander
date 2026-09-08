namespace SharpCommander.Core.Models;

/// <summary>
/// A release found on the update server, compared against the running build.
/// </summary>
public sealed record UpdateInfo
{
    /// <summary>The version of the published release, parsed from its tag.</summary>
    public required Version Version { get; init; }

    /// <summary>The tag as published, for display ("v2.2.0").</summary>
    public required string Tag { get; init; }

    /// <summary>Where a person can read about it and download it.</summary>
    public required string Url { get; init; }

    /// <summary>The release notes, which may be long or empty.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary>True when this release is newer than the build asking.</summary>
    public required bool IsNewer { get; init; }
}
