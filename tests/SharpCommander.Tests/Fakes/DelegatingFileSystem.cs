using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>
/// Forwards to a real service but lets a test hold or fail a listing, hold a copy, move or folder measurement
/// (to observe a running operation) and records files "opened" instead of launching them.
/// </summary>
public sealed class DelegatingFileSystem(IFileSystemService inner) : IFileSystemService
{
    /// <summary>Awaited before every listing; return a pending task to hold it.</summary>
    public Func<string?, Task>? BeforeList { get; set; }

    /// <summary>When set, every listing throws this exception instead of running.</summary>
    public Exception? ListFailure { get; set; }

    /// <summary>Awaited (with the operation token) before every copy or move, e.g. to keep a batch running.</summary>
    public Func<string, CancellationToken, Task>? BeforeTransfer { get; set; }

    /// <summary>Awaited (with the token) before a folder is measured.</summary>
    public Func<string, CancellationToken, Task>? BeforeDirectorySize { get; set; }

    /// <summary>Paths handed to <see cref="OpenWithDefaultAsync"/>, which never launches anything.</summary>
    public List<string> Opened { get; } = [];

    /// <summary>The cancellation token of every listing, in order, so a test can see which ones were cancelled.</summary>
    public List<CancellationToken> ListingTokens { get; } = [];

    public async Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default)
    {
        lock (ListingTokens)
        {
            ListingTokens.Add(cancellationToken);
        }

        if (BeforeList is { } gate)
        {
            await gate(path);
        }

        if (ListFailure is { } failure)
        {
            throw failure;
        }

        return await inner.GetEntriesAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default) => inner.GetDrivesAsync(cancellationToken);

    public async Task CopyAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (BeforeTransfer is { } gate)
        {
            await gate(source, cancellationToken);
        }

        await inner.CopyAsync(source, destinationDirectory, onConflict, progress, cancellationToken);
    }

    public async Task MoveAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (BeforeTransfer is { } gate)
        {
            await gate(source, cancellationToken);
        }

        await inner.MoveAsync(source, destinationDirectory, onConflict, progress, cancellationToken);
    }

    public Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default) => inner.DeleteAsync(path, progress, cancellationToken);

    public Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default) => inner.CreateDirectoryAsync(path, cancellationToken);

    public Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default)
    {
        Opened.Add(path);
        return Task.CompletedTask;
    }

    public bool IsExecutableOrScript(string path) => inner.IsExecutableOrScript(path);

    public bool Exists(string path) => inner.Exists(path);

    public bool IsDirectory(string path) => inner.IsDirectory(path);

    public string? GetParentPath(string path) => inner.GetParentPath(path);

    public string GetDefaultDirectory() => inner.GetDefaultDirectory();

    public Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default) => inner.RenameAsync(path, newName, cancellationToken);

    public Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default) => inner.OpenInFileExplorerAsync(path, cancellationToken);
    public Task RevealInFileExplorerAsync(string path, CancellationToken cancellationToken = default) => inner.RevealInFileExplorerAsync(path, cancellationToken);

    public async Task<long> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        if (BeforeDirectorySize is { } gate)
        {
            await gate(path, cancellationToken);
        }

        return await inner.GetDirectorySizeAsync(path, progress, cancellationToken);
    }
}
