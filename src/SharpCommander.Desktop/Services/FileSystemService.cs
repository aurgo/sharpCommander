using System.Buffers.Binary;
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
        ".sh", ".bash", ".zsh", ".command", ".tool", ".run", ".appimage",
        ".jar", ".lnk", ".hta", ".cpl", ".msc"
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
            using var process = Process.Start(CreateOpenStartInfo(path));
        }, cancellationToken);
    }

    /// <summary>
    /// Builds the command that opens an entry with its default handler. On Unix the platform opener is invoked
    /// by name instead of letting UseShellExecute decide: .NET runs any file whose execute bit is set directly,
    /// which would start a program without the confirmation <see cref="IsExecutableOrScript"/> exists to prompt
    /// for, and would exec every file on a FAT or NTFS-3g mount before falling back to the opener.
    /// </summary>
    private static ProcessStartInfo CreateOpenStartInfo(string path)
    {
        var workingDirectory = Path.GetDirectoryName(path) ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            };
        }

        var opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        var startInfo = new ProcessStartInfo(opener)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        startInfo.ArgumentList.Add(path);
        return startInfo;
    }

    public bool IsExecutableOrScript(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        if (ExecutableExtensions.Contains(extension))
        {
            return true;
        }

        // On Unix the extension does not decide: the kernel runs anything with the execute bit, so an extension
        // the list does not know (hello.py, tool.x86_64, an ELF renamed foo.1) is still a program. The magic
        // number keeps this from flagging every file on a FAT or NTFS-3g mount, where everything is mode 0777.
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return false;
        }

        try
        {
            return (File.GetUnixFileMode(path) & AnyExecute) != 0
                   && (extension.Length == 0 || HasExecutableHeader(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads the first bytes of a file and reports whether they are a shebang, an ELF image or a Mach-O image
    /// (thin or universal, either byte order). Anything else is data that merely carries the execute bit.
    /// </summary>
    private static bool HasExecutableHeader(string path)
    {
        Span<byte> header = stackalloc byte[4];

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return false;
            }
        }

        if (header[0] == '#' && header[1] == '!')
        {
            return true;
        }

        var magic = BinaryPrimitives.ReadUInt32BigEndian(header);

        return magic is 0x7F454C46 // ELF
            or 0xFEEDFACE or 0xFEEDFACF // Mach-O, 32 and 64 bit
            or 0xCEFAEDFE or 0xCFFAEDFE // the same, byte swapped
            or 0xCAFEBABE or 0xBEBAFECA; // Mach-O universal binary
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

    public Task<int> ApplyAttributesAsync(IReadOnlyList<string> paths, AttributeChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(change);

        if (change.IsEmpty)
        {
            return Task.FromResult(0);
        }

        return Task.Run(() =>
        {
            var changed = 0;

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                changed += ApplyTo(path, change, cancellationToken);
            }

            return changed;
        }, cancellationToken);
    }

    private static int ApplyTo(string path, AttributeChange change, CancellationToken cancellationToken)
    {
        var changed = 0;
        var isDirectory = Directory.Exists(path);

        if (!isDirectory && !File.Exists(path))
        {
            return 0;
        }

        try
        {
            var attributes = File.GetAttributes(path);
            var wanted = attributes;

            wanted = Toggle(wanted, FileAttributes.ReadOnly, change.ReadOnly);
            wanted = Toggle(wanted, FileAttributes.Hidden, change.Hidden);
            wanted = Toggle(wanted, FileAttributes.Archive, change.Archive);
            wanted = Toggle(wanted, FileAttributes.System, change.System);

            if (wanted != attributes)
            {
                File.SetAttributes(path, wanted);
                changed++;
            }

            // Unix permissions are a separate call and only exist off Windows.
            if (change.UnixMode is { } mode && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, mode);
                changed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Protected or vanished: counted as unchanged, the caller reports the shortfall.
            AppLog.Warning($"The attributes of '{path}' could not be changed.", ex);
        }

        if (!change.Recursive || !isDirectory)
        {
            return changed;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.None
        };

        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            changed += ApplyTo(child, change, cancellationToken);
        }

        return changed;
    }

    private static FileAttributes Toggle(FileAttributes attributes, FileAttributes flag, bool? wanted)
    {
        return wanted switch
        {
            true => attributes | flag,
            false => attributes & ~flag,
            _ => attributes
        };
    }

    public Task<long> FindFirstDifferenceAsync(string leftPath, string rightPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(leftPath);
        ArgumentException.ThrowIfNullOrEmpty(rightPath);

        return Task.Run(async () =>
        {
            // The same file compared with itself is identical without reading a byte.
            if (PathUtils.AreSamePath(leftPath, rightPath))
            {
                return -1L;
            }

            const int BufferSize = 64 * 1024;

            await using var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
            await using var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);

            var leftBuffer = new byte[BufferSize];
            var rightBuffer = new byte[BufferSize];
            long offset = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var leftRead = await ReadBlockAsync(left, leftBuffer, cancellationToken);
                var rightRead = await ReadBlockAsync(right, rightBuffer, cancellationToken);
                var common = Math.Min(leftRead, rightRead);

                var difference = leftBuffer.AsSpan(0, common).CommonPrefixLength(rightBuffer.AsSpan(0, common));
                if (difference < common)
                {
                    return offset + difference;
                }

                // One ended before the other: the shorter file is a prefix of the longer one.
                if (leftRead != rightRead)
                {
                    return offset + common;
                }

                if (leftRead == 0)
                {
                    return -1L;
                }

                offset += leftRead;
            }
        }, cancellationToken);
    }

    /// <summary>Fills the buffer as far as the stream allows; a short read does not mean end of file.</summary>
    private static async Task<int> ReadBlockAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    public Task OpenTerminalAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"The folder '{directory}' does not exist.");
        }

        return Task.Run(() =>
        {
            foreach (var startInfo in TerminalCandidates(directory))
            {
                try
                {
                    using var process = Process.Start(startInfo);
                    if (process is not null)
                    {
                        return;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Not installed on this machine: try the next one.
                }
            }

            throw new InvalidOperationException("No terminal emulator could be started.");
        }, cancellationToken);
    }

    /// <summary>
    /// The terminals to try, best first. Linux has no single answer, so the common emulators are attempted in
    /// turn and the first one that starts wins.
    /// </summary>
    private static IEnumerable<ProcessStartInfo> TerminalCandidates(string directory)
    {
        if (OperatingSystem.IsMacOS())
        {
            yield return new ProcessStartInfo("open", ["-a", "Terminal", directory]) { UseShellExecute = false };
            yield break;
        }

        if (OperatingSystem.IsWindows())
        {
            // Windows Terminal when present, the console host otherwise.
            yield return new ProcessStartInfo("wt.exe", ["-d", directory]) { UseShellExecute = false };
            yield return new ProcessStartInfo("cmd.exe") { WorkingDirectory = directory, UseShellExecute = true };
            yield break;
        }

        foreach (var terminal in new[] { "x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty", "kitty", "xterm" })
        {
            yield return new ProcessStartInfo(terminal) { WorkingDirectory = directory, UseShellExecute = false };
        }
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

        // A name that differs only by case is a case-only rename of the same entry, whatever the platform
        // comparison says: the mount decides, not the OS. Testing Exists here would refuse it on a
        // case-insensitive volume mounted on Linux (vfat, exFAT, NTFS-3g, a casefold directory), where the
        // comparison is ordinal but the kernel resolves both names to the same file. File.Move and
        // Directory.Move perform the rename on either kind of file system and still refuse to clobber a
        // genuinely different entry.
        var caseOnlyRename = string.Equals(Path.GetFileName(fullPath), newName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && (File.Exists(newPath) || Directory.Exists(newPath)))
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
