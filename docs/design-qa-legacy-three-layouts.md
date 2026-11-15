# Three-layout visual reconstruction — ongoing

final result: blocked

The user's three supplied screenshots are the authoritative visual targets. The
previous layout-switch release does not pass visual acceptance against them.
Build and functional acceptance do not establish visual fidelity.

## Evidence

- Sources: `docs/visual-targets/mission.png`, `focus.png`, `system.png`.
- Native captures: `shots/visual-review/v30-home-mission.png`,
  `v30-home-focus.png`, `v30-home-system.png`.
- Viewport: 1672 x 941 pixels, matching the supplied source dimensions.
- Native application, no browser chrome or device frame; no density resampling.
- State: temporary project with 61% weighted progress and two tasks. System time,
  activity, document availability and live status differ intentionally from the
  source's illustrative values.
- Source and current Focus/System captures opened together in one comparison
  input. Full view and readable health, dependency and right-panel regions reviewed.

## Findings and ordered remaining work

1. P1 — Global shell differs. The source has a project picker, centered search,
   designed icons, job count and compact controls. Current shell still has the
   older command buttons and an extra homepage header. Rebuild those regions.
2. P1 — Focus right panel differs. Source has a wide AI collaborator with context
   documents, tabs and execution events. Current panel still shows mission/task/log
   cards. Implement actual context selection and execution events before marking
   this region complete.
3. P1 — System right panel differs. Source includes handoff checklist, deliverables
   and recommended action. Current generic mission panel is not equivalent.
4. P1 — Vertical rhythm differs. All source major regions fit within the frame;
   current Focus/System require scrolling. Reduce panel heights, remove duplicate
   content, and match source region bounds.
5. P1 — Focus stage strip must use connected circular workstream nodes, rather
   than the System chevrons currently reused there.
6. P2 — Typography differs: title sizes, uppercase labels, weights and baseline
   spacing need matching to the references. Several implementation hints are
   explanatory implementation text rather than product copy.
7. P2 — Colors/material differ: card fills remain too flat and light, background
   grid, border hierarchy and restrained depth need matching.
8. P2 — Asset fidelity: new embedded pharmaceutical image restores the subject
   direction, but is generated independently and is not pixel-identical to the
   source. Its placement/crop and the icon set remain unfinished.
9. P2 — Mission activity area still lacks source-style log table and detailed
   overview. System activity does not yet expose the source tabs and trends.

## Iterations

- Prior release: only rearranged existing dashboard cards. Rejected by user.
- Current iteration: Mission large ring and horizontal stage axis; actual daily
  log activity chart; Focus current-action region with working navigation; System
  dependency chevrons and non-clipped health summary; embedded factory asset.
- Fixed native navigation horizontal overflow by reserving scrollbar width.
- Captured all three layouts at source dimensions. Visual acceptance remains
  blocked by findings above. Functional Engineering UI acceptance: 27/27.

Do not present this iteration as a finished faithful reproduction. Do not install
over the running application until the visual reconstruction is ready.
