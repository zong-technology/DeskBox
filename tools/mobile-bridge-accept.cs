using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Sync;

public class MobileBridgeAccept
{
 static int pass,fail;static void C(string n,bool ok,string d){if(ok){pass++;Console.WriteLine("PASS  "+n+"  ["+d+"]");}else{fail++;Console.WriteLine("FAIL  "+n+"  ["+d+"]");}}
 public static int Main(){var dir=Path.Combine(Path.GetTempPath(),"deskbox-bridge-"+DateTime.Now.Ticks);Directory.CreateDirectory(dir);Store.DataDir=dir;AppPaths.DataDir=dir;var root=Path.Combine(dir,"Project");Directory.CreateDirectory(root);var store=new Store();store.Init();var ws=new WorkspaceStore();var p=ws.Add("SureHand",root,"PLC");store.Tasks.Add(new TaskItem{Id="T1",Title="Windows Task",Project=p.Id,Priority="high",CreatedAt=1});store.SaveTasksOnly();using(var c=new LocalSyncCoordinator(store)){c.PublishWindowsSnapshot();C("Windows项目进入SyncStore",c.Sync.Records.Any(x=>x.EntityType=="project"&&x.Id==p.Id),c.Sync.Records.Count.ToString());C("Windows任务进入SyncStore",c.Sync.Records.Any(x=>x.EntityType=="task"&&x.Id=="T1"),"T1");var queue=c.Sync.Queue.Count;c.PublishWindowsSnapshot();C("重复快照不重复入队",c.Sync.Queue.Count==queue,queue.ToString());var apply=typeof(LocalSyncCoordinator).GetMethod("ApplyRemote",BindingFlags.NonPublic|BindingFlags.Instance);var mobileTask=new MobileTaskDto{Id="T2",ProjectId=p.Id,Title="iPhone Incident Follow-up",Priority="high",Status="open",UpdatedAt=10};var record=new SyncRecord{EntityType="task",Id="T2",Version=1,Json=Json.ToJson(mobileTask)};apply.Invoke(c,new object[]{record,SyncApplyResult.Applied});C("手机任务回写Windows",store.Tasks.Any(x=>x.Id=="T2"&&x.Title.Contains("iPhone")),store.Tasks.Count.ToString());mobileTask.Title="Updated on iPhone";record.Json=Json.ToJson(mobileTask);apply.Invoke(c,new object[]{record,SyncApplyResult.Applied});C("手机任务更新Windows",store.Tasks.First(x=>x.Id=="T2").Title=="Updated on iPhone","updated");apply.Invoke(c,new object[]{new SyncRecord{EntityType="task",Id="T2",Deleted=true},SyncApplyResult.Applied});C("手机tombstone删除Windows任务",!store.Tasks.Any(x=>x.Id=="T2"),"deleted");var mobileProject=new MobileProjectDto{Id=p.Id,Name="SureHand Mobile",Number="SH-01",Customer="Pharma",Progress=50,Risk="high"};apply.Invoke(c,new object[]{new SyncRecord{EntityType="project",Id=p.Id,Json=Json.ToJson(mobileProject)},SyncApplyResult.Applied});var reloaded=new WorkspaceStore();C("手机项目元数据回写Windows",reloaded.Current.Name=="SureHand Mobile"&&reloaded.Current.Risk=="high",reloaded.Current.Name);}Console.WriteLine("\n==== Mobile Bridge 验收: PASS "+pass+" / FAIL "+fail+" ====");return fail==0?0:1;}
}
