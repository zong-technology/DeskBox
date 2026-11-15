# 构建 DeskBox.exe（.NET Framework 4.8 自带 csc，无需安装任何依赖）
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1 [-OutDir .\dist]
param(
  [string]$OutDir = "dist",
  [switch]$NoPause
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw "找不到 .NET Framework 编译器: $csc" }

$src = Get-ChildItem -Path "src" -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
if ($src.Count -eq 0) { throw "src 下没有找到 .cs 文件" }

New-Item -ItemType Directory -Force -Path (Join-Path $root $OutDir) | Out-Null
$exe = Join-Path $root "$OutDir\DeskBox.exe"
$ico = Join-Path $root "assets\deskbox.ico"

# 注意：不要用 $args（PowerShell 自动变量，splat 会失效导致 csc 收不到参数、构建假成功）
$cscArgs = @(
  "/nologo", "/target:winexe", "/optimize+", "/platform:anycpu",
  "/out:$exe",
  "/r:System.dll", "/r:System.Core.dll", "/r:System.Security.dll", "/r:System.Drawing.dll",
  "/r:System.Windows.Forms.dll", "/r:System.Web.Extensions.dll", "/r:System.IO.Compression.dll", "/r:System.IO.Compression.FileSystem.dll", "/r:System.Management.dll",
  "/r:Microsoft.VisualBasic.dll",
  "/win32icon:$ico"
  "/resource:$root\assets\engineering-factory.png,DeskBox.EngineeringFactory"
  "/resource:$root\assets\voice-presence.png,DeskBox.VoicePresence"
  "/resource:$root\public\assets\silverwing-avatar.png,DeskBox.Silverwing"
  "/resource:$root\assets\voice-probe.png,DeskBox.VoiceProbe"
  "/resource:$root\skills\documents\worker.py,DeskBox.DocumentWorker"
  "/resource:$root\skills\documents\SKILL.md,DeskBox.DocumentSkill"
  "/resource:$root\skills\documents\windows-ocr.ps1,DeskBox.DocumentOcr"
) + $src

Write-Host "编译 $($src.Count) 个源文件 -> $exe" -ForegroundColor Cyan
$stampBefore = if (Test-Path $exe) { (Get-Item $exe).LastWriteTimeUtc } else { [datetime]::MinValue }
$out = & $csc @cscArgs 2>&1
$code = $LASTEXITCODE
$errors = $out | Where-Object { $_ -match ": error " }
$warnings = $out | Where-Object { $_ -match ": warning " }
if ($warnings) { Write-Host "警告 $($warnings.Count) 条" -ForegroundColor Yellow }
if ($errors) {
  $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
  throw "编译失败（$($errors.Count) 个错误）"
}
if ($code -ne 0) { throw "csc 退出码 $code" }

if (-not (Test-Path $exe)) { throw "未生成 exe" }
$stampAfter = (Get-Item $exe).LastWriteTimeUtc
if ($stampAfter -le $stampBefore) { throw "exe 未更新（可能被正在运行的进程占用），请先退出 DeskBox 再构建" }
$size = [math]::Round((Get-Item $exe).Length / 1KB, 1)
Write-Host "构建成功：$exe ($size KB)" -ForegroundColor Green
