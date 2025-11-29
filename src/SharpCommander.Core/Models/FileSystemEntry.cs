namespace SharpCommander.Core.Models;

/// <summary>
/// Represents a file system entry (file, directory, or drive).
/// </summary>
public sealed record FileSystemEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required FileSystemEntryType EntryType { get; init; }
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = string.Empty;
    public bool IsReady { get; init; } = true;
    public string VolumeLabel { get; init; } = string.Empty;
    public string DriveFormat { get; init; } = string.Empty;
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
