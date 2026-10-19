[CmdletBinding()]
param(
    [string]$WorkflowDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) '.github/workflows')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Checks the workflow text that keeps release signing on reviewed main code:
# - no workflow runs on tag pushes (or tag creation);
# - release.yml is dispatched only, with version and publish inputs;
# - only release.yml's sign job and signing-smoke.yml's signing job name the release-signing
#   environment, and each requires github.ref == 'refs/heads/main' in its job condition;
# - release.yml resolves the release with eng/Resolve-InstallerRelease.ps1, and only its
#   publish job can write repository contents, creating a draft release at the run's commit.
# Jobs are read by indentation: top-level jobs:, job headers at 2 spaces, job keys at 4.
# Workflows must also pass Test-BriosaSupportPins.ps1, which rejects YAML outside a plain subset.
$signingEnvironment = 'release-signing'
$signingJobs = @('release.yml:sign', 'signing-smoke.yml:signing')
$mainCondition = "github.ref == 'refs/heads/main'"
$failures = [Collections.Generic.List[string]]::new()

function ConvertFrom-Scalar([string]$Text) {
    $value = ($Text -replace '\s+#.*$', '').Trim()
    $quoted = [regex]::Match($value, "^'(?<value>(?:[^']|'')*)'$")
    if ($quoted.Success) { return $quoted.Groups['value'].Value.Replace("''", "'") }
    $quoted = [regex]::Match($value, '^"(?<value>[^"\\]*)"$')
    if ($quoted.Success) { return $quoted.Groups['value'].Value }
    $value
}
function ConvertFrom-FlowSequence([string]$Text) {
    $value = ConvertFrom-Scalar $Text
    if ($value -notmatch '^\[(?<items>.*)\]$') { return @($value) }
    @($Matches['items'] -split ',' | ForEach-Object { ConvertFrom-Scalar $_ } | Where-Object { $_ })
}
function Read-Workflow([IO.FileInfo]$File) {
    $lines = [IO.File]::ReadAllLines($File.FullName)
    $entries = [Collections.Generic.List[object]]::new()
    $block = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        $indent = $raw.Length - $raw.TrimStart(' ').Length
        if ($block -ge 0) {
            if ($raw.Trim().Length -eq 0 -or $indent -gt $block) { continue }
            $block = -1
        }
        if ($raw -match '^\s*(#|$)') { continue }
        $line = [regex]::Match($raw, '^(?<indent> *)(?<item>- +)?(?<rest>.*?)\s*$')
        $column = $indent + $line.Groups['item'].Length
        $pair = [regex]::Match($line.Groups['rest'].Value, '^(?<key>[A-Za-z0-9_-]+):(?:\s+(?<value>.*))?$')
        if ($pair.Success) {
            $value = ($pair.Groups['value'].Value -replace '^#.*$', '').Trim()
            $entries.Add([pscustomobject]@{ Index = $entries.Count; Line = $i; Column = $column; Key = $pair.Groups['key'].Value
                Raw = $value; Value = (ConvertFrom-Scalar $value); Block = ($value -match '^[|>]') })
            if ($value -match '^[|>]') { $block = $column }
        }
        elseif ($line.Groups['rest'].Value -match '^[|>]') { $block = $indent }
    }
    [pscustomobject]@{ Name = $File.Name; Lines = $lines; Entries = $entries }
}
function Get-Descendants($Workflow, $Parent) {
    $result = [Collections.Generic.List[object]]::new()
    for ($k = $Parent.Index + 1; $k -lt $Workflow.Entries.Count; $k++) {
        if ($Workflow.Entries[$k].Column -le $Parent.Column) { break }
        $result.Add($Workflow.Entries[$k])
    }
    , $result
}
function Get-Children($Workflow, $Parent) {
    $descendants = Get-Descendants $Workflow $Parent
    if ($descendants.Count -eq 0) { return @() }
    $column = ($descendants | Measure-Object -Property Column -Minimum).Minimum
    @($descendants | Where-Object Column -eq $column)
}
function Get-Child($Workflow, $Parent, [string]$Key) {
    @(Get-Children $Workflow $Parent | Where-Object Key -ceq $Key) | Select-Object -First 1
}
function Get-TopLevel($Workflow, [string]$Key) {
    @($Workflow.Entries | Where-Object { $_.Column -eq 0 -and $_.Key -ceq $Key }) | Select-Object -First 1
}
function Get-Jobs($Workflow) {
    $jobsEntry = Get-TopLevel $Workflow 'jobs'
    if ($null -eq $jobsEntry) { $failures.Add("$($Workflow.Name) has no top-level jobs: mapping."); return @() }
    $jobs = foreach ($header in @(Get-Children $Workflow $jobsEntry)) {
        if ($header.Column -ne 2 -or $header.Raw) {
            $failures.Add("$($Workflow.Name):$($header.Line + 1) job '$($header.Key)' must be a mapping header indented 2 spaces under jobs:.")
            continue
        }
        $keys = @(Get-Children $Workflow $header)
        foreach ($key in $keys | Where-Object Column -ne 4) {
            $failures.Add("$($Workflow.Name):$($key.Line + 1) job '$($header.Key)' keys must be indented 4 spaces.")
        }
        $next = @($Workflow.Entries | Where-Object { $_.Index -gt $header.Index -and $_.Column -le 2 }) | Select-Object -First 1
        $end = if ($null -ne $next) { $next.Line } else { $Workflow.Lines.Count }
        [pscustomobject]@{ Name = $header.Key; Header = $header; Keys = $keys; FirstLine = $header.Line; EndLine = $end
            Text = ($Workflow.Lines[$header.Line..($end - 1)] -join "`n") }
    }
    @($jobs)
}
function Get-JobKey($Job, [string]$Key) {
    @($Job.Keys | Where-Object Key -ceq $Key) | Select-Object -First 1
}

