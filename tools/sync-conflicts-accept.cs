using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Sync;
using DeskBox.UI;
using DeskBox.UI.Views;

public class SyncConflictsAccept
{
    static int pass, fail;
    static void C(string name,bool ok) { if(ok)pass++;else fail++;Console.WriteLine((ok?"PASS ":"FAIL ")+name); }
    static SyncRecord Remote(string id,string title) { return new SyncRecord { Id=id,EntityType="task",Version=1,BaseVersion=0,DeviceId="iphone",Json=Json.ToJson(new MobileTaskDto { Id=id,Title=title }) }; }
    [STAThread] public static int Main()
    {
        var dir=Path.Combine(Path.GetTempPath(),"deskbox-conflicts-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);Store.DataDir=dir;AppPaths.DataDir=dir;
        var store=new Store();store.Init();store.Tasks.Add(new TaskItem{Id="T1",Title="电脑原始任务"});
        using(var sync=new LocalSyncCoordinator(store))
        {
            sync.PublishWindowsSnapshot(); var remote=Remote("T1","手机修改任务");sync.Sync.MergeRemote(remote);var conflict=sync.Sync.Conflicts.First();
            store.Tasks[0].Title="电脑后续新修改";sync.PublishWindowsSnapshot();long version=sync.Sync.Records.First(x=>x.Id=="T1").Version;
            C("旧比较版本拒绝覆盖新数据",sync.ResolveConflict(conflict.Id,true,version-1)==SyncApplyResult.Conflict && store.Tasks[0].Title=="电脑后续新修改");
            C("保留电脑使用最新数据非旧快照",sync.ResolveConflict(conflict.Id,false,version)==SyncApplyResult.Applied && store.Tasks[0].Title=="电脑后续新修改");
            C("已解决旧手机提交可确认而非重复冲突",sync.Sync.MergeRemote(remote)==SyncApplyResult.Resolved && sync.Sync.Conflicts.Count(x=>x.Status=="open")==0);
            C("结果生成更高版本并排队",sync.Sync.Records.First(x=>x.Id=="T1").Version>version && sync.Sync.Queue.Any(x=>x.Record.Id=="T1"));
            remote=Remote("T1","手机新的不同修改");sync.Sync.MergeRemote(remote);conflict=sync.Sync.Conflicts.Last();version=sync.Sync.Records.First(x=>x.Id=="T1").Version;
            C("同版本不同内容仍产生新冲突",conflict.Status=="open" && conflict.Remote.Json.Contains("新的不同修改"));
            using(var dialog=new SyncConflictsForm(sync,Palette.For("cyber","#22D3EE")))
            {
                dialog.Show();Application.DoEvents();var output=Path.Combine(Environment.CurrentDirectory,"shots","sync-review");Directory.CreateDirectory(output);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var localText = (TextBox)typeof(SyncConflictsForm).GetField("_local", flags).GetValue(dialog);
                var remoteText = (TextBox)typeof(SyncConflictsForm).GetField("_remote", flags).GetValue(dialog);
                C("比较栏包含两端真实内容", localText.Text.Contains("电脑后续新修改") && remoteText.Text.Contains("手机新的不同修改"));
                using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(Path.Combine(output,"conflict-comparison.png"));}
                C("冲突比较界面可显示",dialog.Visible);
            }
            C("采用手机结果回写实际任务",sync.ResolveConflict(conflict.Id,true,version)==SyncApplyResult.Applied && store.Tasks[0].Title=="手机新的不同修改");
            C("重复处理被拒绝",sync.ResolveConflict(conflict.Id,true,version)==SyncApplyResult.Invalid);
            var invalid = Remote("T1", "错误记录"); invalid.Version = 2; invalid.Json = Json.ToJson(new MobileTaskDto { Id = "OTHER", Title = "错误目标" }); sync.Sync.MergeRemote(invalid);
            conflict = sync.Sync.Conflicts.Last(); version = sync.Sync.Records.First(x => x.Id == "T1").Version;
            C("记录ID不匹配拒绝写入其他任务", sync.ResolveConflict(conflict.Id,true,version)==SyncApplyResult.Invalid && !store.Tasks.Any(x => x.Id == "OTHER"));
            sync.ResolveConflict(conflict.Id,false,version);
            var deleted=Remote("T1","");deleted.Version=2;deleted.Deleted=true;sync.Sync.MergeRemote(deleted);conflict=sync.Sync.Conflicts.Last();version=sync.Sync.Records.First(x=>x.Id=="T1").Version;
            C("选择删除标记回写任务删除",sync.ResolveConflict(conflict.Id,true,version)==SyncApplyResult.Applied && !store.Tasks.Any(x=>x.Id=="T1"));
            var reload=new SyncStore();C("解决状态持久化",reload.Conflicts.All(x=>x.Status!="open"));
            store.Tasks.Add(new TaskItem { Id="T3", Title="随后在电脑删除" }); sync.PublishWindowsSnapshot(); sync.Sync.MergeRemote(Remote("T3","手机旧任务"));
            conflict=sync.Sync.Conflicts.Last(); store.Tasks.RemoveAll(x=>x.Id=="T3"); sync.PublishWindowsSnapshot();
            var tombstone=sync.Sync.Records.First(x=>x.Id=="T3"); C("电脑删除任务产生同步删除标记",tombstone.Deleted);
            C("保留电脑删除不复活旧任务",sync.ResolveConflict(conflict.Id,false,tombstone.Version)==SyncApplyResult.Applied && !store.Tasks.Any(x=>x.Id=="T3") && sync.Sync.Records.First(x=>x.Id=="T3").Deleted);
        }
        Console.WriteLine("Sync conflict: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
