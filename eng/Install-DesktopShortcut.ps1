[CmdletBinding()]
param([switch]$Remove)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$launcher = Join-Path $PSScriptRoot 'Briosa.Launcher.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Briosa Installer.lnk'
if ($Remove) {
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
    Write-Output 'Start menu shortcut removed. Packages, settings, and SA are unchanged.'
    return
}
if (-not (Test-Path -LiteralPath $launcher)) { throw 'Run this script beside the published Briosa.Launcher.exe in its permanent folder.' }
$shell = New-Object -ComObject WScript.Shell
try {
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $launcher
    $link.WorkingDirectory = $PSScriptRoot
    $link.Description = 'Manage Briosa packages and installer versions'
    $link.Save()
} finally { $null = [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
Write-Output 'Start menu shortcut created. No SDK activation or registration maintenance was performed.'
