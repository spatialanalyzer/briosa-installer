# Using the Briosa Installer

- Status: Proposed workflows; the installer is not released
- Date: 2026-09-06
- Design: [Installer and SDK management](installer-and-sdk-management.md)
- Interface: [Application experience](application-experience.md)
- Settings: [Source configuration](source-configuration.md)
- Prior planning: [Briosa #158](https://github.com/spatialanalyzer/briosa/issues/158)

Use one standard management application to obtain and maintain exact-target
Briosa server packages. Engineering teams separately manage the applications
that consume those servers, including dependencies, configuration, and deployment
impact. The installer does not discover those applications or create project
profiles.

SpatialAnalyzer remains separately installed and licensed. Multiple installed
versions do not imply that arbitrary SA instances can be automated concurrently.
Screen/action names here are proposed labels, not released commands.

## Choose your source

Install the standard Briosa Installer from public hosting where permitted or an
unchanged internal copy. Its own installation must work offline, including its
prerequisites. A customized enterprise build is unnecessary.

On first launch, public Briosa releases are selected by default. Before any
catalog or update request, you can open Sources and choose a custom repository.
Enter the enterprise Briosa catalog URL, test access when available, and save.
Use the supported authentication method if required. A settings file supplied
before first launch can make this step noninteractive.

The GUI, direct file editing, import, and post-install/CLI scripts all use the
same versioned settings. In Sources, installer updates default to "Use the same
source" as server packages. If your organization uses another remote for the
installer, supply that separate update catalog. It supplies both update metadata
and the installer download. A failed explicit update source does not fall back
to the server source or public hosting. Optional administrator restrictions are
shown when they actually apply.

For an offline workstation, select a complete approved catalog/bundle or internal
share containing immutable payloads and verification material. Follow the same
review/install flow after verification.

## Install the required server packages

Open Installations and select the exact SA target and Briosa version you need.
An SA release with MP exports but no released/approved Briosa product is shown
as unavailable. Review source, identity, scope, disk use, and prerequisites, then
install. Existing versions remain available alongside the new package.

The result identifies the installed package. It does not imply an SDK connection
or execution-ready session. Review SDK setup if prerequisites need attention.

You can close the installer after setup. Configure consuming applications through
their existing client-package dependencies and runtime settings. No application
registration, client inventory, or installer project selection is required.
Client-library instructions live in the language repositories and public docs;
their package managers retain ownership of NuGet, PyPI, and npm configuration.

## Review SDK setup

The app distinguishes installed SDK files, the version Windows is configured to
activate, and any actual runtime identity observation. It compares product
requirements with the effective SDK candidate. An older SDK that satisfies the
relevant requirements is not automatically broken.

For a problem, review the proposed SDK version and compatibility effects on
installed server products. Coordinate maintenance under your organization's
process. The installer cannot identify all custom applications affected by a
shared SDK change. Their owning teams assess and coordinate that impact.

Use an automated repair action only where the vendor procedure has been validated.
Otherwise, export a sanitized IT/vendor handoff. Do not improvise registry edits
or change an attestation to report an identity that was not observed.

An optional explicit environment-validation action uses one owned Briosa worker
in the appropriate licensed desktop session. It verifies the real SA/SDK
identities and runtime readiness. Resolve existing SA/SDK ownership conflicts
first; unrelated SA jobs are not silently closed. A previously opened secondary
SA window may need to be reopened to acquire the SDK endpoint.

The proposed normal setup uses one approved backward-compatible SDK for several
SA targets. Current exact SDK identity checks still apply until the broader
runtime compatibility policy is implemented. Selecting a different server does
not automatically change shared registration.

## Update, restore, or remove packages

| Change | Installer action | Application team's responsibility |
| --- | --- | --- |
| Management-app update | Check the configured update catalog, verify its installer payload, and apply the reviewed update/restart while preserving settings and server packages. | Follow applicable software-management policy. |
| Server maintenance version | Install another immutable version alongside the existing one. | Test and deliberately adopt the desired runtime through application configuration/dependencies. |
| Another SA target | Show whether a matching server product is available and install it separately. | Choose matching client dependencies and coordinate SA deployment. |
| Damaged/previous package | Restore that exact artifact from the configured source when permitted. | Manage application rollback and its data/recovery implications. |
| Server removal | Review the exact package and detectable running/in-use state; remove only that version. | Decide whether applications still need it and coordinate impact before removal. |
| SDK maintenance/regression | Explain product compatibility and approved maintenance or fallback. | Coordinate workloads and affected custom applications externally. |

The installer does not scan for applications, inspect their lockfiles, or maintain
a list of projects that would be affected by a change. Configuration-management
systems and other engineering-team processes can orchestrate adoption separately.
The CLI provides package operations and useful outcomes without a client registry.

If an installation fails, keep existing complete packages usable under policy.
Restoring a package does not undo SA data changes or establish whether an
interrupted MP executed. Removing Briosa leaves SA, licensing, and shared SDK
registration intact. A server still running or files otherwise in use prevent
immediate removal; stop it through its normal owner and retry deliberately.

If a future SDK cannot satisfy the server targets needed for a maintenance plan,
review the reported product conflict. Coordinate a controlled SDK switch between
workloads or separately validated environments. Do not alternate shared
registration automatically as applications start.

## Optional enterprise deployment and support

IT can maintain mirrors, publish approved metadata, supply the standard settings
file, establish protected policy, and deploy exact packages noninteractively.
These are optional distribution methods. Installing under an administrator or
system account does not launch SA; runtime work happens later in the approved
desktop user context.

Use Activity and diagnostics for package/source failures or SDK setup problems.
Support exports contain curated product/policy identities, diagnostic codes, and
operation outcomes. Omit job data, paths, credentials, host/process identifiers,
license material, and raw vendor errors. Follow the relevant organization,
Briosa, and Hexagon support channels.
