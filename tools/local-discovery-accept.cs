using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DeskBox.Core;
using DeskBox.Sync;

public class LocalDiscoveryAccept
{
 static int pass,fail;static void C(string n,bool ok,string d){if(ok){pass++;Console.WriteLine("PASS  "+n+"  ["+d+"]");}else{fail++;Console.WriteLine("FAIL  "+n+"  ["+d+"]");}}
 public static int Main(){using(var service=new LocalSyncDiscovery("Engineering-PC",47831,"ABC123",IPAddress.Loopback,0)){service.Start();C("发现服务启动",service.Running&&service.Port>0,service.Port.ToString());using(var client=new UdpClient()){client.Client.ReceiveTimeout=3000;var probe=Encoding.ASCII.GetBytes(LocalSyncDiscovery.Probe);client.Send(probe,probe.Length,new IPEndPoint(IPAddress.Loopback,service.Port));IPEndPoint remote=null;var bytes=client.Receive(ref remote);var response=Json.FromJson<DiscoveryResponse>(Encoding.UTF8.GetString(bytes));C("发现返回设备名",response!=null&&response.DeviceName=="Engineering-PC",response==null?"none":response.DeviceName);C("发现返回TLS端口",response.TlsPort==47831,response.TlsPort.ToString());C("发现返回证书指纹",response.Thumbprint=="ABC123",response.Thumbprint);C("发现协议版本",response.ProtocolVersion==1,response.ProtocolVersion.ToString());}service.Stop();C("发现服务可停止",!service.Running,"stopped");}Console.WriteLine("\n==== Local Discovery 验收: PASS "+pass+" / FAIL "+fail+" ====");return fail==0?0:1;}
}
