---
name: deskbox-visual-compare
description: Compare matching DeskBox screenshots and diagnose visual changes using PNG differences and their measured bounds.
---

Use the visual comparer in `docs/UI-TOOLCHAIN.md`. Use one screenshot per route with the same viewport, DPI, theme, capture method, fixture and UI state. Keep baseline, candidate and output separate on D:. Inspect magenta regions and changed bounds in report.md. Missing files or dimension mismatch invalidate the comparison. Review dynamic UI regions explicitly before agreeing on any tolerance. Use the design atlas for human review until aligned per-page exports exist.
