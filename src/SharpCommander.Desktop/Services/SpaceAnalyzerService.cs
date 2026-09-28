using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCommander.Core.Interfaces;
using SharpCommander.Core.Models;

namespace SharpCommander.Desktop.Services;

/// <summary>
/// Downloads, keeps and starts SpaceAnalyzer. Every release goes to a folder named after its version below
/// <see cref="AppPaths.ToolsDirectory"/>, and the newest complete one is the one that runs:
///
/// - a download is written, checked and unpacked in a staging folder, and only a finished copy is renamed into
///   its version folder, so an interrupted or corrupt download never leaves anything that would be started;
/// - a newer release never overwrites the copy in use (Windows cannot replace a running program): it is simply a
///   newer folder, and the older ones are removed once nothing holds them.
///
/// The release is read from the GitHub API anonymously, as <see cref="GitHubUpdateService"/> does, and the file is
/// checked against the SHA-256 that GitHub computed when it was uploaded.
/// </summary>
public sealed class SpaceAnalyzerService : ISpaceAnalyzerService, IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/aurgo/SpaceAnalyzer/releases/latest";

    /// <summary>Prefix of the staging folders; a version folder can never start with it.</summary>
    private const string StagingPrefix = ".download-";

    /// <summary>A staging folder older than this belongs to a download that was interrupted (a crash, a power cut).</summary>
    private static readonly TimeSpan StagingLifetime = TimeSpan.FromDays(1);

    /// <summary>How long "open" may take to hand a bundle to LaunchServices before it is assumed to have worked.</summary>
    private static readonly TimeSpan LaunchServicesTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _client;
    private readonly string _root;
    private readonly SpaceAnalyzerPackage? _package;
    private readonly TimeSpan _stallTimeout;

    public SpaceAnalyzerService()
        : this(new HttpClient(), Path.Combine(AppPaths.ToolsDirectory, "SpaceAnalyzer"), SpaceAnalyzerPackage.ForThisSystem())
    {
    }

    /// <summary>
    /// Creates a service on a given client, folder and package, so tests need neither a network nor the system the
    /// package is for. <paramref name="stallTimeout"/> is how long a download may go without receiving a byte.
    /// </summary>
    internal SpaceAnalyzerService(HttpClient client, string root, SpaceAnalyzerPackage? package, TimeSpan? stallTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(root);

        _client = client;
        _root = root;
        _package = package;
        _stallTimeout = stallTimeout ?? TimeSpan.FromSeconds(30);

        // This bounds the connection and the headers only. The body of a download is read after them and is bounded
        // by the stall timeout instead, so a slow line that keeps delivering is never cut off halfway.
        _client.Timeout = TimeSpan.FromSeconds(30);

        // GitHub refuses requests with no user agent.
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("SharpCommander");
    }

    public bool IsSupported => _package is not null;

    public SpaceAnalyzerInstall? Installed => _package is { } package ? FindInstalled(package) : null;

    public async Task<SpaceAnalyzerRelease?> FindLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_package is not { } package)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warning($"The SpaceAnalyzer release check answered {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync(
                stream, GitHubJsonContext.Default.GitHubRelease, cancellationToken).ConfigureAwait(false);

            if (release?.TagName is not { Length: > 0 } tag || release.Draft)
            {
                return null;
            }

            if (GitHubUpdateService.ParseVersion(tag) is not { } version)
            {
                AppLog.Warning($"The latest SpaceAnalyzer release is tagged '{tag}', which is not a version.");
                return null;
            }

            var asset = release.Assets.FirstOrDefault(candidate => string.Equals(candidate.Name, package.AssetName, StringComparison.Ordinal));
            if (asset?.BrowserDownloadUrl is not { } url
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps)
            {
                AppLog.Warning($"SpaceAnalyzer {tag} publishes no usable {package.AssetName}.");
                return null;
            }

            return new SpaceAnalyzerRelease
            {
                Version = version,
                Tag = tag,
                AssetName = package.AssetName,
                DownloadUrl = url,
                Size = asset.Size,
                Sha256 = ParseSha256(asset.Digest)
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // No network, a slow answer or something unexpected on the wire: the caller says so in its own words.
            AppLog.Warning("The latest SpaceAnalyzer release could not be read.", ex);
            return null;
        }
    }

    public Task<SpaceAnalyzerInstall> InstallAsync(SpaceAnalyzerRelease release, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (_package is not { } package)
        {
            throw new PlatformNotSupportedException("SpaceAnalyzer publishes no build for this system.");
        }

        // Off the UI thread as a whole: hashing and unpacking a few megabytes is not free.
        return Task.Run(() => InstallCoreAsync(package, release, progress, cancellationToken), cancellationToken);
    }

    public Task LaunchAsync(SpaceAnalyzerInstall install, string? folder)
    {
        ArgumentNullException.ThrowIfNull(install);

        var viaLaunchServices = _package?.Packaging == SpaceAnalyzerPackaging.AppBundleZip;
        var startInfo = CreateStartInfo(install.Path, folder, viaLaunchServices);

        return Task.Run(async () =>
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"SpaceAnalyzer could not be started from '{install.Path}'.");

            if (!viaLaunchServices)
            {
                return;
            }

            // "open" hands the bundle to LaunchServices and quits at once; a refusal only shows in how it quits.
            var error = process.StandardError.ReadToEndAsync();
            if (process.WaitForExit(LaunchServicesTimeout) && process.ExitCode != 0)
            {
                throw new InvalidOperationException($"macOS did not open SpaceAnalyzer: {(await error).Trim()}");
            }
        });
    }

    /// <summary>
    /// The command that starts SpaceAnalyzer. A macOS bundle is handed to LaunchServices ("open"), which brings it to
    /// the front with its own Dock icon; "-n" starts a new copy even when one is already open, as on the other
    /// systems, because a copy already open would ignore the folder. Elsewhere the program is run directly.
    /// </summary>
    internal static ProcessStartInfo CreateStartInfo(string entryPoint, string? folder, bool viaLaunchServices)
    {
        if (viaLaunchServices)
        {
            var open = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false, RedirectStandardError = true };
            open.ArgumentList.Add("-n");
            open.ArgumentList.Add("-a");
            open.ArgumentList.Add(entryPoint);

            if (folder is not null)
            {
                open.ArgumentList.Add("--args");
                open.ArgumentList.Add(folder);
            }

            return open;
        }

        var startInfo = new ProcessStartInfo(entryPoint)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(entryPoint) ?? string.Empty
        };

        if (folder is not null)
        {
            startInfo.ArgumentList.Add(folder);
        }

        return startInfo;
    }

    /// <summary>Reads GitHub's "sha256:&lt;hex&gt;" digest as lowercase hex; anything else counts as no digest.</summary>
    internal static string? ParseSha256(string? digest)
    {
        const string Prefix = "sha256:";

        if (digest is null || !digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = digest[Prefix.Length..].Trim();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The newest version folder that holds the program. Folders that are not versions (staging, anything put there
    /// by hand) and versions that lack the program are passed over.
    /// </summary>
    private SpaceAnalyzerInstall? FindInstalled(SpaceAnalyzerPackage package)
    {
        try
        {
            if (!Directory.Exists(_root))
            {
                return null;
            }

            return Directory.EnumerateDirectories(_root)
                .Select(folder => (Folder: folder, Version: VersionOf(folder)))
                .Where(candidate => candidate.Version is not null)
                .OrderByDescending(candidate => candidate.Version)
                .Where(candidate => File.Exists(Path.Combine(candidate.Folder, package.Program)))
                .Select(candidate => new SpaceAnalyzerInstall(candidate.Version!, Path.Combine(candidate.Folder, package.EntryPoint)))
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warning($"The SpaceAnalyzer folder '{_root}' could not be read.", ex);
            return null;
        }
    }

    private async Task<SpaceAnalyzerInstall> InstallCoreAsync(SpaceAnalyzerPackage package, SpaceAnalyzerRelease release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_root);
        RemoveAbandonedDownloads();

        var staging = Path.Combine(_root, StagingPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);

        try
        {
            var download = Path.Combine(staging, "download");
            await DownloadAsync(release, download, progress, cancellationToken).ConfigureAwait(false);

            var unpacked = Path.Combine(staging, "unpacked");
            Unpack(package, download, unpacked);

            var program = Path.Combine(unpacked, package.Program);
            if (!File.Exists(program))
            {
                throw new InvalidDataException($"{release.AssetName} does not contain {package.Program}.");
            }

            if (!OperatingSystem.IsWindows())
            {
                // An archive that lost the execute bit on the way would otherwise fail with a bare "permission denied".
                File.SetUnixFileMode(program, File.GetUnixFileMode(program)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }

            var folder = Path.Combine(_root, release.Version.ToString());
            MoveIntoPlace(unpacked, folder, package);
            RemoveVersionsOlderThan(release.Version);

            AppLog.Info($"SpaceAnalyzer {release.Version} is kept in '{folder}'.");
            return new SpaceAnalyzerInstall(release.Version, Path.Combine(folder, package.EntryPoint));
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    /// <summary>
    /// Streams the file to disk while hashing it, then holds it against what the release published. The time limit
    /// is on silence rather than on the whole download: every block that arrives re-arms it.
    /// </summary>
    private async Task DownloadAsync(SpaceAnalyzerRelease release, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stall.CancelAfter(_stallTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = release.Size > 0 ? release.Size : response.Content.Headers.ContentLength ?? 0;

            await using var source = await response.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = new byte[81920];
            long written = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
            {
                stall.CancelAfter(_stallTimeout);
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), stall.Token).ConfigureAwait(false);
                written += read;

                if (total > 0)
                {
                    progress?.Report(Math.Min(1.0, (double)written / total));
                }
            }

            if (release.Size > 0 && written != release.Size)
            {
                throw new InvalidDataException(
                    $"The download of {release.AssetName} ended at {written:N0} of {release.Size:N0} bytes.");
            }

            if (release.Sha256 is { } expected && !string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The download of {release.AssetName} does not match the SHA-256 published with the release, so it was discarded.");
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The silence limit, or the client's own limit on the headers: either way the server stopped answering.
            throw new TimeoutException($"The download of {release.AssetName} stopped answering.", ex);
        }
    }

    /// <summary>Unpacks a verified download. Both archive readers refuse an entry that would land outside the destination.</summary>
    private static void Unpack(SpaceAnalyzerPackage package, string download, string destination)
    {
        Directory.CreateDirectory(destination);

        switch (package.Packaging)
        {
            case SpaceAnalyzerPackaging.Executable:
                File.Move(download, Path.Combine(destination, package.Program));
                break;

            case SpaceAnalyzerPackaging.TarGz:
                using (var file = File.OpenRead(download))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
                }

                break;

            case SpaceAnalyzerPackaging.AppBundleZip:
                // Unix permissions and the bytes of every file come through unchanged, so the bundle's signature holds.
                ZipFile.ExtractToDirectory(download, destination, overwriteFiles: false);
                break;
        }
    }

    /// <summary>
    /// Renames the finished copy to its version folder. The rename is what makes it visible, so a version folder is
    /// either absent or complete. A copy that another SharpCommander finished first is kept.
    /// </summary>
    private static void MoveIntoPlace(string unpacked, string folder, SpaceAnalyzerPackage package)
    {
        var program = Path.Combine(folder, package.Program);

        if (Directory.Exists(folder))
        {
            if (File.Exists(program))
            {
                return;
            }

            // Not something a rename of ours can produce: a copy damaged by hand. It is replaced.
            Directory.Delete(folder, recursive: true);
        }

        // An antivirus still scanning the new program can hold it open for a moment, which makes the rename fail on
        // Windows; it is tried again a few times before the download is given up.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(unpacked, folder);
                return;
            }
            catch (IOException) when (File.Exists(program))
            {
                // Another SharpCommander finished the same version a moment earlier; theirs is just as good.
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    /// <summary>Removes the staging folders of downloads that were interrupted long ago.</summary>
    private void RemoveAbandonedDownloads()
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(_root, StagingPrefix + "*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > StagingLifetime)
                {
                    TryDeleteDirectory(folder);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Info($"Old SpaceAnalyzer downloads could not be cleaned up: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the versions older than <paramref name="version"/>. One that is running cannot be removed on Windows;
    /// it stays until the next release comes in, and the newest copy is the one that runs meanwhile.
    /// </summary>
    private void RemoveVersionsOlderThan(Version version)
    {
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                if (VersionOf(folder) is { } old && old < version)
                {
                    TryDeleteDirectory(folder);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Info($"Older SpaceAnalyzer versions could not be cleaned up: {ex.Message}");
        }
    }

    private static Version? VersionOf(string folder) => Version.TryParse(Path.GetFileName(folder), out var version) ? version : null;

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Info($"'{folder}' could not be removed yet: {ex.Message}");
        }
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>How a SpaceAnalyzer download is packed.</summary>
internal enum SpaceAnalyzerPackaging
{
    /// <summary>The download is the program itself (Windows).</summary>
    Executable,

    /// <summary>A .tar.gz that holds the program (Linux).</summary>
    TarGz,

    /// <summary>A .zip that holds the application bundle (macOS).</summary>
    AppBundleZip
}

/// <summary>The SpaceAnalyzer download for one system, and what it unpacks to.</summary>
internal sealed record SpaceAnalyzerPackage(string AssetName, SpaceAnalyzerPackaging Packaging)
{
    /// <summary>What is started, relative to its version folder.</summary>
    public string EntryPoint => Packaging switch
    {
        SpaceAnalyzerPackaging.Executable => "SpaceAnalyzer.exe",
        SpaceAnalyzerPackaging.AppBundleZip => "SpaceAnalyzer.app",
        _ => "SpaceAnalyzer"
    };

    /// <summary>The executable file itself, relative to its version folder: inside the bundle on macOS.</summary>
    public string Program => Packaging == SpaceAnalyzerPackaging.AppBundleZip
        ? Path.Combine(EntryPoint, "Contents", "MacOS", "SpaceAnalyzer")
        : EntryPoint;

    /// <summary>The package for the system this runs on, or null when none is published for it.</summary>
    public static SpaceAnalyzerPackage? ForThisSystem()
    {
        OSPlatform? platform = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX
            : OperatingSystem.IsLinux() ? OSPlatform.Linux
            : null;

        // The processor of the operating system rather than of this process: an x86 SharpCommander on 64-bit
        // Windows, or an x64 one emulated on ARM64, still gets the native build.
        return platform is { } known ? For(known, RuntimeInformation.OSArchitecture) : null;
    }

    /// <summary>The file published for <paramref name="platform"/> on <paramref name="architecture"/>, or null when there is none.</summary>
    public static SpaceAnalyzerPackage? For(OSPlatform platform, Architecture architecture)
    {
        var processor = architecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };

        if (processor is null)
        {
            return null;
        }

        if (platform == OSPlatform.Windows)
        {
            return new SpaceAnalyzerPackage($"SpaceAnalyzer-windows-{processor}.exe", SpaceAnalyzerPackaging.Executable);
        }

        if (platform == OSPlatform.Linux)
        {
            return new SpaceAnalyzerPackage($"SpaceAnalyzer-linux-{processor}.tar.gz", SpaceAnalyzerPackaging.TarGz);
        }

        // One universal bundle runs natively on Apple Silicon and on Intel.
        return platform == OSPlatform.OSX
            ? new SpaceAnalyzerPackage("SpaceAnalyzer-macos.zip", SpaceAnalyzerPackaging.AppBundleZip)
            : null;
    }
}
