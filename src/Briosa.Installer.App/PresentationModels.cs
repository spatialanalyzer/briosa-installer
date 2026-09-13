using Briosa.Installer.Core;

namespace Briosa.Installer.App;

public sealed record ServerRow(CatalogPackage Package, InstalledPackage? Installed, bool Available, bool Latest)
{
    public string Id => Package.Id;
    public string Version => Package.Version;
    public string Target => Package.TargetDisplay;
    public string Status => Installed is not null ? (Latest ? "Installed · latest in source" : "Installed") :
        Latest ? "Available · latest in source" : "Available";
    public string SizeDisplay => Available ? Package.SizeDisplay : "—";
    public string AccessibleName => $"Briosa server {Version}, SpatialAnalyzer {Target}, {Status}";
}
public sealed record DownloadedInstaller(InstalledPackage Package)
{
    public string Version => Package.Version;
    public string AccessibleName => $"Downloaded Briosa Installer {Version}";
}
public sealed record SdkEvidence(SdkObservation Observation)
{
    public string Kind => Observation.Kind;
    public string Version => Observation.Version;
    public string AccessibleName => $"{Kind}, version {Version}, {Observation.Status}";
}
public sealed record ActivityView(ActivityEntry Entry)
{
    public string Title => ActionTitle(Entry.Operation);
    public string Outcome => Entry.Outcome switch { "Succeeded" => "Completed", "Cancelled" => "Cancelled", _ => "Needs attention" };
    public string Time => Entry.Time.ToLocalTime().ToString("g");
    public string Context => Entry.Component is null ? "" :
        $"Briosa {(Entry.Component == "server" ? "server" : "Installer")} {Entry.Version}" +
        (Entry.Target is null ? "" : $" · SA {Entry.Target}");
    public string AccessibleName => string.Join(", ", new[] { Title, Context, Outcome, Time }.Where(value => value.Length > 0));
    public static string ActionTitle(string code) => code switch
    {
        "Settings.Save" => "Saved source settings",
        "Settings.Import" => "Imported source settings",
        "Settings.Load" => "Loaded settings",
        "Settings.Test" => "Tested server source",
        "Settings.TestUpdater" => "Tested installer update source",
        "Credentials.Save" => "Saved source credential",
        "Credentials.Remove" => "Removed source credential",
        "Catalog.Read" => "Checked server source",
        "Installer.Check" => "Checked for installer updates",
        "Installer.Update" => "Prepared installer update",
        "Installer.Activate" => "Selected installer version",
        "Package.Install" => "Installed server",
        "Package.Verify" => "Verified package files",
        "Package.Repair" => "Repaired package",
        "Package.Remove" => "Removed package",
        "Package.Recover" => "Recovered interrupted operations",
        "Package.List" => "Refreshed local inventory",
        "Sdk.Inspect" => "Inspected SDK setup",
        "Support.Export" => "Exported support report",
        _ => "Installer operation",
    };
}
