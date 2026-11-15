// 工作流 v2 验收（规格 5.1–5.3）：节点顺序、数据传递、AI 节点、取消、超时、失败策略、确认、记录
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Automation;
using DeskBox.Core;

public class WfAccept
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    static WorkflowNode Node(string type, string kind, params string[] kv)
    {
        var n = new WorkflowNode
        {
            Id = "n_" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Type = type, Kind = kind,
            Params = new Dictionary<string, string>()
        };
        for (int i = 0; i + 1 < kv.Length; i += 2) n.Params[kv[i]] = kv[i + 1];
        return n;
    }

    public static int Main(string[] args)
    {
        var dir = Path.Combine(Path.GetTempPath(), "wfacc-" + DateTime.Now.Ticks);
        var work = Path.Combine(dir, "work");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(work);
        DeskBox.Core.AppPaths.DataDir = dir;
        DeskBox.Store.DataDir = dir;

        File.WriteAllText(Path.Combine(work, "a.txt"), "第一份文档内容：季度目标是提升转化率。");
        File.WriteAllText(Path.Combine(work, "b.txt"), "第二份文档内容：预算为十二万元。");
        File.WriteAllText(Path.Combine(work, "c.log"), "日志内容");

        var store = new DeskBox.Store();
        store.Init();
        store.Settings.WorkflowFailMode = "stop";
        store.Settings.WorkflowConcurrency = 1;
        store.SaveSettingsOnly();

        var engine = new WorkflowEngine(store);
        engine.ConfirmDanger = delegate(string t2) { return true; };   // 模拟用户允许写入
        Check("执行器已初始化", engine.Runner != null, engine.Runner == null ? "null" : "ok");

        Console.WriteLine();
        Console.WriteLine("== 1. 节点顺序与数据传递（搜索 → 读取 → 写入） ==");
        var wf = engine.Add("测试流水线", "manual", 0);
        wf.UseNodes = true;
        wf.Description = "搜索 txt → 读取第一个 → 写入汇总";
        wf.Nodes = new List<WorkflowNode>
        {
            Node("trigger", "manual"),
            Node("action", "search", "root", work, "pattern", "*.txt"),
            Node("action", "read", "input", "{{files}}"),
            Node("action", "write", "path", Path.Combine(work, "out-summary.txt"), "input", "{{prev}}")
        };
        engine.Save();

        var run = engine.RunV2(wf.Id, false);
        Check("执行成功", run.Ok, run.Message);
        Check("节点记录 4 条（含触发）", run.NodeRecords.Count == 4, "count=" + run.NodeRecords.Count);
        Check("顺序正确：search→read→write",
            run.NodeRecords.Count >= 4 && run.NodeRecords[1].Kind == "search" && run.NodeRecords[2].Kind == "read" && run.NodeRecords[3].Kind == "write",
            string.Join("→", run.NodeRecords.Select(r => r.Kind).ToArray()));
        Check("写入文件真实产生", File.Exists(Path.Combine(work, "out-summary.txt")), "exists=" + File.Exists(Path.Combine(work, "out-summary.txt")));
        var written = File.Exists(Path.Combine(work, "out-summary.txt")) ? File.ReadAllText(Path.Combine(work, "out-summary.txt")) : "";
        Check("数据传递：写入内容来自上一节点", written.Contains("第一份文档"), "len=" + written.Length);
        Check("每节点有耗时与输出摘要", run.NodeRecords.All(r => r.ElapsedMs >= 0 && r.OutputSummary.Length > 0 || r.Skipped),
            string.Join(" | ", run.NodeRecords.Select(r => r.Kind + ":" + r.ElapsedMs + "ms").ToArray()));

        Console.WriteLine();
        Console.WriteLine("== 2. 预演不落地 ==");
        var beforeWrite = File.ReadAllText(Path.Combine(work, "out-summary.txt"));
        File.Delete(Path.Combine(work, "out-summary.txt"));
        var dry = engine.RunV2(wf.Id, true);
        Check("预演成功", dry.Ok, dry.Message);
        Check("预演不写文件", !File.Exists(Path.Combine(work, "out-summary.txt")), "exists=" + File.Exists(Path.Combine(work, "out-summary.txt")));

        Console.WriteLine();
        Console.WriteLine("== 3. 高危操作 fail-closed（无确认通道必须拒绝） ==");
        var wfWrite = engine.Add("写文件", "manual", 0);
        wfWrite.UseNodes = true;
        wfWrite.Nodes = new List<WorkflowNode>
        {
            Node("action", "write", "path", Path.Combine(work, "danger.txt"), "input", "危险内容")
        };
        engine.Save();
        engine.ConfirmDanger = null;
        var rNoConfirm = engine.RunV2(wfWrite.Id, false);
        Check("无确认通道时拒绝写入", !rNoConfirm.Ok && !File.Exists(Path.Combine(work, "danger.txt")),
            rNoConfirm.Message + " exists=" + File.Exists(Path.Combine(work, "danger.txt")));

        int confirmCalls = 0;
        engine.ConfirmDanger = delegate(string t) { confirmCalls++; return true; };
        var rConfirm = engine.RunV2(wfWrite.Id, false);
        Check("有确认且允许时写入成功", rConfirm.Ok && File.Exists(Path.Combine(work, "danger.txt")), "confirmCalls=" + confirmCalls);

        Console.WriteLine();
        Console.WriteLine("== 4. 条件节点 ==");
        var wfCond = engine.Add("条件流", "manual", 0);
        wfCond.UseNodes = true;
        wfCond.Nodes = new List<WorkflowNode>
        {
            Node("condition", "fileexists", "path", Path.Combine(work, "a.txt")),
            Node("action", "notify", "text", "文件存在，继续")
        };
        engine.Save();
        var rCond = engine.RunV2(wfCond.Id, false);
        Check("条件满足则继续", rCond.Ok, rCond.Message);

        var wfCond2 = engine.Add("条件流2", "manual", 0);
        wfCond2.UseNodes = true;
        wfCond2.Nodes = new List<WorkflowNode>
        {
            Node("condition", "fileexists", "path", Path.Combine(work, "不存在.txt")),
            Node("action", "notify", "text", "不该执行到这里")
        };
        engine.Save();
        var rCond2 = engine.RunV2(wfCond2.Id, false);
        Check("条件不满足则停止", !rCond2.Ok, rCond2.Message);

        Console.WriteLine();
        Console.WriteLine("== 5. 取消 ==");
        var wfSlow = engine.Add("慢流程", "manual", 0);
        wfSlow.UseNodes = true;
        wfSlow.Nodes = new List<WorkflowNode>
        {
            Node("action", "search", "root", work, "pattern", "*"),
            Node("ai", "generate", "instruction", "生成长文本", "input", "x")
        };
        engine.Save();
        string cancelRunId = "r_cancel_test";
        var cancelThread = new Thread(delegate()
        {
            Thread.Sleep(300);
            engine.Cancel(cancelRunId);
        });
        cancelThread.IsBackground = true;
        cancelThread.Start();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rCancel = engine.Runner.Run(wfSlow, wfSlow.Nodes, false, cancelRunId);
        sw.Stop();
        Check("取消后不报告成功", !rCancel.Ok, rCancel.Message);
        Check("取消响应在 30 秒内", sw.ElapsedMilliseconds < 30000, "ms=" + sw.ElapsedMilliseconds);

        Console.WriteLine();
        Console.WriteLine("== 6. 失败策略（stop / skip） ==");
        store.Settings.WorkflowFailMode = "stop";
        store.SaveSettingsOnly();
        var wfFail = engine.Add("失败停止", "manual", 0);
        wfFail.UseNodes = true;
        wfFail.Nodes = new List<WorkflowNode>
        {
            Node("action", "read", "input", Path.Combine(work, "不存在.txt")),
            Node("action", "notify", "text", "不应执行")
        };
        engine.Save();
        var rStop = engine.RunV2(wfFail.Id, false);
        Check("stop 策略：失败后停止", !rStop.Ok && rStop.NodeRecords.Count == 1, "nodes=" + rStop.NodeRecords.Count + " msg=" + rStop.Message);

        store.Settings.WorkflowFailMode = "skip";
        store.SaveSettingsOnly();
        var rSkip = engine.RunV2(wfFail.Id, false);
        Check("skip 策略：继续执行后续节点", rSkip.NodeRecords.Count == 2, "nodes=" + rSkip.NodeRecords.Count);

        Console.WriteLine();
        Console.WriteLine("== 7. 重试次数上限（不会无限执行） ==");
        var wfRetry = engine.Add("重试", "manual", 0);
        wfRetry.UseNodes = true;
        var retryNode = Node("action", "read", "input", Path.Combine(work, "不存在.txt"));
        retryNode.Retries = 2;
        wfRetry.Nodes = new List<WorkflowNode> { retryNode };
        engine.Save();
        var rRetry = engine.RunV2(wfRetry.Id, false);
        Check("重试次数受限（1+2=3 次）", rRetry.NodeRecords.Count == 1 && rRetry.NodeRecords[0].Attempts == 3,
            "attempts=" + (rRetry.NodeRecords.Count > 0 ? rRetry.NodeRecords[0].Attempts : 0));

        Console.WriteLine();
        Console.WriteLine("== 8. 复制工作流 ==");
        var copy = engine.Copy(wf.Id);
        Check("复制成功", copy != null && copy.Id != wf.Id, copy == null ? "null" : copy.Name);
        Check("复制含节点且节点 Id 不同", copy != null && copy.Nodes.Count == wf.Nodes.Count && copy.Nodes[1].Id != wf.Nodes[1].Id,
            copy == null ? "null" : "nodes=" + copy.Nodes.Count);

        Console.WriteLine();
        Console.WriteLine("== 9. 旧配置兼容（无 Nodes 走旧逻辑） ==");
        var legacy = engine.Add("旧工作流", "manual", 0);
        legacy.UseNodes = false;
        legacy.Nodes = new List<WorkflowNode>();
        legacy.Actions = new List<WorkflowStep>
        {
            new WorkflowStep { Type = "log", Params = new Dictionary<string, string> { { "root", work }, { "pattern", "*.log" } } }
        };
        engine.Save();
        var rLegacy = engine.RunV2(legacy.Id, false);
        Check("旧工作流仍可执行", rLegacy.Ok, rLegacy.Message);

        Console.WriteLine();
        Console.WriteLine("== 10. 运行记录 ==");
        var logs = store.ReadLogs(200);
        Check("工作流执行写入工作记录", logs.Any(l => (l.Action ?? "") == "automation"), "count=" + logs.Count(l => l.Action == "automation"));
        var runsFile = Path.Combine(dir, "workflow-runs.jsonl");
        Check("运行记录落盘", File.Exists(runsFile) && File.ReadAllLines(runsFile).Length >= 3,
            "lines=" + (File.Exists(runsFile) ? File.ReadAllLines(runsFile).Length : 0));

        Console.WriteLine();
        Console.WriteLine("==== 工作流 v2 验收: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        try { Directory.Delete(dir, true); } catch { }
        return fail == 0 ? 0 : 1;
    }
}
