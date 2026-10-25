[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Ref,
    [Parameter(Mandatory)][string]$Commit,
    [Parameter(Mandatory)][AllowEmptyString()][string]$Version,
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

# Resolves the installer version and signing eligibility for release.yml.
# - Releases are dispatched from a branch. Only main is eligible to sign and draft a
#   release; any other branch is an unsigned dry run. Tag refs and other non-branch refs
#   fail: publishing the draft release creates the v<version> tag.
# - If v<version> already exists, it must point at the dispatched commit.
# Writes value and eligible to GITHUB_OUTPUT when it is set.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRoot = [IO.Path]::GetFullPath($RepositoryRoot)

# The pattern alone would accept one trailing newline, which would add a line to GITHUB_OUTPUT.
if ($Version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$' -or $Version.Contains("`n")) {
    throw "Version '$Version' is not a supported semantic version; use the installer version without v."
}
if ($Commit -cnotmatch '^[0-9a-f]{40}$') {
    throw "Commit '$Commit' is not a full commit SHA."
}
if ($Ref -clike 'refs/tags/*') {
    throw "Release ref '$Ref' is a tag. Dispatch installer releases from main; publishing the draft release creates the tag."
}
if ($Ref -cnotlike 'refs/heads/*') {
    throw "Release ref '$Ref' is not a branch; dispatch installer releases from main."
}
$eligible = $Ref -ceq 'refs/heads/main'
if (-not $eligible) {
    Write-Host "::warning::$Ref is not main; this dispatch is an unsigned dry run."
}

# An existing tag must already name this commit, for example one pushed before the draft.
$tag = "v$Version"
$tagged = @(& git -C $resolvedRoot rev-parse --verify --quiet "refs/tags/$tag^{commit}")
switch ($LASTEXITCODE) {
    0 {
        if ($tagged.Count -ne 1 -or $tagged[0] -cne $Commit) {
            throw "Tag $tag already points to $($tagged -join ' '), not $Commit."
        }
    }
    1 { }
    default { throw "Could not check for an existing $tag tag (git exit code $LASTEXITCODE)." }
}

Write-Host "Installer version $Version from $Ref at $Commit; eligible to sign: $eligible."

if ($env:GITHUB_OUTPUT) {
    "value=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "eligible=$($eligible.ToString().ToLowerInvariant())" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}

# git rev-parse reports a missing tag with exit code 1; do not leak it to the caller.
$global:LASTEXITCODE = 0
[pscustomobject]@{ Version = $Version; Tag = $tag; Eligible = $eligible }
