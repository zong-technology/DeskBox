---
name: deskbox-ui-regression
description: Run and diagnose DeskBox 12-page navigation, layout capture and visual regression before accepting UI changes.
---

Run `tools/ui-regression.ps1 -ExpectedDpi 96` at 100% scaling; use 120 and 144 for the other actual Windows scale sessions. Follow `docs/UI-TOOLCHAIN.md`. Require 12 unique keyboard navigation passes and 12 route screenshots at each window size. Inspect the three workspace layout captures separately. Only use synthetic D: data. A compiled EXE or screenshot count alone cannot satisfy an interaction or design acceptance criterion.
