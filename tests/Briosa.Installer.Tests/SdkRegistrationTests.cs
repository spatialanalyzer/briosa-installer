using Briosa.Installer.Cli;
using Briosa.Installer.Core;

namespace Briosa.Installer.Tests;

public sealed class SdkRegistrationTests
{
    private static readonly SdkRegistrationTarget Target = new(@"C:\SA\new", "2099.2.0202.2", @"C:\SA\new\SpatialAnalyzerSDK.exe", "new-hash");
    private static SdkReport Report(bool selected = false) => new(DateTimeOffset.UtcNow,
        [new(SdkReport.ConfiguredRegistration, "ClassesRoot / 32-bit (matches LocalMachine)",
            selected ? Target.Version : "2099.1.0101.1", selected ? Target.Executable : @"C:\SA\old\SpatialAnalyzerSDK.exe", "Fixture", SdkEvidenceState.UnquotedPath)], "Fixture");

    [Fact]
    public async Task SavesBeforeMutationAndRequiresObservedRegistrationAfterward()
    {
        var environment = new Fixture(); var service = new SdkRegistrationService(environment);
        var plan = await service.PrepareAsync(Target.Directory);
        var result = await service.ApplyAsync(plan);
        Assert.Equal(SdkRegistrationResultCode.Succeeded, result.Code);
        Assert.Equal(["Lock", "Checkpoint", "Register", "Result", "Unlock"], environment.Events);
        Assert.Equal("2099.1.0101.1", plan.CurrentVersion);
        Assert.Equal(Target.Version, result.Report!.Observations.Single().Version);
    }
    [Theory]
    [InlineData((int)VendorRegistrationResult.Cancelled, SdkRegistrationResultCode.Cancelled)]
    [InlineData((int)VendorRegistrationResult.Failed, SdkRegistrationResultCode.VendorFailed)]
    [InlineData((int)VendorRegistrationResult.Unknown, SdkRegistrationResultCode.OutcomeUnknown)]
    [InlineData((int)VendorRegistrationResult.Completed, SdkRegistrationResultCode.VerificationFailed)]
    public async Task DoesNotTurnAnExitCodeIntoVerifiedSuccess(int vendor, SdkRegistrationResultCode expected)
    {
        var environment = new Fixture { Vendor = (VendorRegistrationResult)vendor, ChangeRegistration = false };
        var service = new SdkRegistrationService(environment);
        var result = await service.ApplyAsync(await service.PrepareAsync(Target.Directory));
        Assert.Equal(expected, result.Code);
        Assert.Equal(1, environment.RegisterCalls); // No retry or automatic rollback.
    }
    [Theory]
    [InlineData(SdkRegistrationError.RunningApplications)]
    [InlineData(SdkRegistrationError.UntrustedPublisher)]
    [InlineData(SdkRegistrationError.UntrustedInstallation)]
    [InlineData(SdkRegistrationError.UnsupportedProcedure)]
    public async Task RechecksBlockersAfterReview(SdkRegistrationError error)
    {
        var environment = new Fixture(); var service = new SdkRegistrationService(environment);
        var plan = await service.PrepareAsync(Target.Directory);
        environment.Blocker = error;
        Assert.Equal(error, (await Assert.ThrowsAsync<SdkRegistrationException>(() => service.ApplyAsync(plan))).Code);
        Assert.Equal(0, environment.RegisterCalls);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChangedRegistryOrExecutableRejectsStaleReview(bool registry)
    {
        var environment = new Fixture(); var service = new SdkRegistrationService(environment);
        var plan = await service.PrepareAsync(Target.Directory);
        if (registry) environment.Current = Report(true);
        else environment.TargetIdentity = Target with { Sha256 = "replaced" };
        Assert.Equal(SdkRegistrationError.ConcurrentChange,
            (await Assert.ThrowsAsync<SdkRegistrationException>(() => service.ApplyAsync(plan))).Code);
        Assert.Equal(0, environment.RegisterCalls);
    }
    [Theory]
    [InlineData("Discovery incomplete", "Fixture", SdkEvidenceState.Unresolved, SdkRegistrationError.IncompleteDiscovery)]
    [InlineData(SdkReport.ConfiguredRegistration, "ClassesRoot (matches CurrentUser)", SdkEvidenceState.Observed, SdkRegistrationError.UserOverride)]
    [InlineData(SdkReport.ConfiguredRegistration, "ClassesRoot", SdkEvidenceState.ServiceRegistration, SdkRegistrationError.ServiceRegistration)]
    public async Task UnsupportedSetupNeverLaunchesVendor(string kind, string context, SdkEvidenceState state, SdkRegistrationError error)
    {
        var environment = new Fixture { Current = new(DateTimeOffset.UtcNow, [new(kind, context, "Unknown", "", "", state)], "") };
        var service = new SdkRegistrationService(environment);
        Assert.Equal(error, (await Assert.ThrowsAsync<SdkRegistrationException>(() => service.PrepareAsync(Target.Directory))).Code);
        Assert.Equal(0, environment.RegisterCalls);
    }
    [Fact]
    public async Task AlreadyConfiguredIsNotReregistered()
    {
        var environment = new Fixture { Current = Report(true) };
        Assert.Equal(SdkRegistrationError.AlreadyConfigured,
            (await Assert.ThrowsAsync<SdkRegistrationException>(() => new SdkRegistrationService(environment).PrepareAsync(Target.Directory))).Code);
        Assert.Equal(0, environment.RegisterCalls);
    }
    [Fact]
    public async Task CannotProceedWithoutCheckpoint()
    {
        var environment = new Fixture { CheckpointFails = true }; var service = new SdkRegistrationService(environment);
        var plan = await service.PrepareAsync(Target.Directory);
        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(plan));
        Assert.Equal(0, environment.RegisterCalls);
    }
    [Fact]
    public async Task UnreadablePostStateCannotBeSuccessful()
    {
        var environment = new Fixture { PostReadFails = true }; var service = new SdkRegistrationService(environment);
        var result = await service.ApplyAsync(await service.PrepareAsync(Target.Directory));
        Assert.Equal(SdkRegistrationResultCode.VerificationFailed, result.Code);
        Assert.Null(result.Report);
    }
    [Fact]
    public async Task ResultWriteFailureDoesNotHideActualMutation()
    {
        var environment = new Fixture { ResultWriteFails = true }; var service = new SdkRegistrationService(environment);
        Assert.Equal(SdkRegistrationResultCode.Succeeded, (await service.ApplyAsync(await service.PrepareAsync(Target.Directory))).Code);
        Assert.Equal(1, environment.RegisterCalls);
    }
    [Fact]
    public async Task CliRequiresExplicitReviewHashAndConfirmation()
    {
        var environment = new Fixture(); var service = new SdkRegistrationService(environment);
        using var output = new StringWriter(); using var error = new StringWriter();
        Assert.Equal(2, await SdkCommands.RunAsync(["use", "--installation", Target.Directory], output, error, service));
        Assert.Equal(6, await SdkCommands.RunAsync(["use", "--installation", Target.Directory, "--review-sha256", "stale", "--yes"], output, error, service));
        Assert.Equal(0, environment.RegisterCalls);
        var plan = await service.PrepareAsync(Target.Directory);
        Assert.Equal(0, await SdkCommands.RunAsync(["use", "--installation", Target.Directory, "--review-sha256",
            SdkRegistrationService.ReviewFingerprint(plan), "--yes"], output, error, service));
        Assert.Equal(1, environment.RegisterCalls);
    }
    [Fact]
    public void VerificationRejectsSameVersionInAnotherFolderAndPerUserRegistration()
    {
        var report = Report(true);
        Assert.False(SdkRegistrationService.Matches(report with { Observations = [report.Observations[0] with { Location = @"C:\copy\SpatialAnalyzerSDK.exe" }] }, Target));
        Assert.False(SdkRegistrationService.Matches(report with { Observations = [report.Observations[0] with { Context = "ClassesRoot (matches CurrentUser)" }] }, Target));
        Assert.Equal(SdkRegistrationService.Fingerprint(report), SdkRegistrationService.Fingerprint(report with { ObservedAt = DateTimeOffset.UtcNow.AddHours(1) }));
    }
    private sealed class Fixture : ISdkRegistrationEnvironment
    {
        public SdkReport Current = Report();
        public SdkRegistrationTarget TargetIdentity = Target;
        public bool ChangeRegistration = true, CheckpointFails, PostReadFails, ResultWriteFails;
        public SdkRegistrationError? Blocker;
        public VendorRegistrationResult Vendor = VendorRegistrationResult.Completed;
        public int RegisterCalls;
        public List<string> Events = [];
        public SdkReport Inspect() => PostReadFails && RegisterCalls > 0 ? throw new IOException("Fixture") : Current;
        public SdkRegistrationTarget ValidateTarget(string directory, SdkReport report)
        { if (Blocker is { } error && error != SdkRegistrationError.RunningApplications) throw new SdkRegistrationException(error); return TargetIdentity; }
        public void RequireIdle() { if (Blocker == SdkRegistrationError.RunningApplications) throw new SdkRegistrationException(Blocker.Value); }
        public IDisposable AcquireMaintenance() { Events.Add("Lock"); return new Held(() => Events.Add("Unlock")); }
        public string SaveCheckpoint(SdkRegistrationPlan plan, SdkReport before)
        { Events.Add("Checkpoint"); if (CheckpointFails) throw new IOException("Fixture"); return "fixture-checkpoint"; }
        public void CompleteCheckpoint(string path, SdkRegistrationResult result)
        { Events.Add("Result"); if (ResultWriteFails) throw new IOException("Fixture"); }
        public VendorRegistrationResult Register(SdkRegistrationTarget target)
        { RegisterCalls++; Events.Add("Register"); if (ChangeRegistration) Current = Report(true); return Vendor; }
        private sealed class Held(Action release) : IDisposable { public void Dispose() => release(); }
    }
}
