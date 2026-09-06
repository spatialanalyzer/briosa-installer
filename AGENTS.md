# Briosa Installer agent guide

Read this guide before changing repository source, documentation, build
infrastructure, or settings. User instructions for the current task take
precedence where they explicitly override this guide.

## Purpose and current state

This repository owns the independent Windows installer/manager for exact-SA-target
Briosa distributions. It currently contains repository basics and draft design
documents only. Do not describe a proposed feature, CLI command, compatibility
rule, repair procedure, or illustrative target as implemented or released.

## Boundaries

- Keep server/worker code, protobuf contracts, MP operations, published artifact
  contracts, runtime identity, and SDK compatibility enforcement in
  [briosa](https://github.com/spatialanalyzer/briosa).
- Shared client resolution and lifecycle behavior belong in Briosa's
  [client behavioral contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/client-library-behavioral-contract.md).
  Coordinate changes there; do not create a conflicting installer-only contract.
- Each running server remains fixed to one exact SA application release. The
  installer manages separate products; it is not a universal MP implementation.
- Current product scope excludes project profiles, consuming-application
  registration, client-dependency inventory, and application-impact tracking.
  Engineering teams manage their own dependencies and runtime selections.
  Installer package inventory and server operational state remain necessary;
  neither is a registry of consuming applications. Any later integration needs
  a separately accepted scope and design.
- Preserve the current runtime compatibility gates until an accepted change is
  implemented and validated. A newer-SDK policy in a proposal is not permission
  to bypass an exact runtime identity check or misstate an attested version.
- Published user documentation belongs in
  [briosa-docs](https://github.com/spatialanalyzer/briosa-docs). Keep design rationale
  here and link to authoritative contracts rather than duplicating them.

## Implementation expectations

- Share one package-management engine between GUI and CLI. Keep resolution,
  policy, planning, verification, installation, and diagnostics testable with fakes.
- Ship one standard installer with a public release source as the default.
  Engineers can configure an enterprise mirror through the GUI, scripts, or the
  same versioned settings file. Do not require customized installers or centrally
  distributed configuration for basic mirror support. Administrator policy is
  optional and separate from ordinary source settings.
- Installer self-updates share the default package source unless an explicit
  update-source override is configured. Both paths use the same configuration,
  policy, authentication, and verification engine. An inaccessible explicit
  update source must not fall back to the default or public hosting.
- Managed/offline deployment must cover the installer, prerequisites, metadata,
  packages, and updates. No hidden public fallback, credential leakage, or
  policy bypass through user settings or client auto-download paths.
- Separate inert installation from runtime startup and readiness. SDK work goes
  through one owned Briosa worker; the installer must not open a second SDK client.
- Preserve active engineering work and immutable installed artifacts. Review
  package changes and maintenance effects before applying them; do not kill
  unowned SA processes or automatically replay an uncertain MP operation.
- Registry discovery is not proof of the actual activated SDK. Use only a
  documented, validated vendor procedure for shared registration maintenance.
  No guessed switches, arbitrary elevated commands, or per-session registry flips.
- Ordinary builds/tests must not require SA or proprietary binaries. Obtain
  explicit current-task permission before controlling SA, activating its SDK,
  changing registry state, or running licensed integration tests.
- Use Apache-2.0 and the organization's
  [governance policies](https://github.com/spatialanalyzer/governance). Do not
  redistribute Hexagon binaries or vendor text. Preserve approved brand wording.

## Workflow

Start from a focused issue and use a short-lived `<issue-number>-<description>`
branch. Keep PRs coherent; use qualified cross-repository references. GitHub
issues and the organization Project are the planning source of truth.

For this documentation-only foundation, check links and `git diff --check`.
Introduce build commands and meaningful tests with the implementation. Do not
invent a supported release matrix, framework choice, signing arrangement, or
enterprise authentication capability to fill an unresolved design question.
