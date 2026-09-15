using Briosa.Installer.Core;
using Xunit;

namespace Briosa.Installer.Tests;

public sealed class ActivityPresentationTests
{
    [Fact]
    public void PackageContextAndDurationSurviveHistoryAndExport()
    {
        using var feed = new SignedFeed();
        var package = feed.AddServer("0.2.0");
        var activity = new ActivityStore(feed.Root);
        Assert.True(activity.Record("Package.Install", "Succeeded", package, 1500));
        var entry = Assert.Single(activity.Read());
        Assert.Equal(package.Version, entry.Version);
        Assert.Equal(package.SpatialAnalyzerTarget, entry.Target);
        Assert.Equal("server", entry.Component);
        Assert.Equal(1500, entry.DurationMs);
        var output = Path.Combine(feed.Root, "report.json"); activity.Export(output);
        Assert.Contains("0.2.0", File.ReadAllText(output));
    }

    [Fact]
    public void CorruptHistoryIsReportedAndNotOverwrittenByAnotherOperation()
    {
        using var feed = new SignedFeed();
        var path = Path.Combine(feed.Root, "activity.json");
        File.WriteAllText(path, "{invalid");
        var activity = new ActivityStore(feed.Root);
        Assert.Empty(activity.Read()); Assert.True(activity.ReadFailed);
        Assert.False(activity.Record("Sdk.Inspect", "Succeeded"));
        Assert.Equal("{invalid", File.ReadAllText(path));
    }

    [Fact]
    public void HistoryDropsUnsafeOptionalMetadata()
    {
        using var feed = new SignedFeed();
        InstallerJson.Write(Path.Combine(feed.Root, "activity.json"), new[]
        {
            new ActivityEntry(DateTimeOffset.UtcNow, "Package.Install", "Succeeded", "private-client", "https://token", @"C:\private-job", -1),
        });
        var entry = Assert.Single(new ActivityStore(feed.Root).Read());
        Assert.Null(entry.Component); Assert.Null(entry.Version); Assert.Null(entry.Target); Assert.Null(entry.DurationMs);
    }

    [Fact]
    public async Task SelectionMetadataSurvivesRestartButDoesNotBypassLaunchVerification()
    {
        using var feed = new SignedFeed();
        var package = feed.AddInstaller("0.3.0"); feed.Publish();
        var store = new PackageStore(feed.StorePath);
        await store.InstallAsync(feed.Settings, CatalogComponent.Installer, package.Id, feed.Hash);
        await store.ActivateInstallerAsync(package.Id);
        var reopened = new PackageStore(feed.StorePath);
        Assert.Equal(package.Id, reopened.ReadInstallerSelection()?.Id);
        var selected = reopened.ReadInstallerSelection()!;
        File.WriteAllText(Path.Combine(selected.Directory, "payload", "Briosa.Installer.exe"), "damaged inert fixture");
        Assert.Equal(package.Id, reopened.ReadInstallerSelection()?.Id);
        Assert.Equal(ManagementError.IntegrityFailure, (await Assert.ThrowsAsync<ManagementException>(() => reopened.ResolveActiveInstallerAsync())).Code);
    }
}

