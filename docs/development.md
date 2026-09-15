# Build, test, and package Briosa Installer

The WPF app, CLI, and bootstrap launcher share a .NET 10 engine. They manage source
settings, authentication, signed catalogs, side-by-side packages, verification,
repair, removal, recovery, installer updates, read-only SDK diagnostics, reviewed
vendor SDK registration changes, and activity.
See the [review guide](review-guide.md) and [administration guide](administration.md).

## Build and validate

Use Windows x64 and the .NET SDK pinned in `global.json`. Ordinary builds and tests
require no SpatialAnalyzer installation, license, or proprietary SDK binaries.
From this repository:

```powershell
dotnet restore Briosa.Installer.slnx --locked-mode
dotnet build Briosa.Installer.slnx -c Release --no-restore
dotnet test tests/Briosa.Installer.Tests -c Release --no-build --no-restore
dotnet run --project tests/Briosa.Installer.App.Smoke -c Release --no-build --no-restore
dotnet run --project src/Briosa.Installer.App -c Release --no-build --no-restore
```

Tests cover configuration conflicts, independent updater routing, credential
isolation, signatures/expiry, malformed inputs, archive containment, integrity
failures, side-by-side installation, repair, recovery, installer selection,
bootstrap refresh, and in-use removal. The process test starts and stops only its
own small fixture. A Credential Manager test round-trips one unique inert credential
and deletes it. Machine ACL tests use in-memory security objects.

The WPF harness loads the real resources/control tree and exercises the full workflow
with signed inert fixtures, temporary directories, and fake SDK observations.
It does not display a native window or control another application. Pass up to three
PNG paths to render the server inventory, source settings, and compact inventory;
light/dark variants are also saved beside the first output. The harness exercises
automatic source persistence, stale requests, filtered recovery, semantic update/rollback
states, safe Activity metadata, automatic credential saves, rapid edits, close
flushing, concurrent-writer protection, write-failure recovery, and compact layout.
It also checks resolved selection/text colors after live theme changes, text contrast,
and navigation, focus, and control boundaries in both themes. Additional renders
show primary actions, source settings, and installer updates in each palette.
This is not interactive accessibility,
clean-Windows, real Artifactory/proxy, or licensed-SA validation.

## Produce a self-contained distribution

```powershell
./eng/Publish-Installer.ps1 -Version 0.1.0-review.7 -OutputDirectory ./artifacts/review
```

Use a new output directory/version; existing packages are immutable. The ZIP has
one product directory containing the WPF app, CLI, launcher, bundled runtime,
licenses, documentation, checksums, and manifest. It requires no network bootstrap
or separately installed .NET. Adjacent provenance matches the embedded manifest.
Package assembly does not contact SA or publish a release.