$workflows = @(Get-ChildItem -LiteralPath $WorkflowDirectory -File | Where-Object Extension -in '.yml', '.yaml' | Sort-Object Name | ForEach-Object { Read-Workflow $_ })
if ($workflows.Count -eq 0) { throw "No workflows were found in $WorkflowDirectory." }
$release = $workflows | Where-Object Name -ceq 'release.yml'
if ($null -eq $release) { throw "$WorkflowDirectory has no release.yml." }

# No workflow may run on a tag push. A push trigger without a branch filter also runs on tags.
foreach ($workflow in $workflows) {
    $on = Get-TopLevel $workflow 'on'
    if ($null -eq $on) { $failures.Add("$($workflow.Name) has no top-level on: trigger."); continue }
    $triggers = if ($on.Raw) { @(ConvertFrom-FlowSequence $on.Raw | ForEach-Object { [pscustomobject]@{ Key = $_; Entry = $null } }) }
    else { @(Get-Children $workflow $on | ForEach-Object { [pscustomobject]@{ Key = $_.Key; Entry = $_ } }) }
    foreach ($trigger in $triggers) {
        if ($trigger.Key -ceq 'create') { $failures.Add("$($workflow.Name) triggers on create, which includes tag creation.") }
        if ($trigger.Key -cne 'push') { continue }
        $filters = if ($null -ne $trigger.Entry) { @(Get-Children $workflow $trigger.Entry | ForEach-Object Key) } else { @() }
        if ($filters -ccontains 'tags' -or $filters -ccontains 'tags-ignore') {
            $failures.Add("$($workflow.Name) triggers on tag pushes (push: tags); dispatch releases from main instead.")
        }
        elseif (-not ($filters -ccontains 'branches' -or $filters -ccontains 'branches-ignore')) {
            $failures.Add("$($workflow.Name) has a push trigger without a branches filter, which also runs on tag pushes.")
        }
    }
}

# release.yml is dispatched only, with a required version and a publish switch that defaults off.
$on = Get-TopLevel $release 'on'
if ($null -ne $on) {
    $triggers = if ($on.Raw) { @(ConvertFrom-FlowSequence $on.Raw) } else { @(Get-Children $release $on | ForEach-Object Key) }
    if ($triggers.Count -ne 1 -or $triggers[0] -cne 'workflow_dispatch') {
        $failures.Add("release.yml on: must contain only workflow_dispatch, not: $($triggers -join ', ').")
    }
    $dispatch = if ($on.Raw) { $null } else { Get-Child $release $on 'workflow_dispatch' }
    $inputs = if ($null -ne $dispatch) { Get-Child $release $dispatch 'inputs' } else { $null }
    if ($null -eq $inputs) { $failures.Add('release.yml workflow_dispatch must declare version and publish inputs.') }
    else {
        $names = @(Get-Children $release $inputs | ForEach-Object Key | Sort-Object)
        if (($names -join ',') -cne 'publish,version') { $failures.Add("release.yml workflow_dispatch inputs must be exactly version and publish, not: $($names -join ', ').") }
        $expected = @{ version = @{ type = 'string'; required = 'true' }; publish = @{ type = 'boolean'; required = 'true'; default = 'false' } }
        foreach ($name in $expected.Keys) {
            $declaration = Get-Child $release $inputs $name
            if ($null -eq $declaration) { continue }
            foreach ($property in $expected[$name].Keys) {
                $actual = Get-Child $release $declaration $property
                $value = if ($null -ne $actual) { $actual.Value } else { '<missing>' }
                if ($value -cne $expected[$name][$property]) { $failures.Add("release.yml input $name must set ${property}: $($expected[$name][$property]), not $value.") }
            }
        }
    }
}

