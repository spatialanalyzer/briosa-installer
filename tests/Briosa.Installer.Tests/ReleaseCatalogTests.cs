using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Briosa.Installer.Cli;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class ReleaseCatalogTests
{
    private static byte[] Fixture => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "catalog.json"));
    private static InstallerSettings Sources => new("https://server.example.test/briosa/catalog.json", "https://installer.example.test/approved/catalog.json");

    [Fact]
    public void ParsesIndependentComponentsAndExactTargets()
    {
        var packages = Parsed(Fixture);
        Assert.Equal(4, packages.Count);
        Assert.Equal(2, packages.Where(p => p.Component == CatalogComponent.Server).Select(p => p.SpatialAnalyzerTarget).Distinct().Count());
        Assert.Null(packages.Single(p => p.Component == CatalogComponent.Installer).SpatialAnalyzerTarget);
    }

    [Theory]
    [InlineData("../outside.zip")]
    [InlineData("https://public.example.test/file.zip")]
    [InlineData("//public.example.test/file.zip")]
    [InlineData(@"C:\outside.zip")]
    [InlineData(@"packages\file.zip")]
    [InlineData("packages/%2e%2e/file.zip")]
    [InlineData("packages/%252f/file.zip")]
    [InlineData("packages//file.zip")]
    [InlineData("packages/file.zip?secret=token")]
    [InlineData("packages/file.zip#anchor")]
    [InlineData("packages/CON.zip")]
    [InlineData("packages/lpt1.json")]
    [InlineData("packages/file.")]
    public void RejectsReferencesThatCouldLeaveOrAmbiguouslyAddressTheMirror(string path)
    {
        var root = Root();
        root["packages"]![0]!["artifact"]!["path"] = path;
        Assert.Equal(CatalogError.UnsafeReference, Failure(Bytes(root)));
    }

    [Fact]
    public void RejectsDuplicatePropertiesAndAmbiguousPackageIdentities()
    {
        Assert.Equal(CatalogError.InvalidDocument, Failure(Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schemaVersion\":1,\"packages\":[]}")));
        var root = Root();
        root["packages"]![1]!["id"] = "SERVER-FIXTURE-A";
        Assert.Equal(CatalogError.DuplicatePackage, Failure(Bytes(root)));
        root = Root();
        root["packages"]![1]!["version"] = "0.1.0-fixture.1";
        Assert.Equal(CatalogError.DuplicatePackage, Failure(Bytes(root)));
    }

    [Fact]
    public void RejectsConflictingReferencesAndInvalidComponentClaims()
    {
        var root = Root();
        root["packages"]![1]!["artifact"]!["path"] = "PACKAGES/SERVER-A.ZIP";
        Assert.Equal(CatalogError.ConflictingReference, Failure(Bytes(root)));
        root = Root();
        root["packages"]![3]!["spatialAnalyzerTarget"] = "2099.1.0101.1";
        Assert.Equal(CatalogError.InvalidPackage, Failure(Bytes(root)));
        root = Root();
        root["packages"]![0]!.AsObject().Remove("provenance");
        Assert.Equal(CatalogError.InvalidPackage, Failure(Bytes(root)));
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0-alpha..1")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0\n")]
    public void RejectsMalformedVersions(string version)
    {
        var root = Root();
        root["packages"]![0]!["version"] = version;
        Assert.Equal(CatalogError.InvalidPackage, Failure(Bytes(root)));
    }

    [Fact]
    public void RejectsUnsupportedSchemasEncodingAndCatalogLimits()
    {
        var root = Root();
        root["schemaVersion"] = 2;
        Assert.Equal(CatalogError.UnsupportedSchema, Failure(Bytes(root)));
        Assert.Equal(CatalogError.InvalidDocument, Failure([0xff, 0xfe]));
        Assert.Equal(CatalogError.TooLarge, Failure(new byte[ReleaseCatalogCodec.MaximumBytes + 1]));
        root = Root();
        var entry = root["packages"]![0]!.DeepClone();
        root["packages"] = new JsonArray(Enumerable.Range(0, 1001).Select(_ => entry.DeepClone()).ToArray());
        Assert.Equal(CatalogError.TooLarge, Failure(Bytes(root)));
    }

    [Fact]
    public async Task SelectsTheExplicitUpdateSourceAndDoesNotFetchReferencedPayloads()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(JsonResponse()));
        using var client = new ReleaseCatalogClient(handler);
        var loaded = Assert.IsType<CatalogResult<CatalogSnapshot>.Success>(await client.ReadAsync(Sources, CatalogComponent.Installer)).Value;
        Assert.Single(loaded.Packages);
        Assert.Equal(Sources.InstallerCatalog, Assert.Single(handler.Requests));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Fixture)).ToLowerInvariant(), loaded.ContentSha256);
        var preview = Assert.IsType<CatalogResult<PackagePreview>.Success>(ReleaseCatalogCodec.Preview(loaded, "installer-fixture")).Value;
        Assert.Equal("https://installer.example.test/approved/installer/fixture.bin", preview.ArtifactLocation);
        Assert.False(preview.CanInstall);
        Assert.Equal("notPerformed", preview.PublisherVerification);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(302, CatalogError.RedirectRejected)]
    [InlineData(401, CatalogError.AuthenticationRequired)]
    [InlineData(403, CatalogError.AccessDenied)]
    [InlineData(404, CatalogError.NotFound)]
    [InlineData(500, CatalogError.SourceUnavailable)]
    public async Task FailedOverrideNeverFallsBackOrFollowsRedirects(int status, CatalogError expected)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Headers = { Location = new Uri(Sources.ServerCatalog) }, Content = new StringContent("untrusted response secret"),
        }));
        using var client = new ReleaseCatalogClient(handler);
        var failure = Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(await client.ReadAsync(Sources, CatalogComponent.Installer));
        Assert.Equal(expected, failure.Error.Code);
        Assert.Equal(Sources.InstallerCatalog, Assert.Single(handler.Requests));
        Assert.DoesNotContain("secret", failure.Error.Message);
    }

    [Fact]
    public async Task SharedSourceIsIntentionalAndEmptyComponentsAreValid()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"schemaVersion\":1,\"packages\":[]}") }));
        using var client = new ReleaseCatalogClient(handler);
        var result = Assert.IsType<CatalogResult<CatalogSnapshot>.Success>(await client.ReadAsync(Sources with { InstallerCatalog = null }, CatalogComponent.Installer));
        Assert.Empty(result.Value.Packages);
        Assert.Equal(Sources.ServerCatalog, Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task InvalidSettingsDoNotStartARequest()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(JsonResponse()));
        using var client = new ReleaseCatalogClient(handler);
        Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(await client.ReadAsync(Sources with { InstallerCatalog = "" }, CatalogComponent.Installer));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BoundsResponseBodiesEvenWithoutContentLength()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new UnknownLengthStream(new byte[ReleaseCatalogCodec.MaximumBytes + 1])),
        }));
        using var client = new ReleaseCatalogClient(handler);
        Assert.Equal(CatalogError.TooLarge, Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(await client.ReadAsync(Sources, CatalogComponent.Server)).Error.Code);
    }

    [Fact]
    public async Task TimeoutCoversAStalledBodyAfterHeaders()
    {
        var body = new StalledStream();
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) }));
        // Allow policy/HTTP initialization on loaded CI hosts before exercising the
        // body deadline. A 150 ms budget could expire before the handler was reached.
        using var client = new ReleaseCatalogClient(handler, TimeSpan.FromSeconds(5));
        var result = await client.ReadAsync(Sources, CatalogComponent.Server);
        Assert.Equal(CatalogError.TimedOut, Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(result).Error.Code);
        Assert.Single(handler.Requests);
        Assert.True(body.ReadStarted, "The deadline must cover reading the response body after headers.");
    }

    [Fact]
    public async Task CallerCancellationIsDistinctFromTimeout()
    {
        using var handler = new FakeHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return JsonResponse(); });
        using var client = new ReleaseCatalogClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        Assert.Equal(CatalogError.Cancelled, Assert.IsType<CatalogResult<CatalogSnapshot>.Failure>(await client.ReadAsync(Sources, CatalogComponent.Server, cancellation.Token)).Error.Code);
    }

    [Fact]
    public async Task LocalCatalogAndCliUseTheSameSourceWithoutNetworkRequests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Briosa.Catalog.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "catalog.json");
            var settingsPath = Path.Combine(directory, "settings.json");
            File.WriteAllBytes(catalogPath, Fixture);
            File.WriteAllText(settingsPath, SettingsCodec.Serialize(new(catalogPath)));
            using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("Offline read attempted network access."));
            using var client = new ReleaseCatalogClient(handler);
            using var output = new StringWriter();
            var exit = await CatalogCommands.RunAsync(["preview", "--component", "server", "--id", "server-fixture-a"], output, TextWriter.Null, new(settingsPath), client);
            Assert.Equal(0, exit);
            Assert.Contains("\"canInstall\": false", output.ToString());
            Assert.Contains("notPerformed", output.ToString());
            Assert.Empty(handler.Requests);
            var loaded = Assert.IsType<CatalogResult<CatalogSnapshot>.Success>(await client.ReadAsync(new(catalogPath), CatalogComponent.Server)).Value;
            var preview = Assert.IsType<CatalogResult<PackagePreview>.Success>(ReleaseCatalogCodec.Preview(loaded, "server-fixture-a")).Value;
            Assert.Equal(Path.Combine(directory, "packages", "server-a.zip"), preview.ArtifactLocation);
            Assert.False(File.Exists(preview.ArtifactLocation));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static JsonObject Root() => JsonNode.Parse(Fixture)!.AsObject();
    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());
    private static IReadOnlyList<CatalogPackage> Parsed(byte[] bytes) => Assert.IsType<CatalogResult<IReadOnlyList<CatalogPackage>>.Success>(ReleaseCatalogCodec.Parse(bytes)).Value;
    private static CatalogError Failure(byte[] bytes) => Assert.IsType<CatalogResult<IReadOnlyList<CatalogPackage>>.Failure>(ReleaseCatalogCodec.Parse(bytes)).Error.Code;
    private static HttpResponseMessage JsonResponse() => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Fixture) };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return reply(request, token);
        }
    }

    private sealed class UnknownLengthStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class StalledStream : Stream
    {
        public bool ReadStarted { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted = true;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
