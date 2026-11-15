---
name: deskbox-release-review
description: Review DeskBox installed or packaged Windows builds for release identity, isolated upgrade safety, UI and rollback readiness.
---

Follow the installed-build section in `docs/UI-TOOLCHAIN.md`. Compare installed, unpacked and archived executable hashes, and verify the running path. Run the candidate in a disposable Windows profile or VM with D: fixtures, offline or with test accounts only. Recheck 12-page navigation, three workspace layouts and actual monitor DPI. Confirm install, upgrade data retention, tray exit, icon restoration and rollback on copied fixtures. Keep historical logs, candidate-host results and installed-version results separate. Never modify production DeskBox data to create release evidence.
