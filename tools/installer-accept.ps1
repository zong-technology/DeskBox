param([string]$SourceExe = 'dist\current-review\DeskBox.exe')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$source = (Resolve-Path -LiteralPath $SourceExe).Path
$target = Join-Path ([IO.Path]::GetTempPath()) ('deskbox-install-accept-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $target 'data') -Force | Out-Null
$marker = Join-Path $target 'data\preserve.txt'
[IO.File]::WriteAllText($marker, 'existing-user-data')
for ($attempt = 0; $attempt -lt 2; $attempt++) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'install.ps1') -InstallDir $target -SourceExe $source -NoShortcut
    if ($LASTEXITCODE -ne 0) { throw 'Installation or upgrade failed.' }
    if ((Get-FileHash -LiteralPath (Join-Path $target 'DeskBox.exe')).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Wrong executable installed.' }
    if ([IO.File]::ReadAllText($marker) -ne 'existing-user-data') { throw 'User data changed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $target 'DeskBox.previous.exe'))) { throw 'Rollback copy missing.' }
if (-not (Test-Path -LiteralPath (Join-Path $target 'tools\install-document-skills.ps1'))) { throw 'Document dependency installer missing.' }
if ((Get-ChildItem -LiteralPath (Join-Path $target 'docs') -File).Count -lt 4) { throw 'Installed documentation missing.' }
Write-Output 'PASS installer executable / upgrade / data preservation / rollback / document setup / docs'
Write-Output ('Test installation: ' + $target)
