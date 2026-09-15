using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Runs every user-triggered file operation with one policy: sources are checked up front (a source that would
/// land on itself or inside itself is reported and skipped, nothing is touched), name collisions go through the
/// conflict dialog whose "apply to all" answer is remembered for the whole batch, deletion is confirmed and uses
/// the trash by default, errors are collected per item and shown once at the end, and the batch can be cancelled.
/// Only one batch runs at a time; the state properties feed the status bar and change on the UI thread.
/// </summary>
public sealed partial class FileOperationsService : ObservableObject, IFileOperationsService
{
    private readonly IFileSystemService _fileSystem;
    private readonly IDialogService _dialogs;
    private readonly ITrashService _trash;
    private int _running;
    private int _generation;
    private CancellationTokenSource? _batchCancellation;
    private TaskCompletionSource? _idleSignal;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _operationName = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private long _processedBytes;

    [ObservableProperty]
    private long _totalBytes;

    [ObservableProperty]
    private int _processedItems;

    [ObservableProperty]
    private int _totalItems;

    public event EventHandler<FileOperationResult>? OperationCompleted;

    public FileOperationsService(IFileSystemService fileSystemService, IDialogService dialogService, ITrashService trashService)
    {
        ArgumentNullException.ThrowIfNull(fileSystemService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(trashService);
        _fileSystem = fileSystemService;
        _dialogs = dialogService;
        _trash = trashService;
    }

    ICommand IFileOperationsService.CancelCommand => CancelCommand;

    /// <summary>Cancels the running batch, if any. Safe to call at any time and from any thread.</summary>
    [RelayCommand(CanExecute = nameof(IsRunning))]
    public void Cancel()
    {
        var cancellation = _batchCancellation;
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The batch finished in the meantime.
        }
    }

    /// <summary>
    /// Completes once the running batch has finished, including the engine's cleanup of partial files after a
    /// cancellation. Already completed when nothing is running, and never faults.
    /// </summary>
    public Task WhenIdleAsync() => Volatile.Read(ref _idleSignal)?.Task ?? Task.CompletedTask;

    // ---- copy and move ----------------------------------------------------------------------------------

    public Task<FileOperationResult> CopyAsync(IReadOnlyList<string> sources, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        return TransferAsync(FileOperationKind.Copy, sources, destinationDirectory, cancellationToken);
    }

    public Task<FileOperationResult> MoveAsync(IReadOnlyList<string> sources, string destinationDirectory, CancellationToken cancellationToken = default)
    {
        return TransferAsync(FileOperationKind.Move, sources, destinationDirectory, cancellationToken);
    }

    private async Task<FileOperationResult> TransferAsync(FileOperationKind kind, IReadOnlyList<string> sources, string destinationDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count == 0)
        {
            return new FileOperationResult { Kind = kind };
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory) || !_fileSystem.IsDirectory(destinationDirectory))
        {
            await _dialogs.ShowErrorAsync(Title(kind), "Choose an existing destination folder first.");
            return FileOperationResult.NotStarted(kind, rejected: true);
        }

        return await RunBatchAsync(kind, sources.Count, cancellationToken, batch => TransferItemsAsync(batch, kind, sources, destinationDirectory));
    }

    private async Task TransferItemsAsync(Batch batch, FileOperationKind kind, IReadOnlyList<string> sources, string destinationDirectory)
    {
        // Planning stats every source three times over. That is microseconds locally but milliseconds per
        // round trip on an SMB, NFS or AFP mount, and it runs before the first byte moves: on the UI thread a
        // few thousand selected files froze the window, with the progress panel not yet shown and Cancel not
        // yet clickable. The results are applied back on the caller's thread, where the batch state lives.
        var planned = await Task.Run(
            () =>
            {
                var items = new List<WorkItem>(sources.Count);
                foreach (var source in sources)
                {
                    batch.Token.ThrowIfCancellationRequested();
                    items.Add(PlanTransfer(source, destinationDirectory, kind));
                }

                return items;
            },
            batch.Token);

        var plan = new List<WorkItem>(sources.Count);
        foreach (var item in planned)
        {
            if (item.Error is not null)
            {
                batch.FailUpFront(item.Path, item.Error);
            }
            else
            {
                plan.Add(item);
            }
        }

        await batch.MeasureAsync(plan);
        var conflicts = new BatchConflictResolver(_dialogs, plan);

        for (var index = 0; index < plan.Count; index++)
        {
            batch.Token.ThrowIfCancellationRequested();
            var item = plan[index];
            conflicts.BeginItem(index);
            var progress = batch.BeginItem(item);

            try
            {
                var operation = kind == FileOperationKind.Copy
                    ? _fileSystem.CopyAsync(item.Path, destinationDirectory, conflicts.ResolveAsync, progress, batch.Token)
                    : _fileSystem.MoveAsync(item.Path, destinationDirectory, conflicts.ResolveAsync, progress, batch.Token);

                await operation;
                batch.Complete(item, conflicts.SkipsInItem);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FileOperationException ex)
            {
                batch.Fail(ex.Path, ex.Message);
            }
            catch (Exception ex)
            {
                batch.Fail(item.Path, ex.Message);
            }
            finally
            {
                batch.EndItem();
            }
        }
    }

    /// <summary>
    /// Validates one source before anything is touched: it must exist, must not already be at the destination,
    /// and a directory must not be copied or moved into itself or one of its subfolders.
    ///
    /// A source or a destination on a server goes through exactly these checks, which is why the questions are
    /// put to the file system service and the arithmetic to <see cref="AnyPath"/>: asking the local disk about a
    /// remote address answered "no longer exists" for every file on it, so nothing could be downloaded.
    /// </summary>
    private WorkItem PlanTransfer(string source, string destinationDirectory, FileOperationKind kind)
    {
        string sourcePath;
        string destinationPath;
        try
        {
            sourcePath = AnyPath.Normalize(source);
            destinationPath = AnyPath.Normalize(destinationDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return WorkItem.Invalid(source, ex.Message);
        }

        var isDirectory = _fileSystem.IsDirectory(sourcePath);
        var name = AnyPath.GetName(sourcePath);
        var verb = kind == FileOperationKind.Copy ? "copy" : "move";

        if (!isDirectory && !_fileSystem.Exists(sourcePath))
        {
            return WorkItem.Invalid(sourcePath, $"'{name}' no longer exists.");
        }

        // A volume root has no name of its own, so Path.Combine(destination, "") would be the destination folder
        // itself and the whole volume would be merged into it without so much as a prompt. Drives reach this
        // through F5/F6 and drag and drop from the Computer view, which (unlike delete and the clipboard) do not
        // filter them out.
        if (AnyPath.IsRoot(sourcePath))
        {
            return WorkItem.Invalid(sourcePath, $"Cannot {verb} a whole drive. Open it and select what to {verb}.");
        }

        var target = AnyPath.Combine(destinationPath, name);
        if (AnyPath.AreSame(sourcePath, target))
        {
            return WorkItem.Invalid(sourcePath, $"Cannot {verb} '{name}': the source and the destination are the same.");
        }

        if (isDirectory && AnyPath.IsSameOrDescendant(sourcePath, destinationPath))
        {
            return WorkItem.Invalid(sourcePath, $"Cannot {verb} '{name}' into itself.");
        }

        return new WorkItem
        {
            Path = sourcePath,
            IsDirectory = isDirectory,
            MayConflict = _fileSystem.Exists(target)
        };
    }

    // ---- delete -----------------------------------------------------------------------------------------

    public async Task<FileOperationResult> DeleteAsync(IReadOnlyList<FileSystemEntry> items, bool permanentRequested, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var targets = items
            .Where(item => item.EntryType is FileSystemEntryType.File or FileSystemEntryType.Directory)
            .ToList();

        if (targets.Count == 0)
        {
            return new FileOperationResult { Kind = FileOperationKind.Delete };
        }

        if (Volatile.Read(ref _running) != 0)
        {
            await ShowAlreadyRunningAsync();
            return FileOperationResult.NotStarted(FileOperationKind.Delete, rejected: true);
        }

        var trashAvailable = _trash.IsSupported;
        var choice = await _dialogs.ShowDeleteConfirmAsync(targets, trashAvailable, permanentRequested);

        return choice switch
        {
            DeleteChoice.Trash => await RunBatchAsync(FileOperationKind.Trash, targets.Count, cancellationToken, batch => TrashItemsAsync(batch, targets)),
            DeleteChoice.Permanent => await RunBatchAsync(FileOperationKind.Delete, targets.Count, cancellationToken, batch => DeleteItemsAsync(batch, targets)),
            _ => FileOperationResult.NotStarted(trashAvailable && !permanentRequested ? FileOperationKind.Trash : FileOperationKind.Delete, rejected: false)
        };
    }

    private async Task DeleteItemsAsync(Batch batch, List<FileSystemEntry> targets)
    {
        var plan = targets.Select(WorkItem.ForEntry).ToList();
        await batch.MeasureAsync(plan);

        foreach (var item in plan)
        {
            batch.Token.ThrowIfCancellationRequested();
            var progress = batch.BeginItem(item);

            try
            {
                await _fileSystem.DeleteAsync(item.Path, progress, batch.Token);
                batch.Complete(item, skippedFiles: 0);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (FileOperationException ex)
            {
                batch.Fail(ex.Path, ex.Message);
            }
            catch (Exception ex)
            {
                batch.Fail(item.Path, ex.Message);
            }
            finally
            {
                batch.EndItem();
            }
        }
    }

    private async Task TrashItemsAsync(Batch batch, List<FileSystemEntry> targets)
    {
        foreach (var entry in targets)
        {
            batch.Token.ThrowIfCancellationRequested();
            var item = WorkItem.ForEntry(entry);
            var progress = batch.BeginItem(item);

            try
            {
                await _trash.MoveToTrashAsync(item.Path, batch.Token);
                batch.Complete(item, skippedFiles: 0);
            }
            catch (OperationCanceledException) when (batch.Token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // The operating system aborted this item (for example a recycle bin prompt was declined).
                batch.Skip();
            }
            catch (FileOperationException ex) when (ex.Kind == FileOperationErrorKind.NotFound)
            {
                batch.Fail(ex.Path, ex.Message);
            }
            catch (Exception ex)
            {
                await DeletePermanentlyAfterTrashFailureAsync(batch, item, progress, ex);
            }
            finally
            {
                batch.EndItem();
            }
        }
    }

    private async Task DeletePermanentlyAfterTrashFailureAsync(Batch batch, WorkItem item, IProgress<FileOperationProgress> progress, Exception trashError)
    {
        var name = Path.GetFileName(item.Path);
        var confirmed = await _dialogs.ShowConfirmAsync(
            "Delete permanently?",
            $"'{name}' could not be moved to the trash:\n{trashError.Message}\n\nDelete it permanently instead? This cannot be undone.",
            "Delete permanently",
            "Skip",
            destructive: true,
            // This prompt follows the delete confirmation the user just answered with Enter, and on Linux
            // without gio it appears within milliseconds; Skip is armed so a repeating Enter cannot delete.
            defaultIsCancel: true);

        if (!confirmed)
        {
            batch.Skip();
            return;
        }

        try
        {
            await _fileSystem.DeleteAsync(item.Path, progress, batch.Token);
            batch.Complete(item, skippedFiles: 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FileOperationException ex)
        {
            batch.Fail(ex.Path, ex.Message);
        }
        catch (Exception ex)
        {
            batch.Fail(item.Path, ex.Message);
        }
    }

    // ---- rename, new folder, open ---------------------------------------------------------------------

    public async Task<string?> RenameAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.EntryType is FileSystemEntryType.ParentDirectory or FileSystemEntryType.Drive)
        {
            return null;
        }

        var directory = AnyPath.GetDirectory(entry.FullPath);
        if (string.IsNullOrEmpty(directory))
        {
            await _dialogs.ShowErrorAsync("Rename", "A root folder cannot be renamed.");
            return null;
        }

        string? Validate(string name)
        {
            var error = PathUtils.ValidateFileName(name);
            if (error is not null)
            {
                return error;
            }

            if (string.Equals(name, entry.Name, StringComparison.Ordinal))
            {
                return null;
            }

            var target = AnyPath.Combine(directory, name);
            return !AnyPath.AreSame(entry.FullPath, target) && _fileSystem.Exists(target)
                ? $"'{name}' already exists in this folder."
                : null;
        }

        var newName = await _dialogs.ShowInputDialogAsync("Rename", $"New name for '{entry.Name}':", entry.Name, Validate);
        if (newName is null || string.Equals(newName, entry.Name, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            await _fileSystem.RenameAsync(entry.FullPath, newName, cancellationToken);
            return AnyPath.Combine(directory, newName);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Renaming '{entry.FullPath}' to '{newName}' failed.", ex);
            await _dialogs.ShowErrorAsync("Rename", $"'{entry.Name}' could not be renamed: {ex.Message}", Details(ex));
            return null;
        }
    }

    public async Task<string?> CreateDirectoryAsync(string parentDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory) || !_fileSystem.IsDirectory(parentDirectory))
        {
            await _dialogs.ShowErrorAsync("New Folder", "Open a folder first: folders cannot be created in the Computer view.");
            return null;
        }

        string? Validate(string name)
        {
            var error = PathUtils.ValidateFileName(name);
            if (error is not null)
            {
                return error;
            }

            return _fileSystem.Exists(AnyPath.Combine(parentDirectory, name)) ? $"'{name}' already exists." : null;
        }

        var name = await _dialogs.ShowInputDialogAsync("New Folder", "Folder name:", SuggestFolderName(parentDirectory), Validate);
        if (name is null)
        {
            return null;
        }

        var path = AnyPath.Combine(parentDirectory, name);
        try
        {
            await _fileSystem.CreateDirectoryAsync(path, cancellationToken);
            return path;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Creating the folder '{path}' failed.", ex);
            await _dialogs.ShowErrorAsync("New Folder", $"The folder '{name}' could not be created: {ex.Message}", Details(ex));
            return null;
        }
    }

    public async Task<bool> OpenAsync(FileSystemEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.EntryType == FileSystemEntryType.ParentDirectory)
        {
            return false;
        }

        if (entry.EntryType == FileSystemEntryType.File && _fileSystem.IsExecutableOrScript(entry.FullPath))
        {
            var run = await _dialogs.ShowConfirmAsync(
                "Run program?",
                $"'{entry.Name}' is a program or a script. Running it may change your system.\n\nDo you want to run it?",
                "Run",
                "Cancel",
                destructive: true);

            if (!run)
            {
                return false;
            }
        }

        try
        {
            await _fileSystem.OpenWithDefaultAsync(entry.FullPath, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"Opening '{entry.FullPath}' failed.", ex);
            await _dialogs.ShowErrorAsync("Open", $"'{entry.Name}' could not be opened: {ex.Message}", Details(ex));
            return false;
        }
    }

    // ---- batch plumbing ---------------------------------------------------------------------------------

    /// <summary>
    /// Claims the single running slot, resets the state, runs <paramref name="body"/>, reports the errors it
    /// collected and raises <see cref="OperationCompleted"/>.
    /// </summary>
    private async Task<FileOperationResult> RunBatchAsync(FileOperationKind kind, int totalItems, CancellationToken cancellationToken, Func<Batch, Task> body)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            await ShowAlreadyRunningAsync();
            return FileOperationResult.NotStarted(kind, rejected: true);
        }

        var generation = Interlocked.Increment(ref _generation);
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _batchCancellation = cancellation;
        Volatile.Write(ref _idleSignal, idle);
        var batch = new Batch(this, kind, generation, cancellation.Token);
        var operationName = OperationNameFor(kind);

        ApplyNow(() =>
        {
            IsRunning = true;
            OperationName = operationName;
            CurrentFile = string.Empty;
            Percent = 0;
            ProcessedBytes = 0;
            TotalBytes = 0;
            ProcessedItems = 0;
            TotalItems = totalItems;
        });

        FileOperationResult result;
        try
        {
            await body(batch);
            result = batch.ToResult(cancelled: false);
        }
        catch (OperationCanceledException)
        {
            result = batch.ToResult(cancelled: true);
        }
        finally
        {
            _batchCancellation = null;
            Interlocked.Increment(ref _generation);
            cancellation.Dispose();
            ApplyNow(ResetState);
            Volatile.Write(ref _running, 0);
            Volatile.Write(ref _idleSignal, null);
            idle.TrySetResult();
        }

        if (result.HasErrors)
        {
            if (result.Cancelled)
            {
                foreach (var error in result.Errors)
                {
                    AppLog.Warning($"{operationName} (cancelled): {error.Path}: {error.Message}");
                }
            }
            else
            {
                await _dialogs.ShowOperationErrorsAsync($"{Title(kind)} finished with errors", result.Errors);
            }
        }

        OperationCompleted?.Invoke(this, result);
        return result;
    }

    private Task ShowAlreadyRunningAsync()
    {
        return _dialogs.ShowErrorAsync("Operation in progress", "An operation is already running. Wait for it to finish or cancel it first.");
    }

    private async Task<long> MeasureAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            // Both branches measure off the UI thread: a directory already does, and a file's length is a stat
            // that costs a network round trip on a remote share. An entry on a server goes the same way as a
            // folder whatever it is — only the server can answer for it, and FileInfo would be measuring a
            // local path that does not exist.
            return item.IsDirectory || AnyPath.IsRemote(item.Path)
                ? await _fileSystem.GetDirectorySizeAsync(item.Path, null, cancellationToken)
                : await Task.Run(() => new FileInfo(item.Path).Length, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Measuring only feeds the progress bar. Whatever the disk, the network or the server answers here,
            // the batch itself still has to run.
            AppLog.Warning($"The size of '{item.Path}' could not be measured.", ex);
            return 0;
        }
    }

    private void ResetState()
    {
        IsRunning = false;
        OperationName = string.Empty;
        CurrentFile = string.Empty;
        Percent = 0;
        ProcessedBytes = 0;
        TotalBytes = 0;
        ProcessedItems = 0;
        TotalItems = 0;
    }

    private double ComputePercent()
    {
        var ratio = TotalBytes > 0
            ? (double)ProcessedBytes / TotalBytes
            : TotalItems > 0
                ? (double)ProcessedItems / TotalItems
                : 0;

        return Math.Clamp(ratio * 100, 0, 100);
    }

    /// <summary>Applies a state change on the UI thread, now when already there, else posted.</summary>
    private static void ApplyNow(Action apply)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            apply();
        }
        else
        {
            Dispatcher.UIThread.Post(apply);
        }
    }

    /// <summary>Like <see cref="ApplyNow"/>, but dropped when the batch it belongs to has already finished.</summary>
    private void Update(int generation, Action apply)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (generation == Volatile.Read(ref _generation))
            {
                apply();
            }

            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _generation))
            {
                apply();
            }
        });
    }

    private static string OperationNameFor(FileOperationKind kind) => kind switch
    {
        FileOperationKind.Copy => "Copying",
        FileOperationKind.Move => "Moving",
        FileOperationKind.Delete => "Deleting",
        FileOperationKind.Trash => "Moving to the trash",
        _ => "Working"
    };

    private static string Title(FileOperationKind kind) => kind switch
    {
        FileOperationKind.Copy => "Copy",
        FileOperationKind.Move => "Move",
        _ => "Delete"
    };

    private static string SuggestFolderName(string parentDirectory)
    {
        if (AnyPath.IsRemote(parentDirectory))
        {
            // GetUniqueName counts the folders already on this disk, which says nothing about a server. The
            // name typed is checked against the server itself when the dialog validates it.
            return "New Folder";
        }

        try
        {
            return PathUtils.GetUniqueName(parentDirectory, "New Folder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return "New Folder";
        }
    }

    /// <summary>Expandable details for unexpected failures; classified I/O errors are self-explanatory.</summary>
    private static string? Details(Exception ex)
    {
        return ex is FileOperationException or IOException or UnauthorizedAccessException ? null : ex.ToString();
    }

    // ---- nested types -----------------------------------------------------------------------------------

    /// <summary>One top-level item of a batch.</summary>
    private sealed class WorkItem
    {
        public required string Path { get; init; }
        public required bool IsDirectory { get; init; }
        public string? Error { get; init; }

        /// <summary>True when something already exists at the destination, so the item may raise conflicts.</summary>
        public bool MayConflict { get; init; }

        /// <summary>Bytes measured before the batch runs, for progress over the whole batch.</summary>
        public long Bytes { get; set; }

        public static WorkItem Invalid(string path, string error) => new() { Path = path, IsDirectory = false, Error = error };

        public static WorkItem ForEntry(FileSystemEntry entry) => new()
        {
            Path = entry.FullPath,
            IsDirectory = entry.EntryType == FileSystemEntryType.Directory
        };
    }

    /// <summary>
    /// Counters and progress mapping of one batch. Item progress from the engine is offset by the bytes of the
    /// items already done, so the exposed percentage covers the whole batch.
    /// </summary>
    private sealed class Batch
    {
        private readonly FileOperationsService _owner;
        private readonly FileOperationKind _kind;
        private readonly int _generation;
        private readonly List<FileOperationError> _errors = [];
        private long _bytesBefore;
        private long _itemBytes;
        private int _succeeded;
        private int _skipped;
        private int _failed;
        private int _processedItems;
        private bool _started;

        public Batch(FileOperationsService owner, FileOperationKind kind, int generation, CancellationToken token)
        {
            _owner = owner;
            _kind = kind;
            _generation = generation;
            Token = token;
        }

        public CancellationToken Token { get; }

        /// <summary>Measures the items and publishes the batch total.</summary>
        public async Task MeasureAsync(IReadOnlyList<WorkItem> items)
        {
            long total = 0;
            foreach (var item in items)
            {
                Token.ThrowIfCancellationRequested();
                var path = item.Path;
                _owner.Update(_generation, () => _owner.CurrentFile = path);
                item.Bytes = await _owner.MeasureAsync(item, Token);
                total += item.Bytes;
            }

            _owner.Update(_generation, () =>
            {
                _owner.TotalBytes = total;
                _owner.Percent = _owner.ComputePercent();
            });
        }

        /// <summary>Starts an item and returns the progress sink to hand to the file system service.</summary>
        public IProgress<FileOperationProgress> BeginItem(WorkItem item)
        {
            _started = true;
            _itemBytes = item.Bytes;
            var path = item.Path;
            _owner.Update(_generation, () => _owner.CurrentFile = path);
            return new ItemProgress(this);
        }

        public void Complete(WorkItem item, int skippedFiles)
        {
            if (!item.IsDirectory && skippedFiles > 0)
            {
                _skipped++;
            }
            else
            {
                _succeeded++;
                _skipped += skippedFiles;
            }
        }

        public void Skip()
        {
            _skipped++;
        }

        public void Fail(string path, string message)
        {
            _failed++;
            _errors.Add(new FileOperationError(path, message));
        }

        /// <summary>Records an item rejected by the up-front checks; it counts as processed at once.</summary>
        public void FailUpFront(string path, string message)
        {
            Fail(path, message);
            _processedItems++;
            var processed = _processedItems;
            _owner.Update(_generation, () =>
            {
                _owner.ProcessedItems = processed;
                _owner.Percent = _owner.ComputePercent();
            });
        }

        public void EndItem()
        {
            _bytesBefore += _itemBytes;
            _itemBytes = 0;
            _processedItems++;

            var bytes = _bytesBefore;
            var processed = _processedItems;
            _owner.Update(_generation, () =>
            {
                _owner.ProcessedItems = processed;
                _owner.ProcessedBytes = Clamp(bytes, _owner.TotalBytes);
                _owner.Percent = _owner.ComputePercent();
            });
        }

        public FileOperationResult ToResult(bool cancelled) => new()
        {
            Kind = _kind,
            Succeeded = _succeeded,
            Skipped = _skipped,
            Failed = _failed,
            Errors = _errors.ToArray(),
            Cancelled = cancelled,
            Started = _started
        };

        /// <summary>Called by the engine from its background thread.</summary>
        private void OnItemProgress(FileOperationProgress progress)
        {
            var bytes = _bytesBefore + progress.ProcessedBytes;
            var file = progress.CurrentFile;
            _owner.Update(_generation, () =>
            {
                _owner.CurrentFile = file;
                _owner.ProcessedBytes = Clamp(bytes, _owner.TotalBytes);
                _owner.Percent = _owner.ComputePercent();
            });
        }

        private static long Clamp(long bytes, long total)
        {
            return total > 0 ? Math.Min(bytes, total) : bytes;
        }

        private sealed class ItemProgress(Batch batch) : IProgress<FileOperationProgress>
        {
            public void Report(FileOperationProgress value) => batch.OnItemProgress(value);
        }
    }

    /// <summary>
    /// Shows the conflict dialog and remembers an "apply to all" answer for the rest of the batch. The remaining
    /// count offered to the dialog is an estimate: the pending items whose destination already exists, plus one
    /// when the current item is a folder being merged (its nested conflicts are not known in advance).
    /// </summary>
    private sealed class BatchConflictResolver(IDialogService dialogs, IReadOnlyList<WorkItem> plan)
    {
        private ConflictResolution? _forAll;
        private int _index;
        private int _skips;

        /// <summary>Gets how many files were skipped while processing the current item.</summary>
        public int SkipsInItem => Volatile.Read(ref _skips);

        public void BeginItem(int index)
        {
            _index = index;
            _skips = 0;
        }

        public async Task<ConflictResolution> ResolveAsync(FileConflict conflict, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolution = _forAll ?? await dialogs.ShowConflictAsync(conflict, EstimateRemaining());

            if (_forAll is null && resolution.ApplyToAll && resolution.Action != ConflictAction.Cancel)
            {
                // A typed name only applies to this conflict; later ones get generated unique names.
                _forAll = new ConflictResolution(resolution.Action, ApplyToAll: true);
            }

            if (resolution.Action == ConflictAction.Skip)
            {
                Interlocked.Increment(ref _skips);
            }

            return resolution;
        }

        private int EstimateRemaining()
        {
            var remaining = 0;
            for (var i = _index + 1; i < plan.Count; i++)
            {
                if (plan[i].MayConflict)
                {
                    remaining++;
                }
            }

            if (plan[_index].IsDirectory)
            {
                remaining++;
            }

            return remaining;
        }
    }
}
