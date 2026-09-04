using System.Buffers;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Copies, moves or deletes one source item with pre-scanned totals, byte-level progress, cancellation and
/// per-file conflict resolution. One instance serves one operation and runs on a background thread, so the
/// conflict resolver is invoked from that thread. Whether a destination is the source itself, lies inside it or
/// sits on the same volume is decided by the file system (see <see cref="FileIdentity"/>), not by comparing
/// path strings, so an alias of the source (a symbolic link, a junction, a mapped drive, a macOS firmlink) can
/// never make a move delete its own source or a copy nest a folder inside itself. Symbolic links inside a tree
/// are recreated as links, never followed. New files are streamed to a temporary file next to the destination
/// and renamed into place once complete, so an interrupted copy leaves at most an obviously temporary file.
/// </summary>
internal sealed class FileTransferEngine
{
    private const int StreamBufferSize = 1024 * 1024;
    private const int ChunkSize = 256 * 1024;
    private const string TempSuffix = ".sharpcommander-tmp";
    private const FileOptions StreamOptions = FileOptions.Asynchronous | FileOptions.SequentialScan;

    private readonly IProgress<FileOperationProgress>? _progress;
    private readonly ConflictPolicy _conflicts;
    private readonly CancellationToken _cancellationToken;
    private readonly List<(string Path, string Reason)> _linkFailures = [];
    private string[]? _mountPoints;
    private bool _scanned;
    private int _totalFiles;
    private int _processedFiles;
    private long _totalBytes;
    private long _processedBytes;
    private long _lastReportTicks;

    /// <summary>Gets the file being processed, for error reports.</summary>
    public string CurrentFile { get; private set; } = string.Empty;

    /// <summary>
    /// When set, replaces the volume detection: false forces the cross-volume path (copy, then delete the
    /// source) even inside one volume, so it can be tested without a second disk.
    /// </summary>
    internal bool? SameVolumeOverride { get; init; }

    /// <summary>
    /// Tests only: skips the identity checks made before an operation starts, to exercise the guards that run
    /// while it executes (the last line of defence against an alias of the source).
    /// </summary>
    internal bool SkipIdentityPrechecks { get; init; }

    /// <summary>Minimum time between two progress reports while streaming a file; tests lower it to observe every chunk.</summary>
    internal int ReportIntervalMilliseconds { get; init; } = 40;

