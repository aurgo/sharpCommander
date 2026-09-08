using System.IO.Compression;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// ZIP support on top of <see cref="System.IO.Compression"/>. Both directions refuse to write outside the folder
/// they were given: a packed entry never carries an absolute path, and an extracted one is resolved and checked
/// against the destination before anything is created, so a crafted archive cannot drop files elsewhere on disk
/// (the "zip slip" attack).
/// </summary>
public sealed class ZipArchiveService : IArchiveService
{
    private static readonly string[] Extensions = [".zip"];

    public bool IsArchive(string path)
    {
        return !string.IsNullOrEmpty(path)
            && Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    public Task<int> CreateAsync(IReadOnlyList<string> sources, string archivePath, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrEmpty(archivePath);

        if (File.Exists(archivePath) || Directory.Exists(archivePath))
        {
            throw new IOException($"'{Path.GetFileName(archivePath)}' already exists.");
        }

        return Task.Run(() =>
        {
            var written = 0;

            try
            {
                using var stream = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

                foreach (var source in sources)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (Directory.Exists(source))
                    {
                        written += AddDirectory(archive, source, progress, cancellationToken);
                    }
                    else if (File.Exists(source))
                    {
                        AddFile(archive, source, Path.GetFileName(source), progress);
                        written++;
                    }
                }
            }
            catch
            {
                // A half-written archive is worse than none: it looks like a real one.
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

        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationDirectory);
            var root = PathUtils.NormalizeFullPath(destinationDirectory);
            var extracted = 0;

            using var archive = ZipFile.OpenRead(archivePath);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

                // The entry may not escape the destination, whatever ".." or absolute path it claims to hold.
                if (!PathUtils.IsSameOrDescendant(root, target))
                {
                    throw new IOException($"'{entry.FullName}' would be written outside the destination folder.");
                }

                // A directory entry is one whose name ends in a separator and that carries no content.
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                progress?.Report(entry.FullName);
                entry.ExtractToFile(target, overwrite: false);
                extracted++;
            }

            return extracted;
        }, cancellationToken);
    }

    private static int AddDirectory(ZipArchive archive, string directory, IProgress<string>? progress, CancellationToken cancellationToken)
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
            AddFile(archive, file, $"{prefix}/{relative}", progress);
            written++;
        }

        // An empty folder still deserves an entry, or extracting would not give it back.
        if (written == 0)
        {
            archive.CreateEntry($"{prefix}/");
        }

        return written;
    }

    private static void AddFile(ZipArchive archive, string file, string entryName, IProgress<string>? progress)
    {
        progress?.Report(file);
        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
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
