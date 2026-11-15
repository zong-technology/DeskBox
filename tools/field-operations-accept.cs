using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Sync;
using DeskBox.UI;
using DeskBox.UI.Views;

public class FieldOperationsAccept
{
    static int pass,fail;
    static void Check(string name,bool ok){if(ok)pass++;else fail++;Console.WriteLine((ok?"PASS ":"FAIL ")+name);}
    static bool Rejected(Action action){try{action();return false;}catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}}
    [STAThread] public static int Main()
    {
        var data=Path.Combine(Path.GetTempPath(),"deskbox-field-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);Store.DataDir=data;AppPaths.DataDir=data;
        var store=new Store();store.Init();var projects=new DeskBox.Engineering.WorkspaceStore();var project=projects.Add("现场联动验收",data,"CSV");
        using(var sync=new LocalSyncCoordinator(store,projects))
        {
            var incident=new IncidentDto{Id="I1",ProjectId=project.Id,Equipment="PT-101",Description="灌装机压力联锁异常",Owner="验证工程师",Severity="S1",Status="open"};
            sync.SaveIncident(incident,0);var record=sync.Sync.Records.Single(r=>r.Id=="I1");
            Check("事件持久化并关联项目位号",sync.Incidents.Single().ProjectId==project.Id&&sync.Incidents.Single().Equipment=="PT-101");
            Check("新事件记录发生与更新时间",sync.Incidents.Single().UpdatedAt>0&&sync.Incidents.Single().OccurredAt>0);
            var loaded=new SyncStore();Check("重启后事件保留",loaded.Records.Any(r=>r.EntityType=="incident"&&r.Id=="I1"));
            incident.Status="closed";Check("无处理措施不能关闭",Rejected(()=>sync.SaveIncident(incident,record.Version)));
            incident.Solution="校准压力传感器并复测联锁";incident.Cause="压力反馈偏移";incident.Status="investigating";sync.SaveIncident(incident,record.Version);
            Check("旧编辑版本不能覆盖后续修改",Rejected(()=>sync.SaveIncident(incident,record.Version)));
            record=sync.Sync.Records.Single(r=>r.Id=="I1");incident.Status="resolved";sync.SaveIncident(incident,record.Version);
            Check("调查与措施完整保存",sync.Incidents.Single().Cause==incident.Cause&&sync.Incidents.Single().Solution==incident.Solution&&sync.Incidents.Single().Status=="resolved");
            var current=sync.Sync.Records.Single(r=>r.Id=="I1");var phone=Json.FromJson<IncidentDto>(current.Json);phone.Status="closed";phone.UpdatedAt++;
            var incoming=new SyncRecord{EntityType="incident",Id="I1",BaseVersion=current.Version,Version=current.Version+1,DeviceId="iphone",UpdatedAt=phone.UpdatedAt,Json=Json.ToJson(phone)};
            Check("手机关闭结果可回写电脑",sync.MergeFromCloud(incoming)==SyncApplyResult.Applied&&sync.Incidents.Single().Status=="closed");
            incoming.Id="OTHER";Check("手机事件ID不一致拒绝入库",sync.MergeFromCloud(incoming)==SyncApplyResult.Invalid&&!sync.Incidents.Any(x=>x.Id=="OTHER"));
            incoming.Id="I1";phone.Status="unknown";incoming.Json=Json.ToJson(phone);Check("手机非法状态拒绝入库",sync.MergeFromCloud(incoming)==SyncApplyResult.Invalid);
            var task=sync.Sync.UpsertLocal("task","T1","{\"Id\":\"T1\",\"Title\":\"电脑\"}","windows");
            var edit=new SyncRecord{EntityType="task",Id="T1",BaseVersion=0,Version=1,DeviceId="iphone",Json="{\"Id\":\"T1\",\"Title\":\"手机\"}"};
            Check("双端编辑产生冲突",sync.Sync.MergeRemote(edit)==SyncApplyResult.Conflict);
            edit.BaseVersion=task.Version;edit.Version=task.Version+1;
            Check("手机明确按新版本重提可提交",sync.Sync.MergeRemote(edit)==SyncApplyResult.Applied);
            Check("重提成功关闭旧手机冲突",!sync.Sync.Conflicts.Any(c=>c.Status=="open"&&c.Remote.Id=="T1"));
            using(var host=new Form{ClientSize=new Size(1120,780)})using(var view=new IncidentsView(store,sync,projects){Dock=DockStyle.Fill})
            {
                host.Controls.Add(view);host.Show();view.Activate();Application.DoEvents();
                var folder=Path.Combine(Environment.CurrentDirectory,"shots","field-review");Directory.CreateDirectory(folder);
                using(var image=new Bitmap(host.Width,host.Height)){host.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"incidents.png"));}
                Check("事件处理页面可显示",view.Visible&&view.Width>800);
                using(var timer=new Timer{Interval=300})
                {
                    timer.Tick+=delegate{var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Text=="现场事件 · 调查与处理");if(dialog==null)return;timer.Stop();using(var image=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,"incident-editor.png"));}Check("事件编辑窗口可显示",dialog.ClientSize.Height>=620);dialog.Close();};
                    timer.Start();typeof(IncidentsView).GetMethod("Edit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,new object[]{current});
                }
                host.Close();
            }
        }
        Console.WriteLine("Field operations: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
