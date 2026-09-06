# Using the Briosa Installer in an Enterprise

- Status: Proposed user workflows; the installer features below are not released
- Date: 2026-09-06
- Design: [Briosa Installer and SDK Management Proposal](installer-and-sdk-management.md)
- Planning: [Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158)
- Interface: [proposed application experience](application-experience.md)

Install Briosa once as a management application, then choose the exact-target
server packages your workstation needs. Your organization controls where the
software comes from and which versions are approved. Each engineering project
records the SA target and Briosa package it uses.

SpatialAnalyzer remains separately installed and licensed software. Installing
Briosa does not open an SA job or establish an SDK connection. Multiple installed
versions do not imply that several SA instances can be automated simultaneously.

The screen/action names in this document describe the proposed experience. They
are not existing commands, API identifiers, or released UI labels.

**For the Enterprise Administrator: Prepare the Source**

1. Make the Briosa Installer available through your organization's software portal
   or deployment tool. Include its offline prerequisites; engineers should not
   need to bootstrap from a public website.
2. Configure an internal artifact source, such as an Artifactory generic feed,
   for approved server packages and their catalog, checksums, provenance,
   verification material, compatibility policy, and documentation. Select a
   controlled proxy or an explicit promotion process according to your policy.
3. Make language clients and all their dependencies available through the
   organization's NuGet, PyPI, and npm repositories. A generic server-package
   feed does not replace those package-manager repositories.
4. Deploy the approved source/trust configuration, authentication method,
   installation scope, permitted versions, and SDK maintenance policy. In
   managed mode, engineers cannot enable a public-source fallback.
5. Approve the SDK version or compatibility family for the workstation profiles.
   Keep required SA/SDK media under your existing vendor software process.
6. Pilot the complete installation and startup workflow on representative
   workstations, including nonstandard installation paths and multiple SA
   releases. Promote the tested configuration to the wider team.

You can use silent deployment to preinstall exact packages and profiles. The
installer reports stable machine-readable outcomes for deployment tooling; it
does not open SA from an administrator or system deployment session. Engineers
run their actual Briosa sessions in the approved desktop user context.

**For the Engineer: First Installation**

Open the organization-provided installer. Confirm the organization source and
sign in through the approved method if required. A locked source is intentional;
contact IT if your required package is missing.

Review the detected SA applications and select the targets your work requires.
The installer shows which have approved Briosa distributions. A release with MP
exports but no supported Briosa product is labeled accordingly and cannot be
selected as a supported runtime.

Review the installation plan. It identifies new packages, disk usage, compatible
client versions, and any SDK prerequisite or maintenance action. Installing
another target normally leaves existing packages and project selections intact.
The installer downloads only from your approved source, verifies the complete
package, and records the installed result.

Create or select the project's target profile. The profile pins the exact SA
target and Briosa artifact; the corresponding target-specific language client
must match. Install that client using your team's normal package-manager commands
and internal feeds. Use the approved lockfile; the Briosa Installer does not
silently change global package sources or project dependencies.

Choose the explicit validation/start action when you are ready to use SA.
Resolve conflicting SA/SDK sessions first. One Briosa-owned worker verifies the
actual environment and proves readiness. An "Installed" result alone does not
mean MP commands are ready.

**Using Several Installed SA Releases**

For example, projects might target SA `2024.1.0508.5` and `2026.1.0529.7`. Those
are illustrative selections; the earlier target does not currently have a
released Briosa distribution. Once both are supported, each project uses its
own matching server package and client dependency.

One newer, organization-approved SDK should normally serve the older applications
under the proposed backward-compatibility policy. Changing project targets then
selects another Briosa distribution without repairing SDK registration each time.
The currently implemented server still requires an exact SDK version match until
that policy is delivered.

To switch an active workload, finish or reconcile pending commands, stop the
current owned SDK session, resolve any existing SA endpoint owner, and start the
selected target's server/application workflow. Briosa will not silently close an
unowned SA instance. A previously opened secondary SA window does not become
SDK-addressable merely because the first one closes; a new eligible instance
may be required.

**When the Installer Reports an SDK Registration Problem**

The diagnostic should explain the situation in terms of versions and actions:
for example, "Windows currently selects an older SDK that does not meet this
profile's requirements. A newer approved SDK is installed. Administrator repair
is required." It must distinguish the registered candidate from an SDK actually
observed running.

Save your work and follow your organization's maintenance procedure. If you are
authorized, choose the separate SDK repair action and review the selected version
and affected profiles. Otherwise, provide IT with the sanitized diagnostic report.
The installer uses a supported vendor repair/registration procedure. It does not
ask you to edit registry values or change permissions manually.

Repair can affect other applications that use the SA SDK. It will not proceed
through unresolved ownership conflicts by terminating their processes. After
repair, follow any restart/reboot instructions and establish a fresh validated
Briosa session. Do not change an attestation to say versions match when they do
not.

**Updating Briosa or SpatialAnalyzer**

| Update | What You Do | What the Installer Should Do |
| --- | --- | --- |
| Briosa Installer update | Accept the organization-approved management-app update or receive it through IT deployment. | Update management software without changing selected runtimes or SDK registration. |
| Briosa server fix for an existing SA target | Review the compatible package and select a new-session migration. | Install beside the old package and preserve the previous profile for rollback. |
| New SA release | Install through the approved SA process, then refresh installer diagnostics. | Detect any SDK registration changes and show whether a matching Briosa product is approved. |
| New compatible SDK | Follow the organization's approval and maintenance process. | Recommend registration only when the approved policy and required profiles permit it. |
| SDK regression | Stop new affected work and contact the designated support/IT channel. | Explain the known incompatibility and approved fallback; never silently downgrade or retry MP work. |

An upstream release does not automatically become an approved corporate update.
If no supported Briosa product exists for a new SA release, keep the old project
selection. Have IT resolve any SDK registration change caused by the SA install
before resuming work.

If the newest SDK cannot support all required projects, the installer reports
the conflict. IT can schedule a controlled SDK switch between workloads or
provide separately validated environments. Project selection must not cause
automatic shared-registry switching.

**Offline Workstations and Recovery**

IT supplies a complete approved offline bundle or internal share containing the
same immutable artifacts and metadata used by the connected installer. Import
it, verify it, and follow the same selection workflow. Internal repository
unavailability must never trigger a public download. Existing verified packages
remain usable offline within organizational policy.

If an update fails before selection changes, continue with the old installed
package. If a selected package must be rolled back, choose the recorded previous
selection when no session is using the affected resources. Software rollback does
not reverse changes to an SA job or determine whether an interrupted MP executed.

When uninstalling, review the affected profiles and packages. Removing Briosa
does not uninstall SA, remove its license, unregister the shared SDK, or close an
open job. A package still used by a project or active session is identified before
removal; retain or deliberately migrate that dependency.

**Getting Help**

Use diagnostics to identify source/authentication failure, missing package,
unsupported target, incompatible SDK, administrator-required maintenance, or
runtime ownership/recovery problems. Export only curated versions, artifact and
policy identities, operation outcomes, and diagnostic codes. Support reports omit
job data, paths, credentials, host/process identifiers, license material, and raw
vendor errors. IT owns repository/policy and machine maintenance problems;
Briosa/Hexagon support investigates product or SDK compatibility through the
appropriate project and vendor channels.
