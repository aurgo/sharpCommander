using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// One SFTP connection. Deliberately small: browsing, and moving whole files in either direction. Everything a
/// remote file system cannot do faithfully — opening a file with its local application, a system trash, a
/// terminal — is simply absent rather than present and broken.
/// </summary>
public interface ISftpService : IDisposable
{
    bool IsConnected { get; }

    /// <summary>The site currently connected, or null.</summary>
    SftpSite? Site { get; }

    /// <summary>
    /// Opens a connection. <paramref name="password"/> is only used when the site authenticates that way; for key
    /// authentication it is the key's passphrase, or null when the key has none.
    /// </summary>
    Task ConnectAsync(SftpSite site, string? password, CancellationToken cancellationToken = default);

    void Disconnect();

    /// <summary>The folder to start in: the site's own choice, or whatever the server drops us in.</summary>
    Task<string> GetStartDirectoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists a remote folder. "." and ".." are not returned; the caller adds its own parent entry.</summary>
    Task<IReadOnlyList<FileSystemEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Copies a remote file down to <paramref name="localPath"/>, which must not exist.</summary>
    Task DownloadAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Copies a local file up to <paramref name="remotePath"/>, replacing what is there.</summary>
    Task UploadAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a remote folder, including any missing parents.</summary>
    Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Deletes a remote file or folder; folders are emptied first.</summary>
    Task DeleteAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Renames an entry in place, keeping it in the same folder.</summary>
    Task RenameAsync(string remotePath, string newName, CancellationToken cancellationToken = default);

    /// <summary>Copies or moves an entry entirely on the server, without the bytes crossing the network twice.</summary>
    Task CopyWithinServerAsync(string sourcePath, string destinationPath, bool move, CancellationToken cancellationToken = default);

    /// <summary>Total size of a folder's contents, or the file's own size.</summary>
    Task<long> GetSizeAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Changes the permission bits of an entry.</summary>
    Task SetPermissionsAsync(string remotePath, UnixFileMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether something is there. Blocking on purpose: the interface the panels use is synchronous here, and the
    /// underlying client is too, so wrapping it in a task would only hide the network round trip.
    /// </summary>
    bool Exists(string remotePath);

    bool IsDirectory(string remotePath);

    /// <summary>Opens a remote file for reading, for transfers that stream straight into another file system.</summary>
    Task<Stream> OpenReadAsync(string remotePath, CancellationToken cancellationToken = default);

    /// <summary>Writes a stream to a remote file, replacing what is there.</summary>
    Task WriteAsync(Stream content, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
}