When production hosting and its approved key exist, supply `-PublicSettingsFile`
with a valid settings document containing the actual HTTPS public catalog and
publisher public key. These values seed first-use source editing. Engineers can
reconfigure the normal installer without an enterprise-customized build.
Production release workflows now use the reviewed public defaults and timestamp
first-party code with Azure Artifact Signing before rebuilding package hashes.
See the [release-signing runbook](https://github.com/spatialanalyzer/briosa/blob/main/docs/maintainers/release-signing.md)
for identity configuration, manual validation, pinned tooling, and key maintenance.
Ordinary CI and the local command above remain unsigned and need no Azure access.

## Offline review demo

The companion `briosa` checkout supplies the shared catalog producer and signer:

```powershell
./eng/New-ReviewDemo.ps1 -OutputDirectory ./artifacts/demo -BriosaRepository ../briosa -InstallerPackage ./artifacts/review/briosa-installer-0.1.0-review.7-win-x64.zip
./artifacts/review/briosa-installer-0.1.0-review.7-win-x64/Briosa.Launcher.exe --config "$PWD/artifacts/demo/settings.json" --store "$PWD/artifacts/demo/store"
```

This creates three inert server packages across two invented SA targets plus the
real locally built installer. A temporary RSA key signs the local catalog; its
private key is deleted. The demo expires after seven days: generate a new directory
after expiry. Never launch the inert server files or treat demo versions as supported
SA releases. The older files in `examples/` demonstrate unsigned metadata only.

Validate the packaged CLI, signed catalog interoperability, install/verify/repair/
remove, installer activation, bootstrap refresh, preserved settings, and launcher
resolution without opening a window:

```powershell
./eng/Test-InstallerPackage.ps1 -PackageDirectory ./artifacts/review/briosa-installer-0.1.0-review.7-win-x64 -BriosaRepository ../briosa
```

## Configuration and operational behavior

The app opens on Installations. Navigation continues with SDK Setup, Activity,
and Settings. Settings owns every user-configurable value accepted by
`settings.json`: server and optional updater catalogs, source sharing,
authentication modes, publisher keys, and the appearance theme. Authentication/publisher dialogs are
part of that page. The application maintains schema-version metadata.
Future settings must include GUI controls using the same validation and persistence.

SDK Setup loads local installation and registry evidence on every page visit,
independently of package sources. Refresh repeats the scan on demand. A scan does
not block navigation or unrelated settings; repeated visits share an in-progress
read. Results arriving after the window closes are ignored. Change SDK opens a
separate selection and review flow before requesting Windows elevation for the
installed vendor's registration command. See [SDK registration](sdk-registration.md)
for prerequisites, CLI review commands, verification, and recovery. Ordinary tests
use a fake registration environment and never change machine registration.

Installations contains only gRPC server downloads and installed servers, even when
the source catalog also contains installers. Settings owns installer update checks,
version comparison, reviewed acquisition/selection/restart, and downloaded-installer
maintenance. Each catalog view has independent results and cancellation; source
edits invalidate both. Update labels use semantic version precedence (including
prereleases, ignoring build metadata); older releases remain explicit rollback choices.
One package-scope control in Settings → Advanced governs both inventories. Links
from server installations and installer updates open that same control. Local
inventory and the saved server catalog load automatically when Installations opens.
Returning to the page retains completed results; Refresh reloads both inventories.
Saved source changes are loaded when the page is next shown, and a tested unchanged
catalog can be reused immediately. Failed reads wait for an explicit retry.

The UI uses the built-in WPF Fluent resources with a saved System/Light/Dark theme, named Fluent
base styles, and the [approved Briosa brand palette](branding.md). The inventory
groups installed and
available servers by exact SA target; details and maintenance controls appear in
context. Settings separates Package sources, Installer updates, Appearance, and Advanced.
Settings persist automatically. Text fields debounce for 450 ms and flush on
focus loss, navigation, and close. Invalid input preserves the last valid source;
appearance can persist before setup and while source text is incomplete. Access
and publisher fields also apply automatically. Failed writes expose recovery actions
without overwriting external edits. Clean windows reload external changes on activation.
See the [redesign decision](architecture/0004-installer-ux.md) for interaction and
accessibility requirements, evidence, and remaining manual validation.

Settings use a shared GUI/CLI lock, content revision, flushed temporary file, and
replacement. Coordinate external writers: this is not an OS-wide compare-and-swap
primitive against arbitrary programs. An explicit missing/invalid configuration
fails closed. The [administration guide](administration.md) describes precedence.

Initial loading and Refresh read the saved server source. Install/repair read it again and
require the reviewed digest. Catalog reads are bounded to 30 seconds including
response bodies; acquisition has a 30-minute deadline and size limits. Cancel
stops before commit where possible. A blocked OS file/share open can outlive
cooperative cancellation; no alternate source is contacted and the transaction
must finish or release its store lock before another mutation. Directory commit
itself completes or is recovered.

The CLI's `--help` lists all commands. JSON commands emit one JSON document.
Exit codes: 0 success; 2 invalid settings/catalog arguments or file error;
3 setup required; 4 settings save conflict; 5 catalog failure; 6 management failure;
130 cancellation. Package changes require `--yes`; acquisition also requires
`--catalog-sha256` from the reviewed catalog. Secrets use standard input or a hidden
prompt, never command-line arguments.

See [the package-management decision](architecture/0003-package-management.md)
for security boundaries, transaction semantics, and remaining release validation.
