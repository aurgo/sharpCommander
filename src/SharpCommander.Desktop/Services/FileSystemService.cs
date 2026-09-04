using System.Diagnostics;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Cross-platform file system service implementation. Listing and volumes are built by
/// <see cref="FileSystemEntryFactory"/> and <see cref="VolumeEnumerator"/>; copy, move and delete run through
/// <see cref="FileTransferEngine"/>.
/// </summary>
public sealed class FileSystemService : IFileSystemService
{
    /// <summary>Extensions that run code when opened.</summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".pif", ".vbs", ".vbe",
        ".js", ".jse", ".ws", ".wsf", ".wsc", ".wsh", ".ps1", ".ps1xml",
        ".ps2", ".ps2xml", ".psc1", ".psc2", ".msi", ".msp", ".reg", ".inf",
        ".sh", ".bash", ".zsh", ".command", ".run", ".appimage"
    };

    private const UnixFileMode AnyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    public async Task<IReadOnlyList<FileSystemEntry>> GetEntriesAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            return await GetDrivesAsync(cancellationToken);
        }

        return await Task.Run(() => ListDirectory(path, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetDrivesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => VolumeEnumerator.GetVolumes(cancellationToken), cancellationToken);
    }

    public Task CopyAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunTransferAsync(source, onConflict, progress, cancellationToken, engine => engine.CopyAsync(source, destinationDirectory));
    }

    public Task MoveAsync(string source, string destinationDirectory, ConflictResolver? onConflict = null, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        return RunTransferAsync(source, onConflict, progress, cancellationToken, engine => engine.MoveAsync(source, destinationDirectory));
    }

    public Task DeleteAsync(string path, IProgress<FileOperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return RunTransferAsync(path, null, progress, cancellationToken, engine => engine.DeleteAsync(path));
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Validate the segment as typed: normalization would silently resolve "." and "..".
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var error = PathUtils.ValidateFileName(name);
        if (error is not null)
        {
            throw new FileOperationException(FileOperationErrorKind.InvalidName, path, error);
        }

        await Task.Run(() => Directory.CreateDirectory(path), cancellationToken);
    }

    public async Task OpenWithDefaultAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

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
            using var process = Process.Start(startInfo);
        }, cancellationToken);
    }

    public bool IsExecutableOrScript(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (extension.Length > 0)
        {
            return ExecutableExtensions.Contains(extension);
        }

        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return (File.GetUnixFileMode(path) & AnyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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

    public async Task RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var error = PathUtils.ValidateFileName(newName);
        if (error is not null)
        {
            throw new FileOperationException(FileOperationErrorKind.InvalidName, path, error);
        }

        await Task.Run(() => Rename(path, newName), cancellationToken);
    }

    public Task OpenInFileExplorerAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            throw new FileNotFoundException("The specified path does not exist.", path);
        }

        // Files are shown inside their folder. So are macOS packages: "open" would launch an application
        // bundle or open a Pages document instead of showing the user where it lives.
        var reveal = !isDirectory || (OperatingSystem.IsMacOS() && IsMacPackage(path));
        return StartFileExplorerAsync(path, reveal, cancellationToken);
    }

    public Task RevealInFileExplorerAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException("The specified path does not exist.", path);
        }

        return StartFileExplorerAsync(path, reveal: true, cancellationToken);
    }

    private static Task StartFileExplorerAsync(string path, bool reveal, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var startInfo = CreateFileExplorerStartInfo(path, reveal);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the file manager.");
        }, cancellationToken);
    }

    public Task<long> GetDirectorySizeAsync(string path, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        return Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            long total = 0;
            var count = 0;

            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    total += file.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Vanished or unreadable: skip.
                }

                if (++count % 256 == 0)
                {
                    progress?.Report(total);
                }
            }

            progress?.Report(total);
            return total;
        }, cancellationToken);
    }

    private static IReadOnlyList<FileSystemEntry> ListDirectory(string path, CancellationToken cancellationToken)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"The folder '{path}' does not exist.");
        }

        var directories = new List<FileSystemEntry>();
        var files = new List<FileSystemEntry>();

        foreach (var info in directory.EnumerateFileSystemInfos())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = FileSystemEntryFactory.TryCreate(info);
            if (entry is null)
            {
                continue;
            }

            (entry.EntryType == FileSystemEntryType.Directory ? directories : files).Add(entry);
        }

        directories.Sort(CompareByName);
        files.Sort(CompareByName);

        var entries = new List<FileSystemEntry>(directories.Count + files.Count + 1);
        if (directory.Parent is not null)
        {
            entries.Add(FileSystemEntryFactory.CreateParent(directory.Parent));
        }

        entries.AddRange(directories);
        entries.AddRange(files);
        return entries;
    }

    private static int CompareByName(FileSystemEntry a, FileSystemEntry b)
    {
        return NaturalStringComparer.Instance.Compare(a.Name, b.Name);
    }

    private static void Rename(string path, string newName)
    {
        var fullPath = PathUtils.NormalizeFullPath(path);
        var isDirectory = Directory.Exists(fullPath);

        if (!isDirectory && !File.Exists(fullPath))
        {
            throw new FileOperationException(FileOperationErrorKind.NotFound, fullPath, $"'{fullPath}' no longer exists.");
        }

        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new FileOperationException(FileOperationErrorKind.InvalidName, fullPath, "A root folder cannot be renamed.");

        if (string.Equals(Path.GetFileName(fullPath), newName, StringComparison.Ordinal))
        {
            return;
        }

        var newPath = Path.Combine(parent, newName);

        // On case-insensitive platforms a name that differs only by case is the same entry and may be renamed.
        var sameEntry = PathUtils.AreSamePath(fullPath, newPath);
        if (!sameEntry && (File.Exists(newPath) || Directory.Exists(newPath)))
        {
            throw new FileOperationException(FileOperationErrorKind.TargetExists, newPath, $"'{newName}' already exists in this folder.");
        }

        if (isDirectory)
        {
            Directory.Move(fullPath, newPath);
        }
        else
        {
            File.Move(fullPath, newPath);
        }
    }

    /// <summary>
    /// Builds the command that opens <paramref name="path"/> in the system file manager. When
    /// <paramref name="reveal"/> is set the entry is selected inside its parent folder instead of being opened.
    /// </summary>
    private static ProcessStartInfo CreateFileExplorerStartInfo(string path, bool reveal)
    {
        if (OperatingSystem.IsWindows())
        {
            // explorer.exe parses "/select," itself, so this keeps the classic quoting.
            return new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = reveal ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = false
            };
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open") { UseShellExecute = false };
            if (reveal)
            {
                startInfo.ArgumentList.Add("-R");
            }

            startInfo.ArgumentList.Add(path);
            return startInfo;
        }

        if (OperatingSystem.IsLinux())
        {
            // xdg-open honors the user's default file manager but cannot select an entry, so revealing
            // falls back to opening the parent folder.
            var directory = reveal ? Path.GetDirectoryName(path) ?? path : path;
            var startInfo = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
            startInfo.ArgumentList.Add(directory);
            return startInfo;
        }

        throw new PlatformNotSupportedException("Opening the file manager is not supported on this platform.");
    }

    /// <summary>
    /// Directory extensions macOS treats as a single document or application. Opening one launches it,
    /// so the file manager must reveal it instead.
    /// </summary>
    private static readonly HashSet<string> MacPackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".app", ".bundle", ".framework", ".kext", ".plugin", ".component", ".prefpane", ".qlgenerator",
        ".saver", ".mdimporter", ".workflow", ".xcodeproj", ".xcworkspace", ".playground", ".rtfd",
        ".pages", ".numbers", ".key", ".photoslibrary", ".aplibrary", ".fcpbundle", ".band", ".logicx",
        ".sparsebundle", ".scptd", ".download", ".pkg", ".mpkg", ".dsym", ".imovielibrary", ".tvlibrary"
    };

    /// <summary>Returns true when the directory is a macOS package (known extension or a Contents/Info.plist).</summary>
    private static bool IsMacPackage(string directory)
    {
        if (MacPackageExtensions.Contains(Path.GetExtension(directory)))
        {
            return true;
        }

        try
        {
            return File.Exists(Path.Combine(directory, "Contents", "Info.plist"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs one engine operation on the thread pool, reports its terminal state and classifies the
    /// well-known .NET exceptions as <see cref="FileOperationException"/> so callers can show a path.
    /// </summary>
    private static async Task RunTransferAsync(string itemPath, ConflictResolver? onConflict, IProgress<FileOperationProgress>? progress, CancellationToken cancellationToken, Func<FileTransferEngine, Task> operation)
    {
        var engine = new FileTransferEngine(onConflict, progress, cancellationToken);

        try
        {
            await Task.Run(() => operation(engine), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            engine.ReportState(FileOperationState.Cancelled);
            throw;
        }
        catch (FileOperationException)
        {
            engine.ReportState(FileOperationState.Failed);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            engine.ReportState(FileOperationState.Failed);
            throw new FileOperationException(FileOperationErrorKind.AccessDenied, CurrentFileOr(engine, itemPath), ex.Message, ex);
        }
        catch (FileNotFoundException ex)
        {
            engine.ReportState(FileOperationState.Failed);
            throw new FileOperationException(FileOperationErrorKind.NotFound, ex.FileName ?? CurrentFileOr(engine, itemPath), ex.Message, ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            engine.ReportState(FileOperationState.Failed);
            throw new FileOperationException(FileOperationErrorKind.NotFound, CurrentFileOr(engine, itemPath), ex.Message, ex);
        }
        catch (Exception)
        {
            engine.ReportState(FileOperationState.Failed);
            throw;
        }
    }

    private static string CurrentFileOr(FileTransferEngine engine, string fallback)
    {
        return string.IsNullOrEmpty(engine.CurrentFile) ? fallback : engine.CurrentFile;
    }
}
