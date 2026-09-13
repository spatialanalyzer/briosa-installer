using System.Text.Json;
using Briosa.Installer.Core;
using Microsoft.Win32;

namespace Briosa.Installer.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SdkDiscoveryTests
{
    private const string OldDirectory = @"C:\Program Files (x86)\Vendor\SpatialAnalyzer 2024.1.0508.5";
    private const string NewDirectory = @"C:\Program Files (x86)\Vendor\SpatialAnalyzer 2026.1.0529.7";
    private static string Sdk(string directory) => Path.Combine(directory, "SpatialAnalyzerSDK.exe");
    private static SdkComRegistration Registration(string? command, RegistryHive hive = RegistryHive.ClassesRoot,
        RegistryView view = RegistryView.Registry32) => new(hive, view, command);
    private static SdkReport Analyze(SdkComRegistration[] registrations, SdkProductRegistration[] products,
        Dictionary<string, string> files, SdkObservation[]? issues = null) =>
        SdkDiscoveryAnalysis.Analyze(registrations, products, issues ?? [], files.ContainsKey, p => files[p]);

    [Fact]
    public void VendorMetadataWithoutInstallLocationFindsAllSdkFilesAndOneConfiguredRegistration()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var products = new[] { OldDirectory, NewDirectory }.Select(directory =>
        {
            var version = directory == OldDirectory ? "2024, 1, 0508, 5" : "2026, 1, 0529, 7";
            files[Sdk(directory)] = version;
            var icon = Path.Combine(directory, "Uninst.exe");
            files[icon] = "1.0";
            return new SdkProductRegistration("LocalMachine / 32-bit", version, null, icon + ", 0");
        }).ToArray();
        var report = Analyze([Registration(Sdk(OldDirectory), RegistryHive.LocalMachine), Registration(Sdk(OldDirectory))], products, files);
        Assert.Equal(2, report.Observations.Count(o => o.Kind == "Installed SDK file"));
        Assert.Equal(2, report.Observations.Count(o => o.Kind == "Installed SA product"));
        var configured = Assert.Single(report.Observations, o => o.Kind == SdkReport.ConfiguredRegistration);
        Assert.Equal("2024.1.0508.5", configured.Version);
        Assert.Equal(SdkEvidenceState.UnquotedPath, configured.State);
        Assert.Equal(Sdk(OldDirectory), configured.Location);
        Assert.Contains("matches LocalMachine", configured.Context);
        Assert.DoesNotContain(report.Observations, o => o.Kind == "Other SDK registration");
        Assert.Contains("activation not observed", configured.Status);
        var exported = JsonSerializer.Serialize(report.Sanitized());
        Assert.Contains("2024.1.0508.5", exported);
        Assert.DoesNotContain("Program Files", exported);
        Assert.DoesNotContain("Vendor", exported);
    }

    [Fact]
    public void ExplicitServerExecutableOverridesCommandAndDoesNotFallBackWhenMissing()
    {
        var files = new Dictionary<string, string> { [Sdk(OldDirectory)] = "2024, 1, 0508, 5", [Sdk(NewDirectory)] = "2026, 1, 0529, 7" };
        var registration = Registration(Sdk(OldDirectory)) with { ServerExecutable = Sdk(NewDirectory) };
        var configured = Assert.Single(Analyze([registration], [], files).Observations);
        Assert.Equal("2026.1.0529.7", configured.Version);
        Assert.Equal(SdkEvidenceState.Observed, configured.State);
        files.Remove(Sdk(NewDirectory));
        configured = Assert.Single(Analyze([registration], [], files).Observations);
        Assert.Equal("Unknown", configured.Version);
        Assert.Equal(Sdk(NewDirectory), configured.Location);
        Assert.Equal(SdkEvidenceState.MissingFile, configured.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative.exe")]
    [InlineData("\"C:\\SDK\\SpatialAnalyzerSDK.exe\"")]
    public void InvalidExplicitExecutableNeverFallsBackToValidCommand(string explicitPath)
    {
        var result = SdkDiscoveryAnalysis.ResolveExecutable(Registration(@"""C:\SDK\SpatialAnalyzerSDK.exe""") with { ServerExecutable = explicitPath }, _ => true);
        Assert.Null(result.Path);
        Assert.Equal(SdkEvidenceState.Unresolved, result.State);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\SA\\SpatialAnalyzerSDK.exe\" /Embedding", "C:\\Program Files\\SA\\SpatialAnalyzerSDK.exe", SdkEvidenceState.Observed)]
    [InlineData("C:\\SDK\\SpatialAnalyzerSDK.exe /Embedding", "C:\\SDK\\SpatialAnalyzerSDK.exe", SdkEvidenceState.Observed)]
    [InlineData("C:\\Program Files\\SA\\SpatialAnalyzerSDK.exe /Embedding", "C:\\Program Files\\SA\\SpatialAnalyzerSDK.exe", SdkEvidenceState.UnquotedPath)]
    [InlineData("\"C:\\SDK\\SpatialAnalyzerSDK.exe", null, SdkEvidenceState.Unresolved)]
    [InlineData("\"C:\\SDK\\SpatialAnalyzerSDK.exe\"suffix", null, SdkEvidenceState.Unresolved)]
    [InlineData("SpatialAnalyzerSDK.exe", null, SdkEvidenceState.Unresolved)]
    [InlineData("C:\\SDK\\anything.cmd", null, SdkEvidenceState.Unresolved)]
    public void CommandParsingRetainsAmbiguityWithoutExecuting(string command, string? path, SdkEvidenceState state)
    {
        var result = SdkDiscoveryAnalysis.ResolveExecutable(Registration(command), p => p == path);
        Assert.Equal(path, result.Path);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void MultipleExistingUnquotedCandidatesStayUnresolved()
    {
        var result = SdkDiscoveryAnalysis.ResolveExecutable(Registration(@"C:\one.exe two.exe"), _ => true);
        Assert.Null(result.Path);
        Assert.Equal(SdkEvidenceState.Unresolved, result.State);
    }

    [Fact]
    public void MissingQuotedFileIsReportedWithoutBorrowingAnInstalledVersion()
    {
        var files = new Dictionary<string, string> { [Sdk(NewDirectory)] = "2026.1.0529.7" };
        var report = Analyze([Registration('"' + Sdk(OldDirectory) + '"')],
            [new("LocalMachine", "2026.1.0529.7", NewDirectory, null)], files);
        var configured = Assert.Single(report.Observations, o => o.Kind == SdkReport.ConfiguredRegistration);
        Assert.Equal("Unknown", configured.Version);
        Assert.Equal(SdkEvidenceState.MissingFile, configured.State);
        Assert.Single(report.Observations, o => o.Kind == "Installed SDK file");
    }

    [Fact]
    public void MergedUserRegistrationIsConfiguredAndDifferentMachineEntryRemainsEvidence()
    {
        var files = new Dictionary<string, string> { [Sdk(OldDirectory)] = "2024.1.0508.5", [Sdk(NewDirectory)] = "2026.1.0529.7" };
        var report = Analyze([Registration(Sdk(NewDirectory), RegistryHive.LocalMachine),
            Registration(Sdk(OldDirectory), RegistryHive.CurrentUser), Registration(Sdk(OldDirectory))], [], files);
        var configured = Assert.Single(report.Observations, o => o.Kind == SdkReport.ConfiguredRegistration);
        Assert.Equal("2024.1.0508.5", configured.Version);
        Assert.Contains("matches CurrentUser", configured.Context);
        var other = Assert.Single(report.Observations, o => o.Kind == "Other SDK registration");
        Assert.Equal("2026.1.0529.7", other.Version);
        Assert.Equal(SdkEvidenceState.OtherRegistration, other.State);
    }

    [Fact]
    public void DistinctRegistryViewsAreNotCollapsedOrGuessed()
    {
        var files = new Dictionary<string, string> { [Sdk(OldDirectory)] = "2024.1.0508.5", [Sdk(NewDirectory)] = "2026.1.0529.7" };
        var report = Analyze([Registration(Sdk(OldDirectory)), Registration(Sdk(NewDirectory), view: RegistryView.Registry64)], [], files);
        Assert.Equal(2, report.Observations.Count(o => o.Kind == SdkReport.ConfiguredRegistration));
        Assert.Contains(report.Observations, o => o.Context.Contains("32-bit") && o.Version == "2024.1.0508.5");
        Assert.Contains(report.Observations, o => o.Context.Contains("64-bit") && o.Version == "2026.1.0529.7");
    }

    [Fact]
    public void IncompleteMergedReadDoesNotPromoteMachineRegistrationToConfigured()
    {
        var report = Analyze([Registration(Sdk(OldDirectory), RegistryHive.LocalMachine)], [],
            new() { [Sdk(OldDirectory)] = "2024.1.0508.5" },
            [new("Discovery incomplete", "ClassesRoot / 32-bit", "Unknown", "Not disclosed", "Access denied")]);
        Assert.DoesNotContain(report.Observations, o => o.Kind == SdkReport.ConfiguredRegistration);
        Assert.Contains(report.Observations, o => o.Kind == "Other SDK registration");
        Assert.Contains(report.Observations, o => o.Kind == "Discovery incomplete");
    }

    [Fact]
    public void ServiceRegistrationDoesNotIdentifyLocalServerFileAsConfigured()
    {
        var report = Analyze([Registration(Sdk(OldDirectory)) with { LocalService = "ExampleService" }], [],
            new() { [Sdk(OldDirectory)] = "2024.1.0508.5" });
        var configured = Assert.Single(report.Observations);
        Assert.Equal("Unknown", configured.Version);
        Assert.Equal(SdkEvidenceState.ServiceRegistration, configured.State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("C:\\missing-install")]
    public void ExistingDisplayIconCanRecoverMissingOrStaleInstallLocation(string? location)
    {
        var icon = Path.Combine(NewDirectory, "Uninst.exe");
        var report = Analyze([], [new("LocalMachine", "2026.1.0529.7", location, '"' + icon + "\", -2")],
            new() { [icon] = "1.0", [Sdk(NewDirectory)] = "2026, 1, 0529, 7" });
        Assert.Equal(NewDirectory, report.Observations.Single(o => o.Kind == "Installed SA product").Location);
        Assert.Equal("2026.1.0529.7", report.Observations.Single(o => o.Kind == "Installed SDK file").Version);
    }

    [Fact]
    public void MissingIconDoesNotInventAnSdkAndDuplicateEntriesKeepOneFile()
    {
        var icon = Path.Combine(NewDirectory, "Uninst.exe");
        var missing = Analyze([], [new("Machine", "2026.1", null, icon + ", 0")],
            new() { [Sdk(NewDirectory)] = "2026.1.0529.7" });
        Assert.DoesNotContain(missing.Observations, o => o.Kind == "Installed SDK file");
        var duplicate = Analyze([], [new("Machine / 32-bit", "2026.1.0529.7", NewDirectory, null),
                new("Machine / 64-bit", "2026.1.0529.7", NewDirectory, null)],
            new() { [Sdk(NewDirectory)] = "2026.1.0529.7" });
        var sdk = Assert.Single(duplicate.Observations, o => o.Kind == "Installed SDK file");
        Assert.Contains("32-bit", sdk.Context);
        Assert.Contains("64-bit", sdk.Context);
    }

    [Theory]
    [InlineData("2026, 1, 0529, 7", "2026.1.0529.7")]
    [InlineData(" 2026.1.0529.7 ", "2026.1.0529.7")]
    [InlineData("C:\\private-job", "Unknown")]
    [InlineData("2026.1 private-data", "Unknown")]
    [InlineData("2026.1.2.3.4", "Unknown")]
    [InlineData(null, "Unknown")]
    public void VersionsNormalizeVendorPunctuationWithoutExportingUnstructuredData(string? input, string expected) =>
        Assert.Equal(expected, SdkDiscoveryAnalysis.NormalizeVersion(input));
}
