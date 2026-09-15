# Conventional Windows setup

- Date: 2026-09-15
- Task: [Installer #8](https://github.com/spatialanalyzer/briosa-installer/issues/8)

The standard entry point is a complete offline setup EXE built with pinned Inno
Setup 7.1.0. It installs for the current user under LocalAppData/Programs, offers
a conventional wizard and destination choice, creates a launcher-based Start menu
shortcut, and registers an uninstaller in Windows Installed apps. No separate
.NET runtime download is required. The first release's setup is per-user;
administrators can still deploy server packages to the protected machine store
using the existing CLI. It does not install or register any Hexagon component.

Setup contains the same finalized signed files as the catalog ZIP. The ZIP remains
the immutable package-management artifact; setup is an independently timestamped
Authenticode executable for initial installation. Build the uninstaller using
Inno Setup's documented SignedUninstaller two-pass procedure, sign it with Azure,
compile it into setup, then sign setup before calculating its published checksum.
The setup compiler download is pinned by release URL and SHA-256 and verified
against the official Pyrsys B.V. Windows publisher before installation.

Setup and application updates share the existing mutable bootstrap launcher.
After copying files, setup calls the shared CLI engine to clear only a selected
installer older than the bundled version, under the package-store lock and policy.
Newer/equal selections and every downloaded package stay intact. Later explicit
rollback remains available; the launcher introduces no permanent version floor.
A failed selection adjustment is reported, with files installed and instructions
to retry after resolving the store failure. The setup wizard rejects downgrades
of the installed setup version. The Installed apps version describes the setup
distribution; the application displays its actual running version.

An application mutex prevents setup/uninstall while the Briosa UI is running.
Setup does not automatically terminate programs or reboot Windows. Uninstall
removes tracked application files and its shortcut/Installed apps registration.
Settings, credentials, package stores, and unowned files are retained; package
removal remains a reviewed action inside the app or CLI. No recursive wildcard
uninstall deletion is used.

On first use, the public source and publisher are available through **Use Briosa
public source**. They are never persisted or requested merely by opening the app,
changing appearance, navigating, or closing it. Entering an enterprise source
continues to save automatically. Existing explicit/user/machine configuration
takes precedence over bundled public defaults.

The release pipeline validates the final signed ZIP and conventional setup,
including Installed apps registration, shortcut target, installed file hashes,
CLI/launcher behavior, reinstall, signed uninstaller, and retention of unowned
data. Local setup tests use a separate product identity and isolated store.
Fixture/unit/WPF checks complement, rather than imply, real Artifactory/proxy,
standard-user rollout, Narrator, and mixed-DPI acceptance.

References: [Inno Setup](https://jrsoftware.org/),
[signed uninstallers](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm),
[shared installed-store contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/installed-package-store.md),
[Azure signing](https://github.com/spatialanalyzer/briosa/blob/main/docs/maintainers/release-signing.md).
