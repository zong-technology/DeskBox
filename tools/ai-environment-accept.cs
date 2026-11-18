using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;

internal static class AiEnvironmentAccept
{
    private static int checks;
    private static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
    [STAThread] public static int Main()
    {
        var home=Path.Combine(Path.GetTempPath(),"deskbox-environment-"+Guid.NewGuid().ToString("N"));Environment.SetEnvironmentVariable("DESKBOX_HOME",home);Directory.CreateDirectory(home);
        try {
            AiEnvironment.Select("network");var providers=new ProviderStore();providers.Add(new AiProvider{Name="Network fixture",BaseUrl="https://fixture.invalid/v1",Model="fixture",ApiKey="fixture-network-key"});
            var store=new Store();store.Tasks.Clear();var chat=new ChatService(store,providers);chat.Current.Messages.Add(new ChatMessage{Role="user",Content="network-only-context"});chat.Save();var token=AiEnvironment.Token;
            AiEnvironment.Select("local");providers.ChangeEnvironment();chat.ChangeEnvironment();
            Check(token.IsCancellationRequested&&!AiEnvironment.Token.IsCancellationRequested,"Switching cancels old requests and creates a fresh lifetime");
            Check(providers.Providers.Count==0&&chat.Current.Messages.Count==0,"Local mode never imports network credentials or conversation context");
            providers.Add(new AiProvider{Name="Local fixture",BaseUrl="http://127.0.0.1:1/v1",Model="local"});chat.Current.Messages.Add(new ChatMessage{Role="user",Content="local-only-context"});chat.Save();
            foreach(var url in new[]{"https://fixture.invalid/v1","http://localhost:11434/v1","http://10.0.0.1/v1","https://127.0.0.1.example.com/v1"}) {
                bool denied=false;try{AiEnvironment.RequireEndpoint(url);}catch(InvalidOperationException){denied=true;}Check(denied,"Local isolation rejects "+url);
            }
            AiEnvironment.RequireEndpoint("http://127.0.0.1:11434/v1");AiEnvironment.RequireEndpoint("http://[::1]:11434/v1");Check(true,"Only numeric IPv4/IPv6 loopback model endpoints are admitted locally");
            var result=new AiClient(new AiProvider{BaseUrl="https://fixture.invalid/v1",Model="fixture"}).Chat(new AiChatRequest{Messages=new List<AiMessage>{new AiMessage{Content="must-not-leave"}}},CancellationToken.None);
            Check(!result.Ok&&result.ErrorKind=="environment","The actual text client rejects external requests before network I/O");
            Check(!ToolRegistry.Instance.Invoke("web.read",new Dictionary<string,string>{{"url","https://fixture.invalid"}},new ToolContext()).Ok,"Direct tool calls cannot bypass the environment");
            Check(!ToolRegistry.Instance.Invoke("workflow.run",new Dictionary<string,string>(),new ToolContext()).Ok,"Local AI cannot start an external workflow");
            Check(!ToolRegistry.Instance.Tools.Any(t=>t.Name=="web.read"||t.Name=="workflow.run"),"Local tool discovery omits network and external execution capabilities");
            var legacy=new LocalAiProvider().Ask(new AiRequest{Kind="summarize",Paths=new List<string>{@"\\fixture.invalid\private\must-not-open.txt"}});
            Check(!legacy.Ok&&legacy.Error.Contains("本地隔离"),"The legacy summary entry point rejects UNC paths before probing the file");
            var document=DocumentSkills.Run(new Dictionary<string,string>{{"op","read"},{"path",@"\\fixture.invalid\private\must-not-open.pdf"}});
            Check(!document.Ok&&document.Error.Contains("本地隔离"),"Direct document parsing cannot bypass the local network path boundary");
            var relay=new MobileAiRunner(providers,()=>new ToolContext{Store=store});var response=relay.Run("offline-phone",new List<AiMessage>{new AiMessage{Content=MobileAiRunner.OperationPrefix+"{\"tool\":\"task.create\",\"arguments\":\"{\\\"title\\\":\\\"blocked\\\"}\"}"}},CancellationToken.None,p=>{}).GetAwaiter().GetResult();
            Check(response.Status=="error"&&store.Tasks.Count==0,"A paired phone cannot write through the isolated desktop relay");
            var recipient=new TcpListener(IPAddress.Loopback,0);recipient.Start();var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
            try {
                int port=((IPEndPoint)listener.LocalEndpoint).Port, target=((IPEndPoint)recipient.LocalEndpoint).Port;
                var serve=Task.Run(delegate {using(var client=listener.AcceptTcpClient()){var stream=client.GetStream();stream.ReadTimeout=5000;var bytes=new byte[8192];stream.Read(bytes,0,bytes.Length);var reply=Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:"+target+"/steal\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");stream.Write(reply,0,reply.Length);}});
                result=new AiClient(new AiProvider{BaseUrl="http://127.0.0.1:"+port+"/v1",Model="fixture",ApiKey="fixture",TimeoutMs=5000,MaxRetries=0}).Chat(new AiChatRequest{Messages=new List<AiMessage>{new AiMessage{Content="private-local-prompt"}}},CancellationToken.None);
                Check(serve.Wait(7000)&&!result.Ok&&!recipient.Pending(),"A real loopback model redirect cannot forward the prompt or key");
            }finally{listener.Stop();recipient.Stop();}
            AiEnvironment.Select("network");providers.ChangeEnvironment();chat.ChangeEnvironment();
            Check(providers.Providers.Count==1&&providers.DefaultProvider.Name=="Network fixture"&&chat.Current.Messages.Any(m=>m.Content=="network-only-context")&&!chat.Current.Messages.Any(m=>m.Content=="local-only-context"),"Returning online restores only the original network configuration and history");
            Check(File.Exists(Path.Combine(AppPaths.DataDir,"chats-local.json"))&&File.Exists(Path.Combine(AppPaths.DataDir,"ai-providers-local.json")),"Local history and encrypted provider configuration remain separate on disk");
            using(var host=new Form{Width=1180,Height=860})
            using(var settings=new DeskBox.UI.Views.SettingsCenterView(store,providers)) {
                settings.Dock=DockStyle.Fill;host.Controls.Add(settings);host.Show();Application.DoEvents();
                typeof(DeskBox.UI.Views.SettingsCenterView).GetMethod("Switch",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(settings,new object[]{1});
                settings.AIEnvironmentChanged+=delegate{chat.ChangeEnvironment();};
                var picker=settings.Controls.Find("ai-environment",true).OfType<ComboBox>().Single();picker.SelectedIndex=0;Application.DoEvents();
                var field=(Control)typeof(DeskBox.UI.Views.SettingsCenterView).GetField("_fBase",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(settings);
                Check(field.Text=="http://127.0.0.1:1/v1","Settings restores the existing local profile instead of leaving network credentials in its editor");
                picker.SelectedIndex=1;Application.DoEvents();Check(field.Text=="https://fixture.invalid/v1","Settings restores the network profile when returning online");
                picker.SelectedIndex=0;providers.Remove(providers.DefaultProvider.Id);picker.SelectedIndex=1;picker.SelectedIndex=0;Application.DoEvents();
                var headers=(Control)typeof(DeskBox.UI.Views.SettingsCenterView).GetField("_fHeaders",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(settings);
                Check(field.Text=="http://127.0.0.1:11434/v1"&&headers.Text=="","Entering an empty local profile clears the previous network editor and offers a loopback model");
                picker.SelectedIndex=1;Application.DoEvents();
                host.Close();
            }
            Console.WriteLine("PASS "+checks+" AI environment checks; synthetic data and loopback HTTP only");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        finally{try{AiEnvironment.Select("network");Directory.Delete(home,true);}catch{}}
    }
}
