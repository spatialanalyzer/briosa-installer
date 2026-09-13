# Standard installer and configurable release sources

- Status: Draft design; paths, keys, and CLI options below are illustrative, not implemented
- Date: 2026-09-06
- Related: [application experience](application-experience.md) and [enterprise distribution](installer-and-sdk-management.md)

## Normal workflow

Ship one standard Briosa Installer. Its default release source is Briosa's public
hosting. An engineer can use that default or change the same app to point at an
existing enterprise mirror, such as an Artifactory remote/generic feed. Basic
mirror support does not require a customized installer, configuration prepared
by IT, or centrally managed deployment.

In the GUI, the engineer opens Settings, chooses a custom repository, supplies
the release catalog location, optionally tests access, and saves. The GUI writes
the same versioned settings file that a script or a person can edit. The
catalog's location is configuration; the installer executable remains unchanged.
An inaccessible source can be saved for later use if its configuration is valid.

Show two settings: "Server packages" and "Installer updates." Installer updates
default to "Use the same source," with the effective catalog location visible.
An engineer can select a separate update catalog when installer and server
releases use different enterprise repositories or approval processes.

Centralized defaults, enforced policy, and deployment tools remain optional.
Organizations still maintain their mirrors and any approval requirements, and
engineers must have appropriate access to the selected source.

## One configuration contract

Use a documented, versioned JSON settings file outside the installed executable
directory. A proposed per-user default is:

```text
%LOCALAPPDATA%\Briosa\Installer\settings.json
```

A minimal enterprise configuration could look like this:

```json
{
  "schemaVersion": 1,
  "source": {
    "catalog": "https://artifacts.example.com/artifactory/briosa-remote/catalog.json"
  }
}
```

The URL is fictitious. The field names and catalog layout need an implementation
review; this is not a configuration accepted by a released executable. `catalog`
would accept an HTTPS catalog URL or an explicitly selected absolute local/share
catalog path for offline use. It names Briosa release metadata, not an arbitrary
Artifactory web console or a NuGet/PyPI/npm index.

When installer updates use a separate remote, the proposed override is:

```json
{
  "schemaVersion": 1,
  "source": {
    "catalog": "https://artifacts.example.com/artifactory/briosa-servers/catalog.json"
  },
  "installerUpdates": {
    "source": {
      "catalog": "https://artifacts.example.com/artifactory/briosa-installer/catalog.json"
    }
  }
}
```

Omitting the override deliberately shares the default source. An explicit
override is authoritative for installer updates; an inaccessible, invalid, or
incomplete update catalog does not fall back to the server source or public
hosting. Apply the same rule to separate offline catalogs. These keys remain
illustrative pending implementation review.

All supported editing paths must use the same schema and resolution rules:

- GUI editing and configuration import/export.
- Direct file editing and file deployment by a post-install script.
- CLI validation and settings changes.
- An explicit configuration path for scripted or alternate-context use; a future
  `--config <path>` option is illustrative syntax, not an existing command.

The GUI shows the effective source and the settings file being edited. Updates
preserve settings. Use schema validation, atomic writes, and detection of
concurrent external edits so saving in the GUI cannot overwrite a script's
unseen change. Version migrations must preserve source selection and unrelated
supported settings; unsupported schema versions produce a useful error. Do not
silently reset malformed configuration to public defaults.

## Resolution and optional policy

Prefer one selected settings document over a large collection of partially
merged source settings. Proposed selection order, highest priority first:

