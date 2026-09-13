[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/packages'),
    [string]$PublicSettingsFile
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$name = "briosa-installer-$Version-win-x64"
$packageRoot = Join-Path $outputRoot $name
$zipPath = Join-Path $outputRoot "$name.zip"
if ((Test-Path -LiteralPath $packageRoot) -or (Test-Path -LiteralPath $zipPath)) { throw 'Choose a new output directory; existing package artifacts are immutable.' }
$null = New-Item -ItemType Directory -Path $packageRoot -Force
$buildRoot = Join-Path $outputRoot "$name.build"
foreach ($project in @('Briosa.Installer.App', 'Briosa.Installer.Cli', 'Briosa.Launcher')) {
    $destination = Join-Path $buildRoot $project
    $projectPath = Join-Path $repositoryRoot "src/$project/$project.csproj"
    & dotnet restore $projectPath --locked-mode -r win-x64
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $project." }
    & dotnet publish $projectPath -c Release -r win-x64 --self-contained true --no-restore -o $destination `
        "-p:Version=$Version" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $project." }
    foreach ($file in Get-ChildItem -LiteralPath $destination -File) {
        $target = Join-Path $packageRoot $file.Name
        if (Test-Path -LiteralPath $target) {
            if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Publish outputs contain conflicting shared files.' }
        } else { Copy-Item -LiteralPath $file.FullName -Destination $target }
    }
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $packageRoot 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-DesktopShortcut.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/review-guide.md') -Destination (Join-Path $packageRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/administration.md') -Destination (Join-Path $packageRoot 'administration.md')
$architecture = Join-Path $packageRoot 'architecture'
$null = New-Item -ItemType Directory -Path $architecture
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs/architecture/0003-package-management.md') -Destination $architecture
if ($PublicSettingsFile) {
    & (Join-Path $packageRoot 'Briosa.Installer.Cli.exe') settings validate --config ([IO.Path]::GetFullPath($PublicSettingsFile))
    if ($LASTEXITCODE -ne 0) { throw 'Public settings failed the shared configuration validator.' }
    $defaults = Get-Content -LiteralPath $PublicSettingsFile -Raw | ConvertFrom-Json
    if ($defaults.schemaVersion -ne 1 -or $defaults.source.catalog -notmatch '^https://' -or -not $defaults.source.publisherKey) { throw 'Public defaults require the actual HTTPS catalog and publisher public key.' }
    Copy-Item -LiteralPath $PublicSettingsFile -Destination (Join-Path $packageRoot 'public-source.json')
}
$nugetLine = (& dotnet nuget locals global-packages --list) -join ''
if ($LASTEXITCODE -ne 0) { throw 'Could not locate runtime license material.' }
$nugetRoot = ($nugetLine -split ': ', 2)[1].Trim()
$licenseRoot = Join-Path $packageRoot 'licenses'
$null = New-Item -ItemType Directory -Path $licenseRoot
$brandRoot = Join-Path $repositoryRoot 'src/Briosa.Installer.App/Assets/Brand'
Copy-Item -LiteralPath (Join-Path $brandRoot 'LICENSE') -Destination (Join-Path $licenseRoot 'briosa-brand-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $brandRoot 'fonts/OFL.txt') -Destination (Join-Path $licenseRoot 'Inter-OFL.txt')
Copy-Item -LiteralPath (Join-Path $brandRoot 'provenance.json') -Destination (Join-Path $licenseRoot 'briosa-brand-provenance.json')
foreach ($runtime in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Briosa.Installer.App/obj/project.assets.json') -Raw | ConvertFrom-Json
    $dependencies = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
    $dependency = $dependencies | Where-Object name -EQ $runtime | Select-Object -First 1
    $runtimeVersion = ($dependency.version.Trim('[', ']') -split ',')[0].Trim()
    $versionRoot = Join-Path (Join-Path $nugetRoot $runtime) $runtimeVersion
    $notices = @(Get-ChildItem -LiteralPath $versionRoot -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD[-.]?PARTY[-.]?NOTICES(\.TXT)?)$')
    if (-not ($notices | Where-Object Name -Match '^LICENSE')) { throw "Missing runtime license for $runtime." }
    foreach ($notice in $notices) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseRoot "$runtime-$($notice.Name)")
    }
}
$manifest = [ordered]@{ schemaVersion = 1; component = 'installer'; artifactName = $name; briosaVersion = $Version; runtimeIdentifier = 'win-x64'; selfContained = $true }
$manifestPath = Join-Path $packageRoot 'manifest.json'
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
$checksums = Get-ChildItem -LiteralPath $packageRoot -File -Recurse | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($packageRoot, $_.FullName).Replace('\', '/')
    "$((Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant())  $relative"
} | Sort-Object
[IO.File]::WriteAllText((Join-Path $packageRoot 'files.sha256'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName) {
        $relative = "$name/" + [IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$digest = (Get-FileHash -LiteralPath $zipPath).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$digest  $name.zip`n")
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $outputRoot "$name.provenance.json")
Write-Output "Created complete offline Windows x64 package: $zipPath"
