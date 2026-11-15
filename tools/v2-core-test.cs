// DeskBox v2 核心模块真机验收：每个功能用真实文件验证（正常 + 异常）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

public class V2Test
{
    static int pass = 0, fail = 0;
    static List<string> failures = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  [" + detail + "]" : "")); }
        else { fail++; failures.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    static string Sandbox;

    public static int Main()
    {
        DeskBox.Store store = new DeskBox.Store();
        DeskBox.Core.AppPaths.DataDir = Path.Combine(Path.GetTempPath(), "deskbox-v2-test-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(DeskBox.Core.AppPaths.DataDir);
        DeskBox.Store.DataDir = DeskBox.Core.AppPaths.DataDir;
        store.Init();

        Sandbox = Path.Combine(DeskBox.Core.AppPaths.DataDir, "sandbox");
        Directory.CreateDirectory(Sandbox);

        Console.WriteLine("== 环境 ==");
        Console.WriteLine("data  : " + DeskBox.Core.AppPaths.DataDir);
        Console.WriteLine("sandbox: " + Sandbox);
        Console.WriteLine();

        TestIndexAndSearch();
        TestLibrary();
        TestFileOpsAndUndo();
        TestPreview();
        TestNlQuery();
        TestAi();
        TestDisk();
        TestMonitor();
        TestWorkflow();

        Console.WriteLine();
        Console.WriteLine("==== 汇总: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in failures) Console.WriteLine("  ✗ " + f);
        return fail == 0 ? 0 : 1;
    }

    // ---------------- 1. 索引与搜索 ----------------
    static void TestIndexAndSearch()
    {
        Console.WriteLine("-- 1. 文件搜索索引 --");
        // 造 30 个文件：不同扩展名/大小/时间
        Directory.CreateDirectory(Path.Combine(Sandbox, "a"));
        Directory.CreateDirectory(Path.Combine(Sandbox, "b"));
        var rnd = new Random(7);
        for (int i = 0; i < 30; i++)
        {
            string ext = i % 3 == 0 ? ".xlsx" : (i % 3 == 1 ? ".pdf" : ".txt");
            string dir = i % 2 == 0 ? "a" : "b";
            string path = Path.Combine(Sandbox, dir, "校准记录_" + i.ToString("00") + ext);
            File.WriteAllText(path, new string('x', 100 + i * 200));
            File.SetLastWriteTime(path, DateTime.Now.AddDays(-(i % 10)));
        }

        var idx = DeskBox.Fs.FileIndex.Instance;
        idx.Configure(new List<string> { Sandbox }, new List<string>());
        idx.Rebuild();
        int waited = 0;
        while (idx.IsIndexing && waited < 15000) { System.Threading.Thread.Sleep(200); waited += 200; }
        Check("索引建立", idx.Count >= 30, "count=" + idx.Count + " 耗时≈" + waited + "ms");

        var q1 = new DeskBox.Core.SearchQuery { Text = "校准" };
        var r1 = idx.Search(q1);
        Check("按名称搜索", r1.TotalMatched == 30, "matched=" + r1.TotalMatched);

        var q2 = new DeskBox.Core.SearchQuery { Exts = new List<string> { ".xlsx" } };
        var r2 = idx.Search(q2);
        Check("按类型筛选 xlsx", r2.TotalMatched == 10, "matched=" + r2.TotalMatched);

        var q3 = new DeskBox.Core.SearchQuery { MinSize = 3000 };
        var r3 = idx.Search(q3);
        Check("按大小筛选 >3000B", r3.TotalMatched > 0 && r3.Hits.All(h => h.Entry.Size >= 3000), "matched=" + r3.TotalMatched);

        var q4 = new DeskBox.Core.SearchQuery { ModifiedAfter = DateTimeOffset.Now.AddDays(-3).ToUnixTimeMilliseconds() };
        var r4 = idx.Search(q4);
        Check("按时间筛选 最近3天", r4.TotalMatched > 0 && r4.TotalMatched < 30, "matched=" + r4.TotalMatched);

        var q5 = new DeskBox.Core.SearchQuery { Text = "校准", Exts = new List<string> { ".pdf" }, PathContains = "b" };
        var r5 = idx.Search(q5);
        Check("组合条件", r5.TotalMatched == 10 && r5.Hits.All(h => h.Entry.Name.ToLower().Contains("校准")), "matched=" + r5.TotalMatched);

        var q6 = new DeskBox.Core.SearchQuery { Text = "不存在的关键词zzz" };
        var r6 = idx.Search(q6);
        Check("无结果返回空", r6.TotalMatched == 0 && r6.Hits.Count == 0, "matched=" + r6.TotalMatched);

        var q7 = new DeskBox.Core.SearchQuery { SortBy = "size", SortDesc = true, Limit = 5 };
        var r7 = idx.Search(q7);
        bool sorted = true;
        for (int i = 1; i < r7.Hits.Count; i++) if (r7.Hits[i - 1].Entry.Size < r7.Hits[i].Entry.Size) sorted = false;
        Check("按大小降序 + limit", r7.Hits.Count == 5 && sorted, "returned=" + r7.Hits.Count);

        var sug = idx.Suggest("校准", 5);
        Check("启动器补全", sug.Count > 0, "suggest=" + sug.Count);

        // 异常：不存在的根
        idx.Configure(new List<string> { @"Z:\no-such-dir-xyz" }, new List<string>());
        idx.Rebuild();
        waited = 0;
        while (idx.IsIndexing && waited < 5000) { System.Threading.Thread.Sleep(100); waited += 100; }
        Check("不存在的根目录不崩", idx.Count == 0, "count=" + idx.Count);
        idx.Configure(new List<string> { Sandbox }, new List<string>());
        idx.Rebuild();
        waited = 0;
        while (idx.IsIndexing && waited < 15000) { System.Threading.Thread.Sleep(200); waited += 200; }
        Console.WriteLine();
    }

    // ---------------- 2. 收藏 / 最近 / 标签 ----------------
    static void TestLibrary()
    {
        Console.WriteLine("-- 2. 收藏与标签 --");
        var store = new DeskBox.Store(); store.Init();
        var lib = new DeskBox.Fs.FileLibrary(store);
        var f1 = Path.Combine(Sandbox, "a", "校准记录_00.xlsx");
        lib.AddFavorite(f1, "默认");
        lib.AddFavorite(f1, "默认");   // 去重
        Check("加入收藏（去重）", lib.Favorites.Count == 1 && lib.IsFavorite(f1), "count=" + lib.Favorites.Count);

        lib.Touch(f1); lib.Touch(f1); lib.Touch(f1);
        Check("最近访问计数", lib.Recent.Count == 1 && lib.Recent[0].OpenCount == 3, "count=" + lib.Recent.Count + " opens=" + (lib.Recent.Count > 0 ? lib.Recent[0].OpenCount : 0));

        lib.SetTags(f1, new List<string> { "校准", "重要" });
        Check("标签写入", lib.GetTags(f1).Count == 2, "tags=" + string.Join(",", lib.GetTags(f1).ToArray()));
        Check("按标签查路径", lib.PathsWithTag("校准").Count == 1, "paths=" + lib.PathsWithTag("校准").Count);

        lib.RemoveFavorite(f1);
        Check("移除收藏", !lib.IsFavorite(f1), "count=" + lib.Favorites.Count);

        var lib2 = new DeskBox.Fs.FileLibrary(store);
        Check("持久化后重载", lib2.GetTags(f1).Count == 2, "tags=" + lib2.GetTags(f1).Count);
        Console.WriteLine();
    }

    // ---------------- 3. 批量重命名 / 撤销 / 历史 ----------------
    static void TestFileOpsAndUndo()
    {
        Console.WriteLine("-- 3. 批量重命名与撤销 --");
        var store = new DeskBox.Store(); store.Init();
        var ops = new DeskBox.Fs.FileOps(store);
        var hist = new DeskBox.Fs.FileHistory(store);

        string dir = Path.Combine(Sandbox, "rename");
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        for (int i = 1; i <= 5; i++)
        {
            var p = Path.Combine(dir, "IMG_" + i + ".jpg");
            File.WriteAllText(p, "x");
            files.Add(p);
        }

        var rule = new DeskBox.Core.RenameRule { Find = "IMG_", Replace = "照片_", NumberPattern = "", TrimSpace = true };
        var plan = ops.PlanRename(files, rule, true);
        Check("规则预览 5 项", plan.Items.Count == 5 && plan.ChangedCount == 5, "changed=" + plan.ChangedCount);
        Check("预览不改文件", File.Exists(Path.Combine(dir, "IMG_1.jpg")), "原文件仍在");

        string msg;
        bool ok = ops.ExecuteRename(plan, out msg);
        Check("执行重命名", ok && File.Exists(Path.Combine(dir, "照片_1.jpg")) && !File.Exists(Path.Combine(dir, "IMG_1.jpg")), msg);
        hist.Record(new DeskBox.Core.FileOpRecord { Id = "r1", Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Op = "rename", Source = files[0], Target = Path.Combine(dir, "照片_1.jpg"), Ok = true, UndoId = ops.LastUndo != null ? ops.LastUndo.Id : "" });

        var last = ops.LastUndo;
        Check("产生撤销批次", last != null && last.Actions.Count == 5, "actions=" + (last != null ? last.Actions.Count : 0));

        bool undone = ops.Undo(last.Id, out msg);
        Check("撤销恢复原名", undone && File.Exists(Path.Combine(dir, "IMG_1.jpg")) && !File.Exists(Path.Combine(dir, "照片_1.jpg")), msg);

        bool again = ops.Undo(last.Id, out msg);
        Check("重复撤销被拒绝", !again, msg);

        // 冲突检测
        File.WriteAllText(Path.Combine(dir, "冲突.txt"), "a");
        File.WriteAllText(Path.Combine(dir, "目标.txt"), "b");
        var conflictPlan = ops.PlanRename(new List<string> { Path.Combine(dir, "冲突.txt") },
            new DeskBox.Core.RenameRule { Find = "冲突", Replace = "目标", TrimSpace = true }, true);
        Check("检出目标已存在冲突", conflictPlan.ConflictCount == 1, "conflicts=" + conflictPlan.ConflictCount + " reason=" + (conflictPlan.Items.Count > 0 ? conflictPlan.Items[0].ConflictReason : ""));

        // 非法字符
        var badPlan = ops.PlanRename(new List<string> { Path.Combine(dir, "IMG_2.jpg") },
            new DeskBox.Core.RenameRule { Find = "IMG_2", Replace = "a/b:c*d", TrimSpace = true }, true);
        var newName = badPlan.Items[0].NewName;
        Check("非法字符被替换", newName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "new=" + newName);

        // 移动 + 撤销
        string dest = Path.Combine(Sandbox, "moved");
        Directory.CreateDirectory(dest);
        bool moved = ops.MoveFiles(files.Take(2).ToList(), dest, out msg);
        Check("批量移动", moved && File.Exists(Path.Combine(dest, "IMG_1.jpg")), msg);
        var mv = ops.LastUndo;
        Check("移动可撤销并恢复", mv != null && ops.Undo(mv.Id, out msg) && File.Exists(Path.Combine(dir, "IMG_1.jpg")), msg);

        // 删除→备份
        bool del = ops.DeleteToBackup(new List<string> { Path.Combine(dir, "冲突.txt") }, out msg);
        Check("删除进备份（非永久）", del && !File.Exists(Path.Combine(dir, "冲突.txt")), msg);
        var dl = ops.LastUndo;
        Check("备份可恢复", dl != null && ops.Undo(dl.Id, out msg) && File.Exists(Path.Combine(dir, "冲突.txt")), msg);

        var q = hist.Query(20, "rename", "");
        Check("历史可查询", q.Count >= 1, "records=" + q.Count);
        Console.WriteLine();
    }

    // ---------------- 4. 预览 ----------------
    static void TestPreview()
    {
        Console.WriteLine("-- 4. 文件预览 --");
        string dir = Path.Combine(Sandbox, "preview");
        Directory.CreateDirectory(dir);

        // 图片
        var png = Path.Combine(dir, "img.png");
        using (var bmp = new Bitmap(120, 80))
        using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.SteelBlue); bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png); }
        var r1 = DeskBox.Fs.PreviewService.Produce(png, 200000);
        Check("图片预览", r1.Kind == "image" && r1.Image != null && r1.Image.Width == 120, "kind=" + r1.Kind);

        // UTF-8 文本
        var txt = Path.Combine(dir, "utf8.txt");
        File.WriteAllText(txt, "校准记录第一行\n第二行中文\n第三行", new UTF8Encoding(false));
        var r2 = DeskBox.Fs.PreviewService.Produce(txt, 200000);
        Check("UTF-8 文本预览", r2.Kind == "text" && r2.Text.Contains("校准记录"), "kind=" + r2.Kind + " len=" + r2.Text.Length);

        // GBK 文本
        var gbk = Path.Combine(dir, "gbk.txt");
        File.WriteAllText(gbk, "国标编码测试内容", Encoding.GetEncoding(936));
        var r3 = DeskBox.Fs.PreviewService.Produce(gbk, 200000);
        Check("GBK 文本预览", r3.Kind == "text" && r3.Text.Contains("国标"), "text=" + r3.Text.Trim());

        // CSV 表格
        var csv = Path.Combine(dir, "t.csv");
        File.WriteAllText(csv, "名称,数量,备注\n反应器,3,正常\n泵,7,待检", new UTF8Encoding(false));
        var r4 = DeskBox.Fs.PreviewService.Produce(csv, 200000);
        Check("CSV 表格预览", r4.Rows.Count >= 3 && r4.Rows[0].Length >= 3, "rows=" + r4.Rows.Count);

        // 截断
        var big = Path.Combine(dir, "big.txt");
        File.WriteAllText(big, new string('A', 5000), new UTF8Encoding(false));
        var r5 = DeskBox.Fs.PreviewService.Produce(big, 100);
        Check("超长文本截断", r5.Truncated || r5.Text.Length <= 120, "len=" + r5.Text.Length + " truncated=" + r5.Truncated);

        // 不存在文件
        var r6 = DeskBox.Fs.PreviewService.Produce(Path.Combine(dir, "nope.xyz"), 1000);
        Check("不存在文件不崩", r6 != null, "kind=" + r6.Kind);
        Console.WriteLine();
    }

