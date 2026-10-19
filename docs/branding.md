# Briosa Installer brand integration

The artwork follows brand v2, the Caliper b identity, from
[spatialanalyzer/briosa-brand@v2.0.0](https://github.com/spatialanalyzer/briosa-brand/releases/tag/v2.0.0)
and its [brand guide](https://github.com/spatialanalyzer/briosa-brand/blob/v2.0.0/BRAND-GUIDE.md).
The maintainer approved brand v2 on 2026-10-18. Each copied file was checked against
the published pack's SHA-256 digest and its `SHA256SUMS.txt`.

The interface palette and workspace background follow brand v2 as well. Brand v2
retires v1's deep blue, graphite, and silver.

## Artwork and typography

- The navigation header shows the brand's Installer compact logo,
  `suite/briosa-installer-compact-*`: color on the white sidebar in light mode,
  inverse on the void sidebar in dark mode, and the white or black version in high
  contrast. It keeps the compact logo's small-cut mark and BRIOSA wordmark and sets
  INSTALLER under the wordmark, right-aligned, in the brand's product label style.
  The maintainer asked on 2026-10-18 for the product name to join the logo, and brand
  v2.1.0 added these lockups. `BrandArtwork` draws the vendored SVG as native WPF
  vector geometry at 172 logical pixels, above the brand's 170-pixel minimum, so
  the outlines stay sharp at every display scale; the 1600-pixel PNG it replaces
  looked rough when WPF scaled it down. Its accessible name is Briosa Installer.
- The wordmark is the supplied outlined artwork and is not altered. Application
  titles and prose continue to use Briosa. The slogan is omitted from the header.
- The app, bootstrap launcher, and WPF windows share the void-tile ICO in
  `Assets/AppIcon`, exported from the brand's dark application icon. Frames at
  32 px and below come from the brand's tighter favicon fit and larger frames from
  the app icon, the same split as the brand pack's own icon exports; each matching
  frame is pixel-identical to the pack's PNG. The tile keeps the light small-cut
  mark readable on light taskbars, where a tile-less symbol fades as v1's white
  planes did. Native 16, 20, 24, 32, 40, 48, 64, 96, 128, and 256 px frames cover
  common taskbar and title-bar scaling sizes. Original brand assets remain unmodified.
- The unmodified Inter variable font is embedded for supporting interface text.
  Logos and fonts load from application resources without network access or a
  Windows font installation. WPF's regular-font resolution is checked by the
  control-tree harness.

## Palette and Windows appearance

| Use | Light mode | Dark mode |
| --- | --- | --- |
| Main background | Clean room `#F3F7FB` with a faint measurement grid | Void `#040B17` with a faint measurement grid |
| Sidebar | White `#FFFFFF` | Deeper void `#030914` |
| Body text and headings | Light ink `#061A33` | Dark ink `#EAF3FF` |
| Secondary text | Light ink 2 `#46607E` | Dark ink 2 `#9DB3CF` |
| Primary action | White on link blue `#006DB6` | Void on cyan `#00BAF1` |
| Navigation | Light ink on white; selected row uses white on link blue | Dark ink on deeper void; selected row uses the sidebar color on cyan |
| Selected text | White on opaque link blue | Void on opaque cyan |
| Selected rows | Link-tinted fill `#E8F2F8` and link-blue indicator | Cyan-tinted fill `#092D42` and cyan indicator |
| Settings sections | Transparent header with a link-blue underline | Transparent header with a cyan underline |
| SDK registration warning | Deviation +1 amber darkened to `#8B6300`, heading and rule | Deviation +1 `#FFD23F` heading and rule |
| Surfaces | Opaque white cards, tables, lists, and controls | Opaque cards `#0B1625` and controls `#0E1B2C` |
| Dividers and card edges | Light line `#B9C9DB` | Dark edge `#223A53` |

The table uses the brand v2 tokens in `Assets/Brand/colors.json`, and the WPF harness
compares the applied background, ink, secondary ink, and accent with that file. Light
jaw `#0096D6` is only 3.08:1 on clean room, so light-mode accents, accent text, and
focus indicators use the link blue that the brand guide prescribes for text; the light
jaw appears only in the grid's vernier rule. Cards, controls, and hover and pressed
fills blend the brand's translucent line tints, `rgb(120,190,255)` over void and
`rgb(0,56,117)` over clean room, so every neutral keeps the palette's blue cast. Dark
mode keeps the maintainer's deeper sidebar, now the shade of the Scanline homepage
footer. Action hover and press states deepen link blue toward light ink in light mode
and lift cyan toward the small-cut point color `#8BE7FF` in dark mode.

The SDK warning uses the deviation scale's +1 step, which the brand reserves for data,
status, and accent rules. Its light step `#D99A00` is only 2.45:1 on white, so the app
darkens it to `#8B6300`: 5.41:1 on white panels and 5.02:1 on clean room. Dark mode
uses `#FFD23F` unchanged, at 12.58:1 on cards. Windows high contrast substitutes
system text color for the warning accent.

On the background and cards, body text has at least 16.21:1 contrast in light mode
and 16.23:1 in dark mode, secondary text 6.03:1 and 8.46:1, and accent text 5.05:1
and 8.04:1. Selected
navigation and primary action text have at least 5.44:1 contrast in light mode and
8.72:1 in dark mode. Control boundaries keep at least 3:1 against their normal,
hovered, and pressed fills. Neither jaw color is used as normal text on light
surfaces. Selection has a visible indicator and text, so color is not its only cue.
The Settings header template uses a three-pixel underline without a filled tab shape.
It retains native TabControl/TabItem selection, keyboard navigation, automation
semantics and keyboard focus cues. Other controls keep their Fluent templates.
The hand cursor is scoped to the header template, so section content retains
the normal pointer and individual controls retain their own cursors.

Navigation's local Fluent brushes live on the ListBox rather than a shared style,
so they refresh when the theme changes. TextBox and PasswordBox use explicit
opaque selection foreground/background pairs. Native keyboard focus rings remain,
and high contrast substitutes system highlight colors for both parts of the pair.
WPF projects opt into the non-adorner selection renderer at process startup through
`Directory.Build.targets`; its legacy adorner renderer ignores SelectionTextBrush
and can draw opaque highlights over the letters. See Microsoft's
[selection-rendering compatibility note](https://github.com/microsoft/dotnet/blob/main/Documentation/compatibility/wpf-SelectionTextBrush-property-for-non-adorner-selection.md).
The WPF harness checks the resolved navigation template and text-selection properties,
4.5:1 primary, secondary, accent, and warning text contrast, and 3:1
selection/focus/control boundaries across both themes; these targeted checks do not
claim complete accessibility compliance.

With brand v2, the maintainer replaced the Layered Planes background, which echoed
the retired three-plane symbol, with a Scanline measurement grid on October 18, 2026.
It follows the instrument look of the briosa.dev homepage concept. `MeasurementGrid.cs`
draws 1-pixel lines on a 40-pixel pitch in the brand's line tint. A radial opacity mask
fades them out from the lower-right corner, so the grid stays clear of dense content.
A vernier rule runs along the bottom edge, like the mark's beam and the ruled wordmark:
minor ticks every 8 pixels in the tick ink, and jaw-colored majors on the grid lines.
The frozen `DrawingBrush` is anchored to the lower-right corner without stretching, so
the pitch and line width stay constant at any window size. It renders at the current
output resolution without bitmaps or a bitmap cache, cannot receive focus or pointer
input, and never animates. The sidebar and dialogs use neutral base colors. Grouped
package rows, settings cards, the SDK table, and the Activity list use opaque surfaces,
so grid lines never cross tabular text. The empty inventory retains the workspace
background.

**Settings → Appearance → App theme** offers System, Light, and Dark. Selecting
a theme applies and saves it immediately, even before a source has been configured.
There is no Save/Discard step. `appearance.theme` in
`settings.json` accepts `system`, `light`, or `dark`; omission defaults to System.
The saved theme applies before the native window opens and is included in settings
import/export. Theme-only saves retain catalog results and do not request metadata.

`BrandTheme` applies the preference application-wide, including dialogs. System
follows Windows app mode; explicit Light/Dark choices remain in effect when
system preferences change. Windows preferences are only read, never written.
In high contrast it removes the application's native-control palette overrides,
leaving WPF's system high-contrast resources in control. The sidebar uses system
window/highlight colors and the white or black Installer compact logo as appropriate.
The measurement grid is removed completely in high contrast.

## Provenance and packaging

Only required release assets are copied into
`src/Briosa.Installer.App/Assets/Brand`. Its `provenance.json` records the immutable
upstream tag and commit, original paths, per-file hashes, and release-pack digest.
The pack ZIP's bytes vary between builds because of entry timestamps, so
`checksumsSha256` also records the digest of its `SHA256SUMS.txt`. Rebuilding the
tagged commit with Git for Windows' default line endings reproduces that digest. The
[third-party notices](../THIRD-PARTY-NOTICES.md) identify the artwork, Saira, and Inter licenses.
Publishing includes the notices, full licenses, and provenance with the self-contained
distribution. Builds do not fetch brand assets or install fonts.

For a brand update, explicitly select a new approved release, verify its checksum,
copy only the required assets, update provenance, and inspect light/dark, compact,
selected, focused, and disabled states. The WPF harness renders representative
screens at 100%, 150%, and 200% rendering scales and checks vector-only backgrounds,
the applied brand tokens, font loading, logo minimum width, and high-contrast
delegation. Its high-contrast render uses the current system colors, so outside a
contrast theme it shows the black logo on a white window.
Native high-contrast, Narrator, and all Windows scaling combinations remain separate
release checks; simulated palette checks do not establish those results.

### Rebuild the application icon

The optional [icon generator](../eng/Build-AppIcon.cjs) uses Node.js and Sharp
0.35.4. Run `node eng/Build-AppIcon.cjs` with Sharp installed in the authoring
environment, or pass an absolute module path as its only argument. Normal .NET
builds use the committed ICO and need neither Node.js nor an image library.
The inputs are the byte-exact `icons/favicon.svg` (frames up to 32 px) and
`icons/app-icon.svg` (larger frames) under `Assets/Brand`.
`Assets/AppIcon/derivation.json` records input/output hashes, which frames each input
supplies, the square export, renderer versions, and frame sizes. The distribution
includes that record with the brand license and original provenance. Briosa Control
Center copies this ICO and record into each SA target.
