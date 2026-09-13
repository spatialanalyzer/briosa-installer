using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Briosa.Installer.Core;

public enum SdkEvidenceState { Observed, UnquotedPath, MissingFile, Unresolved, ServiceRegistration, OtherRegistration }
public sealed record SdkObservation(string Kind, string Context, string Version, string Location, string Status,
    SdkEvidenceState State = SdkEvidenceState.Observed);
public sealed record SdkReport(DateTimeOffset ObservedAt, IReadOnlyList<SdkObservation> Observations, string Guidance)
{
    public const string ConfiguredRegistration = "Configured SDK registration";
    public object Sanitized() => new { observedAt = ObservedAt, observations = Observations.Select(o => new { o.Kind, o.Context,
        version = SdkDiscoveryAnalysis.NormalizeVersion(o.Version), o.Status, o.State }), guidance = Guidance };
}
public interface ISdkDiscovery { SdkReport Inspect(); }
public sealed class WindowsSdkDiscovery : ISdkDiscovery
{
    // From the committed SA 2026.1.0529.7 interop public API, SpatialAnalyzerSDKClass GuidAttribute.
    private const string ClassId = "{7C73DBF2-1921-48FE-99A6-2F8FE124E1DE}";
    public SdkReport Inspect()
    {
        if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform);
        var registrations = new List<SdkComRegistration>();
        var products = new List<SdkProductRegistration>();
        var issues = new List<SdkObservation>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine, RegistryHive.ClassesRoot })
            {
                var context = $"{hive} / {(view == RegistryView.Registry64 ? "64-bit" : "32-bit")}";
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    // Product discovery must survive failures reading the COM registration.
                    try { ReadRegistration(root, hive, view, registrations); }
                    catch (Exception e) when (ReadFailure(e)) { Incomplete(context + " / COM registration"); }
                    if (hive == RegistryHive.ClassesRoot) continue;
                    using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var entry = uninstall.OpenSubKey(name);
                            if (entry?.GetValue("DisplayName") is not string display ||
                                !(display.Contains("SpatialAnalyzer", StringComparison.OrdinalIgnoreCase) ||
                                  display.Contains("Spatial Analyzer", StringComparison.OrdinalIgnoreCase))) continue;
                            products.Add(new(context, entry.GetValue("DisplayVersion") as string,
                                entry.GetValue("InstallLocation") as string, entry.GetValue("DisplayIcon") as string));
                        }
                        catch (Exception e) when (ReadFailure(e)) { Incomplete(context + " / installed product"); }
                    }
                }
                catch (Exception e) when (ReadFailure(e)) { Incomplete(context); }
            }
        }
        return SdkDiscoveryAnalysis.Analyze(registrations, products, issues, File.Exists, FileVersion);

        void Incomplete(string context) => issues.Add(new("Discovery incomplete", context, "Unknown", "Not disclosed",
            "Access denied or unreadable registration", SdkEvidenceState.Unresolved));
    }

    [SupportedOSPlatform("windows")]
    private static void ReadRegistration(RegistryKey root, RegistryHive hive, RegistryView view, List<SdkComRegistration> registrations)
    {
        var prefix = hive == RegistryHive.ClassesRoot ? "" : @"Software\Classes\";
        using var clsid = root.OpenSubKey(prefix + @"CLSID\" + ClassId);
        if (clsid is null) return;
        using var local = clsid.OpenSubKey("LocalServer32");
        var service = clsid.GetValue("LocalService") as string;
        if (clsid.GetValue("AppID") is string appId && Guid.TryParse(appId, out var id))
        {
            using var app = root.OpenSubKey(prefix + @"AppID\" + id.ToString("B"));
            service ??= app?.GetValue("LocalService") as string;
        }
        registrations.Add(new(hive, view, local?.GetValue(null) as string, local?.GetValue("ServerExecutable") as string, service));
    }

    private static bool ReadFailure(Exception e) => e is UnauthorizedAccessException or System.Security.SecurityException or IOException;
    private static string FileVersion(string path)
    {
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? "Unknown"; }
        catch (Exception e) when (ReadFailure(e) || e is ArgumentException) { return "Unknown"; }
    }
}
