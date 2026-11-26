using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.UI;

public class PermissionLiveAccept
{
    static int pass, fail;
    static void C(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    public static int Main()
    {
        var temp = Path.Combine(Path.GetTempPath(), "deskbox-permission-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); Store.DataDir = temp; AppPaths.DataDir = temp;
        var root = Path.Combine(temp, "allowed"); Directory.CreateDirectory(root); var path = Path.Combine(root, "test.txt"); File.WriteAllText(path, "实时权限内容");
        var store = new Store(); store.Init(); var chat = new ChatService(store, new ProviderStore()); bool enabled = true;
        chat.PermissionRoots = TrustedRoots.Load; chat.FileAccessEnabled = () => enabled; chat.RefreshPermissions(); var session = chat.NewSession();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var execute = typeof(ChatService).GetMethod("ExecuteToolCalls", flags);
        Func<bool> read = () => { execute.Invoke(chat, new object[] { session, ToolCallParser.Parse("```tool\n" + Json.ToJson(new { name = "fs.read", args = new { path = path } }) + "\n```"), CancellationToken.None, chat.Project, new Action<string>(_ => {}) }); return session.Messages.Last(x => x.Role == "tool").Content.StartsWith("✅"); };
        C("初始未授权拒绝", !read());
        TrustedRoots.Add(root); C("新增授权无需重建聊天即可读取", read());
        var build = typeof(ChatService).GetMethod("BuildContext", flags);
        var context = (List<AiMessage>)build.Invoke(chat, new object[] { session, chat.Project }); C("模型上下文看到新授权", context[0].Content.Contains(root));
        chat.AllowedFiles.Add(path); TrustedRoots.Remove(root); C("移除授权立即拒绝旧工具上下文", !read()); C("撤销目录同时清除内部临时文件授权", !chat.AllowedFiles.Contains(path));
        TrustedRoots.Add(root); enabled = false; C("关闭文件总开关拒绝访问", !read());
        context = (List<AiMessage>)build.Invoke(chat, new object[] { session, chat.Project }); C("模型识别文件访问关闭", context[0].Content.Contains("所有文件工具都会拒绝"));
        enabled = true; C("重新启用实时生效", read());
        string drive = Path.GetPathRoot(temp); TrustedRoots.Save(new List<string> { drive }); C("盘符授权保留根反斜杠", TrustedRoots.Load().Single() == drive);
        var permissionFile = Path.Combine(temp, "trusted-roots.json"); File.SetAttributes(permissionFile, FileAttributes.ReadOnly);
        bool failed = false; try { TrustedRoots.Save(new List<string> { root }); } catch (IOException) { failed = true; } finally { File.SetAttributes(permissionFile, FileAttributes.Normal); }
        C("保存失败明确抛出且保留旧权限", failed && TrustedRoots.Load().Single() == drive);
        TrustedRoots.Save(new List<string> { root }); chat.RefreshPermissions();
        using (var active = new CancellationTokenSource())
        {
            typeof(ChatService).GetField("_cts", flags).SetValue(chat, active); typeof(ChatService).GetProperty("Busy").SetValue(chat, true, null);
            TrustedRoots.Remove(root); chat.RefreshPermissions(); C("撤销授权取消当前请求", active.IsCancellationRequested);
            typeof(ChatService).GetProperty("Busy").SetValue(chat, false, null); typeof(ChatService).GetField("_cts", flags).SetValue(chat, null);
        }
        var outside = new ToolContext { AllowedRoots = new List<string> { root } }; var result = ToolRegistry.Instance.Invoke("doc.read", new Dictionary<string,string>{{"path", Path.Combine(temp, "allowed-other", "x.txt")}}, outside); C("相似目录前缀不越权", !result.Ok);
        Console.WriteLine("Live permissions: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
