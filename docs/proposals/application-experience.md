# Briosa Installer application experience

- Status: Original product proposal; the implemented review experience is described in the [review guide](../review-guide.md) and [UX decision](../architecture/0004-installer-ux.md)
- Date: 2026-09-06
- Technical design: [Installer and SDK management](installer-and-sdk-management.md)
- Task walkthroughs: [Administrator and engineer workflows](installer-user-workflows.md)
- Settings: [Source configuration](source-configuration.md)

## Product shape and ownership

Build a small Windows utility for obtaining, inspecting, and maintaining Briosa
server distributions. Engineers can close it after setup. It keeps an inventory
of installed packages and source/maintenance settings; it does not discover
custom applications, inspect their dependencies, create project profiles, or
record which clients use a server.

Engineering teams own application configuration, rollout, and impact assessment
through their existing processes and configuration-management tools. Neither
the installer nor the server requires consuming applications to register.
Necessary runtime request/connection state remains owned by the server; future
authentication is a separate design concern.

Use one installation engine from both the Windows GUI and a noninteractive CLI.
The same standard installer defaults to public Briosa releases and supports
engineer-configured enterprise mirrors and offline sources. Central deployment
and enforced administrator policy are optional. UI framework and bootstrap
packaging were subsequently selected as .NET 10/WPF and a self-contained Windows x64 distribution.

## Window and navigation

Use a conventional Windows window with system typography, a narrow navigation
column, and a clear main work area. Stack or reflow navigation at narrow widths.
Support keyboard operation, visible focus, screen readers, light/dark appearance,
and high contrast. Explain status in text as well as color.

The four views are:

| View | Main content | Actions |
| --- | --- | --- |
| Installations | Exact SA releases, available server packages, installed versions, and package status. | Review installation/update, inspect details, repair files, remove a specific version. |
| SDK Setup | Effective registered SDK candidate, installed alternatives, product compatibility, and maintenance requirements. | Inspect evidence, preview maintenance, obtain a sanitized handoff, explicitly validate a runtime environment. |
| Activity | Package/source/maintenance operations and their outcomes. | Inspect results and sanitized support information. |
| Settings | All user-configurable settings, plus the running installer version and available installer releases. | Edit/save/import/export source and trust settings; check for installer updates, review download/selection/restart, and maintain downloaded installers. |

Keep this navigation order and open on Installations. Capitalize the app name as
Briosa. Every supported user-configurable value in `settings.json` must have a
GUI equivalent under Settings; file editing is optional. Schema-version metadata
is maintained by the application.

Installations contains only gRPC server downloads and installed servers. Installer
self-updates and rollback belong in Settings, alongside their update-source
configuration. A mixed source catalog must not mix these product types in the UI.

Show the active source in the window chrome. Display "Managed by your
organization" only when actual policy applies; ordinary source controls are
editable by the engineer. Elevate only the narrow operation needing privileges.

## 1. Choose a download source

On an unconfigured launch, show public Briosa releases as the default selection.
Allow a custom repository or offline source before any network operation. A
complete standard installer must support offline setup, including prerequisites,
so a post-install script can supply settings before the app first runs.

For a custom source, enter the catalog URL and optionally test the connection.
Valid settings save automatically, including while the source is unavailable.
The GUI and JSON file represent the same settings; direct editing, import, and
CLI/scripts use the same schema and resolution rules. Show the effective file
location. Keep credential secrets in a supported secure store/provider.

