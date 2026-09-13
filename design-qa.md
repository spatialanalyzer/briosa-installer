# Design validation

## Current review: theme-aware interaction colors

Scope: selection, text highlighting, focus and interaction states in the existing
light/dark app. The background design and approved artwork are retained.

1. **Appearance and navigation — corrected.** Native review-build-14 captures
   confirmed cyan navigation selection in both themes. It provided only 2.02:1
   contrast against the light sidebar. White on deep blue now gives 11.52:1 text
   contrast in light mode; deep blue on cyan gives 5.10:1 in dark mode. The
   control-tree check also caught style-scoped brushes retaining the old theme.
   Moving the local brushes onto the live navigation ListBox fixes theme changes
   while retaining the Fluent control template.
2. **Settings inputs — corrected.** Selected text previously relied on a translucent
   highlight and an independently chosen Windows foreground. TextBox and PasswordBox
   now use explicit opaque theme-matched pairs, including system highlight pairs
   in high contrast. Native testing caught WPF's legacy adorner renderer covering
   selected letters despite correct brush properties. The app and harness now
   opt into non-adorner selection rendering at process startup. Selected Settings
   tabs use an accent border, tint and text.
3. **Package selections and actions — corrected.** Selected rows use a theme tint
   and a visible accent edge, including updater tables. Primary buttons and checked
   controls share paired fills/foregrounds; light-theme hover/press darkens blue,
   while dark-theme hover/press lifts cyan. Neutral controls get distinct pressed
   fills and stronger hover/press boundaries. Existing focus rings remain visible.

Evidence for this review is under `artifacts/color-audit/`: native before captures
`01-before-dark-selection.png` and `02-before-light-navigation.png`; actual WPF
control renders `controls.png.light-install-action.png`, `.dark-install-action.png`,
`.light-sources.png`, `.dark-sources.png`, `.light-updates.png`, `.dark-updates.png`,
and the light/dark compact and scaled variants. Text-selection pixel verification
uses a native window; offscreen renders alone do not display active text selection.
Native review-build-16 captures `03-after-dark-selection.png`,
`04-after-light-navigation.png`, and `05-after-light-selection.png` confirm readable
selected letters and immediate navigation palette changes in the packaged executable.
The 119 core/CLI tests, WPF workflow/contrast checks, locked build, and review-16
packaged workflows passed. Local documentation links and original brand hashes
were also checked. Review build 15 was superseded during native verification.

The contrast harness checks resolved navigation template brushes after theme switches,
TextBox/PasswordBox selection properties, 4.5:1 normal text, and 3:1 focus/selection
indicators and interactive control boundaries. It preserves system high-contrast
delegation. These checks do not establish full accessibility compliance, native
high-contrast behavior, Narrator support, or all Windows DPI combinations.

