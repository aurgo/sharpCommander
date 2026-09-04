using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Only the pure file system parts are exercised: talking to Finder, gio or the recycle bin would need the
/// real desktop (and on macOS would trigger the automation permission prompt).
/// </summary>
public class TrashServiceTests
{
    [Fact]
    public void IsSupported_OnDesktopPlatforms()
    {
        var expected = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

        Assert.Equal(expected, new TrashService().IsSupported);
    }

    [Fact]
    public void MoveToUserTrashFolder_MovesEntriesUnderUniqueNames()
    {
        using var dir = new TempDir();
        var trash = Path.Combine(dir.Path, "Trash");
        var first = dir.File("note.txt", "1");

        var moved1 = TrashService.MoveToUserTrashFolder(first, trash);
        var second = dir.File("note.txt", "2");
        var moved2 = TrashService.MoveToUserTrashFolder(second, trash);
        var folder = dir.Dir("docs");
        var moved3 = TrashService.MoveToUserTrashFolder(folder, trash);

        Assert.Equal("note.txt", Path.GetFileName(moved1));
        Assert.Equal("note (2).txt", Path.GetFileName(moved2));
        Assert.Equal("docs", Path.GetFileName(moved3));
        Assert.False(File.Exists(first));
        Assert.False(Directory.Exists(folder));
        Assert.Equal("1", File.ReadAllText(moved1));
        Assert.Equal("2", File.ReadAllText(moved2));
        Assert.True(Directory.Exists(moved3));
    }

    [Fact]
    public void MoveToFreedesktopTrash_WritesTrashInfoThenMovesEntry()
    {
        using var dir = new TempDir();
        var root = Path.Combine(dir.Path, "Trash");
        var file = dir.File("my file.txt", "x");

        var moved = TrashService.MoveToFreedesktopTrash(file, root);
        var again = dir.File("my file.txt", "y");
        var moved2 = TrashService.MoveToFreedesktopTrash(again, root);

        Assert.Equal(Path.Combine(root, "files", "my file.txt"), moved);
        Assert.Equal("my file (2).txt", Path.GetFileName(moved2));
        Assert.False(File.Exists(file));
        Assert.Equal("x", File.ReadAllText(moved));
        Assert.Equal("y", File.ReadAllText(moved2));

        var info = File.ReadAllText(Path.Combine(root, "info", "my file.txt.trashinfo"));
        Assert.StartsWith("[Trash Info]\n", info);
        Assert.Contains("Path=" + TrashService.EncodeTrashPath(file) + "\n", info);
        Assert.Contains("DeletionDate=", info);
        Assert.True(File.Exists(Path.Combine(root, "info", "my file (2).txt.trashinfo")));
    }

    [Fact]
    public void EncodeTrashPath_EscapesSegmentsAndKeepsSeparators()
    {
        Assert.Equal("/home/user/my%20file%231.txt", TrashService.EncodeTrashPath("/home/user/my file#1.txt"));
    }
}
