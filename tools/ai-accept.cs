// AI 链路真机验收：ProviderStore(DPAPI) + AiClient(流式/错误分类) + ChatService(多轮/取消/重生成)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;

public class AiAccept
{
    static int pass = 0, fail = 0;
    static readonly List<string> fails = new List<string>();
    const string BaseUrl = "http://127.0.0.1:18080";

    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + (detail.Length > 0 ? "  [" + detail + "]" : "")); }
        else { fail++; fails.Add(name + " :: " + detail); Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    public static int Main()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "deskbox-ai-accept-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dataDir);
        DeskBox.Core.AppPaths.DataDir = dataDir;
        DeskBox.Store.DataDir = dataDir;

        var store = new DeskBox.Store();
        store.Init();

        Console.WriteLine("== 1. 服务商管理（DPAPI 加密） ==");
        var ps = new ProviderStore();
        var p = new AiProvider
        {
            Name = "本地Mock",
            Protocol = "openai",
            BaseUrl = BaseUrl,
            Model = "mock-chat",
            ApiKey = "sk-test-1234567890abcdef",
            TimeoutMs = 15000,
            MaxRetries = 1,
            Enabled = true
        };
        ps.Add(p);
        Check("新增服务商", ps.Providers.Count == 1 && ps.DefaultProvider != null, "count=" + ps.Providers.Count + " default=" + (ps.DefaultProvider != null ? ps.DefaultProvider.Name : "null"));

        ps.Save();
        var json = File.ReadAllText(Path.Combine(dataDir, "ai-providers.json"));
        Check("jar 内无明文 Key", !json.Contains("sk-test-1234567890abcdef"), "json len=" + json.Length);

        var ps2 = new ProviderStore();
        var reloaded = ps2.Providers.FirstOrDefault();
        Check("重载能解密 Key", reloaded != null && reloaded.ApiKey == "sk-test-1234567890abcdef", reloaded == null ? "none" : ("key len=" + (reloaded.ApiKey ?? "").Length));
        Check("MaskKey 脱敏", ProviderStore.MaskKey("sk-test-1234567890abcdef").IndexOf("1234567890") < 0, ProviderStore.MaskKey("sk-test-1234567890abcdef"));

        Console.WriteLine();
        Console.WriteLine("== 2. 连接测试（真实 HTTP） ==");
        var client = new AiClient(ps2.DefaultProvider);
        var t = client.TestConnection(new CancellationTokenSource(15000).Token);
        Check("测试连接成功", t.Ok, "status=" + t.HttpStatus + " ms=" + t.ElapsedMs);

        foreach (var c in new[]
        {
            new { Model = "err-auth", Kind = "auth" },
            new { Model = "err-model", Kind = "model" },
            new { Model = "err-ratelimit", Kind = "ratelimit" },
            new { Model = "err-server", Kind = "server" },
            new { Model = "err-protocol", Kind = "protocol" },
        })
        {
            var bad = ps2.Providers.First().Clone();
            bad.Model = c.Model;
            var r = new AiClient(bad).Chat(new AiChatRequest
            {
                Model = bad.Model,
                Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "hi" } }
            }, new CancellationTokenSource(15000).Token);
            Check("错误分类 " + c.Model, !r.Ok && r.ErrorKind == c.Kind, "kind=" + r.ErrorKind + " status=" + r.HttpStatus);
        }

        var badUrl = ps2.Providers.First().Clone();
        badUrl.BaseUrl = "http://127.0.0.1:1";
        var rUrl = new AiClient(badUrl).Chat(new AiChatRequest { Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "x" } } }, new CancellationTokenSource(8000).Token);
        Check("网络失败分类", !rUrl.Ok && rUrl.ErrorKind == "network", "kind=" + rUrl.ErrorKind);

        var toModel = ps2.Providers.First().Clone();
        toModel.Model = "err-timeout";
        toModel.TimeoutMs = 2000;
        var rTo = new AiClient(toModel).Chat(new AiChatRequest { Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "x" } } }, new CancellationTokenSource(9000).Token);
        Check("超时分类", !rTo.Ok && rTo.ErrorKind == "timeout", "kind=" + rTo.ErrorKind + " ms=" + rTo.ElapsedMs);

        Console.WriteLine();
        Console.WriteLine("== 3. 流式输出 ==");
        var sb = new System.Text.StringBuilder();
        int chunks = 0;
        new AiClient(ps2.DefaultProvider).ChatStream(
            new AiChatRequest { Model = "mock-chat", Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "流式测试" } } },
            new CancellationTokenSource(20000).Token,
            delegate(AiStreamChunk ch)
            {
                if (!string.IsNullOrEmpty(ch.Delta)) { sb.Append(ch.Delta); chunks++; }
            });
        Check("流式收到多个分片", chunks > 5, "chunks=" + chunks + " len=" + sb.Length);
        Check("流式内容完整", sb.ToString().Contains("mock 回复") && sb.ToString().Contains("第一点"), "text=" + (sb.Length > 40 ? sb.ToString(0, 40) : sb.ToString()));

        Console.WriteLine();
        Console.WriteLine("== 4. 取消 ==");
        var cts = new CancellationTokenSource();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var cancelModel = ps2.Providers.First().Clone();
        cancelModel.Model = "err-timeout";
        new Thread(delegate()
        {
            Thread.Sleep(700);
            cts.Cancel();
        }).Start();
        var rCancel = new AiClient(cancelModel).Chat(new AiChatRequest { Messages = new List<AiMessage> { new AiMessage { Role = "user", Content = "x" } } }, cts.Token);
        sw.Stop();
        Check("取消能中断请求", !rCancel.Ok && sw.ElapsedMilliseconds < 6000, "ms=" + sw.ElapsedMilliseconds + " kind=" + rCancel.ErrorKind);

        Console.WriteLine();
        Console.WriteLine("== 5. ChatService 多轮 / 历史 / 重生成 ==");
        var chat = new ChatService(store, ps2);
        var session = chat.NewSession();
        chat.SetProvider(ps2.DefaultProvider.Id, "mock-chat");

        var done = new ManualResetEventSlim(false);
        chat.Changed += delegate
        {
            if (chat.Busy || chat.Current == null) return;
            // 必须等「最后一条就是已完成的 assistant」，否则第一轮的回复会让等待提前返回
            var last = chat.Current.Messages.Count > 0 ? chat.Current.Messages[chat.Current.Messages.Count - 1] : null;
            if (last != null && last.Role == "assistant" && !last.Streaming) done.Set();
        };
        chat.Send("第一轮问题");
        bool ok1 = done.Wait(25000);
        Check("发送并收到回复", ok1 && chat.Current.Messages.Count >= 2, "count=" + chat.Current.Messages.Count);
        var firstReply = chat.Current.Messages.LastOrDefault(m => m.Role == "assistant");
        Check("回复内容非空", firstReply != null && firstReply.Content.Length > 10, firstReply == null ? "null" : ("len=" + firstReply.Content.Length));

        done.Reset();
        chat.Send("第二轮问题");
        bool ok2 = done.Wait(25000);
        Check("多轮上下文", ok2 && chat.Current.Messages.Count >= 4, "count=" + chat.Current.Messages.Count);

        int before = chat.Current.Messages.Count;
        done.Reset();
        chat.Regenerate();
        bool ok3 = done.Wait(25000);
        Check("重新生成替换最后一条", ok3 && chat.Current.Messages.Count == before, "before=" + before + " after=" + chat.Current.Messages.Count);

        chat.Save();
        var chat2 = new ChatService(store, ps2);
        Check("会话历史持久化", chat2.Sessions.Count >= 1 && chat2.Sessions[0].Messages.Count >= 3,
            "sessions=" + chat2.Sessions.Count + " msgs=" + (chat2.Sessions.Count > 0 ? chat2.Sessions[0].Messages.Count : 0));

        Console.WriteLine();
        Console.WriteLine("== 6. 错误时保留输入（ChatService 不丢会话） ==");
        var badProvider = ps2.Providers.First().Clone();
        badProvider.Model = "err-auth";
        ps2.Update(badProvider);
        var s2 = chat.NewSession();
        chat.SetProvider(badProvider.Id, "err-auth");
        var errDone = new ManualResetEventSlim(false);
        chat.Changed += delegate
        {
            if (chat.Busy || chat.Current == null) return;
            var lastE = chat.Current.Messages.Count > 0 ? chat.Current.Messages[chat.Current.Messages.Count - 1] : null;
            if (lastE != null && lastE.Role == "assistant" && !lastE.Streaming) errDone.Set();
        };
        chat.Send("这条会失败");
        errDone.Wait(20000);
        var userMsg = chat.Current.Messages.FirstOrDefault(m => m.Role == "user");
        var errMsg = chat.Current.Messages.LastOrDefault(m => m.Role == "assistant");
        // 诊断：失败时打印完整现场（消息列表、忙碌状态、日志尾部）
        if (errMsg == null || string.IsNullOrEmpty(errMsg.Error))
        {
            Console.WriteLine("  [诊断] busy=" + chat.Busy + " status=" + chat.Status + " msgs=" + chat.Current.Messages.Count);
            foreach (var m in chat.Current.Messages)
                Console.WriteLine("    role=" + m.Role + " len=" + (m.Content ?? "").Length + " err='" + (m.Error ?? "") + "' kind='" + (m.ErrorKind ?? "") + "' streaming=" + m.Streaming);
            var logPath = System.IO.Path.Combine(dataDir, "deskbox.log");
            if (System.IO.File.Exists(logPath))
            {
                var lines = System.IO.File.ReadAllLines(logPath);
                Console.WriteLine("    [log tail]");
                foreach (var line in lines.Skip(Math.Max(0, lines.Length - 8))) Console.WriteLine("      " + line);
            }
        }
        Check("失败保留用户消息", userMsg != null && userMsg.Content == "这条会失败", userMsg == null ? "lost" : "ok");
        Check("失败有错误信息", errMsg != null && !string.IsNullOrEmpty(errMsg.Error), errMsg == null ? "no assistant msg" : (errMsg.ErrorKind + ": " + (errMsg.Error.Length > 40 ? errMsg.Error.Substring(0, 40) : errMsg.Error)));

        Console.WriteLine();
        Console.WriteLine("==== 汇总: PASS " + pass + " / FAIL " + fail + " ====");
        foreach (var f in fails) Console.WriteLine("  ✗ " + f);
        Console.WriteLine("数据目录: " + dataDir);
        return fail == 0 ? 0 : 1;
    }
}
