# Build and run the development preview

The development app and CLI share source settings and read-only catalog browsing
through one .NET library. Sources and Installations are functional; SDK setup and
Activity describe future work. Package acquisition, installation, self-update
execution, administrator policy, authentication, and SDK integration remain
subsequent work.

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
saves, and CLI/engine interoperability. Catalog tests cover malformed metadata,
unsafe paths, conflicting identities, independent source routing, redirects,
bounded streaming, cancellation, and offline reads. The WPF smoke harness loads
the actual resources/control tree and exercises settings, catalog selection,
package previews, and cancellation of stale results using temporary settings and
a fake HTTP handler. It never displays a native window or controls an existing application.
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
copying. Editing settings makes no connection check or background network request.

After saving, open Installations and select Server packages or Installer releases.
Refresh catalog explicitly reads the effective source; select a row and choose
Preview selected package to inspect its exact identity and declared payload
location, size, and digest. The preview also records the catalog content digest.
It does not download that payload or verify the publisher. Installer releases
have an independent version and no SA target; browsing them does not apply a
self-update. Changing sources, component, or settings clears old results.

For an immediately runnable offline demonstration, use the
[example walkthrough](../examples/README.md). All example releases are invented
fixtures with no payloads and imply no supported SA releases.

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
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- catalog list --component server
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- catalog list --component installer
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- catalog preview --component server --id <catalog-package-id>
```

The URLs are illustrative. `settings set` preserves an existing update override
unless `--same-source` is explicitly supplied. `show` emits the selected JSON
document; `validate` checks configuration syntax and does not prove access.
Replace `<catalog-package-id>` with an ID returned by `catalog list`. Catalog
commands explicitly read the selected catalog and emit JSON. Preview performs
a fresh read and does not retrieve package or provenance payloads. JSON reports
`publisherVerification: "notPerformed"`; a preview always has `canInstall: false`.

All commands accept `--config <file>`. An explicitly selected missing file fails
instead of falling back. To intentionally create one, use:

```powershell
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings init --config "$env:TEMP\briosa-preview-settings.json" --server-catalog https://artifacts.example.com/artifactory/briosa-servers/catalog.json
dotnet run --project src/Briosa.Installer.App -c Release --no-build -- --config "$env:TEMP\briosa-preview-settings.json"
```

`init` refuses to overwrite an existing file. Exit codes are 0 (success), 2
(invalid arguments/configuration or file error), 3 (setup required), and 4
(save conflict), 5 (catalog error), and 130 (catalog read cancelled). Ctrl+C
cancels CLI catalog reads. No raw configuration values appear in failure messages.

## Catalog access boundaries

Only an explicit Refresh catalog or CLI catalog command reads a source. The
reader accepts HTTPS and configured absolute Windows file/share paths, caps
metadata at 1 MiB and 1,000 packages, and bounds a read to 30 seconds including
the response body. Cancel returns control without keeping late results. A blocked
operating-system file/share open can outlive that caller wait; no payload or
machine mutation is scheduled when it eventually completes.

HTTP redirects, encoded responses, malformed catalogs, and conflicting identities
are rejected. Failures never switch to another source. Each refresh reads afresh;
there is no cross-source cache. HTTPS uses ordinary certificate validation and
system proxy behavior. HTTP sends no automatic Windows credentials or cookies
and has no authentication provider: 401/403 responses produce a controlled
diagnostic. Authenticated Artifactory/proxy support still needs implementation and
testing; configuring a mirror alone does not claim that capability.
Windows handles access to configured file shares using the current user's normal
filesystem context; the app does not implement a separate share sign-in flow.

Artifact references are relative children of the selected catalog's directory,
so an unchanged catalog can be copied with its payload layout to another mirror.
They are declarations until verified acquisition exists. The implementation
rejects traversal, absolute URLs, and ambiguous Windows path segments. It does
not treat lexical containment as proof of a filesystem junction's destination.

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
artifact. Packaging, signing, authentication, source policy, verified payload
acquisition, installation recovery, and self-update all need their own
reviewed implementation and tests.
