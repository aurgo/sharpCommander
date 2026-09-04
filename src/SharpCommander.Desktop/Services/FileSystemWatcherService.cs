using SharpCommander.Core.Interfaces;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Watches one directory for changes. When the underlying <see cref="FileSystemWatcher"/> fails (typically a
/// buffer overflow while many files change at once) the watcher is recreated after a delay that doubles from one
/// second up to thirty, and a synthetic change is raised at once so the listing refreshes. Events arrive on
/// background threads.
/// </summary>
public sealed class FileSystemWatcherService : IFileSystemWatcher
{
    private const int InternalBufferSize = 64 * 1024;
    private static readonly TimeSpan InitialRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private string? _path;
    private CancellationTokenSource? _restart;
    private TimeSpan _restartDelay = InitialRestartDelay;
    private bool _disposed;

    public event EventHandler<FileSystemChangedEventArgs>? Changed;

    /// <summary>Raised from a background thread after the watcher was recreated following an error.</summary>
    public event EventHandler? Restarted;

    public bool IsWatching { get; private set; }

    /// <summary>Gets the delay the next automatic restart will wait for.</summary>
    internal TimeSpan RestartDelay
    {
        get
        {
            lock (_gate)
            {
                return _restartDelay;
            }
        }
    }

    public void Start(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CancelRestart();
            StopCore();
            _restartDelay = InitialRestartDelay;
            _path = path;
            StartCore(path);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            CancelRestart();
            _path = null;
            StopCore();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelRestart();
            _path = null;
            StopCore();
        }
    }

    /// <summary>
    /// Feeds an error into the running watcher as if the operating system had reported it, so the restart
    /// logic can be exercised without provoking a real buffer overflow (tests). Ignored when nothing is watched.
    /// </summary>
    internal void SimulateError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        FileSystemWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
        }

        if (watcher is not null)
        {
            OnError(watcher, new ErrorEventArgs(exception));
        }
    }

    /// <summary>Creates the watcher; must be called under the lock.</summary>
    private void StartCore(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Size,
                Filter = "*",
                IncludeSubdirectories = false,
                InternalBufferSize = InternalBufferSize
            };

            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Changed += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            _watcher = watcher;
            IsWatching = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            AppLog.Warning($"The folder '{path}' cannot be watched for changes.", ex);
            watcher?.Dispose();
            _watcher = null;
            IsWatching = false;
        }
    }

    /// <summary>Disposes the watcher; must be called under the lock.</summary>
    private void StopCore()
    {
        var watcher = _watcher;
        _watcher = null;
        IsWatching = false;

        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The watcher is gone already; disposing it below is all that matters.
        }

        watcher.Created -= OnCreated;
        watcher.Deleted -= OnDeleted;
        watcher.Changed -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }

    /// <summary>Cancels a scheduled restart; must be called under the lock.</summary>
    private void CancelRestart()
    {
        _restart?.Cancel();
        _restart?.Dispose();
        _restart = null;
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
        string path;
        TimeSpan delay;
        CancellationToken token;

        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(sender, _watcher) || _path is null)
            {
                return;
            }

            path = _path;
            StopCore();

            delay = _restartDelay;
            _restartDelay = TimeSpan.FromTicks(Math.Min(_restartDelay.Ticks * 2, MaxRestartDelay.Ticks));

            CancelRestart();
            _restart = new CancellationTokenSource();
            token = _restart.Token;
        }

        AppLog.Warning($"Watching '{path}' failed; the watcher restarts in {delay.TotalSeconds:0} s.", e.GetException());

        // Changes were probably lost (buffer overflow): let the listing refresh now.
        RaiseChanged(path, FileSystemChangeType.Modified);
        _ = RestartLaterAsync(path, delay, token);
    }

    private async Task RestartLaterAsync(string path, TimeSpan delay, CancellationToken token)
    {
        while (true)
        {
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (token.IsCancellationRequested || _disposed || !string.Equals(_path, path, StringComparison.Ordinal))
                {
                    return;
                }

                StartCore(path);
                if (IsWatching)
                {
                    _restart?.Dispose();
                    _restart = null;
                }
                else
                {
                    delay = _restartDelay;
                    _restartDelay = TimeSpan.FromTicks(Math.Min(_restartDelay.Ticks * 2, MaxRestartDelay.Ticks));
                }
            }

            if (!IsWatching)
            {
                continue;
            }

            Restarted?.Invoke(this, EventArgs.Empty);

            // Anything that changed while the watcher was down is picked up by this refresh.
            RaiseChanged(path, FileSystemChangeType.Modified);
            return;
        }
    }

    private void RaiseChanged(string path, FileSystemChangeType changeType)
    {
        Changed?.Invoke(this, new FileSystemChangedEventArgs
        {
            Path = path,
            ChangeType = changeType
        });
    }
}
