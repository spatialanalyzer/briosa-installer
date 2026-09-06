# Discussion publication record

- Status: Published for discussion; the proposal does not establish implemented capabilities.
- Destination: [spatialanalyzer/community](https://github.com/orgs/spatialanalyzer/discussions), General category.
- Title: **[Architecture] Briosa Installer: configurable sources, server versions, and SDK maintenance**
- Published: [Community Discussion #8](https://github.com/orgs/spatialanalyzer/discussions/8).
- The text below the divider records the initial published body; follow the Discussion for subsequent feedback and decisions.

---

We propose one independent Windows Briosa Installer application that manages
separate server distributions for exact SpatialAnalyzer releases. The new
[briosa-installer repository](https://github.com/spatialanalyzer/briosa-installer)
contains the proposal; it does not yet contain a working installer.

SpatialAnalyzer and the SA SDK remain separately installed and licensed vendor
products. This proposal manages Briosa packages and approved maintenance; it
does not redistribute those proprietary products or change runtime support claims.

## Engineer workflow

1. Install the standard application. Public releases are the default source;
   configure an enterprise catalog URL or offline source before any public request.
   The GUI, editable JSON file, and scripts use the same settings contract.
   Installer updates can share the server source or use a separate configured remote.
2. Select the exact SA target and server version, review the plan, and install.
   Products for different targets and versions of the same target coexist.
3. Review SDK setup when necessary. Use a separate, approved maintenance action
   or an IT/vendor handoff; explicit runtime validation uses one owned worker.
4. Close the installer and use engineering applications under their own
   configuration. Return to install updates alongside older packages, restore
   files, or explicitly remove a specific version.

The proposed views are Sources, Installations, SDK setup, and Activity. They
expose package and machine state without requiring application registration.

## Application ownership stays with engineering teams

Neither the installer nor the server maintains project profiles, a consuming-client
registry, or custom-application dependency records as part of this initial product.
The server still owns its operational request, queue, and SDK state. Future
authentication is covered separately by
[Secure Remote Briosa Deployment](https://github.com/orgs/spatialanalyzer/discussions/7);
it is not a reason for installer-managed application profiles.

Teams manage application dependencies, runtime selection, adoption, rollback,
and impact assessment through their existing configuration-management processes.
The installer can detect running/in-use package files but cannot know whether an
idle application still depends on them. It must not promise a list of impacted
projects or silently remove older versions during an update.

## Enterprise distribution without a custom installer

Basic mirror support requires a catalog location and appropriate access, not
an IT-customized build. Sources must support internal and complete offline distribution,
with no public fallback after an enterprise mirror is selected. Credentials
remain in a supported secure provider/store.

IT may optionally distribute settings, enforce policy, and deploy the same
packages noninteractively. Clients and their dependencies continue to use normal
NuGet, PyPI, and npm tooling. The installer does not manage consuming projects.

## The installer also updates through a configured source

Expose two settings in Sources: **Server packages** and **Installer updates**.
Installer updates default to **Use the same source**, with an optional override
when an organization uses a separate remote or approval process for the installer.
Both source settings live in the same versioned configuration file, editable
through GUI or scripts.

The updater resolves configuration before making any request, obtains both
update metadata and the installer payload from its effective source, verifies
the release, and offers a reviewed update/restart. It preserves configuration
and existing server packages. An unavailable explicit update source does not
fall back to the server source or public hosting. Credential handling and payload
verification use the same engine as server-package downloads.

## SDK compatibility and repair

Assume SA SDK backward compatibility as the normal direction, while using an
explicit compatibility policy with known-bad exclusions. Prefer a newest approved
SDK that satisfies the server targets selected for maintenance, and show its
compatibility with the other installed products. Current runtime exact SDK gates
remain authoritative until separately reviewed changes are implemented.

Shared SDK repair is an explicit maintenance operation using a documented,
validated vendor procedure. It can affect consumers beyond Briosa; their teams
coordinate that impact externally. Do not rewrite shared registration each time
a different application or server starts. For a future incompatibility, retain
known-good alternatives and report product conflicts. Version-specific activation
remains a bounded vendor-assisted research topic, not an assumed capability.

## Initial delivery and implementation questions

Start with configurable sources, verified package installation/inventory,
side-by-side updates, explicit removal, SDK diagnosis, sanitized diagnostics,
and equivalent noninteractive operations. Automated SDK repair is enabled only
for validated vendor procedures; diagnosis and an IT/vendor handoff can ship first.

Implementation review still needs to establish:

1. Supported enterprise authentication methods, signing/publisher verification,
   and offline trust/revocation behavior.
2. The artifact/catalog and installed-package discovery contract in `briosa`,
   allowing clients to resolve runtimes without registering themselves.
3. Windows packaging and GUI technology, including unattended deployment,
   accessible interaction, narrow elevation, and self-update recovery.
4. Vendor-confirmed SDK compatibility boundaries and supported registration/repair
   procedures with explicit licensed validation.

Feedback on the proposed workflow and deployment requirements is welcome. After
review, accepted implementation decisions should become focused issues; neither
this discussion nor its prototypes establish released capabilities.

Detailed proposals:

- [Application experience](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/application-experience.md)
- [Source configuration](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/source-configuration.md)
- [Installer and SDK management](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/installer-and-sdk-management.md)
- [User workflows](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/installer-user-workflows.md)

Prior planning: [Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158)
and [SDK identity #70](https://github.com/spatialanalyzer/briosa/issues/70).
