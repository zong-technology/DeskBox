// 工具调用验收：验证 AI 回复中的 ```tool 块被解析 → 白名单 → 授权目录 → 确认 → 执行 → 回灌
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;

public class ToolAccept
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  [" + detail + "]" : "")); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    // 模拟一个「会说工具调用」的模型：把最后一条用户消息原样映射成工具块
    class FakeClient { }

    public static int Main()
    {
        Console.WriteLine("== A. 解析器单元测试 ==");
        var text = "我需要找一下文件。\n\n```tool\n{\"name\":\"fs.search\",\"args\":{\"root\":\"D:\\\\tmp\",\"keyword\":\"pdf\",\"limit\":\"5\"}}\n```\n\n稍等。";
        var calls = ToolCallParser.Parse(text);
        Check("解析出 1 个工具调用", calls.Count == 1, "count=" + calls.Count);
        Check("工具名正确", calls.Count > 0 && calls[0].Name == "fs.search", calls.Count > 0 ? calls[0].Name : "");
        Check("参数解析正确", calls.Count > 0 && calls[0].Args.ContainsKey("keyword") && calls[0].Args["keyword"] == "pdf",
            calls.Count > 0 ? string.Join(",", calls[0].Args.Keys.ToArray()) : "");

        var two = "```tool\n{\"name\":\"a\",\"args\":{}}\n```\n文本\n```tool\n{\"name\":\"b\",\"args\":{\"x\":\"1\"}}\n```";
        Check("多块解析", ToolCallParser.Parse(two).Count == 2, "count=" + ToolCallParser.Parse(two).Count);
        Check("非法 JSON 被忽略而不抛异常", ToolCallParser.Parse("```tool\n{not json}\n```").Count == 0, "ok");
        Check("无工具块返回空", ToolCallParser.Parse("普通回答，没有工具。").Count == 0, "ok");
        Check("非 tool 标记的代码块不误判", ToolCallParser.Parse("```csharp\nvar x = 1;\n```").Count == 0, "ok");

        Console.WriteLine();
        Console.WriteLine("== B. 工具执行（真实文件系统） ==");
        var dataDir = Path.Combine(Path.GetTempPath(), "deskbox-tool-" + DateTime.Now.Ticks);
        var workDir = Path.Combine(dataDir, "work");
        var outsideDir = Path.Combine(Path.GetTempPath(), "deskbox-outside-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outsideDir);
        DeskBox.Core.AppPaths.DataDir = dataDir;
        DeskBox.Store.DataDir = dataDir;

        // 造 3 个真实文件
        File.WriteAllText(Path.Combine(workDir, "报告A.pdf"), "pdf-a");
        File.WriteAllText(Path.Combine(workDir, "报告B.pdf"), "pdf-b");
        File.WriteAllText(Path.Combine(workDir, "说明.txt"), "txt");

        var store = new DeskBox.Store();
        store.Init();

        var ctx = new ToolContext
        {
            Store = store,
            AllowedRoots = new List<string> { workDir },
            Ai = AiService.Instance,
            Workflows = null
        };

        var r1 = ToolRegistry.Instance.Invoke("fs.search",
            new Dictionary<string, string> { { "root", workDir }, { "keyword", "pdf" }, { "limit", "10" } }, ctx);
        Check("fs.search 真实返回 2 个 pdf", r1.Ok && r1.Output.Contains("报告A.pdf") && r1.Output.Contains("报告B.pdf"),
            r1.Ok ? r1.Output.Replace("\n", " | ").Substring(0, Math.Min(80, r1.Output.Length)) : r1.Error);

        var r2 = ToolRegistry.Instance.Invoke("fs.search",
            new Dictionary<string, string> { { "root", outsideDir }, { "keyword", "x" }, { "limit", "5" } }, ctx);
        Check("越权目录被拒绝", !r2.Ok && r2.Error.Contains("授权"), r2.Ok ? "竟然成功" : r2.Error);

        var r3 = ToolRegistry.Instance.Invoke("fs.read",
            new Dictionary<string, string> { { "path", Path.Combine(workDir, "说明.txt") } }, ctx);
        Check("fs.read 读到真实内容", r3.Ok && r3.Output.Contains("txt"), r3.Ok ? "len=" + r3.Output.Length : r3.Error);

        var r4 = ToolRegistry.Instance.Invoke("fs.read",
            new Dictionary<string, string> { { "path", Path.Combine(outsideDir, "nope.txt") } }, ctx);
        Check("越权读取被拒绝", !r4.Ok, r4.Ok ? "竟然成功" : r4.Error);

        var target = Path.Combine(workDir, "ai-输出.txt");
        var r5 = ToolRegistry.Instance.Invoke("fs.write",
            new Dictionary<string, string> { { "path", target }, { "content", "AI 生成内容" } }, ctx);
        Check("fs.write 需要确认（未注入确认回调时拒绝）", !r5.Ok, r5.Ok ? "竟然成功" : r5.Error);

        ctx.Confirm = delegate(string t) { return true; };
        var r6 = ToolRegistry.Instance.Invoke("fs.write",
            new Dictionary<string, string> { { "path", target }, { "content", "AI 生成内容" } }, ctx);
        Check("确认后真实写入成功", r6.Ok && File.Exists(target) && File.ReadAllText(target) == "AI 生成内容",
            r6.Ok ? "文件存在" : r6.Error);

        ctx.Confirm = delegate(string t) { return false; };
        var r7 = ToolRegistry.Instance.Invoke("fs.write",
            new Dictionary<string, string> { { "path", Path.Combine(workDir, "拒绝.txt") }, { "content", "x" } }, ctx);
        Check("用户拒绝时不写入", !r7.Ok && !File.Exists(Path.Combine(workDir, "拒绝.txt")), r7.Ok ? "竟然成功" : r7.Error);

        var r8 = ToolRegistry.Instance.Invoke("task.create",
            new Dictionary<string, string> { { "title", "AI 创建的任务" }, { "priority", "high" } }, ctx);
        Check("task.create 真正写入任务列表", r8.Ok && store.Tasks.Any(t => t.Title == "AI 创建的任务"),
            "tasks=" + store.Tasks.Count);

        var r9 = ToolRegistry.Instance.Invoke("不存在的工具", new Dictionary<string, string>(), ctx);
        Check("未注册工具被拒绝", !r9.Ok && r9.Error.Contains("未注册"), r9.Ok ? "竟然成功" : r9.Error);

        // 任务持久化验证
        var store2 = new DeskBox.Store();
        store2.Init();
        Check("任务落盘可重新读取", store2.Tasks.Any(t => t.Title == "AI 创建的任务"), "reloaded=" + store2.Tasks.Count);

        Console.WriteLine();
        Console.WriteLine("== C. 提示词包含工具清单与授权目录 ==");
        var ps = new ProviderStore();
        var chat = new ChatService(store, ps);
        chat.AllowedRoots = new List<string> { workDir };
        var session = chat.NewSession();
        var buildMethod = typeof(ChatService).GetMethod("BuildContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var msgs = (List<AiMessage>)buildMethod.Invoke(chat, new object[] { session });
        var system = msgs.First(m => m.Role == "system").Content;
        Check("系统提示含工具清单", system.Contains("fs.search") && system.Contains("task.create"), "len=" + system.Length);
        Check("系统提示含授权目录", system.Contains(workDir), "ok");
        Check("系统提示含禁止任意命令", system.Contains("不能直接执行系统命令"), "ok");

        Console.WriteLine();
        Console.WriteLine("==== 汇总: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        try { Directory.Delete(dataDir, true); Directory.Delete(outsideDir, true); } catch { }
        return fail == 0 ? 0 : 1;
    }
}
