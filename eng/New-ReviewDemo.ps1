[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$BriosaRepository,
    [string]$InstallerPackage
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$demoRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $demoRoot) { throw 'Use a new demo directory to preserve earlier review state.' }
$feed = Join-Path $demoRoot 'feed'
$null = New-Item -ItemType Directory -Path $feed -Force
$producer = Join-Path $BriosaRepository 'eng/New-ReleaseCatalog.ps1'
$signer = Join-Path $BriosaRepository 'eng/Sign-ReleaseCatalog.ps1'
$packages = @(
    @{ version = '0.1.0-demo.1'; target = '2099.1.0101.1' },
    @{ version = '0.2.0-demo.1'; target = '2099.1.0101.1' },
    @{ version = '0.1.0-demo.1'; target = '2099.2.0202.2' }
)
foreach ($package in $packages) {
    $name = "briosa-$($package.version)-sa-$($package.target)-win-x64"
    $manifest = [ordered]@{ schemaVersion = 2; artifactName = $name; briosaVersion = $package.version; runtimeIdentifier = 'win-x64'; spatialAnalyzerTarget = $package.target; protocolPackage = 'briosa'; spatialAnalyzerBundled = $false }
    $manifestBytes = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json) + "`n")
    [IO.File]::WriteAllBytes((Join-Path $feed "$name.provenance.json"), $manifestBytes)
    $zipPath = Join-Path $feed "$name.zip"
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $stream = $zip.CreateEntry("$name/manifest.json").Open()
        try { $stream.Write($manifestBytes) } finally { $stream.Dispose() }
        foreach ($file in @('Briosa.Server.exe', 'Briosa.Worker.exe', 'README.txt')) {
            $stream = $zip.CreateEntry("$name/$file").Open()
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes('INERT REVIEW FIXTURE. Not an executable. No SpatialAnalyzer or Hexagon content. Never launch this file.')
                $stream.Write($bytes)
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    [IO.File]::WriteAllText("$zipPath.sha256", "$((Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant())  $name.zip`n")
}
if ($InstallerPackage) {
    $archive = (Resolve-Path -LiteralPath $InstallerPackage).Path
    $base = [IO.Path]::GetFileNameWithoutExtension($archive)
    foreach ($source in @($archive, "$archive.sha256", (Join-Path ([IO.Path]::GetDirectoryName($archive)) "$base.provenance.json"))) {
        Copy-Item -LiteralPath $source -Destination $feed
    }
}
$catalog = Join-Path $feed 'catalog.json'
& $producer -ArtifactDirectory $feed -OutputPath $catalog
$rsa = [Security.Cryptography.RSA]::Create(3072)
$privateFile = Join-Path $demoRoot 'temporary-fixture-private-key.pem'
try {
    [IO.File]::WriteAllText($privateFile, $rsa.ExportPkcs8PrivateKeyPem())
    $publicKey = $rsa.ExportSubjectPublicKeyInfoPem()
    [IO.File]::WriteAllText((Join-Path $demoRoot 'fixture-public-key.pem'), $publicKey)
    & $signer -CatalogPath $catalog -PrivateKeyPath $privateFile -ValidDays 7
    $settings = [ordered]@{ schemaVersion = 1; source = [ordered]@{ catalog = $catalog; publisherKey = $publicKey } }
    [IO.File]::WriteAllText((Join-Path $demoRoot 'settings.json'), ($settings | ConvertTo-Json -Depth 5) + "`n")
} finally {
    $rsa.Dispose()
    if (Test-Path -LiteralPath $privateFile) { Remove-Item -LiteralPath $privateFile }
}
[IO.File]::WriteAllText((Join-Path $demoRoot 'README.txt'), @'
This is a generated local review fixture, not production release infrastructure.
All server packages and SA target identifiers are invented and contain inert text,
never real executables or vendor binaries. Do not use them as application runtimes.
The optional installer package is the real locally built manager application.
The temporary signing private key was deleted. The fixture catalog expires in 7 days;
generate a fresh demo directory to continue reviewing after expiry.
Launch the app with --config <this-directory>\settings.json --store <this-directory>\store.
Install two versions, verify, damage an inert file and repair, then remove one version.
Other installed versions should remain. Use Installed > Recover for interrupted staging.
For installer switching, install the real installer package and select Use installer version.
All settings and package changes stay in this demo directory when those explicit options are used.
'@)
Write-Output "Review demo ready: $demoRoot"
