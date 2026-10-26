[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$BriosaRepository,
    # The approved behavioral contract major for a single-major package set. There is no default: the
    # package engine accepts any positive major, so release validation must state which it expects.
    [Parameter(Mandatory, ParameterSetName = 'SingleMajor')][ValidateRange(1, 4294967295)][uint32]$ExpectedCompatibilityMajor,
    # The approved major for each artifactName when the set mixes majors. Every supplied package
    # needs an entry, and every entry must name a supplied package.
    [Parameter(Mandatory, ParameterSetName = 'MajorPerArtifact')][ValidateNotNullOrEmpty()][hashtable]$ExpectedCompatibilityMajors,
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
    $approved = $null
    if ($PSCmdlet.ParameterSetName -eq 'MajorPerArtifact') {
        $approved = [Collections.Generic.Dictionary[string, uint32]]::new([StringComparer]::Ordinal)
        foreach ($entry in $ExpectedCompatibilityMajors.GetEnumerator()) {
            $value = $entry.Value
            Check ($entry.Key -is [string] -and ($value -is [int] -or $value -is [long] -or $value -is [uint32]) -and
                $value -ge 1 -and $value -le [uint32]::MaxValue) 'Approved compatibility majors must map artifact names to positive uint32 majors.'
            $approved[$entry.Key] = [uint32]$value
        }
    }
    $provenance = @(Get-ChildItem -LiteralPath $artifacts -Filter 'briosa-*-sa-*-win-x64.provenance.json' -File |
        Where-Object { $_.Name -notlike 'briosa-client-*' })
    Check ($provenance.Count -ge 2) 'Supply at least two real server packages for coexistence validation.'
    $majors = [Collections.Generic.Dictionary[string, uint32]]::new([StringComparer]::Ordinal)
    foreach ($file in $provenance) {
        $manifest = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        Check ($manifest.schemaVersion -eq 3) 'Expected a compatibility-aware server manifest.'
        $name = $manifest.artifactName
        Check ($file.Name -ceq "$name.provenance.json") 'Server provenance identity and filename disagree.'
        $expected = $ExpectedCompatibilityMajor
        if ($null -ne $approved) {
            Check ($approved.ContainsKey($name)) "Server package $name has no approved behavioral contract major."
            $expected = $approved[$name]
        }
        Check ($manifest.compatibility.major -eq $expected) "Server package $name has an unexpected behavioral contract major."
        $majors[$name] = $expected
    }
    if ($null -ne $approved) {
        Check (@($approved.Keys | Where-Object { -not $majors.ContainsKey($_) }).Count -eq 0) 'An approved compatibility major names a server package that was not supplied.'
    }
    # Copy only after every manifest has its approved major, so a rejected set copies no archive.
    foreach ($name in $majors.Keys) {
        $archive = Join-Path $artifacts "$name.zip"
        Check (Test-Path -LiteralPath $archive -PathType Leaf) 'Server ZIP is missing.'
        Copy-Item -LiteralPath (Join-Path $artifacts "$name.provenance.json") -Destination $feed
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
    # Every product goes into the same private store, so products of different majors coexist.
    foreach ($package in $inventory.packages) {
        Check ($majors.ContainsKey($package.id)) 'Catalog lists a server product that was not supplied.'
        $result = Invoke-Cli -Arguments @('packages', 'install', '--component', 'server', '--id', $package.id,
            '--catalog-sha256', $inventory.catalogSha256, '--config', $config, '--store', $store, '--yes') | ConvertFrom-Json
        $installed.Add($package.id)
        $receipt = Get-Content -LiteralPath (Join-Path $result.directory 'receipt.json') -Raw | ConvertFrom-Json
        Check ($receipt.package.id -eq $package.id) 'Receipt identity differs from the selected package.'
        $embedded = Get-Content -LiteralPath (Join-Path $result.directory 'payload/manifest.json') -Raw | ConvertFrom-Json
        Check ($embedded.compatibility.major -eq $majors[$package.id]) 'Installed manifest changed its behavioral contract major.'
    }
    $all = Invoke-Cli -Arguments @('packages', 'list', '--store', $store, '--config', $config) | ConvertFrom-Json -NoEnumerate
    $listed = @($all | ForEach-Object { $_.id })
    Check ($listed.Count -eq $installed.Count -and @($installed | Where-Object { $_ -cnotin $listed }).Count -eq 0) 'A side-by-side installation was replaced.'
    foreach ($id in $installed) {
        $null = Invoke-Cli -Arguments @('packages', 'verify', '--id', $id, '--store', $store, '--config', $config)
    }
    # Repair the first product of each approved major, then check that every product still verifies.
    $repaired = @($installed | Group-Object { $majors[$_] } | Sort-Object { [uint32]$_.Name } | ForEach-Object { $_.Group[0] })
    foreach ($id in $repaired) {
        $null = Invoke-Cli -Arguments @('packages', 'repair', '--component', 'server', '--id', $id,
            '--catalog-sha256', $inventory.catalogSha256, '--config', $config, '--store', $store, '--yes')
    }
    foreach ($id in $installed) {
        $null = Invoke-Cli -Arguments @('packages', 'verify', '--id', $id, '--store', $store, '--config', $config)
    }
    $majorList = @($majors.Values | Sort-Object -Unique)
    $majorText = "$(if ($majorList.Count -gt 1) { 'majors' } else { 'major' }) $($majorList -join ', ')"
    foreach ($id in @($installed)) {
        $null = Invoke-Cli -Arguments @('packages', 'remove', '--id', $id, '--store', $store, '--config', $config, '--yes')
        $null = $installed.Remove($id)
    }
    $remaining = Invoke-Cli -Arguments @('packages', 'list', '--store', $store, '--config', $config) | ConvertFrom-Json -NoEnumerate
    Check ($remaining.Count -eq 0) 'Removal left a server product in the store.'
    Write-Host "Verified $($listed.Count) real server packages at compatibility ${majorText} side by side in one store: signed catalog, schema-3 manifests, receipts, verification of every product, exact-package repair of $($repaired -join ', '), and removal. No server or SDK was launched."
}
finally {
    # Only products installed by this invocation, and not yet removed, are removed from its private store.
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
