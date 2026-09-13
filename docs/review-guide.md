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

1. **Sources:** select the catalog file or HTTPS URL. Configure authentication
   and import the publisher's PEM public key after comparing its SHA-256
   fingerprint through a trusted channel. Save. Installer updates share this
   source unless you configure their separate source, credentials, and key.
2. **Installations / Available:** refresh, select an exact package, inspect its
   details, and review installation. Signature, expiry, declared size/digest,
   archive structure, and product manifest are checked before it is committed.
3. **Installations / Installed:** refresh to see independently installed versions.
   Verify files, repair an exact artifact, remove one version, or recover an
   interrupted operation. Application teams own adoption and change coordination.
4. **Installer updates:** select Installer application under Available, install a
   verified version, then choose Use installer version under Installed. Restart
   when ready. Both metadata and payload use the effective updater source.
   Launching through the permanent bootstrap lets version selection refresh that
   launcher and its bundled runtime too. If its file is protected/in use, the
   selected version remains recorded and the app reports that refresh needs retry.
5. **SDK setup:** inspect installed-product and registration evidence, then export
   a sanitized IT/vendor handoff if maintenance is needed. The app does not
   activate the SDK, run MPs, or change Windows registration.
6. **Activity:** review outcomes and export a report without source URLs, paths,
   credentials, host/process identities, or SA job data.

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
