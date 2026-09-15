[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Compiler = (Join-Path $PSScriptRoot '../artifacts/tools/inno-7.1.0/ISCC.exe'),
    [ValidateSet('unsigned', 'prepare-signing', 'signed')][string]$Mode = 'unsigned',
    [switch]$TestIdentity,
    [string]$TestVersion
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackageDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
$version = $manifest.briosaVersion
if ($manifest.component -cne 'installer' -or $manifest.runtimeIdentifier -cne 'win-x64' -or $version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z.-]+)?$') { throw 'Invalid installer manifest.' }
if ($TestVersion) {
    if (-not $TestIdentity -or $Mode -ne 'unsigned' -or $TestVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+-setup-test$') { throw 'Version overrides are only for unsigned setup test fixtures.' }
    $version = $TestVersion
}
if ((& $Compiler --version) -notmatch '^7\.1\.0$') { throw 'Use the pinned Inno Setup 7.1.0 compiler.' }
$null = New-Item -ItemType Directory -Path (Join-Path $output 'uninstaller') -Force
$arguments = @("/DPackageRoot=$package", "/DProductVersion=$version", "/DNumericVersion=$(($version -split '-')[0])", "/DOutputRoot=$output")
if ($TestIdentity) { $arguments += '/DTestSuffix= (Setup Test)' }
if ($Mode -ne 'unsigned') { $arguments += '/DSignUninstaller=yes' }
$log = @(& $Compiler @arguments (Join-Path $PSScriptRoot 'setup/Briosa.iss') 2>&1)
$compilerExit = $LASTEXITCODE
$log | Set-Content -LiteralPath (Join-Path $output "compile-$Mode.log")
if ($Mode -eq 'prepare-signing') {
    $uninstaller = @(Get-ChildItem (Join-Path $output 'uninstaller') -File | Where-Object Extension -In '.e32', '.e64')
    if ($compilerExit -eq 0 -or $uninstaller.Count -ne 1 -or ($log -join "`n") -notmatch '(?i)(digitally sign|digital signature|not signed)') {
        $log | Write-Host
        throw 'The compiler did not produce exactly one uninstaller awaiting a signature.'
    }
    # Inno deliberately fails this first pass after writing the unsigned uninstaller.
    # We validated that expected outcome; do not leak its exit status to CI's wrapper.
    $global:LASTEXITCODE = 0
    Write-Output $uninstaller[0].FullName
    return
}
if ($compilerExit -ne 0) { $log | Write-Host; throw "Setup compilation failed ($compilerExit)." }
$setup = Join-Path $output "briosa-installer-$version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw 'Setup output is missing.' }
Write-Output $setup
