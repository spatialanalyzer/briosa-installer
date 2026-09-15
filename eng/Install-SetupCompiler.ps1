[CmdletBinding()]
param([string]$Destination = (Join-Path $PSScriptRoot '../artifacts/tools/inno-7.1.0'))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$destinationPath = [IO.Path]::GetFullPath($Destination)
$compiler = Join-Path $destinationPath 'ISCC.exe'
if (Test-Path -LiteralPath $compiler) {
    if ((& $compiler --version) -notmatch '^7\.1\.0$') { throw 'Unexpected setup compiler version.' }
    Write-Output $compiler
    return
}
$null = New-Item -ItemType Directory -Path $destinationPath -Force
$download = Join-Path $destinationPath 'innosetup-7.1.0-x64.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' -OutFile $download
if ((Get-FileHash $download -Algorithm SHA256).Hash -cne '0362A383ED217D4C4239B5933866DD96D3EB2102737DA92F80F6057A4B40DF2F') { throw 'Setup compiler download digest differs from the pinned release.' }
$signature = Get-AuthenticodeSignature $download
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'Pyrsys B.V.') { throw 'Setup compiler publisher verification failed.' }
$process = Start-Process -FilePath $download -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER', '/NOICONS', "/DIR=`"$destinationPath`"") -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $compiler)) { throw 'Setup compiler installation failed.' }
Write-Output $compiler
