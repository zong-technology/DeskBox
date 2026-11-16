# DeskBox 2.1.0-hologram.4 installation verification

Source: 7c9e971790f61958349387fb05a0538c34b28bf1
Windows CI: https://github.com/zong-technology/DeskBox/actions/runs/43867333117

PASS: 47 setup, launch, upgrade, rollback and uninstall checks.
PASS: archive/build/installed/running executable SHA-256 identity.
Payload SHA-256: 1167223781ca763c654d855ea2dd0388a98a555806ce3e040f20671638ab2b90
PASS: 89 immersive hologram checks against the installed payload.
PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (96 DPI / 100%).
PASS: three workspace layouts captured at each window size.

All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.
The real installed candidate was launched and its normal exit was checked. Production user data was never used.
Not verified: every Windows 10/11 target, 125%/150% DPI, real paid voice calls, or user-specific historical migrations.
This candidate is unsigned and requires .NET Framework 4.8 or later.
