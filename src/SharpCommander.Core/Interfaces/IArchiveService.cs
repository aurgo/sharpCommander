namespace SharpCommander.Core.Interfaces;

/// <summary>
/// Creates and extracts archives. Only ZIP is handled today; the interface is deliberately format-agnostic so
/// another format can be added without touching the view models.
/// </summary>
public interface IArchiveService
{
    /// <summary>True when <paramref name="path"/> has an extension this service can extract.</summary>
    bool IsArchive(string path);

    /// <summary>
    /// Packs each source (file or folder) into a new archive at <paramref name="archivePath"/>, which must not
    /// exist. A folder is stored with its own name as the root, so extracting gives the folder back.
    /// </summary>
    /// <returns>The number of entries written.</returns>
    Task<int> CreateAsync(IReadOnlyList<string> sources, string archivePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts <paramref name="archivePath"/> into <paramref name="destinationDirectory"/>, which is created if
    /// needed. Entries that would land outside it are refused rather than written.
    /// </summary>
    /// <returns>The number of entries extracted.</returns>
    Task<int> ExtractAsync(string archivePath, string destinationDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
