[CmdletBinding()]
param([Parameter(Mandatory)][string]$SetupPath, [Parameter(Mandatory)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$setup = Get-Item -LiteralPath $SetupPath
if ($setup.Name -cnotmatch '^briosa-installer-[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?-win-x64-setup\.exe$') { throw 'Unexpected setup filename.' }
$signature = Get-AuthenticodeSignature -LiteralPath $setup.FullName
if ($signature.Status -ne 'Valid' -or $null -eq $signature.TimeStamperCertificate -or $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -cne 'David Lucas') { throw 'Setup must have a valid timestamped release-publisher signature.' }
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$destination = Join-Path $OutputDirectory $setup.Name
if (Test-Path -LiteralPath $destination) { throw 'Setup release files are immutable.' }
Copy-Item -LiteralPath $setup.FullName -Destination $destination
$digest = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText([IO.Path]::GetFullPath("$destination.sha256"), "$digest  $($setup.Name)`n", [Text.UTF8Encoding]::new($false))
