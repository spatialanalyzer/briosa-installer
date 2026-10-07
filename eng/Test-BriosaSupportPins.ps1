[CmdletBinding()]
param([string]$Branch = 'main')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Workflows execute Briosa signing, catalog, and trust tooling from pinned commits. Each pin must
# be a full SHA reachable from Briosa's main branch, never a branch name or an unmerged commit.
$repository = Split-Path -Parent $PSScriptRoot
$pins = [Collections.Generic.List[object]]::new()
foreach ($workflow in Get-ChildItem -LiteralPath (Join-Path $repository '.github/workflows') -Filter '*.yml' -File) {
    $text = Get-Content -LiteralPath $workflow.FullName -Raw
    $checkouts = [regex]::Matches($text, '(?m)^\s*repository:\s*spatialanalyzer/briosa\s*$').Count
    $pinned = [regex]::Matches($text, '(?m)^\s*repository:\s*spatialanalyzer/briosa\s*\r?\n\s*ref:\s*([^\s#]+)')
    if ($checkouts -ne $pinned.Count) { throw "$($workflow.Name) has a Briosa checkout without a ref on the following line." }
    $actions = [regex]::Matches($text, '(?m)\buses:\s*spatialanalyzer/briosa/[^@\s]*@([^\s#]+)')
    foreach ($match in @($pinned) + @($actions)) {
        $pins.Add([pscustomobject]@{ Workflow = $workflow.Name; Revision = $match.Groups[1].Value })
    }
}
if ($pins.Count -eq 0) { throw 'No Briosa support pins were found.' }
foreach ($revision in @($pins.Revision | Sort-Object -Unique)) {
    $sources = ($pins | Where-Object Revision -ceq $revision | ForEach-Object Workflow | Sort-Object -Unique) -join ', '
    if ($revision -cnotmatch '^[0-9a-f]{40}$') { throw "$sources must pin Briosa to a full lowercase commit SHA, not '$revision'." }
    $status = gh api "repos/spatialanalyzer/briosa/compare/$revision...${Branch}?per_page=1" --jq '.status'
    if ($LASTEXITCODE -ne 0) { throw "Could not compare Briosa $revision with $Branch." }
    if ($status -notin @('ahead', 'identical')) { throw "$sources pin Briosa $revision, which is not reachable from $Branch ($status)." }
    Write-Host "Briosa $revision ($sources) is reachable from $Branch."
}
