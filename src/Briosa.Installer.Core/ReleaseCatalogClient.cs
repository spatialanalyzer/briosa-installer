using System.Security.Cryptography;

namespace Briosa.Installer.Core;

public sealed class ReleaseCatalogClient : IDisposable
{
    private readonly HttpMessageHandler? handler;
    private readonly ICredentialStore? credentials;
    private readonly TimeSpan timeout;
    public ReleaseCatalogClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null, ICredentialStore? credentials = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        this.handler = handler;
        this.credentials = credentials;
    }
    public async Task<CatalogResult<CatalogSnapshot>> ReadAsync(InstallerSettings settings, CatalogComponent component, CancellationToken cancellationToken = default)
    {
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure)
            return CatalogResult<CatalogSnapshot>.Fail(CatalogError.InvalidSource);
        var source = settings.Source(component);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnterprisePolicy.Load()?.Validate(source);
            using var transport = new SourceTransport(source, credentials, handler);
            var read = Task.Run(() => transport.MetadataAsync(null, ReleaseCatalogCodec.MaximumBytes, deadline.Token), deadline.Token);
            var bytes = await read.WaitAsync(deadline.Token).ConfigureAwait(false);
            var parsed = ReleaseCatalogCodec.Parse(bytes);
            if (parsed is CatalogResult<IReadOnlyList<CatalogPackage>>.Failure invalid) return new CatalogResult<CatalogSnapshot>.Failure(invalid.Error);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            PublisherProof? proof = null;
            if (source.PublisherKey is not null)
            {
                var name = source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(source.Catalog).Segments.Last() : Path.GetFileName(source.Catalog);
                var signature = await Task.Run(() => transport.MetadataAsync(name + ".signature.json", 16384, deadline.Token), deadline.Token)
                    .WaitAsync(deadline.Token).ConfigureAwait(false);
                proof = PublisherTrust.Verify(source.PublisherKey, hash, signature, DateTimeOffset.UtcNow);
            }
            deadline.Token.ThrowIfCancellationRequested();
            var packages = ((CatalogResult<IReadOnlyList<CatalogPackage>>.Success)parsed).Value.Where(p => p.Component == component).ToList().AsReadOnly();
            return new CatalogResult<CatalogSnapshot>.Success(new(source.Catalog, hash, component, packages) { Publisher = proof });
        }
        catch (OperationCanceledException) { return CatalogResult<CatalogSnapshot>.Fail(cancellationToken.IsCancellationRequested ? CatalogError.Cancelled : CatalogError.TimedOut); }
        catch (ManagementException e)
        {
            return CatalogResult<CatalogSnapshot>.Fail(e.Code switch
            {
                ManagementError.RedirectRejected => CatalogError.RedirectRejected,
                ManagementError.AuthenticationRequired => CatalogError.AuthenticationRequired,
                ManagementError.AccessDenied => CatalogError.AccessDenied,
                ManagementError.PolicyDenied => CatalogError.PolicyDenied,
                ManagementError.LimitExceeded => CatalogError.TooLarge,
                ManagementError.PackageNotFound => CatalogError.NotFound,
                ManagementError.InvalidSignature or ManagementError.UntrustedPublisher => CatalogError.VerificationFailed,
                ManagementError.ExpiredCatalog => CatalogError.Expired,
                _ => CatalogError.SourceUnavailable,
            });
        }
        catch (UnauthorizedAccessException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.AccessDenied); }
        catch (FileNotFoundException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.NotFound); }
        catch (DirectoryNotFoundException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.NotFound); }
        catch (Exception e) when (e is HttpRequestException or IOException or ArgumentException or NotSupportedException)
        { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.SourceUnavailable); }
    }
    public void Dispose() => handler?.Dispose();
}
