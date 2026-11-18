param(
    [string]$Iscc = '',
    [switch]$Verify,
    [switch]$IsolatedTestAccount
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
if ($env:OS -ne 'Windows_NT') { throw 'Run this entry point on Windows. It does not use GitHub Actions.' }
if ($Verify -and -not $IsolatedTestAccount) { throw 'Full installation tests require a disposable Windows user/VM. Pass -IsolatedTestAccount only in that environment.' }
if (-not $Iscc) { $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
$installerAvailable = Test-Path -LiteralPath $Iscc
if ($Verify -and -not $installerAvailable) { throw 'Install Inno Setup 6.5+ before running full verification.' }
& (Join-Path $root 'build.ps1') -OutDir dist -NoPause
$exe = Join-Path $root 'dist\DeskBox.exe'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'The compiled product version is not safe for a release filename.' }
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,6)
$output = Join-Path $root ('dist\local-release\windows-' + $version + '-' + $stamp)
New-Item -ItemType Directory -Path $output | Out-Null
if ($installerAvailable) {
    & (Join-Path $root 'tools\package-windows.ps1') -Iscc $Iscc
    Copy-Item -LiteralPath (Join-Path $root ('dist\installer\DeskBox-' + $version + '-Setup.exe')) -Destination $output
}
if ($Verify) {
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $csc /nologo /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ('/r:'+$exe) ('/out:'+(Join-Path $root 'dist\silverwing-accept.exe')) (Join-Path $root 'tools\silverwing-accept.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Native acceptance host compilation failed.' }
    & (Join-Path $root 'dist\silverwing-accept.exe') $exe (Join-Path $root 'dist\hologram-qa')
    if ($LASTEXITCODE -ne 0) { throw 'Native acceptance failed.' }
    & (Join-Path $root 'tools\setup-accept.ps1') -SetupPath (Join-Path $output ('DeskBox-'+$version+'-Setup.exe'))
    & (Join-Path $root 'tools\write-install-report.ps1')
    Copy-Item -LiteralPath (Join-Path $root 'dist\installer\INSTALL-TEST-REPORT.md') -Destination $output
}
$source = ''; $dirty = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $source = (& git rev-parse HEAD 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -eq 0) { $dirty = [bool](& git status --porcelain) } else { $source = '' }
}
$stage = Join-Path $output 'DeskBox'
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath $exe -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'docs\WINDOWS-INSTALL.md') -Destination $stage
$manifest = [ordered]@{ Product='DeskBox'; Version=$version; SourceSHA=$source; Dirty=$dirty; BuiltAtUTC=[DateTime]::UtcNow.ToString('o'); Builder='local-windows'; Platform='Windows .NET Framework 4.8'; NativeAndInstallerVerified=[bool]$Verify; SetupIncluded=[bool]$installerAvailable; PayloadSHA256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'BUILD-MANIFEST.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $output 'BUILD-MANIFEST.json') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'packaging\RELEASE-NOTES.md') -Destination (Join-Path $output 'RELEASE-NOTES.md')
if (-not $Verify) { 'CANDIDATE: compiled locally; native UI, installation/upgrade and real voice calls have not been verified for these bytes.' | Add-Content -LiteralPath (Join-Path $output 'RELEASE-NOTES.md') -Encoding UTF8 }
Compress-Archive -LiteralPath $stage -DestinationPath (Join-Path $output ('DeskBox-'+$version+'-Portable.zip'))
Remove-Item -LiteralPath $stage -Recurse -Force
$hashes = foreach ($file in Get-ChildItem -LiteralPath $output -File) { (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $file.Name }
$hashes | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
Write-Host ('Local release: ' + $output) -ForegroundColor Green
if (-not $installerAvailable) { Write-Host 'Portable ZIP created. For a Setup.exe, install Inno Setup 6.5+ once and run again.' -ForegroundColor Yellow }
Write-Host ('Full native/installer verification: ' + [bool]$Verify)
