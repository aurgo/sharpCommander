using System.Diagnostics;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Cross-platform file system service implementation.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    public async Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return await GetDrivesAsync(cancellationToken);
        }

        return await Task.Run(() =>
        {
            var entries = new List<FileSystemEntry>();
            var directoryInfo = new DirectoryInfo(path);

            if (!directoryInfo.Exists)
            {
                return entries;
            }

            try
            {
                // Add directories first
                foreach (var dir in directoryInfo.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    entries.Add(new FileSystemEntry
                    {
                        Name = dir.Name,
                        FullPath = dir.FullName,
                        EntryType = FileSystemEntryType.Directory,
                        LastModified = dir.LastWriteTime
                    });
                }

                // Add files
                foreach (var file in directoryInfo.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    entries.Add(new FileSystemEntry
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        EntryType = FileSystemEntryType.File,
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied - return empty or partial list
            }
            catch (IOException)
            {
                // IO error - return empty or partial list
            }

            return entries;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<FileSystemEntry>>(() =>
        {
            var drives = new List<FileSystemEntry>();
            
            foreach (var drive in DriveInfo.GetDrives())
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                drives.Add(new FileSystemEntry
                {
                    Name = drive.Name,
                    FullPath = drive.Name,
                    EntryType = FileSystemEntryType.Drive,
                    IsReady = drive.IsReady,
                    Size = drive.IsReady ? drive.TotalSize : 0,
                    VolumeLabel = drive.IsReady ? drive.VolumeLabel : string.Empty,
                    DriveFormat = drive.IsReady ? drive.DriveFormat : string.Empty
                });
            }
            
            return drives;
        }, cancellationToken);
    }

    public async Task CopyAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(async () =>
        {
            if (File.Exists(source))
            {
                await CopyFileAsync(source, destination, overwrite, progress, cancellationToken);
            }
            else if (Directory.Exists(source))
            {
                await CopyDirectoryAsync(source, destination, overwrite, progress, cancellationToken);
            }
        }, cancellationToken);
    }

    private async Task CopyFileAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(source);
        var destPath = Path.Combine(destination, fileName);
        
        Directory.CreateDirectory(destination);
        
        progress?.Report(new FileOperationProgress
        {
            CurrentFile = source,
            State = FileOperationState.InProgress
        });

        await Task.Run(() => File.Copy(source, destPath, overwrite), cancellationToken);
        
        progress?.Report(new FileOperationProgress
        {
            CurrentFile = source,
            State = FileOperationState.Completed
        });
    }

    private async Task CopyDirectoryAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken)
    {
        var sourceInfo = new DirectoryInfo(source);
        var targetPath = Path.Combine(destination, sourceInfo.Name);
        
        Directory.CreateDirectory(targetPath);

        foreach (var dir in sourceInfo.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(dir.FullName, targetPath, overwrite, progress, cancellationToken);
        }

        foreach (var file in sourceInfo.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CopyFileAsync(file.FullName, targetPath, overwrite, progress, cancellationToken);
        }
    }

    public async Task MoveAsync(string source, string destination, bool overwrite, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = source,
                State = FileOperationState.InProgress
            });

            var fileName = Path.GetFileName(source);
            var destPath = Path.Combine(destination, fileName);
            
            if (File.Exists(source))
            {
                if (overwrite && File.Exists(destPath))
                {
                    File.Delete(destPath);
                }
                File.Move(source, destPath);
            }
            else if (Directory.Exists(source))
            {
                if (overwrite && Directory.Exists(destPath))
                {
                    Directory.Delete(destPath, true);
                }
                Directory.Move(source, destPath);
            }
            
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = source,
                State = FileOperationState.Completed
            });
        }, cancellationToken);
    }

    public async Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = path,
                State = FileOperationState.InProgress
            });

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            else if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            
            progress?.Report(new FileOperationProgress
            {
                CurrentFile = path,
                State = FileOperationState.Completed
            });
        }, cancellationToken);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Directory.CreateDirectory(path), cancellationToken);
    }

    public async Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }, cancellationToken);
    }

    public bool Exists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    public bool IsDirectory(string path)
    {
        return Directory.Exists(path);
    }

    public string? GetParentPath(string path)
    {
        return Directory.GetParent(path)?.FullName;
    }

    public string GetDefaultDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
