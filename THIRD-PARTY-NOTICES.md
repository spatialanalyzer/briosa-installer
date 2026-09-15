# Third-party notices

## Briosa identity

The logo, icon, and palette are copied from
[spatialanalyzer/briosa-brand@v1](https://github.com/spatialanalyzer/briosa-brand/releases/tag/v1),
commit `0a86718f66164e4a773bea37f738888a57c6bce0`, under Apache-2.0.
The custom lowercase wordmark is supplied artwork; prose continues to use Briosa.
Trademark permissions remain separate from copyright licensing.

The application ICO is a raster export of the unmodified transparent inverse
symbol, contained in square frames with centered padding. Shapes, colors, and
transparency are preserved. `Assets/AppIcon/derivation.json` records the export, generator,
and hashes; the distribution includes it as `licenses/briosa-app-icon-derivation.json`.

In source, original paths, SHA-256 digests, and the approved pack digest are in
`src/Briosa.Installer.App/Assets/Brand/provenance.json`. The original license is
in that directory's `LICENSE`. The Windows distribution includes these as
`licenses/briosa-brand-provenance.json` and `licenses/briosa-brand-LICENSE.txt`.

## Inter

Copyright 2020 The Inter Project Authors (https://github.com/rsms/inter).

The unmodified `fonts/Inter-Variable.ttf` from the same brand release is embedded
for supporting interface text. It is distributed under the SIL Open Font License
1.1. The complete notice is preserved in
`src/Briosa.Installer.App/Assets/Brand/fonts/OFL.txt` and in the Windows
distribution as `licenses/Inter-OFL.txt`. No system font installation is performed.

## Microsoft .NET and WPF

The self-contained Windows distribution includes Microsoft .NET and WPF runtime
license and third-party notices in `licenses/`. Native Fluent control templates
come from that runtime; their source is not copied into this repository.
