# Third-party notices

## Windows setup

The conventional setup executable and uninstaller are built with unmodified
[Inno Setup 7.1.0](https://jrsoftware.org/).
Copyright (C) 1997–2026 Jordan Russell; portions Copyright (C) 2000–2026 Martijn Laan.
The Inno Setup notices and About information remain intact in the setup engine.

## Briosa identity

The logo, icon, and color tokens are copied from brand v2 (Caliper b) in
[spatialanalyzer/briosa-brand](https://github.com/spatialanalyzer/briosa-brand),
commit `f08fc75acdc36ebc4e9ac6167523efbbeb383f44`, under Apache-2.0. That commit
identifies itself as v2.0.0; its release tag is not yet published.
The BRIOSA wordmark is supplied artwork; prose continues to use Briosa.
Trademark permissions remain separate from copyright licensing.

The application ICO is a raster export of the unmodified void-tile app icon,
`icons/favicon.svg` at 32 px and below and `icons/app-icon.svg` above, in square
frames. Shapes, colors, and transparent corners are preserved.
`Assets/AppIcon/derivation.json` records the export, generator, and hashes; the
distribution includes it as `licenses/briosa-app-icon-derivation.json`.

In source, original paths, SHA-256 digests, and the pack's checksum-list digest are in
`src/Briosa.Installer.App/Assets/Brand/provenance.json`. The original license is
in that directory's `LICENSE`. The Windows distribution includes these as
`licenses/briosa-brand-provenance.json` and `licenses/briosa-brand-LICENSE.txt`.

## Saira

Copyright 2020 The Saira Project Authors (https://github.com/Omnibus-Type/Saira).

The BRIOSA wordmark in the logo is artwork outlined from Saira by the brand pack;
no Saira font file is included. Saira's SIL Open Font License 1.1 is preserved for
attribution in `src/Briosa.Installer.App/Assets/Brand/fonts/Saira-OFL.txt` and in
the Windows distribution as `licenses/Saira-OFL.txt`.

## Inter

Copyright 2020 The Inter Project Authors (https://github.com/rsms/inter).

The unmodified `fonts/Inter-Variable.ttf` from the same brand pack is embedded
for supporting interface text. It is distributed under the SIL Open Font License
1.1. The complete notice is preserved in
`src/Briosa.Installer.App/Assets/Brand/fonts/Inter-OFL.txt` and in the Windows
distribution as `licenses/Inter-OFL.txt`. No system font installation is performed.

## Microsoft .NET and WPF

The self-contained Windows distribution includes Microsoft .NET and WPF runtime
license and third-party notices in `licenses/`. Native Fluent control templates
come from that runtime; their source is not copied into this repository.
