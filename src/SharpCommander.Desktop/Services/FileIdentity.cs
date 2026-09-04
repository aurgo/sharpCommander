using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Identifies a file system entry the way the operating system does, by volume and file number (device and
/// inode on Unix, volume serial number and file index on Windows), so two paths that name one entry through an
/// alias (a symbolic link, a junction, a mapped drive, a bind mount, a macOS firmlink such as
/// /System/Volumes/Data/..., or another letter case on a case-insensitive volume) compare equal although their
/// strings differ. Every lookup returns null when the entry cannot be examined, so callers can fall back to path
/// comparison. The Unix side calls the runtime's own native shim (libSystem.Native), which ships with every
/// .NET runtime and self-contained publish and hides the per-platform layout of struct stat.
/// </summary>
internal readonly record struct FileIdentity(ulong Volume, ulong Index)
{
    /// <summary>
    /// Gets the identity of the entry at <paramref name="path"/>: of the entry itself (a link included) by
    /// default, or of what the last path segment points to when <paramref name="followLinks"/> is set.
    /// Intermediate links are always resolved. Null when the entry does not exist or cannot be examined.
    /// </summary>
    public static FileIdentity? TryGet(string path, bool followLinks = false)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Windows.Get(path, followLinks);
            }

            return Unix.Get(path, followLinks);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when both paths name the same entry (links themselves, not what they point to).</summary>
    public static bool AreSameEntry(string a, string b)
    {
        return TryGet(a) is { } first && TryGet(b) is { } second && first == second;
    }

    /// <summary>
    /// True when an entry exists at <paramref name="other"/> and it is not the entry at <paramref name="path"/>
    /// (for example a distinct "README.txt" next to "readme.txt" on a case-sensitive file system).
    /// </summary>
    public static bool IsDifferentEntry(string path, string other)
    {
        return TryGet(other) is { } existing && TryGet(path) is { } self && existing != self;
    }

    /// <summary>
    /// Tells whether the entry at <paramref name="entryPath"/> lives on the same volume as the directory at
    /// <paramref name="directoryPath"/> (the directory is resolved through a link, the entry is not, because a
    /// move renames the entry itself into the resolved directory). Null when either side cannot be examined.
    /// </summary>
    public static bool? OnSameVolume(string entryPath, string directoryPath)
    {
        if (TryGet(entryPath) is not { } entry || TryGet(directoryPath, followLinks: true) is not { } directory)
        {
            return null;
        }

        return entry.Volume == directory.Volume;
    }

    /// <summary>
    /// Tells whether the directory <paramref name="candidatePath"/> resolves to <paramref name="ancestorDirectory"/>
    /// itself or lies anywhere below it, walking up the candidate one segment at a time and comparing identities,
    /// so links and aliases in any segment are seen through. Null when it cannot be decided.
    /// </summary>
    public static bool? IsSameOrInside(string ancestorDirectory, string candidatePath)
    {
        if (TryGet(ancestorDirectory, followLinks: true) is not { } ancestor)
        {
            return null;
        }

        var current = PathUtils.NormalizeFullPath(candidatePath);
        var examined = false;

        while (true)
        {
            if (TryGet(current, followLinks: true) is { } identity)
            {
                examined = true;
                if (identity == ancestor)
                {
                    return true;
                }
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                return examined ? false : null;
            }

            current = parent;
        }
    }

    // ---- Unix -------------------------------------------------------------------------------------------

    private static class Unix
    {
        private const string SystemNative = "libSystem.Native";

        /// <summary>Layout of the runtime's FileStatus (src/native/libs/System.Native/pal_io.h); only Dev and Ino are read.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct FileStatus
        {
            public int Flags;
            public int Mode;
            public uint Uid;
            public uint Gid;
            public long Size;
            public long ATime;
            public long ATimeNsec;
            public long MTime;
            public long MTimeNsec;
            public long CTime;
            public long CTimeNsec;
            public long BirthTime;
            public long BirthTimeNsec;
            public long Dev;
            public long RDev;
            public long Ino;
            public uint UserFlags;
        }

        [DllImport(SystemNative, EntryPoint = "SystemNative_Stat", ExactSpelling = true)]
        private static extern int Stat(IntPtr path, ref FileStatus output);

        [DllImport(SystemNative, EntryPoint = "SystemNative_LStat", ExactSpelling = true)]
        private static extern int LStat(IntPtr path, ref FileStatus output);

        public static FileIdentity? Get(string path, bool followLinks)
        {
            var native = Marshal.StringToCoTaskMemUTF8(path);
            try
            {
                var status = default(FileStatus);
                var result = followLinks ? Stat(native, ref status) : LStat(native, ref status);
                return result == 0 ? new FileIdentity(unchecked((ulong)status.Dev), unchecked((ulong)status.Ino)) : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(native);
            }
        }
    }

    // ---- Windows ----------------------------------------------------------------------------------------

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        private const uint FileReadAttributes = 0x80;
        private const uint FileShareAll = 0x1 | 0x2 | 0x4;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public FILETIME CreationTime;
            public FILETIME LastAccessTime;
            public FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateFileW(IntPtr fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "CloseHandle", ExactSpelling = true)]
        private static extern int CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", ExactSpelling = true, SetLastError = true)]
        private static extern int GetFileInformationByHandle(IntPtr handle, ref ByHandleFileInformation information);

        public static FileIdentity? Get(string path, bool followLinks)
        {
            var flags = FileFlagBackupSemantics | (followLinks ? 0 : FileFlagOpenReparsePoint);
            var name = Marshal.StringToHGlobalUni(path);
            IntPtr handle;
            try
            {
                handle = CreateFileW(name, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(name);
            }

            if (handle == InvalidHandleValue || handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var information = default(ByHandleFileInformation);
                if (GetFileInformationByHandle(handle, ref information) == 0)
                {
                    return null;
                }

                return new FileIdentity(information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
