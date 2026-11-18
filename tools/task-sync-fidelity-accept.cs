using System;
using System.IO;
using System.Linq;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Sync;

public class TaskSyncFidelityAccept
{
    static int passed,failed;
    static void Check(string text,bool ok){if(ok)passed++;else failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+text);}
    [STAThread] public static int Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"deskbox-task-fidelity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);AppPaths.DataDir=Store.DataDir=root;
        var store=new Store();store.Init();var projects=new WorkspaceStore();
        using(var sync=new LocalSyncCoordinator(store,projects)){
            // Same field spelling as the phone; deliberately use a different property order.
            const string json="{\"Notes\":\"SIP的FS需供应商更新步骤\",\"Owner\":\"王工\",\"UpdatedAt\":100,\"Id\":\"phone-task\",\"ProjectId\":\"\",\"Title\":\"复核超滤任务\",\"Status\":\"open\",\"Priority\":\"high\",\"Due\":\"2026-12-01\"}";
            var incoming=new SyncRecord{EntityType="task",Id="phone-task",Version=1,BaseVersion=0,UpdatedAt=100,DeviceId="iphone",Json=json};
            Check("手机任务真实合入电脑",sync.MergeFromCloud(incoming)==SyncApplyResult.Applied);
            Check("电脑保留手机负责人和备注",store.Tasks.Single().Owner=="王工"&&store.Tasks.Single().Notes.Contains("FS"));
            sync.PublishWindowsSnapshot();var record=sync.Sync.Records.Single(r=>r.Id==incoming.Id);
            Console.WriteLine("Projection diagnostic: incoming="+Json.ToJson(Json.FromJson<MobileTaskDto>(json))+"; native="+Json.ToJson(store.Tasks.Single())+"; outgoing="+record.Json+"; version="+record.Version);
            Check("仅JSON字段顺序不同不制造新版本或假冲突",record.Version==1&&record.Json==json&&!sync.Sync.Conflicts.Any());
            Check("同步快照仍含完整负责人及原时间",Json.FromJson<MobileTaskDto>(record.Json).Owner=="王工"&&Json.FromJson<MobileTaskDto>(record.Json).UpdatedAt==100);
            var ctx=new ToolContext{Store=store,Projects=projects,Sync=sync,Confirm=detail=>true,UiInvoke=action=>action()};
            Check("AI可按负责人查询真实任务",ToolRegistry.Instance.Invoke("task.list",new System.Collections.Generic.Dictionary<string,string>{{"query","王工"}},ctx).Output.Contains("复核超滤"));
            var task=store.Tasks.Single();var updated=ToolRegistry.Instance.Invoke("task.update",new System.Collections.Generic.Dictionary<string,string>{{"id",task.Id},{"expected",ApplicationTool.TaskStamp(task)},{"notes","供应商已确认更新步骤"}},ctx);
            Check("电脑AI仅改备注不清空负责人",updated.Ok&&store.Tasks.Single().Owner=="王工");
            sync.PublishWindowsSnapshot();var changed=sync.Sync.Records.Single(r=>r.Id==incoming.Id);var dto=Json.FromJson<MobileTaskDto>(changed.Json);
            Check("回传手机的修改时间递增且字段完整",changed.Version==2&&dto.UpdatedAt>100&&dto.Owner=="王工"&&dto.Notes.Contains("已确认"));
            sync.PublishWindowsSnapshot();Check("无变更的重复同步不反复加版本",sync.Sync.Records.Single(r=>r.Id==incoming.Id).Version==changed.Version);
            task=store.Tasks.Single();updated=ToolRegistry.Instance.Invoke("task.update",new System.Collections.Generic.Dictionary<string,string>{{"id",task.Id},{"expected",ApplicationTool.TaskStamp(task)},{"owner",""}},ctx);sync.PublishWindowsSnapshot();
            Check("明确清空负责人可同步且不会恢复旧值",updated.Ok&&Json.FromJson<MobileTaskDto>(sync.Sync.Records.Single(r=>r.Id==incoming.Id).Json).Owner=="");
            var reloaded=new Store();reloaded.Init();Check("负责人清空和备注均可重启恢复",reloaded.Tasks.Single().Owner==""&&reloaded.Tasks.Single().Notes.Contains("已确认"));
        }
        using(var restarted=new LocalSyncCoordinator(store,projects)){
            store.Tasks.RemoveAll(t=>t.Id=="phone-task");store.SaveTasksOnly();restarted.PublishWindowsSnapshot();
            Check("重启后首次同步前删除仍发送删除标记",restarted.Sync.Records.Single(r=>r.Id=="phone-task").Deleted);
        }
        Console.WriteLine("Task sync fidelity: PASS "+passed+" / FAIL "+failed);return failed==0?0:1;
    }
}
