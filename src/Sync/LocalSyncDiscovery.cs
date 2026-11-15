using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Sync
{
    public class DiscoveryResponse { public string Service = "DeskBox"; public string DeviceName = ""; public int TlsPort; public string Thumbprint = ""; public int ProtocolVersion = 1; }

    public sealed class LocalSyncDiscovery : IDisposable
    {
        public const string Probe = "DESKBOX_DISCOVER_V1";
        private readonly string _deviceName; private readonly int _tlsPort; private readonly string _thumbprint; private readonly IPEndPoint _endpoint;
        private UdpClient _udp; private Thread _thread; private volatile bool _running;
        public LocalSyncDiscovery(string deviceName,int tlsPort,string thumbprint,IPAddress address,int port){_deviceName=deviceName??Environment.MachineName;_tlsPort=tlsPort;_thumbprint=thumbprint??"";_endpoint=new IPEndPoint(address??IPAddress.Any,port);}
        public int Port{get;private set;} public bool Running{get{return _running;}}
        public void Start(){if(_running)return;_udp=new UdpClient(_endpoint);Port=((IPEndPoint)_udp.Client.LocalEndPoint).Port;_running=true;_thread=new Thread(Loop){IsBackground=true,Name="DeskBox-Discovery"};_thread.Start();}
        private void Loop(){while(_running){try{IPEndPoint remote=null;var bytes=_udp.Receive(ref remote);if(Encoding.ASCII.GetString(bytes)==Probe){var response=new DiscoveryResponse{DeviceName=_deviceName,TlsPort=_tlsPort,Thumbprint=_thumbprint};var body=Encoding.UTF8.GetBytes(Json.ToJson(response));_udp.Send(body,body.Length,remote);}}catch{if(!_running)break;}}}
        public void Stop(){_running=false;try{if(_udp!=null)_udp.Close();}catch{}try{if(_thread!=null)_thread.Join(1000);}catch{}}
        public void Dispose(){Stop();}
    }
}
