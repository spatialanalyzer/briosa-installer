using System.IO;
using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public sealed record SaInstallationRow(SdkObservation Installation, IReadOnlyList<SdkObservation> Registrations)
{
    public string Version => Installation.Version;
    public string Location => Installation.Location;
    public bool IsRegistered => Registrations.Count > 0;
    public string RegistrationHint => IsRegistered
        ? "This installation's SDK path is registered in: " + string.Join("; ", Registrations.Select(r => r.Context))
        : "This installation's SDK path is not referenced by the inspected registration.";
    public string AccessibleName => $"SpatialAnalyzer {Version}, {(IsRegistered ? "SDK registered" : "SDK not registered")}, {Location}";
}

public sealed record SdkSetupPresentation(string Title, string Summary, string Findings, IReadOnlyList<SaInstallationRow> Installations)
{
    public static SdkSetupPresentation FromReport(SdkReport report)
    {
        var registered = report.Observations.Where(o => o.Kind == SdkReport.ConfiguredRegistration).ToArray();
        var installations = report.Observations.Where(o => o.Kind == "Installed SA product")
            .Select(product => new SaInstallationRow(product, registered.Where(r => Matches(product, r)).ToArray()))
            .ToArray();
        var versions = registered.Select(o => o.Version).Distinct().ToArray();
        var incomplete = report.Observations.Any(o => o.Kind == "Discovery incomplete");
        var unresolved = registered.Any(o => o.Version == "Unknown" || o.State is SdkEvidenceState.Unresolved or SdkEvidenceState.ServiceRegistration);
        var missing = registered.Any(o => o.State == SdkEvidenceState.MissingFile);
        var title = registered.Length == 0 ? incomplete ? "Unable to determine" : "Not found" :
            unresolved || missing ? "Needs review" : versions.Length == 1 ? versions[0] : "Multiple versions";
        var marked = installations.Count(i => i.IsRegistered);
        var summary = installations.Length == 0 ? "No SpatialAnalyzer installations were identified." :
            $"{installations.Length} SpatialAnalyzer installation{(installations.Length == 1 ? "" : "s")} found.";
        if (marked == 1) summary += " The marked installation supplies the registered SDK.";
        else if (marked > 1) summary += " The marked installations are referenced by different registry views.";

        var findings = new List<string>();
        if (registered.Length == 0 && !incomplete) findings.Add("No SDK registration was found for the inspecting user.");
        if (unresolved) findings.Add("The configured SDK could not be fully identified. See registration details.");
        if (missing) findings.Add("The registered SDK executable is missing.");
        if (registered.Any(r => FullPath(r.Location) is not null && !installations.Any(i => i.Registrations.Contains(r))))
            findings.Add("A registered SDK path does not match any listed SA installation.");
        if (versions.Length > 1 && !unresolved) findings.Add("The registry views refer to different SDK versions.");
        if (registered.Any(o => o.State == SdkEvidenceState.UnquotedPath))
            findings.Add("The registered path is unquoted; Windows launch resolution may be ambiguous.");
        if (report.Observations.Any(o => o.Kind == "Other SDK registration"))
            findings.Add("A different underlying registration was also found. See registration details.");
        if (incomplete) findings.Add("Some installation or registry locations could not be inspected.");
        return new("Configured SDK: " + title, summary, string.Join(" ", findings), installations);
    }

    private static bool Matches(SdkObservation product, SdkObservation registration)
    {
        if (registration.State is SdkEvidenceState.Unresolved or SdkEvidenceState.ServiceRegistration) return false;
        var directory = FullPath(product.Location);
        var executable = FullPath(registration.Location);
        // Version equality alone cannot distinguish two copies of the same release.
        return directory is not null && executable is not null &&
            string.Equals(Path.Combine(directory, "SpatialAnalyzerSDK.exe"), executable, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FullPath(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && path.IndexOfAny(Path.GetInvalidPathChars()) < 0
                ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
