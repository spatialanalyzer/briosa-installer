using System.Net;
using System.Security.Cryptography;

namespace Briosa.Installer.Core;

public sealed class ReleaseCatalogClient : IDisposable
{
    private readonly HttpClient http;
    private readonly TimeSpan timeout;

    public ReleaseCatalogClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        this.timeout = timeout ?? TimeSpan.FromSeconds(30);
        if (this.timeout <= TimeSpan.Zero || this.timeout > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(timeout));
        http = new HttpClient(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<CatalogResult<CatalogSnapshot>> ReadAsync(InstallerSettings settings, CatalogComponent component, CancellationToken cancellationToken = default)
    {
        if (SettingsCodec.Validate(settings) is Outcome<InstallerSettings>.Failure)
            return CatalogResult<CatalogSnapshot>.Fail(CatalogError.InvalidSource);
        var source = component == CatalogComponent.Server ? settings.ServerCatalog : settings.EffectiveInstallerCatalog;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? ReadHttpsAsync(new Uri(source), deadline.Token)
                : Task.Run(() => ReadFileAsync(source, deadline.Token), deadline.Token);
            // Bounds caller wait even if an OS file/share open cannot abort promptly.
            var bytesResult = await read.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (bytesResult is CatalogResult<byte[]>.Failure failure) return new CatalogResult<CatalogSnapshot>.Failure(failure.Error);
            var bytes = ((CatalogResult<byte[]>.Success)bytesResult).Value;
            var parsed = ReleaseCatalogCodec.Parse(bytes);
            if (parsed is CatalogResult<IReadOnlyList<CatalogPackage>>.Failure invalid) return new CatalogResult<CatalogSnapshot>.Failure(invalid.Error);
            var packages = ((CatalogResult<IReadOnlyList<CatalogPackage>>.Success)parsed).Value.Where(package => package.Component == component).ToList().AsReadOnly();
            deadline.Token.ThrowIfCancellationRequested();
            return new CatalogResult<CatalogSnapshot>.Success(new(source, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), component, packages));
        }
        catch (OperationCanceledException)
        {
            return CatalogResult<CatalogSnapshot>.Fail(cancellationToken.IsCancellationRequested ? CatalogError.Cancelled : CatalogError.TimedOut);
        }
        catch (UnauthorizedAccessException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.AccessDenied); }
        catch (FileNotFoundException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.NotFound); }
        catch (DirectoryNotFoundException) { return CatalogResult<CatalogSnapshot>.Fail(CatalogError.NotFound); }
        catch (Exception exception) when (exception is HttpRequestException or IOException or ArgumentException or NotSupportedException)
        {
            return CatalogResult<CatalogSnapshot>.Fail(CatalogError.SourceUnavailable);
        }
    }

    private async Task<CatalogResult<byte[]>> ReadHttpsAsync(Uri source, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if ((int)response.StatusCode is >= 300 and <= 399) return CatalogResult<byte[]>.Fail(CatalogError.RedirectRejected);
        if (!response.IsSuccessStatusCode)
            return CatalogResult<byte[]>.Fail(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => CatalogError.AuthenticationRequired,
                HttpStatusCode.Forbidden => CatalogError.AccessDenied,
                HttpStatusCode.NotFound => CatalogError.NotFound,
                _ => CatalogError.SourceUnavailable,
            });
        if (response.Content.Headers.ContentEncoding.Count > 0) return CatalogResult<byte[]>.Fail(CatalogError.UnsupportedEncoding);
        if (response.Content.Headers.ContentLength > ReleaseCatalogCodec.MaximumBytes) return CatalogResult<byte[]>.Fail(CatalogError.TooLarge);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
    }

    private static async Task<CatalogResult<byte[]>> ReadFileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 8192, useAsync: true);
        if (stream.Length > ReleaseCatalogCodec.MaximumBytes) return CatalogResult<byte[]>.Fail(CatalogError.TooLarge);
        return await ReadBoundedAsync(stream, token).ConfigureAwait(false);
    }

    private static async Task<CatalogResult<byte[]>> ReadBoundedAsync(Stream stream, CancellationToken token)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > ReleaseCatalogCodec.MaximumBytes) return CatalogResult<byte[]>.Fail(CatalogError.TooLarge);
            output.Write(buffer, 0, count);
        }
        return new CatalogResult<byte[]>.Success(output.ToArray());
    }

    public void Dispose() => http.Dispose();
}
