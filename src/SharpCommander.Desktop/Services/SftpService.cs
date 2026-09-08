using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// SFTP over SSH.NET. One connection at a time, which is what a two-pane file manager needs and keeps the
/// lifetime obvious.
///
/// Host keys are accepted on first sight and remembered for the rest of the session, then checked on every later
/// connection: a fingerprint that changes mid-session means something is wrong and the connection is refused
/// rather than silently trusted. Verifying against the user's known_hosts is the honest next step; until then
/// this at least catches a swap under our feet.
/// </summary>
public sealed class SftpService : ISftpService
{
    private static readonly string[] DefaultKeyNames = ["id_ed25519", "id_ecdsa", "id_rsa"];

    private readonly Dictionary<string, string> _knownHosts = new(StringComparer.OrdinalIgnoreCase);
    private SftpClient? _client;

    public bool IsConnected => _client?.IsConnected == true;

    public SftpSite? Site { get; private set; }

    public async Task ConnectAsync(SftpSite site, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);

        Disconnect();

        var info = BuildConnectionInfo(site, password);
        var client = new SftpClient(info);

        client.HostKeyReceived += (_, e) =>
        {
            // FingerPrintSHA256 is already a string in this version; FingerPrint is the older byte form.
            var fingerprint = !string.IsNullOrEmpty(e.FingerPrintSHA256)
                ? e.FingerPrintSHA256
                : Convert.ToHexString(e.FingerPrint ?? []);
            var endpoint = $"{site.Host}:{site.Port}";

            if (_knownHosts.TryGetValue(endpoint, out var seen))
            {
                e.CanTrust = string.Equals(seen, fingerprint, StringComparison.OrdinalIgnoreCase);
                if (!e.CanTrust)
                {
                    AppLog.Warning($"The host key of {endpoint} changed during this session; the connection was refused.");
                }

                return;
            }

            _knownHosts[endpoint] = fingerprint;
            e.CanTrust = true;
        };

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _client = client;
        Site = site;
    }

    public void Disconnect()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch (Exception ex) when (ex is SshException or ObjectDisposedException)
        {
            // Already gone; nothing useful to do about it while closing.
        }

        _client.Dispose();
        _client = null;
        Site = null;
    }

    public Task<string> GetStartDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var wanted = Site?.InitialPath ?? string.Empty;

        return Task.Run(() =>
        {
            if (!string.IsNullOrWhiteSpace(wanted) && client.Exists(wanted))
            {
                return RemotePath.Normalize(wanted);
            }

            return RemotePath.Normalize(client.WorkingDirectory);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);

        return Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var entries = new List<FileSystemEntry>();

            foreach (var file in client.ListDirectory(path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.Name is "." or "..")
                {
                    continue;
                }

                entries.Add(ToEntry(file));
            }

            return entries;
        }, cancellationToken);
    }

    public Task DownloadAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        ArgumentException.ThrowIfNullOrEmpty(localPath);

        if (File.Exists(localPath) || Directory.Exists(localPath))
        {
            throw new IOException($"'{Path.GetFileName(localPath)}' already exists.");
        }

        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

            // A partial file left behind after a failure looks like a complete one, so it is removed.
            try
            {
                using var stream = new FileStream(localPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                client.DownloadFile(RemotePath.Normalize(remotePath), stream, downloaded => progress?.Report((long)downloaded));
            }
            catch
            {
                TryDelete(localPath);
                throw;
            }
        }, cancellationToken);
    }

    public Task UploadAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var client = Connected();

        if (!File.Exists(localPath))
        {
            throw new FileNotFoundException("The file to upload does not exist.", localPath);
        }

        return Task.Run(() =>
        {
            using var stream = File.OpenRead(localPath);
            client.UploadFile(stream, RemotePath.Normalize(remotePath), uploaded => progress?.Report((long)uploaded));
        }, cancellationToken);
    }

    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);

        return Task.Run(() =>
        {
            // The server creates one level at a time, so walk down building whatever is missing.
            var current = string.Empty;
            foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                cancellationToken.ThrowIfCancellationRequested();
                current += "/" + segment;

                if (!client.Exists(current))
                {
                    client.CreateDirectory(current);
                }
            }
        }, cancellationToken);
    }

    public Task DeleteAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);

        return Task.Run(() => DeleteRecursive(client, path, cancellationToken), cancellationToken);
    }

    public Task RenameAsync(string remotePath, string newName, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);
        var parent = RemotePath.GetParent(path) ?? RemotePath.Root;
        var target = RemotePath.Combine(parent, newName);

        return Task.Run(() =>
        {
            // Only a case change may land on an existing name: that is the same entry on a case-insensitive server.
            if (!string.Equals(path, target, StringComparison.OrdinalIgnoreCase) && client.Exists(target))
            {
                throw new IOException($"'{newName}' already exists in that folder.");
            }

            client.RenameFile(path, target);
        }, cancellationToken);
    }

    public Task CopyWithinServerAsync(string sourcePath, string destinationPath, bool move, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var source = RemotePath.Normalize(sourcePath);
        var destination = RemotePath.Normalize(destinationPath);

        return Task.Run(() =>
        {
            if (move)
            {
                client.RenameFile(source, destination);
                return;
            }

            CopyRecursive(client, source, destination, cancellationToken);
        }, cancellationToken);
    }

    public Task<long> GetSizeAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);

        return Task.Run(() => SizeOf(client, path, cancellationToken), cancellationToken);
    }

    public Task SetPermissionsAsync(string remotePath, UnixFileMode mode, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        var path = RemotePath.Normalize(remotePath);

        return Task.Run(() =>
        {
            var attributes = client.GetAttributes(path);
            attributes.SetPermissions(ToOctal(mode));
            client.SetAttributes(path, attributes);
        }, cancellationToken);
    }

    public bool Exists(string remotePath)
    {
        try
        {
            return Connected().Exists(RemotePath.Normalize(remotePath));
        }
        catch (Exception ex) when (ex is SshException or InvalidOperationException)
        {
            return false;
        }
    }

    public bool IsDirectory(string remotePath)
    {
        try
        {
            return Connected().GetAttributes(RemotePath.Normalize(remotePath)).IsDirectory;
        }
        catch (Exception ex) when (ex is SshException or InvalidOperationException)
        {
            return false;
        }
    }

    public Task<Stream> OpenReadAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var client = Connected();
        return Task.Run<Stream>(() => client.OpenRead(RemotePath.Normalize(remotePath)), cancellationToken);
    }

    public Task WriteAsync(Stream content, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var client = Connected();

        return Task.Run(() => client.UploadFile(content, RemotePath.Normalize(remotePath), uploaded => progress?.Report((long)uploaded)), cancellationToken);
    }

    private static void CopyRecursive(SftpClient client, string source, string destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!client.GetAttributes(source).IsDirectory)
        {
            // No server-side copy exists in the protocol, so the bytes come down and go back up.
            using var stream = new MemoryStream();
            client.DownloadFile(source, stream);
            stream.Position = 0;
            client.UploadFile(stream, destination, canOverride: true);
            return;
        }

        if (!client.Exists(destination))
        {
            client.CreateDirectory(destination);
        }

        foreach (var child in client.ListDirectory(source))
        {
            if (child.Name is "." or "..")
            {
                continue;
            }

            CopyRecursive(client, RemotePath.Combine(source, child.Name), RemotePath.Combine(destination, child.Name), cancellationToken);
        }
    }

    private static long SizeOf(SftpClient client, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attributes = client.GetAttributes(path);
        if (!attributes.IsDirectory)
        {
            return attributes.Size;
        }

        long total = 0;
        foreach (var child in client.ListDirectory(path))
        {
            if (child.Name is "." or "..")
            {
                continue;
            }

            total += SizeOf(client, RemotePath.Combine(path, child.Name), cancellationToken);
        }

        return total;
    }

    /// <summary>The mode as the three octal digits the protocol carries.</summary>
    private static short ToOctal(UnixFileMode mode)
    {
        var value = 0;
        if (mode.HasFlag(UnixFileMode.UserRead)) value |= 0x100;
        if (mode.HasFlag(UnixFileMode.UserWrite)) value |= 0x080;
        if (mode.HasFlag(UnixFileMode.UserExecute)) value |= 0x040;
        if (mode.HasFlag(UnixFileMode.GroupRead)) value |= 0x020;
        if (mode.HasFlag(UnixFileMode.GroupWrite)) value |= 0x010;
        if (mode.HasFlag(UnixFileMode.GroupExecute)) value |= 0x008;
        if (mode.HasFlag(UnixFileMode.OtherRead)) value |= 0x004;
        if (mode.HasFlag(UnixFileMode.OtherWrite)) value |= 0x002;
        if (mode.HasFlag(UnixFileMode.OtherExecute)) value |= 0x001;
        return (short)value;
    }

    public void Dispose() => Disconnect();

    private static void DeleteRecursive(SftpClient client, string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var attributes = client.GetAttributes(path);
        if (!attributes.IsDirectory)
        {
            client.DeleteFile(path);
            return;
        }

        foreach (var child in client.ListDirectory(path))
        {
            if (child.Name is "." or "..")
            {
                continue;
            }

            DeleteRecursive(client, RemotePath.Combine(path, child.Name), cancellationToken);
        }

        client.DeleteDirectory(path);
    }

    private static FileSystemEntry ToEntry(ISftpFile file)
    {
        var isDirectory = file.IsDirectory;

        return new FileSystemEntry
        {
            Name = file.Name,
            FullPath = RemotePath.Normalize(file.FullName),
            EntryType = isDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File,
            Size = isDirectory ? 0 : file.Length,
            LastModified = file.LastWriteTime,
            Created = file.LastWriteTime,
            LastAccessed = file.LastAccessTime,
            Extension = isDirectory ? string.Empty : Path.GetExtension(file.Name),
            Attributes = isDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            IsHidden = file.Name.StartsWith('.'),
            IsSymbolicLink = file.IsSymbolicLink,
            UnixPermissions = Permissions(file)
        };
    }

    private static string Permissions(ISftpFile file)
    {
        Span<char> chars = stackalloc char[9];
        chars[0] = file.OwnerCanRead ? 'r' : '-';
        chars[1] = file.OwnerCanWrite ? 'w' : '-';
        chars[2] = file.OwnerCanExecute ? 'x' : '-';
        chars[3] = file.GroupCanRead ? 'r' : '-';
        chars[4] = file.GroupCanWrite ? 'w' : '-';
        chars[5] = file.GroupCanExecute ? 'x' : '-';
        chars[6] = file.OthersCanRead ? 'r' : '-';
        chars[7] = file.OthersCanWrite ? 'w' : '-';
        chars[8] = file.OthersCanExecute ? 'x' : '-';
        return new string(chars);
    }

    private static ConnectionInfo BuildConnectionInfo(SftpSite site, string? secret)
    {
        if (site.Authentication == SftpAuthentication.Password)
        {
            if (string.IsNullOrEmpty(secret))
            {
                throw new ArgumentException("This site authenticates with a password, but none was supplied.", nameof(secret));
            }

            return new ConnectionInfo(site.Host, site.Port, site.Username, new PasswordAuthenticationMethod(site.Username, secret));
        }

        var keys = LoadKeys(site, secret);
        if (keys.Count == 0)
        {
            throw new FileNotFoundException("No usable private key was found. Set one on the site, or put a key in ~/.ssh.");
        }

        return new ConnectionInfo(site.Host, site.Port, site.Username, new PrivateKeyAuthenticationMethod(site.Username, [.. keys]));
    }

    private static List<PrivateKeyFile> LoadKeys(SftpSite site, string? passphrase)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(site.KeyPath))
        {
            candidates.Add(site.KeyPath);
        }
        else
        {
            var ssh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            candidates.AddRange(DefaultKeyNames.Select(name => Path.Combine(ssh, name)));
        }

        var keys = new List<PrivateKeyFile>();
        foreach (var candidate in candidates.Where(File.Exists))
        {
            try
            {
                keys.Add(string.IsNullOrEmpty(passphrase) ? new PrivateKeyFile(candidate) : new PrivateKeyFile(candidate, passphrase));
            }
            catch (Exception ex) when (ex is SshException or IOException or UnauthorizedAccessException)
            {
                // Encrypted with a different passphrase, or not a key at all: try the next candidate.
                AppLog.Warning($"The key '{candidate}' could not be read.", ex);
            }
        }

        return keys;
    }

    private SftpClient Connected()
    {
        return _client is { IsConnected: true } client
            ? client
            : throw new InvalidOperationException("Not connected to a server.");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The incomplete download '{path}' could not be removed.", ex);
        }
    }
}