    public FileTransferEngine(ConflictResolver? onConflict, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        _conflicts = new ConflictPolicy(onConflict);
        _progress = progress;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Copies a file or directory into a directory.</summary>
    public async Task CopyAsync(string source, string destinationDirectory)
    {
        var (sourcePath, destinationParent, destination) = ResolveTargets(source, destinationDirectory, "copy");
        CurrentFile = sourcePath;
        Report(FileOperationState.Starting);
        Directory.CreateDirectory(destinationParent);

        if (Directory.Exists(sourcePath))
        {
            var directory = new DirectoryInfo(sourcePath);
            if (IsLink(directory))
            {
                AddToTotals(0);
                await TransferLinkAsync(directory, destination, move: false, sameVolume: false);
            }
            else
            {
                ScanTree(directory);
                await CopyDirectoryAsync(directory, destination);
            }
        }
        else
        {
            var file = new FileInfo(sourcePath);
            if (IsLink(file))
            {
                AddToTotals(0);
                await TransferLinkAsync(file, destination, move: false, sameVolume: false);
            }
            else
            {
                AddToTotals(file.Length);
                await CopyFileWithPolicyAsync(file, destination);
            }
        }

        ThrowIfLinksFailed(moved: false);
        CurrentFile = sourcePath;
        Report(FileOperationState.Completed);
    }

    /// <summary>Moves a file or directory into a directory.</summary>
    public async Task MoveAsync(string source, string destinationDirectory)
    {
        var (sourcePath, destinationParent, destination) = ResolveTargets(source, destinationDirectory, "move");
        CurrentFile = sourcePath;
        Report(FileOperationState.Starting);
        Directory.CreateDirectory(destinationParent);
        var sameVolume = OnSameVolume(sourcePath, destinationParent);

        if (Directory.Exists(sourcePath))
        {
            var directory = new DirectoryInfo(sourcePath);
            if (IsLink(directory))
            {
                AddToTotals(0);
                await TransferLinkAsync(directory, destination, move: true, sameVolume);
            }
            else
            {
                await MoveDirectoryAsync(directory, destination, sameVolume);
            }
        }
        else
        {
            var file = new FileInfo(sourcePath);
            if (IsLink(file))
            {
                AddToTotals(0);
                await TransferLinkAsync(file, destination, move: true, sameVolume);
            }
            else
            {
                AddToTotals(file.Length);
                await MoveFileAsync(file, destination, sameVolume);
            }
        }

        ThrowIfLinksFailed(moved: true);
        CurrentFile = sourcePath;
        Report(FileOperationState.Completed);
    }

    /// <summary>Permanently deletes a file or directory tree.</summary>
    public Task DeleteAsync(string path)
    {
        var target = PathUtils.NormalizeFullPath(path);
        CurrentFile = target;

        if (File.Exists(target))
        {
            var file = new FileInfo(target);
            AddToTotals(IsLink(file) ? 0 : SafeLength(file));
            Report(FileOperationState.Starting);
            DeleteFile(file);
        }
        else if (Directory.Exists(target))
        {
            var directory = new DirectoryInfo(target);
            Report(FileOperationState.Starting);
            if (IsLink(directory))
            {
                DeleteLink(directory);
            }
            else
            {
                ScanTree(directory);
                DeleteTree(directory);
            }
        }
        else
        {
            throw new FileOperationException(FileOperationErrorKind.NotFound, target, $"'{target}' does not exist.");
        }

        CurrentFile = target;
        Report(FileOperationState.Completed);
        return Task.CompletedTask;
    }

    /// <summary>Reports a terminal state; used by the service when the operation failed or was cancelled.</summary>
    public void ReportState(FileOperationState state) => Report(state);

    /// <summary>True for a symbolic link or, on Windows, a junction; false for anything else or when unreadable.</summary>
    internal static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) != 0 && info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- validation -------------------------------------------------------------------------------------

    /// <summary>
    /// Checks the request before anything is touched: the source must exist and must not be a volume root, the
    /// destination must not be the source itself (by path or by identity, so aliases count) and a directory must
    /// not go into itself or one of its subfolders (again by path and by identity).
    /// </summary>
    private (string Source, string DestinationParent, string Destination) ResolveTargets(string source, string destinationDirectory, string verb)
    {
        var sourcePath = PathUtils.NormalizeFullPath(source);
        var destinationParent = PathUtils.NormalizeFullPath(destinationDirectory);
        var isDirectory = Directory.Exists(sourcePath);

        if (!isDirectory && !File.Exists(sourcePath))
        {
            throw new FileOperationException(FileOperationErrorKind.NotFound, sourcePath, $"'{sourcePath}' does not exist.");
        }

        if (PathUtils.IsVolumeRoot(sourcePath))
        {
            throw new FileOperationException(FileOperationErrorKind.InvalidName, sourcePath,
                $"Cannot {verb} '{sourcePath}': a whole volume cannot be {Past(verb)} as one item. Open it and {verb} its contents instead.");
        }

        var name = Path.GetFileName(sourcePath);
        var destination = Path.Combine(destinationParent, name);

        if (PathUtils.AreSamePath(sourcePath, destination) || (!SkipIdentityPrechecks && FileIdentity.AreSameEntry(sourcePath, destination)))
        {
            throw new FileOperationException(FileOperationErrorKind.SameLocation, sourcePath,
                $"Cannot {verb} '{name}': the destination is the same location as the source.");
        }

        if (isDirectory && !IsLink(new DirectoryInfo(sourcePath)))
        {
            var inside = PathUtils.IsSameOrDescendant(sourcePath, destinationParent)
                         || (!SkipIdentityPrechecks && FileIdentity.IsSameOrInside(sourcePath, destinationParent) == true);
            if (inside)
            {
                throw new FileOperationException(FileOperationErrorKind.DestinationInsideSource, sourcePath,
                    $"Cannot {verb} '{name}' into itself or one of its subfolders.");
            }
        }

        return (sourcePath, destinationParent, destination);
    }

    private static string Past(string verb) => verb == "copy" ? "copied" : "moved";

    // ---- copy -------------------------------------------------------------------------------------------

