using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// The mount points known to the operating system, for deciding which volume a path lives on when the file
/// system cannot be asked directly. Longest mount point first, so the deepest match wins.
/// </summary>
internal static class MountPoints
{
    /// <summary>Loads the mount points, longest first; only "/" when they cannot be listed.</summary>
    public static string[] Load()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Select(drive => PathUtils.NormalizeFullPath(drive.Name))
                .Distinct(StringComparer.Ordinal)
                .OrderByDescending(mount => mount.Length)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ["/"];
        }
    }

    /// <summary>Returns the mount point (from <paramref name="mountPoints"/>) that contains <paramref name="path"/>.</summary>
    public static string Of(string path, string[] mountPoints)
    {
        foreach (var mountPoint in mountPoints)
        {
            if (PathUtils.IsSameOrDescendant(mountPoint, path))
            {
                return mountPoint;
            }
        }

        return Path.GetPathRoot(path) ?? "/";
    }
}
