# Briosa Installer review build

This Windows x64 distribution contains the .NET runtime. Extract its complete
directory to a permanent location and open `Briosa.Launcher.exe`. No separate
.NET installation, SpatialAnalyzer installation, or network bootstrap is needed.
Optionally run `Install-DesktopShortcut.ps1` to create a Start menu entry.

For repository review, `eng/New-ReviewDemo.ps1` generates a signed local feed and
settings file. Launch with `--config <demo>/settings.json --store <demo>/store`.
This keeps review installations in that directory. The generated server payloads
are inert text and the SA identifiers are invented; do not launch those server files.
The optional installer package is this real application. The fixture signature
expires in seven days; regenerate the demo after expiry.

## Review the application

1. **Installations:** local inventory and available servers load automatically
   from the saved source. **Refresh** reloads both. On an unconfigured first use,
   choose **Configure package source**. Empty, loading, failed, and filtered views
   explain the next action. A failed source keeps installed versions visible;
   use **Try again** or **Refresh** when the source is available.
2. **Settings → Package sources:** enter an HTTPS catalog URL or browse to a local
   or network catalog. **Access and publisher…** configures authentication and the
   publisher's PEM public key; compare its fingerprint through a trusted channel.
   Changes save automatically and work offline. **Test connection** checks the
   current values without downloading packages.
   Installer updates share this source, credentials, and publisher unless you turn
   off **Use the same source as server packages** and configure the revealed override.
3. **Installations:** server versions are grouped by exact SA release. Installed
   and available versions appear together; search and SA/status filters narrow the
   list. Select one version to reveal **Install…** or **Verify files**, **Repair…**,
   and **Remove…**. **Details** contains provenance, hashes, and locations. The install
   review names the version, target, source, destination, size, and verification.
   A new version installs alongside existing versions. Application teams own adoption.
4. **Settings → Installer updates:** **Check for updates** offers a newer version
   when one exists in the saved source. **Download update…** reviews, verifies, and
   selects it for the next launch; **Restart to update…** is a separate action.
   The selected next-launch version remains visible after reopening. When the source
   has no newer version, rollback is never promoted as the normal update action.
   Expand **Previous versions and recovery** to deliberately choose a specific release.
   Expand **Downloaded installer versions** to verify, repair, remove, or select
   an existing installer. Both metadata and payload use the saved effective updater
   source. An unavailable mirror has no fallback.
   Launching through the permanent bootstrap lets version selection refresh that
   launcher and its bundled runtime too. If its file is protected/in use, the
   selected version remains recorded and the app reports that refresh needs retry.
5. **SDK Setup:** inspect the configured SDK summary and SA installation table.
   Use a row's information icon for combined installation and SDK registration
   details. The app does not activate the SDK, run MPs, or change Windows registration.
6. **Activity:** review persistent outcomes in local time, filter **Needs attention**,
   and open details for result codes and recovery guidance. Package entries include
   their version, exact SA target when applicable, and duration. Export a report
   without source URLs, paths, credentials, host/process identities, or SA job data.

**Settings → Advanced** contains the shared package scope/location, local inventory
refresh, interrupted-operation recovery, JSON viewing, import/export, and reload.
Changing scope changes the inventory being viewed; it does not move packages.
Imports apply automatically. Text fields save after a short typing pause and when
you leave the input. Closing flushes pending edits. Invalid input stays visible with
an explanation; failed writes offer Retry or Use saved values. External edits are
reloaded when the window becomes active if there are no pending local changes;
conflicting local edits are preserved for review instead of overwriting the file.

**Settings → Appearance → App theme** selects System, Light, or Dark. System follows
your Windows app mode. Changes apply and persist immediately for future launches,
including before source setup.
The choice also appears as `appearance.theme` in JSON and survives import/export.
Windows contrast themes take priority. Changing Briosa's theme does not change Windows.

Access methods, publisher choices, and typed credentials also save automatically.
Secrets stay in Windows Credential Manager for the exact catalog; a blank secret
keeps the stored value. Publisher fingerprint approval and **Remove stored credential…**
remain explicit reviewed actions. Close the dialog when finished.

User packages live under `%LOCALAPPDATA%\Briosa\Packages`. Machine deployment
uses `%PROGRAMDATA%\Briosa\Packages` from an authorized administrator terminal.
The GUI does not silently elevate. Settings live separately under
`%LOCALAPPDATA%\Briosa\Installer`, with an optional explicit `--config` file.

The public production catalog and signing identity are release infrastructure,
and are not provisioned by this local review build. Use the included review
walkthrough's generated local feed or an approved internal signed catalog. This
does not declare invented demo SA versions to be supported products.

## Scripted use

Run `Briosa.Installer.Cli.exe --help` for the complete command surface. Configure
sources with `settings set`; use `credentials set` to supply a token/password on
standard input or through a hidden interactive prompt. `trust import` requires
the public-key file and approved fingerprint. `catalog preview` supplies the
catalog digest required by `packages install` or `packages repair --yes`.

No failure switches to public hosting. HTTPS uses normal Windows certificate and
proxy behavior. Bearer tokens, Basic credentials, and explicitly selected Windows
authentication are implemented; browser-based SSO/token refresh is not automated.
File shares use the caller's normal Windows filesystem identity.

Initial bootstrap trust applies to the complete downloaded distribution itself.
The publisher signature used for subsequent catalog/package operations is separate
from Windows executable code signing. This local review artifact is not represented
as a publicly signed production release.

The [administration guide](administration.md) covers mirror layout, authentication,
optional policy, and machine deployment. The repository
[development guide](https://github.com/spatialanalyzer/briosa-installer/blob/main/docs/development.md)
contains build and demo-generation commands; local implementation may precede
publication on the main branch.
