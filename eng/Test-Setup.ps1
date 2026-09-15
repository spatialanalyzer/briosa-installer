[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$PackageDirectory,
    [string]$UpgradeSetupPath,
    [switch]$TestIdentity,
    [switch]$RequireSignatures
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$setup = [IO.Path]::GetFullPath($SetupPath)
$package = [IO.Path]::GetFullPath($PackageDirectory)
$suffix = if ($TestIdentity) { ' (Setup Test)' } else { '' }
$name = 'Briosa Installer' + $suffix
$registry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{712B370D-F4A1-470E-B080-25A8F02BC531}' + $suffix + '_is1'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) "$name.lnk"
if ((Test-Path -LiteralPath $registry) -or (Test-Path -LiteralPath $shortcut)) { throw 'An existing installation/shortcut occupies this identity. Use an isolated test account.' }
if (-not $TestIdentity -and $env:GITHUB_ACTIONS -ne 'true') { throw 'Production setup tests require a disposable CI account; use TestIdentity locally.' }
$testParent = Join-Path ([IO.Path]::GetTempPath()) 'Briosa.Setup.Tests'
$root = Join-Path $testParent ([Guid]::NewGuid().ToString('N'))
$app = Join-Path $root 'application'
$null = New-Item -ItemType Directory -Path $root -Force
$sentinel = Join-Path $root 'retained-settings.json'
[IO.File]::WriteAllText($sentinel, '{"appearance":{"theme":"dark"}}')
$sentinelHash = (Get-FileHash $sentinel).Hash
function Assert-Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Assert-Signature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    Assert-Check ($signature.Status -eq 'Valid' -and $null -ne $signature.TimeStamperCertificate -and $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -ceq 'David Lucas') "Invalid release signature: $Path"
}
function Invoke-Setup([string]$Path, [string]$Log, [bool]$ExpectFailure = $false) {
    $process = Start-Process -FilePath $Path -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$app`"", "/LOG=`"$root/$Log`"") -WindowStyle Hidden -PassThru
    try {
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Owned setup test timed out; see $root/$Log" }
        Assert-Check (($process.ExitCode -eq 0) -ne $ExpectFailure) "Unexpected setup exit: $($process.ExitCode); see $root/$Log"
    } finally { $process.Dispose() }
}
function Assert-Payload {
    foreach ($line in Get-Content (Join-Path $package 'files.sha256')) {
        Assert-Check ($line -match '^([a-f0-9]{64})  (.+)$') 'Invalid packaged checksum.'
        Assert-Check ((Get-FileHash -LiteralPath (Join-Path $app $Matches[2])).Hash -ieq $Matches[1]) 'Setup changed a packaged file.'
    }
}
function Assert-Version([string]$Path) {
    Assert-Check ((Get-ItemProperty -LiteralPath $registry).DisplayVersion -ceq (Get-Item -LiteralPath $Path).VersionInfo.ProductVersion.Trim()) 'Installed apps version is incorrect.'
}
function Uninstall-Test {
    $uninstaller = Join-Path $app 'unins000.exe'
    if (Test-Path -LiteralPath $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$root/uninstall.log`"") -WindowStyle Hidden -Wait -PassThru
        Assert-Check ($process.ExitCode -eq 0) 'Uninstall failed.'
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while ((Test-Path -LiteralPath $uninstaller) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    }
}
try {
    if ($RequireSignatures) { Assert-Signature $setup }
    Invoke-Setup $setup 'install.log'
    Assert-Check (Test-Path -LiteralPath $registry) 'Installed apps registration is missing.'
    Assert-Check (Test-Path -LiteralPath $shortcut) 'Start menu shortcut is missing.'
    $shell = New-Object -ComObject WScript.Shell
    try { Assert-Check ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $app 'Briosa.Launcher.exe')) 'Shortcut bypasses the launcher.' }
    finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
    Assert-Payload
    Assert-Version $setup
    if ($RequireSignatures) { Assert-Signature (Join-Path $app 'unins000.exe') }
    $runningApplication = [Threading.Mutex]::new($false, 'Local\BriosaInstallerApplication')
    try {
        Invoke-Setup $setup 'running-install-rejected.log' $true
        Invoke-Setup (Join-Path $app 'unins000.exe') 'running-uninstall-rejected.log' $true
        Assert-Payload
        Assert-Version $setup
    } finally { $runningApplication.Dispose() }
    & (Join-Path $app 'Briosa.Installer.Cli.exe') --help | Out-Null
    Assert-Check ($LASTEXITCODE -eq 0) 'Installed CLI failed.'
    $launcher = Start-Process -FilePath (Join-Path $app 'Briosa.Launcher.exe') -ArgumentList @('--check', '--store', "`"$root/packages`"") -WindowStyle Hidden -Wait -PassThru
    Assert-Check ($launcher.ExitCode -eq 0) 'Installed launcher failed.'
    # Reinstall/repair preserves user-created files and external data.
    $userFile = Join-Path $app 'user-retained.txt'
    [IO.File]::WriteAllText($userFile, 'Unowned user data must survive uninstall.')
    Invoke-Setup $setup 'reinstall.log'
    Assert-Payload
    if ($UpgradeSetupPath) {
        $upgrade = [IO.Path]::GetFullPath($UpgradeSetupPath)
        Assert-Check ((Get-Item $upgrade).VersionInfo.ProductVersion.Trim() -cne (Get-Item $setup).VersionInfo.ProductVersion.Trim()) 'Upgrade requires a different setup version.'
        if ($RequireSignatures) { Assert-Signature $upgrade }
        [IO.File]::WriteAllText((Join-Path $app 'README.md'), 'An old application file must be replaced by upgrade.')
        Invoke-Setup $upgrade 'upgrade.log'
        Assert-Version $upgrade
        Assert-Payload
        Invoke-Setup $setup 'downgrade-rejected.log' $true
        Assert-Version $upgrade
        Assert-Payload
        Assert-Check (Test-Path -LiteralPath $userFile) 'Upgrade deleted unowned data.'
        Write-Output 'Setup upgrade, file replacement, and downgrade rejection passed.'
    }
    Uninstall-Test
    Assert-Check (-not (Test-Path -LiteralPath $registry)) 'Installed apps registration remained after uninstall.'
    Assert-Check (-not (Test-Path -LiteralPath $shortcut)) 'Start menu shortcut remained after uninstall.'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $app 'Briosa.Installer.exe'))) 'Application remained after uninstall.'
    Assert-Check (Test-Path -LiteralPath $userFile) 'Uninstall deleted unowned user data.'
    Assert-Check ((Get-FileHash $sentinel).Hash -eq $sentinelHash) 'Setup/uninstall changed retained settings.'
    Write-Output 'Setup passed: install, Installed apps, launcher shortcut, exact payload hashes, running-app protection, CLI/launcher, reinstall, uninstall, and retained data.'
} finally {
    Uninstall-Test
    $resolved = [IO.Path]::GetFullPath($root)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($testParent) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside setup test root.' }
    if (Test-Path -LiteralPath $registry) { throw "Test installation still registered; retain logs in $root for recovery." }
    # Keep the small setup logs for inspection; remove only this owned test tree's payloads.
    foreach ($child in @($app, (Join-Path $root 'packages'))) {
        $target = [IO.Path]::GetFullPath($child)
        if (-not $target.StartsWith($resolved + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup.' }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
    Write-Output "Setup logs: $root"
}