1. An explicitly supplied configuration file for this app invocation.
2. The user's settings file.
3. An optional machine-default settings file, for example under
   `%PROGRAMDATA%\Briosa\Installer\`.
4. Built-in defaults with the public source selected for initial setup.

Treat source location and its credential reference as one configuration unit.
Do not inherit credentials from a previously selected source or assemble a
source from fields belonging to unrelated configuration files. A selected file
that is missing, unreadable, invalid, or incomplete must produce an error;
absence of an optional default file alone permits trying the next default.

Select the settings document first, then resolve the default source and any
installer-update override inside it. Apply optional administrator policy to both
effective sources. An override needs credentials appropriate to its own source;
do not copy or forward the default source's credentials to another destination.

An optional administrator-protected policy constrains the effective settings
after selection. User files, imports, scripts, and explicit configuration paths
cannot bypass it. Show the relevant restriction if a chosen source is disallowed.
Ordinary settings are not an enforcement mechanism; an engineer's editable file
must not be presented as proof that IT approved a source or package.

The installer maintains no project profiles, consuming-application inventory,
or client-dependency records. Application teams own their dependencies and runtime
selection outside the installer. Coordinate the installed-package discovery
contract with `briosa` so clients can resolve product identities without having
to register themselves with the installer or server.

## Configure before connecting

Load and validate configuration before any source-dependent network operation,
including background metadata checks and updates to the management application.
For an unconfigured interactive launch, show source selection with public hosting
preselected and wait for the engineer to continue. A valid supplied configuration
can satisfy setup noninteractively. If settings disappear, return to source
setup rather than silently using public hosting on the next start.

The standard app's own installation must be possible offline, including its
prerequisites, and must not auto-launch a public updater before a post-install
script can configure it. Where public access is blocked, distribute an unchanged
copy of that complete installer internally. IT need not rebuild or customize it.
This requirement is why a network-only bootstrapper cannot be the sole delivery
artifact.

The effective server source supplies its release catalog, compatibility metadata,
verification material, server artifacts, and related documentation. The effective
installer-update source supplies both its update metadata and installer payloads.
By default these are the same source. There are no hidden public updater URLs.
A missing artifact or failed connection never triggers a public fallback.

Changing source configuration does not select a runtime for an application,
change SDK registration, or rewrite a language package manager's global settings.
Invalidate unapplied plans that depended on the old source. Rebind catalog caches
to the selected source and trust context; cached public metadata cannot stand in
for the enterprise catalog. Retain installed artifact identities and provenance
and evaluate their continued use under any applicable policy.

## Installer self-update

Use the same source-resolution, policy, authentication, and verification engine
for server downloads and the management application's own update check. The
configured update source must be resolved before a manual or background check;
the choice of update-check schedule remains an implementation setting. Show the
effective source and result in the app's update UI.

Read update metadata, compare the independent installer version, and retrieve
the selected installer payload through that effective source. Relative payload
locations resolve within that source's allowed layout; metadata from a mirror
must not send the download silently to public GitHub or another origin. Verify
publisher identity, approved metadata, and the exact payload before offering the
reviewed update/restart operation. A failed check leaves the installed app usable
and reports the error. An update does not upgrade server packages or change SDK
registration.

Keep settings outside the executable directory and preserve both source settings
through self-update. If applying an update needs a separate/elevated helper, pass
the explicit verified update plan and enforce it again at the privilege boundary.
The helper must not independently discover a public default from another user's
settings context, forward credential secrets, or fetch an unreviewed payload.
Package application, recovery, and helper lifetime need implementation validation.

## Mirror layout and authentication

Briosa must publish a downloadable catalog and a documented mirrorable artifact
layout. Its release contract belongs in `briosa`; this file defines how the app
selects a source. Resolve relative artifact locations within the selected source
root, without hidden public asset URLs. An ordinary mirror should be able to
preserve the catalog and payload bytes without rewriting signed release metadata.
The operator supplies the actual Briosa catalog URL from the configured mirror;
the installer must validate it instead of treating any Artifactory URL as a
compatible catalog.

Keep tokens and passwords out of settings files, URLs, command arguments, and
exported examples. An authenticated source may refer to a credential profile in
a supported secure provider/store. Bind that reference to the intended source
and do not forward credentials across unapproved redirects. The initial product
must document the authentication methods it implements; basic source editing
must not depend on an organization developing a custom authentication plugin.

TLS trust and an HTTP forward proxy are separate from the artifact-source URL.
Use supported Windows/enterprise networking configuration without turning off
certificate validation. Native language packages and their dependencies still
use the organization's NuGet, PyPI, and npm sources through normal tooling.

## Implementation validation

When implementation begins, verify GUI/file/CLI equivalence; configuration read
before network access; first-use public-to-mirror selection; direct edits and
concurrent GUI saves; retained settings through updates; malformed and missing
explicit files; optional policy enforcement; source changes with cached metadata;
shared and separate updater sources; updater metadata/payload routing; appropriate
credentials per source; configuration preservation and helper-context handling;
authentication expiry; blocked redirects and public fallback; and complete offline
bootstrap. The current repository contains the proposal, not those tests.
