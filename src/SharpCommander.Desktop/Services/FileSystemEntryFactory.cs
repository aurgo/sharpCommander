using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Builds <see cref="FileSystemEntry"/> records from file system metadata.
/// </summary>
internal static class FileSystemEntryFactory
{
    /// <summary>
    /// Creates the entry for a file or directory. Returns null when its metadata cannot be read; broken
    /// symbolic links are still returned (size 0) so the user can see and remove them.
    /// </summary>
    public static FileSystemEntry? TryCreate(FileSystemInfo info)
    {
        try
        {
            return Create(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IsLink(info) ? CreateUnreadable(info) : null;
        }
    }

    /// <summary>Creates the ".." entry pointing at <paramref name="parent"/>.</summary>
    public static FileSystemEntry CreateParent(DirectoryInfo parent)
    {
        return new FileSystemEntry
        {
            Name = "..",
            FullPath = parent.FullName,
            EntryType = FileSystemEntryType.ParentDirectory,
            LastModified = parent.LastWriteTime,
            Attributes = FileAttributes.Directory
        };
    }

    /// <summary>Formats Unix permissions as "rwxr-xr-x" (with s/S and t/T for setuid, setgid and sticky).</summary>
    public static string FormatUnixPermissions(UnixFileMode mode)
    {
        Span<char> chars = stackalloc char[9];
        chars[0] = Has(mode, UnixFileMode.UserRead) ? 'r' : '-';
        chars[1] = Has(mode, UnixFileMode.UserWrite) ? 'w' : '-';
        chars[2] = ExecuteChar(Has(mode, UnixFileMode.UserExecute), Has(mode, UnixFileMode.SetUser), 's', 'S');
        chars[3] = Has(mode, UnixFileMode.GroupRead) ? 'r' : '-';
        chars[4] = Has(mode, UnixFileMode.GroupWrite) ? 'w' : '-';
        chars[5] = ExecuteChar(Has(mode, UnixFileMode.GroupExecute), Has(mode, UnixFileMode.SetGroup), 's', 'S');
        chars[6] = Has(mode, UnixFileMode.OtherRead) ? 'r' : '-';
        chars[7] = Has(mode, UnixFileMode.OtherWrite) ? 'w' : '-';
        chars[8] = ExecuteChar(Has(mode, UnixFileMode.OtherExecute), Has(mode, UnixFileMode.StickyBit), 't', 'T');
        return new string(chars);
    }

    private static FileSystemEntry Create(FileSystemInfo info)
    {
        var attributes = info.Attributes;
        var isDirectory = info is DirectoryInfo;

        return new FileSystemEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            EntryType = isDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File,
            Size = info is FileInfo file ? file.Length : 0,
            LastModified = info.LastWriteTime,
            Created = info.CreationTime,
            LastAccessed = info.LastAccessTime,
            Extension = isDirectory ? string.Empty : info.Extension,
            Attributes = attributes,
            IsHidden = IsHidden(info.Name, attributes),
            IsReadOnly = (attributes & FileAttributes.ReadOnly) != 0,
            IsSymbolicLink = (attributes & FileAttributes.ReparsePoint) != 0,
            UnixPermissions = GetUnixPermissions(info)
        };
    }

    private static FileSystemEntry CreateUnreadable(FileSystemInfo info)
    {
        var isDirectory = info is DirectoryInfo;

        return new FileSystemEntry
        {
            Name = info.Name,
            FullPath = info.FullName,
            EntryType = isDirectory ? FileSystemEntryType.Directory : FileSystemEntryType.File,
            Extension = isDirectory ? string.Empty : info.Extension,
            Attributes = FileAttributes.ReparsePoint,
            IsHidden = !OperatingSystem.IsWindows() && info.Name.StartsWith('.'),
            IsSymbolicLink = true
        };
    }

    private static bool IsHidden(string name, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.Hidden) != 0)
        {
            return true;
        }

        return !OperatingSystem.IsWindows() && name.StartsWith('.');
    }

    private static string? GetUnixPermissions(FileSystemInfo info)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return FormatUnixPermissions(info.UnixFileMode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsLink(FileSystemInfo info)
    {
        try
        {
            return info.LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Has(UnixFileMode mode, UnixFileMode flag) => (mode & flag) != 0;

    private static char ExecuteChar(bool execute, bool special, char specialExecute, char specialOnly)
    {
        if (special)
        {
            return execute ? specialExecute : specialOnly;
        }

        return execute ? 'x' : '-';
    }
}
