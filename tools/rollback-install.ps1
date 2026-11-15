param([string]$InstallDir = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$target = [IO.Path]::GetFullPath($InstallDir)
$current = Join-Path $target 'DeskBox.exe'
$previous = Join-Path $target 'DeskBox.previous.exe'
if (-not (Test-Path -LiteralPath $previous -PathType Leaf)) { throw 'No previous version exists. Rollback did not change anything.' }
if (-not (Test-Path -LiteralPath $current -PathType Leaf)) { throw 'DeskBox.exe is missing in the selected installation.' }
$running = $null
try { $running = [Threading.Mutex]::OpenExisting('DeskBox.SingleInstance.7F3A') } catch [Threading.WaitHandleCannotBeOpenedException] { }
if ($running) { $running.Dispose(); throw 'Exit DeskBox from its tray menu before rolling back.' }

$stage = Join-Path $target ('DeskBox.rollback-' + [guid]::NewGuid().ToString('N') + '.tmp')
try {
    [IO.File]::Copy($previous, $stage, $false)
    [IO.File]::Replace($stage, $current, (Join-Path $target 'DeskBox.before-rollback.exe'))
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Force }
}
Write-Output ('Previous executable restored: ' + $current)
Write-Output 'The replaced candidate is DeskBox.before-rollback.exe. User data and API settings were not changed.'
