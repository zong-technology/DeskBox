# DeskBox 2.1.0-hologram.1 installation verification

Source: b88773b2c3506317d151037c786916c4e8e65c60
Windows CI: https://github.com/zong-technology/DeskBox/actions/runs/43732489699

PASS: 44 setup, launch, upgrade, rollback and uninstall checks.
PASS: archive/build/installed/running executable SHA-256 identity.
Payload SHA-256: 1521110f4eb5439ee9dc294389d75156ce929bdde0bf92c65a03461745fbe2fa
PASS: 183 hologram and style checks against the installed payload.
PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (96 DPI / 100%).
PASS: three workspace layouts captured at each window size.

All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.
The real installed candidate was launched and its normal exit was checked. Production user data was never used.
Not verified: every Windows 10/11 target, 125%/150% DPI, real paid voice calls, or user-specific historical migrations.
This candidate is unsigned and requires .NET Framework 4.8 or later.
