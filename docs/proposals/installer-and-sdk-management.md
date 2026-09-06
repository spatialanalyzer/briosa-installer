# Briosa Installer and SDK Management Proposal

- Status: Draft product proposal; not implemented or a current support claim
- Date: 2026-09-06
- Planning references: [installer/control-center scope #158](https://github.com/spatialanalyzer/briosa/issues/158), [SDK identity #70](https://github.com/spatialanalyzer/briosa/issues/70)
- Companion: [proposed administrator and engineer workflows](installer-user-workflows.md)
- Interface: [proposed application experience](application-experience.md)
- Configuration: [standard installer and configurable sources](source-configuration.md)

This proposal now belongs to `briosa-installer`, following the decision to give
the management application its own repository and release lifecycle. Server,
artifact, and shared client/runtime contracts remain authoritative in
[briosa](https://github.com/spatialanalyzer/briosa). The issue links above retain
the original planning context; this move does not accept the proposed runtime
changes or close those issues.

Build one Briosa Installer application that manages independently installed,
exact-SA-target server distributions. Enterprise engineers should obtain approved
software through their organization's repositories. Engineering teams manage
their applications' dependencies and runtime selections independently. Updating
or repairing shared SDK registration is an
explicit maintenance operation, separate from choosing a Briosa target.

The maintainer's design assumption is that newer SA SDK releases are generally
backward-compatible with older SA applications and should remain so. Use that
expectation to simplify the normal workstation setup, while retaining a supported
way to pin a known-good SDK and handle an exception. This assumption is not a
claim that every future SDK has been tested.

This proposal changes the direction of #158 from one target-qualified installer
or desktop distribution to one installer managing multiple target packages. It
also proposes replacing exact SDK/application version equality with an explicit
SDK compatibility policy. Existing runtime checks remain authoritative until
those changes are implemented and validated. In particular, today's server will
still reject an attested newer SDK that differs from its exact target. Never
work around that by reporting an incorrect SDK version in an attestation.

**Product Responsibilities**

| Component | Responsibility |
| --- | --- |
| Briosa Installer | Discover installations; obtain and verify packages; install, update, repair, remove, and inventory server distributions; explain prerequisites. |
| Engineer | Configure the standard installer's release source, authenticate as required, and select packages within applicable organizational policy. |
| Application-owning engineering team | Manage application dependencies, runtime selection, rollout, and impact assessment through its own configuration-management processes and tooling. |
| Enterprise administrator | Maintain internal mirrors and approval processes where required; optionally distribute defaults, enforce policy, or deploy machine-wide software; authorize shared SDK maintenance. |
| Language client | Resolve a compatible installed runtime from its dependency/configuration and use the existing explicit lifecycle contract without registering a consuming application. |
| Exact-target server and worker | Own runtime identity, SA connection, readiness, SDK lifetime, and MP execution. |
| Optional Control Center/tray | Monitor or control explicitly owned runtime sessions through the existing server boundary. It may share installer integration without being required for execution. |
| SpatialAnalyzer installer and SDK | Remain vendor-owned, separately installed and licensed software. |

The installer has its own version. Each server distribution retains its Briosa
version, exact SA target, architecture, source/artifact identity, and protocol
identity. Installing multiple distributions does not create one universal gRPC
server. Every running server remains fixed to one exact SA application release.
Keep target runtime source and builds independent; installer orchestration does
not become shared MP implementation code.

Installing Briosa does not start SA, activate COM, connect an SDK, or run an MP.
The installer can later offer an explicit environment-validation action through
one owned Briosa server/worker. It never becomes another SDK client. A successful
installation is distinct from execution readiness.

Neither the installer nor the server maintains project profiles, a registry of
consuming applications, or a client-dependency graph. The installer knows installed
product identities and its own maintenance state. The server still owns necessary
request, queue, process, and SDK-connection state. Future authentication is a
separate design concern, not a reason to introduce project registration. Current
scope excludes application-impact analysis and project-aware rollout or removal.

**Enterprise Distribution Comes First**

Ship one standard installer with the public Briosa release source as its default.
The primary enterprise workflow is for the engineer to configure that same app
with the organization's mirror/catalog location. Support GUI editing, a versioned
settings file, direct editing, and scripting against the same configuration
contract. An IT-customized installer or preconfigured deployment is optional.

Expose server-package and installer-update sources separately in the UI, shared
by default. Support an explicit update-catalog override for a separate enterprise
remote. Both paths use the same resolution, policy, authentication, and
verification engine. Installer update metadata and payloads must come through
the effective update source; a failed override has no fallback to the default
server source. See [installer self-update](source-configuration.md#installer-self-update).

Source selection must be available before the first network request for metadata,
packages, prerequisites, or installer updates. Keep the standard app's own setup
offline-capable so a post-install script can configure it before first launch.
The public default is a selectable starting point, never a fallback after a
configured enterprise source fails. See [source configuration](source-configuration.md).

The enterprise installation path must work when workstation access to public
GitHub, NuGet, PyPI, npm, and Briosa endpoints is blocked. This includes the
installer itself, updates, catalog/compatibility metadata, documentation, server
packages, and any prerequisites for the installer. No download-on-first-command
or language-package install hook may bypass that policy.

Support these source locations through the same package-resolution contract;
administrator enforcement is an optional overlay, not a source type:

| Mode | Behavior |
| --- | --- |
| Public source | Standard default, ready to use when permitted. The initial setup screen permits changing it before any request. |
| Enterprise mirror | Engineer-configured internal HTTPS catalog/feed, including Artifactory; respect applicable organizational requirements. No public fallback. |
| Offline source | A complete bundle or local/share source, including metadata and verification material, selected through the same app without requiring custom packaging. |

An administrator can optionally lock source URLs, trusted publishers, approval channels,
allowed versions, installation scope, SDK policy, repair permissions, and update
behavior. Normal user settings and `BRIOSA_SERVER_PATH` must not override a
managed allowlist. An inaccessible feed produces an actionable failure, not a
fallback to another source. Existing verified installations can run offline
according to enterprise policy; startup does not require an update check.

Use a source-neutral catalog of ordinary immutable artifacts. Package references
should be relative to the configured source root, avoiding embedded public
download URLs that escape a corporate mirror. Verify redirects against the
approved origins and never forward credentials to an unapproved destination.
Use Windows trust configuration for enterprise certificates and the configured
enterprise network proxy. Certificate validation stays enabled.

Authenticate through a supported credential provider or Windows credential
storage, respecting applicable organizational policy; support noninteractive credential delivery for deployment
tools. Do not require an engineer to have a GitHub account or Artifactory
administrative rights. Credentials must not appear in command arguments, settings
files, source control, diagnostic output, or copied installation instructions.
Interactive browser sign-in cannot be the only authentication method. The first
implementation must name and test its supported authentication modes rather
than promising every enterprise SSO mechanism.

**Artifactory Integration**

Keep the two distribution paths explicit:

| Content | Recommended Enterprise Route |
| --- | --- |
| Installer, server ZIPs, protocol ZIPs, approved catalog, checksums, provenance, signatures, SBOMs, and offline documentation | Generic artifact repository or equivalent internal HTTPS file feed. |
| NuGet client and dependencies | Organization's NuGet repository using normal NuGet tooling and policy. |
| Python client and dependencies | Organization's PyPI repository using normal Python packaging tools and policy. |
| JavaScript/TypeScript client and dependencies | Organization's npm repository using normal npm tooling and policy. |
| SpatialAnalyzer installation media | Existing organization/vendor distribution process and applicable licensing; not a dependency Briosa silently downloads. |

Artifactory supports remote caching repositories and virtual repositories that
present a common endpoint. Generic virtual repositories do not synthesize package
metadata: Briosa must publish its catalog as a real downloadable artifact. The
administrator can proxy a suitable static upstream layout or promote approved
artifacts into an internal local repository. A Git source mirror is not assumed
to mirror GitHub Release assets or language-package dependencies. See
[remote repositories](https://docs.jfrog.com/artifactory/docs/remote-repositories),
[virtual repositories](https://docs.jfrog.com/artifactory/docs/virtual-repositories),
and [package-client support](https://docs.jfrog.com/artifactory/docs/repository-support-for-package-clients).

Make both direct caching and curated promotion possible. A proxy cache alone is
not an approval process. Preserve upstream artifact bytes and provenance when
mirroring. Enterprise approval metadata may select a subset and bind approved
digests without rewriting upstream signed metadata. If IT repackages an artifact,
give the repackaging its own identity and retain the original provenance chain.

The installer must not inspect application lockfiles, generate project
configuration, or rewrite NuGet, pip, or npm settings. Language client guidance
belongs in the public documentation and client repositories. Application teams
select matching dependencies and use their organization's package sources and
configuration-management processes. The installer may expose general product
metadata, but it does not record which client or application consumes a product.

**Installer Features and State**

| Feature | Required Behavior |
| --- | --- |
| Inventory | Show installed SA application releases, registered SDK candidate, and installed Briosa packages separately. Support vendor-reviewed discovery plus an administrator-configured installation location. Do not discover consuming applications. |
| Source configuration | Use the same editable settings through GUI, file, and CLI/script paths; expose the effective source and settings location; read configuration before network work; preserve it through updates. |
| Compatibility preview | Explain server/SA/SDK compatibility from product metadata and observed environment evidence. Show effects on installed server products without inferring which applications use them. |
| Installation | Resolve a pinned artifact, stage and verify it, then atomically publish an immutable complete directory. Reject incomplete or conflicting files. |
| Updates | Distinguish installer updates, server maintenance releases, new SA target products, compatibility-policy updates, and SDK registration changes. Apply independently; installer updates use the shared source or explicit update-source override and preserve settings/server packages. |
| Diagnostics | Distinguish installed, correctly registered, compatible, attached, and MP-ready. Identify missing source access or required administrator action. |
| Repair | Restore a damaged Briosa package from its approved source; offer SDK registration maintenance as a separate operation. |
| Removal | Review exact package identities and detectable running/in-use state; preserve other versions, SA, and shared SDK registration. Do not claim to enumerate dependent applications or assess their impact. |
| Automation | Offer equivalent GUI and noninteractive CLI behavior, plan/preview, idempotent apply, machine-readable output, stable exit statuses, and reboot-required reporting. |

Installer screens should be usable without COM terminology in the normal path:
organization source, available SA targets, selected components, review changes,
progress, and installed status. Advanced diagnostics may explain the registry
cause of a problem and the maintenance action needed.

Installed-package discovery and artifact identity need a shared contract with
`briosa` and its first-party clients. Clients resolve a suitable installed product
without registering with the installer or server. Application dependency and
configuration changes stay with the owning team. Until client/server compatibility
rules are expanded, preserve the current exact server version/source checks.

Use one immutable package store keyed by exact artifact identity and architecture,
with mutable configuration kept separately. Under managed deployment, machine-wide
packages are administrator-writable and readable by permitted runtime users;
per-user installation remains available when policy permits it. Resolve the
installation in the intended runtime user's context. Do not run SA as a service
or as the administrator merely because an elevated deployment tool installed
Briosa. The normal runtime remains in the signed-in user's desktop session.

Concurrent installers must coordinate package writes. Resolve a complete plan
before applying it, retain a bounded journal, verify sufficient space, and leave
existing complete packages intact after download, checksum, extraction, or policy
failure. Installation/removal must respect detected active use of package files. Do not
replace loaded executables or terminate active engineering work to install an
update. New packages can be staged while an old session is running.

An update installs another immutable product version alongside older versions.
It does not change a consuming application's runtime selection or automatically
remove an older package. The installer cannot know whether an idle application
still depends on a package. Its owning team coordinates adoption and explicit
removal through external configuration-management processes.

Package recovery restores an exact installed artifact where policy permits.
Application rollback is managed externally; restoring package availability does
not undo SA document changes, SDK installation effects, or ambiguously completed
MP commands. A machine-wide mutable "latest" selection must not silently replace
an application's explicit runtime choice.

**Artifact and Approval Contract**

Define the catalog schema in a focused implementation review. It needs stable
artifact identities, relative locations, digests, size, publisher/provenance,
exact SA target, architecture, server/protocol/client pairing, installer feature
requirements, SDK compatibility-policy identity, and support/validation status.
Catalog membership does not create new MP capabilities. Keep this metadata
separate from private MP evidence and operation implementation.

Sign the distributed installer and establish verifiable authenticity for release
metadata and artifacts; checksums alone are not publisher authentication. Support
approved verification and trust updates through internal/offline distribution,
including an explicit enterprise policy for stale metadata, revocation checks,
and rollback exceptions. Retain immutable snapshots so an air-gapped site can
reproduce its approved state. Metadata refresh may offer a plan but cannot silently
change installed binaries, SDK registration, or active-session admission.

Provide a full offline payload and detection/uninstall documentation suitable for
enterprise software deployment tools. Do not require JFrog CLI on workstations;
ordinary approved file/HTTPS retrieval is sufficient for Briosa packages.
Administrator publishing pipelines may use JFrog tooling independently.

**Normal SDK Compatibility Policy**

Prefer one newest organization-approved SDK that is compatible with the installed
or explicitly selected server targets, instead of re-registering an exact SDK for every target
switch. Here, "newest" means the approved release order in compatibility metadata,
not lexical sorting or the numerically largest file version. Treat exact SA
release identifiers as opaque values, including historical formats.

Use product metadata to show compatibility for installed server distributions
and targets selected for an installation/maintenance plan. These are package
requirements, not saved application profiles. The operator and engineering teams
decide which combinations must remain usable; installation does not prove an
application currently depends on a target.

The expected backward compatibility has two separate obligations: the newer SDK
must implement the COM interfaces/bindings Briosa uses, and it must communicate
correctly with the older target application. Neither guarantees an unchanged MP
contract across SA applications. Every server still uses the command contracts
reviewed for its exact application target.

A policy records a supported SDK family or reviewed release set, required
interfaces, relevant target restrictions, known-bad releases/combinations, the
preferred fallback, evidence basis, and validation status. Maintainers can use
the stated backward-compatibility expectation to approve a family with documented
coverage; they need not pretend every SDK/SA pair received a licensed test.
Future SDK releases enter through an explicit compatibility-policy and enterprise
promotion step. No unbounded `SDK version >= SA version` rule auto-approves an
unknown release. An enterprise can narrow the upstream policy to exact SDK pins.

The server must enforce that policy at runtime; installer approval is not a
substitute. Keep these facts separate in discovery and readiness:

- Exact SA application required by the server and identity actually connected.
- Actual activated SDK identity and the source of that observation/attestation.
- Whether an applicable compatibility rule allows that SDK with the target.
- Whether the current worker generation has proved its execution channel.

An approved newer SDK is "compatible under policy," not an exact version match.
Unknown identity, an unapproved combination, or a known incompatibility prevents
MP admission. Runtime evidence overrides conflicting attestation. Static file or
registry inspection never becomes an invented runtime observation. Identity and
compatibility evidence must be re-established after replacement or relevant
installation/registration changes, before a new execution probe.

**Repairing SDK Registration**

Yes: the installer should diagnose and offer repair when the effective SDK
registration is missing, points to a removed installation, or selects an older
SDK that does not satisfy the selected server product requirements. An older SDK
that satisfies the relevant product requirements is not automatically broken.
Preview whether upgrading registration
is necessary or merely an administrator-approved maintenance preference.

Prefer the vendor installer repair operation or a Hexagon-documented registration
entry point from an already installed and approved SDK. Do not guess command-line
switches, run `regsvr32` against an EXE, patch proprietary binaries, or reconstruct
a presumed complete vendor registration by changing one path. Registration may
involve CLSID, ProgID, AppID, interface, and type-library state.

The proposed maintenance transaction is:

1. Inspect effective registration in the actual runtime user/architecture context,
   including per-user overrides, relevant machine registration, and installed
   candidate identities. Explain registered candidate versus actual running SDK.
2. Select the SDK allowed by enterprise policy and the operator's selected
   server targets. Show the current and proposed versions and compatibility
   effects on installed server products. Shared registration can
   also affect non-Briosa SDK consumers; obtain the administrator's maintenance
   authorization or apply an explicit preauthorized deployment policy.
3. Require a clean maintenance window. Stop only owned Briosa resources through
   normal lifecycle actions; ask the operator to resolve other SDK/SA consumers.
   Do not kill unknown processes, save/discard jobs, or silently take over them.
4. Recheck the state immediately before applying the plan. Use a narrowly scoped
   elevated helper only when required. Invoke an allowlisted vendor procedure;
   a catalog or settings file must not supply arbitrary elevated commands.
5. Retain the pre-change information needed for a reviewed restoration procedure
   in protected local storage, and retain approved vendor media if restoration
   requires it. Do not promise that importing a registry backup reverses an SDK
   installation. Vendor maintenance may be non-atomic.
6. Reinspect registration and report whether a new session, application restart,
   or vendor-required reboot is necessary. A command's zero exit code is not
   sufficient evidence of a correct runtime.
7. At an explicit validation/start action, let one owned worker verify actual SDK
   and SA identity, compatibility, and bounded MP readiness. Never launch a second
   diagnostic SDK client beside the worker.

If maintenance fails, preserve the incident and block readiness until the state
is established. Restore only through a reviewed procedure, with state checks so
concurrent changes made by another installer are not overwritten. If the vendor
procedure is unknown, the installer must provide a diagnostic and IT/vendor
handoff; it must not improvise a registry edit.

User-level registration shadows may require a different repair from machine-wide
registration. Elevation under another account does not prove the engineer's
activation context was repaired. Verify the effective state for the intended
runtime account after maintenance.

**What Windows COM Does and Does Not Establish**

The shared classic registration is a real constraint, but "Windows can never
select another version" would be too strong. These mechanisms need different
treatment:

| Mechanism | Consequence for Briosa |
| --- | --- |
| Ordinary activation of one CLSID | With one effective registration, there is no SA-release parameter that selects an arbitrary SDK executable. Keep this as the initial activation model. |
| Existing EXE class registration | A running EXE can publish a class factory used before a new executable is launched from registry information. Starting a particular SDK first may affect activation, but reliable selection, races, process ownership, and cleanup require vendor evidence. It is a research candidate, not a supported shortcut. |
| Per-user COM registration | Can affect the effective user-specific mapping; it does not inherently provide a different mapping for each Briosa worker under that user. It can shadow IT's machine policy and depends on activation context. Diagnose it; do not introduce it as a hidden repair. |
| Versioned ProgIDs or CLSIDs | Would be useful if Hexagon actually implements distinct activatable classes. Creating extra ProgID aliases for the same CLSID does not provide independent implementations; inventing a CLSID does not make the EXE register that class factory. |
| `RegOverridePredefKey` | Redirects predefined-key access in the calling process. Its documented scope does not prove reliable isolation of out-of-process COM activation and the SDK's registration/child behavior. No production reliance without a focused proof and vendor support. |
| Classic side-by-side manifests / packaged COM | Do not assume that adding a manifest beside Briosa isolates the installed EXE SDK. Windows has packaged EXE COM support, but adoption is a separate packaging/activation design with vendor, licensing, and identity requirements. |

Microsoft documents the [LocalServer32 mapping](https://learn.microsoft.com/en-us/windows/win32/com/localserver32),
[running EXE class table](https://learn.microsoft.com/en-us/windows/win32/com/registering-a-running-exe-server),
[merged user/machine registry view](https://learn.microsoft.com/en-us/windows/win32/sysinfo/merged-view-of-hkey-classes-root),
[ProgID-to-CLSID relationship](https://learn.microsoft.com/en-us/windows/win32/com/-progid--key),
[process-local registry overrides](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regoverridepredefkey),
[side-by-side authoring requirements](https://learn.microsoft.com/en-us/windows/win32/sbscs/guidelines-for-creating-side-by-side-assemblies),
and [packaged EXE COM registration](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-com-exeserver).
The Briosa conclusions in the table are design judgments; these sources do not
establish that a particular SA SDK supports those alternatives.

Two independent selection problems remain: which SDK EXE COM activates, and
which SA application owns the SDK communication endpoint. Solving the former
does not make arbitrary SA windows addressable or permit simultaneous execution
clients. Do not change registry values before each client start and restore them
afterward: other clients/installers and already running class factories make
that unsuitable as transparent per-session routing.

A bounded vendor-assisted activation investigation is worthwhile before ruling
out exact SDK selection. Ask whether Hexagon supports starting a chosen installed
SDK EXE so it registers the required class factory without changing persistent
registration, or provides a version-specific factory/moniker. Establish the
supported startup arguments, class-registration lifetime and reuse behavior,
actual process identity, and how competing factories are excluded. Test sequential
target changes, an existing different SDK, startup races, worker crashes, and
cleanup. Promotion requires deterministic selection of the requested SDK and the
existing single-owner STA boundary; an occasional successful launch is insufficient.
Any licensed experiment requires explicit task authorization. The installer
proposal does not depend on this investigation succeeding.

**If a Future SDK Breaks Backward Compatibility**

Plan for the exception without making routine target switches change registration:

1. Quarantine the affected SDK/target combination in a versioned compatibility
   policy and pause its promotion through enterprise feeds. Identify whether the
   failure is in COM interfaces, SDK-to-SA communication, or operation behavior.
2. Retain the last known-good SDK and vendor installation/repair instructions.
   Distribute policy updates and remediation through the same internal/offline
   channels. Do not require workstation Internet access to learn the approved fix.
3. Stage a known-good alternative and coordinate maintenance. Do not silently
   downgrade shared registration or interrupt an in-flight MP. If runtime identity
   or a safety-critical policy changes, close admission for subsequent work under
   the reviewed runtime contract and preserve any completion ambiguity.
4. Compute whether one approved SDK can satisfy the server targets selected for
   maintenance, and show effects on the other installed products. If it can,
   repair registration under the approved maintenance plan. If not, report the conflict explicitly: use a
   planned registration change between workloads, or separately validated Windows
   environments with their own SA installation and license. Never let an
   installer oscillate between incompatible SDKs automatically.
5. Work with Hexagon on a fixed SDK, a supported version-specific activation path,
   or an explicit new compatibility family. Ship target-specific interop/worker
   fixes where required; keep application contracts exact and document validated
   behavior before promoting the repaired combination.

Do not infer that a VM, a separate Windows user, or a future remote Briosa mode
automatically solves all licensing, desktop-session, and endpoint-ownership
constraints. Isolated environments need their own validation; secure remote
Briosa remains separate roadmap work.

**Validation and Delivery**

| Stage | Required Evidence |
| --- | --- |
| Source and package resolution | Stock installer with public default and engineer-configured internal/offline sources; shared and separate installer-update catalogs; GUI/file/script equivalence; configuration loaded before any request; update metadata/payload routing and preservation of settings; malformed config and source-switch handling; blocked redirects/public fallback; authentication expiry; TLS/proxy behavior; approved metadata/digests; missing dependency and corrupt artifact handling. |
| Install/update/remove | Clean Windows machine; user and managed-machine scope; silent deployment; nonstandard paths; concurrent/interrupted installation; in-use package protection; rollback and clean removal. |
| Product compatibility | Exact server/SA target checks; approved newer SDK; known-bad and unknown SDK; family-policy updates; conflicting server-product requirements; stale identity and metadata. |
| SDK repair | Fake registration-provider tests for permission denial, user/machine shadowing, drift, procedure failure, restoration failure, and no arbitrary elevated execution. |
| Runtime ownership | Portable fake-process coverage of other-target conflicts and cleanup that preserves SA; no second SDK client or automatic replay. |
| Licensed claims | Explicitly authorized exact-environment validation of supported registration procedures, SDK identity, newer-SDK/older-SA behavior, representative retained operations, failure recovery, and clean ownership. |

Implement the catalog/source contract, package store, and diagnostics
with a CLI first or together with the GUI. Engineer-configurable sources, optional enterprise policy, offline use, and
noninteractive deployment belong in the first supported installer release.
Implement the runtime compatibility policy before advertising newer-SDK support.
Automated SDK repair is enabled only for documented, validated vendor procedures;
diagnosis and a precise administrator handoff can ship earlier.

Resolve these concrete design dependencies with Hexagon and enterprise reviewers:
the supported registration/repair entry points; actual runtime identity probes;
scope of SDK backward compatibility; any supported version-specific activation;
installer signing and offline trust rules; supported enterprise authentication;
and package-store ownership and discovery without consumer registration. Keep proposed installer
behavior out of current product guidance until it is implemented and released.
