using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Briosa.Installer.Core;

public enum CatalogComponent { Server, Installer }
public enum CatalogError
{
    InvalidSource, InvalidDocument, UnsupportedSchema, InvalidPackage, DuplicatePackage,
    UnsafeReference, ConflictingReference, TooLarge, SourceUnavailable, AuthenticationRequired,
    AccessDenied, NotFound, RedirectRejected, UnsupportedEncoding, TimedOut, Cancelled, PackageNotFound,
}

public sealed record CatalogFailure(CatalogError Code)
{
    public string Message => Code switch
    {
        CatalogError.InvalidSource => "Choose a valid catalog location in Sources.",
        CatalogError.InvalidDocument => "The source did not return a valid Briosa release catalog.",
        CatalogError.UnsupportedSchema => "This release catalog schema version is not supported.",
        CatalogError.InvalidPackage => "A catalog package has invalid or incomplete metadata.",
        CatalogError.DuplicatePackage => "The catalog contains duplicate package identities.",
        CatalogError.UnsafeReference => "A catalog reference leaves the supported mirror layout or uses an unsafe path.",
        CatalogError.ConflictingReference => "The catalog declares different sizes or hashes for the same artifact path.",
        CatalogError.TooLarge => "The catalog exceeds the 1 MiB or 1,000-package limit.",
        CatalogError.AuthenticationRequired => "The source requires authentication. Authenticated feeds are not supported in this preview.",
        CatalogError.AccessDenied => "Access to the selected catalog was denied.",
        CatalogError.NotFound => "The selected catalog was not found.",
        CatalogError.RedirectRejected => "The source redirected the catalog request. Configure the final permitted catalog URL.",
        CatalogError.UnsupportedEncoding => "The catalog response uses an unsupported content encoding.",
        CatalogError.TimedOut => "The catalog read timed out. No alternate source was contacted.",
        CatalogError.Cancelled => "The catalog read was cancelled.",
        CatalogError.PackageNotFound => "The selected package is no longer in this catalog snapshot.",
        _ => "The selected catalog could not be read. No alternate source was contacted.",
    };
}

public abstract record CatalogResult<T>
{
    private CatalogResult() { }
    public sealed record Success(T Value) : CatalogResult<T>;
    public sealed record Failure(CatalogFailure Error) : CatalogResult<T>;
    public static CatalogResult<T> Fail(CatalogError code) => new Failure(new(code));
}

public sealed record ArtifactReference(string Path, long Size, string Sha256);
public sealed record CatalogPackage(string Id, CatalogComponent Component, string Version,
    string RuntimeIdentifier, string? SpatialAnalyzerTarget, ArtifactReference Artifact, ArtifactReference? Provenance)
{
    public string ComponentName => Component == CatalogComponent.Server ? "server" : "installer";
    public string TargetDisplay => SpatialAnalyzerTarget ?? "Independent of SA";
    public string SizeDisplay => Artifact.Size switch
    {
        < 1024 => $"{Artifact.Size:N0} B",
        < 1048576 => $"{Artifact.Size / 1024d:N1} KiB",
        _ => $"{Artifact.Size / 1048576d:N1} MiB",
    };
}

public sealed record CatalogSnapshot(string Source, string ContentSha256, CatalogComponent Component, IReadOnlyList<CatalogPackage> Packages);
public sealed record PackagePreview(string CatalogSource, string CatalogSha256, CatalogPackage Package,
    string ArtifactLocation, string? ProvenanceLocation)
{
    public string PublisherVerification => "notPerformed";
    public bool CanInstall => false;
}

