# DeskBox 2.1.0-hologram.2 installation verification

Source: 6fdc0c7382b2203503193c6806ba0fb490a3d8cc
Windows CI: https://github.com/zong-technology/DeskBox/actions/runs/43738141992

PASS: 47 setup, launch, upgrade, rollback and uninstall checks.
PASS: archive/build/installed/running executable SHA-256 identity.
Payload SHA-256: b27d574b9c170ca90c285bff348dbfb31df58d1be6a415f428b6c3b7969fa1c2
PASS: 45 immersive hologram checks against the installed payload.
PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (96 DPI / 100%).
PASS: three workspace layouts captured at each window size.

All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.
The real installed candidate was launched and its normal exit was checked. Production user data was never used.
Not verified: every Windows 10/11 target, 125%/150% DPI, real paid voice calls, or user-specific historical migrations.
This candidate is unsigned and requires .NET Framework 4.8 or later.
