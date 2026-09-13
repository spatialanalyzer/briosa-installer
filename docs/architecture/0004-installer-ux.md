# 0004: Task-focused installer experience

- Status: Implemented for local review
- Date: 2026-09-13
- Task: [Installer #2](https://github.com/spatialanalyzer/briosa-installer/issues/2)
- Related: [Package management](0003-package-management.md), [review walkthrough](../review-guide.md)

## Problem

The initial functional application displayed catalogs, installed packages, technical
previews, maintenance controls, and long forms at the same time. Engineers had to
infer the next action, switch between duplicate inventories, and distinguish ordinary
installer updates from rollback. Credentials also had a different persistence
boundary from the surrounding settings editor without a sufficiently clear action.

The accepted UX review calls for fewer simultaneous choices, contextual actions,
clear failure/recovery states, and native Windows appearance. Product scope and
package verification remain governed by the existing architecture.

## Screen and workflow decisions

| Area | Implemented behavior |
| --- | --- |
| Navigation | Briosa capitalization; Installations, SDK Setup, Activity, Settings. Installations opens first. |
| First use | An actionable empty state opens source settings. Configured launches load local inventory; checking a remote catalog remains explicit. |
| Servers | One searchable list groups available and installed versions by exact SA target. SA and status filters narrow the list. Latest means latest in the checked source, not a runtime compatibility recommendation. |
| Actions | Selecting a server reveals Install or Verify/Repair/Remove. Details contain full hashes, provenance, and locations. Reviews name the action, version, target, source, destination, size, and verification boundary. |
| Sources | Package sources is the first Settings section. Browse and Test work with the editor. Testing reads metadata without saving or acquiring payloads; Save remains possible offline. |
| Updater routing | A checked sharing option hides redundant override controls. Turning it off reveals the independent updater source. Metadata and payload use that exact effective source with no fallback. |
| Settings edits | Save/Discard appears only when needed. Import stages values. Source edits invalidate prior checks; late results cannot repopulate a changed source. External file edits require reload before package operations. |
| Credentials | Explicit Save credential now / Remove stored credential actions immediately update Credential Manager. Applying the dialog stages authentication mode and publisher selection. Cancel/Discard does not undo separately saved credentials. |
| Installer updates | The ordinary action offers only a newer release. Acquisition and restart are separate. Specific versions and rollback require opening recovery controls and selecting a row; none is automatically selected. |
| Next launch | Running and selected installer versions are separate claims. Selection metadata survives reopening; launch still verifies the selected package through the existing engine. |
| Advanced | One shared scope/location control, local refresh, journal recovery, JSON, import/export, and reload. Scope selection does not move packages. |
| SDK Setup | Summary first, evidence and details second. Inspection reads files/registry only. No COM activation, MP execution, automated registration repair, or readiness claim. |
| Activity | Live actions and saved history use the same rows, local timestamps, outcomes, and safe package context. Details expose result codes and guidance; Needs attention filters failures/cancellations. Unreadable history is reported and preserved. |

Server versions continue to coexist independently. No consuming applications,
project profiles, dependency discovery, or impact assessment is introduced.

## Native presentation and accessibility

Use the built-in WPF Fluent theme with system light/dark appearance, system accent,
dynamic theme brushes, consistent typography, spacing, and section cards. Retain
native control templates and keyboard behavior. Avoid window-local theme settings
that shadow application styles. Custom controls build on named Fluent base styles.

The main window supports a compact 820 × 580 logical size. A flexible server list
keeps selected actions visible; longer settings content scrolls above a fixed save
area. Confirmation details scroll above fixed Cancel/action buttons. Cancel receives
initial focus. Status is expressed in text, with meaningful automation names and
explicit live-region change events for assistive tools. Secrets remain masked and
never enter diagnostic context.

## Validation and limits

Automated validation exercises the real WPF resource/control tree with signed inert
packages, fake SDK evidence, and temporary stores. It covers first use, editor
test/save/discard, independent updater failures, stale/external settings changes,
filter and source recovery, side-by-side maintenance, explicit rollback, persisted
next-launch selection metadata, Activity context, credential-save semantics, and
compact layout. Core/CLI and complete packaged launcher checks remain required.

Light/dark control-tree renders and a native Windows walkthrough verify layout,
readable source/update states, selection, confirmation focus, SDK evidence, and
Activity. These checks do not establish full Narrator compatibility, every DPI/text
scale, Windows high-contrast behavior, clean-machine deployment, or an enterprise's
real Artifactory/proxy/SSO configuration. Those remain release validation tasks.
The review package has no production public catalog or signing identity.

The older application proposal retains prospective SDK maintenance and compatibility
ideas. It is not evidence that those behaviors exist in this review build.
