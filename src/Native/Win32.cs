using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskBox.Native
{
    /// <summary>Win32 封装：桌面层挂载、系统图标开关、Shell 图标提取、资源管理器定位。</summary>
    public static class Win32
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_SHOWNOACTIVATE = 4;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        private const uint SMTO_NORMAL = 0x0000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        // ---------- Shell 图标 ----------
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public const uint SHGFI_ICON = 0x000000100;
        public const uint SHGFI_LARGEICON = 0x000000000;
        public const uint SHGFI_SMALLICON = 0x000000001;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        public const uint SHGFI_SYSICONINDEX = 0x000004000;

        public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        public static Icon GetShellIcon(string path, bool large)
        {
            return GetShellIcon(path, large, false);
        }

        /// <summary>
        /// 取 Shell 图标。useFileAttributes=true 时不访问磁盘、按扩展名/属性返回类型图标，
        /// 用于文件已失效或权限失败时的兜底（不会卡在网络路径上）。
        /// </summary>
        public static Icon GetShellIcon(string path, bool large, bool useFileAttributes)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var info = new SHFILEINFO();
            uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            uint attributes = 0;
            if (useFileAttributes)
            {
                flags |= SHGFI_USEFILEATTRIBUTES;
                attributes = FILE_ATTRIBUTE_NORMAL;
            }
            IntPtr r = SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(info), flags);
            if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
            try
            {
                var icon = (Icon)Icon.FromHandle(info.hIcon).Clone();
                return icon;
            }
            catch { return null; }
            finally { DestroyIcon(info.hIcon); }
        }

        // ---------- 桌面层 ----------
        public static IntPtr FindDefViewHost()
        {
            IntPtr host = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                IntPtr dv = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (dv != IntPtr.Zero) { host = h; return false; }
                return true;
            }, IntPtr.Zero);
            return host;
        }

        public static IntPtr FindWallpaperWorkerW()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                IntPtr res;
                SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out res);
            }
            IntPtr defHost = FindDefViewHost();
            if (defHost == IntPtr.Zero) return IntPtr.Zero;
            // 图标宿主之后的第一个 WorkerW 就是壁纸层
            IntPtr w = FindWindowEx(IntPtr.Zero, defHost, "WorkerW", null);
            return w;
        }

        /// <summary>把窗口挂到桌面层（壁纸之上、图标之下）；失败返回 false。</summary>
        public static bool AttachToDesktop(IntPtr form)
        {
            IntPtr target = FindWallpaperWorkerW();
            if (target == IntPtr.Zero) target = FindWindow("Progman", null);
            if (target == IntPtr.Zero) return false;
            IntPtr prev = SetParent(form, target);
            if (GetParent(form) == IntPtr.Zero) return false;
            SetWindowPos(form, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            return true;
        }

        public static void DetachFromDesktop(IntPtr form)
        {
            SetParent(form, IntPtr.Zero);
        }

        /// <summary>显示/隐藏系统桌面图标（Explorer 的 SysListView32）。</summary>
        public static bool SetSystemIconsVisible(bool visible)
        {
            IntPtr host = FindDefViewHost();
            if (host == IntPtr.Zero) return false;
            IntPtr defView = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero) return false;
            IntPtr listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (listView == IntPtr.Zero) listView = defView;
            ShowWindow(listView, visible ? SW_SHOW : SW_HIDE);
            return true;
        }

        public static bool GetSystemIconsVisible()
        {
            IntPtr host = FindDefViewHost();
            if (host == IntPtr.Zero) return true;
            IntPtr defView = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero) return true;
            IntPtr listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            if (listView == IntPtr.Zero) return IsWindowVisible(defView);
            return IsWindowVisible(listView);
        }

        // ---------- 打开 / 定位 ----------
        public static void OpenPath(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Write("error", "open", "打开失败 " + path + " : " + ex.Message); }
        }

        public static void RevealPath(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { AppLog.Write("error", "open", "定位失败 " + path + " : " + ex.Message); }
        }

        public static void ShowProperties(string path)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true, Verb = "properties" };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { AppLog.Write("error", "open", "打开属性失败 " + path + " : " + ex.Message); }
        }

        public static void SetHiddenAttribute(string path, bool hidden)
        {
            try
            {
                var attr = System.IO.File.GetAttributes(path);
                if (hidden) attr |= System.IO.FileAttributes.Hidden;
                else attr &= ~System.IO.FileAttributes.Hidden;
                System.IO.File.SetAttributes(path, attr);
            }
            catch (Exception ex) { AppLog.Write("warn", "hide", "设置隐藏属性失败 " + path + " : " + ex.Message); }
        }

        public static string GetWallpaperPath()
        {
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
                if (!string.IsNullOrEmpty(v) && System.IO.File.Exists(v)) return v;
                var t = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (System.IO.File.Exists(t)) return t;
            }
            catch { }
            return null;
        }
    }
}
