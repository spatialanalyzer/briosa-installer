# Enterprise sources and deployment

Engineers use Settings in the ordinary Briosa Installer to configure their approved
Artifactory URL or Windows file/share catalog. They can save through the GUI,
edit JSON, import settings, or use the CLI. IT does not need to distribute a custom
installer. Optional machine defaults and restrictions support centralized management.

## Mirror layout and updates

Mirror `catalog.json`, `catalog.json.signature.json`, and every referenced ZIP and
provenance file, preserving relative paths and exact bytes. The catalog has no
public artifact URLs. Configure the final raw-file URL, not an Artifactory web UI
or a redirect. A Generic repository/remote serving that layout is sufficient.
Keep caches fresh so catalog/signature come from the same publication and are
served before signature expiry. Publish payloads before metadata/signature; an
inconsistent pair fails closed and can be retried after the mirror finishes updating.

Servers and installer releases can share a catalog or use independent repositories.
A separate updater source controls **all** updater metadata, signature, ZIP, and
provenance requests. No failure falls back to public hosting or the server source.
Retain the approved publisher key when mirroring unchanged bytes. If an enterprise
republishes metadata under its own signature, explicitly approve that publisher.

## Authentication and settings

The Settings page supports anonymous, Bearer access tokens, Basic username/password or token,
and explicit Windows authentication. Secrets reside in the current Windows account's
Credential Manager under the exact catalog location; settings and diagnostics omit
them. Changing a URL resets authentication and needs credentials for the new location.
Updater overrides have independent authentication. Browser SSO and automatic token
refresh are not implemented; obtain an approved repository credential from IT.

In **Settings → Package sources**, test the editor values before saving if desired;
testing downloads metadata only. Save is also available offline. Source edits and
imports are staged until **Save changes**. The access dialog clearly separates
credential storage: **Save credential now** and **Remove stored credential…** are
immediate Credential Manager operations and are not undone by Cancel/Discard.
Applying the dialog stages the selected authentication mode and publisher key.

HTTPS keeps ordinary certificate validation and system proxy behavior. Cookies and
redirects are disabled. Windows credentials for origin and proxy are enabled only
in Windows mode. Environments requiring token authentication plus a separately
authenticated proxy need validation of their Windows proxy configuration; there is
no separate proxy-password UI. Shares use the caller's Windows filesystem identity.

Example settings (URLs and PEM contents are placeholders):

```json
{
  "schemaVersion": 1,
  "source": {
    "catalog": "https://artifacts.example.com/artifactory/briosa/catalog.json",
    "authentication": "bearer",
    "publisherKey": "-----BEGIN PUBLIC KEY-----\n...approved SPKI PEM...\n-----END PUBLIC KEY-----"
  },
  "installerUpdates": {
    "source": {
      "catalog": "https://artifacts.example.com/artifactory/briosa-installer/catalog.json",
      "authentication": "bearer",
      "publisherKey": "-----BEGIN PUBLIC KEY-----\n...approved SPKI PEM...\n-----END PUBLIC KEY-----"
    }
  }
}
```

Omit `installerUpdates` to share the server source, authentication, and publisher.
Import an approved RSA 3072–8192-bit public key after comparing its SHA-256
fingerprint through a trusted channel, using the GUI or CLI:

```powershell
./Briosa.Installer.Cli.exe settings set --server-catalog https://artifacts.example.com/artifactory/briosa/catalog.json
./Briosa.Installer.Cli.exe credentials set --component server --mode bearer
./Briosa.Installer.Cli.exe trust import --component server --key ./approved-public-key.pem --fingerprint <approved-sha256>
```

`credentials set` prompts without echo. Automation can pipe a secret provider's
output to standard input; keep it out of command lines, transcripts, and repositories.
Provision credentials as the installing account. `credentials remove` deletes that
source's vault entry and selects anonymous mode. Changing sources does not copy or
delete unrelated vault entries.

