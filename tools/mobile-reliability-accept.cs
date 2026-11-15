using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Sync;

public class MobileReliabilityAccept
{
    static int pass, fail;
    static void Check(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-mobile-reliability-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir); AppPaths.DataDir = dir; Store.DataDir = dir;
        var store = new Store(); store.Init();
        var workspaces = new WorkspaceStore();
        using (var sync = new LocalSyncCoordinator(store, workspaces))
        {
            // The project is created AFTER the coordinator, as in an actual desktop session.
            var project = workspaces.Add("New live project", dir, "PLC");
            workspaces.SetStageProgress(project.Id, "PLC", 80);
            sync.PublishWindowsSnapshot();
            var record = sync.Sync.Records.FirstOrDefault(x => x.Id == project.Id);
            Check("启动后新增项目可同步", record != null);
            Check("同步使用实时项目进度", record != null && Json.FromJson<MobileProjectDto>(record.Json).Progress > 0);
            var apply = typeof(LocalSyncCoordinator).GetMethod("ApplyRemote", BindingFlags.NonPublic | BindingFlags.Instance);
            apply.Invoke(sync, new object[] { new SyncRecord { Id = project.Id, EntityType = "project", Json = Json.ToJson(new MobileProjectDto { Id = project.Id, Name = "Updated on phone", Risk = "high" }) }, SyncApplyResult.Applied });
            Check("手机项目修改立即反映在共享桌面数据", workspaces.Current.Name == "Updated on phone");
            var ticket = sync.CreatePairing(5);
            Check("新配对码有效", sync.Devices.IsPairingValid(ticket));
            sync.Devices.CancelPairing(ticket);
            Check("取消后旧码不可配对", !sync.Devices.IsPairingValid(ticket) && sync.Devices.CompletePairing(ticket.Code, "Phone", "ios", 30) == null);
            ticket = sync.CreatePairing(5);
            var credential = sync.Devices.CompletePairing(ticket.Code, "Phone", "ios", 30);
            Check("成功配对后码立即失效", credential != null && !sync.Devices.IsPairingValid(ticket));
            ticket = sync.CreatePairing(5); sync.Stop();
            Check("停止服务废止待配对码", !sync.Devices.IsPairingValid(ticket));
            Check("停止服务不撤销已配对设备", sync.Devices.Validate(credential.DeviceId, credential.AccessToken));
        }
        Console.WriteLine("Mobile reliability: PASS " + pass + " / FAIL " + fail);
        return fail == 0 ? 0 : 1;
    }
}
