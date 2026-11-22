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
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out int paths, out int modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref int paths, [Out] byte[] pathInfo, ref int modes, [Out] byte[] modeInfo, IntPtr topology);
    [StructLayout(LayoutKind.Sequential, Pack=4)] public struct ScaleInfo {
        public int Kind, Size;
        public long Adapter;
        public int Source, Minimum, Current, Maximum;
    }
    [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref ScaleInfo info);
    [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref ScaleInfo info);
    public static ScaleInfo ReadScale() {
        int pathCount, modeCount;
        int result = GetDisplayConfigBufferSizes(2, out pathCount, out modeCount);
        if (result != 0 || pathCount != 1) throw new InvalidOperationException("Display paths unavailable for deterministic scale testing: " + result + ", paths " + pathCount);
        var paths = new byte[pathCount * 72];
        result = QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, new byte[modeCount * 64], IntPtr.Zero);
        if (result != 0) throw new InvalidOperationException("Display path query returned " + result);
        // The Windows Settings display-scale requests are driver dependent. The
        // acceptance script verifies each trial with GetDpiForWindow independently.
        var scale = new ScaleInfo { Kind = -3, Size = 32, Adapter = BitConverter.ToInt64(paths, 0), Source = BitConverter.ToInt32(paths, 8) };
        result = DisplayConfigGetDeviceInfo(ref scale);
        if (result != 0) throw new InvalidOperationException("Scale query returned " + result);
        return scale;
    }
    public static int SetScale(ScaleInfo scale, int relative) {
        scale.Kind = -4;
        scale.Size = 24;
        scale.Minimum = relative;
        return DisplayConfigSetDeviceInfo(ref scale);
    }
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
