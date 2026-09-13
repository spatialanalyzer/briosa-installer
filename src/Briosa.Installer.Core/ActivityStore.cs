namespace Briosa.Installer.Core;

public sealed record ActivityEntry(DateTimeOffset Time, string Operation, string Outcome);
public sealed class ActivityStore(string directory)
{
    private string PathName => Path.Combine(directory, "activity.json");
    public IReadOnlyList<ActivityEntry> Read()
    {
        try { return File.Exists(PathName) ? InstallerJson.Read<List<ActivityEntry>>(PathName).Where(e => Code(e.Operation) && Code(e.Outcome)).Take(200).ToList() : []; }
        catch { return []; }
    }
    public void Record(string operation, string outcome)
    {
        // Callers supply fixed action/result codes, never raw exceptions, arguments, paths, or credentials.
        if (!Code(operation) || !Code(outcome)) return;
        try
        {
            Directory.CreateDirectory(directory);
            using var held = new FileStream(PathName + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var entries = Read().Prepend(new ActivityEntry(DateTimeOffset.UtcNow, operation, outcome)).Take(200).ToList();
            InstallerJson.Write(PathName, entries);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Logging cannot turn a committed operation into a failure. */ }
    }
    private static bool Code(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Za-z][A-Za-z0-9.]{0,63}\z");
    public void Export(string output, SdkReport? sdk = null) => InstallerJson.Write(output, new
    {
        schemaVersion = 1, appVersion = typeof(ActivityStore).Assembly.GetName().Version?.ToString(),
        exportedAt = DateTimeOffset.UtcNow, activity = Read(), sdk = sdk?.Sanitized(),
        privacy = "No source URLs, filesystem paths, user or host names, process identifiers, credentials, license details, or SA job data are included.",
    });
}