    // ---------------- 5. 自然语言 ----------------
    static void TestNlQuery()
    {
        Console.WriteLine("-- 5. 自然语言搜索 --");
        var cases = new[]
        {
            "最近修改的 Excel",
            "大于100MB的视频",
            "包含校准记录 的文档",
            "上个月修改的图片",
            "最近7天的pdf",
            "mtime>2024-01-01 的表格",
        };
        foreach (var c in cases)
        {
            var q = DeskBox.Ai.NlQuery.Parse(c);
            var explained = DeskBox.Ai.NlQuery.Explain(q);
            bool ok = q.Exts.Count > 0 || q.MinSize > 0 || q.ModifiedAfter > 0 || q.Text.Length > 0;
            Check("解析「" + c + "」", ok, explained);
        }
        var q2 = DeskBox.Ai.NlQuery.Parse("大于100MB的视频");
        Check("大小+类型正确", q2.MinSize == 100L * 1024 * 1024 && q2.Exts.Contains(".mp4"), "min=" + q2.MinSize + " exts=" + string.Join(",", q2.Exts.ToArray()));
        var q3 = DeskBox.Ai.NlQuery.Parse("最近修改的 Excel");
        Check("时间+类型正确", q3.ModifiedAfter > 0 && q3.Exts.Contains(".xlsx"), "after=" + q3.ModifiedAfter + " exts=" + string.Join(",", q3.Exts.ToArray()));
        Console.WriteLine();
    }

