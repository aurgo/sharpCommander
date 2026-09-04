using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

public class FileSystemServiceTests
{
    private readonly FileSystemService _service = new();

    private static ConflictResolver Always(ConflictAction action, bool applyToAll = false, string? newName = null)
    {
        return (_, _) => Task.FromResult(new ConflictResolution(action, applyToAll, newName));
    }

    private static ConflictResolver Recording(List<FileConflict> conflicts, ConflictResolution answer)
    {
        return (conflict, _) =>
        {
            lock (conflicts)
            {
                conflicts.Add(conflict);
            }

            return Task.FromResult(answer);
        };
    }

    /// <summary>Progress sink that runs synchronously on the reporting thread (Progress&lt;T&gt; would post to a context).</summary>
    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly object _gate = new();

        public void Report(T value)
        {
            lock (_gate)
            {
                handler(value);
            }
        }
    }

    private static string Rel(params string[] parts) => Path.Combine(parts);

    // ---- listing ----------------------------------------------------------------------------------------

    [Fact]
    public async Task GetEntries_DirectoriesFirstThenFiles_InNaturalOrder()
    {
        using var dir = new TempDir();
        dir.File("file10.txt");
        dir.File("file2.txt");
        dir.File("Alpha.txt");
        dir.Dir("zeta");
        dir.Dir("beta");

        var entries = await _service.GetEntriesAsync(dir.Path);

        Assert.Equal(new[] { "..", "beta", "zeta", "Alpha.txt", "file2.txt", "file10.txt" }, entries.Select(e => e.Name).ToArray());
        Assert.Equal(FileSystemEntryType.ParentDirectory, entries[0].EntryType);
        Assert.Equal(FileSystemEntryType.Directory, entries[1].EntryType);
        Assert.Equal(FileSystemEntryType.File, entries[3].EntryType);
    }

    [Fact]
    public async Task GetEntries_FillsMetadata()
    {
        using var dir = new TempDir();
        var file = dir.File("data.bin", "12345");

        var entry = (await _service.GetEntriesAsync(dir.Path)).Single(e => e.Name == "data.bin");

        Assert.Equal(5, entry.Size);
        Assert.Equal(".bin", entry.Extension);
        Assert.Equal(File.GetLastWriteTime(file), entry.LastModified);
        Assert.True(entry.Created > DateTime.MinValue);
        Assert.False(entry.IsHidden);
        Assert.False(entry.IsSymbolicLink);
        Assert.Equal(OperatingSystem.IsWindows(), entry.UnixPermissions is null);
        if (entry.UnixPermissions is not null)
        {
            Assert.Equal(9, entry.UnixPermissions.Length);
            Assert.StartsWith("rw", entry.UnixPermissions);
        }
    }

    [Fact]
    public async Task GetEntries_FlagsHiddenEntries()
    {
        using var dir = new TempDir();
        var hidden = dir.File(".hidden");
        dir.File("visible.txt");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }

        var entries = await _service.GetEntriesAsync(dir.Path);

        Assert.True(entries.Single(e => e.Name == ".hidden").IsHidden);
        Assert.False(entries.Single(e => e.Name == "visible.txt").IsHidden);
    }

    [Fact]
    public async Task GetEntries_ReportsSymbolicLinks_IncludingBrokenOnes()
    {
        using var dir = new TempDir();
        var target = dir.File("target.txt", "hello");
        try
        {
            File.CreateSymbolicLink(Path.Combine(dir.Path, "link.txt"), target);
            File.CreateSymbolicLink(Path.Combine(dir.Path, "broken"), Path.Combine(dir.Path, "missing"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Creating links needs a privilege on some Windows configurations.
        }

        var entries = await _service.GetEntriesAsync(dir.Path);

        Assert.True(entries.Single(e => e.Name == "link.txt").IsSymbolicLink);
        Assert.False(entries.Single(e => e.Name == "target.txt").IsSymbolicLink);
        var broken = entries.Single(e => e.Name == "broken");
        Assert.True(broken.IsSymbolicLink);
        Assert.Equal(FileSystemEntryType.File, broken.EntryType);
    }

    [Fact]
    public async Task GetEntries_MissingDirectory_Throws()
    {
        using var dir = new TempDir();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.GetEntriesAsync(Path.Combine(dir.Path, "nope")));
    }

    [Fact]
    public async Task GetDrives_ReturnsAtLeastOneReadyVolumeWithSpace()
    {
        var drives = await _service.GetDrivesAsync();

        Assert.NotEmpty(drives);
        var ready = drives.First(d => d.IsReady);
        Assert.Equal(FileSystemEntryType.Drive, ready.EntryType);
        Assert.NotNull(ready.TotalSize);
        Assert.NotNull(ready.FreeSpace);
        Assert.True(ready.TotalSize > 0);
        Assert.Equal(ready.TotalSize, ready.Size);
        Assert.False(string.IsNullOrEmpty(ready.Name));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/", drives[0].FullPath);
        }
    }

    // ---- same location and containment (C1, H4) ---------------------------------------------------------

    [Fact]
    public async Task Move_FileToItsOwnDirectory_ThrowsAndKeepsFile()
    {
        using var dir = new TempDir();
        var file = dir.File("important.txt", "keep me");

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.MoveAsync(file, dir.Path));

        Assert.Equal(FileOperationErrorKind.SameLocation, ex.Kind);
        Assert.Equal("keep me", File.ReadAllText(file));
    }

    [Fact]
    public async Task Move_DirectoryToItsOwnParent_ThrowsAndKeepsTree()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        var inner = dir.File(Rel("docs", "a.txt"), "a");

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.MoveAsync(docs, dir.Path + Path.DirectorySeparatorChar));

        Assert.Equal(FileOperationErrorKind.SameLocation, ex.Kind);
        Assert.Equal("a", File.ReadAllText(inner));
    }

    [Fact]
    public async Task Copy_ToSameLocation_ThrowsSameLocation()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "a");

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.CopyAsync(file, dir.Path));

        Assert.Equal(FileOperationErrorKind.SameLocation, ex.Kind);
        Assert.Single(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    [Fact]
    public async Task Copy_DirectoryIntoSiblingWithPrefixName_Works()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        dir.File(Rel("docs", "a.txt"), "a");
        var docs2 = dir.Dir("docs2");

        await _service.CopyAsync(docs, docs2);

        Assert.Equal("a", File.ReadAllText(Path.Combine(docs2, "docs", "a.txt")));
    }

    [Fact]
    public async Task CopyOrMove_DirectoryIntoItsOwnSubfolder_ThrowsDestinationInsideSource()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        var sub = dir.Dir(Rel("docs", "sub"));
        var file = dir.File(Rel("docs", "a.txt"), "a");

        var copy = await Assert.ThrowsAsync<FileOperationException>(() => _service.CopyAsync(docs, sub));
        var move = await Assert.ThrowsAsync<FileOperationException>(() => _service.MoveAsync(docs, sub));

        Assert.Equal(FileOperationErrorKind.DestinationInsideSource, copy.Kind);
        Assert.Equal(FileOperationErrorKind.DestinationInsideSource, move.Kind);
        Assert.Empty(Directory.EnumerateFileSystemEntries(sub));
        Assert.Equal("a", File.ReadAllText(file));
    }

    [Fact]
    public async Task Copy_MissingSource_ThrowsNotFound()
    {
        using var dir = new TempDir();

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.CopyAsync(Path.Combine(dir.Path, "ghost.txt"), dir.Dir("out")));

        Assert.Equal(FileOperationErrorKind.NotFound, ex.Kind);
    }

    // ---- conflicts (C2) ---------------------------------------------------------------------------------

    [Fact]
    public async Task Copy_OntoExisting_WithoutResolver_SkipsAndKeepsDestination()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "f.txt"), "new");
        var destination = dir.File(Rel("dst", "f.txt"), "old");

        await _service.CopyAsync(source, Path.Combine(dir.Path, "dst"));

        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Equal("new", File.ReadAllText(source));
    }

    [Fact]
    public async Task Copy_Overwrite_ReplacesDestination()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "f.txt"), "new");
        var destination = dir.File(Rel("dst", "f.txt"), "old");

        await _service.CopyAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Overwrite));

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.True(File.Exists(source));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "dst")));
    }

    [Fact]
    public async Task Copy_Rename_UsesUniqueOrGivenName()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "f.txt"), "new");
        var destination = dir.File(Rel("dst", "f.txt"), "old");
        var destinationDirectory = Path.Combine(dir.Path, "dst");

        await _service.CopyAsync(source, destinationDirectory, Always(ConflictAction.Rename));
        await _service.CopyAsync(source, destinationDirectory, Always(ConflictAction.Rename, newName: "renamed.txt"));

        Assert.Equal("old", File.ReadAllText(destination));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destinationDirectory, "f (2).txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(destinationDirectory, "renamed.txt")));
    }

    [Fact]
    public async Task Copy_Cancel_ThrowsOperationCanceledAndKeepsDestination()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "f.txt"), "new");
        var destination = dir.File(Rel("dst", "f.txt"), "old");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CopyAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Cancel)));

        Assert.Equal("old", File.ReadAllText(destination));
    }

    [Fact]
    public async Task Copy_ApplyToAll_AsksOnlyOnce()
    {
        using var dir = new TempDir();
        var source = dir.Dir("src");
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            dir.File(Rel("src", name), "new");
            dir.File(Rel("dst", "src", name), "old");
        }

        var conflicts = new List<FileConflict>();
        await _service.CopyAsync(source, Path.Combine(dir.Path, "dst"), Recording(conflicts, new ConflictResolution(ConflictAction.Overwrite, ApplyToAll: true)));

        Assert.Single(conflicts);
        Assert.False(conflicts[0].IsDirectory);
        Assert.Equal(3, conflicts[0].SourceSize);
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            Assert.Equal("new", File.ReadAllText(Path.Combine(dir.Path, "dst", "src", name)));
        }
    }

    [Fact]
    public async Task Copy_FileOntoDirectoryWithSameName_OverwriteIsRefused()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "x"), "file");
        var destinationDirectory = dir.Dir("dst");
        dir.Dir(Rel("dst", "x"));

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.CopyAsync(source, destinationDirectory, Always(ConflictAction.Overwrite)));
        await _service.CopyAsync(source, destinationDirectory, Always(ConflictAction.Skip));

        Assert.Equal(FileOperationErrorKind.TargetExists, ex.Kind);
        Assert.True(Directory.Exists(Path.Combine(destinationDirectory, "x")));
    }

    [Fact]
    public async Task Copy_DirectoryMerge_Skip_KeepsExistingAndAddsNew()
    {
        using var dir = new TempDir();
        var source = dir.Dir(Rel("src", "docs"));
        dir.File(Rel("src", "docs", "same.txt"), "src");
        dir.File(Rel("src", "docs", "only-src.txt"), "src");
        dir.File(Rel("dst", "docs", "same.txt"), "dst");
        dir.File(Rel("dst", "docs", "only-dst.txt"), "dst");
        var merged = Path.Combine(dir.Path, "dst", "docs");

        await _service.CopyAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Skip));

        Assert.Equal("dst", File.ReadAllText(Path.Combine(merged, "same.txt")));
        Assert.Equal("dst", File.ReadAllText(Path.Combine(merged, "only-dst.txt")));
        Assert.Equal("src", File.ReadAllText(Path.Combine(merged, "only-src.txt")));
    }

    [Fact]
    public async Task Move_DirectoryOntoExisting_Overwrite_MergesAndKeepsDestinationOnlyFiles()
    {
        using var dir = new TempDir();
        var source = dir.Dir(Rel("src", "docs"));
        dir.File(Rel("src", "docs", "same.txt"), "src");
        dir.File(Rel("src", "docs", "only-src.txt"), "src");
        dir.File(Rel("src", "docs", "nested", "deep.txt"), "src");
        dir.File(Rel("dst", "docs", "same.txt"), "dst");
        dir.File(Rel("dst", "docs", "only-dst.txt"), "dst");
        var merged = Path.Combine(dir.Path, "dst", "docs");

        await _service.MoveAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Overwrite));

        Assert.Equal("src", File.ReadAllText(Path.Combine(merged, "same.txt")));
        Assert.Equal("dst", File.ReadAllText(Path.Combine(merged, "only-dst.txt")));
        Assert.Equal("src", File.ReadAllText(Path.Combine(merged, "only-src.txt")));
        Assert.Equal("src", File.ReadAllText(Path.Combine(merged, "nested", "deep.txt")));
        Assert.False(Directory.Exists(source));
    }

    [Fact]
    public async Task Move_DirectoryOntoExisting_Skip_LeavesSkippedFilesInSource()
    {
        using var dir = new TempDir();
        var source = dir.Dir(Rel("src", "docs"));
        var skipped = dir.File(Rel("src", "docs", "same.txt"), "src");
        dir.File(Rel("src", "docs", "only-src.txt"), "src");
        dir.File(Rel("dst", "docs", "same.txt"), "dst");
        dir.File(Rel("dst", "docs", "only-dst.txt"), "dst");
        var merged = Path.Combine(dir.Path, "dst", "docs");

        await _service.MoveAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Skip));

        Assert.Equal("dst", File.ReadAllText(Path.Combine(merged, "same.txt")));
        Assert.Equal("dst", File.ReadAllText(Path.Combine(merged, "only-dst.txt")));
        Assert.Equal("src", File.ReadAllText(Path.Combine(merged, "only-src.txt")));
        Assert.Equal("src", File.ReadAllText(skipped));
        Assert.True(Directory.Exists(source));
    }

    [Fact]
    public async Task Move_FileOntoExisting_Overwrite_ReplacesAndRemovesSource()
    {
        using var dir = new TempDir();
        var source = dir.File(Rel("src", "f.txt"), "new");
        var destination = dir.File(Rel("dst", "f.txt"), "old");

        await _service.MoveAsync(source, Path.Combine(dir.Path, "dst"), Always(ConflictAction.Overwrite));

        Assert.Equal("new", File.ReadAllText(destination));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public async Task Move_DirectoryToEmptyDestination_RenamesWholeTree()
    {
        using var dir = new TempDir();
        var source = dir.Dir(Rel("src", "docs"));
        dir.File(Rel("src", "docs", "nested", "a.txt"), "a");
        var destinationDirectory = dir.Dir("dst");

        await _service.MoveAsync(source, destinationDirectory);

        Assert.Equal("a", File.ReadAllText(Path.Combine(destinationDirectory, "docs", "nested", "a.txt")));
        Assert.False(Directory.Exists(source));
    }

    // ---- progress and cancellation (H9) -----------------------------------------------------------------

    [Fact]
    public async Task Copy_ReportsBytesReachingTotal_AndPreservesContentAndTimestamp()
    {
        using var dir = new TempDir();
        var data = new byte[3 * 1024 * 1024 + 123];
        new Random(1).NextBytes(data);
        var source = Path.Combine(dir.Path, "big.bin");
        File.WriteAllBytes(source, data);
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(source, stamp);
        var destinationDirectory = dir.Dir("out");
        var reports = new List<FileOperationProgress>();

        await _service.CopyAsync(source, destinationDirectory, progress: new SynchronousProgress<FileOperationProgress>(reports.Add));

        var last = reports[^1];
        Assert.Equal(FileOperationState.Starting, reports[0].State);
        Assert.Equal(FileOperationState.Completed, last.State);
        Assert.Equal(data.Length, last.TotalBytes);
        Assert.Equal(data.Length, last.ProcessedBytes);
        Assert.Equal(1, last.TotalFiles);
        Assert.Equal(1, last.ProcessedFiles);
        Assert.Equal(100, last.PercentComplete);
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].ProcessedBytes >= reports[i - 1].ProcessedBytes, "processed bytes must never decrease");
        }

        var copied = Path.Combine(destinationDirectory, "big.bin");
        Assert.Equal(data, File.ReadAllBytes(copied));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(copied));
    }

    [Fact]
    public async Task Copy_Cancelled_RemovesPartialFileAndKeepsFinishedOnes()
    {
        using var dir = new TempDir();
        var source = dir.Dir("src");
        dir.File(Rel("src", "a.txt"), "first");
        dir.File(Rel("src", "b.txt"), "second");
        var destinationDirectory = dir.Dir("out");
        using var cts = new CancellationTokenSource();
        var started = new List<string>();
        var states = new List<FileOperationState>();
        var progress = new SynchronousProgress<FileOperationProgress>(p =>
        {
            states.Add(p.State);
            if (p.State != FileOperationState.InProgress || !p.CurrentFile.EndsWith(".txt", StringComparison.Ordinal) || started.Contains(p.CurrentFile))
            {
                return;
            }

            started.Add(p.CurrentFile);
            if (started.Count == 2)
            {
                cts.Cancel(); // Cancel as the second file (whichever the file system lists second) begins.
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.CopyAsync(source, destinationDirectory, progress: progress, cancellationToken: cts.Token));

        Assert.Equal(2, started.Count);
        var finished = Path.Combine(destinationDirectory, "src", Path.GetFileName(started[0]));
        var partial = Path.Combine(destinationDirectory, "src", Path.GetFileName(started[1]));
        Assert.Equal(File.ReadAllText(started[0]), File.ReadAllText(finished));
        Assert.False(File.Exists(partial));
        Assert.Equal(FileOperationState.Cancelled, states[^1]);
    }

    // ---- symbolic links are never followed ---------------------------------------------------------------

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // Creating links needs a privilege on some Windows configurations.
        }
    }

    [Fact]
    public async Task Delete_DirectorySymbolicLink_RemovesOnlyTheLink()
    {
        using var dir = new TempDir();
        var real = dir.Dir("real");
        var kept = dir.File(Rel("real", "keep.txt"), "keep");
        var link = Path.Combine(dir.Path, "link");
        if (!TryCreateDirectoryLink(link, real))
        {
            return;
        }

        await _service.DeleteAsync(link);

        Assert.False(Directory.Exists(link));
        Assert.Equal("keep", File.ReadAllText(kept));
    }

    [Fact]
    public async Task CopyMoveDelete_DirectorySymbolicLinks_AreHandledAsLinks()
    {
        using var dir = new TempDir();
        var real = dir.Dir("real");
        var kept = dir.File(Rel("real", "keep.txt"), "keep");
        var tree = dir.Dir("tree");
        dir.File(Rel("tree", "a.txt"), "a");
        if (!TryCreateDirectoryLink(Path.Combine(tree, "link"), real))
        {
            return;
        }

        var copyTarget = dir.Dir("copy");
        await _service.CopyAsync(tree, copyTarget);
        Assert.NotNull(new DirectoryInfo(Path.Combine(copyTarget, "tree", "link")).LinkTarget);
        Assert.Equal("a", File.ReadAllText(Path.Combine(copyTarget, "tree", "a.txt")));

        var topLink = Path.Combine(dir.Path, "toplink");
        Directory.CreateSymbolicLink(topLink, real);
        var moveTarget = dir.Dir("moved");
        await _service.MoveAsync(topLink, moveTarget);
        Assert.False(Directory.Exists(topLink));
        Assert.NotNull(new DirectoryInfo(Path.Combine(moveTarget, "toplink")).LinkTarget);

        await _service.DeleteAsync(Path.Combine(copyTarget, "tree"));
        Assert.False(Directory.Exists(Path.Combine(copyTarget, "tree")));
        Assert.Equal("keep", File.ReadAllText(kept));
        Assert.Single(Directory.EnumerateFileSystemEntries(real));
    }

    // ---- delete -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_Directory_RemovesTreeAndReportsEachFile()
    {
        using var dir = new TempDir();
        var target = dir.Dir("docs");
        dir.File(Rel("docs", "a.txt"), "aa");
        dir.File(Rel("docs", "nested", "b.txt"), "bbb");
        dir.File(Rel("docs", "nested", "deeper", "c.txt"), "c");
        var reports = new List<FileOperationProgress>();

        await _service.DeleteAsync(target, new SynchronousProgress<FileOperationProgress>(reports.Add));

        Assert.False(Directory.Exists(target));
        var last = reports[^1];
        Assert.Equal(FileOperationState.Completed, last.State);
        Assert.Equal(3, last.TotalFiles);
        Assert.Equal(3, last.ProcessedFiles);
        Assert.Equal(6, last.ProcessedBytes);
    }

    [Fact]
    public async Task Delete_Missing_ThrowsNotFound()
    {
        using var dir = new TempDir();

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.DeleteAsync(Path.Combine(dir.Path, "ghost")));

        Assert.Equal(FileOperationErrorKind.NotFound, ex.Kind);
    }

    // ---- rename (M1) ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("../x.txt")]
    [InlineData("sub/x.txt")]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    public async Task Rename_RejectsInvalidNames(string newName)
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "a");

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.RenameAsync(file, newName));

        Assert.Equal(FileOperationErrorKind.InvalidName, ex.Kind);
        Assert.Equal("a", File.ReadAllText(file));
        Assert.Single(Directory.EnumerateFileSystemEntries(dir.Path));
    }

    [Fact]
    public async Task Rename_WhenTargetExists_ThrowsAndKeepsBoth()
    {
        using var dir = new TempDir();
        var a = dir.File("a.txt", "a");
        var b = dir.File("b.txt", "b");

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.RenameAsync(a, "b.txt"));

        Assert.Equal(FileOperationErrorKind.TargetExists, ex.Kind);
        Assert.Equal("a", File.ReadAllText(a));
        Assert.Equal("b", File.ReadAllText(b));
    }

    [Fact]
    public async Task Rename_CaseOnlyChange_IsAllowed()
    {
        using var dir = new TempDir();
        var file = dir.File("readme.txt", "r");

        await _service.RenameAsync(file, "README.txt");

        var names = Directory.EnumerateFiles(dir.Path).Select(Path.GetFileName).ToArray();
        Assert.Equal(new[] { "README.txt" }, names);
    }

    [Fact]
    public async Task Rename_Directory_Works()
    {
        using var dir = new TempDir();
        var docs = dir.Dir("docs");
        dir.File(Rel("docs", "a.txt"), "a");

        await _service.RenameAsync(docs, "papers");

        Assert.False(Directory.Exists(docs));
        Assert.Equal("a", File.ReadAllText(Path.Combine(dir.Path, "papers", "a.txt")));
    }

    [Fact]
    public async Task Rename_Missing_ThrowsNotFound()
    {
        using var dir = new TempDir();

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.RenameAsync(Path.Combine(dir.Path, "ghost.txt"), "x.txt"));

        Assert.Equal(FileOperationErrorKind.NotFound, ex.Kind);
    }

    // ---- misc -------------------------------------------------------------------------------------------

    [Fact]
    public async Task GetDirectorySize_SumsNestedFiles()
    {
        using var dir = new TempDir();
        dir.File("a.bin", new string('a', 10));
        dir.File(Rel("sub", "b.bin"), new string('b', 20));
        dir.File(Rel("sub", "deeper", "c.bin"), new string('c', 30));
        long reported = -1;

        var total = await _service.GetDirectorySizeAsync(dir.Path, new SynchronousProgress<long>(v => reported = v));

        Assert.Equal(60, total);
        Assert.Equal(60, reported);
    }

    [Fact]
    public async Task CreateDirectory_RejectsInvalidLeafName()
    {
        using var dir = new TempDir();

        var ex = await Assert.ThrowsAsync<FileOperationException>(() => _service.CreateDirectoryAsync(Path.Combine(dir.Path, "..")));
        await _service.CreateDirectoryAsync(Path.Combine(dir.Path, "fine"));

        Assert.Equal(FileOperationErrorKind.InvalidName, ex.Kind);
        Assert.True(Directory.Exists(Path.Combine(dir.Path, "fine")));
    }

    [Fact]
    public void IsExecutableOrScript_ByExtensionAndUnixExecuteBit()
    {
        using var dir = new TempDir();
        var plain = dir.File("tool", "#!/bin/sh");

        Assert.True(_service.IsExecutableOrScript(Path.Combine(dir.Path, "setup.exe")));
        Assert.True(_service.IsExecutableOrScript(Path.Combine(dir.Path, "run.sh")));
        Assert.False(_service.IsExecutableOrScript(Path.Combine(dir.Path, "readme.txt")));
        Assert.False(_service.IsExecutableOrScript(plain));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(plain, File.GetUnixFileMode(plain) | UnixFileMode.UserExecute);
            Assert.True(_service.IsExecutableOrScript(plain));
        }
    }

    // ---- audit regressions: rename and the Unix open gate -------------------------------------------------

    [Fact]
    public async Task Rename_CaseOnlyChange_StillRefusesToClobberADifferentEntry()
    {
        // Skipping the Exists pre-check for a case-only rename is what makes it work on a case-insensitive
        // volume mounted on Linux. The safety it gives up has to come back from Move: on a case-sensitive
        // file system "a.txt" and "A.TXT" are two entries, and the rename must not silently destroy one.
        using var dir = new TempDir();
        var lower = dir.File("a.txt", "lower");
        var upper = Path.Combine(dir.Path, "A.TXT");

        if (File.Exists(upper))
        {
            // Case-insensitive host: "A.TXT" is the same entry, so the rename is the legitimate case-only one.
            await _service.RenameAsync(lower, "A.TXT");
            Assert.Equal("lower", File.ReadAllText(Path.Combine(dir.Path, "A.TXT")));
            return;
        }

        File.WriteAllText(upper, "upper");

        await Assert.ThrowsAnyAsync<Exception>(() => _service.RenameAsync(lower, "A.TXT"));

        Assert.Equal("lower", File.ReadAllText(lower));
        Assert.Equal("upper", File.ReadAllText(upper));
    }

    [Fact]
    public void IsExecutableOrScript_UnixScriptWithAnUnknownExtension_IsRecognised()
    {
        // The extension list decided alone whenever a name had any extension, so a +x hello.py was opened
        // without the confirmation the contract promises, and .NET then executed it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var script = dir.File("hello.py", "#!/usr/bin/env python3\nprint('hi')\n");
        File.SetUnixFileMode(script, File.GetUnixFileMode(script) | UnixFileMode.UserExecute);

        Assert.True(_service.IsExecutableOrScript(script));
    }

    [Fact]
    public void IsExecutableOrScript_UnixElfWithAnArbitraryExtension_IsRecognised()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var binary = Path.Combine(dir.Path, "tool.1");
        File.WriteAllBytes(binary, [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0]);
        File.SetUnixFileMode(binary, File.GetUnixFileMode(binary) | UnixFileMode.UserExecute);

        Assert.True(_service.IsExecutableOrScript(binary));
    }

    [Fact]
    public void IsExecutableOrScript_UnixDataCarryingTheExecuteBit_IsNotAProgram()
    {
        // Everything on a FAT or NTFS-3g mount is mode 0777; the magic number is what keeps those files from
        // being treated as programs.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var notes = dir.File("notes.txt", "just text");
        var data = dir.File("archive.dat", "not a program either");
        File.SetUnixFileMode(notes, File.GetUnixFileMode(notes) | UnixFileMode.UserExecute);
        File.SetUnixFileMode(data, File.GetUnixFileMode(data) | UnixFileMode.UserExecute);

        Assert.False(_service.IsExecutableOrScript(notes));
        Assert.False(_service.IsExecutableOrScript(data));
    }

    [Fact]
    public void IsExecutableOrScript_KnownExtensionsAreRecognisedWithoutTheExecuteBit()
    {
        using var dir = new TempDir();

        Assert.True(_service.IsExecutableOrScript(dir.File("app.jar", "x")));
        Assert.True(_service.IsExecutableOrScript(dir.File("start.tool", "x")));
        Assert.True(_service.IsExecutableOrScript(dir.File("panel.cpl", "x")));
    }
}
