using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;

// A real loopback SSE transport exercises project switches without external model credentials.
internal static class ActiveProjectStream
{
    private static void Expect(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        Console.WriteLine("PASS " + text);
    }
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "deskbox-live-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Store.DataDir = AppPaths.DataDir = root;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using (var first = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var server = Task.Run(delegate
                {
                    using (var client = listener.AcceptTcpClient())
                    using (var stream = client.GetStream())
                    {
                        var bytes = new byte[8192];
                        stream.ReadTimeout = 3000;
                        stream.Read(bytes, 0, bytes.Length);
                        var header = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"A partial reply\"}}]}\n\n");
                        stream.Write(header, 0, header.Length);
                        stream.Flush();
                        first.Set();
                        release.Wait(8000);
                        try
                        {
                            var late = Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"late prior reply\"}}]}\n\ndata: [DONE]\n\n");
                            stream.Write(late, 0, late.Length);
                        }
                        catch (IOException) { }
                    }
                });
                try
                {
                    var store = new Store();
                    store.Init();
                    var projects = new WorkspaceStore();
                    var workA = Path.Combine(root, "first");
                    var workB = Path.Combine(root, "second");
                    Directory.CreateDirectory(workA);
                    Directory.CreateDirectory(workB);
                    var a = projects.Add("A", workA, "PLC");
                    var b = projects.Add("B", workB, "DCS");
                    var providers = new ProviderStore();
                    providers.Add(new AiProvider { Name = "Loopback fixture", BaseUrl = "http://127.0.0.1:" + port + "/v1", Model = "fixture", TimeoutMs = 5000, MaxRetries = 0 });
                    var chat = new ChatService(store, providers);
                    projects.SetCurrent(a.Id);
                    chat.SetProjectContext(projects.Context);
                    chat.Send("A-only engineering request");
                    Expect(first.Wait(5000), "real loopback model request delivered");
                    Expect(SpinWait.SpinUntil(() => chat.Current.Messages.Any(message => message.Content == "A partial reply"), 3000), "stream delta reached A while running");
                    projects.SetCurrent(b.Id);
                    chat.SetProjectContext(projects.Context);
                    release.Set();
                    Expect(SpinWait.SpinUntil(() => !chat.Busy, 5000), "project switch stops the previous stream");
                    Expect(chat.Current.ProjectId == b.Id && chat.Current.Messages.Count == 0, "late A stream cannot enter B");
                    projects.SetCurrent(a.Id);
                    chat.SetProjectContext(projects.Context);
                    Expect(chat.Current.Messages.Any(message => message.ErrorKind == "cancelled"), "cancelled A result survives its project switch");
                }
                finally { release.Set(); listener.Stop(); server.Wait(5000); }
            }
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }
}
