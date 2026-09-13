[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$BriosaRepository
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath $PackageDirectory).Path
$testRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'Briosa.Packaged.Tests'))
$caseRoot = [IO.Path]::GetFullPath((Join-Path $testRoot ([Guid]::NewGuid().ToString('N'))))
$null = New-Item -ItemType Directory -Path $caseRoot -Force
$cli = Join-Path $package 'Briosa.Installer.Cli.exe'
function Invoke-Cli([string[]]$Arguments) {
    $result = & $cli @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Packaged CLI failed: $($Arguments[0]) $($Arguments[1])." }
    return ($result -join "`n")
}
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Check-Launcher([string]$Path, [string]$Store) {
    $start = [Diagnostics.ProcessStartInfo]::new($Path)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add('--check')
    $start.ArgumentList.Add('--store')
    $start.ArgumentList.Add($Store)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Owned launcher check timed out.' }
        Check ($process.ExitCode -eq 0) 'Packaged launcher resolution failed.'
    } finally { $process.Dispose() }
}
try {
    foreach ($notice in @('THIRD-PARTY-NOTICES.md', 'licenses/briosa-brand-LICENSE.txt', 'licenses/Inter-OFL.txt', 'licenses/briosa-brand-provenance.json')) {
        Check (Test-Path -LiteralPath (Join-Path $package $notice) -PathType Leaf) "Packaged license or provenance is missing: $notice"
    }
    $demo = Join-Path $caseRoot 'demo'
    & (Join-Path $PSScriptRoot 'New-ReviewDemo.ps1') -OutputDirectory $demo -BriosaRepository $BriosaRepository -InstallerPackage "$package.zip"
    $config = Join-Path $demo 'settings.json'
    $store = Join-Path $demo 'store'
    $initialSettingsHash = (Get-FileHash -LiteralPath $config).Hash
    $catalog = (Invoke-Cli @('catalog', 'list', '--component', 'server', '--config', $config)) | ConvertFrom-Json
    Check ($catalog.publisherVerification -eq 'verified' -and $catalog.packages.Count -eq 3) 'Signed server catalog did not verify.'
    $one = $catalog.packages[0].id
    $two = $catalog.packages[1].id
    $installedOne = (Invoke-Cli @('packages', 'install', '--component', 'server', '--id', $one, '--catalog-sha256', $catalog.catalogSha256, '--config', $config, '--store', $store, '--yes')) | ConvertFrom-Json
    $null = Invoke-Cli @('packages', 'install', '--component', 'server', '--id', $two, '--catalog-sha256', $catalog.catalogSha256, '--config', $config, '--store', $store, '--yes')
    $null = Invoke-Cli @('packages', 'verify', '--id', $one, '--store', $store, '--config', $config)
    [IO.File]::WriteAllText((Join-Path $installedOne.directory 'payload/README.txt'), 'Intentionally damaged inert fixture.')
    & $cli packages verify --id $one --store $store --config $config 2>$null | Out-Null
    Check ($LASTEXITCODE -eq 6) 'Corrupt installed file was not detected.'
    $null = Invoke-Cli @('packages', 'repair', '--component', 'server', '--id', $one, '--catalog-sha256', $catalog.catalogSha256, '--config', $config, '--store', $store, '--yes')
    $null = Invoke-Cli @('packages', 'verify', '--id', $one, '--store', $store, '--config', $config)
    $null = Invoke-Cli @('packages', 'remove', '--id', $one, '--store', $store, '--config', $config, '--yes')
    $remaining = (Invoke-Cli @('packages', 'list', '--store', $store, '--config', $config)) | ConvertFrom-Json -NoEnumerate
    Check ($remaining.Count -eq 1 -and $remaining[0].id -eq $two) 'Removal changed the other version.'
    $updates = (Invoke-Cli @('catalog', 'list', '--component', 'installer', '--config', $config)) | ConvertFrom-Json
    Check ($updates.publisherVerification -eq 'verified' -and $updates.packages.Count -eq 1) 'Installer catalog did not verify.'
    $id = $updates.packages[0].id
    $installedApp = (Invoke-Cli @('packages', 'install', '--component', 'installer', '--id', $id, '--catalog-sha256', $updates.catalogSha256, '--config', $config, '--store', $store, '--yes')) | ConvertFrom-Json
    $bootstrap = Join-Path $caseRoot 'bootstrap'
    Copy-Item -LiteralPath $package -Destination $bootstrap -Recurse
    $launcher = Join-Path $bootstrap 'Briosa.Launcher.exe'
    Check-Launcher $launcher $store
    $null = Invoke-Cli @('app', 'activate', '--id', $id, '--store', $store, '--bootstrap', $launcher, '--yes')
    Check-Launcher $launcher $store
    $selected = Get-Content -LiteralPath (Join-Path $store 'active-installer.json') -Raw | ConvertFrom-Json
    Check ($selected.id -eq $id) 'Installer selection was not preserved.'
    Check ((Get-FileHash -LiteralPath $launcher).Hash -eq (Get-FileHash -LiteralPath (Join-Path $installedApp.directory 'payload/Briosa.Launcher.exe')).Hash) 'Bootstrap digest differs.'
    $null = Invoke-Cli @('packages', 'verify', '--id', $id, '--store', $store, '--config', $config)
    $null = Invoke-Cli @('packages', 'verify', '--id', $two, '--store', $store, '--config', $config)
    Check ((Get-FileHash -LiteralPath $config).Hash -eq $initialSettingsHash) 'Installer update changed settings.'
    Write-Output 'Packaged workflow passed: signed catalog interop, two server versions, corruption detection, repair, isolated removal, real installer acquisition/selection, bootstrap refresh, launcher resolution, and preserved settings.'
} finally {
    $resolved = [IO.Path]::GetFullPath($caseRoot)
    if (-not $resolved.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the test root.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
