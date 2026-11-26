$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$report = Get-Content -LiteralPath (Join-Path $root 'dist\installer-qa\install-report.json') -Raw | ConvertFrom-Json
$environment = Get-Content -LiteralPath (Join-Path $root 'dist\installer-qa\installed-tia-environment.json') -Raw | ConvertFrom-Json
if ($report.Result -ne 'PASS') { throw 'Installer verification did not pass.' }
$lines = @(
    ('# DeskBox ' + $report.Version + ' installation verification'),
    '',
    ('Source: ' + $(if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { (& git -C $root rev-parse HEAD | Out-String).Trim() })),
    $(if ($env:GITHUB_RUN_ID) { 'Windows CI: https://github.com/' + $env:GITHUB_REPOSITORY + '/actions/runs/' + $env:GITHUB_RUN_ID } else { 'Builder: local Windows test account / VM' }),
    '',
    ('PASS: ' + $report.Count + ' setup, launch, upgrade, rollback and uninstall checks.'),
    'PASS: archive/build/installed/running executable SHA-256 identity.',
    ('Payload SHA-256: ' + $report.SourceSHA256.ToLowerInvariant()),
    ('PASS: ' + $report.NativeChecks + ' immersive hologram checks for the byte-verified installed payload.'),
    ('Windows: ' + $report.Windows + ' build ' + $report.WindowsBuild),
    ('PASS: 12-page keyboard navigation at 1600x900, 1280x800 and 1024x768 (' + $report.UIExpectedDpi + ' DPI).'),
    'PASS: three workspace layouts captured at each window size.',
    'PASS: the real installed DeskBox process accepts a physical mouse click, English input, Shift+Enter, Ctrl+A, Ctrl+C and Chinese clipboard paste in the TIA requirement editor.',
    ('PASS: ' + $report.TiaScreenshotCount + ' TIA screenshots captured directly from the installed DeskBox process.'),
    '',
    'All data was synthetic. The old executable used for byte-exact rollback checks was a separate executable fixture.',
    'The real installed candidate was launched and its normal exit was checked. Production user data was never used.',
    'BLOCKED in the default CI: Microsoft Pinyin composition, licensed TIA V20 work and live provider audio.',
    'This candidate is unsigned and requires .NET Framework 4.8 or later.'
)
$lines += '', '## Installed window DPI trials', ''
$lines += foreach ($trial in $environment.DpiMatrix) { $trial.Status + ' - ' + $trial.Dpi + ' DPI - ' + $trial.Evidence }
$lines | Set-Content -LiteralPath (Join-Path $root 'dist\installer\INSTALL-TEST-REPORT.md') -Encoding UTF8
