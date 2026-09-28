using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SharpCommander.Core.Models;
using SharpCommander.Desktop.Services;
using Xunit;
using Xunit.Abstractions;

namespace SharpCommander.Tests;

/// <summary>
/// Downloading and keeping SpaceAnalyzer, against a stub server and a temporary folder rather than github.com.
/// Unpacking is plain .NET, so the three kinds of download (the Windows executable, the Linux tar.gz and the macOS
/// bundle zip) are all exercised on every system.
/// </summary>
public sealed class SpaceAnalyzerServiceTests(ITestOutputHelper output) : IDisposable
{
    private const string ApiUrl = "https://api.github.com/repos/aurgo/SpaceAnalyzer/releases/latest";

    private static readonly SpaceAnalyzerPackage Windows = SpaceAnalyzerPackage.For(OSPlatform.Windows, Architecture.X64)!;
    private static readonly SpaceAnalyzerPackage Linux = SpaceAnalyzerPackage.For(OSPlatform.Linux, Architecture.X64)!;
    private static readonly SpaceAnalyzerPackage MacOS = SpaceAnalyzerPackage.For(OSPlatform.OSX, Architecture.Arm64)!;

    private readonly TempDir _dir = new();
    private readonly StubServer _server = new();

    public void Dispose() => _dir.Dispose();

    private string Root => Path.Combine(_dir.Path, "SpaceAnalyzer");

    private SpaceAnalyzerService Service(SpaceAnalyzerPackage? package, TimeSpan? stallTimeout = null) =>
        new(new HttpClient(_server), Root, package, stallTimeout);

    // ---- which download --------------------------------------------------------------------------------

    [Theory]
    [InlineData("WINDOWS", Architecture.X64, "SpaceAnalyzer-windows-x64.exe")]
    [InlineData("WINDOWS", Architecture.Arm64, "SpaceAnalyzer-windows-arm64.exe")]
    [InlineData("LINUX", Architecture.X64, "SpaceAnalyzer-linux-x64.tar.gz")]
    [InlineData("LINUX", Architecture.Arm64, "SpaceAnalyzer-linux-arm64.tar.gz")]
    [InlineData("OSX", Architecture.Arm64, "SpaceAnalyzer-macos.zip")]
    [InlineData("OSX", Architecture.X64, "SpaceAnalyzer-macos.zip")]
    public void EachSystemGetsTheFileItsReleaseIsPublishedAs(string platform, Architecture architecture, string asset)
    {
        Assert.Equal(asset, SpaceAnalyzerPackage.For(OSPlatform.Create(platform), architecture)?.AssetName);
    }

    [Theory]
    [InlineData("WINDOWS", Architecture.X86)]
    [InlineData("WINDOWS", Architecture.Arm)]
    [InlineData("LINUX", Architecture.Arm)]
    [InlineData("LINUX", Architecture.S390x)]
    [InlineData("FREEBSD", Architecture.X64)]
    public void NothingIsOfferedWhereNoBuildIsPublished(string platform, Architecture architecture)
    {
        Assert.Null(SpaceAnalyzerPackage.For(OSPlatform.Create(platform), architecture));
    }

    [Fact]
    public void ThisSystemGetsTheKindOfDownloadMadeForIt()
    {
        var package = SpaceAnalyzerPackage.ForThisSystem();

        Assert.NotNull(package);
        Assert.Equal(
            OperatingSystem.IsWindows() ? SpaceAnalyzerPackaging.Executable
            : OperatingSystem.IsMacOS() ? SpaceAnalyzerPackaging.AppBundleZip
            : SpaceAnalyzerPackaging.TarGz,
            package.Packaging);
    }

    [Theory]
    [InlineData("sha256:780A44B0AA8E4AA1A6A006AE070E1075EA4AE0861A56659F722F68BD2C3863A6", "780a44b0aa8e4aa1a6a006ae070e1075ea4ae0861a56659f722f68bd2c3863a6")]
    [InlineData("sha256:780a44b0aa8e4aa1a6a006ae070e1075ea4ae0861a56659f722f68bd2c3863a6", "780a44b0aa8e4aa1a6a006ae070e1075ea4ae0861a56659f722f68bd2c3863a6")]
    [InlineData("sha512:780a44b0aa8e4aa1a6a006ae070e1075ea4ae0861a56659f722f68bd2c3863a6", null)]
    [InlineData("sha256:not-hex", null)]
    [InlineData("sha256:780a44", null)]
    [InlineData(null, null)]
    public void ParseSha256_OnlyAcceptsACompleteSha256(string? digest, string? expected)
    {
        Assert.Equal(expected, SpaceAnalyzerService.ParseSha256(digest));
    }

