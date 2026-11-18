using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Ai
{
    // Shared by text AI, legacy AI, realtime voice, workflow AI and paired-device AI.
    public static class AiEnvironment
    {
        private static string _mode;
        private static CancellationTokenSource _lifetime = new CancellationTokenSource();
        private static readonly object Gate = new object();
        public static event Action Changed;
        public static string Mode { get { lock(Gate) { if(_mode==null) { var path=Path.Combine(AppPaths.DataDir,"ai-environment.json");var value=Json.Load<string>(path,File.Exists(path)?"local":"network");_mode=value=="network"?"network":"local"; } return _mode; } } }
        public static bool Local { get { return Mode=="local"; } }
        public static string Suffix { get { return Local?"-local":""; } }
        public static CancellationToken Token { get { lock(Gate)return _lifetime.Token; } }
        public static void Select(string mode)
        {
            if(mode!="local"&&mode!="network")throw new ArgumentException("AI 环境无效。");
            CancellationTokenSource previous;
            lock(Gate) {
                if(Mode==mode)return;
                var path=Path.Combine(AppPaths.DataDir,"ai-environment.json");Directory.CreateDirectory(AppPaths.DataDir);
                var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {File.WriteAllText(temporary,Json.ToJson(mode),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
                finally {if(File.Exists(temporary))File.Delete(temporary);}
                previous=_lifetime;_lifetime=new CancellationTokenSource();_mode=mode;
            }
            try {previous.Cancel();} catch(AggregateException) { } // Every registered request is notified even if a disposed transport throws.
            var changed=Changed;if(changed!=null)changed();
        }
        public static bool Loopback(Uri uri)
        {
            if(uri==null)return false;
            IPAddress address;
            // Numeric loopback only; DNS aliases and LAN addresses cannot leave the machine in local mode.
            return IPAddress.TryParse(uri.Host.Trim('[',']'),out address) && IPAddress.IsLoopback(address);
        }
        public static void RequireEndpoint(string endpoint)
        {
            Uri uri;
            if(!Uri.TryCreate(endpoint,UriKind.Absolute,out uri)||!string.IsNullOrEmpty(uri.UserInfo))throw new ArgumentException("AI 地址无效，不能包含登录凭证。");
            if(Local&&!Loopback(uri))throw new InvalidOperationException("本地隔离模式仅允许 127.0.0.1 或 ::1 的本机模型，已阻止外部请求。");
        }
        public static void RequireNetwork()
        {
            if(Local)throw new InvalidOperationException("本地隔离模式禁止联网及设备同步。");
        }
        public static void RequireLocalPath(string path)
        {
            if(!Local || string.IsNullOrWhiteSpace(path))return;
            var full=Path.GetFullPath(path);
            if(full.StartsWith(@"\\",StringComparison.Ordinal)||new DriveInfo(Path.GetPathRoot(full)).DriveType==DriveType.Network)throw new InvalidOperationException("本地隔离模式不能访问网络共享或映射的网络磁盘。");
            var root=Path.GetPathRoot(full);var current=root;
            // Walk from the drive root so a remote junction is rejected before probing anything beneath it.
            foreach(var part in full.Substring(root.Length).Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries)) {
                current=Path.Combine(current,part);
                try {if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("本地隔离模式不能访问符号链接或重解析路径。");}
                catch(FileNotFoundException){break;}catch(DirectoryNotFoundException){break;}
            }
        }
        public static bool ToolAllowed(string name) { return !Local || (name!="workflow.run" && name!="web.read"); }
    }
}
