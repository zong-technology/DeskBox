// 读取 DeskBox 窗口内所有文本控件内容（用于断言索引状态、命中数等）
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class Probe
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
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

    public static int Main(string[] args)
    {
        uint pid = uint.Parse(args[0]);
        string filter = args.Length > 1 ? args[1] : "";
        IntPtr main = FindMainWindow(pid);
        List<string> texts = new List<string>();
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder t = new StringBuilder(300); GetWindowText(h, t, 300);
            if (t.Length > 0) texts.Add(t.ToString());
            return true;
        }, IntPtr.Zero);
        foreach (string s in texts)
        {
            if (filter.Length == 0 || s.IndexOf(filter, StringComparison.Ordinal) >= 0) Console.WriteLine(s);
        }
        return 0;
    }
}
