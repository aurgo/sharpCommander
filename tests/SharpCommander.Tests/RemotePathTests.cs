using SharpCommander.Core.Models;
using SharpCommander.Core.Utilities;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Remote path arithmetic and the parts of the SFTP service that need no server. The connection itself is not
/// covered here: it needs a real host, and a fake one would only test the fake.
/// </summary>
public class RemotePathTests
{
    [Theory]
    [InlineData("/home/user", "file.txt", "/home/user/file.txt")]
    [InlineData("/home/user/", "file.txt", "/home/user/file.txt")]
    [InlineData("/", "file.txt", "/file.txt")]
    [InlineData("/home", "/absolute/elsewhere", "/absolute/elsewhere")]
    [InlineData("/home//user", "sub//deep", "/home/user/sub/deep")]
    public void Combine_JoinsWithASingleSlash(string basePath, string name, string expected)
    {
        Assert.Equal(expected, RemotePath.Combine(basePath, name));
    }

    [Theory]
    [InlineData("/home/user/file.txt", "/home/user")]
    [InlineData("/home", "/")]
    [InlineData("/", null)]
    [InlineData("", null)]
    public void GetParent_WalksUpAndStopsAtTheRoot(string path, string? expected)
    {
        Assert.Equal(expected, RemotePath.GetParent(path));
    }

    [Theory]
    [InlineData("/home/user/file.txt", "file.txt")]
    [InlineData("/home/user/", "user")]
    [InlineData("/", "/")]
    public void GetName_ReturnsTheLastSegment(string path, string expected)
    {
        Assert.Equal(expected, RemotePath.GetName(path));
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("   ", "/")]
    [InlineData("home/user", "/home/user")]
    [InlineData("//home///user//", "/home/user")]
    public void Normalize_AlwaysProducesAPosixPath(string? path, string expected)
    {
        Assert.Equal(expected, RemotePath.Normalize(path));
    }

    // ---- the site model -----------------------------------------------------------------------------------

    [Fact]
    public void CredentialKey_SeparatesAccountsPortsAndHosts()
    {
        var a = new SftpSite { Host = "example.com", Port = 22, Username = "ana" };
        var b = new SftpSite { Host = "example.com", Port = 22, Username = "luis" };
        var c = new SftpSite { Host = "example.com", Port = 2222, Username = "ana" };

        Assert.NotEqual(a.CredentialKey, b.CredentialKey);
        Assert.NotEqual(a.CredentialKey, c.CredentialKey);
        Assert.Equal("sftp://ana@example.com:22", a.CredentialKey);
    }

    [Fact]
    public void DisplayName_FallsBackToUserAtHost()
    {
        Assert.Equal("ana@example.com", new SftpSite { Host = "example.com", Username = "ana" }.DisplayName);
        Assert.Equal("Trabajo", new SftpSite { Host = "example.com", Username = "ana", Name = "Trabajo" }.DisplayName);
    }

    // ---- the service without a connection -----------------------------------------------------------------

    [Fact]
    public async Task EveryOperationRefusesWhenNotConnected()
    {
        using var service = new SftpService();

        Assert.False(service.IsConnected);
        Assert.Null(service.Site);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ListAsync("/"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetStartDirectoryAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDirectoryAsync("/tmp/x"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("/tmp/x"));
    }

    [Fact]
    public void DisconnectingTwiceIsHarmless()
    {
        using var service = new SftpService();

        service.Disconnect();
        service.Disconnect();

        Assert.False(service.IsConnected);
    }

    [Fact]
    public async Task ConnectingWithoutAPasswordIsRefusedBeforeAnyNetworkCall()
    {
        using var service = new SftpService();
        var site = new SftpSite { Host = "example.invalid", Username = "ana", Authentication = SftpAuthentication.Password };

        // No password: this must fail on the argument, not by trying to reach a host that does not exist.
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConnectAsync(site, null));
    }

    [Fact]
    public async Task ConnectingWithAMissingKeySaysSoInsteadOfDialling()
    {
        using var service = new SftpService();
        var site = new SftpSite
        {
            Host = "example.invalid",
            Username = "ana",
            Authentication = SftpAuthentication.PrivateKey,
            KeyPath = Path.Combine(Path.GetTempPath(), "sharpcommander-no-such-key")
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.ConnectAsync(site, null));
    }

    // ---- remote addresses ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("sftp://ana@example.com:22/home/ana", "ana", "example.com", 22, "/home/ana")]
    [InlineData("sftp://ana@example.com/home/ana", "ana", "example.com", 22, "/home/ana")]
    [InlineData("sftp://ana@example.com:2222/", "ana", "example.com", 2222, "/")]
    [InlineData("sftp://ana@example.com:2222", "ana", "example.com", 2222, "/")]
    [InlineData("sftp://a.b@example.com/x", "a.b", "example.com", 22, "/x")]
    public void TryParse_ReadsEveryPart(string path, string user, string host, int port, string remote)
    {
        var address = SftpAddress.TryParse(path);

        Assert.NotNull(address);
        Assert.Equal(user, address.Username);
        Assert.Equal(host, address.Host);
        Assert.Equal(port, address.Port);
        Assert.Equal(remote, address.Path);
    }

    [Theory]
    [InlineData("/home/ana")]
    [InlineData("C:\\Users\\ana")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("sftp://example.com/x")]
    [InlineData("sftp://ana@/x")]
    [InlineData("sftp://ana@example.com:notaport/x")]
    [InlineData("sftp://ana@example.com:99999/x")]
    public void TryParse_RefusesWhatIsNotARemotePath(string? path)
    {
        Assert.Null(SftpAddress.TryParse(path));
    }

    [Fact]
    public void ToString_RoundTripsThroughTryParse()
    {
        const string Path = "sftp://ana@example.com:2222/home/ana/docs";

        Assert.Equal(Path, SftpAddress.TryParse(Path)!.ToString());
    }

    [Fact]
    public void CombineAndParent_StayOnTheSameServer()
    {
        var address = SftpAddress.TryParse("sftp://ana@example.com:2222/home/ana")!;

        Assert.Equal("sftp://ana@example.com:2222/home/ana/docs", address.Combine("docs"));
        Assert.Equal("sftp://ana@example.com:2222/home", address.Parent());
        Assert.Equal("sftp://ana@example.com:2222/", SftpAddress.TryParse("sftp://ana@example.com:2222/home")!.Parent());
        Assert.Null(SftpAddress.TryParse("sftp://ana@example.com:2222/")!.Parent());
    }

    [Fact]
    public void IsRemote_IsTheSameTestAsParsing()
    {
        Assert.True(SftpAddress.IsRemote("sftp://ana@example.com/x"));
        Assert.True(SftpAddress.IsRemote("SFTP://ana@example.com/x"));
        Assert.False(SftpAddress.IsRemote("/home/ana"));
        Assert.False(SftpAddress.IsRemote(null));
    }
}
