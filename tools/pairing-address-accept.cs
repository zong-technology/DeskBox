using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Sync;
using DeskBox.UI;
using DeskBox.UI.Views;
public class PairingAddressAccept
{
    [STAThread] public static int Main()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deskbox-address-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);Store.DataDir=dir;AppPaths.DataDir=dir;var store=new Store();store.Init();int pass=0,fail=0;
        Action<string,bool> check=(name,ok)=>{if(ok)pass++;else fail++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);};
        using(var sync=new LocalSyncCoordinator(store))
        {
            var addresses=sync.LocalAddresses();check("排除不可路由的自动地址",addresses.All(x=>!x.StartsWith("169.254.") && x!="127.0.0.1"));
            var wifi=NetworkInterface.GetAllNetworkInterfaces().Where(x=>x.OperationalStatus==OperationalStatus.Up && x.NetworkInterfaceType==NetworkInterfaceType.Wireless80211).SelectMany(x=>x.GetIPProperties().UnicastAddresses).Where(x=>x.Address.AddressFamily==AddressFamily.InterNetwork).Select(x=>x.Address.ToString()).Where(x=>!x.StartsWith("169.254.")).ToArray();
            if(wifi.Length>0)check("实际WLAN优先于VMware或VPN",addresses.Count>0&&wifi.Contains(addresses[0]));
            using(var view=new MobileSyncView(store,sync))
            {
                var picker=(DarkSelect)typeof(MobileSyncView).GetField("_addresses",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);
                check("配对页列出真实地址且默认首选可达网卡",picker.Inner.Items.Cast<string>().SequenceEqual(addresses) && (addresses.Count==0||picker.SelectedText==addresses[0]));
            }
        }
        Console.WriteLine("Pairing addresses: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
