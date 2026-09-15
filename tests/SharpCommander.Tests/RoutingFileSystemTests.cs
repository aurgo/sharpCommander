using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using SharpCommander.Tests.Fakes;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The router: which side of the local/remote boundary each operation lands on, and that files crossing it
/// arrive intact. The server is an in-memory stand-in — the protocol itself is SSH.NET's business and needs a
/// real host, but the routing is ours and is what breaks.
/// </summary>
public class RoutingFileSystemTests
{
    private const string Remote = "sftp://ana@example.com:22";

    private static (RoutingFileSystemService Router, FakeSftpServer Server) Build()
    {
        var server = new FakeSftpServer();
        var connections = new SftpConnections(() => server);
        connections.ConnectAsync(new SftpSite { Host = "example.com", Port = 22, Username = "ana" }, null).GetAwaiter().GetResult();
        return (new RoutingFileSystemService(new FileSystemService(), connections), server);
    }

    [Fact]
    public async Task ListsARemoteFolderWithNavigableAddresses()
    {
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "hello");
        server.AddDirectory("/home/ana/docs");

        var entries = await router.GetEntriesAsync($"{Remote}/home/ana");

        // The panel navigates by FullPath, so a listing has to hand back addresses, not bare server paths.
        Assert.Contains(entries, e => e.FullPath == $"{Remote}/home/ana/notes.txt");
        Assert.Contains(entries, e => e.FullPath == $"{Remote}/home/ana/docs" && e.EntryType == FileSystemEntryType.Directory);
    }

    [Fact]
    public void LocalPathsStillGoToTheLocalDisk()
    {
        using var dir = new TempDir();
        var (router, _) = Build();
        var file = dir.File("a.txt", "x");

        Assert.True(router.Exists(file));
        Assert.True(router.IsDirectory(dir.Path));
        Assert.Equal(dir.Path, router.GetParentPath(file));
    }

    /// <summary>
    /// A server never sends ".."; the router adds it, as every other listing has one. Without it a remote
    /// folder was the only place in the application with no way back up in the list itself.
    /// </summary>
    [Fact]
    public async Task ARemoteListingOpensWithAWayBackUp()
    {
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "hello");

        var entries = await router.GetEntriesAsync($"{Remote}/home/ana");

        var parent = entries[0];
        Assert.Equal("..", parent.Name);
        Assert.Equal(FileSystemEntryType.ParentDirectory, parent.EntryType);
        Assert.Equal($"{Remote}/home", parent.FullPath);
    }

    [Fact]
    public async Task TheRootOfAServerHasNoWayBackUp()
    {
        var (router, server) = Build();
        server.AddDirectory("/home");

        var entries = await router.GetEntriesAsync($"{Remote}/");

        Assert.DoesNotContain(entries, entry => entry.EntryType == FileSystemEntryType.ParentDirectory);
        Assert.Contains(entries, entry => entry.FullPath == $"{Remote}/home");
    }

    [Fact]
    public void RemoteParentStaysOnTheServer()
    {
        var (router, server) = Build();
        server.AddDirectory("/home/ana/docs");

        Assert.Equal($"{Remote}/home/ana", router.GetParentPath($"{Remote}/home/ana/docs"));
        Assert.True(router.IsDirectory($"{Remote}/home/ana/docs"));
        Assert.False(router.Exists($"{Remote}/home/ana/missing"));
    }

    // ---- the four transfer combinations ----------------------------------------------------------------------

    [Fact]
    public async Task UploadsAFileFromTheLocalDisk()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddDirectory("/home/ana");
        var file = dir.File("report.txt", "contents");

        await router.CopyAsync(file, $"{Remote}/home/ana");

        Assert.Equal("contents", server.ReadFile("/home/ana/report.txt"));
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task MovingUpwardsRemovesTheLocalOriginal()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddDirectory("/home/ana");
        var file = dir.File("report.txt", "contents");

        await router.MoveAsync(file, $"{Remote}/home/ana");

        Assert.Equal("contents", server.ReadFile("/home/ana/report.txt"));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task UploadsAWholeFolder()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddDirectory("/home/ana");
        var folder = dir.Dir("box");
        dir.File("box/one.txt", "1");
        dir.File("box/deep/two.txt", "2");

        await router.CopyAsync(folder, $"{Remote}/home/ana");

        Assert.Equal("1", server.ReadFile("/home/ana/box/one.txt"));
        Assert.Equal("2", server.ReadFile("/home/ana/box/deep/two.txt"));
    }

    [Fact]
    public async Task DownloadsAFile()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "from the server");

        await router.CopyAsync($"{Remote}/home/ana/notes.txt", dir.Path);

        Assert.Equal("from the server", await File.ReadAllTextAsync(Path.Combine(dir.Path, "notes.txt")));
        Assert.True(server.HasFile("/home/ana/notes.txt"));
    }

    [Fact]
    public async Task MovingDownwardsRemovesTheRemoteOriginal()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "from the server");

        await router.MoveAsync($"{Remote}/home/ana/notes.txt", dir.Path);

        Assert.True(File.Exists(Path.Combine(dir.Path, "notes.txt")));
        Assert.False(server.HasFile("/home/ana/notes.txt"));
    }

    [Fact]
    public async Task DownloadingTwiceKeepsBothInsteadOfOverwriting()
    {
        using var dir = new TempDir();
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "server copy");

        await router.CopyAsync($"{Remote}/home/ana/notes.txt", dir.Path);
        await router.CopyAsync($"{Remote}/home/ana/notes.txt", dir.Path);

        Assert.Equal(2, Directory.GetFiles(dir.Path, "notes*").Length);
    }

    [Fact]
    public async Task CopyingWithinTheServerNeverLeavesIt()
    {
        var (router, server) = Build();
        server.AddFile("/home/ana/notes.txt", "stay put");
        server.AddDirectory("/home/ana/archive");

        await router.CopyAsync($"{Remote}/home/ana/notes.txt", $"{Remote}/home/ana/archive");

        Assert.Equal("stay put", server.ReadFile("/home/ana/archive/notes.txt"));
        Assert.True(server.HasFile("/home/ana/notes.txt"));
    }

    [Fact]
    public async Task LocalToLocalIsUntouched()
    {
        using var dir = new TempDir();
        var (router, _) = Build();
        var target = dir.Dir("target");
        var file = dir.File("a.txt", "x");

        await router.CopyAsync(file, target);

        Assert.True(File.Exists(Path.Combine(target, "a.txt")));
    }

    // ---- the everyday commands ------------------------------------------------------------------------------

    [Fact]
    public async Task CreatesDeletesAndRenamesOnTheServer()
    {
        var (router, server) = Build();
        server.AddDirectory("/home/ana");

        await router.CreateDirectoryAsync($"{Remote}/home/ana/nueva");
        Assert.True(server.HasDirectory("/home/ana/nueva"));

        await router.RenameAsync($"{Remote}/home/ana/nueva", "renombrada");
        Assert.True(server.HasDirectory("/home/ana/renombrada"));
        Assert.False(server.HasDirectory("/home/ana/nueva"));

        await router.DeleteAsync($"{Remote}/home/ana/renombrada");
        Assert.False(server.HasDirectory("/home/ana/renombrada"));
    }

    [Fact]
    public async Task MeasuresARemoteFolder()
    {
        var (router, server) = Build();
        server.AddFile("/home/ana/box/a.txt", new string('x', 10));
        server.AddFile("/home/ana/box/b.txt", new string('y', 5));

        Assert.Equal(15, await router.GetDirectorySizeAsync($"{Remote}/home/ana/box"));
    }

    [Fact]
    public async Task PermissionsReachTheServerAndWindowsFlagsDoNot()
    {
        var (router, server) = Build();
        server.AddFile("/home/ana/script.sh", "#!/bin/sh");
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        var changed = await router.ApplyAttributesAsync([$"{Remote}/home/ana/script.sh"], new AttributeChange { UnixMode = mode });

        Assert.Equal(1, changed);
        Assert.Equal(mode, server.Permissions["/home/ana/script.sh"]);
    }

    // ---- what a server honestly cannot do -------------------------------------------------------------------

    [Fact]
    public async Task RefusesTheCommandsThatHaveNoRemoteMeaning()
    {
        var (router, _) = Build();

        await Assert.ThrowsAsync<NotSupportedException>(() => router.OpenTerminalAsync($"{Remote}/home/ana"));
        await Assert.ThrowsAsync<NotSupportedException>(() => router.RevealInFileExplorerAsync($"{Remote}/home/ana/x"));
        await Assert.ThrowsAsync<NotSupportedException>(() => router.OpenInFileExplorerAsync($"{Remote}/home/ana"));
        await Assert.ThrowsAsync<NotSupportedException>(() => router.FindFirstDifferenceAsync($"{Remote}/a", "/tmp/b"));
    }

    [Fact]
    public async Task RefusesToCopyStraightBetweenTwoServers()
    {
        var (router, _) = Build();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => router.CopyAsync($"{Remote}/home/ana/x", "sftp://luis@other.example.com:22/tmp"));
    }

    [Fact]
    public void AnUnconnectedEndpointIsSimplyNotThere()
    {
        var (router, _) = Build();

        // Exists and IsDirectory answer false rather than throwing: the panels call them while merely drawing.
        Assert.False(router.Exists("sftp://luis@other.example.com:22/tmp"));
        Assert.False(router.IsDirectory("sftp://luis@other.example.com:22/tmp"));
    }

    [Fact]
    public async Task AnUnconnectedEndpointFailsLoudlyWhenItMatters()
    {
        var (router, _) = Build();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GetEntriesAsync("sftp://luis@other.example.com:22/tmp"));
    }
}
