# Briosa Installer application experience

- Status: Draft product and interaction proposal; no application is implemented
- Date: 2026-09-06
- Technical design: [Installer and SDK management](installer-and-sdk-management.md)
- Task walkthroughs: [Administrator and engineer workflows](installer-user-workflows.md)

## Product shape

Build a small Windows management application that engineers can close after
setup. It should feel like a practical installed-software manager: a clear list
of SA releases, readable status, a few specific actions, and a review of changes.
Avoid making engineers learn feeds, COM, or package graphs just to get started.
Put those details behind an explanation or an administrator view.

Use one installer engine from both a Windows GUI and a noninteractive CLI. IT
deployment must not automate clicks. The management application's own installer
must support a complete internally distributed/offline payload. The choice of
Windows UI framework and bootstrap packaging is still open; this proposal does
not select WPF, WinUI, MSI, or another technology.

The management app should not have to remain open for a client to resolve and
use an installed package. Runtime sessions still follow the shared Briosa
lifecycle contract. A tray monitor can be considered separately after the core
deployment workflow works.

## Window and navigation

Use a conventional Windows window with a narrow navigation column, a main work
area, and a contextual details panel where there is room. On a narrow window,
stack the details beneath the selection. Use system typography, light/dark and
high-contrast support, keyboard navigation, screen-reader labels, and visible
focus indicators. Status must be expressed in text, not color alone.

Show the source and policy near the top of every screen: for example,
"Managed by your organization · Approved channel." Display the last successful
metadata refresh and offer an explicit refresh. A failed refresh must explain
whether an already installed selection can still be used under the cached policy.
Never equate "not the newest version" with a broken installation.

| Navigation | What the engineer sees | Main actions |
| --- | --- | --- |
| Installations | Detected SA releases, available approved Briosa products, installed server versions, and package status. | Review installation, inspect package, review update, repair package, remove. |
| Project profiles | A named project with its exact SA target, pinned server artifact, compatible client identity, SDK policy, and last validation result. | Create a profile, review a new selection, export project configuration, restore a previous selection. |
| SDK setup | Effective registered SDK candidate, installed alternatives, policy requirements, and affected profiles. Actual observed runtime identity is shown separately when available. | Inspect details, preview approved maintenance, export an IT handoff. |
| Sources | Approved artifact source, offline source, credential state, policy ownership, and metadata freshness. | Test source access, refresh, import an approved bundle, request administrator help. |
| Activity | Installation and maintenance results with timestamps and next steps. | Inspect an operation, export a sanitized report. |

Show administrator-only actions with a short explanation such as
"Managed by IT" or "Administrator required." Avoid a window full of unexplained
disabled controls. Elevate only the narrow maintenance/install action that needs
it, not the whole application by default.

## Installations screen

Lead with the user's question: "Which SA release does this project use?" Give
each exact release a row. Keep the friendly release label and exact identifier
visible together; two builds of the same SA release must be distinguishable.

Show application availability separately from Briosa package status. A package
can be staged before SA is installed, but cannot become runtime-ready then.
An installed SA release without a released/approved Briosa product remains
visible with an explanation. A captured MP command inventory alone must never
create an installable product.

The details panel explains the selected server version, artifact/source identity,
architecture, client pairing, SDK requirement, and approval/validation basis.
Engineers can expand the digest and technical evidence without needing them for
ordinary selection.

"Review installation" opens a concrete plan: exact artifacts, approved source,
scope, disk use, prerequisites, expected result, and any separate maintenance
needed. After applying it, show "Package installed; session not validated" and
offer profile setup. A green package indicator must not suggest an SDK connection
has already been proved ready.

Support retrying a failed download or an incomplete staged installation without
changing the old working selection. Display whether no changes were applied,
installation completed, or recovery is required. An ambiguous MP outcome is a
runtime concern and cannot be repaired by retrying an installation action.

## Project profiles and target switching

A profile represents a reproducible project selection, not a mutable machine-wide
"current SA version." Switching the highlighted profile in the installer edits
only the proposed selection. Applying it records a reviewed configuration for
future sessions; existing sessions keep their original product.

Let engineers name a profile, choose an exact target, and review compatible
server/client identities. The profile format and local-store resolution rules
must be agreed with the shared client behavioral contract in `briosa`. An export
must use portable identities; workstation installation paths and credentials do
not belong in a committed team configuration.

