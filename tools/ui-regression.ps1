param(
    [Parameter(Mandatory=$true)]
    [ValidateSet(96,120,144)]
    [int]$ExpectedDpi
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$relative = 'dist\ui-qa-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')
$output = Join-Path $root $relative
New-Item -ItemType Directory -Path $output | Out-Null
$oldTemp = $env:TEMP
$oldTmp = $env:TMP
Push-Location $root
try {
    $env:TEMP = $output
    $env:TMP = $output
    & .\build.ps1 -OutDir $relative -NoPause
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $render = Join-Path $PSScriptRoot 'render-v4-ui.cs'
    $smoke = Join-Path $PSScriptRoot 'smoke-ui.cs'
    $compare = Join-Path $PSScriptRoot 'visual-compare.cs'
    & $csc /nologo /codepage:65001 /main:Smoke "/out:$output\smoke-ui.exe" "/r:$output\DeskBox.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $render $smoke
    if ($LASTEXITCODE) { throw 'Smoke compilation failed.' }
    & $csc /nologo /codepage:65001 "/out:$output\render-v4-ui.exe" "/r:$output\DeskBox.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll $render
    if ($LASTEXITCODE) { throw 'Renderer compilation failed.' }
    & $csc /nologo /codepage:65001 "/out:$output\visual-compare.exe" /r:System.Drawing.dll $compare
    if ($LASTEXITCODE) { throw 'Visual comparer compilation failed.' }

    $dpiMatched = $true
    foreach ($size in '1600x900','1280x800','1024x768') {
        $width, $height = $size -split 'x'
        $capture = Join-Path $output "requested-dpi-$ExpectedDpi-$size"
        $log = Join-Path $output "$size.log"
        & "$output\smoke-ui.exe" $capture $width $height | Tee-Object -FilePath $log
        if ($LASTEXITCODE) { throw "Navigation failed at $size. See $log" }
        if (-not (Select-String -LiteralPath $log -SimpleMatch "window {Width=$width, Height=$height}" -Quiet)) {
            throw "Unexpected window size for $size. See $log"
        }
        if (-not (Select-String -LiteralPath $log -Pattern "window .* DPI $ExpectedDpi MONITOR SCALE $($ExpectedDpi*100/96)$" -Quiet)) {
            $dpiMatched = $false
            Write-Warning "DPI mismatch for $size. See $log."
        }
        if (@(Get-ChildItem -LiteralPath $capture -Filter '*.png' -File).Count -ne 12) {
            throw "Expected 12 screenshots at $size."
        }
        & "$output\render-v4-ui.exe" "$capture-layouts" $width $height | Tee-Object -FilePath (Join-Path $output "$size-layouts.log")
        if ($LASTEXITCODE) { throw "Layout capture failed at $size." }
        foreach ($layout in 'mission','focus','system') {
            if (-not (Test-Path -LiteralPath "$capture-layouts\v30-home-$layout.png")) {
                throw "Missing $layout screenshot at $size."
            }
        }
    }
    Write-Host "Capture and navigation complete: $output"
    if (-not $dpiMatched) { throw "Captures are diagnostic only. The requested DPI was not verified: $output" }
} finally {
    Pop-Location
    $env:TEMP = $oldTemp
    $env:TMP = $oldTmp
}
