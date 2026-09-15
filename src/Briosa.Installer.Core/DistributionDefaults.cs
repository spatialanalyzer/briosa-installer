namespace Briosa.Installer.Core;

public static class DistributionDefaults
{
    // Release packaging supplies the real public catalog and publisher identity.
    // This only seeds the first-use editor; it never authorizes a network request.
    public static InstallerSettings? Load(string? directory = null)
    {
        var path = Path.Combine(directory ?? AppContext.BaseDirectory, "public-source.json");
        var result = new SettingsStore().Load(new(path));
        return result is Outcome<SettingsSnapshot>.Success success ? success.Value.Settings
            : throw new ManagementException(ManagementError.InvalidInput);
    }
}
