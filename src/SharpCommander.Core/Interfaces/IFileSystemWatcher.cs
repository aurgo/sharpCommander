namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Interface for file system watching.
/// </summary>
public interface IFileSystemWatcher : IDisposable
{
    /// <summary>
    /// Event raised when file system changes.
    /// </summary>
    event EventHandler<FileSystemChangedEventArgs>? Changed;
    
    /// <summary>
    /// Starts watching the specified path.
    /// </summary>
    void Start(string path);
    
    /// <summary>
    /// Stops watching.
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Gets whether the watcher is currently active.
    /// </summary>
    bool IsWatching { get; }
}

/// <summary>
/// Event arguments for file system changes.
/// </summary>
public sealed class FileSystemChangedEventArgs : EventArgs
{
    public required string Path { get; init; }
    public required FileSystemChangeType ChangeType { get; init; }
    public string? OldPath { get; init; }
}

/// <summary>
/// Type of file system change.
/// </summary>
public enum FileSystemChangeType
{
    Created,
    Deleted,
    Modified,
    Renamed
}
