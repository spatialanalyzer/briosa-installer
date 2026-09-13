using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Briosa.Installer.Core;

public sealed record SourceCredential(string UserName, string Secret);
public interface ICredentialStore
{
    SourceCredential? Read(string catalog);
    void Save(string catalog, SourceCredential credential);
    void Delete(string catalog);
}

// Each exact catalog owns its credential. Payload requests reuse only that source's credential.
public sealed class WindowsCredentialStore : ICredentialStore
{
    private static string Target(string catalog) => "Briosa.Installer/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog)));
    private static void RequireWindows()
    { if (!OperatingSystem.IsWindows()) throw new ManagementException(ManagementError.UnsupportedPlatform); }

    public SourceCredential? Read(string catalog)
    {
        RequireWindows();
        if (!CredRead(Target(catalog), 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new ManagementException(ManagementError.AccessDenied);
        }
        try
        {
            var item = Marshal.PtrToStructure<Credential>(pointer);
            if (item.BlobSize > 5120) throw new ManagementException(ManagementError.InvalidInput);
            return new(item.UserName ?? "", Marshal.PtrToStringUni(item.Blob, checked((int)item.BlobSize / 2)) ?? "");
        }
        finally { CredFree(pointer); }
    }
    public void Save(string catalog, SourceCredential credential)
    {
        RequireWindows();
        if (string.IsNullOrEmpty(credential.Secret) || credential.Secret.Length > 2560 ||
            credential.Secret.Any(char.IsControl) || credential.UserName.Any(char.IsControl) || credential.UserName.Contains(':'))
            throw new ManagementException(ManagementError.InvalidInput);
        var pointer = Marshal.StringToCoTaskMemUni(credential.Secret);
        try
        {
            var item = new Credential { Type = 1, TargetName = Target(catalog), BlobSize = checked((uint)credential.Secret.Length * 2),
                Blob = pointer, Persist = 2, UserName = credential.UserName };
            if (!CredWrite(ref item, 0)) throw new ManagementException(ManagementError.AccessDenied);
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }
    public void Delete(string catalog)
    {
        RequireWindows();
        if (!CredDelete(Target(catalog), 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new ManagementException(ManagementError.AccessDenied);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr credential);
}
