# Layered Planes vector refinement

Source direction: `docs/design/layered-planes-reference.png` (1983 × 793), plus
the maintainer's request to replace the background PNGs after observing uneven
colors, pixelated lines, and distracting visual artifacts in review build 10.

## Correction

The earlier raster comparison did not catch those material-quality defects.
The background now uses native WPF geometry, controlled solid fills, and a short
fading cyan stroke. The background PNGs, their resource inclusion, and their
generation notes are removed. No bitmap cache is used. The reference PNG remains
documentation only; it is not an application resource.

## Validation evidence

The WPF smoke harness renders the actual control tree in the same Installations
state as the concept: one fictional SA target with installed 0.2.0 and available
0.1.0 rows, selected installed version, and contextual maintenance controls.

- `artifacts/ux-renders/vector-servers.png.light.png` and `.dark.png`:
  1140 × 800 logical pixels at 96 DPI.
- `artifacts/ux-renders/vector-compact.png.light.png` and `.dark.png`:
  820 × 580 logical pixels at 96 DPI.
- `artifacts/ux-renders/vector-servers.png.light-150.png`:
  1140 × 800 logical pixels rendered to 1710 × 1200 pixels at 144 DPI.
- `artifacts/ux-renders/vector-servers.png.dark-200.png`:
  1140 × 800 logical pixels rendered to 2280 × 1600 pixels at 192 DPI.

These are WPF rendering scales, not a claim to testing Windows display settings
or moving the native window between monitors. The source is a presentation board
with margins and mode labels; comparisons use its app content, not the outer
canvas. Production layout and type sizes remain authoritative.

## Findings after correction

The reviewed light/dark and compact renders retain the selected broad-plane
composition with even surfaces. The 150% light and 200% dark renders were opened
at their original pixel dimensions: the long diagonal strokes are rendered
directly at those resolutions, without the previous baked-in jagged lines,
grain, or mottling. The surfaces deliberately use flat fills rather than imitating
the generated image's lighting texture. No actionable visual defect was found
in these renders.

Typography, spacing, navigation, and content retain their established production
values. The decoration remains behind opaque controls and cannot intercept input.
The WPF harness verifies geometry-only light/dark backgrounds and replacement
with the plain system brush in simulated high contrast. The 107 core/CLI tests,
WPF workflows, and local documentation links passed.

Native high contrast, Narrator, and mixed-DPI monitor behavior remain separate
release checks; these rendering checks do not establish those results.

final result: passed

## Appearance preference and application icon follow-up

The maintainer requested an in-app theme selector and investigation of the
taskbar icon's placement. Settings now includes an Appearance tab with a named
System/Light/Dark combo box, immediate preview, and the existing fixed Save/Discard
controls. The saved preference is applied application-wide before native startup.

Reviewed `artifacts/ux-renders/appearance-servers.png.appearance-dark.png`,
`.appearance-light.png`, and `.appearance-compact.png`: all four Settings tabs,
the complete appearance card, and save actions fit at 1140 × 800 and 820 × 580.
Text and controls remain readable against the neutral planes in both modes.
The WPF workflow check covers reopening, discard, external-file conflict/reload,
System selection, explicit-mode refresh, and a theme save during a pending catalog
read. Theme-only saves preserve catalog results. The 118 core/CLI tests pass.

The original icon's blue tile is centered, but its symbol's filled-area centroid
at 256 px is approximately (127.8, 141.1), below the canvas center (127.5, 127.5).
The application derivative moves the intact symbol upward; its centroid is now
approximately (127.8, 127.6). Reviewed original and adjusted exports at 256, 32,
and 24 px. It retains the approved geometry, colors, and small-size optical variant.
Ten native ICO sizes are shared by the executable, launcher, and WPF windows.
The original brand files remain byte-exact, with the derivative separately recorded.

The native app title-bar icon was inspected. The computer-use window inventory
does not expose the Windows taskbar, so direct taskbar placement and stale pinned
shortcut behavior remain for the maintainer's visual review. No Windows icon cache
or pinned shortcut was modified. Native contrast themes and mixed-DPI checks remain
subject to the release limitations above.
