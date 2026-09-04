using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Moves across volumes (H8), simulated by forcing the engine's copy-then-delete path on one disk: every
/// file is copied before its source is removed, and a failure or cancellation leaves the unmoved part intact.
/// </summary>
public class FileTransferEngineTests
{
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task Move_AcrossVolumes_CopiesEverythingThenRemovesTheSource()
    {
        using var dir = new TempDir();
        var source = dir.Dir("tree");
        dir.File(Path.Combine("tree", "a.txt"), "aaa");
        dir.File(Path.Combine("tree", "sub", "b.txt"), "bbbb");
        dir.Dir(Path.Combine("tree", "empty"));
        var destination = dir.Dir("dst");
        var reports = new List<FileOperationProgress>();
        var engine = new FileTransferEngine(null, new SynchronousProgress<FileOperationProgress>(reports.Add), CancellationToken.None)
        {
            SameVolumeOverride = false
        };

        await Task.Run(() => engine.MoveAsync(source, destination));

        var moved = Path.Combine(destination, "tree");
        Assert.False(Directory.Exists(source));
        Assert.Equal("aaa", File.ReadAllText(Path.Combine(moved, "a.txt")));
        Assert.Equal("bbbb", File.ReadAllText(Path.Combine(moved, "sub", "b.txt")));
        Assert.True(Directory.Exists(Path.Combine(moved, "empty")));

        var last = reports.Last();
        Assert.Equal(FileOperationState.Completed, last.State);
        Assert.Equal(2, last.TotalFiles);
        Assert.Equal(2, last.ProcessedFiles);
        Assert.Equal(7, last.TotalBytes);
        Assert.Equal(7, last.ProcessedBytes);
        Assert.Equal(100, last.PercentComplete);
    }

    [Fact]
    public async Task Move_AcrossVolumes_CancelledAtAConflict_LeavesEverythingInPlace()
    {
        using var dir = new TempDir();
        var source = dir.Dir("tree");
        var files = new[] { "1.txt", "2.txt", "3.txt" }.Select(name => dir.File(Path.Combine("tree", name), "new " + name)).ToList();
        var destination = dir.Dir("dst");
        foreach (var name in new[] { "1.txt", "2.txt", "3.txt" })
        {
            dir.File(Path.Combine("dst", "tree", name), "old " + name);
        }

        var conflicts = 0;
        ConflictResolver resolver = (_, _) =>
        {
            Interlocked.Increment(ref conflicts);
            return Task.FromResult(new ConflictResolution(ConflictAction.Cancel));
        };
        var engine = new FileTransferEngine(resolver, null, CancellationToken.None) { SameVolumeOverride = false };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => engine.MoveAsync(source, destination)));

        Assert.Equal(1, conflicts);
        Assert.True(Directory.Exists(source));
        foreach (var file in files)
        {
            Assert.Equal("new " + Path.GetFileName(file), File.ReadAllText(file));
            Assert.Equal("old " + Path.GetFileName(file), File.ReadAllText(Path.Combine(destination, "tree", Path.GetFileName(file))));
        }

        Assert.Empty(Directory.GetFiles(Path.Combine(destination, "tree"), "*.sharpcommander-tmp"));
    }

    [Fact]
    public async Task Move_AcrossVolumes_FailedCopy_KeepsTheSource()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return;
        }

        using var dir = new TempDir();
        var file = dir.File("a.txt", "keep me");
        var destination = dir.Dir("readonly");
        File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var engine = new FileTransferEngine(null, null, CancellationToken.None) { SameVolumeOverride = false };

        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() => engine.MoveAsync(file, destination)));

            Assert.Equal("keep me", File.ReadAllText(file));
            Assert.Empty(Directory.GetFileSystemEntries(destination));
        }
        finally
        {
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
