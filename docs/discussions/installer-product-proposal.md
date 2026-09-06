# Proposed discussion: Briosa Installer — deployment, profiles, and SDK maintenance

Status: Ready for maintainer review; **not posted to GitHub Discussions**.
Suggested destination: [community Discussions](https://github.com/orgs/spatialanalyzer/discussions).
Choose the available architecture/design category when publishing.

---

We propose one independent Windows Briosa Installer application that manages
separate server distributions for exact SpatialAnalyzer application releases.
The new [briosa-installer repository](https://github.com/spatialanalyzer/briosa-installer)
contains the product proposal; it does not yet contain a working installer.

The goal is a straightforward experience for enterprise engineers who use
several SA releases and must obtain software through their organization's
approved repositories, including JFrog Artifactory.

## What an engineer would do

1. Install Briosa Installer from the organization's software portal.
2. See detected SA releases and the matching approved Briosa packages.
3. Review an installation plan and install the needed products side by side.
4. Create a project profile pinning the exact target and server/client selection.
5. Explicitly validate the environment through one owned Briosa runtime session.
6. Review future updates and migrate the project for its next session, retaining
   the previous selection when policy permits rollback.

The proposed window has Installations, Project profiles, SDK setup, Sources, and
Activity views. It keeps normal tasks readable and puts package identities,
compatibility evidence, and administrative details one level deeper.

## Enterprise deployment is part of the initial product

The installer, prerequisites, catalog/policy metadata, packages, verification
material, and updates must all work through approved internal or offline sources.
Managed mode has no public fallback. Clients and their dependencies continue to
use the organization's NuGet, PyPI, and npm tooling.

The GUI and noninteractive CLI share one engine. IT can deploy pinned packages
and profiles without a signed-in desktop, starting SA, or activating the SDK.
Engineers use those installations later in their approved user sessions.

## SDK compatibility and maintenance

The working assumption is that newer SA SDKs remain backward-compatible. The
normal configuration should therefore use the newest organization-approved SDK
that satisfies all required project profiles, under an explicit compatibility
policy with known-bad exclusions. Each server still targets one exact SA
application and its reviewed MP contract.

Current server gates still require exact SDK identity matching. The broader
policy needs a separate reviewed runtime change before the installer can rely
on it. Installation approval cannot override actual runtime identity or readiness.

The installer should diagnose shared registration problems and offer a separate
maintenance preview using a supported, validated vendor repair procedure.
Changing project targets must not transparently rewrite shared COM registration.
If Hexagon changes compatibility, keep a known-good fallback and make conflicting
profile requirements visible. A supported version-specific SDK activation path
is worth a bounded vendor-assisted investigation, but is not assumed to exist.

## Repository ownership

Installer UX, package management, sources, and maintenance orchestration live in
`briosa-installer`. Runtime, protocol, published artifact and shared client
contracts remain in `briosa`. Released user guidance belongs in `briosa-docs`.
The management application has its own version and release cadence.

## Decisions to review

1. Does the proposed screen/workflow model fit engineers' actual deployment and
   project-switching tasks? Which administrative restrictions must be visible?
2. Which enterprise authentication methods, software-deployment tools, and
   offline trust requirements should the first release explicitly support?
3. What catalog/profile contract should the installer and all first-party clients
   share, and how should team pins coexist with local installation paths?
4. Which Windows packaging and GUI technology best supports unattended deployment,
   accessibility, narrow elevation, and maintaining the installer itself?
5. Can Hexagon confirm backward-compatibility boundaries and supported SDK
   registration/repair entry points, including failure recovery?
6. Can the first release ship SDK diagnosis and an IT handoff while automated
   repair waits for validated vendor procedures?

Detailed proposals:

- [Application experience](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/application-experience.md)
- [Installer and SDK management](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/installer-and-sdk-management.md)
- [Administrator and engineer workflows](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/proposals/installer-user-workflows.md)

Related planning: [Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158)
and [SDK identity #70](https://github.com/spatialanalyzer/briosa/issues/70).
