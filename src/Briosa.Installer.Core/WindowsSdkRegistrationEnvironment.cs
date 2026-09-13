using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace Briosa.Installer.Core;

[SupportedOSPlatform("windows")]
internal sealed class WindowsSdkRegistrationEnvironment : ISdkRegistrationEnvironment
{
    // Installed in all three inspected SA releases; provenance is recorded in docs/sdk-registration.md.
    private const string ProcedureHash = "EAA2DE87A9DCC7C5A2D4AA917DFE9F7F77DB6DCBB67F550690E0050EB6393390";
    private readonly WindowsSdkDiscovery discovery = new();
    public SdkReport Inspect() => discovery.Inspect();
    public SdkRegistrationTarget ValidateTarget(string directory, SdkReport report)
    {
        try
        {
            if (!Path.IsPathFullyQualified(directory) || new DriveInfo(Path.GetPathRoot(directory)!).DriveType != DriveType.Fixed)
                throw new SdkRegistrationException(SdkRegistrationError.UntrustedInstallation);
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            var product = report.Observations.FirstOrDefault(o => o.Kind == "Installed SA product" &&
                o.Context.StartsWith("LocalMachine", StringComparison.Ordinal) &&
                string.Equals(o.Location, directory, StringComparison.OrdinalIgnoreCase));
            var executable = Path.Combine(directory, "SpatialAnalyzerSDK.exe");
            var sdk = report.Observations.FirstOrDefault(o => o.Kind == "Installed SDK file" &&
                string.Equals(o.Location, executable, StringComparison.OrdinalIgnoreCase));
            if (product is null || sdk is null || sdk.Version == "Unknown" || sdk.Version != product.Version || !File.Exists(executable))
                throw new SdkRegistrationException(SdkRegistrationError.UnknownInstallation);
            ValidateInstallation(directory);
            var procedure = Path.Combine(directory, "SpatialAnalyzerSDK-register-server.bat");
            if (!File.Exists(procedure) || Hash(procedure) != ProcedureHash)
                throw new SdkRegistrationException(SdkRegistrationError.UnsupportedProcedure);
            using var held = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!SdkAuthenticode.IsApproved(executable))
                throw new SdkRegistrationException(SdkRegistrationError.UntrustedPublisher);
            return new(directory, sdk.Version, executable, Convert.ToHexString(SHA256.HashData(held)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException or ManagementException)
        { throw new SdkRegistrationException(SdkRegistrationError.AccessDenied); }
    }
    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void ValidateInstallation(string directory)
    {
        SafeFiles.NoLinks(directory);
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            ValidateAcl(parent.GetAccessControl(), parent.FullName == directory);
        // Check code-bearing files and their directories too, including DLL search locations.
        var pending = new Stack<string>(); pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new SdkRegistrationException(SdkRegistrationError.UntrustedInstallation);
                if ((attributes & FileAttributes.Directory) != 0)
                { ValidateAcl(new DirectoryInfo(entry).GetAccessControl(), true); pending.Push(entry); }
                else if (Path.GetExtension(entry).ToLowerInvariant() is ".exe" or ".dll" or ".ocx" or ".manifest" or ".bat")
                    ValidateAcl(new FileInfo(entry).GetAccessControl(), true);
            }
        }
    }
    private static void ValidateAcl(FileSystemSecurity security, bool includeWrite, bool userOwned = false)
    {
        var trusted = new HashSet<string>(StringComparer.Ordinal)
        {
            "S-1-5-18", "S-1-5-32-544",
            "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", // TrustedInstaller
        };
        if (userOwned) trusted.Add(WindowsIdentity.GetCurrent().User!.Value);
        if (security.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier owner || !trusted.Contains(owner.Value))
            throw new SdkRegistrationException(SdkRegistrationError.UntrustedInstallation);
        var rights = FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (includeWrite) rights |= FileSystemRights.Write;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
            var genericWrite = ((uint)rule.FileSystemRights & 0x50000000) != 0;
            if (((rule.FileSystemRights & rights) != 0 || genericWrite) && !trusted.Contains(rule.IdentityReference.Value))
                throw new SdkRegistrationException(SdkRegistrationError.UntrustedInstallation);
        }
    }
    public void RequireIdle()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (name.StartsWith("SpatialAnalyzer", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Spatial Analyzer", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Briosa.Server", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("Briosa.Worker", StringComparison.OrdinalIgnoreCase))
                        throw new SdkRegistrationException(SdkRegistrationError.RunningApplications);
                }
                catch (InvalidOperationException) { /* Process exited during enumeration. */ }
                catch (Win32Exception) { throw new SdkRegistrationException(SdkRegistrationError.AccessDenied); }
            }
        }
    }
    public IDisposable AcquireMaintenance()
    {
        try
        {
            var semaphore = new Semaphore(1, 1, @"Global\Briosa.SdkRegistrationMaintenance");
            if (!semaphore.WaitOne(0)) { semaphore.Dispose(); throw new SdkRegistrationException(SdkRegistrationError.MaintenanceBusy); }
            return new HeldMaintenance(semaphore);
        }
        catch (UnauthorizedAccessException) { throw new SdkRegistrationException(SdkRegistrationError.MaintenanceBusy); }
    }
    private sealed class HeldMaintenance(Semaphore semaphore) : IDisposable
    { public void Dispose() { semaphore.Release(); semaphore.Dispose(); } }
    public string SaveCheckpoint(SdkRegistrationPlan plan, SdkReport before)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Briosa", "Installer", "sdk-registration");
        SafeFiles.NoLinks(directory);
        if (!Directory.Exists(directory))
        {
            var acl = new DirectorySecurity();
            var user = WindowsIdentity.GetCurrent().User!;
            acl.SetOwner(user); acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { user, new SecurityIdentifier("S-1-5-18"), new SecurityIdentifier("S-1-5-32-544") })
                acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.CreateDirectory(directory);
        }
        ValidateAcl(new DirectoryInfo(directory).GetAccessControl(), true, userOwned: true);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddTHHmmss}-{Guid.NewGuid():N}.json");
        InstallerJson.Write(path, new { schemaVersion = 1, plan, before, status = "Prepared",
            recovery = "Use the prior installed SDK's reviewed registration procedure after inspecting current state. This record is evidence, not a registry rollback script." });
        return path;
    }
    public void CompleteCheckpoint(string path, SdkRegistrationResult result) => InstallerJson.Write(path + ".result.json", result);
    public VendorRegistrationResult Register(SdkRegistrationTarget target)
    {
        try
        {
            // Never execute the batch file, accept arbitrary arguments, or synthesize registry values.
            using var held = new FileStream(target.Executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (Convert.ToHexString(SHA256.HashData(held)) != target.Sha256)
                throw new SdkRegistrationException(SdkRegistrationError.ConcurrentChange);
            RequireIdle();
            using var process = Process.Start(new ProcessStartInfo(target.Executable)
            {
                Arguments = "/Regserver", WorkingDirectory = target.Directory,
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (process is null || !process.WaitForExit(60_000)) return VendorRegistrationResult.Unknown;
            return process.ExitCode == 0 ? VendorRegistrationResult.Completed : VendorRegistrationResult.Failed;
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return VendorRegistrationResult.Cancelled; }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return VendorRegistrationResult.Unknown; }
    }
}
