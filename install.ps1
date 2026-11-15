# 安装 DeskBox 到 D 盘（默认 D:\DeskBox），创建开始菜单与桌面快捷方式，可选开机自启。
# 用法：powershell -ExecutionPolicy Bypass -File install.ps1 [-InstallDir D:\DeskBox] [-AutoStart]
param(
  [string]$InstallDir = "D:\DeskBox",
  [switch]$AutoStart,
  [switch]$NoShortcut,
  [string]$SourceExe = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = $SourceExe
if ([string]::IsNullOrWhiteSpace($exe)) {
  $stage = "dist\install-stage"
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "build.ps1") -OutDir $stage
  if ($LASTEXITCODE -ne 0) { throw 'Fresh build failed. Installation aborted.' }
  $exe = Join-Path $root "$stage\DeskBox.exe"
}
$exe = (Resolve-Path -LiteralPath $exe -ErrorAction Stop).Path
$targetExe = Join-Path ([IO.Path]::GetFullPath($InstallDir)) 'DeskBox.exe'
foreach ($running in Get-Process DeskBox -ErrorAction SilentlyContinue) {
  if ($running.Path -and [string]::Equals($running.Path, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Exit the installed DeskBox from its tray menu before upgrading. Existing data was not changed.'
  }
}

Write-Host "安装到 $InstallDir …" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$installData = Join-Path $InstallDir "data"
New-Item -ItemType Directory -Force -Path $installData | Out-Null

$installedExe = Join-Path $InstallDir "DeskBox.exe"
if (Test-Path -LiteralPath $installedExe) {
  # 覆盖前保留上一版，确保新版本异常时仍可人工回退；不触碰 data。
  Copy-Item -LiteralPath $installedExe -Destination (Join-Path $InstallDir "DeskBox.previous.exe") -Force
}
Copy-Item -LiteralPath $exe -Destination $installedExe -Force
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $InstallDir "README.md") -Force -ErrorAction SilentlyContinue
$docs = Join-Path $root 'docs'
if (Test-Path -LiteralPath $docs) { Copy-Item -LiteralPath $docs -Destination $InstallDir -Recurse -Force }
$installTools = Join-Path $InstallDir 'tools'
New-Item -ItemType Directory -Force -Path $installTools | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\install-document-skills.ps1') -Destination $installTools -Force

# dist\data 属于开发/验收运行数据，不应安装到其他用户目录。
# 首次启动由 Store 创建默认数据；升级时绝不覆盖现有用户配置、任务、密钥、历史或索引。
$existingData = @(Get-ChildItem -LiteralPath $installData -Force -ErrorAction SilentlyContinue)
if ($existingData.Count -gt 0) {
  Write-Host "检测到现有用户数据，升级时保持不变（$($existingData.Count) 个顶层条目）" -ForegroundColor Green
} else {
  Write-Host "首次安装：数据目录为空，程序首次启动时将创建默认配置" -ForegroundColor DarkGray
}

if (-not $NoShortcut) {
  $target = Join-Path $InstallDir "DeskBox.exe"
  $shell = New-Object -ComObject WScript.Shell

  # 开始菜单
  $startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
  $lnk1 = Join-Path $startDir "桌匣 DeskBox.lnk"
  $s1 = $shell.CreateShortcut($lnk1); $s1.TargetPath = $target; $s1.WorkingDirectory = $InstallDir
  $s1.Description = "桌匣 DeskBox · 桌面管理器"; $s1.Save()

  # 桌面（用系统桌面目录，可能是 D:\桌面）
  $desktop = [Environment]::GetFolderPath('Desktop')
  $lnk2 = Join-Path $desktop "桌匣 DeskBox.lnk"
  $s2 = $shell.CreateShortcut($lnk2); $s2.TargetPath = $target; $s2.WorkingDirectory = $InstallDir
  $s2.Description = "桌匣 DeskBox · 桌面管理器"; $s2.Save()
  Write-Host "已创建快捷方式：$lnk1 / $lnk2" -ForegroundColor Green
}

# 开机自启（HKCU Run，不写 C 盘文件）
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if ($AutoStart) {
  Set-ItemProperty -Path $runKey -Name "DeskBox" -Value "`"$InstallDir\DeskBox.exe`" --autostart"
  Write-Host "已设置开机自启" -ForegroundColor Green
}

Write-Host ""
Write-Host "安装完成。启动：$InstallDir\DeskBox.exe" -ForegroundColor Green
Write-Host "退出时请在托盘图标右键 → 退出（会自动恢复系统桌面图标）。" -ForegroundColor DarkGray
if (-not $NoShortcut) { Write-Host "已创建快捷方式：$InstallDir\DeskBox.exe" -ForegroundColor DarkGray }
