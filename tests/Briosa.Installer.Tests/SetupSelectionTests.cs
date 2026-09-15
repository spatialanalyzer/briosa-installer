using Briosa.Installer.Cli;
using Briosa.Installer.Core;
using Xunit;

namespace Briosa.Installer.Tests;

public sealed class SetupSelectionTests
{
    [Fact]
    public async Task SetupSupersedesOlderSelectionAndPreservesNewerVersionsAndExplicitRollback()
    {
        using var feed = new SignedFeed();
        var older = feed.AddInstaller("0.1.0");
        var newer = feed.AddInstaller("0.3.0");
        var server = feed.AddServer("0.4.0"); feed.Publish();
        var store = new PackageStore(feed.StorePath);
        foreach (var package in new[] { older, newer, server })
            await store.InstallAsync(feed.Settings, package.Component, package.Id, feed.Hash);
        await store.ActivateInstallerAsync(older.Id);
        Assert.True(store.PreferBundledInstaller("0.2.0"));
        Assert.Null(await store.ResolveActiveInstallerAsync());
        Assert.Equal(3, store.List().Count);
        await store.ActivateInstallerAsync(newer.Id);
        Assert.False(store.PreferBundledInstaller("0.2.0"));
        Assert.False(store.PreferBundledInstaller("0.3.0"));
        Assert.Equal(newer.Id, store.ReadInstallerSelection()!.Id);
        await store.ActivateInstallerAsync(older.Id);
        Assert.Equal(older.Id, store.ReadInstallerSelection()!.Id);
        foreach (var package in store.List()) await store.VerifyAsync(package.Id);
    }

    [Fact]
    public async Task SetupSelectionRequiresConfirmationAndDoesNotCreateAnEmptyStore()
    {
        using var feed = new SignedFeed();
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(6, await ManagementCommands.RunAsync(["app", "prefer-bundled", "--version", "0.1.0", "--store", feed.StorePath], output, error));
        Assert.Equal(0, await ManagementCommands.RunAsync(["app", "prefer-bundled", "--version", "0.1.0", "--store", feed.StorePath, "--yes"], output, error));
        Assert.False(Directory.Exists(feed.StorePath));
        Assert.Throws<ManagementException>(() => new PackageStore(feed.StorePath).PreferBundledInstaller("invalid"));
    }
}
