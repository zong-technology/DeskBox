// 按索引点击左侧导航并截图（用于逐视图验收）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class ShotRail
{
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static IntPtr FindMain(uint pid)
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

    public static List<IntPtr> Rail(IntPtr main)
    {
        List<IntPtr> found = new List<IntPtr>();
        RECT window;GetWindowRect(main,out window);
        EnumChildWindows(main, delegate(IntPtr h, IntPtr l)
        {
            RECT r; GetWindowRect(h, out r);
            int w = r.R - r.L, ht = r.B - r.T;
            bool legacy=r.L-window.L<70&&w>=50&&w<=60&&ht>=45&&ht<=60;
            bool workspace=r.L-window.L<210&&w>=170&&w<=190&&ht>=34&&ht<=42;
            if(IsWindowVisible(h)&&(legacy||workspace))found.Add(h);
            return true;
        }, IntPtr.Zero);
        found.Sort(delegate(IntPtr a, IntPtr b) { RECT ra, rb; GetWindowRect(a, out ra); GetWindowRect(b, out rb); return ra.T - rb.T; });
        return found;
    }

    public static void Click(IntPtr h)
    {
        PostMessage(h, 0x0201, (IntPtr)1, (IntPtr)((20 << 16) | 28));
        Thread.Sleep(70);
        PostMessage(h, 0x0202, IntPtr.Zero, (IntPtr)((20 << 16) | 28));
        // DarkButton implements Enter activation; target the navigation HWND only.
        PostMessage(h, 0x0100, (IntPtr)13, IntPtr.Zero);
        PostMessage(h, 0x0101, (IntPtr)13, IntPtr.Zero);
    }

    public static void Shot(IntPtr h, string path)
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
        string dir = args[1];
        string[] names = args[2].Split(',');
        IntPtr main = FindMain(pid);
        if(main==IntPtr.Zero){Console.WriteLine("NO MAIN WINDOW");return 1;}
        System.IO.Directory.CreateDirectory(dir);
        if(args[2]=="--main"){Shot(main,System.IO.Path.Combine(dir,"installed.png"));return 0;}
        List<IntPtr> rail = Rail(main);
        Console.WriteLine("rail=" + rail.Count + " names=" + names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            int idx = int.Parse(names[i].Split(':')[0]);
            string name = names[i].Split(':')[1];
            if (idx < 0 || idx >= rail.Count) { Console.WriteLine("missing navigation " + name); return 1; }
            Click(rail[idx]);
            Thread.Sleep(1800);
            Shot(main, System.IO.Path.Combine(dir, "v3-" + name + ".png"));
            Console.WriteLine("shot " + name + " (idx " + idx + ")");
        }
        return 0;
    }
}
