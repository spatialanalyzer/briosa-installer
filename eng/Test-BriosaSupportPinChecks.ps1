[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Runs Test-BriosaSupportPins.ps1 against disposable workflow fixtures. Each unsafe form must fail
# for its stated reason, even when the same workflow also contains a valid pin.
$checker = Join-Path $PSScriptRoot 'Test-BriosaSupportPins.ps1'
$valid = 'c19f64d3b048ba33727e0db1ffa8f5806ed6c072'
$offMain = 'be5f50b4bc5c6a3043908fde46af37d6d7b29bb1'
$pinned = @"
      - uses: actions/checkout@v6
        with:
          repository: spatialanalyzer/briosa
          ref: $valid
          path: .support/briosa
"@
function Checkout([string]$With) { "      - uses: actions/checkout@v6`n        with:`n$With" }
$sha = 'full lowercase commit SHA'
$subset = 'outside the subset this check reads'
$backslash = [char]92
$cases = @(
    @{ Name = 'single-quoted repository with a branch ref'; Expect = $sha
       Steps = Checkout "          repository: 'spatialanalyzer/briosa'`n          ref: main" }
    @{ Name = 'double-quoted repository and ref off main'; Expect = 'not reachable from main'
       Steps = Checkout "          repository: `"spatialanalyzer/briosa`"`n          ref: `"$offMain`"" }
    @{ Name = 'mixed-case repository'; Expect = $sha
       Steps = Checkout "          repository: SpatialAnalyzer/Briosa`n          ref: main" }
    @{ Name = 'ref before repository'; Expect = $sha
       Steps = Checkout "          ref: main`n          path: .support/other`n          repository: spatialanalyzer/briosa" }
    @{ Name = 'missing ref'; Expect = 'exactly one ref'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          path: .support/other" }
    @{ Name = 'duplicate ref'; Expect = 'exactly one ref'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          ref: $valid`n          ref: main" }
    @{ Name = 'ref only inside a block scalar'; Expect = 'exactly one ref'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          sparse-checkout: |`n            ref: $valid" }
    @{ Name = 'quoted ref key'; Expect = $subset
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          'ref': $valid" }
    @{ Name = 'quoted repository key'; Expect = $subset
       Steps = Checkout "          `"repository`": spatialanalyzer/briosa`n          ref: $valid" }
    @{ Name = 'flow mapping'; Expect = $subset
       Steps = "      - uses: actions/checkout@v6`n        with: { repository: spatialanalyzer/briosa, ref: $valid }" }
    @{ Name = 'expression repository'; Expect = 'expression-valued repository'
       Steps = Checkout "          repository: `${{ github.repository_owner }}/briosa`n          ref: $valid" }
    @{ Name = 'expression ref'; Expect = $sha
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          ref: `${{ inputs.ref }}" }
    @{ Name = 'YAML alias ref'; Expect = $subset
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          ref: *pin" }
    @{ Name = 'quoted action tag'; Expect = $sha
       Steps = "      - uses: 'spatialanalyzer/briosa/.github/actions/sign-package@v0.9.2'" }
    @{ Name = 'clone URL in a script'; Expect = 'outside a pinned checkout'
       Steps = "      - run: git clone https://github.com/spatialanalyzer/briosa.git .support/briosa" }
    @{ Name = 'escaped slash in a double-quoted repository'; Expect = $subset
       Steps = Checkout "          repository: `"spatialanalyzer\/briosa`"`n          ref: main" }
    @{ Name = 'Unicode escape in a double-quoted repository'; Expect = $subset
       Steps = Checkout "          repository: `"spatialanalyzer$($backslash)u002Fbriosa`"`n          ref: main" }
    @{ Name = 'escaped key and value'; Expect = $subset
       Steps = Checkout "          `"reposit$($backslash)u006Fry`": `"spatialanalyzer\/briosa`"`n          ref: main" }
    @{ Name = 'folded multi-line repository expression'; Expect = 'single-line literal'
       Steps = Checkout "          repository: >-`n            `${{ format('{0}/{1}', 'spatialanalyzer', 'briosa') }}`n          ref: main" }
    @{ Name = 'repository value on the next line'; Expect = $subset
       Steps = Checkout "          repository:`n            spatialanalyzer/briosa`n          ref: main" }
    @{ Name = 'double-quoted repository across lines'; Expect = $subset
       Steps = Checkout "          repository: `"spatialanalyzer/`n            briosa`"`n          ref: main" }
    @{ Name = 'folded multi-line ref'; Expect = 'single-line literal'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          ref: >-`n            $valid" }
    @{ Name = 'tagged repository'; Expect = $subset
       Steps = Checkout "          repository: !!str spatialanalyzer/briosa`n          ref: main" }
    @{ Name = 'anchored repository'; Expect = $subset
       Steps = Checkout "          repository: &repo spatialanalyzer/briosa`n          ref: main" }
    @{ Name = 'capitalized input keys'; Expect = $sha
       Steps = Checkout "          Repository: spatialanalyzer/briosa`n          Ref: main" }
    @{ Name = 'tab indentation'; Expect = 'uses a tab'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n`t  ref: main" }
    @{ Name = 'branch ref in a .yaml workflow'; Expect = $sha; File = 'fixture.yaml'
       Steps = Checkout "          repository: spatialanalyzer/briosa`n          ref: main" }
    @{ Name = 'quoted valid pins with comments'; Expect = $null
       Steps = Checkout "          repository: 'spatialanalyzer/briosa' # reviewed`n          ref: `"$valid`" # main" }
    @{ Name = 'valid ref listed first'; Expect = $null
       Steps = Checkout "          ref: $valid`n          repository: spatialanalyzer/briosa" }
    @{ Name = 'comments and other repositories'; Expect = $null
       Steps = "      # Squash-merged as spatialanalyzer/briosa#174.`n" + (Checkout "          repository: spatialanalyzer/briosa-docs") }
)
$failures = [Collections.Generic.List[string]]::new()
foreach ($case in $cases) {
    $directory = Join-Path ([IO.Path]::GetTempPath()) "briosa-pin-check-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    try {
        $file = if ($case.ContainsKey('File')) { $case.File } else { 'fixture.yml' }
        $workflow = "name: Fixture`non: push`njobs:`n  fixture:`n    runs-on: windows-latest`n    steps:`n$pinned`n$($case.Steps)`n"
        [IO.File]::WriteAllText((Join-Path $directory $file), $workflow)
        $outcome = try { & $checker -WorkflowDirectory $directory *> $null; $null } catch { $_.Exception.Message }
        if ($null -eq $case.Expect) {
            if ($null -ne $outcome) { $failures.Add("$($case.Name): rejected unexpectedly: $outcome") }
        }
        elseif ($null -eq $outcome) { $failures.Add("$($case.Name): accepted an unsafe pin.") }
        elseif (-not $outcome.Contains($case.Expect)) { $failures.Add("$($case.Name): rejected for another reason: $outcome") }
    }
    finally { Remove-Item -LiteralPath $directory -Recurse -Force }
}
if ($failures.Count -gt 0) { throw "Support-pin checker cases failed:`n$($failures -join "`n")" }
Write-Host "Support-pin checker handled $($cases.Count) fixture workflows as expected."