    // ---------------- 6. AI 助手 ----------------
    static void TestAi()
    {
        Console.WriteLine("-- 6. AI 文件助手 --");
        string dir = Path.Combine(Sandbox, "ai");
        Directory.CreateDirectory(dir);
        var a = Path.Combine(dir, "a.txt");
        File.WriteAllText(a, "项目进度报告\n本周完成反应器调试，校准记录已归档。\n下周计划：泵体测试。\n风险：备件到货延迟。", new UTF8Encoding(false));
        var b = Path.Combine(dir, "b.txt");
        File.WriteAllText(b, "项目进度报告\n本周完成反应器调试，校准记录已归档。\n下周计划：泵体测试与验收。\n新增：供应商评审。", new UTF8Encoding(false));

        var ai = DeskBox.Ai.AiService.Instance;
        var sum = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "summarize", Paths = new List<string> { a } });
        Check("文档摘要", sum.Ok && sum.Meta.Count >= 3 && sum.Text.Length > 0, "meta=" + sum.Meta.Count + " bullets=" + sum.Bullets.Count);

        var cmp = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "compare", Paths = new List<string> { a, b } });
        bool hasSim = cmp.Meta.Any(m => m.Key == "相似度");
        Check("文件对比", cmp.Ok && hasSim, hasSim ? cmp.Meta.First(m => m.Key == "相似度").Value : "no similarity");

        var rep = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "report", Paths = new List<string> { a, b, Path.Combine(Sandbox, "a", "校准记录_00.xlsx") } });
        Check("工作总结", rep.Ok && rep.Bullets.Count > 0, "bullets=" + rep.Bullets.Count);

        var org = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "organize", Paths = Directory.GetFiles(Path.Combine(Sandbox, "a")).Take(10).ToList() });
        Check("整理建议", org.Ok && org.Bullets.Count > 0, "bullets=" + org.Bullets.Count);

        var bad = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "summarize", Paths = new List<string> { Path.Combine(dir, "nope.txt") } });
        Check("异常输入有错误提示", !bad.Ok && bad.Error.Length > 0, bad.Error);

        Check("默认引擎为本地", ai.ActiveProvider == "本地" && !ai.RemoteEnabled, ai.ActiveProvider);
        Console.WriteLine();
    }

    // ---------------- 7. 磁盘分析 ----------------
    static void TestDisk()
    {
        Console.WriteLine("-- 7. 磁盘分析 --");
        string root = Path.Combine(Sandbox, "disk");
        Directory.CreateDirectory(Path.Combine(root, "sub1"));
        Directory.CreateDirectory(Path.Combine(root, "sub2"));
        byte[] blob = new byte[8192];
        for (int i = 0; i < 4; i++) File.WriteAllBytes(Path.Combine(root, "sub1", "dup" + i + ".bin"), blob);  // 4 份相同
        File.WriteAllBytes(Path.Combine(root, "sub2", "big.bin"), new byte[3 * 1024 * 1024]);
        File.WriteAllText(Path.Combine(root, "small.txt"), "abc");

        var an = new DeskBox.Sys.DiskAnalyzer();
        var rep = an.Analyze(root, new List<string>(), 10, 10, true, null, null);
        long expect = 4L * 8192 + 3 * 1024 * 1024 + 3;
        Check("总大小准确", rep.TotalBytes == expect, "actual=" + rep.TotalBytes + " expect=" + expect);
        Check("文件数准确", rep.FileCount == 6, "count=" + rep.FileCount);
        Check("大文件排序", rep.LargeFiles.Count > 0 && rep.LargeFiles[0].Size == 3 * 1024 * 1024, "top=" + (rep.LargeFiles.Count > 0 ? rep.LargeFiles[0].Size : 0));
        Check("目录聚合", rep.Dirs.Count == 2, "dirs=" + rep.Dirs.Count);
        Check("重复文件检出", rep.Duplicates.Count == 1 && rep.Duplicates[0].Paths.Count == 4, "groups=" + rep.Duplicates.Count + " paths=" + (rep.Duplicates.Count > 0 ? rep.Duplicates[0].Paths.Count : 0));
        Check("浪费空间计算", rep.Duplicates.Count > 0 && rep.Duplicates[0].Wasted == 3L * 8192, "wasted=" + (rep.Duplicates.Count > 0 ? rep.Duplicates[0].Wasted : 0));

        var rep2 = an.Analyze(Path.Combine(Sandbox, "no-such"), new List<string>(), 5, 5, false, null, null);
        Check("不存在的目录有错误提示", rep2.Error.Length > 0, rep2.Error);
        Console.WriteLine();
    }

    // ---------------- 8. 系统监控 ----------------
    static void TestMonitor()
    {
        Console.WriteLine("-- 8. 系统监控 --");
        var mon = new DeskBox.Sys.SystemMonitor(800);
        DeskBox.Core.SystemSample lastSample = null;
        mon.Updated += delegate(DeskBox.Core.SystemSample s) { lastSample = s; };
        mon.Start();
        System.Threading.Thread.Sleep(3000);
        mon.Stop();
        Check("收到采样", lastSample != null, lastSample == null ? "none" : "ok");
        if (lastSample != null)
        {
            Check("CPU 0..100", lastSample.CpuPercent >= 0 && lastSample.CpuPercent <= 100, lastSample.CpuPercent.ToString("0.0") + "%");
            Check("内存总量 > 0", lastSample.MemTotal > 0 && lastSample.MemUsedPercent > 0, DeskBox.UI.UiKit.HumanSize(lastSample.MemTotal));
            Check("磁盘卷 >= 1", lastSample.Disks.Count >= 1, "disks=" + lastSample.Disks.Count);
            Check("网络速率 >= 0", lastSample.NetRxBps >= 0 && lastSample.NetTxBps >= 0, lastSample.NetRxBps.ToString("0") + "/" + lastSample.NetTxBps.ToString("0") + " B/s");
        }
        Console.WriteLine();
    }

    // ---------------- 9. 自动化工作流 ----------------
    static void TestWorkflow()
    {
        Console.WriteLine("-- 9. 自动化工作流 --");
        var store = new DeskBox.Store(); store.Init();
        var eng = new DeskBox.Automation.WorkflowEngine(store);
        string root = Path.Combine(Sandbox, "wf");
        string dest = Path.Combine(Sandbox, "wf-out");
        Directory.CreateDirectory(root);
        for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(root, "r" + i + ".xlsx"), "x");

        var wf = eng.Add("测试移动", "manual", 60);
        var act = new DeskBox.Automation.WorkflowStep { Type = "move" };
        act.Params["root"] = root;
        act.Params["pattern"] = "*.xlsx";
        act.Params["targetDir"] = dest;
        wf.Actions.Add(act);
        eng.Save();

        var dry = eng.Run(wf.Id, true);
        Check("预演不改文件", dry.Ok && Directory.GetFiles(root).Length == 5, dry.Message);
        Check("预演报告影响数", dry.Affected == 5, "affected=" + dry.Affected);

        var real = eng.Run(wf.Id, false);
        Check("真实执行移动", real.Ok && Directory.GetFiles(root).Length == 0 && Directory.GetFiles(dest).Length == 5, real.Message);
        Check("运行记录已保存", eng.Runs.Count >= 2, "runs=" + eng.Runs.Count);

        var badWf = eng.Add("错误目录", "manual", 60);
        var badAct = new DeskBox.Automation.WorkflowStep { Type = "move" };
        badAct.Params["root"] = @"Z:\nope";
        badWf.Actions.Add(badAct);
        var badRun = eng.Run(badWf.Id, true);
        Check("目录不存在时有明确错误", !badRun.Ok && badRun.Message.Contains("不存在"), badRun.Message);
        Console.WriteLine();
    }
}