Provide project-specific client instructions for .NET, Python, and JavaScript
using the organization's package sources. Do not install language tools or
rewrite project dependencies or global feed settings without a separate reviewed
action. Each project's client still has to match its exact target.

Offer explicit environment validation through an owned server/worker. Show the
target, actual connected SA identity, actual activated SDK identity, applicable
compatibility rule, and readiness result as distinct information. Historical
validation includes its time and is not a live promise of readiness. Running
this validation requires an appropriate licensed user session and clear consent
to the startup/connection actions involved.

If an existing SA instance owns the SDK endpoint, explain the conflict and how
the operator can resolve it. Never silently close an unrelated job. Switching
from an older SA project to a newer one normally changes the selected server and
client, with no shared SDK registry rewrite. That normal path depends on the
proposed runtime SDK compatibility policy being delivered.

## Updates and recovery

Present updates by purpose, not as one "Update everything" button:

- **Management app:** an approved installer update with its own version.
- **Server maintenance:** another package for the same exact SA target, installed
  alongside the old package; a project migrates separately for its next session.
- **Another SA target:** an additional product, not an in-place upgrade to an
  unrelated project's target.
- **Compatibility policy:** a reviewed rules update with effects on profiles.
- **SDK maintenance:** a separate shared-system change, requiring its own plan.

Show the previous selection and the consequences of restoring it. Removal lists
affected profiles and active sessions and requires those references to be
resolved. Removing Briosa must leave the SA application, licensing, and shared
SDK registration intact.

## SDK setup and administrator handoff

Explain a problem as "Windows currently selects SDK X; these projects require
an approved compatible SDK" before exposing registry details. An older SDK that
satisfies every required profile need not be repaired. Prefer the newest
organization-approved SDK that satisfies all required profiles, using explicit
compatibility metadata and known-bad exclusions.

The repair preview shows current and proposed SDK identity, the supported vendor
procedure, required privileges, restart requirements, and affected profiles.
Shared SDK consumers outside Briosa may also be affected. The engineer can export
a sanitized handoff or proceed under an applicable administrative maintenance
authorization. A screen preview is not evidence that a vendor repair procedure
has already been established.

If no validated automation exists, ship diagnosis and precise IT/vendor guidance.
Automated repair can follow when the vendor entry point and failure recovery have
been proved. No hidden registry toggles during project startup are planned.

For a future SDK regression, show which combinations are blocked and the approved
fallback. If one SDK cannot satisfy all required profiles, offer a clear
administrator decision: planned maintenance between workloads or separately
validated environments. Do not promise a per-project SDK selection mechanism
unless Hexagon and runtime validation establish one.

## Enterprise and offline use

IT supplies the installer through the usual software portal or deployment tool,
with locked sources and approval policy. The engineer's source view should show
that configuration without requiring Artifactory administration. Native language
package repositories remain separate from the generic artifact/catalog feed.

An offline import first inspects the bundle, publisher/approval material, policy
snapshot, and payload completeness, then offers the same installation plan as an
online source. Sources may refresh metadata without silently migrating profiles
or rewriting registration. Document the supported noninteractive authentication
methods and policy behavior before calling an environment supported.

The CLI must offer the same planning, installation, profile, diagnosis, and
removal operations with stable exit statuses and machine-readable output. Define
the syntax in an implementation review; no command examples here should be
mistaken for a released interface. IT needs idempotent deployment, scope and
detection rules, unattended authentication, reboot reporting, and useful errors
without an interactive desktop or SA activation during installation.

## Proposed first release and later work

The first useful release should include managed and offline bootstrap, source
policy, verified package installation/removal, profiles, reviewed updates,
noninteractive deployment, SDK diagnosis, and sanitized diagnostics. The GUI and
CLI should use the same engine and produce the same plans and outcomes.

Deliver automated SDK repair only for validated vendor procedures. A runtime
monitor, extra source adapters, advanced fleet reporting, and any alternative
COM activation mechanism can be reviewed separately. Neither a tray process nor
a speculative SDK selection mechanism should delay basic reliable deployment.

Before implementation, resolve catalog/profile ownership with `briosa`, select
the Windows packaging/UI approach, define supported authentication and offline
trust, and validate the SDK compatibility/repair plan with Hexagon. Convert
accepted decisions into focused implementation issues rather than treating every
sentence of this draft as approved policy.
