using SharpCommander.Core.Interfaces;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Cross-platform file system watcher implementation.
/// </summary>
public sealed class FileSystemWatcherService : IFileSystemWatcher
{
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public event EventHandler<FileSystemChangedEventArgs>? Changed;

    public bool IsWatching { get; private set; }

    public void Start(string path)
    {
        Stop();

        if (!Directory.Exists(path))
        {
            return;
        }

        _watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.DirectoryName 
                         | NotifyFilters.FileName 
                         | NotifyFilters.LastWrite 
                         | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnCreated;
        _watcher.Deleted += OnDeleted;
        _watcher.Changed += OnChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnError;

        IsWatching = true;
    }

    public void Stop()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreated;
        _watcher.Deleted -= OnDeleted;
        _watcher.Changed -= OnChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _watcher = null;
        IsWatching = false;
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        RaiseChanged(e.FullPath, FileSystemChangeType.Created);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        RaiseChanged(e.FullPath, FileSystemChangeType.Deleted);
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        // Skip directory change events as they can be noisy
        if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath))
        {
            return;
        }
        
        RaiseChanged(e.FullPath, FileSystemChangeType.Modified);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        Changed?.Invoke(this, new FileSystemChangedEventArgs
        {
            Path = e.FullPath,
            ChangeType = FileSystemChangeType.Renamed,
            OldPath = e.OldFullPath
        });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        // Log error or restart watcher if needed
        Stop();
    }

    private void RaiseChanged(string path, FileSystemChangeType changeType)
    {
        Changed?.Invoke(this, new FileSystemChangedEventArgs
        {
            Path = path,
            ChangeType = changeType
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _disposed = true;
    }
}
