using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Compares two folders by name, size and write time. Contents are never read: a byte-for-byte comparison of a
/// large tree costs far more than the extra confidence is worth here, and the checksum window is there for the
/// cases that need certainty. Write times are compared with a two-second tolerance, which is the resolution FAT
/// and several network file systems keep, so copies do not all show up as different.
/// </summary>
public sealed class DirectoryComparer : IDirectoryComparer
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromSeconds(2);

    public Task<DirectoryComparison> CompareAsync(string leftPath, string rightPath, bool includeHidden, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(leftPath);
        ArgumentException.ThrowIfNullOrEmpty(rightPath);

        return Task.Run(() =>
        {
            var left = Snapshot(leftPath, includeHidden, cancellationToken);
            var right = Snapshot(rightPath, includeHidden, cancellationToken);

            var names = new HashSet<string>(left.Keys, PathUtils.PathComparer);
            names.UnionWith(right.Keys);

            var items = new List<ComparisonItem>(names.Count);
            foreach (var name in names.OrderBy(name => name, PathUtils.PathComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                items.Add(Compare(name, left.GetValueOrDefault(name), right.GetValueOrDefault(name)));
            }

            return new DirectoryComparison
            {
                LeftPath = leftPath,
                RightPath = rightPath,
                Items = items
            };
        }, cancellationToken);
    }

    private static ComparisonItem Compare(string name, Side? left, Side? right)
    {
        if (left is null)
        {
            return new ComparisonItem { Name = name, State = ComparisonState.OnlyRight, IsDirectory = right!.IsDirectory, RightSize = right.Size };
        }

        if (right is null)
        {
            return new ComparisonItem { Name = name, State = ComparisonState.OnlyLeft, IsDirectory = left.IsDirectory, LeftSize = left.Size };
        }

        // A name that is a folder on one side and a file on the other is a real difference, not a match.
        var same = left.IsDirectory == right.IsDirectory
            && (left.IsDirectory || (left.Size == right.Size && Within(left.Modified, right.Modified)));

        return new ComparisonItem
        {
            Name = name,
            State = same ? ComparisonState.Same : ComparisonState.Different,
            IsDirectory = left.IsDirectory && right.IsDirectory,
            LeftSize = left.Size,
            RightSize = right.Size
        };
    }

    private static bool Within(DateTime a, DateTime b) => (a - b).Duration() <= TimestampTolerance;

    private static Dictionary<string, Side> Snapshot(string path, bool includeHidden, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, Side>(PathUtils.PathComparer);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"The folder '{path}' does not exist.");
        }

        // AttributesToSkip defaults to Hidden|System, which would drop hidden entries before IsHidden below ever
        // saw them and make includeHidden do nothing.
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.None };

        foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!includeHidden && IsHidden(entry))
            {
                continue;
            }

            var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
            result[entry.Name] = new Side(isDirectory, isDirectory ? 0 : ((FileInfo)entry).Length, entry.LastWriteTimeUtc);
        }

        return result;
    }

    private static bool IsHidden(FileSystemInfo entry)
    {
        return (entry.Attributes & FileAttributes.Hidden) != 0
            || (!OperatingSystem.IsWindows() && entry.Name.StartsWith('.'));
    }

    private sealed record Side(bool IsDirectory, long Size, DateTime Modified);
}
