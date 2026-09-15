using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// What the SFTP session settles before it opens a socket: which private key it will use, and what it says when
/// there is none it can use. The protocol itself is SSH.NET's and needs a real server; these never reach one.
/// </summary>
public class SftpServiceTests
{
    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static SftpSite Site(string keyPath) => new()
    {
        Host = "example.com",
        Port = 22,
        Username = "ana",
        Authentication = SftpAuthentication.PrivateKey,
        KeyPath = keyPath
    };

    /// <summary>
    /// The field's own hint says keys live in "~/.ssh", so that is what gets typed into it. Unexpanded, the key
    /// was quietly skipped and the connection failed saying no key had been found anywhere.
    /// </summary>
    [Fact]
    public async Task AKeyPathWithTheHomeShortcut_IsLookedUpInTheHomeFolder()
    {
        using var service = new SftpService();

        var error = await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ConnectAsync(Site("~/.ssh/not-a-real-key"), null));

        Assert.Contains(Path.Combine(Home, ".ssh", "not-a-real-key"), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("~", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AKeyThatIsNotThere_SaysWhereItLooked()
    {
        using var service = new SftpService();
        var missing = Path.Combine(Path.GetTempPath(), "sharpcommander-no-key");

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.ConnectAsync(Site(missing), null));

        // Sending someone who pointed at a key off to ~/.ssh is sending them to the wrong place.
        Assert.Contains(missing, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(".ssh", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFileThatIsNotAKey_SaysSoRatherThanBlamingTheFolder()
    {
        using var dir = new TempDir();
        using var service = new SftpService();
        var notAKey = dir.File("notes.txt", "this is not a private key");

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => service.ConnectAsync(Site(notAKey), null));

        Assert.Contains(notAKey, error.Message, StringComparison.Ordinal);
        Assert.Contains("passphrase", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
