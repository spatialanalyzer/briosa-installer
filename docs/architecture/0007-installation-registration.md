# Side-by-side installation registration

Accepted 2026-09-18. Implements the server-owned
[installation selection contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/installation-selection-and-compatibility.md).

The GUI and CLI register committed server products in the 64-bit Windows Registry.
User and custom stores use HKCU; the protected canonical machine store uses HKLM.
Every product location has its own stable ID, so multiple server versions, SA
targets, and copies coexist. No global default or shared path variable is written.
Registration does not change Hexagon SDK registration.

A registration contains lookup hints. Clients validate the receipt, manifest,
target, compatibility contract, and running server separately. The installer
accepts server manifest schemas 2 and 3; schema 3 requires a valid behavioral
contract major and revision. Existing signature, publisher, and payload integrity
checks still apply.

Registration follows the filesystem commit. If it fails, the committed product
remains available and the operation reports incomplete registration. Recovery
reconciles the index with committed receipts; removal affects only the chosen
product location. Stale entries cannot authorize a launch.

To backfill existing products, choose Settings → Advanced → **Register existing
installations**, or run:

```powershell
Briosa.Installer.Cli.exe packages register --store C:\BriosaPackages --yes
```

The explicit rescan covers the selected store, including custom offline stores.
Clients also enumerate canonical stores, so existing installations remain
discoverable before rescan. Registering checks manifest/receipt agreement and
required files; **Verify** remains the full payload integrity operation.

Tests cover independent registrations, stable repair IDs, post-commit Registry
failure, repeated recovery, removal failure, custom-store isolation, both manifest
generations, and a disposable HKCU Registry64 round trip with Unicode data.