    private async Task CopyDirectoryAsync(DirectoryInfo source, string destination)
    {
        var target = await ResolveDirectoryTargetAsync(source, destination);
        if (target is null)
        {
            AccountSkippedTree(source);
            return;
        }

        var created = CreateDirectoryOutsideSource(source, target);

        foreach (var directory in source.EnumerateDirectories())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var childTarget = Path.Combine(target, directory.Name);

            if (IsLink(directory))
            {
                await TransferLinkAsync(directory, childTarget, move: false, sameVolume: false);
            }
            else
            {
                await CopyDirectoryAsync(directory, childTarget);
            }
        }

        foreach (var file in source.EnumerateFiles())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var childTarget = Path.Combine(target, file.Name);

            if (IsLink(file))
            {
                await TransferLinkAsync(file, childTarget, move: false, sameVolume: false);
            }
            else
            {
                await CopyFileWithPolicyAsync(file, childTarget);
            }
        }

        if (created)
        {
            ApplyDirectoryMetadata(source, target);
        }
    }

    private async Task<bool> CopyFileWithPolicyAsync(FileInfo source, string destination)
    {
        CurrentFile = source.FullName;
        Report(FileOperationState.InProgress);

        var target = await ResolveFileTargetAsync(source, destination);
        if (target is null)
        {
            SkipFile(source.Length);
            return false;
        }

        await CopyFileContentsAsync(source, target);
        CompleteFile();
        return true;
    }

    /// <summary>
    /// Streams the file into a temporary file next to the destination and renames it over the destination once
    /// it is complete, whether or not something already exists there. An interrupted copy (cancel, error, crash,
    /// process exit) therefore leaves at most an obviously temporary "*.sharpcommander-tmp" file, never a
    /// plausible but truncated file under the real name.
    /// </summary>
    private async Task CopyFileContentsAsync(FileInfo source, string target)
    {
        var temp = $"{target}.{Guid.NewGuid():N}{TempSuffix}";

        try
        {
            await using (var input = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, StreamBufferSize, StreamOptions))
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, StreamBufferSize, StreamOptions))
            {
                await PumpAsync(input, output);
            }

            ApplyFileMetadata(source, temp);

            if (File.Exists(target))
            {
                ClearReadOnly(target);
            }

            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    private async Task PumpAsync(FileStream input, FileStream output)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, ChunkSize), _cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), _cancellationToken);
                _processedBytes += read;
                ReportThrottled();
            }

            await output.FlushAsync(_cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // ---- move -------------------------------------------------------------------------------------------

    private async Task MoveDirectoryAsync(DirectoryInfo source, string destination, bool sameVolume)
    {
        var target = await ResolveDirectoryTargetAsync(source, destination);
        if (target is null)
        {
            AccountSkippedTree(source);
            return;
        }

        if (!Directory.Exists(target) && sameVolume && TryRenameDirectory(source, target))
        {
            AccountMovedTree(target);
            return;
        }

        // Merging into an existing directory, or crossing volumes: go file by file. The whole tree is scanned
        // once, at the top level, so the recursion below reports against a known total; a tree with links is
        // moved across volumes only when links can be created at the destination, before anything is deleted.
        if (!_scanned)
        {
            var links = ScanTree(source);
            if (!sameVolume && links > 0)
            {
                ProbeLinkCreation(Path.GetDirectoryName(target) ?? target, source.FullName);
            }
        }

        var created = CreateDirectoryOutsideSource(source, target);

        foreach (var directory in source.EnumerateDirectories())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var childTarget = Path.Combine(target, directory.Name);

            if (IsLink(directory))
            {
                await TransferLinkAsync(directory, childTarget, move: true, sameVolume);
            }
            else
            {
                await MoveDirectoryAsync(directory, childTarget, sameVolume);
            }
        }

        foreach (var file in source.EnumerateFiles())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var childTarget = Path.Combine(target, file.Name);

            if (IsLink(file))
            {
                await TransferLinkAsync(file, childTarget, move: true, sameVolume);
            }
            else
            {
                await MoveFileAsync(file, childTarget, sameVolume);
            }
        }

        if (created)
        {
            ApplyDirectoryMetadata(source, target);
        }

        DeleteDirectoryIfEmpty(source);
    }

    private async Task MoveFileAsync(FileInfo source, string destination, bool sameVolume)
    {
        CurrentFile = source.FullName;
        Report(FileOperationState.InProgress);

        var target = await ResolveFileTargetAsync(source, destination);
        if (target is null)
        {
            SkipFile(source.Length);
            return;
        }

        var length = source.Length;
        if (sameVolume && TryRenameFile(source, target))
        {
            _processedBytes += length;
            CompleteFile();
            return;
        }

        await CopyFileContentsAsync(source, target);

        if (FileIdentity.AreSameEntry(source.FullName, target))
        {
            // The destination turned out to be the source through an alias the checks could not see: the copy
            // just rewrote the file in place, and deleting the "source" would delete the only copy.
            throw new FileOperationException(FileOperationErrorKind.SameLocation, source.FullName,
                $"Cannot move '{source.Name}': the destination is the same location as the source.");
        }

        RemoveFile(source);
        CompleteFile();
    }

    /// <summary>
    /// Renames a whole directory. Returns false when the rename is refused although both sides look fine
    /// (typically a mount point below the same root), so the caller falls back to copy and delete; a rename
    /// refused because the destination lies inside the source is a hard error, as the fallback would nest the tree.
    /// </summary>
    private static bool TryRenameDirectory(DirectoryInfo source, string target)
    {
        try
        {
            Directory.Move(source.FullName, target);
            return true;
        }
        catch (IOException) when (Directory.Exists(source.FullName) && !Directory.Exists(target))
        {
            if (FileIdentity.IsSameOrInside(source.FullName, Path.GetDirectoryName(target) ?? target) == true)
            {
                throw new FileOperationException(FileOperationErrorKind.DestinationInsideSource, source.FullName,
                    $"Cannot move '{source.Name}' into itself or one of its subfolders.");
            }

            return false;
        }
    }

    private static bool TryRenameFile(FileInfo source, string target)
    {
        try
        {
            if (File.Exists(target))
            {
                ClearReadOnly(target);
            }

            File.Move(source.FullName, target, overwrite: true);
            return true;
        }
        catch (IOException) when (File.Exists(source.FullName))
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the destination directory of a tree and makes sure it did not land inside the source through an
    /// alias the up-front checks could not see (the copy would otherwise recurse into what it writes): the
    /// directory just created is removed again and the operation refused. Returns true when the directory was created.
    /// </summary>
    private static bool CreateDirectoryOutsideSource(DirectoryInfo source, string target)
    {
        var shadow = Path.Combine(source.FullName, Path.GetFileName(target));
        var shadowExisted = Directory.Exists(shadow);
        var created = !Directory.Exists(target);
        Directory.CreateDirectory(target);

        var inside = FileIdentity.IsSameOrInside(source.FullName, target) == true || (created && !shadowExisted && Directory.Exists(shadow));
        if (!inside)
        {
            return created;
        }

        if (created)
        {
            TryDeleteEmptyDirectory(target);
        }

        throw new FileOperationException(FileOperationErrorKind.DestinationInsideSource, source.FullName,
            $"Cannot copy or move '{source.Name}' into itself or one of its subfolders.");
    }

    // ---- links ------------------------------------------------------------------------------------------

    /// <summary>
    /// Copies or moves a symbolic link (or junction) as a link, never following it. An occupied destination is a
    /// conflict like any other; Overwrite is possible only when the existing entry is itself a link. A link that
    /// cannot be recreated (Windows without the symbolic link privilege, a FAT volume) is recorded and reported
    /// once the rest of the tree is done; for a move it stays at the source.
    /// </summary>
    private async Task TransferLinkAsync(FileSystemInfo link, string destination, bool move, bool sameVolume)
    {
        CurrentFile = link.FullName;
        Report(FileOperationState.InProgress);

        var target = await ResolveLinkTargetAsync(link, destination);
        if (target is null)
        {
            SkipFile(0);
            return;
        }

        if (move && sameVolume && TryRenameLink(link, target))
        {
            CompleteFile();
            return;
        }

        if (!TryRecreateLink(link, target, out var reason))
        {
            _linkFailures.Add((link.FullName, reason));
            _processedFiles++;
            Report(FileOperationState.InProgress);
            return;
        }

        if (move)
        {
            DeleteLink(link);
        }

        CompleteFile();
    }

    /// <summary>
    /// Returns where the link should go: the destination when free, a renamed path, or null to skip. An existing
    /// link may be replaced (it is removed here); a file or a folder in the way cannot be overwritten by a link.
    /// </summary>
    private async Task<string?> ResolveLinkTargetAsync(FileSystemInfo link, string destination)
    {
        var existing = ExistingEntry(destination);
        if (existing is null)
        {
            return destination;
        }

        var conflict = new FileConflict(link.FullName, destination, IsDirectory: false, 0,
            existing is FileInfo file ? SafeLength(file) : 0, link.LastWriteTime, existing.LastWriteTime);
        var resolution = await _conflicts.ResolveAsync(conflict, _cancellationToken);
        var target = ApplyResolution(resolution, link.Name, destination, canOverwrite: IsLink(existing),
            $"Cannot replace '{destination}': a link cannot replace a file or a folder.");

        if (target is not null && resolution.Action == ConflictAction.Overwrite)
        {
            DeleteLink(existing);
        }

        return target;
    }

    private static bool TryRenameLink(FileSystemInfo link, string target)
    {
        try
        {
            if (link is DirectoryInfo)
            {
                Directory.Move(link.FullName, target);
            }
            else
            {
                File.Move(link.FullName, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryRecreateLink(FileSystemInfo link, string target, out string reason)
    {
        try
        {
            var linkTarget = link.LinkTarget ?? throw new IOException("The link target could not be read.");
            if (link is DirectoryInfo)
            {
                Directory.CreateSymbolicLink(target, linkTarget);
            }
            else
            {
                File.CreateSymbolicLink(target, linkTarget);
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Verifies that links can be created in <paramref name="directory"/> before a move deletes anything, so a
    /// tree with links is never left split across volumes (Windows needs a privilege, FAT volumes refuse links).
    /// </summary>
    private static void ProbeLinkCreation(string directory, string sourcePath)
    {
        var probe = Path.Combine(directory, $".{Guid.NewGuid():N}{TempSuffix}");
        try
        {
            File.CreateSymbolicLink(probe, ".");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new FileOperationException(FileOperationErrorKind.AccessDenied, sourcePath,
                $"Cannot move '{Path.GetFileName(sourcePath)}': it contains links and links cannot be created in '{directory}' ({ex.Message}). Nothing was moved.", ex);
        }

        TryDeleteFile(probe);
    }

    /// <summary>Reports the links that could not be recreated, once the rest of the item was processed.</summary>
    private void ThrowIfLinksFailed(bool moved)
    {
        if (_linkFailures.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", _linkFailures.Take(5).Select(failure => Path.GetFileName(failure.Path)));
        if (_linkFailures.Count > 5)
        {
            names += ", ...";
        }

        var outcome = moved
            ? $"the rest was moved, but {Plural(_linkFailures.Count, "link")} stayed at the source because it could not be recreated at the destination"
            : $"the rest was copied, but {Plural(_linkFailures.Count, "link")} could not be recreated at the destination";

        throw new FileOperationException(FileOperationErrorKind.AccessDenied, _linkFailures[0].Path,
            $"{char.ToUpperInvariant(outcome[0])}{outcome[1..]}: {names}. {_linkFailures[0].Reason}");
    }

    /// <summary>Removes a symbolic link (or junction) without touching what it points to.</summary>
    private static void DeleteLink(FileSystemInfo link)
    {
        if (link is DirectoryInfo && OperatingSystem.IsWindows())
        {
            Directory.Delete(link.FullName);
        }
        else
        {
            File.Delete(link.FullName);
        }
    }

    /// <summary>The entry at <paramref name="path"/> (a dangling link included), or null when nothing is there.</summary>
    private static FileSystemInfo? ExistingEntry(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return File.Exists(path) ? new FileInfo(path) : null;
    }

    // ---- delete -----------------------------------------------------------------------------------------

    private void DeleteTree(DirectoryInfo directory)
    {
        foreach (var subdirectory in directory.EnumerateDirectories())
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (IsLink(subdirectory))
            {
                CurrentFile = subdirectory.FullName;
                DeleteLink(subdirectory);
                CompleteFile();
            }
            else
            {
                DeleteTree(subdirectory);
            }
        }

        foreach (var file in directory.EnumerateFiles())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            DeleteFile(file);
        }

        CurrentFile = directory.FullName;
        RemoveDirectory(directory);
    }

    private void DeleteFile(FileInfo file)
    {
        CurrentFile = file.FullName;
        var length = IsLink(file) ? 0 : SafeLength(file);
        RemoveFile(file);
        _processedFiles++;
        _processedBytes += length;
        Report(FileOperationState.InProgress);
    }

    private static void RemoveFile(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (UnauthorizedAccessException) when ((file.Attributes & FileAttributes.ReadOnly) != 0)
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
            file.Delete();
        }
    }

    private static void RemoveDirectory(DirectoryInfo directory)
    {
        try
        {
            directory.Delete();
        }
        catch (UnauthorizedAccessException) when ((directory.Attributes & FileAttributes.ReadOnly) != 0)
        {
            directory.Attributes &= ~FileAttributes.ReadOnly;
            directory.Delete();
        }
    }

    private static void DeleteDirectoryIfEmpty(DirectoryInfo directory)
    {
        if (!directory.EnumerateFileSystemInfos().Any())
        {
            RemoveDirectory(directory);
        }
    }

    // ---- conflicts --------------------------------------------------------------------------------------

    /// <summary>
    /// Returns where the directory should go: the destination itself (new, or an existing directory to merge
    /// into), a renamed path, or null to skip. A file in the way raises a conflict.
    /// </summary>
    private async Task<string?> ResolveDirectoryTargetAsync(DirectoryInfo source, string destination)
    {
        if (Directory.Exists(destination) || !File.Exists(destination))
        {
            return destination;
        }

        var existing = new FileInfo(destination);
        var conflict = new FileConflict(source.FullName, destination, IsDirectory: true, 0, SafeLength(existing),
            source.LastWriteTime, existing.LastWriteTime);
        var resolution = await _conflicts.ResolveAsync(conflict, _cancellationToken);
        return ApplyResolution(resolution, source.Name, destination, canOverwrite: false,
            $"Cannot replace '{destination}': a file and a folder cannot replace each other.");
    }

    /// <summary>
    /// Returns where the file should go: the destination (new, or to overwrite), a renamed path, or null to skip.
    /// </summary>
    private async Task<string?> ResolveFileTargetAsync(FileInfo source, string destination)
    {
        var existingFile = File.Exists(destination) ? new FileInfo(destination) : null;
        var existingDirectory = existingFile is null && Directory.Exists(destination);

        if (existingFile is null && !existingDirectory)
        {
            return destination;
        }

        var conflict = new FileConflict(source.FullName, destination, IsDirectory: false, source.Length,
            existingFile is null ? 0 : SafeLength(existingFile), source.LastWriteTime,
            existingFile?.LastWriteTime ?? Directory.GetLastWriteTime(destination));
        var resolution = await _conflicts.ResolveAsync(conflict, _cancellationToken);
        return ApplyResolution(resolution, source.Name, destination, canOverwrite: existingFile is not null,
            $"Cannot replace '{destination}': a file and a folder cannot replace each other.");
    }

    private static string? ApplyResolution(ConflictResolution resolution, string name, string destination, bool canOverwrite, string overwriteRefusal)
    {
        switch (resolution.Action)
        {
            case ConflictAction.Overwrite:
                if (!canOverwrite)
                {
                    throw new FileOperationException(FileOperationErrorKind.TargetExists, destination, overwriteRefusal);
                }

                return destination;

            case ConflictAction.Skip:
                return null;

            case ConflictAction.Rename:
                return GetRenamedTarget(resolution.NewName, name, destination);

            default:
                throw new OperationCanceledException("The operation was cancelled.");
        }
    }

    private static string GetRenamedTarget(string? newName, string name, string destination)
    {
        var directory = Path.GetDirectoryName(destination) ?? string.Empty;

        if (string.IsNullOrEmpty(newName))
        {
            return Path.Combine(directory, PathUtils.GetUniqueName(directory, name));
        }

        var error = PathUtils.ValidateFileName(newName);
        if (error is not null)
        {
            throw new FileOperationException(FileOperationErrorKind.InvalidName, destination, error);
        }

        var renamed = Path.Combine(directory, newName);
        if (File.Exists(renamed) || Directory.Exists(renamed))
        {
            throw new FileOperationException(FileOperationErrorKind.TargetExists, renamed, $"'{newName}' already exists at the destination.");
        }

        return renamed;
    }

    // ---- accounting and progress ------------------------------------------------------------------------

    /// <summary>
    /// Counts the files and bytes below a directory (links count as files of size zero); unreadable
    /// subdirectories are estimated as empty. Returns how many links the tree contains.
    /// </summary>
    private int ScanTree(DirectoryInfo root)
    {
        var (files, bytes, links) = MeasureTree(root);
        _totalFiles += files;
        _totalBytes += bytes;
        _scanned = true;
        return links;
    }

    private void AccountSkippedTree(DirectoryInfo directory)
    {
        if (!_scanned)
        {
            return;
        }

        var (files, bytes, _) = MeasureTree(directory);
        _processedFiles += files;
        _processedBytes += bytes;
        Report(FileOperationState.InProgress);
    }

    private void AccountMovedTree(string target)
    {
        if (!_scanned)
        {
            return;
        }

        var (files, bytes, _) = MeasureTree(new DirectoryInfo(target));
        _processedFiles += files;
        _processedBytes += bytes;
        Report(FileOperationState.InProgress);
    }

    private (int Files, long Bytes, int Links) MeasureTree(DirectoryInfo root)
    {
        var files = 0;
        var links = 0;
        long bytes = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var info in current.EnumerateFileSystemInfos())
                {
                    if (IsLink(info))
                    {
                        files++;
                        links++;
                    }
                    else if (info is DirectoryInfo subdirectory)
                    {
                        pending.Push(subdirectory);
                    }
                    else if (info is FileInfo file)
                    {
                        files++;
                        bytes += SafeLength(file);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Totals are an estimate; the copy itself reports the real error.
            }
        }

        return (files, bytes, links);
    }

    private void AddToTotals(long bytes)
    {
        _totalFiles++;
        _totalBytes += bytes;
    }

    private void SkipFile(long length)
    {
        _processedFiles++;
        _processedBytes += length;
        Report(FileOperationState.InProgress);
    }

    private void CompleteFile()
    {
        _processedFiles++;
        Report(FileOperationState.InProgress);
    }

    private void Report(FileOperationState state)
    {
        if (_progress is null)
        {
            return;
        }

        _lastReportTicks = Environment.TickCount64;
        _progress.Report(new FileOperationProgress
        {
            CurrentFile = CurrentFile,
            State = state,
            TotalFiles = _totalFiles,
            ProcessedFiles = _processedFiles,
            TotalBytes = _totalBytes,
            ProcessedBytes = _processedBytes
        });
    }

    /// <summary>Reports at most every few tens of milliseconds while streaming a file.</summary>
    private void ReportThrottled()
    {
        if (_progress is null || Environment.TickCount64 - _lastReportTicks < ReportIntervalMilliseconds)
        {
            return;
        }

        Report(FileOperationState.InProgress);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a rename can move the entry into the directory: decided by the volume the file system reports for
    /// each side, falling back to the drive letter or the mount point table when it cannot be asked.
    /// </summary>
    private bool OnSameVolume(string entryPath, string directoryPath)
    {
        if (SameVolumeOverride is { } forced)
        {
            return forced;
        }

        if (FileIdentity.OnSameVolume(entryPath, directoryPath) is { } known)
        {
            return known;
        }

        if (OperatingSystem.IsWindows())
        {
            return string.Equals(Path.GetPathRoot(entryPath), Path.GetPathRoot(directoryPath), StringComparison.OrdinalIgnoreCase);
        }

        _mountPoints ??= MountPoints.Load();
        return string.Equals(MountPoints.Of(entryPath, _mountPoints), MountPoints.Of(directoryPath, _mountPoints), StringComparison.Ordinal);
    }

    private static void ApplyFileMetadata(FileInfo source, string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, source.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: some file systems refuse timestamps.
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.SetAttributes(path, source.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
        else
        {
            try
            {
                File.SetUnixFileMode(path, source.UnixFileMode);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: for example FAT volumes have no permissions.
            }
        }
    }

    private static void ApplyDirectoryMetadata(DirectoryInfo source, string target)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(target, source.LastWriteTimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The replacing move reports the real error if it still fails.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done for a partial file we cannot remove.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind empty; the error the caller raises explains why the operation stopped.
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
