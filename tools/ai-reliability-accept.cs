using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.UI;
using DeskBox.UI.Views;

public class AiReliabilityAccept
{
    static int pass, fail;
    static void C(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    static void PumpUntil(Func<bool> done, int seconds) { var until = DateTime.UtcNow.AddSeconds(seconds); while (!done() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); } Application.DoEvents(); }
    [STAThread] public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-ai-reliability-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        Store.DataDir = dir; AppPaths.DataDir = dir; var store = new Store(); store.Init();
        var providers = new ProviderStore(); var provider = new AiProvider { Name = "Reliability Mock", BaseUrl = "http://127.0.0.1:18080", Model = "mock-long-stream", ApiKey = "test", Enabled = true, TimeoutMs = 60000, MaxRetries = 0 }; providers.Add(provider);
        var chat = new ChatService(store, providers); chat.NewSession(); chat.SetProvider(provider.Id, "mock-long-stream");
        using (var form = new Form { Width = 1200, Height = 900 })
        using (var view = new ChatView(store))
        {
            view.SetServices(chat, providers); form.Controls.Add(view); form.Show(); Application.DoEvents();
            var started = DateTime.UtcNow; chat.Send("请持续输出超过30秒");
            PumpUntil(() => chat.Current.Messages.Any(x => x.Role == "assistant" && x.Content.Length > 0), 8);
            C("首批内容在结束前可见", chat.Busy && chat.Current.Messages.Last().Content.Length > 0);
            var execution = (Label)typeof(ChatView).GetField("_execution", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
            PumpUntil(() => execution.Text.Contains("已收到"), 2);
            C("真实流式状态与字符数显示", execution.Text.Contains("已收到") && execution.Text.Contains("流式"));
            var shots = Path.Combine(Environment.CurrentDirectory, "shots", "ai-reliability-review"); Directory.CreateDirectory(shots);
            using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(shots, "streaming-state.png")); }
            PumpUntil(() => !chat.Busy || (DateTime.UtcNow - started).TotalSeconds > 31, 34);
            C("持续输出超过30秒不会自动取消", chat.Busy && chat.Current.Messages.Last().Content.Length > 0);
            PumpUntil(() => !chat.Busy, 18);
            C("长回复正常完成且保留尾部", !chat.Busy && chat.Current.Messages.Last().Content.Contains("分片39") && string.IsNullOrEmpty(chat.Current.Messages.Last().Error));
            for (int round = 0; round < 5; round++) { chat.SetProvider(provider.Id, "mock-fast"); chat.Send("连续问答 " + round); PumpUntil(() => !chat.Busy, 8); }
            C("连续多轮均完成", !chat.Busy && chat.Current.Messages.Count(x => x.Role == "assistant" && string.IsNullOrEmpty(x.Error)) == 6);
            chat.SetProvider(provider.Id, "mock-long-stream"); chat.Send("取消测试");
            PumpUntil(() => chat.Current.Messages.Last().Content.Length > 0, 5); chat.Cancel(); PumpUntil(() => !chat.Busy, 5);
            C("主动取消快速结束并保留已收到内容", !chat.Busy && chat.Current.Messages.Last().ErrorKind == "cancelled" && chat.Current.Messages.Last().Content.Length > 0);
        }
        var markdown = typeof(ChatView).GetNestedType("MarkdownLite", BindingFlags.NonPublic);
        var parse = markdown.GetMethod("Parse"); var measure = markdown.GetMethod("MeasureBlock"); var draw = markdown.GetMethod("DrawBlock");
        using (var bitmap = new Bitmap(500, 2500)) using (var graphics = Graphics.FromImage(bitmap)) using (var font = new Font("Microsoft YaHei UI", 9.5F))
        {
            var content = "# 中文标题\n- " + new string('测', 100) + "\n> 引用段落\n```scl\n" + new string('X', 300) + "\n```\n尾部文字";
            var blocks = (IEnumerable)parse.Invoke(null, new object[] { content }); bool equal = true; int y = 0;
            foreach (var block in blocks) { int a = (int)measure.Invoke(null, new object[] { graphics, block, font, 320 }); int b = (int)draw.Invoke(null, new object[] { graphics, block, font, 10, y, 320, Palette.For("cyber", "#22D3EE") }); equal &= a == b; y += b; }
            C("Markdown代码/列表/引用测量与绘制高度一致", equal);
        }
        Console.WriteLine("AI reliability: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
