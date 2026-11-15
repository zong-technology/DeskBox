---
name: winforms-layout-audit
description: Audit DeskBox WinForms controls for clipping, overflow, scaling and layout at different window sizes and DPI.
---

Use the matrix in `docs/UI-TOOLCHAIN.md`. Record both WinForms DeviceDpi and the monitor scale. Test with actual Windows scaling in an isolated desktop; an enlarged PNG does not count. Examine page headings, toolbars, scroll behavior, tab order, minimum widths, focus indicators, and long Chinese labels. Review Mission, Focus and System as separate layouts. Identify each finding with route, scale, window size and screenshot; report untested cells explicitly.
