using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// Exercises the real keychain, so it only runs when asked: <c>SC_KEYCHAIN=1 dotnet test --filter SecretStore</c>.
/// The keychain is shared machine state and can put up its own permission dialog, which would hang a test run
/// that nobody is watching. Entries are written under a throwaway key and removed again, so a run leaves nothing
/// behind.
/// </summary>
public class SecretStoreTests
{
    private static readonly KeychainSecretStore Store = new();

    private static bool Enabled => Environment.GetEnvironmentVariable("SC_KEYCHAIN") == "1" && Store.IsAvailable;

    [Fact]
    public async Task RoundTripsASecret()
    {
        if (!Enabled)
        {
            return;
        }

        var key = "sharpcommander-test://" + Guid.NewGuid().ToString("N");
        try
        {
            await Store.SetAsync(key, "correct horse battery staple");

            Assert.Equal("correct horse battery staple", await Store.GetAsync(key));
        }
        finally
        {
            await Store.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task ReplacesAnExistingSecret()
    {
        if (!Enabled)
        {
            return;
        }

        var key = "sharpcommander-test://" + Guid.NewGuid().ToString("N");
        try
        {
            await Store.SetAsync(key, "first");
            await Store.SetAsync(key, "second");

            Assert.Equal("second", await Store.GetAsync(key));
        }
        finally
        {
            await Store.RemoveAsync(key);
        }
    }

    [Fact]
    public async Task ReadingAnUnknownKeyReturnsNull()
    {
        if (!Enabled)
        {
            return;
        }

        Assert.Null(await Store.GetAsync("sharpcommander-test://" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task RemovingWhatIsNotThereIsNotAnError()
    {
        if (!Enabled)
        {
            return;
        }

        await Store.RemoveAsync("sharpcommander-test://" + Guid.NewGuid().ToString("N"));
    }

    [Fact]
    public async Task HandlesSecretsWithSpacesAndQuotes()
    {
        if (!Enabled)
        {
            return;
        }

        // The secret goes through standard input precisely so shell-hostile characters cannot break it.
        const string Awkward = "a b\"c'd$e`f\\g";
        var key = "sharpcommander-test://" + Guid.NewGuid().ToString("N");
        try
        {
            await Store.SetAsync(key, Awkward);

            Assert.Equal(Awkward, await Store.GetAsync(key));
        }
        finally
        {
            await Store.RemoveAsync(key);
        }
    }
}
