using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Moves entries to the platform trash: the recycle bin through SHFileOperation on Windows, the Finder (with
/// ~/.Trash as fallback) on macOS, and "gio trash" (with the freedesktop.org Trash specification as fallback)
/// on Linux.
/// </summary>
public sealed class TrashService : ITrashService
{
    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    public async Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = PathUtils.NormalizeFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileOperationException(FileOperationErrorKind.NotFound, fullPath, $"'{fullPath}' does not exist.");
        }

        if (OperatingSystem.IsWindows())
        {
            await Task.Run(() => MoveToRecycleBin(fullPath), cancellationToken);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await MoveToTrashMacAsync(fullPath, cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await MoveToTrashLinuxAsync(fullPath, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("The trash is not supported on this platform.");
        }
    }

    /// <summary>Windows only; the explicit guard lets the platform analyzer see the call is safe.</summary>
    private static void MoveToRecycleBin(string fullPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The recycle bin is only available on Windows.");
        }

        WindowsRecycleBin.Delete(fullPath);
    }

    // ---- macOS ------------------------------------------------------------------------------------------

    private static async Task MoveToTrashMacAsync(string fullPath, CancellationToken cancellationToken)
    {
        var script = $"tell application \"Finder\" to delete POSIX file \"{EscapeAppleScriptString(fullPath)}\"";
        if (await RunProcessAsync("osascript", ["-e", script], cancellationToken))
        {
            return;
        }

        var trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
        await Task.Run(() => MoveToUserTrashFolder(fullPath, trash), cancellationToken);
    }

    /// <summary>
    /// Fallback used when Finder automation is unavailable: moves the entry into the user's trash folder under
    /// a unique name. Returns the new location.
    /// </summary>
    internal static string MoveToUserTrashFolder(string fullPath, string trashDirectory)
    {
        Directory.CreateDirectory(trashDirectory);
        var name = PathUtils.GetUniqueName(trashDirectory, Path.GetFileName(fullPath));
        var target = Path.Combine(trashDirectory, name);
        MoveEntry(fullPath, target);
        return target;
    }

    private static string EscapeAppleScriptString(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    // ---- Linux ------------------------------------------------------------------------------------------

    private static async Task MoveToTrashLinuxAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (await RunProcessAsync("gio", ["trash", "--", fullPath], cancellationToken))
        {
            return;
        }

        await Task.Run(() => MoveToFreedesktopTrash(fullPath, GetFreedesktopTrashRoot()), cancellationToken);
    }

    /// <summary>
    /// Implements the freedesktop.org Trash specification: writes info/&lt;name&gt;.trashinfo first, then moves the
    /// entry to files/&lt;name&gt;. Returns the new location.
    /// </summary>
    internal static string MoveToFreedesktopTrash(string fullPath, string trashRoot)
    {
        var filesDirectory = Path.Combine(trashRoot, "files");
        var infoDirectory = Path.Combine(trashRoot, "info");
        Directory.CreateDirectory(filesDirectory);
        Directory.CreateDirectory(infoDirectory);

        var name = FindFreeTrashName(filesDirectory, infoDirectory, Path.GetFileName(fullPath));
        var infoPath = Path.Combine(infoDirectory, name + ".trashinfo");
        var info = new StringBuilder()
            .Append("[Trash Info]\n")
            .Append("Path=").Append(EncodeTrashPath(fullPath)).Append('\n')
            .Append("DeletionDate=").Append(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append('\n')
            .ToString();

        // CreateNew claims the name atomically, as the specification requires.
        using (var stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = Encoding.UTF8.GetBytes(info);
            stream.Write(bytes, 0, bytes.Length);
        }

        var target = Path.Combine(filesDirectory, name);
        try
        {
            MoveEntry(fullPath, target);
        }
        catch
        {
            File.Delete(infoPath);
            throw;
        }

        return target;
    }

    private static string GetFreedesktopTrashRoot()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(dataHome, "Trash");
    }

    private static string FindFreeTrashName(string filesDirectory, string infoDirectory, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        if (stem.Length == 0)
        {
            stem = name;
            extension = string.Empty;
        }

        for (var counter = 1; ; counter++)
        {
            var candidate = counter == 1 ? name : $"{stem} ({counter}){extension}";
            var filePath = Path.Combine(filesDirectory, candidate);
            var infoPath = Path.Combine(infoDirectory, candidate + ".trashinfo");

            if (!File.Exists(filePath) && !Directory.Exists(filePath) && !File.Exists(infoPath))
            {
                return candidate;
            }
        }
    }

    /// <summary>Percent-encodes each path segment, keeping the separators, as the specification asks.</summary>
    internal static string EncodeTrashPath(string fullPath)
    {
        var segments = fullPath.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }

        return string.Join('/', segments);
    }

    // ---- shared -----------------------------------------------------------------------------------------

    private static void MoveEntry(string fullPath, string target)
    {
        if (Directory.Exists(fullPath))
        {
            Directory.Move(fullPath, target);
        }
        else
        {
            File.Move(fullPath, target);
        }
    }

    /// <summary>Runs a helper process; returns true on exit code 0 and false when it fails or is not installed.</summary>
    private static async Task<bool> RunProcessAsync(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // Drain both pipes so a chatty helper cannot block on a full buffer.
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            // Surfacing stderr makes a denied automation permission visible: without
            // NSAppleEventsUsageDescription (or with the consent refused) macOS fails the Apple event with
            // errAEEventNotPermitted (-1743) and every delete would otherwise take the fallback in silence.
            var message = (await stderr).Trim();
            AppLog.Warning(message.Length == 0
                ? $"{fileName} exited with code {process.ExitCode}; using the fallback."
                : $"{fileName} exited with code {process.ExitCode}: {message}");

            return false;
        }
        catch (Win32Exception)
        {
            // The helper is not installed.
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited.
        }
    }

    // ---- Windows ----------------------------------------------------------------------------------------

    /// <summary>
    /// SHFileOperationW with FOF_ALLOWUNDO. The struct is blittable (IntPtr fields, string marshalled by hand)
    /// so the P/Invoke needs no runtime marshalling and stays AOT-safe. Layout matches the 64-bit definition;
    /// 32-bit Windows would need Pack = 1, which the published builds do not target.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static class WindowsRecycleBin
    {
        private const uint FO_DELETE = 3;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        [DllImport("shell32.dll", EntryPoint = "SHFileOperationW", ExactSpelling = true)]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

        public static void Delete(string fullPath)
        {
            // pFrom is a double-null-terminated list; StringToHGlobalUni adds the second terminator.
            var from = Marshal.StringToHGlobalUni(fullPath + "\0");
            try
            {
                var operation = new SHFILEOPSTRUCTW
                {
                    wFunc = FO_DELETE,
                    pFrom = from,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING
                };

                var result = SHFileOperationW(ref operation);
                if (result != 0)
                {
                    throw new IOException($"The recycle bin refused '{fullPath}' (SHFileOperation error 0x{result:X}).", result);
                }

                if (operation.fAnyOperationsAborted != 0)
                {
                    throw new OperationCanceledException("Moving to the recycle bin was aborted.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(from);
            }
        }
    }
}
