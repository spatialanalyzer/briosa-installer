# Briosa Installer agent guide

Read this guide before changing repository source, documentation, build
infrastructure, or settings. User instructions for the current task take
precedence where they explicitly override this guide.

## Purpose and current state

This repository owns the independent Windows installer/manager for exact-SA-target
Briosa distributions. Its .NET 10/WPF review build includes a shared package engine,
CLI, launcher, configurable sources/authentication, publisher verification,
transactional package maintenance, installer selection, and read-only SDK evidence.
The maintainer selected .NET 10/WPF on 2026-09-06 and authorized completing the app
without pausing for each increment on 2026-09-12. Do not describe local review
artifacts as production releases or claim vendor repair or broader SDK compatibility.

## Boundaries

- Keep server/worker code, protobuf contracts, MP operations, published artifact
  contracts, runtime identity, and SDK compatibility enforcement in
  [briosa](https://github.com/spatialanalyzer/briosa).
- Shared client resolution and lifecycle behavior belong in Briosa's
  [client behavioral contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/client-library-behavioral-contract.md).
  Coordinate changes there; do not create a conflicting installer-only contract.
- Each running server remains fixed to one exact SA application release. The
  installer manages separate products; it is not a universal MP implementation.
- Current product scope excludes project profiles, consuming-application
  registration, client-dependency inventory, and application-impact tracking.
  Engineering teams manage their own dependencies and runtime selections.
  Installer package inventory and server operational state remain necessary;
  neither is a registry of consuming applications. Any later integration needs
  a separately accepted scope and design.
- Preserve the current runtime compatibility gates until an accepted change is
  implemented and validated. A newer-SDK policy in a proposal is not permission
  to bypass an exact runtime identity check or misstate an attested version.
- Published user documentation belongs in
  [briosa-docs](https://github.com/spatialanalyzer/briosa-docs). Keep design rationale
  here and link to authoritative contracts rather than duplicating them.

## Implementation expectations

- Use [briosa-brand](https://github.com/spatialanalyzer/briosa-brand) as the source
  of truth for artwork, palette, typography, size, and clear space. The app currently
  vendors approved v1 assets under `Assets/Brand`, with their original licenses and
  provenance. Keep the outlined lowercase wordmark intact; capitalize Briosa in
  prose. Preserve system high-contrast control colors and offline asset loading.
- Use the selected Layered Planes background: graphite in dark mode and silver/
  white in light mode, with neutral sidebars and opaque content surfaces. Blue
  and cyan are accents. Render the planes with native WPF vector geometry and
  controlled fills; do not reintroduce background PNGs or bitmap caches. Disable
  decorative background artwork in high contrast.
- Settings changes apply and persist automatically. Debounce text entry briefly,
  flush pending edits on focus loss/navigation/close, and serialize writes through
  the shared revision-checked store. Invalid input and save failures need clear
  recovery feedback; never overwrite another writer or claim a failed save succeeded.
  Appearance works before source setup and retains Windows contrast-theme priority.
  Theme-only saves must not reset package catalogs. Use the deeper charcoal shade
  of graphite for dark backgrounds, with subtle native vector planes.
  Theme accents use deep blue with white text in light mode and cyan with deep-blue
  text in dark mode. Pair selection fills and foregrounds, including text inputs;
  retain visible focus/selection indicators and recheck live theme changes.
  The selected navigation label specifically uses the sidebar charcoal on cyan
  in dark mode. Settings sections use transparent headers and a selected underline,
  retaining native TabControl/TabItem keyboard and automation behavior.
  Keep the hand cursor on the header template only; setting it on TabItem also
  affects section content. The navbar uses the color logo in light mode and the
  inverse-color logo in dark mode, matching the taskbar symbol's three colors.
  App/launcher/window icons share the transparent inverse-symbol ICO under `Assets/AppIcon`,
  with white, silver-gray, and cyan-blue planes and equal left/right padding;
  do not restore the blue background tile or the rejected vertical offset.
  Preserve its documented derivation and leave original brand assets byte-exact.
- Capitalize the product name as Briosa. Keep navigation ordered Installations,
  SDK Setup, Activity, Settings, with Installations as the opening page.
- Installations contains only gRPC server downloads and installed server packages.
  Installer self-update checks, acquisition, version selection/restart, and
  downloaded-installer maintenance belong in Settings with the update source.
- Settings is the GUI home for all user-configurable values in settings.json.
  New configuration options must include equivalent GUI controls and shared
  validation/persistence; do not require hand-editing JSON for supported options.
  Schema-version metadata remains maintained by the application.
- Keep the server inventory grouped by exact SA release, with contextual actions
  and details on demand. Source editing opens first in Settings; ordinary updater
  checks must never promote rollback as a recommended update. Preserve explicit
  credential isolation and the shared scope control in Advanced. Access methods,
  publisher selections and credentials save automatically; retain explicit publisher
  fingerprint approval and credential-removal review. See the
  [UX decision](docs/architecture/0004-installer-ux.md).
- Share one package-management engine between GUI and CLI. Keep resolution,
  policy, planning, verification, installation, and diagnostics testable with fakes.
- Ship one standard installer with a public release source as the default.
  Engineers can configure an enterprise mirror through the GUI, scripts, or the
  same versioned settings file. Do not require customized installers or centrally
  distributed configuration for basic mirror support. Administrator policy is
  optional and separate from ordinary source settings.
- Installer self-updates share the default package source unless an explicit
  update-source override is configured. Both paths use the same configuration,
  policy, authentication, and verification engine. An inaccessible explicit
  update source must not fall back to the default or public hosting.
- Managed/offline deployment must cover the installer, prerequisites, metadata,
  packages, and updates. No hidden public fallback, credential leakage, or
  policy bypass through user settings or client auto-download paths.
- Catalog structure and publisher trust are separate claims. Unsigned metadata
  can be browsed; installation requires a pinned approved publisher key, valid
  unexpired signature, unchanged reviewed catalog digest, and verified payload
  and manifest. Keep the shared catalog/signature/store contract in `briosa`.
- Installations automatically loads the saved server source on opening and after
  source changes are saved. Refresh reloads catalog and local inventory; page
  switching alone does not repeat completed or failed requests. Unconfigured
  launches make no catalog request. Keep all reads bounded, preserve updater routing,
  clear stale results when settings change, reject redirects, and never request
  payloads during browsing. Credential secrets belong to the exact selected
  catalog in Windows Credential Manager. Windows credentials require the explicit
  Windows authentication mode. Never enable cookies, public fallback, TLS bypass,
  or automatic retries of package changes.
- Source changes may retain the same approved publisher key for unchanged mirrors,
  but must not inherit authentication from another catalog. Recheck settings and
  policy before committing a downloaded package. Keep settings outside binaries.
- Protect canonical all-users stores and administrator policy from non-admin
  writers. Never silently repair pre-existing weak ownership or ACLs. Keep
  arbitrary scripts/commands out of catalogs and do not execute installed servers.
- Preserve complete old packages on failed staging. Use the store lock and
  transaction journal for changes, detect in-use files, and recover before another
  mutation. Do not delete paths outside the owned store or traverse reparse points.
- Separate inert installation from runtime startup and readiness. SDK work goes
  through one owned Briosa worker; the installer must not open a second SDK client.
- Preserve active engineering work and immutable installed artifacts. Review
  package changes and maintenance effects before applying them; do not kill
  unowned SA processes or automatically replay an uncertain MP operation.
- Registry discovery is not proof of the actual activated SDK. Use only a
  documented, validated vendor procedure for shared registration maintenance.
  No guessed switches, arbitrary elevated commands, or per-session registry flips.
  Installed SA metadata may omit InstallLocation; recover candidates through an
  existing DisplayIcon file and probe the sibling SDK. Preserve file evidence
  separately from product metadata, normalize vendor comma-separated versions,
  and retain unquoted-path ambiguity. Label the merged classes registration for
  each view as configured; do not count its matching machine entry twice or promote
  another underlying registration when the merged read is unavailable.
  SDK Setup shows one row per SA installation, its directory, and a persistent
  Registered marker matched by the complete sibling SDK path (never version alone).
  The summary leads with the configured SDK version. Keep raw SDK/registry
  observations in diagnostics and the combined installation/registration dialog,
  opened through an accessible information icon in each row. Keep export on
  Activity; do not restore SDK Setup's export or bottom details buttons.
  Retain incomplete, missing, and conflicting evidence.
  Load local SDK evidence automatically on each visit to SDK Setup, with a
  Refresh button above the summary at the right. Keep scans off the UI thread,
  prevent overlapping reads, and leave navigation and unrelated work responsive.
  Do not apply results or record activity after the window closes.
- Ordinary builds/tests must not require SA or proprietary binaries. Obtain
  explicit current-task permission before controlling SA, activating its SDK,
  changing registry state, or running licensed integration tests.
- Use Apache-2.0 and the organization's
  [governance policies](https://github.com/spatialanalyzer/governance). Do not
  redistribute Hexagon binaries or vendor text. Preserve approved brand wording.

## Workflow

Start from a focused issue and use a short-lived `<issue-number>-<description>`
branch. Keep PRs coherent; use qualified cross-repository references. GitHub
issues and the organization Project are the planning source of truth.

Follow [development instructions](docs/development.md): restore locked
dependencies, build the solution, run core/CLI tests and the WPF smoke harness,
and check links and `git diff --check`. Test complete distribution publishing and
packaged CLI/launcher behavior before handing off binaries. Do not invent a
supported SA release matrix, production signing identity, enterprise deployment
validation, or public catalog URL. Tests use disposable keys, credentials, inert
packages, and an explicitly owned test process; never commit a private key.
