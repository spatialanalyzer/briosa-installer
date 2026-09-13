# Briosa Installer brand integration

The styling follows [spatialanalyzer/briosa-brand@v1](https://github.com/spatialanalyzer/briosa-brand/tree/v1)
and its [brand guide](https://github.com/spatialanalyzer/briosa-brand/blob/v1/BRAND-GUIDE.md).
The published pack was verified against its SHA-256 checksum before copying assets.

## Artwork and typography

- The navigation header uses the supplied color horizontal logo on silver in
  light mode and the all-white logo on graphite in dark mode.
  It is displayed at 160 logical pixels without distortion, with additional clear
  space in a 208-pixel sidebar. The wordmark is the supplied lowercase artwork;
  application titles, accessible names, and prose continue to use Briosa.
- Installer is a separate readable product descriptor. No new suite lockup or
  altered wordmark is created. The slogan is omitted from the compact header.
- The app, bootstrap launcher, and WPF windows share the transparent three-color
  symbol ICO in `Assets/AppIcon`. It uses the approved inverse symbol: white,
  silver-gray, and cyan-blue planes, with no blue background tile. Square icon
  frames preserve the original geometry and aspect ratio with equal left/right
  padding. The earlier vertical adjustment was removed following maintainer review.
  Native 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames cover common
  taskbar and title-bar scaling sizes. Original brand assets remain unmodified.
- The unmodified Inter variable font is embedded for supporting interface text.
  Logos and fonts load from application resources without network access or a
  Windows font installation. WPF's regular-font resolution is checked by the
  control-tree harness.

## Palette and Windows appearance

| Use | Light mode | Dark mode |
| --- | --- | --- |
| Main background | Layered white `#FFFFFF` and silver `#F2F2F2` planes | Layered graphite `#585B62` planes |
| Body text | Graphite `#585B62` | White `#FFFFFF` |
| Headings | Deep blue | White |
| Primary action | White on deep blue | Deep blue on cyan `#00BAF1` |
| Navigation | Graphite on silver; selected row uses deep blue on cyan | White on graphite; selected row uses deep blue on cyan |
| Surfaces | Opaque silver/white | Opaque graphite tints |

The five approved sRGB colors are copied in `Assets/Brand/colors.json`. Surface,
hover, and border tints are derived from them. Cyan is not used as normal text on
white. Selection has a visible indicator and text, so color is not its only cue.
The underlying Fluent control templates retain their keyboard and automation behavior.

The maintainer selected [Layered Planes](design/layered-planes-reference.png) on
September 13, 2026, then requested native vector geometry to remove the generated
background PNGs' uneven colors and line artifacts. `LayeredPlanes.cs` now draws
the broad intersecting planes with WPF geometry and controlled solid-color fills.
Only the short cyan edge uses a fading stroke. The frozen `DrawingBrush` preserves
the geometry's proportions, crops at the workspace edges, and renders at the
current output resolution without scaling a bitmap or using a bitmap cache.
It cannot receive focus or pointer input and does not animate or download at runtime. The
sidebar and dialogs use neutral base colors, and grouped package rows and settings
cards use opaque surfaces for readability. The empty inventory also retains the
workspace background. The old background PNGs are removed. The concept image in
this document is reference material only; the application never loads it.

**Settings → Appearance → App theme** offers System, Light, and Dark. Selecting
a theme previews it immediately; **Save changes** persists it with source settings,
and **Discard changes** restores the saved choice. `appearance.theme` in
`settings.json` accepts `system`, `light`, or `dark`; omission defaults to System.
The saved theme applies before the native window opens and is included in settings
import/export. Theme-only saves retain catalog results and do not request metadata.

`BrandTheme` applies the preference application-wide, including dialogs. System
follows Windows app mode; explicit Light/Dark choices remain in effect when
system preferences change. Windows preferences are only read, never written.
In high contrast it removes the application's native-control palette overrides,
leaving WPF's system high-contrast resources in control. The sidebar uses system
window/highlight colors and the supplied white or black logo as appropriate.
Decorative planes are removed completely in high contrast.

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
screens at 100%, 150%, and 200% rendering scales and checks vector-only backgrounds,
font loading, logo minimum width, and high-contrast delegation.
Native high-contrast, Narrator, and all Windows scaling combinations remain separate
release checks; simulated palette checks do not establish those results.

### Rebuild the application icon

The optional [icon generator](../eng/Build-AppIcon.cjs) uses Node.js and Sharp
0.35.4. Run `node eng/Build-AppIcon.cjs` with Sharp installed in the authoring
environment, or pass an absolute module path as its only argument. Normal .NET
builds use the committed ICO and need neither Node.js nor an image library.
The input is the byte-exact v1 `marks/briosa-symbol-inverse.svg` under
`Assets/Brand`. `Assets/AppIcon/derivation.json` records input/output hashes,
the transparent square export, renderer versions, and frame sizes. The distribution
includes that record with the brand license and original provenance.
