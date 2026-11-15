param([string]$OutDir = 'dist\validation-review')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll','/r:System.Management.dll','/r:Microsoft.VisualBasic.dll',"/r:$OutDir\DeskBox.exe")
$passed = 0; $failed = 0; $assertions = 0; $mock = $null
try {
    $listener = Get-NetTCPConnection -LocalPort 18080 -State Listen -ErrorAction SilentlyContinue
    if ($listener) { throw 'Local test port 18080 is occupied. Stop its owner before running acceptance.' }
    $node = (Get-Command node -ErrorAction Stop).Source
    $mock = Start-Process -FilePath $node -ArgumentList 'tools\mock-ai-server.js' -WorkingDirectory $root -WindowStyle Hidden -PassThru
    for ($i=0; $i -lt 20; $i++) {
        $probe = New-Object System.Net.Sockets.TcpClient
        try { $probe.Connect('127.0.0.1',18080); break } catch { Start-Sleep -Milliseconds 100 } finally { $probe.Dispose() }
    }
    foreach ($source in Get-ChildItem tools -Filter '*accept.cs' | Sort-Object Name) {
        $exe = Join-Path $OutDir ($source.BaseName + '.exe')
        $compiled = & $csc /nologo /target:exe "/out:$exe" $refs $source.FullName 2>&1
        if ($LASTEXITCODE -ne 0) { $failed++; Write-Output "COMPILE FAIL $($source.Name)"; $compiled; continue }
        $output = & $exe 2>&1; $code = $LASTEXITCODE
        $summary = $output | Where-Object { $_ -match 'PASS\s+\d+\s*/\s*FAIL\s+\d+' } | Select-Object -Last 1
        if ($summary -match 'PASS\s+(\d+)\s*/\s*FAIL\s+(\d+)') { $assertions += [int]$Matches[1] }
        if ($code -ne 0) { $failed++; Write-Output "FAIL $($source.Name)"; $output | Select-Object -Last 20 }
        else { $passed++; Write-Output "PASS $($source.Name) :: $summary" }
    }
    Write-Output "SUITES PASS $passed / FAIL $failed; ASSERTIONS PASSED $assertions"
} finally {
    if ($mock -and !$mock.HasExited) { Stop-Process -Id $mock.Id -Force }
}
if ($failed -gt 0) { exit 1 }