Control resource mappings were checked against the official WPF sources for
[text fields](https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/TextBox.xaml),
[navigation](https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Styles/ListBoxItem.xaml),
and [focus rings](https://github.com/dotnet/wpf/blob/v10.0.0/src/Microsoft.DotNet.Wpf/src/Themes/PresentationFramework.Fluent/Resources/DefaultFocusVisualStyle.xaml).

## Earlier review: automatic settings and deeper dark mode

The maintainer rejected the manual Save/Discard workflow and requested substantially
darker backgrounds. Settings now apply and persist automatically. Text fields wait
450 ms after typing; focus loss, navigation, and closing flush pending values.
Authentication, approved publisher choices, and credentials also apply automatically.
Fingerprint approval and credential removal retain their deliberate review actions.
Failed writes and invalid source input expose recovery rather than pretending to save.

The dark workspace is now #1E1F21, its sidebar #1A1B1C, cards #28292B, and controls
#2E2F31, with subtler vector edges. White text on the card color is approximately
14.5:1 contrast. The light palette and transparent icon remain unchanged.

Reviewed the actual WPF renders under `artifacts/ux-renders/`: `autosave-servers.png.dark.png`,
`autosave-servers.png.appearance-dark.png`, `autosave-servers.png.appearance-light.png`,
and `autosave-servers.png.appearance-compact.png`. The 1140 × 800 and 820 × 580
views retain readable labels, all four Settings tabs, accessible controls, and
subtle background planes. Routine Save/Discard buttons and the unsaved badge are gone.

The 119 core/CLI tests and WPF workflow harness passed. Coverage includes automatic
source persistence, rapid theme changes, reopening, flushing on close, source-free
appearance, invalid source text with independent theme persistence, concurrent-file
conflicts, locked-file write failure/retry, pending catalog reads, and automatic
credential storage restricted to the selected catalog. Existing package workflows
still pass. No real SA or native Windows appearance setting was changed.

The following sections retain the earlier visual review history; the automatic
settings flow and darker palette above supersede their Save/Discard and brightness details.

## Earlier Layered Planes vector refinement

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

The first icon revision incorrectly treated the reported problem as vertical
centering. The maintainer clarified that the concern was horizontal centering and
requested the transparent three-color symbol instead of the blue background tile.
The vertical translation is removed. The current icon is a direct export of the
approved inverse symbol: white, silver-gray, and cyan-blue planes, preserving the
original geometry and aspect ratio inside transparent square frames.

All ten decoded ICO frames have exactly equal left and right transparent margins:
1 px at 16/20, 2 px at 24/32, 3 px at 40, 4 px at 48, 5 px at 64, 8 px at 96,
10 px at 128, and 21 px at 256. Corner alpha is zero at every size. Reviewed
`artifacts/icon-review/transparent-256.png`, `transparent-32.png`, and
`transparent-24.png`. The icon has no tile, clipping, or nonuniform scaling.
The executable, launcher, and WPF windows use this same ICO. Original brand files
remain byte-exact and the export is recorded in `Assets/AppIcon/derivation.json`.

The native app title-bar icon was inspected. The computer-use window inventory
does not expose the Windows taskbar, so direct taskbar placement and stale pinned
shortcut behavior remain for the maintainer's visual review. No Windows icon cache
or pinned shortcut was modified. Native contrast themes and mixed-DPI checks remain
subject to the release limitations above.

## Settings section underline follow-up

Review build 17 replaces the filled Settings section headers with transparent
headers and a 3 px underline beneath the selected label. The label and underline
use deep blue in light mode and cyan in dark mode. Selected navigation text now
matches the sidebar charcoal in dark mode, retaining the existing cyan fill.

Reviewed the light/dark appearance renders and the 820 × 580 compact render under
`artifacts/underline-review`. All four complete labels fit on one row. Spacing is
inside the header template because native TabPanel layout excludes item margins
when allocating width; using an outer margin initially clipped the labels.

The packaged app was checked in both themes with immediate theme switching.
Ctrl+Tab and Ctrl+Shift+Tab change the selected section and its underline; keyboard
focus on a header retains a visible native focus ring. Settings continue to save
automatically. All 119 core/CLI tests, WPF smoke workflows, packaged CLI/launcher
checks, 56 local documentation links, and 13 original brand asset hashes passed.
The native contrast-theme and mixed-DPI release limitations above still apply.

## Settings cursor and navbar symbol follow-up

Review build 18 moves the hand cursor from TabItem to its header template, preventing
cursor inheritance into Settings content. The dark navbar now uses the supplied
inverse-color horizontal logo with the same three-color symbol as the taskbar icon;
the supplied wordmark and light-mode logo are retained.

Reviewed the dark appearance render under `artifacts/cursor-brand-review` and the
packaged app on Settings, including interaction with the header and blank card
content. The content displays the normal pointer. All 119 core/CLI tests, WPF smoke
workflows, package checks, documentation links, and original asset hashes passed.

## SDK discovery follow-up

Review build 20 fixes missing SDK files when SA uninstall entries omit
InstallLocation, identifies existing candidate files behind unquoted registration
paths while preserving launch ambiguity, normalizes comma-separated file versions,
and consolidates matching machine/merged registry evidence. Configured registration
is listed first and selected; a wrapping status column explains each observation.

Reviewed the SDK light/dark and compact renders under
`artifacts/sdk-discovery-review`. Native read-only inspection on the maintainer's
workstation found three SDK files and one configured registration, matching the
separately read installation and registry evidence. No COM activation, SA connection,
or registration change was performed. The 147 core/CLI tests (including 28 new
discovery cases), WPF workflows, package checks, documentation links, and original
brand hashes passed. Runtime SDK identity and broader deployment checks remain
outside this inspection.

## SDK installation overview follow-up

Review build 21 consolidates SDK Setup into one row per SA installation, with
its installation directory and a checkmark plus Registered label matched to the
full registered SDK path. The summary leads with the configured SDK version;
discovery caveats appear beneath it and complete registry evidence remains in
Registration details. Selecting a row does not change registration or its marker.

Reviewed the light/dark and compact SDK renders under
`artifacts/sdk-overview-review`. Paths wrap at 820 × 580 without hiding the three
fixture installations or footer actions. Native read-only inspection in the
packaged app shows the workstation's three installations and identifies
2024.1.0508.5 as configured. The checkmark remains on that installation when
another row is selected, and the registration details dialog retains the
registered executable path and unquoted-path finding.

The 147 core/CLI tests, expanded WPF presentation/workflow checks, complete
publishing and packaged CLI/launcher checks, 56 local documentation links,
and 13 original brand asset hashes passed. No COM activation, SA connection,
or registration change was performed. Native high contrast, Narrator, and
mixed-DPI release checks remain subject to the limitations above.

## SDK row information follow-up

Review build 22 removes SDK Setup's handoff export and bottom details buttons.
A small vector information icon in each row opens one dialog containing the
chosen installation's path and registration status plus the workstation's
registration evidence. Each icon has a 36 px hit area, descriptive accessible
name including the SA version, and an installation-path help description.
Support-report export remains on Activity.

Reviewed dark and compact light renders under `artifacts/sdk-row-details-review`:
all three rows retain readable paths, registration markers, and visible information
icons. Native packaged review confirmed that clicking the unselected 2026 row's
icon opens its own installation details while identifying 2024 as the workstation's
configured SDK. Escape closes the dialog and returns to the table.

The Release build, 147 core/CLI tests, WPF workflow checks, complete publishing,
packaged CLI/launcher checks, 57 local documentation links, and 13 original brand
hashes passed. Inspection remains read-only. The native high-contrast, Narrator,
and mixed-DPI release limitations above still apply.

## Automatic SDK loading follow-up

Review build 23 loads local SDK evidence on each visit to SDK Setup. Refresh now
sits above the summary at the upper right, with the last update time on the left.
The scan runs independently of package sources and app mutations; it does not
disable navigation or unrelated Settings. In-progress reads are shared across
repeat visits, and late results are ignored after the window closes.

Reviewed the dark and compact light renders under `artifacts/sdk-auto-refresh-review`.
The toolbar, summary, three installation rows, and row information icons fit in
both layouts. Native review confirmed that first navigation populates all three
workstation installations without clicking a scan button, and Refresh rereads the
same setup successfully.

The expanded WPF harness covers early navigation during startup, no configured
source, held scans, overlapping refresh attempts, page-return reloads, empty
results replacing stale rows, failure/retry, and closing during a scan. The Release
build, all 147 core/CLI tests, WPF checks, package publishing and packaged workflows,
57 local documentation links, and 13 original brand hashes passed. The inspection
remains read-only; native accessibility and mixed-DPI limitations above still apply.

## Reviewed SDK registration changes

Review builds 24–25 add Change SDK beside Refresh. The selection dialog lists
installed SA releases and shows the chosen installation path. A separate review
shows the current SDK, requested SDK, and executable before Windows elevation.
Read-only evidence remains selectable during maintenance; actions are disabled.
This avoids the native disabled table's white background in dark mode.

The shared GUI/CLI engine validates the installed vendor procedure, Windows
Authenticode signer, protected local installation, idle processes, and unchanged
reviewed setup. It records pre-change evidence privately and verifies registration
after the vendor command exits. It does not launch an SDK client or issue MP work.

On 2026-09-13, the maintainer-authorized live test used the implemented CLI and
shared engine to change registration from 2024.1.0508.5 to 2026.1.0529.7 and back
to 2024.1.0508.5. Both vendor commands exited successfully and both resulting paths
and versions passed fresh registration verification. An independent final registry
read confirmed the restored 2024 executable; no SA/SDK/Briosa server processes
remained. Evidence is retained locally under `artifacts/sdk-registration-review`
and the private maintenance directory described in `docs/sdk-registration.md`.
No vendor binaries, registry exports, or private machine records are committed.

Native packaged review exercised selection, the real read-only preflight, the
current/selected version review, and cancellation without a further mutation.
Dark and compact light renders retain readable paths and visible actions.
The Release build, all 167 core/CLI tests (including 20 registration cases), WPF
workflow checks, complete publishing, packaged CLI/launcher workflows, 63 local
documentation links, and 13 original brand hashes passed. This validates the
observed registration procedure, not runtime COM activation, MP readiness, a wider
SA support matrix, enterprise rollout, Narrator, or mixed-DPI behavior.

## Newer installed SDK advisory

Review build 26 shows a non-blocking recommendation in SDK Setup when complete
local evidence identifies an older configured SDK and an available bundled SDK
for the newest installed SA release. The message strongly recommends that newer
version for most users while explicitly allowing intentional older-SDK workflows.
It uses the existing theme-aware blue accent, a text heading, and a polite live
announcement. It does not open a dialog, disable actions, change registration,
or classify the configuration as a failed operation.

The comparison uses all four numeric release components. Equal versions with
different zero padding and newer registered versions do not trigger an advisory.
Incomplete discovery, unknown or conflicting effective versions, missing SDK
files, and service registration retain their existing diagnostic behavior.
Refreshed evidence updates or clears the message.

The Release build, 167 core/CLI tests, expanded WPF comparison and refresh checks,
packaged CLI/launcher workflows, 63 local documentation links, and 13 original
brand hashes passed. Dark and compact light renders under
`artifacts/sdk-advisory-review` retain readable advice and a scrollable installation
table. Native packaged review confirmed automatic display of the 2026.1.0529.7
recommendation with 2024.1.0508.5 still registered. No SDK registration mutation or
runtime SA connection was performed for this change. Existing native accessibility
and mixed-DPI validation limitations still apply.

## Registration warning wording and recommended selection

Review build 27 titles the advisory "SDK registration warning" and describes the
mismatch with the latest SA release installed on the machine without embedding a
release number or advertising a software update. Intentional older-SDK use remains
valid. Change SDK labels each available copy of the newest local release
"Recommended"; this uses the same numeric comparison and local file evidence as
the warning, without selecting or registering it automatically.

Expanded WPF checks cover unordered versions, fourth-component ordering, equivalent
release copies, incomplete discovery, missing SDK files, no installations, and
preserving the selected installation path for both recommended and older choices.
Native Build 27 review verified the revised warning and the open dropdown showing
2026.1.0529.7 as Recommended, including that text in the accessibility tree.
The workstation's configured SDK remains 2024.1.0508.5; this change performed no
registration mutation or SA connection.

Release build, 167 core/CLI tests, WPF workflow checks, package publishing and
packaged CLI/launcher workflows, 63 local documentation links, and 13 original
brand hashes passed. Dark and compact light renders are retained under
`artifacts/sdk-registration-guidance-review`. Existing accessibility and mixed-DPI
release-validation limits remain unchanged.

## Amber warning accents

Review build 28 uses complementary amber for the SDK warning heading and left
rule: #9C5700 in light mode and #FFC46B in dark mode. Their contrast against the
corresponding card surfaces is 4.97:1 and 9.26:1. Body text retains its normal
color. High contrast maps the warning accent to system window text color, and
the explicit warning heading remains a non-color cue.

Dark and compact light renders under `artifacts/sdk-warning-colors-review` were
inspected. Native packaged review confirmed the amber treatment on SDK Setup.
The Release build, 167 core/CLI tests, WPF checks, packaged CLI/launcher workflows,
63 documentation links, and 13 original brand hashes passed. This styling change
does not alter recommendation or registration behavior. Native high-contrast,
Narrator, and mixed-DPI release checks retain their previously recorded limits.
