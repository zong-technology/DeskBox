using System;
using System.IO;
using System.Linq;
using DeskBox;
using DeskBox.Core;
using DeskBox.Sync;

public class CloudSyncAccept
{
    static int pass, fail;
    static void Check(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    public static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "deskbox-cloud-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data); Store.DataDir = data; AppPaths.DataDir = data;
        var store = new Store(); store.Init();
        var credential = new CloudCredential { BaseUrl="https://sync.example", DeviceId="desktop-test",Token=new string('a',64),GateToken="test-secret-gate",Role="desktop",ExpiresAt=DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeMilliseconds() };
        Check("有效电脑云凭证",credential.Valid("desktop"));
        credential.Role="ios";Check("拒绝手机凭证作为主电脑",!credential.Valid("desktop"));credential.Role="desktop";
        credential.BaseUrl="http://sync.example";Check("拒绝明文HTTP云地址",!credential.Valid("desktop"));credential.BaseUrl="https://sync.example";
        credential.Token=null;Check("空Token不能通过验证",!credential.Valid("desktop"));credential.Token=new string('a',64);
        using (var coordinator = new LocalSyncCoordinator(store))
        {
            using (var agent = new CloudSyncAgent(coordinator))
            {
                Check("账号登录地址拒绝HTTP",!agent.SetAccountUrl("http://sync.example"));
                Check("自有云HTTPS地址可保存",agent.SetAccountUrl("https://sync.example") && agent.AccountUrl=="https://sync.example");
                Check("云凭证可导入",agent.Import(Json.ToJson(credential)) && agent.LoggedIn);
                var encrypted=File.ReadAllText(Path.Combine(data,"cloud-login.enc"));
                Check("凭证落盘不含Token或平台密钥",!encrypted.Contains(credential.Token) && !encrypted.Contains(credential.GateToken));
                Check("非法导入保留已有登录",!agent.Import("invalid") && agent.LoggedIn);
            }
            using (var resumed = new CloudSyncAgent(coordinator))
            {
                Check("重启可恢复DPAPI登录",resumed.LoggedIn);
                Check("重启恢复实际云服务地址",resumed.AccountUrl=="https://sync.example");
                var record=new SyncRecord{EntityType="task",Id="C1",Version=1,BaseVersion=0,UpdatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),Json=Json.ToJson(new MobileTaskDto{Id="C1",Title="云端工程任务",Priority="high"})};
                Check("手机云提交可回写实际任务",coordinator.MergeFromCloud(record)==SyncApplyResult.Applied && store.Tasks.Any(t=>t.Id=="C1" && t.Title=="云端工程任务"));
                Check("重发同一云提交幂等",coordinator.MergeFromCloud(record)==SyncApplyResult.Duplicate);
                record.Id="C2";Check("拒绝记录与DTO目标不一致",coordinator.MergeFromCloud(record)==SyncApplyResult.Invalid && !store.Tasks.Any(t=>t.Id=="C2"));
                record.EntityType="project";Check("手机不可通过云接口改写项目",coordinator.MergeFromCloud(record)==SyncApplyResult.Invalid);
                resumed.Logout();Check("退出不删除本地任务",!resumed.LoggedIn && store.Tasks.Any(t=>t.Id=="C1") && !File.Exists(Path.Combine(data,"cloud-login.enc")));
            }
        }
        Console.WriteLine("Cloud sync: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
