# Build and run the development preview

The first increment implements source settings through a Windows WPF app and a
CLI backed by one .NET library. No catalog client, package manager, self-update
executor, administrator policy engine, authentication provider, or SDK integration
is implemented yet. The other three navigation pages describe future work.

## Prerequisites and validation

Use Windows and the .NET SDK pinned in `global.json`. A SpatialAnalyzer install,
SA license, Windows App SDK runtime, and proprietary SDK files are not required.
Run these commands from the repository root:

```powershell
dotnet restore Briosa.Installer.slnx --locked-mode
dotnet build Briosa.Installer.slnx -c Release --no-restore
dotnet test tests/Briosa.Installer.Tests -c Release --no-build --no-restore
dotnet run --project tests/Briosa.Installer.App.Smoke -c Release --no-build --no-restore
```

Core/CLI tests cover invalid sources and schemas, whole-document precedence,
independent updater settings, explicit missing files, external edits, stale
saves, and CLI/engine interoperability. The WPF smoke harness loads the actual
resources/control tree, exercises source save/reload and navigation with temporary
settings, and never displays a native window or controls an existing application.
It is not a substitute for interactive keyboard, screen-reader, high-contrast,
scaling, and supported-Windows validation before release.

To render the same WPF tree for visual inspection, pass one output PNG path to
the smoke harness. Its sources are fictitious; its settings-file label is replaced
with a symbolic user path. It does not capture the desktop.

## Start the app

```powershell
dotnet run --project src/Briosa.Installer.App -c Release --no-build --no-restore
```

The app starts on Sources. Enter an HTTPS catalog URL or an absolute Windows
file/share path. Installer updates share the server catalog unless you clear
"Use the same source" and enter a separate catalog. Save persists both settings;
Reload loads the file again; View JSON displays the current editor values for
copying. No connection check or background network request is made.

The user settings file is `%LOCALAPPDATA%\Briosa\Installer\settings.json`.
Saving optional machine defaults creates a user settings file; it does not edit
`%PROGRAMDATA%\Briosa\Installer\settings.json`. These are defaults, not enforced
administrator policy. If the selected file is invalid, repair it externally and
reload; the GUI will not silently replace it.

The public release catalog is not published yet, so the development preview
requires an explicit location. Do not substitute a guessed GitHub endpoint.
The released product must restore the agreed public default and first-use source
selection once its actual catalog contract and hosting exist.

## Use the CLI

```powershell
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings set --server-catalog https://artifacts.example.com/artifactory/briosa-servers/catalog.json
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings set --installer-catalog https://artifacts.example.com/artifactory/briosa-installer/catalog.json
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings show
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings validate
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings set --same-source
```

The URLs are illustrative. `settings set` preserves an existing update override
unless `--same-source` is explicitly supplied. `show` emits the selected JSON
document; `validate` checks configuration syntax and does not prove access.

All commands accept `--config <file>`. An explicitly selected missing file fails
instead of falling back. To intentionally create one, use:

```powershell
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings init --config "$env:TEMP\briosa-preview-settings.json" --server-catalog https://artifacts.example.com/artifactory/briosa-servers/catalog.json
dotnet run --project src/Briosa.Installer.App -c Release --no-build -- --config "$env:TEMP\briosa-preview-settings.json"
```

`init` refuses to overwrite an existing file. Exit codes are 0 (success), 2
(invalid arguments/configuration or file error), 3 (setup required), and 4
(save conflict). No raw configuration values appear in failure messages.

## Save behavior and current boundaries

The writer uses a shared GUI/CLI lock, a content revision, a flushed temporary
file, and replacement within the same directory. A stale save returns a conflict.
Direct external edits are checked by content rather than timestamps. This is
not an OS-wide compare-and-swap guarantee against arbitrary programs replacing
the file in the interval between the final check and replacement. Coordinate
unrelated writers; the GUI prompts before discarding unsaved edits.

A `.lock` sidecar remains beside the settings file; no process holds it when
idle. A failed write cleans up its temporary file where access permits. There is
no persistent activity log, client registry, or project inventory.

This is a framework-dependent development build, not the final offline delivery
artifact. Packaging, signing, authentication, source policy, network validation,
download integrity, installation recovery, and self-update all need their own
reviewed implementation and tests.
