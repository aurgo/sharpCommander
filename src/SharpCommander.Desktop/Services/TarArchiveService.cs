using System.Formats.Tar;
using System.IO.Compression;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// TAR support, plain or gzip-compressed, on top of <see cref="System.Formats.Tar"/>. Entries are read and
/// written one at a time rather than through TarFile so extraction can apply the same containment check as the
/// ZIP service: an entry that resolves outside the destination is refused instead of written.
/// </summary>
public sealed class TarArchiveService : IArchiveService
{
    /// <summary>Longest suffixes first, so ".tar.gz" is recognised before ".gz" would be.</summary>
    private static readonly string[] Extensions = [".tar.gz", ".tgz", ".tar"];

    public bool IsArchive(string path)
    {
        return SuffixOf(path) is not null;
    }

    public Task<int> CreateAsync(IReadOnlyList<string> sources, string archivePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrEmpty(archivePath);

        if (File.Exists(archivePath) || Directory.Exists(archivePath))
        {
            throw new IOException($"'{Path.GetFileName(archivePath)}' already exists.");
        }

        var compressed = IsCompressed(archivePath);

        return Task.Run(() =>
        {
            var written = 0;

            try
            {
                using var file = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using Stream stream = compressed ? new GZipStream(file, CompressionLevel.Optimal) : file;
                using var writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true);

                foreach (var source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Directory.Exists(source))
                    {
                        written += AddDirectory(writer, source, progress, cancellationToken);
                    }
                    else if (File.Exists(source))
                    {
                        progress?.Report(source);
                        writer.WriteEntry(source, Path.GetFileName(source));
                        written++;
                    }
                }
            }
            catch
            {
                // Never leave a half-written archive behind: it looks like a real one.
                TryDelete(archivePath);
                throw;
            }

            return written;
        }, cancellationToken);
    }

    public Task<int> ExtractAsync(string archivePath, string destinationDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationDirectory);

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("The archive does not exist.", archivePath);
        }

        var compressed = IsCompressed(archivePath);

        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);
            var root = PathUtils.NormalizeFullPath(destinationDirectory);
            var extracted = 0;

            using var file = File.OpenRead(archivePath);
            using Stream stream = compressed ? new GZipStream(file, CompressionMode.Decompress) : file;
            using var reader = new TarReader(stream);

            while (reader.GetNextEntry() is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.GetFullPath(Path.Combine(root, entry.Name));

                // The entry may not escape the destination, whatever ".." or absolute path it claims to hold.
                if (!PathUtils.IsSameOrDescendant(root, target))
                {
                    throw new IOException($"'{entry.Name}' would be written outside the destination folder.");
                }

                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(target);
                        break;

                    case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        progress?.Report(entry.Name);
                        entry.ExtractToFile(target, overwrite: false);
                        extracted++;
                        break;

                    default:
                        // Links, devices and the like: a file manager has no business recreating those blindly.
                        break;
                }
            }

            return extracted;
        }, cancellationToken);
    }

    private static int AddDirectory(TarWriter writer, string directory, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var root = PathUtils.NormalizeFullPath(directory);
        var prefix = Path.GetFileName(root);
        var written = 0;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            progress?.Report(file);
            writer.WriteEntry(file, $"{prefix}/{relative}");
            written++;
        }

        if (written == 0)
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"{prefix}/"));
        }

        return written;
    }

    private static bool IsCompressed(string path)
    {
        var suffix = SuffixOf(path);
        return suffix is ".tar.gz" or ".tgz";
    }

    private static string? SuffixOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        return Extensions.FirstOrDefault(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The incomplete archive '{path}' could not be removed.", ex);
        }
    }
}
