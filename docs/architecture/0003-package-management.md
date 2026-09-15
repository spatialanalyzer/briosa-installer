# Package management and installer version selection

- Date: 2026-09-12
- Status: Implemented for a full local application review; production publication is separate.
- Direction: [Discussion #8](https://github.com/orgs/spatialanalyzer/discussions/8).

## Boundaries and composition

The WPF app and CLI share configuration, source transport, publisher verification,
archive validation, inventory, package transactions, recovery, and diagnostics.
The independent launcher resolves the selected installer version without fetching
metadata. None of these components activates the SA SDK, executes MPs, starts a
server, or records consuming applications. SDK discovery reads Windows installer
and COM registration metadata and distinguishes that evidence from runtime identity.

Shared catalog, signature, and installed-store contracts are owned by the companion
`briosa` change in `docs/architecture/release-catalog.md` and
`docs/architecture/installed-package-store.md`. No server/worker implementation or
client runtime selection changes are made here.

## Authentication and publisher identity

Source settings select anonymous, Bearer, Basic, or Windows authentication. Bearer
tokens and Basic credentials reside in Windows Credential Manager under a hash of
the exact catalog location. Updater overrides use their own credentials; sharing
a source deliberately shares its authentication. Changing the catalog resets the
authentication mode. Publisher keys can remain the same when moving to an
unchanged mirror, and are not credentials.

HTTPS keeps normal certificate validation and system proxy behavior. Cookies and
redirect following are disabled. Windows credentials, including default proxy
credentials, are enabled only in the explicit Windows mode. No failure tries an
alternate source. Browser sign-in and token refresh are not automated.

The protocol uses a detached RSA-PSS/SHA-256 signature over the catalog digest and
validity timestamps, verified against the approved SPKI public key. Keys are
3072–8192 bits; signatures expire within 90 days of issuance. A per-source/publisher
high-water timestamp in each store rejects older catalogs after a newer one has
been accepted there. Clock integrity is an OS/enterprise responsibility. This
local high-water mark is not a global transparency log or fleet-wide revocation
service. Key replacement/removal and protected policy are explicit administrative
controls; production key custody is not provisioned by this review build.

The engineer compares imported key fingerprints through an approved channel.
Release packaging can supply the real public catalog and key in `public-source.json`.
That file supplies the explicit **Use Briosa public source** first-use choice.
It does not select the source when the app opens or when appearance is saved.
Installations automatically reads the saved server source. Missing or invalid user configuration never silently
selects the public source for a request.

## Package transactions

Install/repair fetch the catalog again and require its digest to match the reviewed
snapshot. They verify its publisher, expiry, and store history before downloading
payloads. Size/digest checks precede extraction. ZIP validation rejects traversal,
links, device names, case aliases, file/directory collisions, foreign top-level
roots, and excessive entries or expanded sizes. The embedded product manifest
must match component, exact version, target, architecture, and required executables.
External provenance must match the manifest when required/present.

One store lock serializes mutation. Complete content is staged under an owned
transaction directory and receives a receipt with per-file digests. A directory
rename commits the package. Existing products are never upgraded in place as part
of ordinary installation. Repair requires the original publisher and exact artifact,
stages a replacement, then preserves an old-directory backup until commit completes.
Recovery restores that old directory when a repair stopped before commit, or
cleans abandoned staging after an already completed change. It never replays MPs.

Removal checks file access, then moves only the selected product to its owned
transaction directory for deletion. Windows rename may reject a new file user
after the access probes close; the app does not force-close handles or processes.
An active installer version cannot be removed. Engineering teams must still
coordinate application usage: no installer can prove that an idle custom
application will never need a package again.

Canonical all-users stores require protected administrative ownership/ACLs;
pre-existing weak ownership is rejected rather than silently changed. New machine
store roots and committed content are protected for Administrators/SYSTEM with
authenticated-user read access. The GUI does not perform blanket elevation.
Machine deployment runs through an authorized administrator terminal. Custom
`--store` locations are explicit caller-managed stores, not automatically managed
shared locations.

## Installer updates and diagnostics

Installations shows gRPC server packages only. Settings owns the running installer
version, explicit update checks, newer/same/older release labels, reviewed download
and version selection, restart, and maintenance of downloaded installer versions.
Checks retain separate catalog state from server browsing and use the saved
effective updater source. Changing settings invalidates both views. Both pages
expose the shared store scope, and installed lists filter that store by component.

Installer ZIPs contain a self-contained WPF app, CLI, and launcher and have an
independent semantic version. Installing one uses the effective updater source
for metadata, signature, archive, and provenance. Activation verifies installed
files and atomically selects a version for the launcher. It does not change source
settings or server packages. The UI can then start the selected installer and
close itself after explicit review. Old versions remain available for deliberate
selection. An incompatible older settings parser must fail closed rather than
resetting configuration during a rollback.

The bootstrap launcher remains at its original path. When launched through it,
the app retains that path and refreshes its executable from the verified installer
package during version selection, including its bundled runtime. The update uses
an atomic file replacement and rejects managed immutable payload directories as
bootstrap destinations. A protected/in-use bootstrap produces an explicit partial
result: the installer version is selected, but the launcher needs an authorized
retry. Direct app launches can select versions without rewriting a bootstrap;
the CLI supports an explicit `--bootstrap` destination for managed deployment.

SDK Setup inspects both registry views and user/machine/merged registration using
the CLSID evidenced by Briosa's committed interop surface. It reports installed
file/registration observations through a combined details dialog accessed from
each installation row's information icon. Sanitized export lives on Activity.
Explicit SDK registration changes use the reviewed installed Hexagon procedure,
as described in [SDK registration](../sdk-registration.md). Broader vendor
installation repair remains outside this feature.
Current exact runtime SDK/SA identity gates remain authoritative.

Activity persists bounded action/outcome codes. Support reports omit source URLs,
paths, secrets, host/process identifiers, license material, and job data. They are
diagnostic records, not a consuming-application inventory.

## Validation and release work

Tests cover signatures, expiry, source routing, credential isolation, malformed
catalogs/archives, integrity failures, side-by-side packages, repair, rollback,
recovery, and protection of an actually running owned test process. WPF smoke
exercises real controls through the entire package workflow using signed inert
fixtures and fake SDK evidence. A Windows Credential Manager fixture is created,
read, and deleted without accessing other credentials. ACL rules are tested in
memory; this does not claim clean-machine or enterprise rollout validation.

The approved signing identity/public feed and Windows signing workflow are now
configured. Before a rollout, validate the intended clean Windows,
proxy/Artifactory, administrative deployment, accessibility, and support environments.
No real Artifactory account, licensed SA session, or machine-wide store was used
during the local implementation tests.

Implementation references: [JFrog authentication formats](https://docs.jfrog.com/integrations/docs/curl-integration),
[Credential Manager read API](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credreadw),
[RSA-PSS](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rsasignaturepaddingmode?view=net-10.0),
and [creation with directory security](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemaclextensions.createdirectory?view=net-10.0).
