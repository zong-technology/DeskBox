using System;
using System.IO;
using System.Linq;
using DeskBox.Core;
using DeskBox.Sync;

public class DeviceTrustAccept
{
    static int pass, fail;
    static void C(string n, bool ok, string d) { if(ok){pass++;Console.WriteLine("PASS  "+n+"  ["+d+"]");}else{fail++;Console.WriteLine("FAIL  "+n+"  ["+d+"]");} }
    public static int Main()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deskbox-device-"+DateTime.Now.Ticks);Directory.CreateDirectory(dir);AppPaths.DataDir=dir;
        var store=new DeviceTrustStore(); var ticket=store.CreatePairingTicket(5);
        C("配对码为6位数字",ticket.Code.Length==6&&ticket.Code.All(char.IsDigit),ticket.Code);
        C("错误配对码拒绝",store.CompletePairing("000000","iPhone","ios",30)==null,"rejected");
        var credential=store.CompletePairing(ticket.Code,"Arthur iPhone","ios",30);
        C("一次性配对成功",credential!=null&&credential.AccessToken.Length>=40,credential==null?"none":credential.DeviceId);
        C("配对码不可重复使用",store.CompletePairing(ticket.Code,"Other","ios",30)==null,"one-time");
        C("正确Token通过",store.Validate(credential.DeviceId,credential.AccessToken),"valid");
        var expiry = store.ExtendTrust(credential.DeviceId, credential.AccessToken);
        C("活跃设备续期无需再次配对",expiry >= credential.ExpiresAt && store.Validate(credential.DeviceId,credential.AccessToken),"same token");
        C("错误Token不能续期",store.ExtendTrust(credential.DeviceId,"wrong")==0,"rejected");
        C("错误Token拒绝",!store.Validate(credential.DeviceId,"bad"),"rejected");
        var file=File.ReadAllText(Path.Combine(dir,"trusted-devices.json"));
        C("磁盘不保存原始Token",!file.Contains(credential.AccessToken)&&file.Contains("TokenHash"),"hashed");
        var reload=new DeviceTrustStore(); C("设备信任持久化",reload.Devices.Count==1&&reload.Validate(credential.DeviceId,credential.AccessToken),reload.Devices.Count.ToString());
        var refreshed=reload.Refresh(credential.DeviceId,credential.AccessToken,60);
        C("刷新Token并使旧Token失效",refreshed!=null&&!reload.Validate(credential.DeviceId,credential.AccessToken)&&reload.Validate(credential.DeviceId,refreshed.AccessToken),"rotated");
        reload.Revoke(credential.DeviceId); C("Device Revoke立即失效",!reload.Validate(credential.DeviceId,refreshed.AccessToken)&&reload.Devices[0].Revoked,"revoked");
        C("撤销设备不能续期",reload.ExtendTrust(credential.DeviceId,refreshed.AccessToken)==0,"rejected");
        reload.Remove(credential.DeviceId); C("可移除设备",reload.Devices.Count==0,"0");
        C("空凭据拒绝",!reload.Validate("","")&&!reload.Validate(null,null),"rejected");
        Console.WriteLine("\n==== Device Trust 验收: PASS "+pass+" / FAIL "+fail+" ===="); return fail==0?0:1;
    }
}
