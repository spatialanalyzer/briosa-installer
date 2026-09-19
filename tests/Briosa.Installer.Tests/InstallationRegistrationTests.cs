using System.Text.Json;
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

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public async Task SchemaThreeRequiresAValidContract(int major, bool valid)
    {
        using var feed = new SignedFeed();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 3, artifactName = "briosa-0.7.0-sa-2099.1.0101.1-win-x64",
            briosaVersion = "0.7.0", runtimeIdentifier = "win-x64",
            spatialAnalyzerTarget = "2099.1.0101.1", protocolPackage = "briosa",
            spatialAnalyzerBundled = false, compatibility = new { major, revision = 0 }
        });
        var package = feed.AddServer("0.7.0", manifestOverride: manifest);
        feed.Publish();
        var registry = new MemoryRegistry();
        var store = new PackageStore(feed.StorePath, installationRegistry: registry);
        if (valid)
        {
            await store.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash);
            Assert.Single(registry.Items);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<ManagementException>(() =>
                store.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash));
            Assert.Equal(ManagementError.InvalidManifest, failure.Code);
            Assert.Empty(store.List());
            Assert.Empty(registry.Items);
        }
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
