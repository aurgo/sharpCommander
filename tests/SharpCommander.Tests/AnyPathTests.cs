using SharpCommander.Core.Utilities;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The path arithmetic used wherever a path may be local or on a server. The local rules are PathUtils' own and
/// are tested there; what matters here is that a remote address survives intact and that the two sides never mix.
/// </summary>
public class AnyPathTests
{
    private const string Remote = "sftp://ana@example.com:22";

    [Fact]
    public void NormalizeLeavesARemoteAddressAlone()
    {
        // Path.GetFullPath would resolve it against the working directory and hand back a local path.
        Assert.Equal($"{Remote}/home/ana", AnyPath.Normalize($"{Remote}/home/ana"));
        Assert.Equal($"{Remote}/home/ana", AnyPath.Normalize($"{Remote}/home/ana/"));
        Assert.Equal($"{Remote}/home/ana", AnyPath.Normalize($"{Remote}//home//ana"));
    }

    [Fact]
    public void NameAndDirectoryFollowTheSideThePathIsOn()
    {
        Assert.Equal("notes.txt", AnyPath.GetName($"{Remote}/home/ana/notes.txt"));
        Assert.Equal($"{Remote}/home/ana", AnyPath.GetDirectory($"{Remote}/home/ana/notes.txt"));

        Assert.Equal(string.Empty, AnyPath.GetName($"{Remote}/"));
        Assert.Null(AnyPath.GetDirectory($"{Remote}/"));

        var local = Path.Combine(Path.GetTempPath(), "notes.txt");
        Assert.Equal("notes.txt", AnyPath.GetName(local));
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), AnyPath.GetDirectory(local));
    }

    [Fact]
    public void CombineJoinsARemoteFolderWithSlashes()
    {
        // Path.Combine would use a backslash here on Windows and break the address.
        Assert.Equal($"{Remote}/home/ana/notes.txt", AnyPath.Combine($"{Remote}/home/ana", "notes.txt"));
        Assert.Equal($"{Remote}/notes.txt", AnyPath.Combine($"{Remote}/", "notes.txt"));
        Assert.Equal(Path.Combine("/tmp", "notes.txt"), AnyPath.Combine("/tmp", "notes.txt"));
    }

    [Fact]
    public void TheTwoSidesAreNeverTheSamePlace()
    {
        Assert.True(AnyPath.AreSame($"{Remote}/home/ana/", $"{Remote}/home/ana"));
        Assert.False(AnyPath.AreSame($"{Remote}/home/ana", $"{Remote}/home/bea"));
        Assert.False(AnyPath.AreSame($"{Remote}/home/ana", "/home/ana"));
        Assert.False(AnyPath.AreSame("/home/ana", $"{Remote}/home/ana"));
    }

    [Fact]
    public void DescendantsAreCountedOnOneServerOnly()
    {
        Assert.True(AnyPath.IsSameOrDescendant($"{Remote}/home/ana", $"{Remote}/home/ana"));
        Assert.True(AnyPath.IsSameOrDescendant($"{Remote}/home/ana", $"{Remote}/home/ana/docs/notes.txt"));
        Assert.True(AnyPath.IsSameOrDescendant($"{Remote}/", $"{Remote}/home"));

        // A sibling whose name merely starts with the same letters, and the same path on another server.
        Assert.False(AnyPath.IsSameOrDescendant($"{Remote}/home/ana", $"{Remote}/home/anabel"));
        Assert.False(AnyPath.IsSameOrDescendant($"{Remote}/home/ana", "sftp://ana@other.example.com:22/home/ana/docs"));
        Assert.False(AnyPath.IsSameOrDescendant($"{Remote}/home/ana", "/home/ana/docs"));
    }

    [Fact]
    public void BothKindsOfRootAreRecognised()
    {
        Assert.True(AnyPath.IsRoot($"{Remote}/"));
        Assert.True(AnyPath.IsRoot(Remote));
        Assert.False(AnyPath.IsRoot($"{Remote}/home"));
        Assert.True(AnyPath.IsRoot(Path.GetPathRoot(Path.GetTempPath())!));
    }
}
