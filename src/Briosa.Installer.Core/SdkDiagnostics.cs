using System.Diagnostics;
using Microsoft.Win32;

namespace Briosa.Installer.Core;

public sealed record SdkObservation(string Kind, string Context, string Version, string Location, string Status);
public sealed record SdkReport(DateTimeOffset ObservedAt, IReadOnlyList<SdkObservation> Observations, string Guidance)
{
    public object Sanitized() => new { observedAt = ObservedAt, observations = Observations.Select(o => new { o.Kind, o.Context,
        version = System.Text.RegularExpressions.Regex.IsMatch(o.Version, @"\A[0-9]+(\.[0-9]+){1,3}\z") ? o.Version : "Unknown", o.Status }), guidance = Guidance };
}
public interface ISdkDiscovery { SdkReport Inspect(); }
public sealed class WindowsSdkDiscovery : ISdkDiscovery
{
    // From the committed SA 2026.1.0529.7 interop public API, SpatialAnalyzerSDKClass GuidAttribute.
    private const string ClassId = "{7C73DBF2-1921-48FE-99A6-2F8FE124E1DE}";
    public SdkReport Inspect()
    {
        if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform);
        var items = new List<SdkObservation>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine, RegistryHive.ClassesRoot })
            {
                var context = $"{hive} / {(view == RegistryView.Registry64 ? "64-bit" : "32-bit")}";
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    var prefix = hive == RegistryHive.ClassesRoot ? "" : @"Software\Classes\";
                    using var registration = root.OpenSubKey(prefix + @"CLSID\" + ClassId + @"\LocalServer32");
                    if (registration?.GetValue(null) is string command)
                    {
                        var path = ExecutablePath(command);
                        items.Add(new("Registered SDK candidate", context, FileVersion(path), path ?? "Unresolved command", path is not null && File.Exists(path) ? "Candidate file exists; activation not observed" : "Missing or ambiguous executable"));
                    }
                    if (hive == RegistryHive.ClassesRoot) continue;
                    using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall is null) continue;
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using var entry = uninstall.OpenSubKey(name);
                        if (entry?.GetValue("DisplayName") is not string display || !display.Contains("SpatialAnalyzer", StringComparison.OrdinalIgnoreCase)) continue;
                        var location = entry.GetValue("InstallLocation") as string ?? "Not recorded";
                        var version = entry.GetValue("DisplayVersion") as string ?? "Unknown";
                        items.Add(new("Installed SA product", context, version, location, "Installer registration; runtime not observed"));
                        if (Directory.Exists(location))
                        {
                            var sdk = Path.Combine(location, "SpatialAnalyzerSDK.exe");
                            if (File.Exists(sdk)) items.Add(new("Installed SDK file", context, FileVersion(sdk), sdk, "File version evidence only"));
                        }
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                { items.Add(new("Discovery incomplete", context, "Unknown", "Not disclosed", "Access denied or unreadable registration")); }
            }
        }
        return new(DateTimeOffset.UtcNow, items,
            "Registration and installed files do not prove which SDK or SA instance is active. Current Briosa runtime identity gates remain authoritative. " +
            "For stale, missing, or user-shadowed registration, coordinate an SA maintenance window and ask IT/Hexagon to use its supported installer repair procedure. " +
            "No vendor registration procedure is enabled in this app. It does not activate COM, connect to SA, execute MPs, or change registration.");
    }
    private static string? ExecutablePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? Environment.ExpandEnvironmentVariables(command[1..end]) : null;
        }
        // An unquoted command with spaces has ambiguous Windows executable resolution.
        return !command.Any(char.IsWhiteSpace) && command.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Environment.ExpandEnvironmentVariables(command) : null;
    }
    private static string FileVersion(string? path)
    {
        try { return path is not null && File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "Unknown" : "Unknown"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return "Unknown"; }
    }
}
