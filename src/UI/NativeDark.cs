using System;
using System.Runtime.InteropServices;

namespace DeskBox.UI
{
    /// <summary>让原生控件（ComboBox 等）跟随暗色主题：优先 DarkMode_Explorer，失败则关闭视觉样式。</summary>
    public static class NativeDark
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string appName, string idList);

        public static void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
            // 部分系统不支持 DarkMode_Explorer：退化为关闭视觉样式，让 BackColor 生效
            try { SetWindowTheme(hwnd, "", ""); } catch { }
        }
    }
}