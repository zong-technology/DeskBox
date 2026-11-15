using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DeskBox;
using DeskBox.Core;
using DeskBox.Sync;

public class MobileTaskAccept
{
    static int pass, fail;
    static void Check(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    public static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "deskbox-mobile-task-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data); Store.DataDir = data; AppPaths.DataDir = data;
        var store = new Store(); store.Init();
        var workspaces = new DeskBox.Engineering.WorkspaceStore(); var project = workspaces.Add("N/A 验证项目", data, "CSV");
        using (var coordinator = new LocalSyncCoordinator(store, workspaces))
        {
            var apply = typeof(LocalSyncCoordinator).GetMethod("ApplyRemote", BindingFlags.NonPublic | BindingFlags.Instance);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var record = new SyncRecord { EntityType="task", Id="M1", Version=1, UpdatedAt=timestamp, Json=Json.ToJson(new MobileTaskDto { Id="M1", Title="OQ 任务", Status="done", Due="2026-11-05", Notes="验证报警记录", Priority="high" }) };
            apply.Invoke(coordinator,new object[]{record,SyncApplyResult.Applied});
            var task=store.Tasks.Single();
            Check("旧手机缺少更新时间使用同步记录时间",task.CreatedAt==timestamp && task.DoneAt==timestamp);
            Check("手机任务字段完整回写",task.Done && task.Priority=="high" && task.Due=="2026-11-05" && task.Notes=="验证报警记录");
            record.Json=Json.ToJson(new MobileTaskDto { Id="M1",Title="重新打开",Status="open",UpdatedAt=timestamp+100 }); apply.Invoke(coordinator,new object[]{record,SyncApplyResult.Applied});
            Check("取消完成会清除旧完成时间",!task.Done && task.DoneAt==0 && task.Title=="重新打开");
            record.Json=Json.ToJson(new MobileTaskDto { Id="WRONG",Title="错误目标" }); apply.Invoke(coordinator,new object[]{record,SyncApplyResult.Applied}); Check("不匹配的记录ID不写入",store.Tasks.Count==1 && task.Title=="重新打开");
            record.Deleted=true;apply.Invoke(coordinator,new object[]{record,SyncApplyResult.Applied});Check("手机删除标记移除电脑任务",store.Tasks.Count==0);
            var steps = DeskBox.Engineering.ValidationProgress.Create(); foreach (var step in steps) step.Percent = null;
            workspaces.SaveValidation(project.Id, steps, "", "", "", data); coordinator.PublishWindowsSnapshot();
            var snapshot = Json.FromJson<MobileProjectDto>(coordinator.Sync.Records.First(x => x.EntityType == "project" && x.Id == project.Id).Json);
            Check("项目N/A不会同步成0%", !snapshot.ProgressApplicable && snapshot.UpdatedAt > 0);
            project.UpdatedAt = 1; workspaces.UpdateProjectDetails(project.Id,"P1","反应器","工程师","","","low"); Check("项目信息修改刷新更新时间",project.UpdatedAt > 1);
        }
        Console.WriteLine("Mobile task: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
