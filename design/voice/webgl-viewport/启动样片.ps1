$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:TEMP = Join-Path $PSScriptRoot 'evidence'
$env:TMP = $env:TEMP
$node = 'D:\Program Files\nodejs\node.exe'
if (!(Test-Path $node)) { $node = (Get-Command node -ErrorAction Stop).Source }
$ready = $false
try {
    $page = Invoke-WebRequest 'http://127.0.0.1:4178' -UseBasicParsing -TimeoutSec 2
    $ready = $page.Content.Contains('WebGL 全息视口样片')
} catch {}
if (!$ready) {
    Start-Process -FilePath $node -ArgumentList ('"' + (Join-Path $PSScriptRoot 'serve.cjs') + '"') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
    Start-Sleep -Milliseconds 800
}
Start-Process 'http://127.0.0.1:4178'

