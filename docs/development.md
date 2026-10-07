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

To validate real server packages from a companion Briosa checkout without starting
the server or SDK, supply a directory containing their ZIPs, checksums, and adjacent
provenance files, plus the behavioral contract major approved for that set:

```powershell
./eng/Test-ServerPackages.ps1 -ArtifactDirectory ../briosa/artifacts/packages -BriosaRepository ../briosa -ExpectedCompatibilityMajor 2
```

The harness creates a disposable publisher key, signed local catalog, settings, and
private store. It checks schema-3 admission, receipts, coexistence, verification,
exact-package repair, and removal for at least two server products. Pass `-CliPath`
to exercise the CLI from an extracted self-contained installer distribution. This
also covers protocol-only server updates such as the MP argument naming migration.
The installer needs no application upgrade for this migration and does not alter
consuming applications' client dependencies.

`-ExpectedCompatibilityMajor` has no default. It checks the supplied package set;
the installer does not negotiate the server's gRPC contract. The package engine
accepts any positive uint32 compatibility major with a uint32 revision and preserves
the manifest unchanged, so a later major installs side by side without an installer
change. It rejects zero, negative, fractional, and overflowing coordinates, unknown
server manifest schemas, another SA target, and provenance that differs from the
embedded manifest; a rejected package leaves earlier products and registrations intact.
Language clients select a compatible runtime through Briosa's
[shared behavioral contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/client-library-behavioral-contract.md).
Inert installation does not establish SDK readiness or compatibility with an
application's existing client dependency.

CI and release packaging run the harness against the reviewed real packages in
[`eng/server-package-candidates.json`](../eng/server-package-candidates.json): a
public Briosa release, its approved compatibility major, and each exact-target ZIP
and provenance digest. It currently lists Server 0.9.2 (major 2) for SA
`2024.1.0508.5` and `2026.1.0529.7`. `eng/Test-ServerPackageCandidates.ps1`
downloads absent files from that GitHub release (about 130 MB per target), verifies
the digests and that each provenance `sourceRevision` equals the supplied Briosa
checkout, requires the packages to fail at a different major, and then runs the
harness. The workflows check out that source revision for the catalog tools and read
no private repository. Changing the candidates, for example to a major-3 server,
updates the approved major, digests, and workflow pin together for review.

```powershell
./eng/Test-ServerPackageCandidates.ps1 -BriosaRepository ../briosa -ArtifactDirectory ./artifacts/server-candidates
```

Workflows check out Briosa tools only at full commit SHAs reachable from Briosa
`main`; `eng/Test-BriosaSupportPins.ps1` enforces this in CI and before release
packaging. The release signing pin `c19f64d` is tree-identical to the previously
reviewed `be5f50b`, which was squash-merged as spatialanalyzer/briosa#174. Never
float these pins; move one only after reviewing the Briosa scripts it executes.

Local validation on 2026-09-25 passed against unpublished `0.9.0-dev.2` server
packages from Briosa commit `82b6ce51cb199a4e04ef3c142af4b27a8c34d1e4`
(compatibility major 2, revision 0), for both SA `2024.1.0508.5` and
`2026.1.0529.7`. A disposable signed catalog and private store exercised receipts,
coexistence, verification, and exact-package repair. Test installation records
were removed during cleanup; no server or SDK was launched and SDK registration
was unchanged. The locked Release build, all 178 core/CLI tests, and the hidden
WPF smoke harness also passed. These results validate installer handling of the
candidate manifests, not a published release or licensed SA compatibility.

```powershell
./eng/Publish-Installer.ps1 -Version 0.1.0-review.7 -OutputDirectory ./artifacts/review
```

Use a new output directory/version; existing packages are immutable. The ZIP has
one product directory containing the WPF app, CLI, launcher, bundled runtime,
licenses, documentation, checksums, and manifest. It requires no network bootstrap
or separately installed .NET. Adjacent provenance matches the embedded manifest.
Package assembly does not contact SA or publish a release.

Supply `-PublicSettingsFile`
with a valid settings document containing the actual HTTPS public catalog and
publisher public key. These values back the explicit first-use public-source choice. Engineers can
reconfigure the normal installer without an enterprise-customized build.
Production release workflows now use the reviewed public defaults and timestamp
first-party code with Azure Artifact Signing before rebuilding package hashes.
See the [release-signing runbook](https://github.com/spatialanalyzer/briosa/blob/main/docs/maintainers/release-signing.md)
for identity configuration, manual validation, pinned tooling, and key maintenance.
Ordinary CI and the local command above remain unsigned and need no Azure access.

## Build conventional Windows setup

The setup wraps the complete distribution above. It installs per-user and adds
the launcher shortcut and Windows Installed apps uninstaller:

```powershell
./eng/Install-SetupCompiler.ps1
./eng/Build-Setup.ps1 -PackageDirectory ./artifacts/review/briosa-installer-0.1.0-review.7-win-x64 -OutputDirectory ./artifacts/setup -TestIdentity
./eng/Test-Setup.ps1 -SetupPath ./artifacts/setup/briosa-installer-0.1.0-review.7-win-x64-setup.exe -PackageDirectory ./artifacts/review/briosa-installer-0.1.0-review.7-win-x64 -TestIdentity
```

Local tests use a separate product/shortcut identity and temporary store. Production
identity installation tests run only on disposable CI accounts. Release workflows
sign the uninstaller, embed it in setup, sign setup, and run the same lifecycle test
with signature verification. They also exercise the complete final signed ZIP.
CI additionally installs an older setup-version fixture containing the same app
payload, upgrades it, verifies replacement of a damaged app file, and rejects a
subsequent setup downgrade. The fixture version override is restricted to unsigned
test identities; it cannot label a production setup with a different package version.
See [setup design](architecture/0005-windows-setup.md) for ownership and update rules.

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
