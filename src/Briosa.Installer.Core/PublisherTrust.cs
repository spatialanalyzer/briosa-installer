using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Briosa.Installer.Core;

public sealed record CatalogSignature(int SchemaVersion, string Algorithm, string CatalogSha256,
    long IssuedAt, long ExpiresAt, string Signature);
public sealed record PublisherProof(string Fingerprint, long IssuedAt, long ExpiresAt);

public static class PublisherTrust
{
    public const string Algorithm = "RSA-PSS-SHA256";
    public static bool IsPublicKey(string pem)
    {
        try { using var key = Open(pem); return true; }
        catch (Exception e) when (e is CryptographicException or ArgumentException or ManagementException) { return false; }
    }
    private static RSA Open(string pem)
    {
        if (pem.Length > 4096 || !pem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
            !pem.TrimEnd().EndsWith("-----END PUBLIC KEY-----", StringComparison.Ordinal))
            throw new ManagementException(ManagementError.UntrustedPublisher);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(pem);
            if (rsa.KeySize is < 3072 or > 8192) throw new ManagementException(ManagementError.UntrustedPublisher);
            return rsa;
        }
        catch { rsa.Dispose(); throw; }
    }
    public static string Fingerprint(string pem)
    {
        using var rsa = Open(pem);
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
    }
    public static byte[] SigningBytes(string hash, long issued, long expires) =>
        Encoding.UTF8.GetBytes(FormattableString.Invariant($"Briosa release catalog signature v1\n{hash}\n{issued}\n{expires}\n"));

    public static PublisherProof Verify(string pem, string catalogHash, byte[] signatureBytes, DateTimeOffset now)
    {
        try
        {
            if (signatureBytes.Length > 16384) throw new ManagementException(ManagementError.InvalidSignature);
            using var json = JsonDocument.Parse(signatureBytes);
            var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) throw new ManagementException(ManagementError.InvalidSignature);
            var envelope = JsonSerializer.Deserialize<CatalogSignature>(signatureBytes, InstallerJson.Options)!;
            if (envelope.SchemaVersion != 1 || envelope.Algorithm != Algorithm || envelope.CatalogSha256 != catalogHash)
                throw new ManagementException(ManagementError.InvalidSignature);
            using var rsa = Open(pem);
            if (!rsa.VerifyData(SigningBytes(catalogHash, envelope.IssuedAt, envelope.ExpiresAt),
                Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
                throw new ManagementException(ManagementError.InvalidSignature);
            var time = now.ToUnixTimeSeconds();
            if (envelope.IssuedAt < 0 || envelope.IssuedAt > time + 300 || envelope.ExpiresAt <= time ||
                envelope.ExpiresAt <= envelope.IssuedAt || envelope.ExpiresAt - envelope.IssuedAt > 90L * 86400)
                throw new ManagementException(ManagementError.ExpiredCatalog);
            return new(Fingerprint(pem), envelope.IssuedAt, envelope.ExpiresAt);
        }
        catch (Exception e) when (e is JsonException or CryptographicException or FormatException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw new ManagementException(ManagementError.InvalidSignature); }
    }
}
