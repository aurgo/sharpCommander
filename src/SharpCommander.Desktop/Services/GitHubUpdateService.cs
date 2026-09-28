using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Reads the latest release from the GitHub API. The response is parsed with a source-generated contract, like
/// the settings: reflection-based JSON is switched off in this project so trimming and AOT cannot break it.
///
/// The call is anonymous, which GitHub rate-limits per address. One check a week stays far inside that
/// limit, and it means no token has to be stored anywhere.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/aurgo/sharpCommander/releases/latest";

    private readonly HttpClient _client;

    public GitHubUpdateService()
        : this(new HttpClient())
    {
    }

    /// <summary>Creates a service on a given client, so tests can answer without a network.</summary>
    internal GitHubUpdateService(HttpClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _client.Timeout = TimeSpan.FromSeconds(15);

        // GitHub refuses requests with no user agent, and asks callers to name the API version they expect.
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpCommander");
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            using var response = await _client.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warning($"The update check answered {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await System.Text.Json.JsonSerializer.DeserializeAsync(
                stream, GitHubJsonContext.Default.GitHubRelease, cancellationToken);

            if (release?.TagName is not { Length: > 0 } tag || release.Draft)
            {
                return null;
            }

            if (ParseVersion(tag) is not { } version)
            {
                AppLog.Warning($"The latest release is tagged '{tag}', which is not a version this can compare.");
                return null;
            }

            return new UpdateInfo
            {
                Version = version,
                Tag = tag,
                Url = release.HtmlUrl ?? "https://github.com/aurgo/sharpCommander/releases",
                Notes = release.Body ?? string.Empty,
                IsNewer = version > currentVersion
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // No network, a slow answer or something unexpected on the wire: not worth bothering anyone with.
            AppLog.Warning("The update check could not be completed.", ex);
            return null;
        }
    }

    /// <summary>Reads "v2.2.0" or "2.2.0" as a version; anything else is refused rather than guessed at.</summary>
    internal static Version? ParseVersion(string tag)
    {
        var trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        // A pre-release suffix ("2.2.0-beta1") is dropped: the numbers are what can be ordered.
        var dash = trimmed.IndexOf('-');
        if (dash > 0)
        {
            trimmed = trimmed[..dash];
        }

        return Version.TryParse(trimmed, out var version) ? version : null;
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>The handful of fields this needs from a GitHub release.</summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];
}

/// <summary>A file attached to a GitHub release.</summary>
internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>"sha256:&lt;hex&gt;", computed by GitHub when the file was uploaded; missing on older uploads.</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class GitHubJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
