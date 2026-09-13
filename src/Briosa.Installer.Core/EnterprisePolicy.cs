namespace Briosa.Installer.Core;

public sealed record EnterprisePolicy(int SchemaVersion, string[] AllowedCatalogPrefixes, string[] PublisherFingerprints,
    bool AllowUserStore = true, bool AllowMachineStore = true)
{
    public static string PolicyPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Briosa", "Installer", "policy.json");
    public static EnterprisePolicy? Load()
    {
        try
        {
            SafeFiles.NoLinks(PolicyPath);
            var policy = InstallerJson.Read<EnterprisePolicy>(PolicyPath);
            if (OperatingSystem.IsWindows())
            {
                WindowsStoreProtection.Validate(new FileInfo(PolicyPath).GetAccessControl());
                WindowsStoreProtection.Validate(new DirectoryInfo(Path.GetDirectoryName(PolicyPath)!).GetAccessControl());
                WindowsStoreProtection.Validate(new DirectoryInfo(Path.GetDirectoryName(Path.GetDirectoryName(PolicyPath))!).GetAccessControl());
            }
            if (policy.SchemaVersion != 1 || policy.AllowedCatalogPrefixes is null || policy.PublisherFingerprints is null ||
                policy.AllowedCatalogPrefixes.Any(string.IsNullOrWhiteSpace) || policy.PublisherFingerprints.Any(p => p.Length != 64 || !p.All(Uri.IsHexDigit)))
                throw new ManagementException(ManagementError.PolicyDenied);
            return policy;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch { throw new ManagementException(ManagementError.PolicyDenied); }
    }
    public void Validate(SourceSettings source, string? storeRoot = null)
    {
        var allowed = AllowedCatalogPrefixes.Any(prefix =>
        {
            if (prefix.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!Uri.TryCreate(prefix, UriKind.Absolute, out var root) || !Uri.TryCreate(source.Catalog, UriKind.Absolute, out var candidate)) return false;
                if (candidate.AbsolutePath.Contains('%') || root.AbsolutePath.Contains('%')) return false;
                return root.Scheme == candidate.Scheme && root.Authority.Equals(candidate.Authority, StringComparison.OrdinalIgnoreCase) &&
                    candidate.AbsolutePath.StartsWith(root.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal);
            }
            return !source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFullPath(source.Catalog).StartsWith(Path.GetFullPath(prefix).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        });
        if (!allowed || (PublisherFingerprints.Length > 0 && (source.PublisherKey is null ||
            !PublisherFingerprints.Contains(PublisherTrust.Fingerprint(source.PublisherKey), StringComparer.OrdinalIgnoreCase))))
            throw new ManagementException(ManagementError.PolicyDenied);
        if (storeRoot is not null) ValidateScope(storeRoot);
    }
    public void ValidateScope(string storeRoot)
    {
        var machine = Path.GetFullPath(storeRoot).TrimEnd('\\', '/').Equals(PackageStore.MachineRoot, StringComparison.OrdinalIgnoreCase);
        if (machine ? !AllowMachineStore : !AllowUserStore) throw new ManagementException(ManagementError.PolicyDenied);
    }
    public void ValidateInstalled(string publisherFingerprint, string storeRoot)
    {
        ValidateScope(storeRoot);
        if (PublisherFingerprints.Length > 0 && !PublisherFingerprints.Contains(publisherFingerprint, StringComparer.OrdinalIgnoreCase))
            throw new ManagementException(ManagementError.PolicyDenied);
    }
}
