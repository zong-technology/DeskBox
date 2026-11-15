using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using DeskBox.Core;
using DeskBox.Sync;

public class LocalSyncServerAccept
{
    static int pass, fail;
    static void C(string n,bool ok,string d){if(ok){pass++;Console.WriteLine("PASS  "+n+"  ["+d+"]");}else{fail++;Console.WriteLine("FAIL  "+n+"  ["+d+"]");}}
    public static int Main()
    {
        try{return Run();}catch(Exception ex){Console.WriteLine("UNHANDLED "+ex.GetType().FullName+": "+ex.Message);Console.WriteLine(ex.StackTrace);return 2;}
    }
    static int Run()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deskbox-local-sync-"+DateTime.Now.Ticks);Directory.CreateDirectory(dir);AppPaths.DataDir=dir;
        using(var cert=LocalSyncCertificate.LoadOrCreate(dir))
        {
            C("TLS证书含私钥",cert.HasPrivateKey&&!string.IsNullOrEmpty(cert.Thumbprint),cert.Thumbprint);
            using(var cert2=LocalSyncCertificate.LoadOrCreate(dir)) C("证书重启后指纹稳定",cert2.Thumbprint==cert.Thumbprint,cert2.Thumbprint);
            var certFile=File.ReadAllText(Path.Combine(dir,"local-sync-cert.json"));C("私钥使用DPAPI保护",certFile.Contains("ProtectedPfx")&&!certFile.Contains("BEGIN PRIVATE KEY"),certFile.Length.ToString());
            var sync=new SyncStore();var trust=new DeviceTrustStore();var ticket=trust.CreatePairingTicket(5);
            using(var server=new LocalSyncServer(cert,sync,trust,IPAddress.Loopback,0))
            {
                server.Start();C("服务启动并获得端口",server.Running&&server.Port>0,server.Port.ToString());
                var health=Call(server.Port,"GET","/health","",null,null);C("TLS健康检查",health.Status==200&&health.Body.Contains("DeskBox Local Sync"),health.Status.ToString());
                using (var broken = new TcpClient("127.0.0.1", server.Port))
                using (var stream = new SslStream(broken.GetStream(), false, delegate { return true; }))
                {
                    stream.AuthenticateAsClient("DeskBox", null, SslProtocols.Tls12, false);
                    var malformed = Encoding.ASCII.GetBytes("POST /pair HTTP/1.1\r\nHost: localhost\r\nContent-Length: invalid\r\n\r\n");
                    stream.Write(malformed, 0, malformed.Length); stream.Flush();
                }
                C("坏请求断连后服务仍可用",Call(server.Port,"GET","/health","",null,null).Status==200,"alive");
                var unauth=Call(server.Port,"GET","/sync/pull?since=0","",null,null);C("未授权同步被拒绝",unauth.Status==401,unauth.Status.ToString());
                var bad=Call(server.Port,"POST","/pair",Json.ToJson(new PairRequest{Code="000000",DeviceName="Bad"}),null,null);C("错误配对码被拒绝",bad.Status==401,bad.Status.ToString());
                var paired=Call(server.Port,"POST","/pair",Json.ToJson(new PairRequest{Code=ticket.Code,DeviceName="Arthur iPhone",Platform="ios"}),null,null);var credential=Json.FromJson<PairResponse>(paired.Body);
                C("正确配对返回设备Token",paired.Status==200&&credential!=null&&credential.Ok&&credential.AccessToken.Length>30,paired.Status.ToString());
                var renewed=Call(server.Port,"POST","/device/renew","",credential.DeviceId,credential.AccessToken);
                C("认证设备可续期且原Token有效",renewed.Status==200&&renewed.Body.Contains("ExpiresAt")&&trust.Validate(credential.DeviceId,credential.AccessToken),renewed.Status.ToString());
                C("无认证不能续期",Call(server.Port,"POST","/device/renew","",null,null).Status==401,"unauthorized");
                var empty=Call(server.Port,"GET","/sync/pull?since=0","",credential.DeviceId,credential.AccessToken);C("授权Pull成功",empty.Status==200&&empty.Body=="[]",empty.Body);
                var record=new SyncRecord{EntityType="incident",Id="I100",Version=1,BaseVersion=0,UpdatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),DeviceId=credential.DeviceId,Json="{\"severity\":\"S2\"}"};
                C("缺失事件ID和描述不能入库",Call(server.Port,"POST","/sync/push",Json.ToJson(record),credential.DeviceId,credential.AccessToken).Status==400&&sync.Records.Count==0,"invalid incident");
                record.Json=Json.ToJson(new IncidentDto{Id="I100",Severity="S2",Description="压力联锁异常",Status="closed"});
                C("未填写措施的关闭事件被拒绝",Call(server.Port,"POST","/sync/push",Json.ToJson(record),credential.DeviceId,credential.AccessToken).Status==400&&sync.Records.Count==0,"closure validation");
                record.Json=Json.ToJson(new IncidentDto{Id="I100",Severity="S2",Description="压力联锁异常"});
                var push=Call(server.Port,"POST","/sync/push",Json.ToJson(record),credential.DeviceId,credential.AccessToken);C("授权Push应用记录",push.Status==200&&push.Body.Contains("Applied"),push.Body);
                var duplicate=Call(server.Port,"POST","/sync/push",Json.ToJson(record),credential.DeviceId,credential.AccessToken);C("重复Push幂等",duplicate.Status==200&&duplicate.Body.Contains("Duplicate"),duplicate.Body);
                var pulled=Call(server.Port,"GET","/sync/pull?since=0","",credential.DeviceId,credential.AccessToken);C("Pull返回真实同步记录",pulled.Status==200&&pulled.Body.Contains("I100")&&pulled.Body.Contains("incident"),pulled.Body.Length.ToString());
                var wrong=Call(server.Port,"GET","/sync/pull?since=0","",credential.DeviceId,"wrong");C("错误Token被拒绝",wrong.Status==401,wrong.Status.ToString());
                trust.Revoke(credential.DeviceId); C("撤销设备不能通过续期恢复访问",Call(server.Port,"POST","/device/renew","",credential.DeviceId,credential.AccessToken).Status==401,"revoked");
                server.Stop();C("服务可正常停止",!server.Running,"stopped");
            }
        }
        Console.WriteLine("\n==== Local Sync Server 验收: PASS "+pass+" / FAIL "+fail+" ====");return fail==0?0:1;
    }
    static Response Call(int port,string method,string path,string body,string device,string token)
    {
        using(var client=new TcpClient("127.0.0.1",port))using(var tls=new SslStream(client.GetStream(),false,delegate{return true;}))
        {
            tls.AuthenticateAsClient("DeskBox",null,SslProtocols.Tls12,false);var payload=Encoding.UTF8.GetBytes(body??"");var sb=new StringBuilder();sb.Append(method+" "+path+" HTTP/1.1\r\nHost: localhost\r\nContent-Length: "+payload.Length+"\r\n");if(device!=null)sb.Append("X-Device-Id: "+device+"\r\nAuthorization: Bearer "+token+"\r\n");sb.Append("Connection: close\r\n\r\n");var head=Encoding.ASCII.GetBytes(sb.ToString());tls.Write(head,0,head.Length);if(payload.Length>0)tls.Write(payload,0,payload.Length);tls.Flush();using(var ms=new MemoryStream()){var buffer=new byte[4096];int n;while((n=tls.Read(buffer,0,buffer.Length))>0)ms.Write(buffer,0,n);var text=Encoding.UTF8.GetString(ms.ToArray());var split=text.IndexOf("\r\n\r\n",StringComparison.Ordinal);var first=text.Substring(0,text.IndexOf("\r\n",StringComparison.Ordinal)).Split(' ');return new Response{Status=int.Parse(first[1]),Body=split<0?"":text.Substring(split+4)};}
        }
    }
    class Response{public int Status;public string Body="";}
}
