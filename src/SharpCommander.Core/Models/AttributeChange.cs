namespace SharpCommander.Core.Models;

/// <summary>
/// What to change on a set of entries. Each flag is three-state: true sets it, false clears it, null leaves it
/// as it is — which is what a multi-item selection needs, where the items may not agree to begin with.
/// </summary>
public sealed record AttributeChange
{
    public bool? ReadOnly { get; init; }

    public bool? Hidden { get; init; }

    public bool? Archive { get; init; }

    public bool? System { get; init; }

    /// <summary>The Unix mode to apply, or null to leave permissions alone. Ignored on Windows.</summary>
    public UnixFileMode? UnixMode { get; init; }

    /// <summary>Apply to the contents of the folders too.</summary>
    public bool Recursive { get; init; }

    /// <summary>True when nothing would actually change.</summary>
    public bool IsEmpty =>
        ReadOnly is null && Hidden is null && Archive is null && System is null && UnixMode is null;
}
