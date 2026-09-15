using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Briosa.Installer.Core;

public sealed class SourceTransport : IDisposable
{
    private readonly SourceSettings source;
    private readonly ICredentialStore credentials;
    private readonly HttpClient http;
    public SourceTransport(SourceSettings source, ICredentialStore? credentials = null, HttpMessageHandler? handler = null)
    {
        this.source = source;
        this.credentials = credentials ?? new WindowsCredentialStore();
        http = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            UseDefaultCredentials = source.Authentication == "windows",
            DefaultProxyCredentials = source.Authentication == "windows" ? CredentialCache.DefaultCredentials : null,
        }, disposeHandler: handler is null) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public string Location(string? relative = null) => relative is null ? source.Catalog :
        source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? new Uri(new Uri(source.Catalog), relative).AbsoluteUri
        : Path.Combine(Path.GetDirectoryName(source.Catalog)!, relative.Replace('/', Path.DirectorySeparatorChar));

    public async Task<byte[]> MetadataAsync(string? relative, int maximum, CancellationToken token)
    {
        using var output = new MemoryStream();
        await CopyAsync(relative, output, maximum, null, null, token).ConfigureAwait(false);
        return output.ToArray();
    }
    public async Task DownloadAsync(ArtifactReference artifact, string destination, IProgress<long>? progress, CancellationToken token)
    {
        if (!ReleaseCatalogCodec.IsSafeReference(artifact.Path) || artifact.Size is < 1 or > 2L * 1024 * 1024 * 1024)
            throw new ManagementException(ManagementError.LimitExceeded);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
        await CopyAsync(artifact.Path, output, artifact.Size, artifact.Size, progress, token).ConfigureAwait(false);
        output.Position = 0;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false)).ToLowerInvariant();
        if (digest != artifact.Sha256) throw new ManagementException(ManagementError.IntegrityFailure);
        output.Flush(true);
    }
    private async Task CopyAsync(string? relative, Stream output, long maximum, long? expected, IProgress<long>? progress, CancellationToken token)
    {
        if (relative is not null && !ReleaseCatalogCodec.IsSafeReference(relative)) throw new ManagementException(ManagementError.InvalidInput);
        var location = Location(relative);
        HttpResponseMessage? response = null;
        try
        {
            Stream input;
            if (source.Catalog.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, location);
                if (source.Authentication is "bearer" or "basic")
                {
                    var credential = credentials.Read(source.Catalog) ?? throw new ManagementException(ManagementError.AuthenticationRequired);
                    request.Headers.Authorization = source.Authentication == "bearer"
                        ? new AuthenticationHeaderValue("Bearer", credential.Secret)
                        : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Secret)));
                }
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and <= 399) throw new ManagementException(ManagementError.RedirectRejected);
                if (!response.IsSuccessStatusCode) throw new ManagementException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => ManagementError.AuthenticationRequired,
                    HttpStatusCode.Forbidden => ManagementError.AccessDenied,
                    HttpStatusCode.NotFound => ManagementError.PackageNotFound,
                    _ => ManagementError.SourceUnavailable,
                });
                if (response.Content.Headers.ContentEncoding.Count != 0) throw new ManagementException(ManagementError.SourceUnavailable);
                if (response.Content.Headers.ContentLength > maximum) throw new ManagementException(ManagementError.LimitExceeded);
                input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            }
            else
            {
                SafeFiles.NoLinks(location);
                input = new FileStream(location, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            }
            await using (input)
            {
                var buffer = new byte[81920];
                long total = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > maximum) throw new ManagementException(ManagementError.LimitExceeded);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    progress?.Report(total);
                }
                if (expected is not null && total != expected) throw new ManagementException(ManagementError.IntegrityFailure);
            }
        }
        finally { response?.Dispose(); }
    }
    public void Dispose() => http.Dispose();
}
