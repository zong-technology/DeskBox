# Twelve-page native workspace QA — 0.5.0

final result: passed

Scope: native layout, navigation, readable controls and interaction checks against the selected twelve-page hierarchy. This is not a claim of pixel-identical reproduction of the compressed reference collage.

## Evidence

- Source: `assets/ui-reference-12-pages.png`.
- Production WinForms controls rendered with isolated fixtures: `shots/reference-review/01-home.png` through `12-mobile.png`.
- Contact sheet: `shots/reference-review/all-12-pages.png`.
- Viewport: 1600×900; additional 1280×800 checks.
- `reference-ui-accept.cs`: 23 passed, including twelve routes, real project filtering/file search, calculation, QR generation, theme change, native MainForm integration and both directions of layout switching.
- Complete acceptance: 39 suites / 645 assertions / zero failures.

## Fixed during QA

- Dock order of titles, toolbars and content; no covering fill panel.
- Native green progress replaced by cyan project progress.
- Four task metric cards and twelve engineering tool cards.
- Workflows changed to list/vertical-preview split.
- Sidebar icon compositing and white button corners.
- Compact-height home progress and weather chart bounds.
- Startup mode now uses explicit state rather than inherited Visible; classic widgets cannot leak into the new layout.
- Actual settings checkbox switches back to the reference workspace without requiring restart.
- Real QR content is generated locally from the account URL; no imitation QR image.

## Known visual differences / follow-up

- Project names, task counts, percentages, files and weather are real data in production; fixtures are marked as samples in QA captures.
- Project cards reuse the existing high-resolution industrial background. Distinct project cover photos and optional user avatars are not inferred from the low-resolution collage.
- Native fonts, operating-system scaling, auxiliary dialogs and alternate-theme details differ from the raster source. No exact pixel match is asserted.
- Previous three-home-layout visual failures remain recorded in `docs/design-qa-legacy-three-layouts.md`; this report does not retroactively pass them.
