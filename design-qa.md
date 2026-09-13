# Layered Planes implementation review

Date: September 13, 2026.

Source visual truth: `docs/design/layered-planes-reference.png`, the selected
Layered Planes concept. Scope is the background and corresponding neutral
surfaces of the existing Windows WPF application.

## Evidence and normalization

The source is a 1983 × 793 two-mode presentation board with margins and mode labels. Compare
the application content inside each frame, excluding the presentation canvas.
The implementation is the real WPF control tree rendered at 1140 × 800 logical
pixels, 96 DPI, one output pixel per logical pixel. This is a native application;
CSS viewport size and browser console checks do not apply. The generated source
has approximate UI geometry, so the comparison uses its natural application
aspect ratio and preserves the established production control sizing rather than
stretching the interface to match illustration artifacts.

Full-view evidence, viewed together with the source in one comparison input:

- `artifacts/ux-renders/planes-servers.png.light.png`
- `artifacts/ux-renders/planes-servers.png.dark.png`
- `artifacts/ux-renders/planes-compact.png.light.png`
- `artifacts/ux-renders/planes-compact.png.dark.png`

Additional settings evidence: `artifacts/ux-renders/planes-settings.png` and
`artifacts/ux-renders/planes-servers.png.dark-settings.png`.

State: Installations, one grouped exact-SA target, selected installed server
0.2.0 and an available 0.1.0 row, with contextual maintenance controls. The
fictional 2099.1.0101.1 target is the same inert test fixture as the source.
Times are generated at test execution and are not fixed design copy.

The 820 × 580 compact renders check the supported minimum viewport. Header,
wordmark, selected row, and bottom action controls are legible in the full-size
renders, so a separate magnified crop is not needed for this scoped change.

## Findings

No actionable P0, P1, or P2 differences in the selected background treatment.

- Fonts and typography: bundled Inter remains in use; the harness resolves its
  glyph typeface from embedded resources. Existing type scale, wrapping, and
  native control text are retained.
- Spacing and layout: the 208-unit navigation rail, 160-unit logo, page margins,
  grouped inventory, and bottom actions retain their established dimensions.
  Compact mode keeps the inventory and actions visible.
- Colors and tokens: graphite replaces the blue dark canvas, sidebar, and
  surfaces. Light mode uses white/silver. Blue and cyan remain accents. Controls
  and rows have opaque neutral backgrounds, and planes recede behind them.
- Image quality: separate generated light/dark background assets reproduce the
  broad intersecting lower planes, quiet upper region, and restrained cyan edge.
  Images preserve their aspect ratios and have no text or logo artifacts. The
  supplied logo is still independent artwork at its approved minimum width.
- Copy and content: existing navigation, labels, source state, and package
  context are retained. No concept titles or design instructions enter the UI.
- Interaction and accessibility: the decoration cannot receive focus or pointer
  input. The WPF workflow harness passes source editing, filtering, package
  maintenance, updates, and compact-layout checks. It also verifies switching
  embedded images between modes and completely removing artwork in simulated
  high contrast. There is no background animation or runtime asset download.

Intentional production constraints: dark-mode selected row text remains white
instead of the concept's cyan for readability; the dark header uses the approved
all-white logo variant suitable for graphite. The existing native control
templates and production layout take precedence over generated UI imprecision.

## Comparison history

The first implementation comparison passed without an actionable P0/P1/P2
finding; no visual correction loop was required. Asset inspection preceded
integration. Native high-contrast, Narrator, and every Windows scaling setting
remain separate release checks; rendered palette checks do not claim those tests.

## Implementation checklist

- [x] Embed both selected background variants and the light-mode approved logo.
- [x] Use neutral base colors and opaque content surfaces.
- [x] Preserve input behavior, keyboard focus, and high-contrast delegation.
- [x] Inspect full and compact light/dark renders against the selected concept.

final result: passed
