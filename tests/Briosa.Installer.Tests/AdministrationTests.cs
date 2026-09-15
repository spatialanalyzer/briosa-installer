using System.Security.AccessControl;
using System.Security.Principal;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class AdministrationTests
{
    [Theory]
    [InlineData("[]")]
    [InlineData("""{"schemaVersion":"2","artifactName":"briosa-1.0.0-sa-2099.1.0101.1-win-x64","briosaVersion":"1.0.0","runtimeIdentifier":"win-x64"}""")]
    [InlineData("""{"schemaVersion":2,"schemaVersion":1}""")]
    public async Task MalformedSignedManifestsFailWithoutCommittingOrCrashing(string json)
    {
        using var feed = new SignedFeed();
        var package = feed.AddServer("1.0.0", manifestOverride: System.Text.Encoding.UTF8.GetBytes(json));
        feed.Publish();
        var store = new PackageStore(feed.StorePath);
        await Assert.ThrowsAsync<ManagementException>(() => store.InstallAsync(feed.Settings, CatalogComponent.Server, package.Id, feed.Hash));
        Assert.Empty(store.List());
        Assert.Empty(Directory.GetDirectories(Path.Combine(feed.StorePath, "transactions")));
    }

    [Fact]
    public async Task BootstrapRefreshUsesVerifiedLauncherAndPreservesSelectedVersionOnFileLock()
    {
        using var feed = new SignedFeed();
        var installer = feed.AddInstaller("1.0.0");
        feed.Publish();
        var store = new PackageStore(feed.StorePath);
        var installed = await store.InstallAsync(feed.Settings, CatalogComponent.Installer, installer.Id, feed.Hash);
        await store.ActivateInstallerAsync(installer.Id);
        var initial = Path.Combine(feed.Root, "initial");
        Directory.CreateDirectory(initial);
        InstallerJson.Write(Path.Combine(initial, "manifest.json"), new { component = "installer" });
        var launcher = Path.Combine(initial, "Briosa.Launcher.exe");
        await File.WriteAllTextAsync(launcher, "Old inert bootstrap");
        using (var locked = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = await Assert.ThrowsAsync<ManagementException>(() => BootstrapUpdater.RefreshAsync(store, installer.Id, launcher));
            Assert.Equal(ManagementError.BootstrapUpdateFailed, failure.Code);
        }
        Assert.NotNull(await store.ResolveActiveInstallerAsync());
        await BootstrapUpdater.RefreshAsync(store, installer.Id, launcher);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(installed.Directory, "payload", "Briosa.Launcher.exe")), await File.ReadAllBytesAsync(launcher));
        await Assert.ThrowsAsync<ManagementException>(() => BootstrapUpdater.RefreshAsync(store, installer.Id,
            Path.Combine(installed.Directory, "payload", "Briosa.Launcher.exe")));
        await store.VerifyAsync(installer.Id);
        Assert.Empty(Directory.GetFiles(initial, "*.tmp"));
    }

    [Fact]
    public void PolicyAppliesPublisherAndScopeRestrictionsToPreviouslyInstalledApps()
    {
        var allowed = new string('a', 64);
        var policy = new EnterprisePolicy(1, ["https://mirror.example.com/briosa/"], [allowed], AllowUserStore: false);
        policy.ValidateInstalled(allowed, PackageStore.MachineRoot + Path.DirectorySeparatorChar);
        Assert.Throws<ManagementException>(() => policy.ValidateInstalled(allowed, PackageStore.UserRoot));
        Assert.Throws<ManagementException>(() => policy.ValidateInstalled(new string('b', 64), PackageStore.MachineRoot));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void MachineStoreAclRequiresAdministrativeOwnershipAndProtectsWrites()
    {
        if (!OperatingSystem.IsWindows()) return;
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var users = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var acl = new DirectorySecurity(); acl.SetOwner(admin);
        acl.AddAccessRule(new FileSystemAccessRule(admin, FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        WindowsStoreProtection.Validate(acl);
        acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.WriteData, AccessControlType.Allow));
        Assert.Equal(ManagementError.AccessDenied, Assert.Throws<ManagementException>(() => WindowsStoreProtection.Validate(acl)).Code);
        acl.RemoveAccessRuleAll(new FileSystemAccessRule(users, FileSystemRights.WriteData, AccessControlType.Allow));
        acl.SetOwner(users);
        Assert.Throws<ManagementException>(() => WindowsStoreProtection.Validate(acl));
    }
    [Fact]
    public void ExportDropsUnstructuredActivityAndVersionData()
    {
        using var feed = new SignedFeed();
        InstallerJson.Write(Path.Combine(feed.Root, "activity.json"), new[]
        {
            new ActivityEntry(DateTimeOffset.UtcNow, "Package.Install", "Succeeded"),
            new ActivityEntry(DateTimeOffset.UtcNow, "https://host/private-token", "secret path"),
        });
        var activity = new ActivityStore(feed.Root);
        var report = new SdkReport(DateTimeOffset.UtcNow,
            [new("Registered SDK candidate", "CurrentUser / 64-bit", @"C:\private-job", @"C:\private-install", "Candidate only")], "No activation");
        var output = Path.Combine(feed.Root, "support.json"); activity.Export(output, report);
        var text = File.ReadAllText(output);
        Assert.DoesNotContain("private-token", text);
        Assert.DoesNotContain("private-job", text);
        Assert.DoesNotContain("private-install", text);
        Assert.Contains("Package.Install", text);
    }
    [Fact]
    public void DistributionDefaultsSeedAnEditorWithoutBecomingSelectedUserSettings()
    {
        using var feed = new SignedFeed();
        File.WriteAllText(Path.Combine(feed.Root, "public-source.json"), SettingsCodec.Serialize(feed.Settings));
        Assert.Equal(feed.Settings, DistributionDefaults.Load(feed.Root));
        var paths = new ConfigurationPaths(Path.Combine(feed.Root, "user-settings.json"));
        Assert.Null(Assert.IsType<Outcome<SettingsSnapshot>.Success>(new SettingsStore().Load(paths)).Value.Settings);
    }
}
