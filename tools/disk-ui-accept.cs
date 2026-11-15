using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.UI.Views;

public class DiskUiAccept
{
    static int pass, fail;
    static void C(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    [STAThread] public static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "deskbox-disk-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data); Store.DataDir = data; AppPaths.DataDir = data;
        var a = Path.Combine(data, "A"); var b = Path.Combine(data, "B"); Directory.CreateDirectory(a); Directory.CreateDirectory(b);
        for (int i = 0; i < 200; i++) File.WriteAllBytes(Path.Combine(a, "file" + i + ".txt"), new byte[4096]);
        var child = Path.Combine(b, "Evidence"); Directory.CreateDirectory(child); File.WriteAllBytes(Path.Combine(child, "report.txt"), new byte[12]);
        var store = new Store(); store.Init();
        using (var form = new Form { Width = 1200, Height = 850 })
        using (var view = new DiskView(store))
        {
            form.Controls.Add(view); form.Show(); Application.DoEvents();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var report = typeof(DiskView).GetField("_report", flags); var status = (Label)typeof(DiskView).GetField("_status", flags).GetValue(view);
            view.StartScan(a); view.StartScan(b);
            var until = DateTime.UtcNow.AddSeconds(12);
            while (report.GetValue(view) == null && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(10); }
            var result = report.GetValue(view) as DiskReport;
            C("快速切换扫描不被旧结果覆盖", result != null && string.Equals(result.Root.TrimEnd('\\'), b, StringComparison.OrdinalIgnoreCase));
            C("概览来自真实目录字节与数量", result != null && result.TotalBytes == 12 && result.FileCount == 1 && result.Dirs.Count == 1);
            C("扫描完成状态准确", status.Text.StartsWith("完成：") && status.Text.Contains("12 B"));
            var output = Path.Combine(Environment.CurrentDirectory, "shots", "disk-review"); Directory.CreateDirectory(output);
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); bitmap.Save(Path.Combine(output, "disk-overview.png")); }
            view.StartScan(a);
            ((System.Threading.CancellationTokenSource)typeof(DiskView).GetField("_cts", flags).GetValue(view)).Cancel();
            until = DateTime.UtcNow.AddSeconds(10);
            while (report.GetValue(view) == null && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            C("取消结果明确标为部分结果", status.Text.StartsWith("已停止，部分结果"));
        }
        Console.WriteLine("Disk UI: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
