// 端到端：切到搜索视图 → 输入关键词 → 点“搜索” → 读命中数 → 截图
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class E2E
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    static IntPtr Main(uint pid)
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

    static List<IntPtr> RailButtons(IntPtr main)
    {
        List<IntPtr> found = new List<IntPtr>();
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            RECT r; GetWindowRect(h, out r);
            int w = r.R - r.L, ht = r.B - r.T;
            if (r.L < 70 && w >= 50 && w <= 60 && ht >= 45 && ht <= 60) found.Add(h);
            return true;
        }, IntPtr.Zero);
        found.Sort(delegate(IntPtr a, IntPtr b) { RECT ra, rb; GetWindowRect(a, out ra); GetWindowRect(b, out rb); return ra.T - rb.T; });
        return found;
    }

    static void Click(IntPtr h)
    {
        PostMessage(h, 0x0201, (IntPtr)1, (IntPtr)((20 << 16) | 28));
        Thread.Sleep(60);
        PostMessage(h, 0x0202, IntPtr.Zero, (IntPtr)((20 << 16) | 28));
    }

    static IntPtr FindByText(IntPtr root, string text)
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

    static List<string> Texts(IntPtr main, string filter)
    {
        List<string> list = new List<string>();
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder t = new StringBuilder(400); GetWindowText(h, t, 400);
            if (t.Length > 0 && (filter.Length == 0 || t.ToString().IndexOf(filter, StringComparison.Ordinal) >= 0)) list.Add(t.ToString());
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static void Shot(IntPtr h, string path)
    {
        RECT r; GetWindowRect(h, out r);
        int w = Math.Max(1, r.R - r.L), ht = Math.Max(1, r.B - r.T);
        using (Bitmap bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            IntPtr dc = g.GetHdc(); PrintWindow(h, dc, 2); g.ReleaseHdc(dc);
            bmp.Save(path, ImageFormat.Png);
        }
    }

    public static int Main(string[] args)
    {
        uint pid = uint.Parse(args[0]);
        string shotDir = args[1];
        string keyword = args.Length > 2 ? args[2] : "pdf";
        IntPtr main = Main(pid);

        List<IntPtr> rail = RailButtons(main);
        if (rail.Count < 2) { Console.WriteLine("NO RAIL"); return 1; }
        Click(rail[1]);              // 搜索
        Thread.Sleep(1500);

        // 找搜索输入框（视图内 y 在 60..220 之间的 EDIT）
        IntPtr edit = IntPtr.Zero;
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            StringBuilder cn = new StringBuilder(120); GetClassName(h, cn, 120);
            if (cn.ToString().IndexOf("EDIT", StringComparison.OrdinalIgnoreCase) < 0) return true;
            RECT r; GetWindowRect(h, out r);
            if (r.L > 70 && r.T > 60 && r.T < 240 && (r.B - r.T) > 12) { edit = h; return false; }
            return true;
        }, IntPtr.Zero);
        if (edit == IntPtr.Zero) { Console.WriteLine("NO EDIT"); return 1; }

        SendMessage(edit, 0x000C, IntPtr.Zero, Marshal.StringToHGlobalUni(keyword));
        Thread.Sleep(400);

        IntPtr btn = FindByText(main, "搜索");
        if (btn == IntPtr.Zero) { Console.WriteLine("NO BTN"); return 1; }
        PostMessage(btn, 0x0201, (IntPtr)1, (IntPtr)((10 << 16) | 20));
        Thread.Sleep(60);
        PostMessage(btn, 0x0202, IntPtr.Zero, (IntPtr)((10 << 16) | 20));
        Thread.Sleep(2000);

        foreach (string s in Texts(main, "命中")) Console.WriteLine("RESULT: " + s);
        foreach (string s in Texts(main, "索引")) Console.WriteLine("STATE : " + s);
        Shot(main, System.IO.Path.Combine(shotDir, "v2-search-" + keyword + ".png"));
        Console.WriteLine("SHOT  : " + System.IO.Path.Combine(shotDir, "v2-search-" + keyword + ".png"));
        return 0;
    }
}
