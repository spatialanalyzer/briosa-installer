[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BriosaRepository,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [string]$CandidateFile = (Join-Path $PSScriptRoot 'server-package-candidates.json'),
    [string]$CliPath
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Acquire the reviewed real server packages when absent, verify their pinned digests and the
# Briosa revisions that built them, then install every package side by side in one store with
# each package's approved compatibility major. The catalog tools are the newest release's source
# revision; every older release must have been built from a revision reachable from Briosa main.
function Check { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Get-Sha256 { param([string]$Path) (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$candidates = Get-Content -LiteralPath $CandidateFile -Raw | ConvertFrom-Json
Check ($candidates.schemaVersion -eq 2) 'Unsupported server package candidate file.'
Check ($candidates.repository -ceq 'spatialanalyzer/briosa') 'Server package candidates must come from Briosa releases.'
$packages = @($candidates.packages)
Check (@($packages.artifactName | Sort-Object -Unique).Count -eq $packages.Count) 'List each server package once.'
$releases = [ordered]@{}
foreach ($package in $packages) {
    $name = $package.artifactName
    Check ($package.release -cmatch '^v([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)$') "Candidate $name release must be a Briosa version tag."
    $version = $Matches[1]
    $major = $package.compatibilityMajor
    Check (($major -is [int] -or $major -is [long]) -and $major -ge 1 -and $major -le [uint32]::MaxValue) "Approved compatibility major for $name must be a positive uint32."
    Check ($name -ceq "briosa-$version-sa-$($package.spatialAnalyzerTarget)-win-x64") "Candidate $name does not match its release and target."
    if (-not $releases.Contains($package.release)) {
        $releases[$package.release] = [pscustomobject]@{ Version = [semver]$version; Major = $major; Packages = [Collections.Generic.List[object]]::new(); Revision = $null }
    }
    $release = $releases[$package.release]
    Check ($release.Major -eq $major) "Packages from $($package.release) must share one approved compatibility major."
    $release.Packages.Add($package)
}
foreach ($tag in $releases.Keys) {
    $targets = @($releases[$tag].Packages.spatialAnalyzerTarget)
    Check ($targets.Count -ge 2 -and @($targets | Sort-Object -Unique).Count -eq $targets.Count) "List one $tag package for each of at least two exact SA targets."
}
$tools = (Resolve-Path -LiteralPath $BriosaRepository).Path
$toolsRevision = git -C $tools rev-parse HEAD
Check ($LASTEXITCODE -eq 0) 'The Briosa tool checkout has no readable revision.'
$artifacts = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($ArtifactDirectory, (Get-Location).Path)).FullName

foreach ($package in $packages) {
    $name = $package.artifactName
    foreach ($file in "$name.zip", "$name.zip.sha256", "$name.provenance.json") {
        if (Test-Path -LiteralPath (Join-Path $artifacts $file) -PathType Leaf) { continue }
        gh release download $package.release --repo $candidates.repository --pattern $file --dir $artifacts
        Check ($LASTEXITCODE -eq 0) "Could not download $file from $($package.release)."
    }
    $zip = Join-Path $artifacts "$name.zip"
    $provenancePath = Join-Path $artifacts "$name.provenance.json"
    Check ((Get-Sha256 $zip) -ceq $package.zipSha256) "$name.zip differs from its reviewed digest."
    Check ((Get-Sha256 $provenancePath) -ceq $package.provenanceSha256) "$name provenance differs from its reviewed digest."
    Check ((Get-Content -LiteralPath "$zip.sha256" -Raw).Trim() -ceq "$($package.zipSha256)  $name.zip") "$name checksum file differs."
    $manifest = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    Check ($manifest.artifactName -ceq $name -and $manifest.spatialAnalyzerTarget -ceq $package.spatialAnalyzerTarget) "$name provenance names another product."
    Check ($manifest.sourceRevision -cmatch '^[0-9a-f]{40}$') "$name provenance has no full Briosa source revision."
    $release = $releases[$package.release]
    if ($null -eq $release.Revision) { $release.Revision = $manifest.sourceRevision }
    Check ($manifest.sourceRevision -ceq $release.Revision) "Packages from $($package.release) were built from different Briosa revisions."
}
$present = @(Get-ChildItem -LiteralPath $artifacts -Filter 'briosa-*-sa-*-win-x64.provenance.json' -File | Where-Object { $_.Name -notlike 'briosa-client-*' })
Check (@($present | Where-Object { $_.Name -cnotin @($packages | ForEach-Object { "$($_.artifactName).provenance.json" }) }).Count -eq 0) 'The artifact directory contains server packages outside the candidate set.'

# The newest release's revision supplies the catalog tools; the workflow pin check keeps it on main.
$ordered = @($releases.Values | Sort-Object Version)
$newest = $ordered[-1]
Check ($newest.Revision -ceq $toolsRevision) "The newest candidates were built from Briosa $($newest.Revision), not the catalog tool revision $toolsRevision."
foreach ($revision in @($ordered | Select-Object -SkipLast 1 | ForEach-Object Revision | Sort-Object -Unique)) {
    $status = gh api "repos/$($candidates.repository)/compare/$revision...main?per_page=1" --jq '.status'
    Check ($LASTEXITCODE -eq 0) "Could not compare Briosa $revision with main."
    Check ($status -in @('ahead', 'identical')) "Older candidates were built from Briosa $revision, which is not reachable from main ($status)."
    Write-Host "Briosa $revision, which built older candidates, is reachable from main."
}

$approved = @{}
foreach ($package in $packages) { $approved[$package.artifactName] = [uint32]$package.compatibilityMajor }
$harness = Join-Path $PSScriptRoot 'Test-ServerPackages.ps1'
$arguments = @{ ArtifactDirectory = $artifacts; BriosaRepository = $tools }
if ($CliPath) { $arguments.CliPath = $CliPath }
# Each real package must fail the release gate when a different major is approved for it.
foreach ($package in $packages) {
    $name = $package.artifactName
    $major = $approved[$name]
    $unapproved = $approved.Clone()
    $unapproved[$name] = if ($major -lt [uint32]::MaxValue) { $major + 1 } else { $major - 1 }
    $accepted = $true
    try { & $harness @arguments -ExpectedCompatibilityMajors $unapproved }
    catch {
        Check ($_.Exception.Message -ceq "Server package $name has an unexpected behavioral contract major.") "Unexpected harness failure: $($_.Exception.Message)"
        $accepted = $false
    }
    Check (-not $accepted) "$name was accepted at unapproved compatibility major $($unapproved[$name])."
}
& $harness @arguments -ExpectedCompatibilityMajors $approved
