# .NET 10/WPF and the source-settings foundation

- Date: 2026-09-06
- Decision: The maintainer selected .NET 10 and WPF for the first application.
- Scope: Development foundation; no released installer or package contract.
- Product direction: [Community Discussion #8](https://github.com/orgs/spatialanalyzer/discussions/8).

## Composition

`Briosa.Installer.App` owns the WPF window and its four navigation views.
`Briosa.Installer.Cli` owns noninteractive argument parsing and output.
Both call `Briosa.Installer.Core`, which owns configuration parsing, effective
source selection, file selection, content revision checks, and persistence.
The core has no WPF, COM, networking, server, or client-library dependency.

This lets GUI and scripted setup use the same behavior and keeps future package
operations independent of the desktop framework. The WPF UI uses system colors,
standard controls, labels, keyboard access keys, and a scrollable content area.
The framework choice does not establish an advertised Windows support matrix.

Microsoft recommends WinUI 3 for new Windows applications; choosing WPF here is
the project's explicit engineering decision, not a claim that Microsoft prefers
WPF. See [Windows development paths](https://learn.microsoft.com/en-us/windows/apps/get-started/)
and the [WPF overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/).
No Windows App SDK dependency is introduced by this increment.

## Implemented configuration subset

The preview implements `schemaVersion: 1`, `source.catalog`, and an optional
`installerUpdates.source.catalog`. Omission shares the server source. An explicit
invalid or incomplete override is an error, never permission to use another
source. HTTPS URLs may not contain user information, queries, or fragments;
offline locations must be absolute Windows drive or share paths.

Select an explicit file, then the per-user file, then optional machine defaults.
Only absence of an optional file allows the next candidate. A selected invalid
document fails as a whole. Unknown/duplicate properties, unsupported versions,
invalid encoding, and oversized files are rejected. No credentials or enforced
policy fields are implemented in this schema subset yet.

Settings are saved outside application binaries. The GUI previews and writes the
same serialization used by the CLI; scripts can also edit the file directly.
See [development instructions](../development.md) for atomic-save behavior and
its boundary with arbitrary external writers.

## Required subsequent work

- Define mirrorable catalogs, artifact identities, trust, and installed-package
  discovery in `briosa` before this app relies on shared contracts.
- Implement approved enterprise authentication and optional policy separately
  from source editing; continue to keep secrets out of source settings.
- Supply the real public source, source setup before requests, and complete
  offline packaging, including prerequisites.
- Add package resolution, review, verified installation, inventory, and recovery.
- Route both updater metadata and payload through the effective updater source;
  preserve settings and installed server packages across actual self-updates.
- Add SDK diagnosis and only validated, vendor-supported maintenance separately.

The source-settings parser is not a release catalog format. No part of this
increment changes runtime identity gates or records consuming applications.
