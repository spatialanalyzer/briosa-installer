[CmdletBinding()]
param(
    [string]$Branch = 'main',
    [string]$WorkflowDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Workflows execute Briosa signing, catalog, and trust tooling from pinned commits. Each pin must
# be a full SHA reachable from Briosa's main branch. Workflows must stay within a YAML subset in
# which the written text is the decoded value: plain keys; plain, single-quoted, or escape-free
# double-quoted scalars; simple flow sequences; and block scalars. Any other form fails closed,
# as does any Briosa reference outside a declared checkout or action. Script contents are not
# interpreted, so workflow review still guards against deliberately obfuscated fetches.
$mention = [regex]'(?i)spatialanalyzer/briosa(?![\w-])'
$item = "(?:'(?:[^']|'')*'|""[^""\\]*""|[^\s,\[\]{}#&*!|>'""%@``?:-][^,\[\]{}#]*?)"
$sequence = [regex]"^\[\s*(?:$item(?:\s*,\s*$item)*)?\s*\](?:\s+#.*)?$"
$plain = [regex]"^(?<value>[^\s,\[\]{}#&*!|>'""%@``?:-](?:[^#]|(?<!\s)#)*?)(?:\s+#.*)?$"

function Read-Value {
    param([string]$Text)
    if ($Text -match '^(#.*)?$') { return @{ Kind = 'empty'; Value = $null } }
    if ($Text -match '^[|>][1-9]?[+-]?[1-9]?(\s+#.*)?$') { return @{ Kind = 'block'; Value = $null } }
    $quoted = [regex]::Match($Text, "^'(?<value>(?:[^']|'')*)'(?:\s+#.*)?$")
    if ($quoted.Success) { return @{ Kind = 'scalar'; Value = $quoted.Groups['value'].Value.Replace("''", "'") } }
    $quoted = [regex]::Match($Text, '^"(?<value>[^"\\]*)"(?:\s+#.*)?$')
    if ($quoted.Success) { return @{ Kind = 'scalar'; Value = $quoted.Groups['value'].Value } }
    if ($sequence.IsMatch($Text)) { return @{ Kind = 'sequence'; Value = $null } }
    $match = $plain.Match($Text)
    if ($match.Success) { return @{ Kind = 'scalar'; Value = $match.Groups['value'].Value.TrimEnd() } }
    return $null
}

$pins = [Collections.Generic.List[object]]::new()
foreach ($workflow in Get-ChildItem -LiteralPath $WorkflowDirectory -File | Where-Object Extension -in '.yml', '.yaml') {
    $lines = [IO.File]::ReadAllLines($workflow.FullName)
    $entries = [Collections.Generic.List[object]]::new()
    $frames = [Collections.Generic.List[object]]::new()
    $nextMap = 0
    $block = -1
    $mentionLines = @{}
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        $location = "$($workflow.Name):$($i + 1)"
        if ($raw.Contains("`t")) { throw "$location uses a tab; indent workflows with spaces." }
        $indent = $raw.Length - $raw.TrimStart(' ').Length
        if ($block -ge 0) {
            if ($raw.Trim().Length -eq 0 -or $indent -gt $block) {
                if ($mention.IsMatch($raw)) { $mentionLines[$i] = $location }
                continue
            }
            $block = -1
        }
        if ($raw -match '^\s*(#|$)') { continue }
        if ($mention.IsMatch($raw)) { $mentionLines[$i] = $location }
        $line = [regex]::Match($raw, '^(?<indent> *)(?<item>- +)?(?<rest>.*?)\s*$')
        $itemWidth = $line.Groups['item'].Length
        $rest = $line.Groups['rest'].Value
        $pair = [regex]::Match($rest, '^(?<key>[A-Za-z0-9_-]+):(?:\s+(?<value>.*))?$')
        $unsupported = "$location uses YAML outside the subset this check reads; use plain keys and plain, single-quoted, or escape-free double-quoted values."
        if ($pair.Success) {
            $value = Read-Value $pair.Groups['value'].Value
            if ($null -eq $value) { throw $unsupported }
            $column = $indent + $itemWidth
            if ($itemWidth -gt 0) { while ($frames.Count -gt 0 -and $frames[-1].Column -ge $column) { $frames.RemoveAt($frames.Count - 1) } }
            else { while ($frames.Count -gt 0 -and $frames[-1].Column -gt $column) { $frames.RemoveAt($frames.Count - 1) } }
            if ($frames.Count -eq 0 -or $frames[-1].Column -ne $column) { $frames.Add([pscustomobject]@{ Column = $column; Map = ++$nextMap }) }
            $entries.Add([pscustomobject]@{ Map = $frames[-1].Map; Key = $pair.Groups['key'].Value.ToLowerInvariant()
                Kind = $value.Kind; Value = $value.Value; Line = $i; Location = $location })
            if ($value.Kind -eq 'block') { $block = $column }
        }
        elseif ($itemWidth -gt 0) {
            $value = Read-Value $rest
            if ($null -eq $value -or $value.Kind -eq 'empty') { throw $unsupported }
            if ($value.Kind -eq 'block') { $block = $indent }
        }
        else { throw $unsupported }
    }
    $pinLines = @{}
    foreach ($entry in $entries) {
        if ($entry.Key -eq 'repository') {
            if ($entry.Kind -ne 'scalar') { throw "$($entry.Location) repository must be a single-line literal." }
            if ($entry.Value.Contains('${{')) { throw "$($entry.Location) checks out an expression-valued repository; name it literally." }
            if ($entry.Value -ine 'spatialanalyzer/briosa') { continue }
            $refs = @($entries | Where-Object { $_.Map -eq $entry.Map -and $_.Key -eq 'ref' })
            if ($refs.Count -ne 1) { throw "$($entry.Location) Briosa checkout needs exactly one ref in the same mapping." }
            if ($refs[0].Kind -ne 'scalar') { throw "$($refs[0].Location) Briosa checkout ref must be a single-line literal." }
            $pins.Add([pscustomobject]@{ Location = $entry.Location; Revision = $refs[0].Value })
            $pinLines[$entry.Line] = $true
        }
        elseif ($entry.Key -eq 'uses' -and $entry.Kind -eq 'scalar') {
            $action = [regex]::Match($entry.Value, '^(?i:spatialanalyzer/briosa)(?:/[^@]*)?@(?<ref>.+)$')
            if (-not $action.Success) { continue }
            $pins.Add([pscustomobject]@{ Location = $entry.Location; Revision = $action.Groups['ref'].Value })
            $pinLines[$entry.Line] = $true
        }
    }
    foreach ($index in $mentionLines.Keys) {
        if (-not $pinLines.ContainsKey($index) -or $mention.Matches($lines[$index]).Count -ne 1) {
            throw "$($mentionLines[$index]) references spatialanalyzer/briosa outside a pinned checkout or action; use repository: and ref: keys or uses: spatialanalyzer/briosa/<path>@<sha>."
        }
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
