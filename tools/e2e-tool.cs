// 端到端：ChatService + 真实 HTTP mock（返回工具块）+ 真实文件系统
// 验证「AI 说 → 工具真执行 → 结果回灌 → AI 基于结果再回答」的完整闭环。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;

public class E2eTool
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  [" + detail + "]" : "")); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    public static int Main(string[] args)
    {
        int port = 18082;
        string workDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "deskbox-e2e-work");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "报告A.pdf"), "a");
        File.WriteAllText(Path.Combine(workDir, "报告B.pdf"), "b");
        File.WriteAllText(Path.Combine(workDir, "说明.txt"), "c");

        var dataDir = Path.Combine(Path.GetTempPath(), "deskbox-e2e-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dataDir);
        DeskBox.Core.AppPaths.DataDir = dataDir;
        DeskBox.Store.DataDir = dataDir;

        var store = new DeskBox.Store();
        store.Init();

        var ps = new ProviderStore();
        var p = new AiProvider
        {
            Name = "E2E-Mock",
            Protocol = "openai",
            BaseUrl = "http://127.0.0.1:" + port,
            Model = "mock-chat",
            ApiKey = "sk-e2e",
            TimeoutMs = 15000,
            MaxRetries = 0,
            Enabled = true
        };
        ps.Add(p);

        var chat = new ChatService(store, ps);
        chat.AllowedRoots = new List<string> { workDir };
        int confirmCalls = 0;
        chat.ConfirmTool = delegate(string toolName, string detail)
        {
            confirmCalls++;
            Console.WriteLine("  [确认请求] " + toolName);
            return true;   // 自动允许（本测试仅用非高危的 fs.search，不会触发确认）
        };

        chat.NewSession();
        chat.SetProvider(p.Id, "mock-chat");

        var done = new ManualResetEventSlim(false);
        chat.Changed += delegate
        {
            if (!chat.Busy && chat.Current != null && chat.Current.Messages.Count >= 3) done.Set();
        };

        Console.WriteLine("== 发送会触发工具调用的消息 ==");
        chat.Send("帮我找一下目录里的 pdf 文件");
        bool ok = done.Wait(40000);

        var msgs = chat.Current.Messages;
        Console.WriteLine("  消息数=" + msgs.Count + " busy=" + chat.Busy);
        foreach (var m in msgs)
            Console.WriteLine("    " + m.Role + " len=" + (m.Content ?? "").Length +
                (string.IsNullOrEmpty(m.ToolName) ? "" : " tool=" + m.ToolName) + (m.Error ?? ""));

        Check("对话在超时内完成", ok && !chat.Busy, "done=" + ok + " busy=" + chat.Busy);
        Check("产生了工具消息", msgs.Any(m => m.Role == "tool"), "tool msgs=" + msgs.Count(m => m.Role == "tool"));
        var toolMsg = msgs.FirstOrDefault(m => m.Role == "tool");
        Check("工具是 fs.search", toolMsg != null && toolMsg.ToolName == "fs.search", toolMsg == null ? "none" : toolMsg.ToolName);
        Check("工具真实找到 2 个 pdf", toolMsg != null && toolMsg.Content.Contains("报告A") && toolMsg.Content.Contains("报告B"),
            toolMsg == null ? "none" : (toolMsg.Content.Length > 60 ? toolMsg.Content.Substring(0, 60) : toolMsg.Content));
        Check("工具结果状态为成功", toolMsg != null && toolMsg.Content.StartsWith("✅"), toolMsg == null ? "none" : toolMsg.Content.Substring(0, Math.Min(2, toolMsg.Content.Length)));

        var lastAssistant = msgs.LastOrDefault(m => m.Role == "assistant");
        Check("AI 基于工具结果继续回答", lastAssistant != null && lastAssistant.Content.Contains("报告A") || (lastAssistant != null && lastAssistant.Content.Contains("pdf")),
            lastAssistant == null ? "none" : ("len=" + lastAssistant.Content.Length));
        Check("第二轮没有再调工具（防循环）", msgs.Count(m => m.Role == "tool") == 1, "tool count=" + msgs.Count(m => m.Role == "tool"));

        Console.WriteLine();
        Console.WriteLine("== 工作记录（工具调用必须留痕） ==");
        var logs = store.ReadLogs(200);
        var toolLogs = logs.Where(l => (l.Message ?? "").Contains("工具调用")).ToList();
        Check("日志记录了工具调用", toolLogs.Count >= 1, "count=" + toolLogs.Count);
        if (toolLogs.Count > 0) Console.WriteLine("    " + toolLogs[0].Message);
        Check("日志不含 API Key", !logs.Any(l => (l.Message ?? "").Contains("sk-e2e")), "checked " + logs.Count + " logs");

        Console.WriteLine();
        Console.WriteLine("==== 汇总: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        return fail == 0 ? 0 : 1;
    }
}