public static class ReleaseCatalogCodec
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumPackages = 1000;
    private const string VersionPattern = @"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(\.(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*))?(\+[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?";

    public static CatalogResult<IReadOnlyList<CatalogPackage>> Parse(byte[] bytes)
    {
        if (bytes.Length > MaximumBytes) return CatalogResult<IReadOnlyList<CatalogPackage>>.Fail(CatalogError.TooLarge);
        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 12 });
            var root = document.RootElement;
            if (!Shape(root, ["schemaVersion", "packages"], [])) return Fail(CatalogError.InvalidDocument);
            if (!root.GetProperty("schemaVersion").TryGetInt32(out var schema) || schema != 1) return Fail(CatalogError.UnsupportedSchema);
            var packages = root.GetProperty("packages");
            if (packages.ValueKind != JsonValueKind.Array) return Fail(CatalogError.InvalidDocument);
            if (packages.GetArrayLength() > MaximumPackages) return Fail(CatalogError.TooLarge);
            var result = new List<CatalogPackage>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new HashSet<(CatalogComponent, string, string?, string)>();
            var references = new Dictionary<string, ArtifactReference>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages.EnumerateArray())
            {
                if (!Shape(package, ["id", "component", "version", "runtimeIdentifier", "artifact"], ["spatialAnalyzerTarget", "provenance"]))
                    return Fail(CatalogError.InvalidPackage);
                var id = Text(package, "id");
                var componentText = Text(package, "component");
                var version = Text(package, "version");
                var runtime = Text(package, "runtimeIdentifier");
                if (!Matches(id, "[A-Za-z0-9][A-Za-z0-9._-]*", 200) || componentText is not ("server" or "installer") ||
                    !Matches(version, VersionPattern, 128) || !Matches(runtime, "[a-z0-9]+(-[a-z0-9]+)+", 64))
                    return Fail(CatalogError.InvalidPackage);
                var component = componentText == "server" ? CatalogComponent.Server : CatalogComponent.Installer;
                var target = Text(package, "spatialAnalyzerTarget");
                var hasProvenance = package.TryGetProperty("provenance", out var provenanceElement);
                if (component == CatalogComponent.Server && (!Matches(target, @"[0-9]{4}\.[0-9]{1,8}\.[0-9]{1,8}\.[0-9]{1,8}", 40) || !hasProvenance))
                    return Fail(CatalogError.InvalidPackage);
                if (component == CatalogComponent.Installer && package.TryGetProperty("spatialAnalyzerTarget", out _))
                    return Fail(CatalogError.InvalidPackage);
                var artifactResult = ReadReference(package.GetProperty("artifact"));
                if (artifactResult is CatalogResult<ArtifactReference>.Failure invalid) return Fail(invalid.Error.Code);
                var artifact = ((CatalogResult<ArtifactReference>.Success)artifactResult).Value;
                ArtifactReference? provenance = null;
                if (hasProvenance)
                {
                    var parsed = ReadReference(provenanceElement);
                    if (parsed is CatalogResult<ArtifactReference>.Failure failure) return Fail(failure.Error.Code);
                    provenance = ((CatalogResult<ArtifactReference>.Success)parsed).Value;
                }
                if (!ids.Add(id!) || !identities.Add((component, version!, target, runtime!))) return Fail(CatalogError.DuplicatePackage);
                foreach (var reference in new[] { artifact, provenance }.OfType<ArtifactReference>())
                {
                    if (references.TryGetValue(reference.Path, out var existing) && (existing.Size != reference.Size || existing.Sha256 != reference.Sha256))
                        return Fail(CatalogError.ConflictingReference);
                    references[reference.Path] = reference;
                }
                result.Add(new(id!, component, version!, runtime!, target, artifact, provenance));
            }
            return new CatalogResult<IReadOnlyList<CatalogPackage>>.Success(result.AsReadOnly());
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            return Fail(CatalogError.InvalidDocument);
        }
    }

    public static bool IsSafeReference(string? path)
    {
        if (!Matches(path, "[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*", 512)) return false;
        return path!.Split('/').All(segment => segment.Length <= 128 && !segment.EndsWith('.') &&
            !Regex.IsMatch(segment.Split('.')[0], @"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    public static CatalogResult<PackagePreview> Preview(CatalogSnapshot catalog, string id)
    {
        var package = catalog.Packages.SingleOrDefault(item => item.Id == id);
        if (package is null) return CatalogResult<PackagePreview>.Fail(CatalogError.PackageNotFound);
        if (!IsSafeReference(package.Artifact.Path) || (package.Provenance is not null && !IsSafeReference(package.Provenance.Path)))
            return CatalogResult<PackagePreview>.Fail(CatalogError.UnsafeReference);
        if (SettingsCodec.Validate(new(catalog.Source)) is Outcome<InstallerSettings>.Failure)
            return CatalogResult<PackagePreview>.Fail(CatalogError.InvalidSource);
        return new CatalogResult<PackagePreview>.Success(new(catalog.Source, catalog.ContentSha256, package,
            Resolve(catalog.Source, package.Artifact.Path), package.Provenance is null ? null : Resolve(catalog.Source, package.Provenance.Path)));
    }

    private static string Resolve(string source, string reference) => source.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        ? new Uri(new Uri(source, UriKind.Absolute), reference).AbsoluteUri
        : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, reference.Replace('/', Path.DirectorySeparatorChar)));

    private static CatalogResult<ArtifactReference> ReadReference(JsonElement value)
    {
        if (!Shape(value, ["path", "size", "sha256"], [])) return CatalogResult<ArtifactReference>.Fail(CatalogError.InvalidPackage);
        var path = Text(value, "path");
        if (!IsSafeReference(path)) return CatalogResult<ArtifactReference>.Fail(CatalogError.UnsafeReference);
        var hash = Text(value, "sha256");
        if (!Matches(hash, "[0-9a-f]{64}", 64) || !value.GetProperty("size").TryGetInt64(out var size) || size is < 1 or > 9007199254740991)
            return CatalogResult<ArtifactReference>.Fail(CatalogError.InvalidPackage);
        return new CatalogResult<ArtifactReference>.Success(new(path!, size, hash!));
    }

    private static CatalogResult<IReadOnlyList<CatalogPackage>> Fail(CatalogError error) => CatalogResult<IReadOnlyList<CatalogPackage>>.Fail(error);
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static bool Matches(string? value, string pattern, int maximum) => value is not null && value.Length <= maximum &&
        Regex.IsMatch(value, @"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static bool Shape(JsonElement value, string[] required, string[] optional)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || (!required.Contains(property.Name) && !optional.Contains(property.Name))) return false;
        return required.All(names.Contains);
    }
}