# Only the two reviewed signing jobs may name release-signing, and only for main.
$jobsByWorkflow = @{}
$found = [Collections.Generic.List[string]]::new()
foreach ($workflow in $workflows) {
    $jobsByWorkflow[$workflow.Name] = Get-Jobs $workflow
    foreach ($job in $jobsByWorkflow[$workflow.Name]) {
        $environment = Get-JobKey $job 'environment'
        if ($null -eq $environment) { continue }
        $location = "$($workflow.Name):$($environment.Line + 1)"
        if (-not $environment.Raw -or $environment.Block) { $failures.Add("$location job '$($job.Name)' must use the scalar form environment: <name>."); continue }
        if ($environment.Value.Contains('${{')) { $failures.Add("$location job '$($job.Name)' uses an expression-valued environment; name it literally."); continue }
        if ($environment.Value -ine $signingEnvironment) { continue }
        $found.Add("$($workflow.Name):$($job.Name)")
        if ($signingJobs -cnotcontains "$($workflow.Name):$($job.Name)") {
            $failures.Add("$location job '$($job.Name)' names $signingEnvironment; only $($signingJobs -join ' and ') may.")
        }
        $condition = Get-JobKey $job 'if'
        if ($null -eq $condition -or -not $condition.Raw -or $condition.Block) {
            $failures.Add("$location job '$($job.Name)' needs a single-line if: that starts with $mainCondition.")
            continue
        }
        $text = $condition.Value
        if (-not ($text -ceq $mainCondition -or $text.StartsWith("$mainCondition && ", [StringComparison]::Ordinal))) {
            $failures.Add("$($workflow.Name):$($condition.Line + 1) job '$($job.Name)' if: must start with $mainCondition, optionally followed by && conditions: $text")
        }
        if ($text.Contains('||')) { $failures.Add("$($workflow.Name):$($condition.Line + 1) job '$($job.Name)' if: must not use ||, which can bypass the main check.") }
        if ($text -match 'refs/tags') { $failures.Add("$($workflow.Name):$($condition.Line + 1) job '$($job.Name)' if: must not allow refs/tags.") }
    }
}
foreach ($expected in $signingJobs | Where-Object { $found -cnotcontains $_ }) {
    $failures.Add("$expected must declare environment: $signingEnvironment.")
}

# release.yml resolves the release from the dispatch, and only publish drafts a release.
$releaseJobs = $jobsByWorkflow['release.yml']
$package = $releaseJobs | Where-Object Name -ceq 'package'
if ($null -eq $package) { $failures.Add('release.yml has no package job.') }
else {
    if (-not $package.Text.Contains('./eng/Resolve-InstallerRelease.ps1')) { $failures.Add('release.yml package job must call ./eng/Resolve-InstallerRelease.ps1.') }
    if ($package.Text -notmatch '(?m)^\s+fetch-depth: 0\s*$') { $failures.Add('release.yml package job checkout must use fetch-depth: 0 so existing tags are checked.') }
}
$publish = $releaseJobs | Where-Object Name -ceq 'publish'
if ($null -eq $publish) { $failures.Add('release.yml has no publish job.') }
else {
    $needs = Get-JobKey $publish 'needs'
    if ($null -eq $needs -or @(ConvertFrom-FlowSequence $needs.Raw) -cnotcontains 'sign') { $failures.Add('release.yml publish job must need the sign job.') }
    $condition = Get-JobKey $publish 'if'
    if ($null -eq $condition -or -not $condition.Value.Contains("needs.package.outputs.eligible == 'true'")) {
        $failures.Add("release.yml publish job if: must require needs.package.outputs.eligible == 'true'.")
    }
    elseif ($condition.Value.Contains('||')) { $failures.Add('release.yml publish job if: must not use ||.') }
    if ($publish.Text -notmatch '--draft(?![=\w-])') { $failures.Add('release.yml publish job must create the release with --draft.') }
    if ($publish.Text -match '--draft=') { $failures.Add('release.yml publish job must not set --draft to a value.') }
    if ($publish.Text -notmatch '--target[''"]?(?:\s*,\s*|\s+)\$env:GITHUB_SHA') { $failures.Add('release.yml publish job must target the run commit with --target $env:GITHUB_SHA.') }
}
foreach ($workflow in $workflows) {
    foreach ($entry in $workflow.Entries) {
        $location = "$($workflow.Name):$($entry.Line + 1)"
        if ($entry.Key -ceq 'permissions' -and $entry.Raw -and $entry.Value -match 'write') {
            $failures.Add("$location grants $($entry.Value); list each permission instead.")
        }
        if ($entry.Key -cne 'contents' -or $entry.Value -cne 'write') { continue }
        $inPublish = $workflow.Name -ceq 'release.yml' -and $null -ne $publish -and $entry.Line -gt $publish.FirstLine -and $entry.Line -lt $publish.EndLine
        if (-not $inPublish) { $failures.Add("$location grants contents: write; only release.yml's publish job may.") }
    }
}

if ($failures.Count -gt 0) { throw "Release workflow policy failed:`n$(($failures | ForEach-Object { "- $_" }) -join "`n")" }
Write-Host "Release workflow policy holds for $($workflows.Count) workflows."
