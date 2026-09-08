namespace SharpCommander.Core.Models;

/// <summary>
/// Represents a file system entry (file, directory, or drive).
/// </summary>
public sealed record FileSystemEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required FileSystemEntryType EntryType { get; init; }

    /// <summary>Size in bytes for files; total capacity for drives; 0 for directories.</summary>
    public long Size { get; init; }

    /// <summary>
    /// The measured size of a folder, once the user has asked for it. Null while unknown, which is what the
    /// size column shows as "&lt;DIR&gt;". Folders are never measured while listing: it would mean walking the
    /// whole tree of every row.
    /// </summary>
    public long? CalculatedSize { get; init; }
    public DateTime LastModified { get; init; }
    public DateTime Created { get; init; }
    public DateTime LastAccessed { get; init; }
    public string Extension { get; init; } = string.Empty;

    /// <summary>Raw attributes as reported by the file system.</summary>
    public FileAttributes Attributes { get; init; }

    /// <summary>True for entries carrying the Hidden attribute and, on Unix, for dotfiles.</summary>
    public bool IsHidden { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsSymbolicLink { get; init; }

    /// <summary>Permissions in "rwxr-xr-x" form on Unix; null on Windows.</summary>
    public string? UnixPermissions { get; init; }

    public bool IsReady { get; init; } = true;
    public string VolumeLabel { get; init; } = string.Empty;
    public string DriveFormat { get; init; } = string.Empty;

    /// <summary>Available free space in bytes; only set for drives.</summary>
    public long? FreeSpace { get; init; }

    /// <summary>Total capacity in bytes; only set for drives (same value as <see cref="Size"/>).</summary>
    public long? TotalSize { get; init; }
}

/// <summary>
/// Type of file system entry.
/// </summary>
public enum FileSystemEntryType
{
    File,
    Directory,
    Drive,
    ParentDirectory
}
