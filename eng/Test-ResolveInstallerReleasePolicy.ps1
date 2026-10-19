[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Runs Resolve-InstallerRelease.ps1 against a disposable git repository. Only a dispatch on
# main is eligible to sign; other branches are unsigned dry runs, and tag or other non-branch
# refs fail. An existing v<version> tag must already point at the dispatched commit.
$resolver = Join-Path $PSScriptRoot 'Resolve-InstallerRelease.ps1'
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporaryBase "briosa-installer-release-policy-$([Guid]::NewGuid().ToString('N'))"
$savedOutput = $env:GITHUB_OUTPUT

function Invoke-FixtureGit {
    $output = & git -c core.autocrlf=false -c commit.gpgsign=false -c tag.gpgsign=false -c user.name=Fixture -c user.email=fixture@example.invalid -C $fixture @args
    if ($LASTEXITCODE -ne 0) { throw "Fixture git $($args[0]) failed." }
    $output
}
function New-FixtureCommit([string]$Name) {
    [IO.File]::WriteAllText((Join-Path $fixture "$Name.txt"), "$Name`n")
    Invoke-FixtureGit add "$Name.txt" | Out-Null
    Invoke-FixtureGit commit --quiet -m $Name | Out-Null
    (Invoke-FixtureGit rev-parse HEAD).Trim()
}
function Invoke-Resolver([hashtable]$Parameters) {
    $outputFile = Join-Path $fixture "github-output-$([Guid]::NewGuid().ToString('N')).txt"
    $env:GITHUB_OUTPUT = $outputFile
    $global:LASTEXITCODE = 0
    try {
        $result = & $resolver -RepositoryRoot $fixture @Parameters -InformationVariable messages 6>$null
        $outputs = @{}
        foreach ($line in [IO.File]::ReadAllLines($outputFile)) {
            $name, $value = $line -split '=', 2
            $outputs[$name] = $value
        }
        [pscustomobject]@{ Failed = $false; Error = ''; Result = $result; Outputs = $outputs
            Messages = @($messages | ForEach-Object { $_.MessageData.ToString() }); ExitCode = $global:LASTEXITCODE }
    }
    catch {
        [pscustomobject]@{ Failed = $true; Error = $_.Exception.Message; Result = $null; Outputs = @{}; Messages = @(); ExitCode = $global:LASTEXITCODE }
    }
}
$failures = [Collections.Generic.List[string]]::new()
function Assert-Accepted([string]$Name, [hashtable]$Parameters, [string]$Eligible) {
    $actual = Invoke-Resolver $Parameters
    if ($actual.Failed) { $failures.Add("${Name}: rejected unexpectedly: $($actual.Error)"); return }
    if ($actual.Outputs['value'] -cne $Parameters.Version) { $failures.Add("${Name}: value output was '$($actual.Outputs['value'])', expected '$($Parameters.Version)'.") }
    if ($actual.Outputs['eligible'] -cne $Eligible) { $failures.Add("${Name}: eligible output was '$($actual.Outputs['eligible'])', expected '$Eligible'.") }
    if ($actual.Result.Eligible.ToString().ToLowerInvariant() -cne $Eligible) { $failures.Add("${Name}: returned eligibility $($actual.Result.Eligible), expected $Eligible.") }
    $warned = @($actual.Messages | Where-Object { $_ -clike '::warning::*unsigned dry run*' }).Count -gt 0
    if ($warned -ne ($Eligible -ceq 'false')) { $failures.Add("${Name}: dry-run warning emitted: $warned, expected $($Eligible -ceq 'false').") }
    if ($actual.ExitCode -ne 0) { $failures.Add("${Name}: left LASTEXITCODE $($actual.ExitCode) for the workflow step.") }
}
function Assert-Rejected([string]$Name, [hashtable]$Parameters, [string]$Expected) {
    $actual = Invoke-Resolver $Parameters
    if (-not $actual.Failed) { $failures.Add("${Name}: accepted; expected rejection containing '$Expected'."); return }
    if (-not $actual.Error.Contains($Expected)) { $failures.Add("${Name}: rejected for another reason: $($actual.Error)") }
}
function Release([string]$Ref, [string]$Commit, [string]$Version) { @{ Ref = $Ref; Commit = $Commit; Version = $Version } }

try {
    [IO.Directory]::CreateDirectory($fixture) | Out-Null
    Invoke-FixtureGit init --quiet --initial-branch=main | Out-Null
    $root = New-FixtureCommit 'root'
    $head = New-FixtureCommit 'head'
    Invoke-FixtureGit switch --quiet -c topic | Out-Null
    $topic = New-FixtureCommit 'topic'
    Invoke-FixtureGit switch --quiet main | Out-Null
    Invoke-FixtureGit tag v1.4.0 $head | Out-Null
    Invoke-FixtureGit tag -a -m 'annotated' v1.4.1 $head | Out-Null
    Invoke-FixtureGit tag v1.5.0 $root | Out-Null

    $tagRejection = 'Dispatch installer releases from main; publishing the draft release creates the tag'
    $branchRejection = 'is not a branch'
    $versionRejection = 'is not a supported semantic version'
    $tagMismatch = 'already points to'

    Assert-Accepted 'dispatch on main' (Release 'refs/heads/main' $head '1.2.0') 'true'
    Assert-Accepted 'prerelease dispatch on main' (Release 'refs/heads/main' $head '1.3.0-rc.1') 'true'
    Assert-Accepted 'dispatch on another branch' (Release 'refs/heads/topic' $topic '1.2.0') 'false'
    Assert-Accepted 'branch named like main' (Release 'refs/heads/main-backup' $head '1.2.0') 'false'
    Assert-Accepted 'existing tag at the dispatched commit' (Release 'refs/heads/main' $head '1.4.0') 'true'
    Assert-Accepted 'existing annotated tag at the dispatched commit' (Release 'refs/heads/main' $head '1.4.1') 'true'

    Assert-Rejected 'tag ref' (Release 'refs/tags/v1.2.0' $head '1.2.0') $tagRejection
    Assert-Rejected 'tag ref for another version' (Release 'refs/tags/v9.9.9' $head '1.2.0') $tagRejection
    Assert-Rejected 'pull request merge ref' (Release 'refs/pull/1/merge' $head '1.2.0') $branchRejection
    Assert-Rejected 'short branch name' (Release 'main' $head '1.2.0') $branchRejection
    Assert-Rejected 'two-part version' (Release 'refs/heads/main' $head '1.2') $versionRejection
    Assert-Rejected 'v-prefixed version' (Release 'refs/heads/main' $head 'v1.2.0') $versionRejection
    Assert-Rejected 'version with a trailing newline' (Release 'refs/heads/main' $head "1.2.0`n") $versionRejection
    Assert-Rejected 'empty version' (Release 'refs/heads/main' $head '') $versionRejection
    Assert-Rejected 'abbreviated commit' (Release 'refs/heads/main' $head.Substring(0, 12) '1.2.0') 'full commit SHA'
    Assert-Rejected 'existing tag at another commit' (Release 'refs/heads/main' $head '1.5.0') $tagMismatch
    Assert-Rejected 'existing tag at another commit on a dry run' (Release 'refs/heads/topic' $topic '1.4.0') $tagMismatch
}
finally {
    $env:GITHUB_OUTPUT = $savedOutput
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if ($resolvedFixture.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedFixture)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
if ($failures.Count -gt 0) { throw "Installer release resolution cases failed:`n$($failures -join "`n")" }
Write-Host 'Installer release resolution policy tests passed.'
$global:LASTEXITCODE = 0
