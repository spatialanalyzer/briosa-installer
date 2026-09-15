using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Briosa.Installer.Core;

internal sealed record SdkComRegistration(RegistryHive Hive, RegistryView View, string? Command,
    string? ServerExecutable = null, string? LocalService = null);
internal sealed record SdkProductRegistration(string Context, string? Version, string? InstallLocation, string? DisplayIcon);
internal sealed record SdkExecutableEvidence(string? Path, SdkEvidenceState State);

// Analyze snapshots without a licensed installation or registry mutation.
internal static class SdkDiscoveryAnalysis
{
    internal static SdkReport Analyze(IReadOnlyList<SdkComRegistration> registrations,
        IReadOnlyList<SdkProductRegistration> products, IReadOnlyList<SdkObservation> issues,
        Func<string, bool> fileExists, Func<string, string> fileVersion)
    {
        var items = new List<SdkObservation>();
        foreach (var product in products)
        {
            var declared = AbsolutePath(product.InstallLocation?.Trim().Trim('"'));
            var icon = IconPath(product.DisplayIcon);
            // DisplayIcon is evidence only: require an existing file, then probe
            // its sibling SDK. Never execute an icon, uninstaller, or registry command.
            var iconDirectory = icon is not null && fileExists(icon) ? Path.GetDirectoryName(icon) : null;
            var locations = new[] { declared, iconDirectory }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var sdks = locations.Select(p => Path.Combine(p, "SpatialAnalyzerSDK.exe")).Where(fileExists).ToArray();
            var location = sdks.Length > 0 ? Path.GetDirectoryName(sdks[0])! : declared ?? iconDirectory ?? "Not recorded";
            items.Add(new("Installed SA product", product.Context, NormalizeVersion(product.Version), location,
                "Installer registration; runtime not observed"));
            foreach (var sdk in sdks)
                items.Add(new("Installed SDK file", product.Context, NormalizeVersion(fileVersion(sdk)), sdk,
                    "File version evidence only"));
        }

        foreach (var registration in registrations)
        {
            var merged = registration.Hive == RegistryHive.ClassesRoot;
            // HKCR is a merged view, not a third independent registration. Keep
            // differing underlying registrations as evidence, without calling them configured.
            if (!merged && registrations.Any(r => r.Hive == RegistryHive.ClassesRoot && SameRegistration(r, registration))) continue;
            var context = $"{registration.Hive} / {(registration.View == RegistryView.Registry64 ? "64-bit" : "32-bit")} registry view";
            if (merged)
            {
                var origins = registrations.Where(r => r.Hive != RegistryHive.ClassesRoot && SameRegistration(r, registration)).Select(r => r.Hive.ToString()).ToArray();
                if (origins.Length > 0) context += " (matches " + string.Join(", ", origins) + ")";
            }
            var executable = ResolveExecutable(registration, fileExists);
            var exists = executable.Path is not null && fileExists(executable.Path);
            var version = exists ? NormalizeVersion(fileVersion(executable.Path!)) : "Unknown";
            var state = executable.State == SdkEvidenceState.Observed && !exists ? SdkEvidenceState.MissingFile : executable.State;
            var status = state switch
            {
                SdkEvidenceState.UnquotedPath => exists ? "Unquoted path; file identified, activation not observed" : "Unquoted path; candidate file missing",
                SdkEvidenceState.Unresolved => "Executable path could not be resolved",
                SdkEvidenceState.MissingFile => "Configured executable is missing",
                SdkEvidenceState.ServiceRegistration => "Service registration; local executable is not authoritative",
                _ => "Configured file exists; activation not observed",
            };
            if (!merged) status = "Underlying registration; not confirmed by the inspected merged view. " + status;
            items.Add(new(merged ? SdkReport.ConfiguredRegistration : "Other SDK registration", context, version,
                executable.Path ?? "Unresolved executable", status, merged ? state : SdkEvidenceState.OtherRegistration));
        }

        // Consolidate duplicate file/product evidence, but retain each registry view.
        var observations = items.GroupBy(o => (o.Kind, Location: o.Location.ToUpperInvariant(), o.Version, o.Status, o.State,
                RegistrationContext: o.Kind is SdkReport.ConfiguredRegistration or "Other SDK registration" ? o.Context : ""))
            .Select(g => g.First() with { Context = string.Join("; ", g.Select(o => o.Context).Distinct()) })
            .Concat(issues).ToArray();
        return new(DateTimeOffset.UtcNow, observations,
            "Configured SDK registration is the merged Windows classes view for the inspecting user and each registry view. " +
            "It is not proof of the SDK used by another account, an elevated process, a service, or an already-running COM server. " +
            "Unquoted paths with spaces identify candidate files but can be ambiguous when Windows launches them. " +
            "Registration and installed files do not prove which SDK or SA instance is active. Briosa runtime identity gates remain authoritative. " +
            "For stale, missing, or shadowed registration, coordinate a maintenance window and the vendor-supported repair procedure with IT / Hexagon. " +
            "This inspection does not activate COM, connect to SA, execute MPs, or change registration.");
    }

    private static bool SameRegistration(SdkComRegistration left, SdkComRegistration right) => left.View == right.View &&
        string.Equals(left.Command, right.Command, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ServerExecutable, right.ServerExecutable, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.LocalService, right.LocalService, StringComparison.OrdinalIgnoreCase);

    internal static SdkExecutableEvidence ResolveExecutable(SdkComRegistration registration, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(registration.LocalService)) return new(null, SdkEvidenceState.ServiceRegistration);
        // ServerExecutable takes precedence as CreateProcess's application name.
        // An invalid explicit value cannot fall back to the command line.
        if (registration.ServerExecutable is not null)
            return Evidence(ExecutablePath(registration.ServerExecutable), false);
        var command = registration.Command?.Trim();
        if (string.IsNullOrEmpty(command)) return new(null, SdkEvidenceState.Unresolved);
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 && (end == command.Length - 1 || char.IsWhiteSpace(command[end + 1]))
                ? Evidence(ExecutablePath(command[1..end]), false) : new(null, SdkEvidenceState.Unresolved);
        }
        var candidates = Regex.Matches(command, @"\.exe(?=\s|$)", RegexOptions.IgnoreCase)
            .Select(m => ExecutablePath(command[..(m.Index + m.Length)]))
            .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var existing = candidates.Where(fileExists).ToArray();
        var path = existing.Length == 1 ? existing[0] : existing.Length == 0 && candidates.Length == 1 ? candidates[0] : null;
        return Evidence(path, path?.Any(char.IsWhiteSpace) == true);

        static SdkExecutableEvidence Evidence(string? path, bool unquoted) => new(path,
            path is null ? SdkEvidenceState.Unresolved : unquoted ? SdkEvidenceState.UnquotedPath : SdkEvidenceState.Observed);
    }

    internal static string NormalizeVersion(string? version)
    {
        if (version is null || version.Length > 100) return "Unknown";
        var parts = Regex.Split(version.Trim(), @"\s*[.,]\s*");
        return parts.Length is >= 2 and <= 4 && parts.All(p => Regex.IsMatch(p, @"\A[0-9]+\z"))
            ? string.Join('.', parts) : "Unknown";
    }

    private static string? IconPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return AbsolutePath(Regex.Replace(value.Trim(), @",\s*-?[0-9]+\s*$", "").Trim().Trim('"'));
    }
    private static string? ExecutablePath(string? value)
    {
        var path = AbsolutePath(value);
        return path?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ? path : null;
    }
    private static string? AbsolutePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            return expanded.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(expanded)
                ? Path.GetFullPath(expanded) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
