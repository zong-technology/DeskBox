$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'This helper changes only the disposable GitHub Windows runner display.' }
if (-not ('DeskBoxCIDisplay' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DeskBoxCIDisplay {
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string device,int mode,IntPtr settings);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettings(IntPtr settings,int flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    public static int SetSize(int width,int height) {
        // DEVMODEW uses a fixed 220-byte layout; keep all current driver fields.
        IntPtr settings=Marshal.AllocHGlobal(220);
        try {
            Marshal.Copy(new byte[220],0,settings,220);
            Marshal.WriteInt16(settings,68,220);
            if(!EnumDisplaySettings(null,-1,settings)) throw new InvalidOperationException("Cannot read the CI display mode");
            Marshal.WriteInt32(settings,172,width);
            Marshal.WriteInt32(settings,176,height);
            Marshal.WriteInt32(settings,72,Marshal.ReadInt32(settings,72)|0x180000);
            return ChangeDisplaySettings(settings,0);
        } finally { Marshal.FreeHGlobal(settings); }
    }
}
'@
}
$before = @([DeskBoxCIDisplay]::GetSystemMetrics(0), [DeskBoxCIDisplay]::GetSystemMetrics(1))
if ($before[0] -lt 1920 -or $before[1] -lt 1080) {
    $result = [DeskBoxCIDisplay]::SetSize(1920,1080)
    if ($result -ne 0) { throw ('CI display change failed: ' + $result) }
}
$after = @([DeskBoxCIDisplay]::GetSystemMetrics(0), [DeskBoxCIDisplay]::GetSystemMetrics(1))
if ($after[0] -lt 1920 -or $after[1] -lt 1080) { throw 'The CI display is still too small for the requested UI matrix.' }
Write-Output ('CI display: ' + ($before -join 'x') + ' -> ' + ($after -join 'x'))
