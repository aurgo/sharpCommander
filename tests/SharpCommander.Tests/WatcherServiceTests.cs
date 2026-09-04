using SharpCommander.Core.Interfaces;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Error recovery of the directory watcher (H7): a failed watcher triggers a refresh at once and is recreated
/// after a delay that doubles from one second, reset by the next explicit start.
/// </summary>
public class WatcherServiceTests
{
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task Error_RefreshesImmediatelyAndRestartsWithBackoff()
    {
        using var dir = new TempDir();
        using var watcher = new FileSystemWatcherService();
        var changes = new List<FileSystemChangedEventArgs>();
        var restarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, e) =>
        {
            lock (changes)
            {
                changes.Add(e);
            }
        };
        watcher.Restarted += (_, _) => restarted.TrySetResult();

        watcher.Start(dir.Path);
        Assert.True(watcher.IsWatching);
        Assert.Equal(TimeSpan.FromSeconds(1), watcher.RestartDelay);

        watcher.SimulateError(new InternalBufferOverflowException("simulated overflow"));

        Assert.False(watcher.IsWatching);
        Assert.Equal(TimeSpan.FromSeconds(2), watcher.RestartDelay);
        lock (changes)
        {
            var change = Assert.Single(changes);
            Assert.Equal(dir.Path, change.Path);
            Assert.Equal(FileSystemChangeType.Modified, change.ChangeType);
        }

        await restarted.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Assert.True(watcher.IsWatching);

        // The refresh after the restart picks up whatever changed while the watcher was down. Only the
        // synthetic refreshes name the watched folder itself; real events would name an entry inside it.
        await WaitUntilAsync(() => { lock (changes) { return Refreshes() >= 2; } }, 2000);
        lock (changes)
        {
            Assert.Equal(2, Refreshes());
        }

        // The recreated watcher reports real changes again.
        var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += (_, e) =>
        {
            if (e.Path.EndsWith("after.txt", StringComparison.Ordinal))
            {
                seen.TrySetResult();
            }
        };
        dir.File("after.txt");
        await seen.Task.WaitAsync(TimeSpan.FromSeconds(8));

        int Refreshes() => changes.Count(change => change.Path == dir.Path && change.ChangeType == FileSystemChangeType.Modified);
    }

    [Fact]
    public void Start_ResetsTheBackoff_AndErrorsWhileStoppedAreIgnored()
    {
        using var dir = new TempDir();
        var other = dir.Dir("other");
        using var watcher = new FileSystemWatcherService();
        var refreshes = 0;
        watcher.Changed += (_, e) =>
        {
            // Synthetic refreshes name the watched folder itself; real events name an entry inside it.
            if (e.Path == dir.Path || e.Path == other)
            {
                Interlocked.Increment(ref refreshes);
            }
        };

        watcher.Start(dir.Path);
        watcher.SimulateError(new IOException("first"));
        Assert.False(watcher.IsWatching);
        Assert.Equal(TimeSpan.FromSeconds(2), watcher.RestartDelay);
        Assert.Equal(1, refreshes);

        watcher.Start(other);
        Assert.True(watcher.IsWatching);
        Assert.Equal(TimeSpan.FromSeconds(1), watcher.RestartDelay);

        watcher.Stop();
        Assert.False(watcher.IsWatching);
        watcher.SimulateError(new IOException("ignored: nothing is watched"));
        Assert.Equal(1, refreshes);
        Assert.Equal(TimeSpan.FromSeconds(1), watcher.RestartDelay);

        watcher.Dispose();
        watcher.Start(dir.Path);
        Assert.False(watcher.IsWatching);
    }
}
