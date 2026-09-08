using SharpCommander.Core.Interfaces;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Picks the archive format from the file name and hands the work to the service that owns it. The view models
/// only ever see one <see cref="IArchiveService"/>, so adding a format is a matter of registering it here.
/// </summary>
public sealed class CompositeArchiveService(params IArchiveService[] formats) : IArchiveService
{
    private readonly IReadOnlyList<IArchiveService> _formats = formats ?? throw new ArgumentNullException(nameof(formats));

    public bool IsArchive(string path) => Find(path) is not null;

    public Task<int> CreateAsync(IReadOnlyList<string> sources, string archivePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return Handler(archivePath).CreateAsync(sources, archivePath, progress, cancellationToken);
    }

    public Task<int> ExtractAsync(string archivePath, string destinationDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        return Handler(archivePath).ExtractAsync(archivePath, destinationDirectory, progress, cancellationToken);
    }

    private IArchiveService Handler(string path)
    {
        return Find(path) ?? throw new NotSupportedException($"'{Path.GetFileName(path)}' is not a supported archive format.");
    }

    private IArchiveService? Find(string path) => _formats.FirstOrDefault(format => format.IsArchive(path));
}
