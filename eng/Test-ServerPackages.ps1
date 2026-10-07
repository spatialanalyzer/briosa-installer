[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$BriosaRepository,
    # The approved behavioral contract major for this package set. There is no default: the
    # package engine accepts any positive major, so release validation must state which it expects.
    [Parameter(Mandatory)][ValidateRange(1, 4294967295)][uint32]$ExpectedCompatibilityMajor,
    [string]$CliPath = "src/Briosa.Installer.Cli/bin/Release/net10.0/Briosa.Installer.Cli.exe"
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$cli = [IO.Path]::GetFullPath($CliPath, $repository)
$artifacts = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$caseRoot = Join-Path $temporaryBase "briosa-server-package-test-$([Guid]::NewGuid().ToString('N'))"
$feed = Join-Path $caseRoot 'feed'
$store = Join-Path $caseRoot 'store'
$config = Join-Path $caseRoot 'settings.json'
[IO.Directory]::CreateDirectory($feed) | Out-Null
$installed = [Collections.Generic.List[string]]::new()
$privateKey = Join-Path $caseRoot 'temporary-private-key.pem'
$rsa = [Security.Cryptography.RSA]::Create(3072)

function Invoke-Cli {
    param([string[]]$Arguments)
    $output = & $cli @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Installer CLI failed: $($Arguments[0]) $($Arguments[1])." }
    return ($output -join "`n")
}
function Check { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }

try {
    $provenance = @(Get-ChildItem -LiteralPath $artifacts -Filter 'briosa-*-sa-*-win-x64.provenance.json' -File |
        Where-Object { $_.Name -notlike 'briosa-client-*' })
    Check ($provenance.Count -ge 2) 'Supply at least two real server packages for coexistence validation.'
    foreach ($file in $provenance) {
        $manifest = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        Check ($manifest.schemaVersion -eq 3) 'Expected a compatibility-aware server manifest.'
        Check ($manifest.compatibility.major -eq $ExpectedCompatibilityMajor) 'Server package has an unexpected behavioral contract major.'
        $archive = Join-Path $artifacts "$($manifest.artifactName).zip"
        Check (Test-Path -LiteralPath $archive -PathType Leaf) 'Server ZIP is missing.'
        Copy-Item -LiteralPath $file.FullName -Destination $feed
        Copy-Item -LiteralPath $archive -Destination $feed
        Copy-Item -LiteralPath "$archive.sha256" -Destination $feed
    }
    $catalog = Join-Path $feed 'catalog.json'
    & (Join-Path $BriosaRepository 'eng/New-ReleaseCatalog.ps1') -ArtifactDirectory $feed -OutputPath $catalog
    [IO.File]::WriteAllText($privateKey, $rsa.ExportPkcs8PrivateKeyPem())
    & (Join-Path $BriosaRepository 'eng/Sign-ReleaseCatalog.ps1') -CatalogPath $catalog -PrivateKeyPath $privateKey -ValidDays 1
    Remove-Item -LiteralPath $privateKey
    @{ schemaVersion = 1; source = @{ catalog = $catalog; publisherKey = $rsa.ExportSubjectPublicKeyInfoPem() } } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $config -Encoding utf8

    $inventory = Invoke-Cli -Arguments @('catalog', 'list', '--component', 'server', '--config', $config) | ConvertFrom-Json
    Check ($inventory.publisherVerification -eq 'verified') 'Disposable publisher verification failed.'
    Check ($inventory.packages.Count -eq $provenance.Count) 'Catalog omitted a server product.'
    foreach ($package in $inventory.packages) {
        $result = Invoke-Cli -Arguments @('packages', 'install', '--component', 'server', '--id', $package.id,
            '--catalog-sha256', $inventory.catalogSha256, '--config', $config, '--store', $store, '--yes') | ConvertFrom-Json
        $installed.Add($package.id)
        $null = Invoke-Cli -Arguments @('packages', 'verify', '--id', $package.id, '--store', $store, '--config', $config)
        $receipt = Get-Content -LiteralPath (Join-Path $result.directory 'receipt.json') -Raw | ConvertFrom-Json
        Check ($receipt.package.id -eq $package.id) 'Receipt identity differs from the selected package.'
    }
    $all = Invoke-Cli -Arguments @('packages', 'list', '--store', $store, '--config', $config) | ConvertFrom-Json -NoEnumerate
    Check ($all.Count -eq $inventory.packages.Count) 'A side-by-side installation was replaced.'
    $first = $installed[0]
    $null = Invoke-Cli -Arguments @('packages', 'repair', '--component', 'server', '--id', $first,
        '--catalog-sha256', $inventory.catalogSha256, '--config', $config, '--store', $store, '--yes')
    $null = Invoke-Cli -Arguments @('packages', 'verify', '--id', $first, '--store', $store, '--config', $config)
    Write-Host "Verified $($installed.Count) real server packages at compatibility major ${ExpectedCompatibilityMajor}: signed catalog, schema-3 manifests, receipts, side-by-side install, verification, and exact-package repair. No server or SDK was launched."
}
finally {
    # Only products installed by this invocation are removed from its private store.
    try {
        foreach ($id in $installed) {
            $null = Invoke-Cli -Arguments @('packages', 'remove', '--id', $id, '--store', $store, '--config', $config, '--yes')
        }
    }
    finally {
        $rsa.Dispose()
        $resolved = [IO.Path]::GetFullPath($caseRoot)
        $prefix = $temporaryBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
