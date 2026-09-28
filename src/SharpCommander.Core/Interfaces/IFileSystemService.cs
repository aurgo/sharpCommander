using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// File system operations. Copy, move and delete run on a background thread, report progress with real byte
/// counts, honor cancellation and never delete anything before its replacement is complete.
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Lists a directory: the ".." entry first (unless at a root), then directories, then files, each group in
    /// natural case-insensitive order. A null or empty path lists the drives. Throws
    /// <see cref="UnauthorizedAccessException"/> or <see cref="DirectoryNotFoundException"/> when the directory
    /// itself cannot be read; entries whose metadata cannot be read are skipped.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the real volumes only (no pseudo file systems or system mount points), with free and total space.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a file or directory into <paramref name="destinationDirectory"/>. Throws
    /// <see cref="FileOperationException"/> (SameLocation, DestinationInsideSource) before touching anything when
    /// the destination is the source itself or lies inside it. Existing files are handled by
    /// <paramref name="onConflict"/> (skipped when null); directories are merged. Cancel removes the partial file.
    /// </summary>
    Task CopyAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a file or directory into <paramref name="destinationDirectory"/> with the same checks and conflict
    /// policy as <see cref="CopyAsync"/>. Same-volume moves rename in place; cross-volume moves copy each file and
    /// delete its source only after the copy completed; a merged source directory is removed only once empty.
    /// </summary>
    Task MoveAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a file or directory, reporting per-file progress for directories.
    /// </summary>
    Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a directory (and missing parents). Throws <see cref="FileOperationException"/> (InvalidName)
    /// when the last segment is not a valid name.
    /// </summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a file with the default application, a directory in the file manager, or an http(s) address in the
    /// browser. A file for which <see cref="IsExecutableOrScript"/> is true is run (callers confirm first); any other
    /// file is handed to the default application and is never executed, whatever its permission bits. Throws on
    /// failure.
    /// </summary>
    Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when opening the file would run code: executables and scripts by extension and, on Unix,
    /// files with an execute bit that have no extension or start with a script or binary signature ("#!", ELF,
    /// Mach-O). Callers should confirm before opening such files.
    /// </summary>
    bool IsExecutableOrScript(string path);

    /// <summary>
    /// Checks if a path exists.
    /// </summary>
    bool Exists(string path);

    /// <summary>
    /// Checks if a path is a directory.
    /// </summary>
    bool IsDirectory(string path);

    /// <summary>
    /// Gets the parent directory path.
    /// </summary>
    string? GetParentPath(string path);

    /// <summary>
    /// Gets the default starting directory.
    /// </summary>
    string GetDefaultDirectory();

    /// <summary>
    /// Renames a file or directory in place. Throws <see cref="FileOperationException"/> (InvalidName) for names
    /// with separators or invalid characters, (TargetExists) when a different entry already has that name and
    /// (NotFound) when the source is gone. Changing only the letter case of the same entry is allowed on every
    /// file system, case-sensitive or not.
    /// </summary>
    Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a directory in the system file manager (Explorer, Finder, or the xdg-open default on Linux) and
    /// reveals a file in its folder. On macOS a package directory (an application bundle, an iWork document, a
    /// photo library) is revealed rather than launched.
    /// </summary>
    Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reveals an entry (file or directory) selected inside its parent folder in the system file manager
    /// (Explorer's /select, Finder's reveal; the parent folder on Linux, where xdg-open cannot select).
    /// </summary>
    Task RevealInFileExplorerAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sums the size of every file below <paramref name="path"/>, skipping unreadable entries and not following
    /// symbolic links. Reports the running total through <paramref name="progress"/>.
    /// </summary>
    Task<long> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="change"/> to <paramref name="paths"/>. Entries that cannot be changed are counted
    /// rather than aborting the batch, so one protected file does not stop the rest.
    /// </summary>
    /// <returns>How many entries were changed.</returns>
    Task<int> ApplyAttributesAsync(IReadOnlyList<string> paths, AttributeChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares two files byte for byte. Returns the offset of the first byte that differs, -1 when the files are
    /// identical, or the length of the shorter file when one is a prefix of the other.
    /// </summary>
    Task<long> FindFirstDifferenceAsync(string leftPath, string rightPath, CancellationToken cancellationToken = default);

    /// <summary>Opens the platform terminal with <paramref name="directory"/> as its working folder.</summary>
    Task OpenTerminalAsync(string directory, CancellationToken cancellationToken = default);
}
