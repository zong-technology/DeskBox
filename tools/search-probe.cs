// 触发一次真实搜索并读回状态
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class SearchProbe
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string n);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static IntPtr FindMainWindow(uint pid)
    {
        IntPtr f = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr l)
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid) { RECT r; GetWindowRect(h, out r); if (r.R - r.L > 800) { f = h; return false; } }
            return true;
        }, IntPtr.Zero);
        return f;
    }

    public static IntPtr FindByText(IntPtr root, string text)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder t = new StringBuilder(200); GetWindowText(h, t, 200);
            if (t.ToString() == text) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FindEditNear(IntPtr root, int minLeft)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(root, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder cn = new StringBuilder(120); GetClassName(h, cn, 120);
            if (cn.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) < 0) return true;
            RECT r; GetWindowRect(h, out r);
            if (r.L >= minLeft && (r.B - r.T) > 10) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string GetTextOf(IntPtr h)
    {
        StringBuilder t = new StringBuilder(400); GetWindowText(h, t, 400); return t.ToString();
    }

    public static int Main(string[] args)
    {
        uint pid = uint.Parse(args[0]);
        string keyword = args.Length > 1 ? args[1] : "pdf";
        IntPtr main = FindMainWindow(pid);

        // 找到搜索视图里的输入框（x >= 70，且在顶部区域 y < 260）
        IntPtr edit = IntPtr.Zero;
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder cn = new StringBuilder(120); GetClassName(h, cn, 120);
            if (cn.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) < 0) return true;
            RECT r; GetWindowRect(h, out r);
            if (r.L > 70 && r.T > 60 && r.T < 220 && (r.B - r.T) > 12) { edit = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (edit == IntPtr.Zero) { Console.WriteLine("NO EDIT BOX"); return 1; }
        SendMessage(edit, 0x000C, IntPtr.Zero, Marshal.StringToHGlobalUni(keyword));   // WM_SETTEXT
        Thread.Sleep(300);

        IntPtr btn = FindByText(main, "搜索");
        if (btn == IntPtr.Zero) { Console.WriteLine("NO SEARCH BUTTON"); return 1; }
        SendMessage(btn, 0x00F5, IntPtr.Zero, IntPtr.Zero);        // BM_CLICK
        Thread.Sleep(1500);

        foreach (string s in AllTexts(main))
        {
            if (s.Contains("命中") || s.Contains("索引")) Console.WriteLine(s);
        }
        return 0;
    }

    public static List<string> AllTexts(IntPtr main)
    {
        List<string> list = new List<string>();
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder t = new StringBuilder(400); GetWindowText(h, t, 400);
            if (t.Length > 0) list.Add(t.ToString());
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
