namespace SharpCommander.Core.Utilities;

/// <summary>
/// Path arithmetic for a path that may be on either side of the local/remote boundary. <see cref="PathUtils"/>
/// follows the rules of the machine this runs on and <see cref="RemotePath"/> those of a server; anything handed
/// whatever the panels happen to be showing needs one that decides per path, and this is it.
///
/// Reaching for the wrong one produces nonsense rather than an error, which is what makes it worth a type of its
/// own: <see cref="System.IO.Path.GetFullPath(string)"/> reads "sftp://ana@example.com:22/home" as a relative
/// name and resolves it against the working directory, and <see cref="System.IO.Path.Combine(string, string)"/>
/// joins a remote folder to a name with a backslash on Windows.
/// </summary>
public static class AnyPath
{
    /// <summary>True when the path names a place on a server rather than on this machine.</summary>
    public static bool IsRemote(string? path) => SftpAddress.IsRemote(path);

    /// <summary>The absolute form, without a trailing separator.</summary>
    public static string Normalize(string path)
    {
        return SftpAddress.TryParse(path) is { } address ? address.ToString() : PathUtils.NormalizeFullPath(path);
    }

    /// <summary>The last segment ("notes.txt"), or an empty string for a root, which has no name of its own.</summary>
    public static string GetName(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (SftpAddress.TryParse(path) is not { } address)
        {
            return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        var name = RemotePath.GetName(address.Path);
        return name == RemotePath.Root ? string.Empty : name;
    }

    /// <summary>The folder holding the path, or null when it is already a root.</summary>
    public static string? GetDirectory(string path)
    {
        return SftpAddress.TryParse(path) is { } address ? address.Parent() : Path.GetDirectoryName(path);
    }

    /// <summary>The path of <paramref name="name"/> inside <paramref name="directory"/>.</summary>
    public static string Combine(string directory, string name)
    {
        return SftpAddress.TryParse(directory) is { } address ? address.Combine(name) : Path.Combine(directory, name);
    }

    /// <summary>
    /// True when both paths are the same place. A local path and a remote one never are, whatever they look
    /// like. Two remote paths are compared exactly: the servers this talks to have case-sensitive file systems,
    /// and calling "A.txt" and "a.txt" the same would refuse a copy that is perfectly legal there.
    /// </summary>
    public static bool AreSame(string a, string b)
    {
        var left = SftpAddress.TryParse(a);
        var right = SftpAddress.TryParse(b);

        if (left is null && right is null)
        {
            return PathUtils.AreSamePath(a, b);
        }

        return left is not null
            && right is not null
            && string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="candidatePath"/> is <paramref name="ancestorDirectory"/> itself or lies below
    /// it. Two paths on different servers, or one here and one there, never are.
    /// </summary>
    public static bool IsSameOrDescendant(string ancestorDirectory, string candidatePath)
    {
        var ancestor = SftpAddress.TryParse(ancestorDirectory);
        var candidate = SftpAddress.TryParse(candidatePath);

        if (ancestor is null && candidate is null)
        {
            return PathUtils.IsSameOrDescendant(ancestorDirectory, candidatePath);
        }

        if (ancestor is null || candidate is null || !string.Equals(ancestor.Endpoint, candidate.Endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var root = RemotePath.Normalize(ancestor.Path);
        var below = RemotePath.Normalize(candidate.Path);

        // The root is the one folder that already ends in its separator; anything else needs one added so that
        // "/home/anabel" is not read as a child of "/home/ana".
        return string.Equals(root, below, StringComparison.Ordinal)
            || below.StartsWith(root == RemotePath.Root ? root : root + "/", StringComparison.Ordinal);
    }

    /// <summary>True for a place with no name of its own: a volume root here, the server's root there.</summary>
    public static bool IsRoot(string path)
    {
        return SftpAddress.TryParse(path) is { } address
            ? RemotePath.Normalize(address.Path) == RemotePath.Root
            : PathUtils.IsVolumeRoot(path);
    }
}
