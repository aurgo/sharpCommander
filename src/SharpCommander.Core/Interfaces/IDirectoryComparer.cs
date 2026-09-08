using SharpCommander.Core.Models;

namespace SharpCommander.Core.Interfaces;

/// <summary>Compares the contents of two folders, name by name.</summary>
public interface IDirectoryComparer
{
    /// <summary>
    /// Compares the immediate contents of two folders. Files are the same when their size and write time match;
    /// the contents are never read, so a comparison is cheap even for large trees. Subfolders are reported as
    /// present or missing, not walked.
    /// </summary>
    Task<DirectoryComparison> CompareAsync(string leftPath, string rightPath, bool includeHidden, CancellationToken cancellationToken = default);
}