    // ---- the latest release ----------------------------------------------------------------------------

    [Fact]
    public async Task FindLatest_ReadsTheFileForThisSystemFromTheRelease()
    {
        const string digest = "sha256:780a44b0aa8e4aa1a6a006ae070e1075ea4ae0861a56659f722f68bd2c3863a6";
        _server.Json(ApiUrl, ReleaseJson("v1.2.0", draft: false,
            ("SpaceAnalyzer-macos.zip", 2_537_565, "sha256:f98aac9a376f1917bd1b330e7ca8e258bd141c4be4ef7b9585534578682dd23f"),
            ("SpaceAnalyzer-windows-x64.exe", 2_050_560, digest),
            ("SHA256SUMS.txt", 679, null)));
        using var service = Service(Windows);

        var release = await service.FindLatestAsync();

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 0), release.Version);
        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal("SpaceAnalyzer-windows-x64.exe", release.AssetName);
        Assert.Equal(DownloadUrl("v1.2.0", "SpaceAnalyzer-windows-x64.exe"), release.DownloadUrl);
        Assert.Equal(2_050_560, release.Size);
        Assert.Equal(digest["sha256:".Length..], release.Sha256);
    }

    [Fact]
    public async Task FindLatest_AReleaseWithoutTheFileForThisSystemIsNothing()
    {
        _server.Json(ApiUrl, ReleaseJson("v1.2.0", draft: false, ("SpaceAnalyzer-macos.zip", 10, null)));
        using var service = Service(Linux);

        Assert.Null(await service.FindLatestAsync());
    }

    [Fact]
    public async Task FindLatest_IgnoresADraft()
    {
        _server.Json(ApiUrl, ReleaseJson("v9.9.9", draft: true, ("SpaceAnalyzer-windows-x64.exe", 10, null)));
        using var service = Service(Windows);

        Assert.Null(await service.FindLatestAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FindLatest_AnErrorAnswerIsNothing(HttpStatusCode status)
    {
        _server.Status(ApiUrl, status);
        using var service = Service(Windows);

        Assert.Null(await service.FindLatestAsync());
    }

    [Fact]
    public async Task FindLatest_RubbishOnTheWireIsNothing()
    {
        _server.Json(ApiUrl, "<html>not json</html>");
        using var service = Service(Windows);

        Assert.Null(await service.FindLatestAsync());
    }

    [Fact]
    public async Task FindLatest_NoNetworkIsNothing()
    {
        _server.Fail(ApiUrl);
        using var service = Service(Windows);

        Assert.Null(await service.FindLatestAsync());
    }

    [Fact]
    public async Task FindLatest_AnUnsupportedSystemDoesNotEvenAsk()
    {
        using var service = Service(package: null);

        Assert.False(service.IsSupported);
        Assert.Null(await service.FindLatestAsync());
        Assert.Empty(_server.Requests);
    }

    // ---- downloading and keeping it ------------------------------------------------------------------

    [Fact]
    public async Task Install_KeepsTheWindowsProgramUnderItsVersion()
    {
        var content = Encoding.UTF8.GetBytes("MZ pretend this is SpaceAnalyzer.exe");
        var release = Publish(Windows, "v1.2.0", content);
        using var service = Service(Windows);

        var install = await service.InstallAsync(release);

        Assert.Equal(new Version(1, 2, 0), install.Version);
        Assert.Equal(Path.Combine(Root, "1.2.0", "SpaceAnalyzer.exe"), install.Path);
        Assert.Equal(content, File.ReadAllBytes(install.Path));
        Assert.Equal(install, service.Installed);

        // Nothing but the version folder: the staging folder is gone.
        Assert.Equal(["1.2.0"], Directory.EnumerateFileSystemEntries(Root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Install_UnpacksTheLinuxProgramAndMakesItExecutable()
    {
        var release = Publish(Linux, "v1.2.0", TarGz(("SpaceAnalyzer", "\u007fELF pretend")));
        using var service = Service(Linux);

        var install = await service.InstallAsync(release);

        Assert.Equal(Path.Combine(Root, "1.2.0", "SpaceAnalyzer"), install.Path);
        Assert.Equal("\u007fELF pretend", File.ReadAllText(install.Path));

        if (!OperatingSystem.IsWindows())
        {
            // The archive carried the program without its execute bit; it must be able to start anyway.
            Assert.True(File.GetUnixFileMode(install.Path).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Install_UnpacksTheMacBundleAndStartsTheBundleItself()
    {
        var release = Publish(MacOS, "v1.2.0", Zip(
            ("SpaceAnalyzer.app/Contents/Info.plist", "<plist/>"),
            ("SpaceAnalyzer.app/Contents/MacOS/SpaceAnalyzer", "pretend Mach-O")),
            withDigest: false);
        using var service = Service(MacOS);

        var install = await service.InstallAsync(release);

        // What is started is the bundle, so macOS treats it as an application.
        Assert.Equal(Path.Combine(Root, "1.2.0", "SpaceAnalyzer.app"), install.Path);
        var program = Path.Combine(install.Path, "Contents", "MacOS", "SpaceAnalyzer");
        Assert.Equal("pretend Mach-O", File.ReadAllText(program));
        Assert.True(File.Exists(Path.Combine(install.Path, "Contents", "Info.plist")));

        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(program).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task Install_ReportsProgressUpToTheWholeFile()
    {
        var content = RandomNumberGenerator.GetBytes(300_000);
        var release = Publish(Windows, "v1.2.0", content);
        var progress = new Recorder();
        using var service = Service(Windows);

        await service.InstallAsync(release, progress);

        Assert.True(progress.Values.Count > 1);
        Assert.Equal(progress.Values.Order(), progress.Values);
        Assert.Equal(1.0, progress.Values[^1]);
    }

    [Fact]
    public async Task Install_AFileThatDoesNotMatchItsChecksumIsDiscarded()
    {
        var release = Publish(Windows, "v1.2.0", Encoding.UTF8.GetBytes("tampered")) with
        {
            Sha256 = Sha256(Encoding.UTF8.GetBytes("the real thing"))
        };
        using var service = Service(Windows);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(release));

        AssertNothingKept(service);
    }

    [Fact]
    public async Task Install_ATruncatedDownloadIsDiscarded()
    {
        var content = Encoding.UTF8.GetBytes("only part of it");
        var release = Publish(Windows, "v1.2.0", content, withDigest: false) with { Size = content.Length + 100 };
        using var service = Service(Windows);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(release));

        AssertNothingKept(service);
    }

    [Fact]
    public async Task Install_AnArchiveWithoutTheProgramIsRefused()
    {
        var release = Publish(Linux, "v1.2.0", TarGz(("README.md", "no program in here")));
        using var service = Service(Linux);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InstallAsync(release));

        AssertNothingKept(service);
    }

    [Fact]
    public async Task Install_AServerErrorKeepsNothing()
    {
        var release = Publish(Windows, "v1.2.0", [1, 2, 3]);
        _server.Status(release.DownloadUrl, HttpStatusCode.NotFound);
        using var service = Service(Windows);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.InstallAsync(release));

        AssertNothingKept(service);
    }

    [Fact]
    public async Task Install_ADownloadThatStopsArrivingIsGivenUp()
    {
        var release = Publish(Windows, "v1.2.0", [1, 2, 3]);
        _server.Stream(release.DownloadUrl, () => new HangingStream());
        using var service = Service(Windows, stallTimeout: TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAsync<TimeoutException>(() => service.InstallAsync(release));

        AssertNothingKept(service);
    }

    [Fact]
    public async Task Install_ANewerReleaseRetiresTheOlderOne()
    {
        using var service = Service(Windows);
        await service.InstallAsync(Publish(Windows, "v1.0.0", Encoding.UTF8.GetBytes("old")));

        var install = await service.InstallAsync(Publish(Windows, "v1.1.0", Encoding.UTF8.GetBytes("new")));

        Assert.Equal(new Version(1, 1, 0), service.Installed?.Version);
        Assert.Equal("new", File.ReadAllText(install.Path));
        Assert.False(Directory.Exists(Path.Combine(Root, "1.0.0")));
    }

    [Fact]
    public async Task Install_AVersionAlreadyKeptIsLeftAsItIs()
    {
        // What another SharpCommander leaves behind when it finishes the same download first.
        _dir.File(Path.Combine("SpaceAnalyzer", "1.2.0", "SpaceAnalyzer.exe"), "theirs");
        using var service = Service(Windows);

        var install = await service.InstallAsync(Publish(Windows, "v1.2.0", Encoding.UTF8.GetBytes("ours")));

        Assert.Equal("theirs", File.ReadAllText(install.Path));
    }

    [Fact]
    public async Task Install_ReplacesAVersionFolderThatLostItsProgram()
    {
        _dir.File(Path.Combine("SpaceAnalyzer", "1.2.0", "stray.txt"));
        using var service = Service(Windows);

        var install = await service.InstallAsync(Publish(Windows, "v1.2.0", Encoding.UTF8.GetBytes("program")));

        Assert.Equal("program", File.ReadAllText(install.Path));
        Assert.False(File.Exists(Path.Combine(Root, "1.2.0", "stray.txt")));
    }

    [Fact]
    public async Task Install_ClearsDownloadsAbandonedLongAgoButNotOnesInProgress()
    {
        var abandoned = _dir.Dir(Path.Combine("SpaceAnalyzer", ".download-abandoned"));
        Directory.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddDays(-2));
        var inProgress = _dir.Dir(Path.Combine("SpaceAnalyzer", ".download-in-progress"));
        using var service = Service(Windows);

        await service.InstallAsync(Publish(Windows, "v1.2.0", [1, 2, 3]));

        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(inProgress));
    }

    [Fact]
    public void Installed_PassesOverFoldersThatAreNotACompleteVersion()
    {
        _dir.Dir(Path.Combine("SpaceAnalyzer", "2.0.0"));
        _dir.File(Path.Combine("SpaceAnalyzer", ".download-x", "SpaceAnalyzer.exe"));
        _dir.File(Path.Combine("SpaceAnalyzer", "notes", "SpaceAnalyzer.exe"));
        using var service = Service(Windows);

        Assert.Null(service.Installed);

        _dir.File(Path.Combine("SpaceAnalyzer", "1.0.0", "SpaceAnalyzer.exe"));
        _dir.File(Path.Combine("SpaceAnalyzer", "1.10.0", "SpaceAnalyzer.exe"));
        _dir.File(Path.Combine("SpaceAnalyzer", "1.9.0", "SpaceAnalyzer.exe"));

        // Versions compare as numbers: 1.10 is newer than 1.9.
        Assert.Equal(new SpaceAnalyzerInstall(new Version(1, 10, 0), Path.Combine(Root, "1.10.0", "SpaceAnalyzer.exe")), service.Installed);
    }

    [Fact]
    public void Installed_IsNothingBeforeTheFirstDownload()
    {
        using var service = Service(Windows);

        Assert.True(service.IsSupported);
        Assert.Null(service.Installed);
        Assert.False(Directory.Exists(Root));
    }

    // ---- starting it -----------------------------------------------------------------------------------

    [Fact]
    public void AProgramIsRunDirectlyOnTheFolder()
    {
        var program = Path.Combine(_dir.Path, "1.2.0", "SpaceAnalyzer.exe");
        var folder = Path.Combine(_dir.Path, "My Documents");

        var startInfo = SpaceAnalyzerService.CreateStartInfo(program, folder, viaLaunchServices: false);

        Assert.Equal(program, startInfo.FileName);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal([folder], startInfo.ArgumentList);
        Assert.Equal(Path.GetDirectoryName(program), startInfo.WorkingDirectory);
    }

    [Fact]
    public void WithoutAFolderItOpensOnItsStartScreen()
    {
        var startInfo = SpaceAnalyzerService.CreateStartInfo("/tools/1.2.0/SpaceAnalyzer", folder: null, viaLaunchServices: false);

        Assert.Empty(startInfo.ArgumentList);
    }

    [Fact]
    public void AMacBundleOpensThroughLaunchServicesAsANewCopy()
    {
        const string bundle = "/Users/ana/Library/Application Support/SharpCommander/tools/SpaceAnalyzer/1.2.0/SpaceAnalyzer.app";

        var onFolder = SpaceAnalyzerService.CreateStartInfo(bundle, "/Users/ana/Downloads", viaLaunchServices: true);
        var onStart = SpaceAnalyzerService.CreateStartInfo(bundle, folder: null, viaLaunchServices: true);

        Assert.Equal("/usr/bin/open", onFolder.FileName);
        Assert.Equal(["-n", "-a", bundle, "--args", "/Users/ana/Downloads"], onFolder.ArgumentList);
        Assert.Equal(["-n", "-a", bundle], onStart.ArgumentList);
    }

    // ---- against the real release --------------------------------------------------------------------

    /// <summary>
    /// Downloads the real release for this system from github.com into a temporary folder. It needs the network, so
    /// it only runs when SC_LIVE is "1": <c>SC_LIVE=1 dotnet test --filter DownloadsTheRealRelease</c>.
    /// </summary>
    [Fact]
    public async Task DownloadsTheRealRelease()
    {
        if (Environment.GetEnvironmentVariable("SC_LIVE") != "1")
        {
            output.WriteLine("Skipped: set SC_LIVE=1 to download SpaceAnalyzer from github.com.");
            return;
        }

        using var service = new SpaceAnalyzerService(new HttpClient(), Root, SpaceAnalyzerPackage.ForThisSystem());

        var release = await service.FindLatestAsync();
        Assert.NotNull(release);
        Assert.NotNull(release.Sha256);

        var install = await service.InstallAsync(release);

        Assert.Equal(install, service.Installed);
        output.WriteLine($"SpaceAnalyzer {install.Version} ({release.AssetName}, {release.Size:N0} bytes) kept at {install.Path}");
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    private void AssertNothingKept(SpaceAnalyzerService service)
    {
        Assert.Null(service.Installed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    /// <summary>Serves <paramref name="content"/> as the file of <paramref name="package"/> and returns the release describing it.</summary>
    private SpaceAnalyzerRelease Publish(SpaceAnalyzerPackage package, string tag, byte[] content, bool withDigest = true)
    {
        var url = DownloadUrl(tag, package.AssetName);
        _server.Bytes(url, content);

        return new SpaceAnalyzerRelease
        {
            Version = GitHubUpdateService.ParseVersion(tag)!,
            Tag = tag,
            AssetName = package.AssetName,
            DownloadUrl = url,
            Size = content.Length,
            Sha256 = withDigest ? Sha256(content) : null
        };
    }

    private static string DownloadUrl(string tag, string name) => $"https://github.com/aurgo/SpaceAnalyzer/releases/download/{tag}/{name}";

    private static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>The fields of a GitHub release that the service reads.</summary>
    private static string ReleaseJson(string tag, bool draft, params (string Name, long Size, string? Digest)[] assets)
    {
        var files = new JsonArray();
        foreach (var (name, size, digest) in assets)
        {
            files.Add(new JsonObject
            {
                ["name"] = name,
                ["size"] = size,
                ["digest"] = digest,
                ["browser_download_url"] = DownloadUrl(tag, name)
            });
        }

        return new JsonObject { ["tag_name"] = tag, ["draft"] = draft, ["assets"] = files }.ToJsonString();
    }

    /// <summary>A .tar.gz like the Linux release: files at the root, here without an execute bit.</summary>
    private static byte[] TarGz(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                });
            }
        }

        return buffer.ToArray();
    }

    /// <summary>A .zip like the macOS release: the bundle folder and the files inside it.</summary>
    private static byte[] Zip(params (string Name, string Content)[] files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Answers by URL, with a 404 for anything it does not know, and records every request.</summary>
    private sealed class StubServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Json(string url, string body) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public void Bytes(string url, byte[] body) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        public void Stream(string url, Func<Stream> body) =>
            _routes[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) };

        public void Status(string url, HttpStatusCode status) =>
            _routes[url] = () => new HttpResponseMessage(status);

        public void Fail(string url) =>
            _routes[url] = () => throw new HttpRequestException("no network");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            // GitHub refuses a request with no user agent, so the service must always send one.
            Assert.NotEmpty(request.Headers.UserAgent);

            var url = request.RequestUri!.ToString();
            return Task.FromResult(_routes.TryGetValue(url, out var answer) ? answer() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    /// <summary>A body that sends one byte and then nothing more, like a connection that hangs.</summary>
    private sealed class HangingStream : Stream
    {
        private bool _sent;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                buffer.Span[0] = 1;
                return 1;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Records progress synchronously, unlike Progress&lt;T&gt;, which posts to a context.</summary>
    private sealed class Recorder : IProgress<double>
    {
        public List<double> Values { get; } = [];

        public void Report(double value)
        {
            lock (Values)
            {
                Values.Add(value);
            }
        }
    }
}
