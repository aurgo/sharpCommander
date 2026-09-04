namespace SharpCommander.Core.Utilities;

/// <summary>
/// Path helpers shared by services and view models: normalization, platform-aware comparison,
/// containment checks, unique-name generation and file-name validation.
/// </summary>
public static class PathUtils
{
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>Device names that Windows refuses as file names, with or without an extension.</summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Gets the comparison used for paths on the current platform: ordinal on Linux (case-sensitive
    /// file systems), ordinal-ignore-case on Windows and macOS.
    /// </summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>Gets the comparer matching <see cref="PathComparison"/>.</summary>
    public static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Returns the absolute form of <paramref name="path"/> without trailing separators.
    /// Roots keep their separator ("/" and "C:\").
    /// </summary>
    public static string NormalizeFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var trimmed = full.TrimEnd(Separators);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return trimmed.Length < root.Length ? root : trimmed;
    }

    /// <summary>Returns true when both paths refer to the same location after normalization.</summary>
    public static bool AreSamePath(string a, string b)
    {
        return string.Equals(NormalizeFullPath(a), NormalizeFullPath(b), PathComparison);
    }

    /// <summary>
    /// Returns true when <paramref name="path"/> is the root of a volume ("/", "C:\", "\\server\share"), which
    /// has no name of its own and can therefore not be copied or moved as an item.
    /// </summary>
    public static bool IsVolumeRoot(string path)
    {
        var full = NormalizeFullPath(path);
        return Path.GetFileName(full).Length == 0 || string.Equals(full, Path.GetPathRoot(full), PathComparison);
    }

    /// <summary>
    /// Returns true when <paramref name="candidatePath"/> is <paramref name="ancestorDirectory"/> itself or
    /// lies anywhere below it. A trailing separator is added before comparing so that a sibling whose name
    /// merely starts with the ancestor's name ("docs2" next to "docs") is not treated as a descendant.
    /// </summary>
    public static bool IsSameOrDescendant(string ancestorDirectory, string candidatePath)
    {
        var ancestor = NormalizeFullPath(ancestorDirectory);
        var candidate = NormalizeFullPath(candidatePath);

        if (string.Equals(ancestor, candidate, PathComparison))
        {
            return true;
        }

        var prefix = ancestor.EndsWith(Path.DirectorySeparatorChar)
            ? ancestor
            : ancestor + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, PathComparison);
    }

    /// <summary>
    /// Returns <paramref name="fileName"/> when nothing with that name exists in <paramref name="directory"/>;
    /// otherwise the first free variant in the form "name (2).ext", "name (3).ext", ...
    /// Folders and dotfiles keep their whole name as the stem. An existing " (n)" counter is replaced, not stacked.
    /// </summary>
    public static string GetUniqueName(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var existing = Path.Combine(directory, fileName);
        if (!PathExists(existing))
        {
            return fileName;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        if (stem.Length == 0 || Directory.Exists(existing))
        {
            stem = fileName;
            extension = string.Empty;
        }

        stem = StripCounterSuffix(stem);

        for (var counter = 2; ; counter++)
        {
            var candidate = $"{stem} ({counter}){extension}";
            if (!PathExists(Path.Combine(directory, candidate)))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Validates a single file or folder name (not a path). Returns a user-facing error message,
    /// or null when the name is acceptable on the current platform.
    /// </summary>
    public static string? ValidateFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The name cannot be empty.";
        }

        if (name is "." or "..")
        {
            return "'.' and '..' are not valid names.";
        }

        if (name.IndexOfAny(Separators) >= 0)
        {
            return "The name cannot contain path separators.";
        }

        var invalidIndex = name.IndexOfAny(InvalidFileNameChars);
        if (invalidIndex >= 0)
        {
            return $"The name contains an invalid character: {DescribeCharacter(name[invalidIndex])}.";
        }

        if (OperatingSystem.IsWindows())
        {
            if (name.EndsWith('.') || name.EndsWith(' '))
            {
                return "The name cannot end with a dot or a space.";
            }

            var device = name.Split('.')[0];
            if (WindowsReservedNames.Contains(device))
            {
                return $"'{device}' is a reserved device name on Windows.";
            }
        }

        return null;
    }

    private static bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    private static string StripCounterSuffix(string stem)
    {
        if (!stem.EndsWith(')'))
        {
            return stem;
        }

        var open = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (open <= 0)
        {
            return stem;
        }

        var digits = stem.AsSpan(open + 2, stem.Length - open - 3);
        if (digits.Length == 0)
        {
            return stem;
        }

        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return stem;
            }
        }

        return stem[..open];
    }

    private static string DescribeCharacter(char c)
    {
        return char.IsControl(c) ? $"U+{(int)c:X4}" : $"'{c}'";
    }
}
