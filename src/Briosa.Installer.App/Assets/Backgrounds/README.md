# Layered Planes backgrounds

Application-specific decorative artwork for the maintainer-selected Layered Planes
design, September 13, 2026. These are separate from the approved upstream logo,
palette, and font files in `Assets/Brand`. The repository license applies.

Both images were generated with the built-in Image Gen tool using
`docs/design/layered-planes-reference.png`, then inspected and copied unchanged.
They contain no interface controls, text, logos, or external references.

| Asset | Pixels | SHA-256 |
| --- | --- | --- |
| `layered-planes-light.png` | 1348 × 1167 | `0738e6bdbfdd839460a6b9bf5880cc4016ae72bd7e78756edad1c40d76cf01ec` |
| `layered-planes-dark.png` | 1349 × 1166 | `3f12abd60a9b89bb0b29fac068f1b3f17ae2341b8af403b17b11b88a6cdbaa2e` |

Prompt direction: isolated full-bleed workspace background, quiet upper area,
two or three broad intersecting matte planes in the lower half, long diagonal
edges, low-contrast hairlines, and a restrained far-right cyan edge. Light mode
uses white/silver; dark mode uses neutral graphite centered on `#585B62`.
Exclude UI, text, logos, frames, heavy texture, dramatic bevels, and blue floods.
The requested 1536 × 1328 canvas was returned at the dimensions above with the
same approximate aspect ratio; the original image dimensions are preserved.

Generation identifiers: light `exec-7ad3d523-e508-426d-93a2-173ce631312f`;
dark `exec-749b2144-810e-49f8-97fa-1a09b1da0641`.

WPF embeds the images and caches decoded, frozen bitmaps. An aspect-preserving
image brush crops at the workspace edges on resize. The decorative element never
participates in input or focus. High contrast replaces it with the system window
brush, and all assets remain available offline.
