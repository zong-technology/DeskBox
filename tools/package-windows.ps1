param([string]$Iscc = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'dist\DeskBox.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build dist\DeskBox.exe first.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw ('Unexpected candidate version: ' + $version) }
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
if ($fileVersion -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'A four-part Windows file version is required.' }
if (-not $Iscc) { $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $Iscc)) { throw 'Install Inno Setup 6.5+ on the build machine.' }
$payloadHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
& $Iscc ('/DReleaseVersion=' + $version) ('/DFileVersion=' + $fileVersion) ('/DPayloadSHA256=' + $payloadHash) (Join-Path $root 'packaging\DeskBox.iss')
if ($LASTEXITCODE -ne 0) { throw 'Windows setup compilation failed.' }
$output = Join-Path $root 'dist\installer'
$setup = Join-Path $output ('DeskBox-' + $version + '-Setup.exe')
$lines = foreach ($file in @($setup, $exe)) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    $hash + '  ' + [IO.Path]::GetFileName($file)
}
$lines | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ascii
