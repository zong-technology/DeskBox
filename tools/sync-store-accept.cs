using System;
using System.IO;
using System.Linq;
using DeskBox.Core;
using DeskBox.Sync;

public class SyncStoreAccept
{
    static int pass, fail;
    static void C(string n, bool ok, string d) { if (ok) { pass++; Console.WriteLine("PASS  " + n + "  [" + d + "]"); } else { fail++; Console.WriteLine("FAIL  " + n + "  [" + d + "]"); } }

    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-sync-" + DateTime.Now.Ticks); Directory.CreateDirectory(dir); AppPaths.DataDir = dir;
        var store = new SyncStore();
        var v1 = store.UpsertLocal("task", "T1", "{\"title\":\"A\"}", "windows");
        C("首次本地写入 version=1", v1.Version == 1 && v1.BaseVersion == 0, v1.Version.ToString());
        C("首次写入进入队列", store.Queue.Count == 1, store.Queue.Count.ToString());
        var v2 = store.UpsertLocal("task", "T1", "{\"title\":\"B\"}", "windows");
        C("再次编辑递增版本", v2.Version == 2 && v2.BaseVersion == 1, v2.Version.ToString());
        C("同实体队列合并为最新一条", store.Queue.Count == 1 && store.Queue[0].Record.Version == 2, store.Queue.Count.ToString());

        var reload = new SyncStore();
        C("记录和队列持久化", reload.Records.Count == 1 && reload.Queue.Count == 1, reload.Records.Count + "/" + reload.Queue.Count);
        var qid = reload.Queue[0].Id; reload.MarkAttempt(qid, "offline");
        C("失败尝试可追踪", reload.Queue[0].Attempts == 1 && reload.Queue[0].LastError == "offline", reload.Queue[0].Attempts.ToString());
        reload.Acknowledge(qid); C("确认后移除队列", reload.Queue.Count == 0, "0");

        var older = new SyncRecord { EntityType="task", Id="T1", Version=1, BaseVersion=0, Json="old", DeviceId="iphone" };
        C("无本地待同步时忽略旧版本", reload.MergeRemote(older) == SyncApplyResult.IgnoredOlder, "ignored");
        var newer = new SyncRecord { EntityType="task", Id="T1", Version=3, BaseVersion=2, Json="{\"title\":\"C\"}", DeviceId="iphone" };
        C("新远端版本可应用", reload.MergeRemote(newer) == SyncApplyResult.Applied && reload.Records[0].Version == 3, reload.Records[0].Version.ToString());
        C("重复包幂等", reload.MergeRemote(newer) == SyncApplyResult.Duplicate, "duplicate");

        var local = reload.UpsertLocal("incident", "I1", "local", "windows");
        var remote = new SyncRecord { EntityType="incident", Id="I1", Version=1, BaseVersion=0, Json="remote", DeviceId="iphone" };
        C("并发编辑产生冲突", reload.MergeRemote(remote) == SyncApplyResult.Conflict && reload.Conflicts.Count == 1, reload.Conflicts.Count.ToString());
        C("重复冲突不重复记录", reload.MergeRemote(remote) == SyncApplyResult.Conflict && reload.Conflicts.Count == 1, reload.Conflicts.Count.ToString());
        var conflictId = reload.Conflicts[0].Id;
        C("可选择远端解决冲突", reload.ResolveConflict(conflictId, true, "windows") == SyncApplyResult.Applied && reload.Conflicts[0].Status == "resolved-remote", reload.Conflicts[0].Status);

        reload.UpsertLocal("incident", "I2", "local2", "windows");
        var remote2 = new SyncRecord { EntityType="incident", Id="I2", Version=1, BaseVersion=0, Json="remote2", DeviceId="iphone" };
        reload.MergeRemote(remote2); var c2 = reload.Conflicts.First(x => x.Status == "open");
        C("可保留本地解决冲突并生成新版本", reload.ResolveConflict(c2.Id, false, "windows") == SyncApplyResult.Applied && reload.Queue.Any(x => x.Record.Id == "I2" && x.Record.Version == 2), "queued");

        var deleted = reload.DeleteLocal("task", "T1", "windows");
        C("删除使用 tombstone", deleted.Deleted && deleted.Version == 4 && reload.Queue.Any(x => x.Record.Id == "T1" && x.Record.Deleted), deleted.Version.ToString());
        C("无效远端包被拒绝", reload.MergeRemote(new SyncRecord()) == SyncApplyResult.Invalid, "invalid");

        var project = new MobileProjectDto { Id="P1", Name="SureHand", SyncScope="local", UpdatedAt=1 };
        var json = Json.ToJson(project); var project2 = Json.FromJson<MobileProjectDto>(json);
        C("共享 Project DTO 可序列化", project2 != null && project2.Id == "P1" && project2.SyncScope == "local", json.Length.ToString());
        var incident = new IncidentDto { Id="I3", ProjectId="P1", Severity="S1", Description="现场故障" };
        C("共享 Incident DTO 可序列化", Json.FromJson<IncidentDto>(Json.ToJson(incident)).Severity == "S1", "S1");

        var draft=new SyncRecord{EntityType="incident",Id="amend",Version=1,BaseVersion=0,UpdatedAt=10,DeviceId="phone",Json="draft-1"};
        C("同手机首个离线版本可应用",reload.MergeRemote(draft)==SyncApplyResult.Applied,"initial");
        draft.UpdatedAt=20;draft.Json="draft-2";C("同手机未确认的后续编辑不误报冲突",reload.MergeRemote(draft)==SyncApplyResult.Applied&&reload.Records.First(x=>x.Id=="amend").Version==2,"promoted");
        draft.UpdatedAt=30;draft.Json="draft-3";C("连续离线编辑持续递增服务版本",reload.MergeRemote(draft)==SyncApplyResult.Applied&&reload.Records.First(x=>x.Id=="amend").Version==3,"latest");
        draft.UpdatedAt=20;draft.Json="draft-2";C("迟到的同手机旧稿不覆盖新稿",reload.MergeRemote(draft)==SyncApplyResult.Resolved&&reload.Records.First(x=>x.Id=="amend").Json=="draft-3","older");
        draft.UpdatedAt=30;draft.Json="draft-3";C("提升版本后的相同请求仍幂等",reload.MergeRemote(draft)==SyncApplyResult.Duplicate,"duplicate");
        var restarted=new SyncStore();C("同源版本映射重启后保留",restarted.MergeRemote(draft)==SyncApplyResult.Duplicate,"persisted origin");
        draft.DeviceId="other-phone";draft.UpdatedAt=40;draft.Json="other";C("另一台手机不能冒充同源修订",restarted.MergeRemote(draft)!=SyncApplyResult.Applied&&restarted.Records.First(x=>x.Id=="amend").Json=="draft-3","other device");
        restarted.UpsertLocal("incident","amend","desktop-edit","windows");draft.DeviceId="phone";
        C("电脑实际改动仍产生真实冲突",restarted.MergeRemote(draft)==SyncApplyResult.Conflict&&restarted.Records.First(x=>x.Id=="amend").Json=="desktop-edit","concurrent edit");
        draft.Version=5;draft.BaseVersion=4;draft.OriginVersion=999;draft.UpdatedAt=50;
        C("客户端不能伪造源版本元数据",restarted.MergeRemote(draft)==SyncApplyResult.Applied&&restarted.Records.First(x=>x.Id=="amend").OriginVersion==5,"server-derived origin");
        Console.WriteLine("\n==== Sync Store 验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
