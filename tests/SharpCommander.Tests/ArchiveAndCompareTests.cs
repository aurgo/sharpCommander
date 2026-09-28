using System.IO.Compression;
using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>Packing and extracting zips, and the folder comparison behind Compare and Synchronize.</summary>
public class ArchiveAndCompareTests
{
    private static readonly CompositeArchiveService Archives = new(new ZipArchiveService(), new TarArchiveService());
    private static readonly DirectoryComparer Comparer = new();

    // ---- packing --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_PacksFilesAndFoldersAndKeepsTheFolderName()
    {
        using var dir = new TempDir();
        var file = dir.File("loose.txt", "hello");
        var folder = dir.Dir("box");
        dir.File("box/inner.txt", "inside");
        dir.File("box/deep/deeper.txt", "deep");
        var archive = Path.Combine(dir.Path, "out.zip");

        var written = await Archives.CreateAsync([file, folder], archive);

        Assert.Equal(3, written);
        using var zip = ZipFile.OpenRead(archive);
        Assert.Equal(
            ["box/deep/deeper.txt", "box/inner.txt", "loose.txt"],
            zip.Entries.Select(entry => entry.FullName).Order());
    }

    [Fact]
    public async Task Create_RefusesToOverwriteAndLeavesTheExistingFileAlone()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "x");
        var archive = dir.File("out.zip", "not really an archive");

        await Assert.ThrowsAsync<IOException>(() => Archives.CreateAsync([file], archive));

        Assert.Equal("not really an archive", await File.ReadAllTextAsync(archive));
    }

    [Fact]
    public async Task Create_RemovesTheArchiveWhenPackingFails()
    {
        using var dir = new TempDir();
        var archive = Path.Combine(dir.Path, "out.zip");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Archives.CreateAsync([dir.File("a.txt")], archive, null, cts.Token));

        Assert.False(File.Exists(archive));
    }

    // ---- extracting -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Extract_RestoresTheTree()
    {
        using var dir = new TempDir();
        var folder = dir.Dir("box");
        dir.File("box/inner.txt", "inside");
        var archive = Path.Combine(dir.Path, "out.zip");
        await Archives.CreateAsync([folder], archive);
        var destination = dir.Dir("out");

        var extracted = await Archives.ExtractAsync(archive, destination);

        Assert.Equal(1, extracted);
        Assert.Equal("inside", await File.ReadAllTextAsync(Path.Combine(destination, "box", "inner.txt")));
    }

    [Fact]
    public async Task Extract_RefusesAnEntryThatWouldEscapeTheDestination()
    {
        using var dir = new TempDir();
        var archive = Path.Combine(dir.Path, "evil.zip");
        var destination = dir.Dir("out");

        using (var stream = new FileStream(archive, FileMode.CreateNew))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            // A classic "zip slip": the name climbs out of whatever folder it is extracted into.
            var entry = zip.CreateEntry("../escaped.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("pwned");
        }

        await Assert.ThrowsAsync<IOException>(() => Archives.ExtractAsync(archive, destination));

        Assert.False(File.Exists(Path.Combine(dir.Path, "escaped.txt")));
    }

    [Fact]
    public void ZipService_OnlyClaimsZip()
    {
        var zip = new ZipArchiveService();

        Assert.True(zip.IsArchive("a.zip"));
        Assert.True(zip.IsArchive("A.ZIP"));
        Assert.False(zip.IsArchive("a.tar.gz"));
        Assert.False(zip.IsArchive("a.txt"));
    }

    // ---- comparing ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Compare_ClassifiesEachName()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.File("left/same.txt", "identical");
        dir.File("right/same.txt", "identical");
        dir.File("left/different.txt", "short");
        dir.File("right/different.txt", "a good deal longer");
        dir.File("left/only-left.txt", "x");
        dir.File("right/only-right.txt", "x");

        // Copies keep their write time so "same" is about content, not about when the test ran.
        File.SetLastWriteTimeUtc(Path.Combine(right, "same.txt"), File.GetLastWriteTimeUtc(Path.Combine(left, "same.txt")));

        var result = await Comparer.CompareAsync(left, right, includeHidden: false);

        Assert.Equal(ComparisonState.Same, State(result, "same.txt"));
        Assert.Equal(ComparisonState.Different, State(result, "different.txt"));
        Assert.Equal(ComparisonState.OnlyLeft, State(result, "only-left.txt"));
        Assert.Equal(ComparisonState.OnlyRight, State(result, "only-right.txt"));
        Assert.Equal(1, result.Same);
        Assert.Equal(1, result.Different);
        Assert.Equal(1, result.OnlyLeft);
        Assert.Equal(1, result.OnlyRight);
    }

    [Fact]
    public async Task Compare_TreatsAFolderAndAFileOfTheSameNameAsDifferent()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        dir.Dir("left/thing");
        dir.File("right/thing", "a file this time");

        var result = await Comparer.CompareAsync(left, right, includeHidden: false);

        Assert.Equal(ComparisonState.Different, State(result, "thing"));
    }

    [Fact]
    public async Task Compare_SkipsHiddenEntriesUnlessAsked()
    {
        using var dir = new TempDir();
        var left = dir.Dir("left");
        var right = dir.Dir("right");
        var hidden = dir.File("left/.hidden", "x");

        // A leading dot hides a file on Unix; Windows goes by the attribute alone.
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        }

        Assert.Empty((await Comparer.CompareAsync(left, right, includeHidden: false)).Items);
        Assert.Single((await Comparer.CompareAsync(left, right, includeHidden: true)).Items);
    }

    private static ComparisonState State(DirectoryComparison comparison, string name)
    {
        return comparison.Items.Single(item => item.Name == name).State;
    }

    // ---- tar ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("out.tar")]
    [InlineData("out.tar.gz")]
    [InlineData("out.tgz")]
    public async Task Tar_RoundTripsFilesAndFolders(string archiveName)
    {
        using var dir = new TempDir();
        var file = dir.File("loose.txt", "hello");
        var folder = dir.Dir("box");
        dir.File("box/inner.txt", "inside");
        var archive = Path.Combine(dir.Path, archiveName);
        var destination = dir.Dir("out-" + Path.GetFileNameWithoutExtension(archiveName));

        var written = await Archives.CreateAsync([file, folder], archive);
        var extracted = await Archives.ExtractAsync(archive, destination);

        Assert.Equal(2, written);
        Assert.Equal(2, extracted);
        Assert.Equal("hello", await File.ReadAllTextAsync(Path.Combine(destination, "loose.txt")));
        Assert.Equal("inside", await File.ReadAllTextAsync(Path.Combine(destination, "box", "inner.txt")));
    }

    [Fact]
    public async Task Tar_GzipActuallyCompresses()
    {
        using var dir = new TempDir();
        var file = dir.File("big.txt", new string('a', 200_000));
        var plain = Path.Combine(dir.Path, "plain.tar");
        var gzipped = Path.Combine(dir.Path, "small.tar.gz");

        await Archives.CreateAsync([file], plain);
        await Archives.CreateAsync([file], gzipped);

        Assert.True(new FileInfo(gzipped).Length < new FileInfo(plain).Length / 10);
    }

    [Fact]
    public void Composite_RecognisesEveryRegisteredFormat()
    {
        Assert.True(Archives.IsArchive("a.zip"));
        Assert.True(Archives.IsArchive("a.tar"));
        Assert.True(Archives.IsArchive("a.tar.gz"));
        Assert.True(Archives.IsArchive("a.TGZ"));
        Assert.False(Archives.IsArchive("a.rar"));
    }

    [Fact]
    public async Task Composite_RefusesAnUnknownFormat()
    {
        using var dir = new TempDir();
        await Assert.ThrowsAsync<NotSupportedException>(
            () => Archives.CreateAsync([dir.File("a.txt")], Path.Combine(dir.Path, "out.rar")));
    }

    [Fact]
    public async Task Tar_RefusesAnEntryThatWouldEscapeTheDestination()
    {
        using var dir = new TempDir();
        var archive = Path.Combine(dir.Path, "evil.tar");
        var destination = dir.Dir("out");

        await using (var stream = File.Create(archive))
        using (var writer = new System.Formats.Tar.TarWriter(stream, System.Formats.Tar.TarEntryFormat.Pax))
        {
            var payload = dir.File("payload.txt", "pwned");
            writer.WriteEntry(payload, "../escaped.txt");
        }

        await Assert.ThrowsAsync<IOException>(() => Archives.ExtractAsync(archive, destination));

        Assert.False(File.Exists(Path.Combine(dir.Path, "escaped.txt")));
    }

    // ---- byte-for-byte file comparison and attributes --------------------------------------------------------

    private static readonly FileSystemService FileSystem = new();

    [Fact]
    public async Task FindFirstDifference_ReportsMinusOneForIdenticalFiles()
    {
        using var dir = new TempDir();
        var a = dir.File("a.bin", new string('x', 200_000));
        var b = dir.File("b.bin", new string('x', 200_000));

        Assert.Equal(-1, await FileSystem.FindFirstDifferenceAsync(a, b));
    }

    [Fact]
    public async Task FindFirstDifference_ReportsTheOffsetOfTheFirstDifferingByte()
    {
        using var dir = new TempDir();
        var a = dir.File("a.bin", new string('x', 100) + "A" + new string('x', 100));
        var b = dir.File("b.bin", new string('x', 100) + "B" + new string('x', 100));

        Assert.Equal(100, await FileSystem.FindFirstDifferenceAsync(a, b));
    }

    [Fact]
    public async Task FindFirstDifference_ReportsTheShorterLengthWhenOneIsAPrefix()
    {
        using var dir = new TempDir();
        var a = dir.File("a.bin", "abc");
        var b = dir.File("b.bin", "abcdef");

        Assert.Equal(3, await FileSystem.FindFirstDifferenceAsync(a, b));
    }

    [Fact]
    public async Task FindFirstDifference_SpansTheBufferBoundary()
    {
        using var dir = new TempDir();
        // The reader works in 64 KB blocks; the difference sits just past the first one.
        var a = dir.File("a.bin", new string('x', 70_000));
        var b = dir.File("b.bin", new string('x', 65_600) + "Y" + new string('x', 4_399));

        Assert.Equal(65_600, await FileSystem.FindFirstDifferenceAsync(a, b));
    }

    [Fact]
    public async Task ApplyAttributes_SetsAndClearsReadOnly()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "x");

        var changed = await FileSystem.ApplyAttributesAsync([file], new AttributeChange { ReadOnly = true });
        Assert.Equal(1, changed);
        Assert.True(new FileInfo(file).IsReadOnly);

        await FileSystem.ApplyAttributesAsync([file], new AttributeChange { ReadOnly = false });
        Assert.False(new FileInfo(file).IsReadOnly);
    }

    [Fact]
    public async Task ApplyAttributes_LeavesUntouchedFlagsAlone()
    {
        using var dir = new TempDir();
        var file = dir.File("a.txt", "x");
        await FileSystem.ApplyAttributesAsync([file], new AttributeChange { ReadOnly = true });

        // Every flag null means nothing to do, so the batch reports no change at all.
        var changed = await FileSystem.ApplyAttributesAsync([file], new AttributeChange());

        Assert.Equal(0, changed);
        Assert.True(new FileInfo(file).IsReadOnly);

        // Tidy up so the temp folder can be removed.
        await FileSystem.ApplyAttributesAsync([file], new AttributeChange { ReadOnly = false });
    }

    [Fact]
    public async Task ApplyAttributes_RecursesIntoFoldersOnlyWhenAsked()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDir();
        var folder = dir.Dir("box");
        var inner = dir.File("box/inner.txt", "x");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        await FileSystem.ApplyAttributesAsync([folder], new AttributeChange { UnixMode = mode });
        Assert.NotEqual(mode, File.GetUnixFileMode(inner));

        await FileSystem.ApplyAttributesAsync([folder], new AttributeChange { UnixMode = mode, Recursive = true });
        Assert.Equal(mode, File.GetUnixFileMode(inner));
    }
}
