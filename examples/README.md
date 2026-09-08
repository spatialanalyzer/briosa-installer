# Catalog browsing fixture

`catalog.json` contains invented packages, hashes, sizes, SA release identifiers,
and versions. None of its payload files exist. It is an unsigned browsing/test
fixture, not a public release catalog or a declaration of supported SA products.

Build the development preview, then run from the repository root:

```powershell
dotnet run --project src/Briosa.Installer.Cli -c Release --no-build -- settings init --config "$env:TEMP\briosa-catalog-demo.json" --server-catalog "$PWD\examples\catalog.json"
dotnet run --project src/Briosa.Installer.App -c Release --no-build -- --config "$env:TEMP\briosa-catalog-demo.json"
```

`init` refuses to overwrite an existing file. Use a new path or intentionally
update an existing demo file with `settings set --config ...`.

Open Installations and click Refresh catalog. Three server rows demonstrate
different releases of one target and a separate target. Select a row and preview
it. Switch Component to Installer application and refresh to see its independent
version from the shared catalog. A separate update source, when configured,
replaces that shared location for the installer component only.

The same operations are available through `catalog list` and `catalog preview`
in the CLI. No fixture package is downloaded or installed.
