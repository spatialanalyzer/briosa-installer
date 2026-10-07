using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class InstallationRegistrationTests
{
    [Fact]
    public void NativeRegistry64RoundTripPreservesUnicodeAndRemovesOnlySelectedEntry()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = @"Software\Briosa.Tests\Installations-" + Guid.NewGuid().ToString("N");
        using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        try
        {
            var registry = new WindowsInstallationRegistry(root);
            var directory = Path.Combine(Path.GetTempPath(), "Briosa Å 空間", "products", "first");
            var first = new InstallationRegistration(1, InstallationRegistration.IdFor(directory),
                directory, "first", "0.7.0", "2026.1.0529.7", "win-x64");
            var otherDirectory = directory + "-second";
            var second = first with { ProductDirectory = otherDirectory, InstallationId = InstallationRegistration.IdFor(otherDirectory) };
            registry.Write(false, first);
            registry.Write(false, second);
            Assert.Contains(first, registry.Read(false));
            using (var entry = hive.OpenSubKey(root + "\\" + first.InstallationId))
                Assert.Equal(RegistryValueKind.String, entry!.GetValueKind("Registration"));
            registry.Remove(false, first.InstallationId);
            Assert.Equal(second, Assert.Single(registry.Read(false)));
            registry.Remove(false, first.InstallationId);
            Assert.Equal(second, Assert.Single(registry.Read(false)));
        }
        finally { hive.DeleteSubKeyTree(root, false); }
    }

    [Fact]
    public async Task VersionsRegisterIndependentlyAndRepairPreservesIdentity()
    {
        using var feed = new SignedFeed();
        var first = feed.AddServer("0.6.1");
        var second = feed.AddServer("0.7.0");
        feed.Publish();
        var registry = new MemoryRegistry();
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        var installed = await store.InstallAsync(feed.Settings, CatalogComponent.Server, first.Id, feed.Hash);
        await store.InstallAsync(feed.Settings, CatalogComponent.Server, second.Id, feed.Hash);
        Assert.Equal(2, registry.Items.Count);
        var id = InstallationRegistration.IdFor(installed.Directory);
        await store.InstallAsync(feed.Settings, CatalogComponent.Server, first.Id, feed.Hash, repair: true);
        Assert.Equal(2, registry.Items.Count);
        Assert.Equal(first.Id, registry.Items[id].PackageId);
        store.Remove(first.Id);
        Assert.Single(registry.Items);
        Assert.Equal(second.Id, registry.Items.Values.Single().PackageId);
    }

    [Fact]
    public async Task RegistryFailureDoesNotUndoCommittedFilesAndRecoverReconciles()
    {
        using var feed = new SignedFeed();
        var package = feed.AddServer("0.7.0");
        feed.Publish();
        var registry = new MemoryRegistry { FailWrites = true };
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        var failure = await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash));
        Assert.Equal(ManagementError.RegistrationIncomplete, failure.Code);
        Assert.Single(store.List());
        await store.VerifyAsync(package.Id);
        registry.FailWrites = false;
        store.Recover();
        Assert.Equal(package.Id, Assert.Single(registry.Items.Values).PackageId);
    }

    [Fact]
    public async Task BackfillAndRemovalFailureAreRecoverableWithoutTouchingAnotherStore()
    {
        using var feed = new SignedFeed();
        var package = feed.AddServer("0.6.1");
        feed.Publish();
        var unindexed = new PackageStore(feed.StorePath);
        var installed = await unindexed.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash);
        var registry = new MemoryRegistry();
        var otherPath = Path.Combine(feed.Root, "other-store", "products", package.Id);
        var other = InstallationRegistration.From(installed) with
        {
            ProductDirectory = otherPath,
            InstallationId = InstallationRegistration.IdFor(otherPath)
        };
        registry.Write(false, other);
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        store.RegisterInstallations();
        Assert.Equal(2, registry.Items.Count);
        registry.FailRemovals = true;
        Assert.Equal(ManagementError.RegistrationIncomplete,
            Assert.Throws<ManagementException>(() => store.Remove(package.Id)).Code);
        Assert.Empty(store.List());
        registry.FailRemovals = false;
        store.Recover();
        Assert.Equal(other, Assert.Single(registry.Items.Values));
    }

    // The package engine stores any well-formed contract unchanged and does not negotiate
    // the server's gRPC major. Release validation approves an exact major separately.
    [Theory]
    [InlineData("""{"major":1,"revision":0}""")]
    [InlineData("""{"major":2,"revision":0}""")]
    [InlineData("""{"major":3,"revision":7}""")]
    [InlineData("""{"major":4294967295,"revision":4294967295}""")]
    public async Task SchemaThreeStoresAnyWellFormedContractUnchanged(string compatibility)
    {
        using var feed = new SignedFeed();
        var manifest = ServerManifest(compatibility: compatibility);
        var package = feed.AddServer("0.7.0", manifestOverride: manifest);
        feed.Publish();
        var registry = new MemoryRegistry();
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        var installed = await store.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash);
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(installed.Directory, "payload", "manifest.json")));
        Assert.Equal(SignedFeed.ServerTarget, Assert.Single(registry.Items.Values).SpatialAnalyzerTarget);
    }

    [Theory]
    [InlineData("""{"major":0,"revision":0}""")]
    [InlineData("""{"major":-1,"revision":0}""")]
    [InlineData("""{"major":4294967296,"revision":0}""")]
    [InlineData("""{"major":18446744073709551616,"revision":0}""")]
    [InlineData("""{"major":2,"revision":-1}""")]
    [InlineData("""{"major":2,"revision":4294967296}""")]
    [InlineData("""{"major":2.5,"revision":0}""")]
    [InlineData("""{"major":"2","revision":0}""")]
    [InlineData("""{"major":2}""")]
    [InlineData("""{"revision":0}""")]
    [InlineData("null")]
    [InlineData(Omitted)]
    public Task SchemaThreeRejectsInvalidOrOverflowingContracts(string compatibility) =>
        AssertRejectedPreservingExisting(ServerManifest(compatibility: compatibility));

    [Theory]
    [InlineData(Omitted)]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("3.5")]
    [InlineData("\"3\"")]
    public Task UnknownServerManifestSchemaIsRejected(string schema) =>
        AssertRejectedPreservingExisting(ServerManifest(schema: schema));

    [Theory]
    [InlineData("2026.1.0529.7")]
    [InlineData("2099.1.0101.2")]
    public Task ManifestForAnotherTargetIsRejected(string target) =>
        AssertRejectedPreservingExisting(ServerManifest(target: target));

    [Fact]
    public async Task ProvenanceMustMatchTheEmbeddedManifestByteForByte()
    {
        var manifest = ServerManifest();
        await AssertRejectedPreservingExisting(manifest, ServerManifest(compatibility: """{"major":3,"revision":0}"""));
        await AssertRejectedPreservingExisting(manifest, [.. manifest, (byte)'\n']);
    }

    private const string Omitted = "<omitted>";

    private static byte[] ServerManifest(string schema = "3", string compatibility = """{"major":2,"revision":0}""",
        string target = SignedFeed.ServerTarget)
    {
        var manifest = new JsonObject();
        if (schema != Omitted) manifest["schemaVersion"] = JsonNode.Parse(schema);
        manifest["artifactName"] = $"briosa-0.7.0-sa-{SignedFeed.ServerTarget}-win-x64";
        manifest["briosaVersion"] = "0.7.0";
        manifest["runtimeIdentifier"] = "win-x64";
        manifest["spatialAnalyzerTarget"] = target;
        manifest["protocolPackage"] = "briosa";
        manifest["spatialAnalyzerBundled"] = false;
        if (compatibility != Omitted) manifest["compatibility"] = JsonNode.Parse(compatibility);
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    // A rejected candidate must leave an earlier complete product and its registration intact.
    private static async Task AssertRejectedPreservingExisting(byte[] manifest, byte[]? provenance = null)
    {
        using var feed = new SignedFeed();
        var existing = feed.AddServer("0.6.1");
        var candidate = feed.AddServer("0.7.0", manifestOverride: manifest, provenanceOverride: provenance);
        feed.Publish();
        var registry = new MemoryRegistry();
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        await store.InstallAsync(feed.Settings, CatalogComponent.Server, existing.Id, feed.Hash);
        var failure = await Assert.ThrowsAsync<ManagementException>(() =>
            store.InstallAsync(feed.Settings, CatalogComponent.Server, candidate.Id, feed.Hash));
        Assert.Equal(ManagementError.InvalidManifest, failure.Code);
        Assert.Equal(existing.Id, Assert.Single(store.List()).Id);
        await store.VerifyAsync(existing.Id);
        Assert.Equal(existing.Id, Assert.Single(registry.Items.Values).PackageId);
    }

    private sealed class MemoryRegistry : IInstallationRegistry
    {
        internal Dictionary<string, InstallationRegistration> Items { get; } = [];
        internal bool FailWrites { get; set; }
        internal bool FailRemovals { get; set; }
        public IReadOnlyList<InstallationRegistration> Read(bool machine) => Items.Values.ToArray();
        public void Write(bool machine, InstallationRegistration registration)
        {
            Assert.False(machine);
            if (FailWrites) throw new ManagementException(ManagementError.RegistrationIncomplete);
            Items[registration.InstallationId] = registration;
        }
        public void Remove(bool machine, string installationId)
        {
            Assert.False(machine);
            if (FailRemovals) throw new ManagementException(ManagementError.RegistrationIncomplete);
            Items.Remove(installationId);
        }
    }
}
