namespace SharpCommander.Core.Models;

/// <summary>
/// What to do when a copy or move finds an entry with the same name at the destination.
/// </summary>
public enum ConflictAction
{
    Overwrite,
    Skip,
    Rename,
    Cancel
}

/// <summary>
/// Describes a name collision found while copying or moving.
/// </summary>
/// <param name="SourcePath">Full path of the entry being copied or moved.</param>
/// <param name="DestinationPath">Full path that already exists at the destination.</param>
/// <param name="IsDirectory">
/// True when the source entry is a directory. Directories are merged into existing directories without
/// raising a conflict, so a directory conflict means the destination is a file with the same name
/// (and a file conflict whose destination is a directory is the opposite mismatch); neither can be overwritten.
/// </param>
/// <param name="SourceSize">Size of the source in bytes (0 for directories).</param>
/// <param name="DestinationSize">Size of the existing destination in bytes (0 for directories).</param>
/// <param name="SourceModified">Last write time of the source.</param>
/// <param name="DestinationModified">Last write time of the existing destination.</param>
public sealed record FileConflict(
    string SourcePath,
    string DestinationPath,
    bool IsDirectory,
    long SourceSize,
    long DestinationSize,
    DateTime SourceModified,
    DateTime DestinationModified);

/// <summary>
/// The answer to a <see cref="FileConflict"/>.
/// </summary>
/// <param name="Action">The chosen action.</param>
/// <param name="ApplyToAll">When true the same action is applied to every further conflict of the operation without asking again.</param>
/// <param name="NewName">
/// For <see cref="ConflictAction.Rename"/>: the new file name to use (a name, not a path). When null, or when the
/// resolution is applied to further conflicts, a unique "name (2).ext" style name is generated.
/// </param>
public sealed record ConflictResolution(ConflictAction Action, bool ApplyToAll = false, string? NewName = null);

/// <summary>
/// Asked once per conflict while copying or moving. May be invoked from a background thread; UI implementations
/// must marshal to the UI thread themselves.
/// </summary>
public delegate Task<ConflictResolution> ConflictResolver(FileConflict conflict, CancellationToken cancellationToken);
