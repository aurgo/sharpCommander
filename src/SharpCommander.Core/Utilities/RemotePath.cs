namespace SharpCommander.Core.Utilities;

/// <summary>
/// Path arithmetic for a remote server. Kept apart from <see cref="System.IO.Path"/> on purpose: that one follows
/// the local platform, so on Windows it would happily hand back backslashes for paths that must always be POSIX.
/// </summary>
public static class RemotePath
{
    public const string Root = "/";

    /// <summary>Joins segments with "/", collapsing repeats and keeping the leading slash.</summary>
    public static string Combine(string basePath, string name)
    {
        ArgumentNullException.ThrowIfNull(basePath);
        ArgumentNullException.ThrowIfNull(name);

        if (name.StartsWith('/'))
        {
            return Normalize(name);
        }

        var trimmed = basePath.TrimEnd('/');
        return Normalize(trimmed.Length == 0 ? "/" + name : trimmed + "/" + name);
    }

    /// <summary>The parent folder, or null at the root.</summary>
    public static string? GetParent(string path)
    {
        var normalized = Normalize(path);
        if (normalized == Root)
        {
            return null;
        }

        var index = normalized.LastIndexOf('/');
        return index <= 0 ? Root : normalized[..index];
    }

    /// <summary>The last segment, or "/" at the root.</summary>
    public static string GetName(string path)
    {
        var normalized = Normalize(path);
        if (normalized == Root)
        {
            return Root;
        }

        return normalized[(normalized.LastIndexOf('/') + 1)..];
    }

    /// <summary>Collapses repeated slashes and drops a trailing one; an empty path becomes the root.</summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Root;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? Root : "/" + string.Join('/', parts);
    }
}
