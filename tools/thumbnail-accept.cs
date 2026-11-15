// V2.1 真实 Shell 缩略图验收：全部在唯一临时沙箱内创建真实 PNG，不触碰用户数据。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Shell;
using DeskBox.UI;

public class ThumbnailAccept
{
    static int pass, fail;
    static readonly List<string> details = new List<string>();
    static void C(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); details.Add(name + " :: " + detail); }
    }

    static string MakePng(string path, int w, int h, Color color)
    {
        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
            using (var brush = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                g.FillEllipse(brush, w / 4, h / 4, w / 2, h / 2);
            using (var f = new Font("Segoe UI", Math.Max(10, w / 10)))
                g.DrawString("DeskBox", f, Brushes.Black, 6, 6);
            bmp.Save(path, ImageFormat.Png);
        }
        return path;
    }

    [STAThread]
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "deskbox-thumb-" + DateTime.Now.Ticks);
        var desktop = Path.Combine(root, "Desktop");
        Directory.CreateDirectory(desktop);
        Store.DataDir = Path.Combine(root, "data");
        DeskBox.Core.AppPaths.DataDir = Store.DataDir;
        Directory.CreateDirectory(Store.DataDir);

        var png = MakePng(Path.Combine(desktop, "工程照片.png"), 512, 384, Color.FromArgb(30, 90, 160));
        var png2 = MakePng(Path.Combine(desktop, "IO List 预览.png"), 256, 256, Color.FromArgb(160, 60, 40));
        var png3 = MakePng(Path.Combine(desktop, "HMI 画面.png"), 300, 200, Color.FromArgb(40, 140, 90));
        Console.WriteLine("沙箱: " + root);

        Console.WriteLine("\n== 缩略图策略 ==");
        C("图片需要缩略图", ThumbnailPolicy.ShouldUseThumbnail(png, false), Path.GetFileName(png));
        C("PDF/Office 需要缩略图，快捷方式使用 Shell 图标",
            ThumbnailPolicy.ShouldUseThumbnail(@"C:\x\a.pdf", false) && ThumbnailPolicy.ShouldUseThumbnail(@"C:\x\a.docx", false)
            && ThumbnailPolicy.ShouldUseThumbnail(@"C:\x\a.xlsx", false) && !ThumbnailPolicy.ShouldUseThumbnail(@"C:\x\a.lnk", false), "pdf/docx/xlsx + lnk icon");
        C("纯文本不需要缩略图", !ThumbnailPolicy.ShouldUseThumbnail(@"C:\x\a.txt", false), "txt");
        C("文件夹使用真实 Shell 图标而不排队", !ThumbnailPolicy.ShouldUseThumbnail(desktop, true), "dir icon");
        C("空路径不请求", !ThumbnailPolicy.ShouldUseThumbnail("", false) && !ThumbnailPolicy.ShouldUseThumbnail(null, true), "empty");
        C("尺寸归一化有上下界", ThumbnailPolicy.NormalizeSize(4) == ThumbnailPolicy.MinSize && ThumbnailPolicy.NormalizeSize(9999) == ThumbnailPolicy.MaxSize,
            ThumbnailPolicy.NormalizeSize(4) + "/" + ThumbnailPolicy.NormalizeSize(9999));
        C("Grid + 大图标才启用缩略图",
            IconBoard.ThumbnailsSupported(BoardView.Grid, 96) && IconBoard.ThumbnailsSupported(BoardView.Grid, ThumbnailPolicy.MinIconSize)
            && !IconBoard.ThumbnailsSupported(BoardView.Grid, ThumbnailPolicy.MinIconSize - 1)
            && !IconBoard.ThumbnailsSupported(BoardView.List, 96) && !IconBoard.ThumbnailsSupported(BoardView.Compact, 96),
            "grid96/list96/compact96/grid32");

        using (var svc = new ShellThumbnailService())
        {
            Console.WriteLine("\n== 异步返回与尺寸 ==");
            var ready = new ManualResetEventSlim(false);
            var readyThread = -1; var readyPath = ""; var readySize = 0;
            svc.Ready += delegate(string p, int s) { readyPath = p; readySize = s; readyThread = Thread.CurrentThread.ManagedThreadId; ready.Set(); };

            ThumbnailInfo early;
            bool cachedBeforeReady = svc.TryPeek(png, 96, out early);
            C("请求前缓存为空", !cachedBeforeReady, "cached=" + cachedBeforeReady);

            var sw = Stopwatch.StartNew();
            bool queued = svc.Request(png, 96, false);
            sw.Stop();
            C("Request 立即返回并入队（不阻塞 UI）", queued && sw.ElapsedMilliseconds < 50, "queued=" + queued + " " + sw.ElapsedMilliseconds + "ms");

            bool twice = svc.Request(png, 96, false);
            C("排队期间重复请求被去重", !twice && svc.Stats.Deduplicated >= 1 && svc.Stats.Requested == 1,
                "second=" + twice + " dedup=" + svc.Stats.Deduplicated + " requested=" + svc.Stats.Requested);

            bool arrived = ready.Wait(20000);
            C("缩略图异步就绪（后台线程回调）", arrived && readyPath == png && readySize == 96 && readyThread != Thread.CurrentThread.ManagedThreadId,
                "arrived=" + arrived + " size=" + readySize + " thread=" + readyThread);

            ThumbnailInfo info;
            bool peek = svc.TryPeek(png, 96, out info);
            C("返回真实 Shell 缩略图而非图标", peek && info.Source == ThumbnailSource.Shell && !info.IsFallback, peek ? info.Source.ToString() : "miss");
            C("缩略图尺寸符合请求上限", peek && info.Width > 0 && info.Height > 0 && info.Width <= 96 && info.Height <= 96,
                peek ? info.Width + "x" + info.Height : "miss");

            Console.WriteLine("\n== 缓存命中 ==");
            long hitsBefore = svc.Stats.CacheHits;
            var sw2 = Stopwatch.StartNew();
            bool requeue = svc.Request(png, 96, false);
            ThumbnailInfo info2;
            bool peek2 = svc.TryPeek(png, 96, out info2);
            sw2.Stop();
            C("缓存命中不再入队", !requeue && peek2, "requeue=" + requeue + " peek=" + peek2);
            C("缓存命中计数增长", svc.Stats.CacheHits > hitsBefore, hitsBefore + " -> " + svc.Stats.CacheHits);
            C("缓存读取是同步廉价操作", sw2.ElapsedMilliseconds < 50, sw2.ElapsedMilliseconds + "ms");
            C("缓存条目受容量约束", svc.Stats.Cached >= 1 && svc.Stats.Cached <= svc.Capacity, "cached=" + svc.Stats.Cached + "/" + svc.Capacity);

            Console.WriteLine("\n== 多尺寸与另一张图 ==");
            var ready2 = new ManualResetEventSlim(false);
            svc.Ready += delegate(string p, int s) { if (p == png2 && s == 48) ready2.Set(); };
            bool q2 = svc.Request(png2, 48, false);
            bool q3 = svc.Request(png, 48, false);
            C("同一文件不同尺寸各自独立缓存", q2 && q3, "q2=" + q2 + " q3=" + q3);
            C("第二张图缩略图就绪", ready2.Wait(20000), "ready=" + ready2.IsSet);

            Console.WriteLine("\n== 失效路径 / 权限失败回退 ==");
            var missingReady = new ManualResetEventSlim(false);
            svc.Ready += delegate(string p, int s) { if (p.EndsWith("已删除的图纸.png", StringComparison.OrdinalIgnoreCase)) missingReady.Set(); };
            var missing = Path.Combine(desktop, "已删除的图纸.png");
            bool qMissing = svc.Request(missing, 64, false);
            bool missingOk = missingReady.Wait(20000);
            ThumbnailInfo missingInfo;
            bool missingPeek = svc.TryPeek(missing, 64, out missingInfo);
            C("失效路径不崩溃并回退 SHGetFileInfo 图标",
                qMissing && missingOk && missingPeek && missingInfo.Source == ThumbnailSource.Icon && missingInfo.IsFallback,
                "peek=" + missingPeek + " source=" + (missingPeek ? missingInfo.Source.ToString() : "miss"));
            bool missingAgain = svc.Request(missing, 64, false);
            C("失效路径不反复重试（负缓存/回退结果被缓存）", !missingAgain, "retry=" + missingAgain);

            var sam = Environment.ExpandEnvironmentVariables(@"%WINDIR%\System32\config\SAM");
            bool samExists = File.Exists(sam);
            var samReady = new ManualResetEventSlim(false);
            svc.Ready += delegate(string p, int s) { if (string.Equals(p, sam, StringComparison.OrdinalIgnoreCase)) samReady.Set(); };
            bool qSam = svc.Request(sam, 64, false);
            bool samOk = qSam && samReady.Wait(20000);
            ThumbnailInfo samInfo;
            bool samPeek = svc.TryPeek(sam, 64, out samInfo);
            C("权限受限文件不崩溃且有回退结果", samExists ? (samOk && samPeek) : true,
                samExists ? ("exists=True source=" + (samPeek ? samInfo.Source.ToString() : "miss")) : "SAM 不存在，跳过");

            Console.WriteLine("\n== 网络路径不阻塞调用方 ==");
            var unc = @"\\localhost\deskbox-missing-share\远程图纸.png";
            using (var fast = new ShellThumbnailService(16, 8, 1200))
            {
                var uncReady = new ManualResetEventSlim(false);
                fast.Ready += delegate(string p, int s) { if (p == unc) uncReady.Set(); };
                var swU = Stopwatch.StartNew();
                bool qUnc = fast.Request(unc, 64, false);
                swU.Stop();
                C("UNC 路径请求不阻塞调用线程", swU.ElapsedMilliseconds < 50, "queued=" + qUnc + " " + swU.ElapsedMilliseconds + "ms");
                ThumbnailInfo uncInfo = null;
                bool uncCached = uncReady.Wait(5000) && fast.TryPeek(unc, 64, out uncInfo);
                C("UNC 在进入 Shell 前安全短路为类型图标", uncCached && uncInfo.Source == ThumbnailSource.Icon && fast.Stats.Timeouts == 0 && fast.Stats.SkippedUnsafe >= 1,
                    "cached=" + uncCached + " timeouts=" + fast.Stats.Timeouts + " skipped=" + fast.Stats.SkippedUnsafe);
                var swNext = Stopwatch.StartNew();
                bool qNext = fast.Request(png3, 64, false);
                swNext.Stop();
                C("队列在超时/失败后仍可继续处理", qNext && swNext.ElapsedMilliseconds < 50, "next=" + qNext + " " + swNext.ElapsedMilliseconds + "ms");

                var bounded = new List<int>();
                for (int i = 0; i < 40; i++) bounded.Add(fast.Stats.Pending);
                C("待处理数量有界", bounded.All(x => x <= fast.MaxQueue + 1), "maxPending=" + bounded.Max() + " limit=" + (fast.MaxQueue + 1));
                C("提取线程硬限制为 2", fast.Stats.PeakActiveExtractors <= 2, "peak=" + fast.Stats.PeakActiveExtractors);
            }

            Console.WriteLine("\n== OneDrive/Offline 占位文件安全短路 ==");
            var offline = MakePng(Path.Combine(desktop, "offline-placeholder.png"), 64, 64, Color.Gray);
            bool offlineSupported = true;
            try { File.SetAttributes(offline, File.GetAttributes(offline) | FileAttributes.Offline); } catch { offlineSupported = false; }
            if (offlineSupported)
            {
                var offlineReady = new ManualResetEventSlim(false);
                svc.Ready += delegate(string p, int s) { if (p == offline) offlineReady.Set(); };
                long skippedBefore = svc.Stats.SkippedUnsafe;
                bool qOffline = svc.Request(offline, 64, false);
                ThumbnailInfo offlineInfo = null;
                bool offlineCached = offlineReady.Wait(5000) && svc.TryPeek(offline, 64, out offlineInfo);
                C("Offline 文件不触发 Shell 水合", qOffline && offlineCached && offlineInfo.Source == ThumbnailSource.Icon && svc.Stats.SkippedUnsafe > skippedBefore,
                    "source=" + (offlineCached ? offlineInfo.Source.ToString() : "none") + " skipped=" + svc.Stats.SkippedUnsafe);
                try { File.SetAttributes(offline, FileAttributes.Normal); } catch { }
            }
            else C("Offline 文件不触发 Shell 水合", true, "当前文件系统不支持设置 Offline 属性，跳过模拟");

            Console.WriteLine("\n== LRU 淘汰 ==");
            using (var small = new ShellThumbnailService(2, 16, 8000))
            {
                var w1 = new ManualResetEventSlim(false); int left = 3;
                small.Ready += delegate { if (Interlocked.Decrement(ref left) == 0) w1.Set(); };
                small.Request(png, 64, false); small.Request(png2, 64, false); small.Request(png3, 64, false);
                C("三张图全部处理完成", w1.Wait(25000), "done=" + (3 - left));
                C("缓存条数不超过容量", small.Stats.Cached <= 2, "cached=" + small.Stats.Cached + " cap=2");
                C("发生 LRU 淘汰", small.Stats.Evictions >= 1, "evictions=" + small.Stats.Evictions);
                small.Prune(new string[0]);
                C("切换到空目录会清空 LRU", small.Stats.Cached == 0 && small.Stats.Pending == 0, "cached=" + small.Stats.Cached + " pending=" + small.Stats.Pending);
            }

            Console.WriteLine("\n== IconBoard 集成（Grid 绘制会请求，List 不会） ==");
            var store = new Store(); store.Init(); store.Settings.IconSize = 96; store.Settings.View = "grid";
            var items = new List<DesktopItem>
            {
                new DesktopItem { Id = "a", Name = "工程照片.png", Path = png, Kind = "file", Ext = ".png" },
                new DesktopItem { Id = "b", Name = "IO List", Path = Path.Combine(desktop, "IO List.xlsx"), Kind = "file", Ext = ".xlsx" },
                new DesktopItem { Id = "c", Name = "notes.txt", Path = Path.Combine(desktop, "notes.txt"), Kind = "file", Ext = ".txt" }
            };

            using (var gridSvc = new ShellThumbnailService())
            using (var board = new IconBoard(store) { View = BoardView.Grid, Thumbnails = gridSvc, Size = new Size(900, 600) })
            {
                board.SetData(items);
                board.CreateControl();
                Application.DoEvents();
                int invalidated = 0;
                board.Invalidated += delegate { Interlocked.Increment(ref invalidated); };
                Paint(board, 900, 600);
                C("Grid 绘制只为缩略图候选排队", gridSvc.Stats.Requested == 2, "requested=" + gridSvc.Stats.Requested + "（png+xlsx）");
                bool readyGrid = WaitFor(delegate { return gridSvc.Stats.Cached >= 2; }, 20000);
                C("Grid 缩略图异步就绪", readyGrid, "cached=" + gridSvc.Stats.Cached);
                bool refreshed = WaitFor(delegate { Application.DoEvents(); return invalidated > 0; }, 8000);
                C("缩略图就绪后 UI 安全刷新（Invalidated 触发）", refreshed,
                    "invalidated=" + invalidated + " handle=" + board.IsHandleCreated);
                Paint(board, 900, 600);
                C("就绪后走缓存绘制，不再重复请求", gridSvc.Stats.Requested == 2, "requested=" + gridSvc.Stats.Requested);
                var refreshedItems = new List<DesktopItem>
                {
                    new DesktopItem { Id = "a", Name = "工程照片.png", Path = png, Kind = "file", Ext = ".png", Mtime = 2 },
                    items[1], items[2]
                };
                long requestedBeforeRefresh = gridSvc.Stats.Requested;
                board.SetData(refreshedItems); Paint(board, 900, 600);
                C("文件修改时间变化会失效并重新请求", WaitFor(delegate { return gridSvc.Stats.Requested > requestedBeforeRefresh; }, 5000),
                    requestedBeforeRefresh + " -> " + gridSvc.Stats.Requested);
            }

            Console.WriteLine("\n== 版本键与显式失效 ==");
            var versionReady = new ManualResetEventSlim(false);
            svc.Ready += delegate(string p, int s) { if (p == png && s == 80) versionReady.Set(); };
            bool v1Queued = svc.Request(png, 80, false, 1); bool v1Ready = versionReady.Wait(10000); ThumbnailInfo v1Info;
            bool v1Cached = svc.TryPeek(png, 80, 1, out v1Info);
            svc.Invalidate(png); versionReady.Reset();
            bool v2Queued = svc.Request(png, 80, false, 2); bool v2Ready = versionReady.Wait(10000); ThumbnailInfo v2Info;
            bool v2Cached = svc.TryPeek(png, 80, 2, out v2Info);
            C("版本变化不会复用旧缩略图", v1Queued && v1Ready && v1Cached && v2Queued && v2Ready && v2Cached, "v1=" + v1Cached + " v2=" + v2Cached);

            using (var listSvc = new ShellThumbnailService())
            using (var listBoard = new IconBoard(store) { View = BoardView.List, Thumbnails = listSvc, Size = new Size(900, 600) })
            {
                listBoard.SetData(items);
                Paint(listBoard, 900, 600);
                C("List 视图不请求缩略图（保持轻量）", listSvc.Stats.Requested == 0, "requested=" + listSvc.Stats.Requested);
            }

            Console.WriteLine("\n== Dispose 资源释放 ==");
            long cachedBefore = svc.Stats.Cached;
            svc.Dispose();
            C("Dispose 后标记已释放且清空缓存", svc.IsDisposed && svc.Stats.Cached == 0, "disposed=" + svc.IsDisposed + " cached " + cachedBefore + " -> " + svc.Stats.Cached);
            ThumbnailInfo afterInfo;
            C("Dispose 后拒绝新请求", svc.Request(png, 96, false) == false && svc.TryPeek(png, 96, out afterInfo) == false, "rejected");
            using (var bmp = new Bitmap(96, 96))
            using (var g = Graphics.FromImage(bmp))
                C("Dispose 后绘制安全返回 false", svc.TryDraw(g, png, 96, new Rectangle(0, 0, 96, 96)) == false, "no-draw");
            bool doubleDisposeOk = true;
            try { svc.Dispose(); } catch { doubleDisposeOk = false; }
            C("重复 Dispose 安全", doubleDisposeOk, "ok");
        }

        Console.WriteLine("\n==== Shell 缩略图验收: PASS " + pass + " / FAIL " + fail + " ====");
        if (fail > 0) foreach (var d in details) Console.WriteLine("  ! " + d);
        return fail == 0 ? 0 : 1;
    }

    static void Paint(Control board, int w, int h)
    {
        var mi = typeof(IconBoard).GetMethod("OnPaint", BindingFlags.NonPublic | BindingFlags.Instance);
        using (var bmp = new Bitmap(w, h))
        using (var g = Graphics.FromImage(bmp))
            mi.Invoke(board, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, w, h)) });
    }

    static bool WaitFor(Func<bool> cond, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (cond()) return true;
            Thread.Sleep(30);
        }
        return cond();
    }
}
