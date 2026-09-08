using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Sends each operation to the file system its path belongs to: the local disk, or an open SFTP session. This is
/// the whole trick behind remote panels — the panels, the operations engine and every command keep talking to one
/// <see cref="IFileSystemService"/> and never learn that some paths are on another machine, so copy, move,
/// rename, delete and new folder work between the two sides with no special cases of their own.
///
/// Transfers that cross the boundary are streamed one file at a time; a folder is walked and rebuilt on the other
/// side. Operations that have no honest remote meaning — opening a terminal, showing an entry in the system file
/// manager — say so instead of pretending.
/// </summary>
public sealed class RoutingFileSystemService(IFileSystemService local, ISftpConnections connections) : IFileSystemService
{
    private readonly IFileSystemService _local = local ?? throw new ArgumentNullException(nameof(local));
    private readonly ISftpConnections _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return _local.GetEntriesAsync(path, cancellationToken);
        }

        return ListRemoteAsync(address, cancellationToken);
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        return _local.GetDrivesAsync(cancellationToken);
    }

    public async Task CopyAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await TransferAsync(source, destinationDirectory, move: false, onConflict, progress, cancellationToken);
    }

    public async Task MoveAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await TransferAsync(source, destinationDirectory, move: true, onConflict, progress, cancellationToken);
    }

    public Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return _local.DeleteAsync(path, progress, cancellationToken);
        }

        progress?.Report(new FileOperationProgress { CurrentFile = path, State = FileOperationState.InProgress });
        return _connections.Require(address).DeleteAsync(address.Path, cancellationToken);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return _local.CreateDirectoryAsync(path, cancellationToken);
        }

        return _connections.Require(address).CreateDirectoryAsync(address.Path, cancellationToken);
    }

    /// <summary>
    /// Remote files are brought down to a temporary copy and opened from there: the local machine is the only one
    /// with applications on it. The copy is left in the system temp folder, which the system clears in its own time.
    /// </summary>
    public async Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            await _local.OpenWithDefaultAsync(path, cancellationToken);
            return;
        }

        var session = _connections.Require(address);
        var temp = Path.Combine(Path.GetTempPath(), "SharpCommander", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        var local = Path.Combine(temp, RemotePath.GetName(address.Path));
        await session.DownloadAsync(address.Path, local, null, cancellationToken);
        await _local.OpenWithDefaultAsync(local, cancellationToken);
    }

    public bool IsExecutableOrScript(string path)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return _local.IsExecutableOrScript(path);
        }

        // A remote file is never run here; it is downloaded first, and that copy is what gets asked about.
        _ = address;
        return false;
    }

    public bool Exists(string path)
    {
        return SftpAddress.TryParse(path) is { } address
            ? _connections.Find(address)?.Exists(address.Path) == true
            : _local.Exists(path);
    }

    public bool IsDirectory(string path)
    {
        return SftpAddress.TryParse(path) is { } address
            ? _connections.Find(address)?.IsDirectory(address.Path) == true
            : _local.IsDirectory(path);
    }

    public string? GetParentPath(string path)
    {
        return SftpAddress.TryParse(path) is { } address ? address.Parent() : _local.GetParentPath(path);
    }

    public string GetDefaultDirectory() => _local.GetDefaultDirectory();

    public Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return _local.RenameAsync(path, newName, cancellationToken);
        }

        return _connections.Require(address).RenameAsync(address.Path, newName, cancellationToken);
    }

    public Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default)
    {
        return SftpAddress.IsRemote(path)
            ? throw new NotSupportedException("A remote folder cannot be shown in the system file manager.")
            : _local.OpenInFileExplorerAsync(path, cancellationToken);
    }

    public Task RevealInFileExplorerAsync(string path, CancellationToken cancellationToken = default)
    {
        return SftpAddress.IsRemote(path)
            ? throw new NotSupportedException("A remote entry cannot be shown in the system file manager.")
            : _local.RevealInFileExplorerAsync(path, cancellationToken);
    }

    public async Task<long> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (SftpAddress.TryParse(path) is not { } address)
        {
            return await _local.GetDirectorySizeAsync(path, progress, cancellationToken);
        }

        var size = await _connections.Require(address).GetSizeAsync(address.Path, cancellationToken);
        progress?.Report(size);
        return size;
    }

    public async Task<int> ApplyAttributesAsync(IReadOnlyList<string> paths, AttributeChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(change);

        var remote = paths.Where(SftpAddress.IsRemote).ToList();
        var here = paths.Where(path => !SftpAddress.IsRemote(path)).ToList();

        var changed = here.Count > 0 ? await _local.ApplyAttributesAsync(here, change, cancellationToken) : 0;

        // The Windows attribute flags have no counterpart on a server; only the permission bits carry over.
        if (change.UnixMode is { } mode)
        {
            foreach (var path in remote)
            {
                var address = SftpAddress.TryParse(path)!;
                await _connections.Require(address).SetPermissionsAsync(address.Path, mode, cancellationToken);
                changed++;
            }
        }

        return changed;
    }

    public Task<long> FindFirstDifferenceAsync(string leftPath, string rightPath, CancellationToken cancellationToken = default)
    {
        return SftpAddress.IsRemote(leftPath) || SftpAddress.IsRemote(rightPath)
            ? throw new NotSupportedException("Comparing a remote file byte for byte would mean downloading it; copy it across first.")
            : _local.FindFirstDifferenceAsync(leftPath, rightPath, cancellationToken);
    }

    public Task OpenTerminalAsync(string directory, CancellationToken cancellationToken = default)
    {
        return SftpAddress.IsRemote(directory)
            ? throw new NotSupportedException("A terminal cannot be opened in a remote folder.")
            : _local.OpenTerminalAsync(directory, cancellationToken);
    }

    private async Task<IReadOnlyList<FileSystemEntry>> ListRemoteAsync(SftpAddress address, CancellationToken cancellationToken)
    {
        var entries = await _connections.Require(address).ListAsync(address.Path, cancellationToken);

        // The listing comes back with server paths; the panels need addresses they can navigate to.
        return [.. entries.Select(entry => entry with { FullPath = address.With(entry.FullPath).ToString() })];
    }

    /// <summary>
    /// One entry from one side to the other. Four combinations, and only the mixed ones need the bytes to travel:
    /// two local paths are the local service's business, and two paths on one server are the server's.
    /// </summary>
    private async Task TransferAsync(string source, string destinationDirectory, bool move, ConflictResolver? onConflict, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var from = SftpAddress.TryParse(source);
        var to = SftpAddress.TryParse(destinationDirectory);

        if (from is null && to is null)
        {
            await (move
                ? _local.MoveAsync(source, destinationDirectory, onConflict, progress, cancellationToken)
                : _local.CopyAsync(source, destinationDirectory, onConflict, progress, cancellationToken));
            return;
        }

        if (from is not null && to is not null && string.Equals(from.Endpoint, to.Endpoint, StringComparison.OrdinalIgnoreCase))
        {
            var name = RemotePath.GetName(from.Path);
            await _connections.Require(from)
                .CopyWithinServerAsync(from.Path, RemotePath.Combine(to.Path, name), move, cancellationToken);
            return;
        }

        if (from is null)
        {
            await UploadAsync(source, to!, move, progress, cancellationToken);
            return;
        }

        if (to is null)
        {
            await DownloadAsync(from, destinationDirectory, move, progress, cancellationToken);
            return;
        }

        // Two different servers: down from one and up to the other, one file at a time.
        throw new NotSupportedException("Copying straight between two servers is not supported; go through a local folder.");
    }

    private async Task UploadAsync(string localPath, SftpAddress destination, bool move, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var session = _connections.Require(destination);

        if (Directory.Exists(localPath))
        {
            var root = RemotePath.Combine(destination.Path, Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar)));
            await session.CreateDirectoryAsync(root, cancellationToken);

            foreach (var child in Directory.EnumerateFileSystemEntries(localPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UploadAsync(child, destination.With(root), move: false, progress, cancellationToken);
            }

            if (move)
            {
                Directory.Delete(localPath, recursive: true);
            }

            return;
        }

        progress?.Report(new FileOperationProgress { CurrentFile = localPath, State = FileOperationState.InProgress });

        await using (var stream = File.OpenRead(localPath))
        {
            await session.WriteAsync(stream, RemotePath.Combine(destination.Path, Path.GetFileName(localPath)), null, cancellationToken);
        }

        if (move)
        {
            File.Delete(localPath);
        }
    }

    private async Task DownloadAsync(SftpAddress source, string destinationDirectory, bool move, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var session = _connections.Require(source);
        var name = RemotePath.GetName(source.Path);

        if (session.IsDirectory(source.Path))
        {
            var root = Path.Combine(destinationDirectory, name);
            Directory.CreateDirectory(root);

            foreach (var child in await session.ListAsync(source.Path, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DownloadAsync(source.With(RemotePath.Combine(source.Path, child.Name)), root, move: false, progress, cancellationToken);
            }

            if (move)
            {
                await session.DeleteAsync(source.Path, cancellationToken);
            }

            return;
        }

        var target = Path.Combine(destinationDirectory, name);
        progress?.Report(new FileOperationProgress { CurrentFile = source.ToString(), State = FileOperationState.InProgress });

        // The service refuses to write over an existing file, so a repeated copy lands beside it.
        if (File.Exists(target) || Directory.Exists(target))
        {
            target = Path.Combine(destinationDirectory, PathUtils.GetUniqueName(destinationDirectory, name));
        }

        await session.DownloadAsync(source.Path, target, null, cancellationToken);

        if (move)
        {
            await session.DeleteAsync(source.Path, cancellationToken);
        }
    }
}
