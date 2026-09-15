[CmdletBinding()]
param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string]$OutputPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$file = (Resolve-Path -LiteralPath $FilePath).Path
$output = [IO.Path]::GetFullPath($OutputPath)
$parent = [IO.Path]::GetDirectoryName($output)
# Azure resolves each catalog entry relative to the catalog's own directory.
$relative = [IO.Path]::GetRelativePath($parent, $file)
if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or $relative.Contains("`n") -or $relative.Contains("`r")) {
    throw 'Signing files must be inside the file-list directory.'
}
[IO.File]::WriteAllText($output, "$relative`n", [Text.UTF8Encoding]::new($false))
