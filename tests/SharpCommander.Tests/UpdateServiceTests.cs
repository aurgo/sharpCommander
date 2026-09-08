using System.Net;
using System.Net.Http;
using System.Text;
using SharpCommander.Desktop.Services;
using Xunit;

namespace SharpCommander.Tests;

/// <summary>
/// The update check, answered by a stub handler rather than github.com: a test that needed the network would
/// fail on a train and prove nothing about this code when it passed.
/// </summary>
public class UpdateServiceTests
{
    private static GitHubUpdateService With(HttpStatusCode status, string body) =>
        new(new HttpClient(new StubHandler(status, body)));

    private static string Release(string tag, bool draft = false) =>
        $$"""
          {"tag_name":"{{tag}}","html_url":"https://github.com/aurgo/sharpCommander/releases/tag/{{tag}}",
           "body":"Notas","draft":{{(draft ? "true" : "false")}}}
          """;

    [Theory]
    [InlineData("v2.2.0", "2.2.0")]
    [InlineData("2.2.0", "2.2.0")]
    [InlineData("V2.2.0", "2.2.0")]
    [InlineData("v2.2.0-beta1", "2.2.0")]
    [InlineData("v2.2", "2.2")]
    public void ParseVersion_ReadsTheUsualTagShapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), GitHubUpdateService.ParseVersion(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    [InlineData("release-candidate")]
    public void ParseVersion_RefusesWhatItCannotOrder(string tag)
    {
        Assert.Null(GitHubUpdateService.ParseVersion(tag));
    }

    [Fact]
    public async Task ReportsANewerRelease()
    {
        using var service = With(HttpStatusCode.OK, Release("v2.2.0"));

        var found = await service.CheckAsync(new Version(2, 1, 0));

        Assert.NotNull(found);
        Assert.True(found.IsNewer);
        Assert.Equal("v2.2.0", found.Tag);
        Assert.Equal(new Version(2, 2, 0), found.Version);
        Assert.Contains("releases/tag/v2.2.0", found.Url);
    }

    [Fact]
    public async Task SaysNothingIsNewerWhenTheBuildIsCurrent()
    {
        using var service = With(HttpStatusCode.OK, Release("v2.1.0"));

        var found = await service.CheckAsync(new Version(2, 1, 0));

        Assert.NotNull(found);
        Assert.False(found.IsNewer);
    }

    [Fact]
    public async Task ANewerLocalBuildIsNotAnUpdate()
    {
        using var service = With(HttpStatusCode.OK, Release("v2.1.0"));

        var found = await service.CheckAsync(new Version(2, 3, 0));

        Assert.False(found!.IsNewer);
    }

    [Fact]
    public async Task IgnoresADraftRelease()
    {
        using var service = With(HttpStatusCode.OK, Release("v9.9.9", draft: true));

        Assert.Null(await service.CheckAsync(new Version(2, 1, 0)));
    }

    [Fact]
    public async Task AnUnorderableTagIsNotAnUpdate()
    {
        using var service = With(HttpStatusCode.OK, Release("nightly"));

        Assert.Null(await service.CheckAsync(new Version(2, 1, 0)));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task AFailedRequestIsSimplyNoAnswer(HttpStatusCode status)
    {
        using var service = With(status, string.Empty);

        Assert.Null(await service.CheckAsync(new Version(2, 1, 0)));
    }

    [Fact]
    public async Task RubbishOnTheWireDoesNotThrow()
    {
        using var service = With(HttpStatusCode.OK, "<html>not json</html>");

        Assert.Null(await service.CheckAsync(new Version(2, 1, 0)));
    }

    [Fact]
    public async Task NoNetworkDoesNotThrow()
    {
        using var service = new GitHubUpdateService(new HttpClient(new ThrowingHandler()));

        Assert.Null(await service.CheckAsync(new Version(2, 1, 0)));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // GitHub refuses a request with no user agent, so the service must always set one.
            Assert.NotEmpty(request.Headers.UserAgent);

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no network");
    }
}
