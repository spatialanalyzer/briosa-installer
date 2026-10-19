# Briosa Installer brand integration

The artwork follows brand v2, the Caliper b identity, from
[spatialanalyzer/briosa-brand@v2.0.0](https://github.com/spatialanalyzer/briosa-brand/releases/tag/v2.0.0)
and its [brand guide](https://github.com/spatialanalyzer/briosa-brand/blob/v2.0.0/BRAND-GUIDE.md).
The maintainer approved brand v2 on 2026-10-18. Each copied file was checked against
the published pack's SHA-256 digest and its `SHA256SUMS.txt`.

The interface palette and background below still follow brand v1. Brand v2 retires
deep blue, graphite, and silver; moving the palette to v2 is a separate restyle.

## Artwork and typography

- The navigation header uses the supplied compact horizontal logo: color on silver
  in light mode and inverse on graphite in dark mode. The full v2 horizontal logo
  needs at least 250 px, so the 208-pixel sidebar uses the compact version, which
  pairs the small-cut mark with the BRIOSA wordmark and has a 100 px minimum.
  It is displayed at 160 logical pixels without distortion, with additional clear
  space. The wordmark is the supplied outlined artwork; application titles,
  accessible names, and prose continue to use Briosa.
- Installer is a separate readable product descriptor. No new suite lockup or
  altered wordmark is created. The slogan is omitted from the compact header.
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
| Main background | Layered white `#FFFFFF` and silver `#F2F2F2` planes | Deep graphite `#1E1F21`, with planes near `#242527` and `#1B1C1E` |
| Body text | Graphite `#585B62` | White `#FFFFFF` |
| Headings | Deep blue | White |
| Primary action | White on deep blue | Deep blue on cyan `#00BAF1` |
| Navigation | Graphite on silver; selected row uses white on deep blue | White on graphite; selected row uses sidebar charcoal `#1A1B1C` on cyan |
| Selected text | White on opaque deep blue | Deep blue on opaque cyan |
| Selected rows | Blue-tinted fill and deep-blue indicator | Cyan-tinted charcoal fill and cyan indicator |
| Settings sections | Transparent header with a deep-blue underline | Transparent header with a cyan underline |
| SDK registration warning | Dark amber `#9C5700` heading and rule | Light amber `#FFC46B` heading and rule |
| Surfaces | Opaque silver/white | Opaque charcoal cards `#28292B` and controls `#2E2F31` |

The table uses the five brand v1 sRGB colors: deep blue `#003875`, cyan `#00BAF1`,
graphite `#585B62`, silver `#F2F2F2`, and white. `Assets/Brand/colors.json` now holds
the brand v2 tokens for the separate restyle; cyan continues there as `dark-jaw`. The maintainer
requested complementary amber for the SDK warning; these semantic UI colors extend
the app palette without changing the original brand assets. Warning text accents
have 4.97:1 contrast on the light card and 9.26:1 on the dark card. Body text keeps
its normal readable color, and Windows high contrast substitutes system text color
for the warning accent.
The maintainer
requested darker backgrounds; dark mode now shades graphite toward black, with a
`#1A1B1C` sidebar and lower-opacity plane edges. Light mode is unchanged. Surface,
hover, and border tints are derived from them. Action hover/press states darken
blue in light mode and lift cyan in dark mode. Selected navigation and primary
action text have at least 11.52:1 contrast in light mode and 5.10:1 in dark mode.
Control boundaries remain visible against their normal, hovered, and pressed fills.
Cyan is not used as normal text on
white. Selection has a visible indicator and text, so color is not its only cue.
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
4.5:1 text contrast, and 3:1 selection/focus/control boundaries across both themes;
these targeted checks do not claim complete accessibility compliance.

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
window/highlight colors and the supplied white or black compact logo as appropriate.
Decorative planes are removed completely in high contrast.

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
font loading, logo minimum width, and high-contrast delegation. Its high-contrast
render uses the current system colors, so outside a contrast theme it shows the black
logo on a white window.
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