Precedence is explicit `--config`, then
`%LOCALAPPDATA%\Briosa\Installer\settings.json`, then optional
`%PROGRAMDATA%\Briosa\Installer\settings.json` defaults. Whole documents do not merge.
Saving machine defaults through the ordinary GUI creates user settings. Distribution
public defaults seed the editor only when no selected configuration exists.

## Optional machine policy

An administrator can protect `%PROGRAMDATA%\Briosa\Installer\policy.json` with:

```json
{
  "schemaVersion": 1,
  "allowedCatalogPrefixes": [
    "https://artifacts.example.com/artifactory/briosa/",
    "https://artifacts.example.com/artifactory/briosa-installer/"
  ],
  "publisherFingerprints": [],
  "allowUserStore": true,
  "allowMachineStore": true
}
```

Empty fingerprints allow any explicitly approved source key; populate the list with
approved 64-character hexadecimal fingerprints to restrict publishers. Prefixes name
directories and check origin/path boundaries; encoded URL paths are rejected under
policy. Empty allowed prefixes deny all sources. Scope restrictions cover install,
remove, recover, and installer selection. Publisher restrictions also apply when
selecting/launching a managed installer. This is installer policy, not Windows
application control for arbitrary executables.

The policy file and its Briosa/Installer parents require Administrators or SYSTEM
ownership and write/delete/permission changes restricted to those principals.
Other accounts may read. Invalid, inaccessible, or weak policy fails closed,
including with explicit configuration files. IT owns these ACLs; the app never
silently rewrites an existing weak policy or machine store.

## Packages and machine deployment

User packages live in `%LOCALAPPDATA%\Briosa\Packages`. Machine packages use
`%PROGRAMDATA%\Briosa\Packages`; run changes from an authorized administrator
terminal. Ordinary users can inspect, verify, and launch its selected installer.
New canonical machine stores have protected Administrators/SYSTEM ownership and
authenticated-user read/execute access. Custom `--store` locations are caller-managed;
they do not automatically acquire canonical machine-store protection.

```powershell
$cli = '.\Briosa.Installer.Cli.exe'
$store = Join-Path $env:ProgramData 'Briosa\Packages'
$catalog = (& $cli catalog list --component server | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0) { throw 'Catalog access failed.' }
# Select an exact ID from the reviewed output.
& $cli packages install --component server --id <selected-id> --catalog-sha256 $catalog.catalogSha256 --store $store --yes
& $cli packages list --store $store
```

Versions remain independent. Verify checks receipt hashes offline. Repair retrieves
the original publisher/artifact through the current source, which must still list
it in a current signed catalog. Remove affects one version and protects active
installer/in-use files without terminating processes. Recovery resolves interrupted
transactions. Back up settings separately; do not edit receipts, catalog history,
or managed payloads as configuration.

Install an installer component with `--component installer`, then use
`app activate --id <id> --store <root> --bootstrap <permanent-launcher-path> --yes`.
The bootstrap option refreshes its executable/runtime from the verified package.
GUI launches through it remember that path automatically. If replacement is denied,
version selection remains committed and the error calls for an authorized retry.
Settings and server packages stay separate. Omit `--bootstrap` only when deliberately
managing that executable separately.

Keep the initial distribution in a permanent directory. The optional
`Install-DesktopShortcut.ps1` creates a user Start menu entry. For machine/custom
stores or explicit settings, include the corresponding arguments in your managed
shortcut. Remove versions through the app; deleting the standalone folder/shortcut
alone leaves managed packages and settings available.

## SDK maintenance and support

SDK Setup reads installation/COM-registration evidence without activating COM and
distinguishes file observations from runtime SDK/SA identity. Export the sanitized
IT/vendor handoff for repair. Automated registry repair awaits a documented supported
vendor procedure. Exact server runtime identity gates still apply.

Engineering teams own application adoption and change coordination through their
own configuration management. The installer has no client registry, project
inventory, or dependency-impact scan.

See [package-management boundaries](architecture/0003-package-management.md).
Real Artifactory/proxy, clean Windows, machine deployment, and accessibility
validation remain rollout checks; local tests do not imply those were exercised.
