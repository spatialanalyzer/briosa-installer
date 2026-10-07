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
# Briosa revision that built them, then run the package harness at the approved major only.
function Check { param([bool]$Condition, [string]$Message) if (-not $Condition) { throw $Message } }
function Get-Sha256 { param([string]$Path) (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

$candidates = Get-Content -LiteralPath $CandidateFile -Raw | ConvertFrom-Json
Check ($candidates.schemaVersion -eq 1) 'Unsupported server package candidate file.'
Check ($candidates.release -cmatch '^v([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)$') 'Candidate release must be a Briosa version tag.'
$version = $Matches[1]
$major = $candidates.compatibilityMajor
Check (($major -is [int] -or $major -is [long]) -and $major -ge 1 -and $major -le [uint32]::MaxValue) 'Approved compatibility major must be a positive uint32.'
$packages = @($candidates.packages)
Check ($packages.Count -ge 2 -and @($packages.spatialAnalyzerTarget | Sort-Object -Unique).Count -eq $packages.Count) 'List one package for each of at least two exact SA targets.'
$tools = (Resolve-Path -LiteralPath $BriosaRepository).Path
$toolsRevision = git -C $tools rev-parse HEAD
Check ($LASTEXITCODE -eq 0) 'The Briosa tool checkout has no readable revision.'
$artifacts = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($ArtifactDirectory, (Get-Location).Path)).FullName

foreach ($package in $packages) {
    $name = $package.artifactName
    Check ($name -ceq "briosa-$version-sa-$($package.spatialAnalyzerTarget)-win-x64") "Candidate $name does not match its release and target."
    foreach ($file in "$name.zip", "$name.zip.sha256", "$name.provenance.json") {
        if (Test-Path -LiteralPath (Join-Path $artifacts $file) -PathType Leaf) { continue }
        gh release download $candidates.release --repo $candidates.repository --pattern $file --dir $artifacts
        Check ($LASTEXITCODE -eq 0) "Could not download $file."
    }
    $zip = Join-Path $artifacts "$name.zip"
    $provenancePath = Join-Path $artifacts "$name.provenance.json"
    Check ((Get-Sha256 $zip) -ceq $package.zipSha256) "$name.zip differs from its reviewed digest."
    Check ((Get-Sha256 $provenancePath) -ceq $package.provenanceSha256) "$name provenance differs from its reviewed digest."
    Check ((Get-Content -LiteralPath "$zip.sha256" -Raw).Trim() -ceq "$($package.zipSha256)  $name.zip") "$name checksum file differs."
    $manifest = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    Check ($manifest.artifactName -ceq $name -and $manifest.spatialAnalyzerTarget -ceq $package.spatialAnalyzerTarget) "$name provenance names another product."
    Check ($manifest.sourceRevision -ceq $toolsRevision) "$name was built from Briosa $($manifest.sourceRevision), not the catalog tool revision $toolsRevision."
}
$present = @(Get-ChildItem -LiteralPath $artifacts -Filter 'briosa-*-sa-*-win-x64.provenance.json' -File | Where-Object { $_.Name -notlike 'briosa-client-*' })
Check ($present.Count -eq $packages.Count) 'The artifact directory contains server packages outside the candidate set.'

$harness = Join-Path $PSScriptRoot 'Test-ServerPackages.ps1'
$arguments = @{ ArtifactDirectory = $artifacts; BriosaRepository = $tools }
if ($CliPath) { $arguments.CliPath = $CliPath }
# The same real packages must fail the release gate when a different major is approved.
$unapproved = if ($major -lt [uint32]::MaxValue) { $major + 1 } else { $major - 1 }
$accepted = $true
try { & $harness @arguments -ExpectedCompatibilityMajor $unapproved }
catch {
    Check ($_.Exception.Message -ceq 'Server package has an unexpected behavioral contract major.') "Unexpected harness failure: $($_.Exception.Message)"
    $accepted = $false
}
Check (-not $accepted) "Candidates were accepted at unapproved compatibility major $unapproved."
& $harness @arguments -ExpectedCompatibilityMajor $major
