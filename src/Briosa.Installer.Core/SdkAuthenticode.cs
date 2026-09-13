using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Briosa.Installer.Core;

[SupportedOSPlatform("windows")]
internal static class SdkAuthenticode
{
    // Reviewed Hexagon signing certificates from installed SA 2024.1 and 2026.1.
    // A new publisher certificate requires a reviewed allowlist update.
    private static readonly HashSet<string> Publishers = new(StringComparer.Ordinal)
    {
        "1E1339CA0371DA0A057DAD6C17296E9A232D6E27F1E67C4DCDF690B97E52014B",
        "84E87961C48124EE3439E6371789937E397214F0093416DA0E986526220DB1EC",
    };
    internal static bool IsApproved(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, UnionChoice = 1, File = pointer,
            StateAction = 1, ProviderFlags = 0x1000 | 0x80, // Cache-only URL retrieval; chain revocation excluding root.
        };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return false;
            var provider = WTHelperProvDataFromStateData(data.StateData);
            if (provider == IntPtr.Zero) return false;
            var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == IntPtr.Zero) return false;
            var cert = WTHelperGetProvCertFromChain(signer, 0);
            if (cert == IntPtr.Zero) return false;
            var certificatePointer = Marshal.PtrToStructure<ProviderCertificate>(cert).Certificate;
            if (certificatePointer == IntPtr.Zero) return false;
            var context = Marshal.PtrToStructure<CertificateContext>(certificatePointer);
            if (context.Size is 0 or > 65536 || context.Encoded == IntPtr.Zero) return false;
            var encoded = new byte[context.Size];
            Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(encoded);
            return Publishers.Contains(certificate.GetCertHashString(HashAlgorithmName.SHA256));
        }
        catch (CryptographicException) { return false; }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr FileHandle, KnownSubject;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback, SipClient;
        public uint UiChoice, RevocationChecks, UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData, UrlReference;
        public uint ProviderFlags, UiContext;
        public IntPtr SignatureSettings;
    }
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate { public uint Size; public IntPtr Certificate; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext { public uint Encoding; public IntPtr Encoded; public uint Size; public IntPtr Info, Store; }
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);
}
