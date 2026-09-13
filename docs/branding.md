# Briosa Installer brand integration

The styling follows [spatialanalyzer/briosa-brand@v1](https://github.com/spatialanalyzer/briosa-brand/tree/v1)
and its [brand guide](https://github.com/spatialanalyzer/briosa-brand/blob/v1/BRAND-GUIDE.md).
The published pack was verified against its SHA-256 checksum before copying assets.

## Artwork and typography

- The navigation header uses the supplied inverse horizontal logo on deep blue.
  It is displayed at 160 logical pixels without distortion, with additional clear
  space in a 208-pixel sidebar. The wordmark is the supplied lowercase artwork;
  application titles, accessible names, and prose continue to use Briosa.
- Installer is a separate readable product descriptor. No new suite lockup or
  altered wordmark is created. The slogan is omitted from the compact header.
- The app and bootstrap launcher use the supplied multisize ICO. WPF windows use
  the same icon resource, so taskbar, title bar, executable, and shortcuts share
  the approved identity.
- The unmodified Inter variable font is embedded for supporting interface text.
  Logos and fonts load from application resources without network access or a
  Windows font installation. WPF's regular-font resolution is checked by the
  control-tree harness.

## Palette and Windows appearance

| Use | Light mode | Dark mode |
| --- | --- | --- |
| Main background | White `#FFFFFF` | Deep blue `#003875` |
| Body text | Graphite `#585B62` | White `#FFFFFF` |
| Headings | Deep blue | White |
| Primary action | White on deep blue | Deep blue on cyan `#00BAF1` |
| Navigation | White on deep blue; selected row uses deep blue on cyan | Same |
| Surfaces | Silver `#F2F2F2` | White tints over deep blue |

The five approved sRGB colors are copied in `Assets/Brand/colors.json`. Surface,
hover, and border tints are derived from them. Cyan is not used as normal text on
white. Selection has a visible indicator and text, so color is not its only cue.
The underlying Fluent control templates retain their keyboard and automation behavior.

`BrandTheme` follows the selected WPF appearance and Windows application theme,
refreshing resources when system preferences change. It reads preferences only.
In high contrast it removes the application's native-control palette overrides,
leaving WPF's system high-contrast resources in control. The sidebar uses system
window/highlight colors and the supplied white or black logo as appropriate.

## Provenance and packaging

Only required release assets are copied into
`src/Briosa.Installer.App/Assets/Brand`. Its `provenance.json` records the immutable
upstream commit, original paths, per-file hashes, and release-pack digest. The
[third-party notices](../THIRD-PARTY-NOTICES.md) identify the artwork and Inter licenses.
Publishing includes the notices, full licenses, and provenance with the self-contained
distribution. Builds do not fetch brand assets or install fonts.

For a brand update, explicitly select a new approved release, verify its checksum,
copy only the required assets, update provenance, and inspect light/dark, compact,
selected, focused, and disabled states. The WPF harness renders representative
screens and checks font loading, logo minimum width, and high-contrast delegation.
Native high-contrast, Narrator, and all Windows scaling combinations remain separate
release checks; simulated palette checks do not establish those results.
