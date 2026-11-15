# 卸载 DeskBox：恢复系统桌面图标、移除自启与快捷方式（保留 data 目录，除非 -Purge）。
param(
  [string]$InstallDir = "D:\DeskBox",
  [switch]$Purge
)
$ErrorActionPreference = "Continue"

Write-Host "停止 DeskBox…" -ForegroundColor Cyan
Get-Process DeskBox -ErrorAction SilentlyContinue | ForEach-Object { $_.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 300; if (-not $_.HasExited) { $_.Kill() } }

# 恢复系统桌面图标（Shell 的 SysListView32 可能仍是隐藏状态）
$sig = @'
using System; using System.Runtime.InteropServices; using System.Text;
public class ShellIcons {
  [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern IntPtr FindWindow(string c, string n);
  [DllImport("user32.dll", CharSet=CharSet.Auto)] static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
  [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
  public static void Show() {
    IntPtr progman = FindWindow("Progman", null);
    IntPtr def = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
    if (def == IntPtr.Zero) { def = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "SHELLDLL_DefView", null); }
    IntPtr lv = def == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
    if (lv != IntPtr.Zero) ShowWindow(lv, 5);
  }
}
'@
try { Add-Type -TypeDefinition $sig -ErrorAction Stop; [ShellIcons]::Show() } catch { Write-Host "恢复桌面图标失败（可手动：桌面右键 → 查看 → 显示桌面图标）" -ForegroundColor Yellow }

Remove-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "DeskBox" -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) "桌匣 DeskBox.lnk") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\桌匣 DeskBox.lnk") -ErrorAction SilentlyContinue

if ($Purge) {
  Write-Host "删除安装目录 $InstallDir …" -ForegroundColor Yellow
  if ((Resolve-Path $InstallDir -ErrorAction SilentlyContinue).Path -eq $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
} else {
  Write-Host "已保留 $InstallDir\data（分类/任务/日志）。彻底清除请加 -Purge" -ForegroundColor DarkGray
}
Write-Host "卸载完成。" -ForegroundColor Green
