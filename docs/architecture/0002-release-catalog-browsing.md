# Read-only release catalog browsing

- Date: 2026-09-08
- Status: Implemented development preview; no install authorization or released feed.
- Builds on: [WPF and source settings](0001-wpf-and-source-settings.md).
- Subsequent implementation: [package management](0003-package-management.md).
  The statements below describe the earlier read-only checkpoint.
- Product direction: [Community Discussion #8](https://github.com/orgs/spatialanalyzer/discussions/8).

## Shared contract and composition

The companion `briosa` change owns `schemas/releases/v1/catalog.schema.json`,
`docs/architecture/release-catalog.md`, and `eng/New-ReleaseCatalog.ps1`. These are
a candidate shared release contract and producer. This repository consumes that
contract; it does not own a second schema or redefine server artifact contents.
The companion changes must be reviewed together before public feed publication.

`ReleaseCatalogCodec` validates bounded UTF-8 metadata and resolves declarations
to a package preview. `ReleaseCatalogClient` owns explicit HTTP/file reads and
returns typed outcomes. WPF and CLI both call them. A fake HTTP handler exercises
transport behavior without public access, credentials, SA, or proprietary files.

Catalog identity includes the component, its independent semantic version,
runtime identifier, and, for a server, its exact SA application target. Browsing
does not select a latest version, discover installed software, infer SDK support,
or change the server's current runtime compatibility checks.

## Source and cancellation behavior

Server packages use `source.catalog`. Installer releases use the explicit
`installerUpdates.source.catalog` override or share the server source when that
override is absent. Invalid or inaccessible overrides fail without fallback.
Relative artifact references stay under the catalog directory lexically so
metadata does not embed a separate public payload origin.

The GUI requires saved settings before a refresh. A settings/component generation
and cancellation token prevent delayed results from a previous source from
reappearing. The saved settings revision is checked before and after a read and
before displaying a package preview. Failed reads clear previous results.

Reads cap both catalog bytes and elapsed time, including body streaming. This
needs an explicit body deadline because .NET's `ResponseHeadersRead` completes
at the headers; see [HttpCompletionOption](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0).
Redirects are disabled and reported rather than followed; see
[AllowAutoRedirect](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclienthandler.allowautoredirect?view=net-10.0).
No default HTTP credentials or cookies are sent. Authenticated enterprise access and
administrator policy remain separate work. The current native file/share open
may remain blocked after the caller's deadline; its late result is discarded.

## Preview and trust

A preview identifies its catalog by source and content SHA-256 and lists the
selected package's declared locations, sizes, and hashes. Those hashes have not
been compared with payload bytes. Both catalog listing and preview report
publisher verification as not performed; every preview has `CanInstall = false`.

There is no payload download, archive extraction, launch, install, self-update
execution, or registry action. An unsigned catalog's self-declared checksums do
not establish an approved publisher. Signing and trust configuration, freshness
and rollback policy, payload/provenance verification, and recovery require a
reviewed design before install plans become executable.

## Validation and next boundary

Core/CLI tests cover invalid catalogs and sources, limits, cancellation during
streaming, redirects, separate updater routing, offline reads, and exact package
identity. The WPF harness exercises the real control tree with fake responses,
including a late response after a source edit. The shared producer can also be
tested against the built CLI through `Test-ReleaseCatalog.ps1 -ConsumerCliAssembly`.
The [offline sample](../../examples/README.md) uses invented metadata and no payloads.

Next work must make publisher verification and approved enterprise authentication
concrete before adding verified payload acquisition. A public feed, packaging
format for the installer itself, installed inventory, and self-update application
are not implied by this browsing increment.
