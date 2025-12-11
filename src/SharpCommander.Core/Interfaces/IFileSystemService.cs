using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Interface for file system operations.
/// </summary>
public interface IFileSystemService
{
    /// <summary>
    /// Gets entries in the specified directory.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all available drives.
    /// </summary>
    Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Copies a file or directory.
    /// </summary>
    Task CopyAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Moves a file or directory.
    /// </summary>
    Task MoveAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a new directory.
    /// </summary>
    Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Opens a file with the default application.
    /// </summary>
    Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a path exists.
    /// </summary>
    bool Exists(string path);
    
    /// <summary>
    /// Checks if a path is a directory.
    /// </summary>
    bool IsDirectory(string path);
    
    /// <summary>
    /// Gets the parent directory path.
    /// </summary>
    string? GetParentPath(string path);
    
    /// <summary>
    /// Gets the default starting directory.
    /// </summary>
    string GetDefaultDirectory();
    
    /// <summary>
    /// Opens the specified path in the system file explorer.
    /// </summary>
    Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default);
}
