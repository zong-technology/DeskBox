# DeskBox 2.1.0-hologram.3 installation verification

Source: c02a4867f9adafd58b2ca74cb5203469ac65952b
Windows CI: https://github.com/zong-technology/DeskBox/actions/runs/43858767928

PASS: 47 setup, launch, upgrade, rollback and uninstall checks.
PASS: archive/build/installed/running executable SHA-256 identity.
Payload SHA-256: 1c173ff77ddaa866749b47ea065b5790f21aee09dfe7272b0788114f56df815f
PASS: 71 immersive hologram checks against the installed payload.
PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (96 DPI / 100%).
PASS: three workspace layouts captured at each window size.

All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.
The real installed candidate was launched and its normal exit was checked. Production user data was never used.
Not verified: every Windows 10/11 target, 125%/150% DPI, real paid voice calls, or user-specific historical migrations.
This candidate is unsigned and requires .NET Framework 4.8 or later.
