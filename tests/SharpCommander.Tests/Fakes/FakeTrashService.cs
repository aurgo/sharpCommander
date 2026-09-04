using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Tests.Fakes;

/// <summary>
/// Trash that records what it received. Items are moved into <see cref="TrashDirectory"/> when one is given,
/// otherwise deleted, so callers can verify the entry is gone. <see cref="FailWith"/> simulates a refusal.
/// </summary>
public sealed class FakeTrashService : ITrashService
{
    public FakeTrashService(string? trashDirectory = null)
    {
        TrashDirectory = trashDirectory;
    }

    public bool IsSupported { get; set; } = true;

    public string? TrashDirectory { get; }

    /// <summary>Paths handed to <see cref="MoveToTrashAsync"/>, in order.</summary>
    public List<string> Trashed { get; } = [];

    /// <summary>When set and returning an exception for a path, that exception is thrown instead of trashing.</summary>
    public Func<string, Exception?>? FailWith { get; set; }

    public Task MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (FailWith?.Invoke(path) is { } error)
        {
            throw error;
        }

        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            throw new FileOperationException(FileOperationErrorKind.NotFound, path, $"'{path}' does not exist.");
        }

        Trashed.Add(path);

        if (TrashDirectory is not null)
        {
            Directory.CreateDirectory(TrashDirectory);
            var target = Path.Combine(TrashDirectory, Path.GetFileName(path));
            if (isDirectory)
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }
        }
        else if (isDirectory)
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
