using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Briosa.Installer.Core;

public enum SdkRegistrationError
{
    UnsupportedPlatform, UnknownInstallation, UntrustedInstallation, UnsupportedProcedure,
    UntrustedPublisher, IncompleteDiscovery, UserOverride, ServiceRegistration, RunningApplications,
    ConcurrentChange, AlreadyConfigured, MaintenanceBusy, AccessDenied
}
public sealed class SdkRegistrationException(SdkRegistrationError code) : Exception(MessageFor(code))
{
    public SdkRegistrationError Code { get; } = code;
    public static string MessageFor(SdkRegistrationError code) => code switch
    {
        SdkRegistrationError.UnknownInstallation => "Refresh SDK Setup and choose an installed SA release with a matching SDK executable.",
        SdkRegistrationError.UntrustedInstallation => "SDK registration requires an installation protected from standard-user changes. Ask IT to check its location and permissions.",
        SdkRegistrationError.UnsupportedProcedure => "This installation does not contain the reviewed Hexagon SDK registration procedure.",
        SdkRegistrationError.UntrustedPublisher => "The SDK executable's approved Hexagon signature could not be verified using Windows cached trust information.",
        SdkRegistrationError.IncompleteDiscovery => "Some registry or installation information could not be read. Resolve that before changing SDK registration.",
        SdkRegistrationError.UserOverride => "A per-user SDK registration overrides machine setup. Ask IT to resolve that override before changing the machine registration.",
        SdkRegistrationError.ServiceRegistration => "This SDK is registered as a service. Use your administrator's service maintenance procedure.",
        SdkRegistrationError.RunningApplications => "Close SpatialAnalyzer, the SA SDK, and Briosa servers before changing SDK registration. The installer will not stop them.",
        SdkRegistrationError.ConcurrentChange => "SDK setup or the selected installation changed after review. Refresh and review the change again.",
        SdkRegistrationError.AlreadyConfigured => "This SDK is already configured.",
        SdkRegistrationError.MaintenanceBusy => "Another SDK registration change is in progress. Wait for it to finish, then refresh.",
        SdkRegistrationError.AccessDenied => "SDK maintenance could not access the required files or registry information. Check permissions with IT.",
        _ => "SDK registration changes require Windows.",
    };
}
public sealed record SdkRegistrationTarget(string Directory, string Version, string Executable, string Sha256);
public sealed record SdkRegistrationPlan(SdkRegistrationTarget Target, string BeforeFingerprint, string CurrentVersion, string? PreviousDirectory);
public enum SdkRegistrationResultCode { Succeeded, Cancelled, VendorFailed, VerificationFailed, OutcomeUnknown }
public sealed record SdkRegistrationResult(SdkRegistrationResultCode Code, SdkReport? Report, string CheckpointPath)
{
    public string Message => Code switch
    {
        SdkRegistrationResultCode.Succeeded => "SDK registration changed and verified. New SDK sessions will use this registration.",
        SdkRegistrationResultCode.Cancelled => "Administrator approval was cancelled. The registration command was not started.",
        SdkRegistrationResultCode.VendorFailed => "Hexagon's registration command reported a failure. Review the current setup before trying again.",
        SdkRegistrationResultCode.VerificationFailed => "The registration command finished, but the selected SDK could not be verified. Review the current setup before trying again.",
        _ => "The registration command's outcome is unknown. It may still be running. Wait for it to finish, then refresh and review the current setup.",
    };
}
public interface ISdkRegistrationService
{
    Task<SdkRegistrationPlan> PrepareAsync(string installationDirectory);
    Task<SdkRegistrationResult> ApplyAsync(SdkRegistrationPlan plan);
}
internal enum VendorRegistrationResult { Completed, Cancelled, Failed, Unknown }
internal interface ISdkRegistrationEnvironment
{
    SdkReport Inspect();
    SdkRegistrationTarget ValidateTarget(string directory, SdkReport report);
    void RequireIdle();
    IDisposable AcquireMaintenance();
    string SaveCheckpoint(SdkRegistrationPlan plan, SdkReport before);
    void CompleteCheckpoint(string path, SdkRegistrationResult result);
    VendorRegistrationResult Register(SdkRegistrationTarget target);
}
public sealed class SdkRegistrationService : ISdkRegistrationService
{
    private readonly ISdkRegistrationEnvironment environment;
    public SdkRegistrationService()
    {
        if (!OperatingSystem.IsWindows()) throw new SdkRegistrationException(SdkRegistrationError.UnsupportedPlatform);
        environment = new WindowsSdkRegistrationEnvironment();
    }
    internal SdkRegistrationService(ISdkRegistrationEnvironment environment) => this.environment = environment;
    public static string ReviewFingerprint(SdkRegistrationPlan plan) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan))));
    public Task<SdkRegistrationPlan> PrepareAsync(string installationDirectory) => Task.Run(() =>
    {
        var report = environment.Inspect();
        ValidateSetup(report);
        var target = environment.ValidateTarget(installationDirectory, report);
        if (Matches(report, target)) throw new SdkRegistrationException(SdkRegistrationError.AlreadyConfigured);
        environment.RequireIdle();
        var registrations = report.Observations.Where(o => o.Kind == SdkReport.ConfiguredRegistration).ToArray();
        var previous = registrations.Select(o => o.Location).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var previousDirectory = previous.Length == 1 && Path.IsPathFullyQualified(previous[0]) ? Path.GetDirectoryName(previous[0]) : null;
        return new SdkRegistrationPlan(target, Fingerprint(report),
            registrations.Length == 0 ? "Not found" : string.Join(", ", registrations.Select(o => o.Version).Distinct()), previousDirectory);
    });
    public Task<SdkRegistrationResult> ApplyAsync(SdkRegistrationPlan plan) => Task.Run(() =>
    {
        using var maintenance = environment.AcquireMaintenance();
        var before = environment.Inspect();
        ValidateSetup(before);
        var target = environment.ValidateTarget(plan.Target.Directory, before);
        if (target != plan.Target || Fingerprint(before) != plan.BeforeFingerprint)
            throw new SdkRegistrationException(SdkRegistrationError.ConcurrentChange);
        environment.RequireIdle();
        var checkpoint = environment.SaveCheckpoint(plan, before);
        var vendor = environment.Register(target);
        SdkReport? after = null;
        try { after = environment.Inspect(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SdkRegistrationException) { }
        var code = vendor switch
        {
            VendorRegistrationResult.Cancelled => SdkRegistrationResultCode.Cancelled,
            VendorRegistrationResult.Failed => SdkRegistrationResultCode.VendorFailed,
            VendorRegistrationResult.Unknown => SdkRegistrationResultCode.OutcomeUnknown,
            _ => after is not null && Matches(after, target) ? SdkRegistrationResultCode.Succeeded : SdkRegistrationResultCode.VerificationFailed,
        };
        var result = new SdkRegistrationResult(code, after, checkpoint);
        // A journal write failure must not hide a completed/unknown vendor mutation.
        try { environment.CompleteCheckpoint(checkpoint, result); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return result;
    });
    private static void ValidateSetup(SdkReport report)
    {
        if (report.Observations.Any(o => o.Kind == "Discovery incomplete"))
            throw new SdkRegistrationException(SdkRegistrationError.IncompleteDiscovery);
        var registrations = report.Observations.Where(o => o.Kind is SdkReport.ConfiguredRegistration or "Other SDK registration").ToArray();
        if (registrations.Any(o => o.Context.Contains("CurrentUser", StringComparison.Ordinal)))
            throw new SdkRegistrationException(SdkRegistrationError.UserOverride);
        if (registrations.Any(o => o.State == SdkEvidenceState.ServiceRegistration))
            throw new SdkRegistrationException(SdkRegistrationError.ServiceRegistration);
    }
    internal static bool Matches(SdkReport report, SdkRegistrationTarget target)
    {
        var registrations = report.Observations.Where(o => o.Kind == SdkReport.ConfiguredRegistration).ToArray();
        return !report.Observations.Any(o => o.Kind is "Discovery incomplete" or "Other SDK registration") &&
            registrations.Length > 0 && registrations.All(o =>
                o.State is SdkEvidenceState.Observed or SdkEvidenceState.UnquotedPath &&
                o.Version == target.Version && string.Equals(o.Location, target.Executable, StringComparison.OrdinalIgnoreCase) &&
                o.Context.Contains("LocalMachine", StringComparison.Ordinal));
    }
    internal static string Fingerprint(SdkReport report) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(report.Observations.OrderBy(o => o.Kind).ThenBy(o => o.Context).ThenBy(o => o.Location)))));
}
