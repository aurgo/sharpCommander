using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Tests.Fakes;

/// <summary>
/// An in-memory server standing in for a real one. It exists to test the routing — which side of the boundary an
/// operation lands on, and that files crossing it arrive intact — not the SFTP protocol, which is SSH.NET's job
/// and needs a real host to exercise.
/// </summary>
public sealed class FakeSftpServer : ISftpService
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "/" };

    public bool IsConnected { get; private set; } = true;

    public SftpSite? Site { get; private set; } = new() { Host = "example.com", Username = "ana", Port = 22 };

    public string StartDirectory { get; set; } = "/home/ana";

    public void AddFile(string path, string content)
    {
        var normalized = RemotePath.Normalize(path);
        _files[normalized] = System.Text.Encoding.UTF8.GetBytes(content);
        AddDirectory(RemotePath.GetParent(normalized) ?? "/");
    }

    public void AddDirectory(string path)
    {
        var normalized = RemotePath.Normalize(path);
        var current = string.Empty;
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += "/" + segment;
            _directories.Add(current);
        }
    }

    public string ReadFile(string path) => System.Text.Encoding.UTF8.GetString(_files[RemotePath.Normalize(path)]);

    public bool HasFile(string path) => _files.ContainsKey(RemotePath.Normalize(path));

    public bool HasDirectory(string path) => _directories.Contains(RemotePath.Normalize(path));

    public Task ConnectAsync(SftpSite site, string? password, CancellationToken cancellationToken = default)
    {
        Site = site;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        IsConnected = false;
        Site = null;
    }

    public Task<string> GetStartDirectoryAsync(CancellationToken cancellationToken = default) => Task.FromResult(StartDirectory);

    public Task<IReadOnlyList<FileSystemEntry>> ListAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);
        var entries = new List<FileSystemEntry>();

        foreach (var directory in _directories.Where(d => d != "/" && RemotePath.GetParent(d) == path))
        {
            entries.Add(Entry(directory, FileSystemEntryType.Directory, 0));
        }

        foreach (var (file, content) in _files.Where(f => RemotePath.GetParent(f.Key) == path))
        {
            entries.Add(Entry(file, FileSystemEntryType.File, content.Length));
        }

        return Task.FromResult<IReadOnlyList<FileSystemEntry>>(entries);
    }

    public Task DownloadAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);
        if (!_files.TryGetValue(path, out var content))
        {
            throw new FileNotFoundException("No such remote file.", path);
        }

        if (File.Exists(localPath))
        {
            throw new IOException("Already exists.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        return File.WriteAllBytesAsync(localPath, content, cancellationToken);
    }

    public async Task UploadAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllBytesAsync(localPath, cancellationToken);
        AddDirectory(RemotePath.GetParent(RemotePath.Normalize(remotePath)) ?? "/");
        _files[RemotePath.Normalize(remotePath)] = content;
    }

    public Task CreateDirectoryAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        AddDirectory(remotePath);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);
        _files.Remove(path);
        _directories.Remove(path);

        foreach (var child in _files.Keys.Where(f => f.StartsWith(path + "/", StringComparison.Ordinal)).ToList())
        {
            _files.Remove(child);
        }

        foreach (var child in _directories.Where(d => d.StartsWith(path + "/", StringComparison.Ordinal)).ToList())
        {
            _directories.Remove(child);
        }

        return Task.CompletedTask;
    }

    public Task RenameAsync(string remotePath, string newName, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);
        var target = RemotePath.Combine(RemotePath.GetParent(path) ?? "/", newName);

        if (_files.Remove(path, out var content))
        {
            _files[target] = content;
        }
        else if (_directories.Remove(path))
        {
            _directories.Add(target);
        }

        return Task.CompletedTask;
    }

    public Task CopyWithinServerAsync(string sourcePath, string destinationPath, bool move, CancellationToken cancellationToken = default)
    {
        var source = RemotePath.Normalize(sourcePath);
        var destination = RemotePath.Normalize(destinationPath);

        if (_files.TryGetValue(source, out var content))
        {
            _files[destination] = content;
            if (move)
            {
                _files.Remove(source);
            }

            return Task.CompletedTask;
        }

        AddDirectory(destination);
        foreach (var (file, data) in _files.Where(f => f.Key.StartsWith(source + "/", StringComparison.Ordinal)).ToList())
        {
            _files[destination + file[source.Length..]] = data;
            if (move)
            {
                _files.Remove(file);
            }
        }

        if (move)
        {
            _directories.Remove(source);
        }

        return Task.CompletedTask;
    }

    public Task<long> GetSizeAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        var path = RemotePath.Normalize(remotePath);

        if (_files.TryGetValue(path, out var content))
        {
            return Task.FromResult((long)content.Length);
        }

        return Task.FromResult(_files
            .Where(f => f.Key.StartsWith(path + "/", StringComparison.Ordinal))
            .Sum(f => (long)f.Value.Length));
    }

    public Task SetPermissionsAsync(string remotePath, UnixFileMode mode, CancellationToken cancellationToken = default)
    {
        Permissions[RemotePath.Normalize(remotePath)] = mode;
        return Task.CompletedTask;
    }

    /// <summary>What SetPermissionsAsync was asked for, so a test can check the routing reached the server.</summary>
    public Dictionary<string, UnixFileMode> Permissions { get; } = new(StringComparer.Ordinal);

    public bool Exists(string remotePath)
    {
        var path = RemotePath.Normalize(remotePath);
        return _files.ContainsKey(path) || _directories.Contains(path);
    }

    public bool IsDirectory(string remotePath) => _directories.Contains(RemotePath.Normalize(remotePath));

    public Task<Stream> OpenReadAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<Stream>(new MemoryStream(_files[RemotePath.Normalize(remotePath)]));
    }

    public async Task WriteAsync(Stream content, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        AddDirectory(RemotePath.GetParent(RemotePath.Normalize(remotePath)) ?? "/");
        _files[RemotePath.Normalize(remotePath)] = buffer.ToArray();
    }

    public void Dispose() => Disconnect();

    private static FileSystemEntry Entry(string path, FileSystemEntryType type, long size) => new()
    {
        Name = RemotePath.GetName(path),
        FullPath = path,
        EntryType = type,
        Size = size,
        LastModified = new DateTime(2026, 1, 1)
    };
}