Show "Server packages" and "Installer updates" explicitly. Installer updates use
the same source by default; a visible option allows a separate update catalog.
Both update metadata and the installer payload use that effective source. An
explicit override has no fallback to the server source if it fails. See the
[self-update contract](source-configuration.md#installer-self-update).

Source changes cover their associated metadata and downloads. Failure does not
trigger public fallback. Preserve installed
artifacts and invalidate unapplied plans/caches that depended on the former
source. No source action changes an application's runtime selection.

## 2. Install server versions

Ask which SA releases the workstation needs to support. Give each exact SA
release a row, with its full identifier visible. Inside that row, distinguish
available packages from individually installed Briosa versions. Several server
versions for the same target may coexist, as may products for different targets.
There is no machine-wide "current project" or client registration step.

Show installed SA and installed Briosa state separately. An SA release with only
MP exports and no released Briosa product remains visible with an explanation;
evidence alone must not create an installable package. Packages can be staged
before SA is available, with the missing runtime prerequisite clearly reported.

Review installation before applying it: exact target/version/artifact, source,
scope, disk use, verification, and required maintenance. Stage and verify a
complete immutable directory. A failed download or extraction leaves older
installed packages intact. Completion says "Package installed" and distinguishes
it from a validated runtime session.

Package details expose artifact identity, architecture, protocol identity, and
SDK requirements. General compatibility metadata does not identify a consuming
client. Do not scan application directories, inspect lockfiles, generate project
configuration, or ask which application will use the installed server.

## 3. Review SDK setup when necessary

Show the registered SDK candidate separately from an actual activated SDK
observation. Explain compatibility against installed server products or products
explicitly selected for the maintenance plan. These are package requirements;
the installer does not know which targets a custom application depends on.

Normally prefer one newest approved SDK compatible with the relevant server
targets. The proposed broader backward-compatibility policy still requires a
reviewed runtime change: today's exact SDK identity gates remain authoritative.
Choosing another installed server must not automatically rewrite shared COM
registration.

A maintenance preview identifies current/proposed SDK versions, product
compatibility effects, privileges, restart needs, and the supported vendor
procedure. Other SDK consumers can also be affected; their owning teams coordinate
that impact externally. The installer does not enumerate those applications or
infer that absence of a running process makes a change safe for all consumers.

Automated repair is available only for a documented, validated vendor procedure.
Until then, diagnosis and a precise IT/vendor handoff are useful deliverables.
An explicit optional validation action can start/connect through one owned
Briosa worker in a licensed desktop session. It must explain that action, respect
existing SA endpoint ownership, and preserve runtime identity/readiness gates.
Installation itself does not activate the SDK or execute MPs.

## 4. Maintain packages independently of applications

The engineer can close the installer after packages and prerequisites are ready.
An application uses its own dependencies and configuration to locate the expected
server. Application adoption and rollback are managed by its owning team, outside
this app.

Distinguish these maintenance operations:

- Check the effective installer-update source and update the management application
  itself, preserving both source settings and installed server packages.
- Install a server maintenance version alongside older versions of that target.
- Add a server product for another exact SA target.
- Inspect a compatibility-policy update and any SDK maintenance it suggests.
- Restore a specific damaged package or remove an explicitly selected version.

Installing an update does not select it for custom applications or automatically
remove an older version. Removal reviews package identity, file location/scope,
and detectable running/in-use state. Check again when applying the operation and
fail clearly if files cannot safely be removed. Do not claim a list of affected
projects or guarantee that an idle application no longer needs the package.
Preserve other installed versions, SA, licensing, and shared SDK registration.

Package recovery can restore availability of a prior exact artifact within
policy. It does not restore an application's settings, undo SA data changes, or
resolve whether an interrupted MP completed.

## First-release scope

Include editable public/internal/offline sources, shared GUI/file/script settings,
verified installation and package inventory, side-by-side updates, explicit
removal, SDK diagnosis, sanitized results, and noninteractive deployment. Optional
administrator policy uses the same standard app. Automated SDK repair follows
validated vendor procedures.

Project profiles, client registries, application-dependency tracking, project-aware
rollout/rollback, and custom-application impact analysis are outside this release.
Any later integration needs a separate accepted design. A tray runtime monitor
and alternate COM activation mechanisms remain separate roadmap work.

The interactive concept uses sample sources and package states. Its simulated
actions do not download, install, launch SA, or modify registration. A sample
maintenance version demonstrates coexistence and is not a released-version claim.
