using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Lists the volumes worth showing in the "Computer" view: every ready drive on Windows, the root volume plus
/// /Volumes/* on macOS, and real block, removable and network mounts on Linux (no pseudo file systems or
/// system mount points).
/// </summary>
internal static class VolumeEnumerator
{
    private static readonly HashSet<string> PseudoFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "proc", "procfs", "sysfs", "devtmpfs", "devpts", "devfs", "tmpfs", "ramfs", "cgroup", "cgroup2",
        "overlay", "squashfs", "autofs", "securityfs", "pstore", "debugfs", "tracefs", "configfs", "fusectl",
        "mqueue", "hugetlbfs", "binfmt_misc", "bpf", "efivarfs", "rpc_pipefs", "nsfs", "selinuxfs", "fdescfs", "none"
    };

    /// <summary>
    /// Mount points that carry no user data. "/run" is deliberately not listed as a whole: udisks2 mounts
    /// removable media at /run/media/&lt;user&gt;/&lt;label&gt; on Fedora, RHEL, Arch and openSUSE, so only the
    /// noisy children of /run are hidden.
    /// </summary>
    private static readonly string[] LinuxSystemMountPrefixes =
    [
        "/dev", "/proc", "/sys", "/snap", "/boot/efi", "/var/lib/docker", "/var/snap", "/var/lib/snapd",
        "/run/user", "/run/credentials", "/run/snapd", "/run/lock", "/run/systemd", "/run/docker"
    ];

    /// <summary>
    /// Volume kinds listed on Linux. CDRom covers mounted DVDs and loop-mounted ISOs (iso9660/udf) and Unknown
    /// covers filesystems .NET cannot name from their statfs magic (bcachefs, erofs, recent virtiofs); both carry
    /// user data, and the pseudo-format and prefix filters already remove the noise.
    /// </summary>
    private static readonly DriveType[] LinuxVolumeTypes =
        [DriveType.Fixed, DriveType.Removable, DriveType.Network, DriveType.CDRom, DriveType.Unknown];

    /// <summary>Returns the visible volumes, root first.</summary>
    public static IReadOnlyList<FileSystemEntry> GetVolumes(CancellationToken cancellationToken)
    {
        var entries = new List<FileSystemEntry>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsVisible(drive))
            {
                entries.Add(CreateEntry(drive));
            }
        }

        entries.Sort(CompareVolumes);
        return entries;
    }

    private static bool IsVisible(DriveInfo drive)
    {
        if (OperatingSystem.IsMacOS())
        {
            var mountPoint = PathUtils.NormalizeFullPath(drive.Name);
            var underVolumes = mountPoint.StartsWith("/Volumes/", StringComparison.Ordinal);
            return (mountPoint == "/" || underVolumes) && !HasPseudoFormat(drive);
        }

        if (OperatingSystem.IsLinux())
        {
            if (HasPseudoFormat(drive))
            {
                return false;
            }

            return IsLinuxVolumeVisible(PathUtils.NormalizeFullPath(drive.Name), SafeDriveType(drive));
        }

        return SafeIsReady(drive);
    }

    /// <summary>
    /// Decides whether a Linux mount point belongs in the Computer view. Pure so it can be unit tested without
    /// a real mount: the caller has already ruled out pseudo filesystems by format.
    /// </summary>
    internal static bool IsLinuxVolumeVisible(string mountPoint, DriveType driveType)
    {
        return LinuxVolumeTypes.Contains(driveType)
               && !IsLinuxSystemMountPoint(mountPoint)
               && !IsHiddenMountPoint(mountPoint);
    }

    private static bool IsLinuxSystemMountPoint(string mountPoint)
    {
        foreach (var prefix in LinuxSystemMountPrefixes)
        {
            if (mountPoint == prefix || mountPoint.StartsWith(prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Hides mount points whose own name starts with a dot: the runtime mount every running AppImage creates at
    /// /tmp/.mount_&lt;app&gt;XXXXXX, and the same convention used by other self-mounting bundles.
    /// </summary>
    private static bool IsHiddenMountPoint(string mountPoint)
    {
        return Path.GetFileName(mountPoint).StartsWith('.');
    }

    private static bool HasPseudoFormat(DriveInfo drive)
    {
        try
        {
            // statfs reports every FUSE mount as "fuse"/"fuseblk", never as "fuse.<subtype>", so there is no
            // subtype rule here: real FUSE volumes (sshfs, NTFS-3g, MTP) carry user data and stay visible.
            return PseudoFormats.Contains(drive.DriveFormat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static FileSystemEntry CreateEntry(DriveInfo drive)
    {
        var name = GetDisplayName(drive);
        var ready = SafeIsReady(drive);
        long? total = null;
        long? free = null;
        var format = string.Empty;

        if (ready)
        {
            try
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
                format = drive.DriveFormat;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ready = false;
                total = null;
                free = null;
            }
        }

        return new FileSystemEntry
        {
            Name = name,
            FullPath = drive.Name,
            EntryType = FileSystemEntryType.Drive,
            IsReady = ready,
            Size = total ?? 0,
            TotalSize = total,
            FreeSpace = free,
            VolumeLabel = name,
            DriveFormat = format,
            Attributes = FileAttributes.Directory
        };
    }

    private static string GetDisplayName(DriveInfo drive)
    {
        if (OperatingSystem.IsWindows())
        {
            var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var label = SafeVolumeLabel(drive);
            return label.Length > 0 ? $"{label} ({letter})" : letter;
        }

        var mountPoint = PathUtils.NormalizeFullPath(drive.Name);
        if (mountPoint == "/")
        {
            return GetRootVolumeLabel() ?? "/";
        }

        var segment = Path.GetFileName(mountPoint);
        return segment.Length > 0 ? segment : mountPoint;
    }

    /// <summary>On macOS the boot volume's label is the name of the /Volumes symlink that points at "/".</summary>
    private static string? GetRootVolumeLabel()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            foreach (var entry in new DirectoryInfo("/Volumes").EnumerateDirectories())
            {
                if (entry.LinkTarget == "/")
                {
                    return entry.Name;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to the generic name.
        }

        return null;
    }

    private static int CompareVolumes(FileSystemEntry a, FileSystemEntry b)
    {
        if (OperatingSystem.IsWindows())
        {
            return string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        var aIsRoot = a.FullPath == "/";
        var bIsRoot = b.FullPath == "/";
        if (aIsRoot != bIsRoot)
        {
            return aIsRoot ? -1 : 1;
        }

        return NaturalStringComparer.Instance.Compare(a.Name, b.Name);
    }

    private static bool SafeIsReady(DriveInfo drive)
    {
        try
        {
            return drive.IsReady;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static DriveType SafeDriveType(DriveInfo drive)
    {
        try
        {
            return drive.DriveType;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DriveType.Unknown;
        }
    }

    private static string SafeVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.VolumeLabel : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
