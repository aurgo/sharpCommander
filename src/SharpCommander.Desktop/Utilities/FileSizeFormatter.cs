namespace SharpCommander.Desktop.Utilities;

/// <summary>
/// Utility class for file size formatting.
/// </summary>
public static class FileSizeFormatter
{
    private static readonly string[] SizeSuffixes = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>
    /// Formats a byte count into a human-readable string.
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string like "1.5 MB".</returns>
    public static string Format(long bytes)
    {
        if (bytes == 0)
        {
            return "0 B";
        }

        int index = 0;
        double size = bytes;

        while (size >= 1024 && index < SizeSuffixes.Length - 1)
        {
            size /= 1024;
            index++;
        }

        return $"{size:N1} {SizeSuffixes[index]}";
    }

    /// <summary>
    /// Formats a byte count for display in file lists.
    /// Returns "&lt;DIR&gt;" for zero bytes (directories).
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <returns>A formatted string or "&lt;DIR&gt;" for directories.</returns>
    public static string FormatForDisplay(long bytes)
    {
        if (bytes == 0)
        {
            return "<DIR>";
        }

        return Format(bytes);
    }
}
