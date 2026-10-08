[CmdletBinding()]
param(
    [string]$Branch = 'main',
    [string]$WorkflowDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Workflows execute Briosa signing, catalog, and trust tooling from pinned commits. Each pin must
# be a full SHA reachable from Briosa's main branch. Every other reference to the repository,
# including YAML forms this line-based reader does not model, fails closed.
$mention = [regex]'(?i)spatialanalyzer/briosa(?![\w-])'
$checkout = [regex]"^(?<indent>\s*)repository\s*:\s*(?<q>['""]?)(?i:spatialanalyzer/briosa)\k<q>(?:\s+#.*)?\s*$"
$action = [regex]"^\s*(?:-\s+)?uses\s*:\s*(?<q>['""]?)(?i:spatialanalyzer/briosa)(?:/[^@'""\s]*)?@(?<ref>[^'""\s#]+)\k<q>(?:\s+#.*)?\s*$"
$refValue = [regex]"^\s*ref\s*:\s*(?<q>['""]?)(?<ref>[^'""\s#]+)\k<q>(?:\s+#.*)?\s*$"
$expressionRepository = [regex]"^\s*(?:-\s+)?['""]?repository['""]?\s*:.*\$\{\{"

# Sibling keys share the repository key's column; deeper lines are nested values or block scalars.
function Get-CheckoutRefs {
    param([string[]]$Lines, [int]$Index, [int]$Column)
    $refs = [Collections.Generic.List[string]]::new()
    foreach ($direction in -1, 1) {
        for ($j = $Index + $direction; $j -ge 0 -and $j -lt $Lines.Count; $j += $direction) {
            $text = $Lines[$j]
            if ($text -match '^\s*(#|$)') { continue }
            $indent = $text.Length - $text.TrimStart().Length
            if ($indent -lt $Column) { break }
            if ($indent -eq $Column -and $text -match '^\s*ref\s*:') { $refs.Add($text) }
        }
    }
    return , $refs
}

$pins = [Collections.Generic.List[object]]::new()
foreach ($workflow in Get-ChildItem -LiteralPath $WorkflowDirectory -File | Where-Object Extension -in '.yml', '.yaml') {
    $lines = [IO.File]::ReadAllLines($workflow.FullName)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $location = "$($workflow.Name):$($i + 1)"
        if ($line -match '^\s*#') { continue }
        if ($expressionRepository.IsMatch($line)) { throw "$location checks out an expression-valued repository; name it literally." }
        $count = $mention.Matches($line).Count
        if ($count -eq 0) { continue }
        $checkoutMatch = $checkout.Match($line)
        $actionMatch = $action.Match($line)
        if ($count -eq 1 -and $checkoutMatch.Success) {
            $refs = Get-CheckoutRefs -Lines $lines -Index $i -Column $checkoutMatch.Groups['indent'].Length
            if ($refs.Count -ne 1) { throw "$location Briosa checkout needs exactly one ref key in the same mapping." }
            $value = $refValue.Match($refs[0])
            if (-not $value.Success) { throw "$location Briosa checkout ref is not a plain commit SHA." }
            $revision = $value.Groups['ref'].Value
        }
        elseif ($count -eq 1 -and $actionMatch.Success) { $revision = $actionMatch.Groups['ref'].Value }
        else { throw "$location references spatialanalyzer/briosa outside a pinned checkout or action; use repository: and ref: keys or uses: spatialanalyzer/briosa/<path>@<sha>." }
        $pins.Add([pscustomobject]@{ Location = $location; Revision = $revision })
    }
}
if ($pins.Count -eq 0) { throw 'No Briosa support pins were found.' }
# Reject malformed pins before any network request.
foreach ($pin in $pins) {
    if ($pin.Revision -cnotmatch '^[0-9a-f]{40}$') { throw "$($pin.Location) must pin Briosa to a full lowercase commit SHA, not '$($pin.Revision)'." }
}
foreach ($revision in @($pins.Revision | Sort-Object -Unique)) {
    $sources = ($pins | Where-Object Revision -ceq $revision | ForEach-Object Location) -join ', '
    $status = gh api "repos/spatialanalyzer/briosa/compare/$revision...${Branch}?per_page=1" --jq '.status'
    if ($LASTEXITCODE -ne 0) { throw "Could not compare Briosa $revision with $Branch." }
    if ($status -notin @('ahead', 'identical')) { throw "$sources pin Briosa $revision, which is not reachable from $Branch ($status)." }
    Write-Host "Briosa $revision ($sources) is reachable from $Branch."
}
