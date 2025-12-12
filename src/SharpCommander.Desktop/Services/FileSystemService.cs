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
                // Add parent directory entry ".." if not at root
                if (directoryInfo.Parent != null)
                {
                    entries.Add(new FileSystemEntry
                    {
                        Name = "..",
                        FullPath = directoryInfo.Parent.FullName,
                        EntryType = FileSystemEntryType.ParentDirectory,
                        LastModified = directoryInfo.Parent.LastWriteTime
                    });
                }

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

    /// <summary>
    /// Set of file extensions considered potentially dangerous.
    /// </summary>
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".pif", ".vbs", ".vbe",
        ".js", ".jse", ".ws", ".wsf", ".wsc", ".wsh", ".ps1", ".ps1xml",
        ".ps2", ".ps2xml", ".psc1", ".psc2", ".msi", ".msp", ".reg", ".inf"
    };

    /// <summary>
    /// Checks if a file extension is considered potentially dangerous.
    /// </summary>
    public static bool IsPotentiallyDangerous(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && DangerousExtensions.Contains(extension);
    }

    public async Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Validate path exists and is not trying to escape
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("The specified path does not exist.", path);
        }

        await Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                // Set working directory to the file's directory, not the app's directory
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
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

    public async Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Ensure the path exists
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("The specified path does not exist.", path);
        }

        await Task.Run(() =>
        {
            try
            {
                ProcessStartInfo startInfo;
                
                // Determine if path is a file or directory
                var isDirectory = Directory.Exists(path);
                
                if (OperatingSystem.IsWindows())
                {
                    // On Windows, use explorer.exe
                    if (isDirectory)
                    {
                        // Open the directory
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"\"{path}\"",
                            UseShellExecute = false
                        };
                    }
                    else
                    {
                        // Select the file in explorer
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{path}\"",
                            UseShellExecute = false
                        };
                    }
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // On macOS, use open command
                    if (isDirectory)
                    {
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "open",
                            Arguments = $"\"{path}\"",
                            UseShellExecute = false
                        };
                    }
                    else
                    {
                        // Reveal in Finder
                        startInfo = new ProcessStartInfo
                        {
                            FileName = "open",
                            Arguments = $"-R \"{path}\"",
                            UseShellExecute = false
                        };
                    }
                }
                else if (OperatingSystem.IsLinux())
                {
                    // On Linux, try common file managers
                    var fileManager = FindLinuxFileManager();
                    
                    if (isDirectory)
                    {
                        startInfo = new ProcessStartInfo
                        {
                            FileName = fileManager,
                            Arguments = $"\"{path}\"",
                            UseShellExecute = false
                        };
                    }
                    else
                    {
                        // Open the parent directory (most Linux file managers don't support select)
                        var directory = Path.GetDirectoryName(path) ?? path;
                        startInfo = new ProcessStartInfo
                        {
                            FileName = fileManager,
                            Arguments = $"\"{directory}\"",
                            UseShellExecute = false
                        };
                    }
                }
                else
                {
                    throw new PlatformNotSupportedException("Opening file explorer is not supported on this platform.");
                }

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start file explorer process.");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to open file explorer: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private static string FindLinuxFileManager()
    {
        // Try to find a file manager in order of preference
        var fileManagers = new[] { "nautilus", "dolphin", "thunar", "nemo", "caja", "pcmanfm", "xdg-open" };
        
        foreach (var fm in fileManagers)
        {
            try
            {
                using var which = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = fm,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                if (which != null)
                {
                    // Wait up to 1 second for the which command to complete
                    if (which.WaitForExit(1000) && which.ExitCode == 0)
                    {
                        return fm;
                    }
                }
            }
            catch
            {
                // Continue to next file manager
            }
        }
        
        // Fallback to xdg-open which should be available on most Linux systems
        return "xdg-open";
    }
}
