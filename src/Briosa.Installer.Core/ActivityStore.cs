namespace Briosa.Installer.Core;

public sealed record ActivityEntry(DateTimeOffset Time, string Operation, string Outcome,
    string? Component = null, string? Version = null, string? Target = null, long? DurationMs = null);
public sealed class ActivityStore(string directory)
{
    private string PathName => Path.Combine(directory, "activity.json");
    public bool ReadFailed { get; private set; }
    public IReadOnlyList<ActivityEntry> Read()
    {
        ReadFailed = false;
        try { return File.Exists(PathName) ? InstallerJson.Read<List<ActivityEntry>>(PathName).Where(e => Code(e.Operation) && Code(e.Outcome)).Select(Sanitize).Take(200).ToList() : []; }
        catch { ReadFailed = true; return []; }
    }
    public bool Record(string operation, string outcome, CatalogPackage? package = null, long? durationMs = null)
    {
        // Callers supply fixed action/result codes, never raw exceptions, arguments, paths, or credentials.
        if (!Code(operation) || !Code(outcome)) return false;
        try
        {
            Directory.CreateDirectory(directory);
            using var held = new FileStream(PathName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var entries = Read().Prepend(Sanitize(new ActivityEntry(DateTimeOffset.UtcNow, operation, outcome,
                package?.ComponentName, package?.Version, package?.SpatialAnalyzerTarget, durationMs))).Take(200).ToList();
            if (ReadFailed) return false;
            InstallerJson.Write(PathName, entries);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    private static ActivityEntry Sanitize(ActivityEntry entry) => entry with
    {
        Component = entry.Component is "server" or "installer" ? entry.Component : null,
        Version = entry.Version is { Length: <= 100 } && ReleaseVersion.IsValid(entry.Version) ? entry.Version : null,
        Target = entry.Target is { Length: <= 64 } && System.Text.RegularExpressions.Regex.IsMatch(entry.Target, @"\A[0-9]+(\.[0-9]+){1,3}\z") ? entry.Target : null,
        DurationMs = entry.DurationMs is >= 0 and <= 86400000 ? entry.DurationMs : null,
    };
    private static bool Code(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z][A-Za-z0-9.]{0,63}\z");
    public void Export(string output, SdkReport? sdk = null) => InstallerJson.Write(output, new
    {
        schemaVersion = 1,
        appVersion = typeof(ActivityStore).Assembly.GetName().Version?.ToString(),
        exportedAt = DateTimeOffset.UtcNow,
        activity = Read(),
        sdk = sdk?.Sanitized(),
        privacy = "No source URLs, filesystem paths, user or host names, process identifiers, credentials, license details, or SA job data are included.",
    });
}
