using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Briosa.Installer.Core;

// All-users stores must not let a less-privileged account replace trusted artifacts.
[SupportedOSPlatform("windows")]
public static class WindowsStoreProtection
{
    private static SecurityIdentifier Administrators => new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static SecurityIdentifier SystemAccount => new(WellKnownSidType.LocalSystemSid, null);
    private static SecurityIdentifier Readers => new(WellKnownSidType.AuthenticatedUserSid, null);
    private const FileSystemRights MutatingRights = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
    public static void Validate(FileSystemSecurity security)
    {
        var owner = security.GetOwner(typeof(SecurityIdentifier));
        if (!Administrators.Equals(owner) && !SystemAccount.Equals(owner)) throw new ManagementException(ManagementError.AccessDenied);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & MutatingRights) != 0 &&
                !Administrators.Equals(rule.IdentityReference) && !SystemAccount.Equals(rule.IdentityReference))
                throw new ManagementException(ManagementError.AccessDenied);
    }
    public static void EnsureDirectory(string directory)
    {
        SafeFiles.NoLinks(directory);
        if (!Directory.Exists(directory))
        {
            var acl = DirectoryAcl();
            acl.CreateDirectory(directory);
        }
        Validate(new DirectoryInfo(directory).GetAccessControl());
    }
    private static DirectorySecurity DirectoryAcl()
    {
        var acl = new DirectorySecurity(); acl.SetOwner(Administrators); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { Administrators, SystemAccount })
            acl.AddAccessRule(new(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        acl.AddAccessRule(new(Readers, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }
    public static void SealFile(string file)
    {
        SafeFiles.NoLinks(file);
        var acl = new FileSecurity(); acl.SetOwner(Administrators); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { Administrators, SystemAccount }) acl.AddAccessRule(new(sid, FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new(Readers, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        new FileInfo(file).SetAccessControl(acl);
    }
    public static void SealTree(string root)
    {
        foreach (var file in SafeFiles.Files(root)) SealFile(file);
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
            new DirectoryInfo(directory).SetAccessControl(DirectoryAcl());
    }
    public static void ValidateTree(string root)
    {
        Validate(new DirectoryInfo(root).GetAccessControl());
        foreach (var file in SafeFiles.Files(root)) Validate(new FileInfo(file).GetAccessControl());
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)) Validate(new DirectoryInfo(directory).GetAccessControl());
    }
}
