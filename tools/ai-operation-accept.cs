using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Sync;

// Real stores and the authenticated job runner, using synthetic data and no model service.
public class AiOperationAccept
{
    static int pass,fail;
    static void Check(string name,bool value){if(value)pass++;else fail++;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
    [STAThread] public static int Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"deskbox-ai-operation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Store.DataDir=Path.Combine(root,"data");AppPaths.DataDir=Store.DataDir;
        var store=new Store();store.Init();var projects=new WorkspaceStore();projects.Add("超滤项目",root,"PLC");var project=projects.Items.Last().Id;var providers=new ProviderStore();bool allow=true,fileAccess=false;int refreshed=0;
        using(var sync=new LocalSyncCoordinator(store,projects)){
            var context=new ToolContext{Store=store,Projects=projects,Sync=sync,AllowedRoots=new List<string>{root},FileAccessEnabled=()=>fileAccess,Confirm=detail=>allow,UiInvoke=action=>action(),RefreshApp=()=>refreshed++};
            var runner=new MobileAiRunner(providers,()=>context);
            Func<string,object,MobileAiProgress> execute=(name,args)=>runner.Run(Guid.NewGuid().ToString(),new List<AiMessage>{new AiMessage{Role="user",Content=MobileAiRunner.OperationPrefix+Json.ToJson(new{tool=name,arguments=Json.ToJson(args)})}},CancellationToken.None,p=>{}).GetAwaiter().GetResult();
            var result=execute("task.create",new{title="SIP的FS需要供应商更新步骤",project_id=project,notes="提供修订后的操作步骤"});
            Check("手机直接操作无需第二次模型请求且真实保存项目任务",result.Status=="completed"&&result.Text.Contains("\"ok\":true")&&store.Tasks.Last().Project==project&&store.Tasks.Last().Notes.Contains("修订"));
            var task=store.Tasks.Last();var expected=ApplicationTool.TaskStamp(task);
            Check("新建任务刷新软件列表",refreshed==1);
            result=execute("task.create",new{title="错误项目",project_id="missing"});Check("无效项目不能保存",result.Text.Contains("\"ok\":false")&&store.Tasks.Count==1);
            result=execute("task.update",new{id=task.Id,expected=expected,done=true});Check("手机可确认并完成任务",result.Text.Contains("\"ok\":true")&&store.Tasks[0].Done);
            result=execute("task.update",new{id=task.Id,expected=expected,title="过期覆盖"});Check("过期版本被拒绝",result.Text.Contains("\"ok\":false")&&!store.Tasks[0].Title.Contains("过期"));
            allow=false;result=execute("task.update",new{id=task.Id,expected=ApplicationTool.TaskStamp(store.Tasks[0]),title="拒绝覆盖"});Check("远程操作保留确认限制",result.Text.Contains("\"ok\":false")&&!store.Tasks[0].Title.Contains("拒绝"));allow=true;
            result=execute("incident.create",new{project_id=project,description="SIP的FS待更新",severity="S2"});Check("手机创建问题进入真实事件同步库",result.Text.Contains("\"ok\":true")&&sync.Incidents.Any(i=>i.ProjectId==project&&i.Description.Contains("SIP")));
            var file=Path.Combine(root,"action.txt");result=execute("fs.write",new{path=file,content="已授权内容"});Check("关闭文件访问仍阻止远程写文件",result.Text.Contains("\"ok\":false")&&!File.Exists(file));
            fileAccess=true;result=execute("fs.write",new{path=file,content="已授权内容"});Check("确认后执行授权目录文件操作",result.Text.Contains("\"ok\":true")&&File.ReadAllText(file)=="已授权内容");
            result=execute("shell.run",new{command="must-not-run"});Check("不执行未注册系统命令",result.Text.Contains("\"ok\":false"));
            var savedDataDir=Store.DataDir;var blocked=Path.Combine(root,"blocked-storage");File.WriteAllText(blocked,"fixture");
            try {
                Store.DataDir=blocked;
                result=execute("task.create",new{title="保存失败不能留下任务",project_id=project});Check("创建保存失败回退内存",result.Text.Contains("\"ok\":false")&&store.Tasks.Count==1);
                var old=store.Tasks[0];result=execute("task.update",new{id=old.Id,expected=ApplicationTool.TaskStamp(old),title="保存失败不能覆盖"});Check("修改保存失败保留原任务",result.Text.Contains("\"ok\":false")&&store.Tasks[0].Title==old.Title);
            } finally { Store.DataDir=savedDataDir; }
            result=execute("app.context",new{});Check("全息可发现完整工具描述",result.Text.Contains("task.create")&&result.Text.Contains("doc.edit")&&result.Text.Contains("Description"));
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();result=runner.Run("cancel",new List<AiMessage>{new AiMessage{Role="user",Content=MobileAiRunner.OperationPrefix+Json.ToJson(new{tool="task.create",arguments="{\"title\":\"取消任务\"}"})}},cancel.Token,p=>{}).GetAwaiter().GetResult();Check("取消操作不会落盘",result.Status=="cancelled"&&store.Tasks.Count==1);}
        }
        Console.WriteLine("AI operations: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
