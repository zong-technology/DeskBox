$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$report = Get-Content -LiteralPath (Join-Path $root 'dist\installer-qa\install-report.json') -Raw | ConvertFrom-Json
if ($report.Result -ne 'PASS') { throw 'Installer verification did not pass.' }
$lines = @(
    '# DeskBox 2.1.0-hologram.5 installation verification',
    '',
    ('Source: ' + $env:GITHUB_SHA),
    ('Windows CI: https://github.com/' + $env:GITHUB_REPOSITORY + '/actions/runs/' + $env:GITHUB_RUN_ID),
    '',
    ('PASS: ' + $report.Count + ' setup, launch, upgrade, rollback and uninstall checks.'),
    'PASS: archive/build/installed/running executable SHA-256 identity.',
    ('Payload SHA-256: ' + $report.SourceSHA256.ToLowerInvariant()),
    ('PASS: ' + $report.NativeChecks + ' immersive hologram checks against the installed payload.'),
    'PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (96 DPI / 100%).',
    'PASS: three workspace layouts captured at each window size.',
    '',
    'All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.',
    'The real installed candidate was launched and its normal exit was checked. Production user data was never used.',
    'Not verified: every Windows 10/11 target, 125%/150% DPI, real paid voice calls, or user-specific historical migrations.',
    'This candidate is unsigned and requires .NET Framework 4.8 or later.'
)
$lines | Set-Content -LiteralPath (Join-Path $root 'dist\installer\INSTALL-TEST-REPORT.md') -Encoding UTF8
