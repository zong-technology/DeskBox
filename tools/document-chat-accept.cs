using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.Core;
using DeskBox.Ai;

public class DocumentChatAccept
{
    public static int Main()
    {
        var directory = Path.Combine(Path.GetTempPath(), "deskbox-document-chat-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        Store.DataDir = directory; AppPaths.DataDir = directory; var store = new Store(); store.Init();
        var provider = new AiProvider { Name = "Document Mock", BaseUrl = "http://127.0.0.1:18080", Model = "mock-document", Enabled = true, ApiKey = "test", TimeoutMs = 60000 };
        var providers = new ProviderStore(); providers.Add(provider); var chat = new ChatService(store, providers); int pass = 0, fail = 0;
        foreach (var extension in new[] { ".pdf", ".xlsx", ".docx" })
        {
            string path = Path.Combine(directory, "工程记录" + extension);
            var created = DocumentSkills.Run(new Dictionary<string, string> { { "op", "create" }, { "path", path }, { "content", "DCS温控联锁\n接受标准为42" } });
            if (!created.Ok) { fail++; Console.WriteLine("FAIL 创建测试文档 " + created.Error); continue; }
            chat.NewSession(); chat.SetProvider(provider.Id, provider.Model); chat.AllowedFiles = new List<string> { path }; chat.Send("DOC_PATH:" + path);
            var until = DateTime.UtcNow.AddSeconds(15); while (chat.Busy && DateTime.UtcNow < until) Thread.Sleep(20);
            bool ok = !chat.Busy && chat.Current.Messages.Any(m => m.Role == "tool" && m.ToolName == "doc.read" && m.Content.Contains("接受标准为42")) && chat.Current.Messages.Last().Role == "assistant" && chat.Current.Messages.Last().Content.Contains("接受标准为42");
            if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + "文档进入AI工具闭环 " + extension);
        }
        string scanned = Path.Combine(directory, "scanned.pdf");
        using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("python.exe", "\"" + Path.Combine(Environment.CurrentDirectory, "tools", "generate-ocr-fixture.py") + "\" \"" + scanned + "\"") { UseShellExecute = false, CreateNoWindow = true })) { process.WaitForExit(); if (process.ExitCode != 0) return 2; }
        chat.NewSession(); chat.SetProvider(provider.Id, "mock-document-ocr"); chat.AllowedFiles = new List<string> { scanned }; chat.Send("DOC_PATH:" + scanned);
        var end = DateTime.UtcNow.AddSeconds(28); while (chat.Busy && DateTime.UtcNow < end) Thread.Sleep(20);
        bool recovered = !chat.Busy && chat.Current.Messages.Any(x => x.Role == "tool" && x.ToolName == "doc.ocr" && x.Content.Contains("42")) && chat.Current.Messages.Last().Role == "assistant" && chat.Current.Messages.Last().Content.Contains("42");
        if (recovered) pass++; else fail++; Console.WriteLine((recovered ? "PASS " : "FAIL ") + "扫描PDF读取失败后转OCR并继续回答");
        Console.WriteLine("Document chat: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
