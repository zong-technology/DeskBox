// V2.1 Smart Desktop foundation acceptance tests. All destructive work stays in a temp sandbox.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.Shell;

public class SmartDesktopAccept
{
    static int pass, fail;
    static void C(string name, bool ok, string detail) { if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); } else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); } }

    [STAThread]
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "deskbox-smart-desktop-" + DateTime.Now.Ticks);
        var a = Path.Combine(root, "DesktopA"); var b = Path.Combine(root, "PublicDesktop"); var target = Path.Combine(root, "Target");
        Directory.CreateDirectory(a); Directory.CreateDirectory(b); Directory.CreateDirectory(target);
        Store.DataDir = Path.Combine(root, "data"); Directory.CreateDirectory(Store.DataDir);
        var categories = Store.DefaultCategories().Select(x => x.Id).ToList();

        Console.WriteLine("== Known Folder and stable scan ==");
        C("系统桌面 Known Folder 可解析", KnownFolderService.GetDesktopLocations(new AppSettings()).Count > 0, string.Join(";", KnownFolderService.GetDesktopLocations(new AppSettings()).Select(x => x.Path).ToArray()));
        File.WriteAllText(Path.Combine(a, "同名.txt"), "A"); File.WriteAllText(Path.Combine(b, "同名.txt"), "B");
        File.WriteAllText(Path.Combine(a, "中文工程文件.txt"), "中文"); Directory.CreateDirectory(Path.Combine(a, "PLC"));
        var overrides = new Dictionary<string, ItemOverride>();
        var items = DesktopScanner.ScanDirectories(new[] { a, b }, overrides, categories);
        C("聚合两个桌面来源", items.Count == 4, "count=" + items.Count);
        var same = items.Where(x => x.Name == "同名.txt").ToList();
        C("同名不同路径具有稳定独立 ID", same.Count == 2 && same[0].Id != same[1].Id, string.Join(",", same.Select(x => x.Id).ToArray()));
        C("中文文件名可扫描", items.Any(x => x.Name == "中文工程文件.txt"), "ok");
        C("文件夹可扫描", items.Any(x => x.Kind == "dir" && x.Name == "PLC"), "ok");
        C("稳定 ID 以路径生成", DesktopScanner.StableItemId(Path.Combine(a, "同名.txt")) == same.First(x => x.Path.StartsWith(a)).Id, "ok");

        var legacyPath = Path.Combine(a, "legacy.txt"); File.WriteAllText(legacyPath, "x");
        overrides[DesktopScanner.ItemId("legacy.txt")] = new ItemOverride { Hidden = true, CategoryId = "docs" };
        var legacyItems = DesktopScanner.ScanDirectory(a, overrides, categories);
        var legacy = legacyItems.First(x => x.Name == "legacy.txt");
        C("旧 name-id 覆盖信息自动迁移", legacy.Hidden && legacy.CategoryId == "docs" && overrides.ContainsKey(legacy.Id), legacy.Id);

        Console.WriteLine("\n== Folder session ==");
        var session = new FolderSession();
        C("会话从 Desktop Home 开始", session.Current == FolderSession.DesktopHome, session.Current);
        C("进入文件夹", session.Navigate(a) && session.Current == Path.GetFullPath(a), session.Current);
        C("进入子文件夹", session.Navigate(Path.Combine(a, "PLC")), session.Current);
        C("Back 返回", session.Back() && session.Current == Path.GetFullPath(a), session.Current);
        C("Forward 前进", session.Forward() && session.Current.EndsWith("PLC"), session.Current);
        C("Up 上一级", session.Up() && session.Current == Path.GetFullPath(a), session.Current);
        session.Home(); C("Home 返回聚合桌面", session.Current == FolderSession.DesktopHome, session.Current);

        Console.WriteLine("\n== File watcher ==");
        using (var watcher = new DesktopWatcher())
        {
            var changed = new ManualResetEventSlim(false); int events = 0;
            watcher.Changed += delegate { Interlocked.Increment(ref events); changed.Set(); };
            watcher.Watch(new[] { a });
            File.WriteAllText(Path.Combine(a, "watch-test.txt"), "1");
            C("文件变化在 3 秒内触发", changed.Wait(3000), "events=" + events);
            Thread.Sleep(500);
            C("事件已去抖合并", events <= 3, "events=" + events);
        }

        Console.WriteLine("\n== Real file operations ==");
        var ops = new ShellFileOperationService();
        var createdFolder = ops.CreateFolder(target, "NewFolder");
        C("真实创建文件夹", createdFolder.Ok && Directory.Exists(createdFolder.Outputs[0]), createdFolder.Message);
        C("撤销创建文件夹", ops.Undo().Ok && !Directory.Exists(createdFolder.Outputs[0]), "recycled");

        var createdFile = ops.CreateTextFile(target, "note.txt");
        C("真实创建文件", createdFile.Ok && File.Exists(createdFile.Outputs[0]), createdFile.Message);
        var renamed = ops.Rename(createdFile.Outputs[0], "renamed.txt");
        C("真实重命名", renamed.Ok && File.Exists(renamed.Outputs[0]), renamed.Message);
        C("撤销重命名", ops.Undo().Ok && File.Exists(createdFile.Outputs[0]), "restored");
        C("撤销创建文件", ops.Undo().Ok && !File.Exists(createdFile.Outputs[0]), "recycled");

        var source = Path.Combine(a, "source.txt"); File.WriteAllText(source, "source");
        var copied = ops.Copy(new[] { source }, target);
        C("真实复制", copied.Ok && File.Exists(copied.Outputs[0]) && File.Exists(source), copied.Message);
        C("撤销复制保留源文件", ops.Undo().Ok && !File.Exists(copied.Outputs[0]) && File.Exists(source), "ok");
        var moved = ops.Move(new[] { source }, target);
        C("真实移动", moved.Ok && !File.Exists(source) && File.Exists(moved.Outputs[0]), moved.Message);
        C("撤销移动", ops.Undo().Ok && File.Exists(source) && !File.Exists(moved.Outputs[0]), "ok");

        File.WriteAllText(Path.Combine(target, "source.txt"), "existing");
        var conflictCopy = ops.Copy(new[] { source }, target);
        C("同名复制生成安全副本名", conflictCopy.Ok && Path.GetFileName(conflictCopy.Outputs[0]).Contains("(2)"), Path.GetFileName(conflictCopy.Outputs[0]));
        ops.Undo();

        var permanent = Path.Combine(target, "permanent-delete.txt"); File.WriteAllText(permanent, "delete");
        C("永久删除仅作用于沙箱文件", ops.Delete(new[] { permanent }, true).Ok && !File.Exists(permanent), "deleted");
        var recycle = Path.Combine(target, "recycle-test-" + Guid.NewGuid().ToString("N") + ".txt"); File.WriteAllText(recycle, "recycle");
        C("删除默认进入 Windows 回收站", ops.Delete(new[] { recycle }, false).Ok && !File.Exists(recycle), "recycled");

        Console.WriteLine("\n== 1000 item performance ==");
        var large = Path.Combine(root, "Large"); Directory.CreateDirectory(large);
        for (int i = 0; i < 1000; i++) File.WriteAllText(Path.Combine(large, "F" + i.ToString("0000") + ".txt"), "x");
        var sw = Stopwatch.StartNew(); var thousand = DesktopScanner.ScanDirectory(large, new Dictionary<string, ItemOverride>(), categories); sw.Stop();
        C("1000 文件完整扫描", thousand.Count == 1000, "count=" + thousand.Count);
        C("1000 文件扫描 < 1000ms", sw.ElapsedMilliseconds < 1000, sw.ElapsedMilliseconds + "ms");

        Console.WriteLine("\n==== Smart Desktop 验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
