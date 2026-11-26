using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Ai
{
    public class ApplicationTool : IAiTool
    {
        private readonly string _name;
        public ApplicationTool(string name){_name=name;}
        public string Name{get{return _name;}}
        public bool RequiresConfirm{get{return _name=="task.update"||_name=="incident.update";}}
        public string Description
        {
            get
            {
                switch(_name)
                {
                    case "task.list":return "读取软件真实任务，含ID、备注、项目及expected校验值。args:{query,project_id,status=all|open|done,offset=0,limit=100}";
                    case "task.update":return "修改真实任务或完成状态，需确认并使用task.list的expected。args:{id,expected,title,due=yyyy-MM-dd,priority=low|normal|high,notes,owner,project_id,done=true|false}；仅修改提供字段。";
                    case "project.list":return "读取软件真实项目和目录、进度。args:{query,offset,limit,include_steps=true|false}，需要验证节点时启用include_steps。";
                    case "incident.list":return "读取真实现场事件及处理措施，含expected_version。args:{query,project_id,offset,limit}";
                    case "incident.create":return "新建现场问题并同步手机；args:{project_id,description,equipment,owner,severity=S1..S4,cause,solution}。先project.list获取真实项目ID。";
                    case "incident.update":return "更新事件并同步手机，需确认及版本检查。args:{id,expected_version,description,project_id,equipment,owner,severity=S1..S4,status=open|investigating|resolved|closed,cause,solution}";
                    case "workflow.list":return "读取已配置工作流的ID、名称、启用状态和触发方式；args:{}。执行使用workflow.run。";
                    case "tia.read":return "只读查询在博图工作台手动加载并授权的工程概况或唯一对象。args:{name}，留空返回概况。只提供导出证据，不操作PLC。";
                    case "app.navigate":return "打开软件功能页面。args:{view=home|desktop|projects|engineering|tia|ai|automation|search|tasks|incidents|weather|monitor|mobile|history|notifications|filehistory|library|disk|clipboard|settings}";
                    default:return "读取软件当前项目、任务/事件数量、文件授权和可用工具范围。args:{}；无需文件访问开关。";
                }
            }
        }
        private static string Get(Dictionary<string,string> args,string name){string value;return args.TryGetValue(name,out value)?value??"":"";}
        public static string TaskStamp(TaskItem task){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Json.ToJson(task)))).Replace("-","").ToLowerInvariant();}
        public ToolResult Invoke(ToolContext ctx,Dictionary<string,string> args)
        {
            if(ctx.Store==null)return ToolResult.Fail("软件数据服务尚未就绪");
            if(ctx.Cancellation.IsCancellationRequested)return ToolResult.Fail("操作已取消");
            int offset,limit;if(!int.TryParse(Get(args,"offset"),out offset))offset=0;if(!int.TryParse(Get(args,"limit"),out limit))limit=100;offset=Math.Max(0,offset);limit=Math.Max(1,Math.Min(200,limit));
            string query=Get(args,"query");
            if(Name=="task.list")
            {
                string status=Get(args,"status"),project=Get(args,"project_id");
                var tasks=ctx.Store.Tasks.Where(t=>(ctx.Project==null||ctx.Project.IncludesTask(t))&&(status!="open"||!t.Done)&&(status!="done"||t.Done)&&(project.Length==0||(ctx.Project==null?t.Project==project:ctx.Project.Id==project))&&(query.Length==0||(t.Title+" "+t.Notes+" "+(t.Owner??"")).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
                return ToolResult.Success(Json.ToJson(new{Total=tasks.Count,Offset=offset,Tasks=tasks.Skip(offset).Take(limit).Select(t=>new{t.Id,t.Title,t.Done,t.Priority,t.Due,t.Notes,Owner=t.Owner??"",t.Project,t.RemindAt,Expected=TaskStamp(t)}).ToArray()}));
            }
            if(Name=="task.update")
            {
                if(ctx.Confirm==null||!ctx.Confirm("修改软件任务："+Get(args,"id")+"\n"+Changes(args)))return ToolResult.Fail("任务修改未获确认");
                if(ctx.Cancellation.IsCancellationRequested)return ToolResult.Fail("操作已取消");
                var index=ctx.Store.Tasks.FindIndex(t=>t.Id==Get(args,"id")&&(ctx.Project==null||ctx.Project.IncludesTask(t)));if(index<0)return ToolResult.Fail("未找到任务ID，请先task.list");
                var original=ctx.Store.Tasks[index];if(!string.Equals(TaskStamp(original),Get(args,"expected"),StringComparison.OrdinalIgnoreCase))return ToolResult.Fail("任务已经变化或缺少expected，请重新task.list后再修改");
                var task=Json.FromJson<TaskItem>(Json.ToJson(original));
                if(args.ContainsKey("title"))task.Title=Get(args,"title").Trim();if(args.ContainsKey("notes"))task.Notes=Get(args,"notes");if(args.ContainsKey("owner"))task.Owner=Get(args,"owner");if(args.ContainsKey("project_id"))task.Project=Get(args,"project_id");
                if(ctx.Project!=null&&!args.ContainsKey("project_id"))task.Project=ctx.Project.Id;
                if(!ValidProject(ctx,task.Project))return ToolResult.Fail("项目ID无效，请先project.list");
                if(args.ContainsKey("due"))task.Due=Get(args,"due");if(args.ContainsKey("priority"))task.Priority=Get(args,"priority");
                DateTime due;if(string.IsNullOrWhiteSpace(task.Title)||!new[]{"low","normal","high"}.Contains(task.Priority)||(!string.IsNullOrEmpty(task.Due)&&!DateTime.TryParseExact(task.Due,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out due)))return ToolResult.Fail("标题、优先级或截止日期无效");
                if(args.ContainsKey("done")){bool done;if(!bool.TryParse(Get(args,"done"),out done))return ToolResult.Fail("done必须是true或false");task.Done=done;task.DoneAt=done?(task.DoneAt>0?task.DoneAt:DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()):0;}
                ctx.Store.Tasks[index]=task;try{ctx.Store.SaveTasksOnly();}catch{ctx.Store.Tasks[index]=original;throw;}ctx.Store.Log("info","task","AI更新任务："+task.Title);Refresh(ctx);
                return ToolResult.Success(Json.ToJson(new{Task=task,Expected=TaskStamp(task)}));
            }
            if(Name=="project.list")
            {
                if(ctx.Projects==null)return ToolResult.Fail("项目服务未就绪");
                var projects=ctx.Projects.Items.Where(p=>ctx.Project==null||p.Id==ctx.Project.Id).Where(p=>query.Length==0||(p.Name+" "+p.Number+" "+p.Customer+" "+p.Equipment).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
                return ToolResult.Success(Json.ToJson(new{Total=projects.Count,Projects=projects.Skip(offset).Take(limit).Select(p=>new{p.Id,p.Name,p.Root,p.Number,p.Customer,p.Equipment,p.Owner,p.Notes,p.Start,p.Deadline,Progress=Engineering.ValidationProgress.Overall(p),ValidationSteps=Get(args,"include_steps")=="true"?p.ValidationSteps:null}).ToArray()}));
            }
            if(Name=="incident.list")
            {
                if(ctx.Sync==null)return ToolResult.Fail("现场事件服务未就绪");
                var records=ctx.Sync.Sync.Records.Where(r=>r.EntityType=="incident"&&!r.Deleted).Select(r=>new{Record=r,Item=Json.FromJson<Sync.IncidentDto>(r.Json)}).Where(x=>x.Item!=null&&(ctx.Project==null||x.Item.ProjectId==ctx.Project.Id)&&(Get(args,"project_id").Length==0||x.Item.ProjectId==Get(args,"project_id"))&&(query.Length==0||(x.Item.Description+" "+x.Item.Equipment+" "+x.Item.Owner+" "+x.Item.Cause+" "+x.Item.Solution).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)).ToList();
                return ToolResult.Success(Json.ToJson(new{Total=records.Count,Incidents=records.Skip(offset).Take(limit).Select(x=>new{Incident=x.Item,ExpectedVersion=x.Record.Version}).ToArray()}));
            }
            if(Name=="incident.create")
            {
                if(ctx.Sync==null)return ToolResult.Fail("现场事件服务未就绪");
                var projectId = Get(args,"project_id");
                if (ctx.Project != null && projectId.Length == 0) projectId = ctx.Project.Id;
                if(!ValidProject(ctx,projectId))return ToolResult.Fail("项目ID无效，请先project.list确定项目");
                var item=new Sync.IncidentDto{Id=Guid.NewGuid().ToString(),ProjectId=projectId,Description=Get(args,"description"),Equipment=Get(args,"equipment"),Owner=Get(args,"owner"),Cause=Get(args,"cause"),Solution=Get(args,"solution")};
                if(args.ContainsKey("severity"))item.Severity=Get(args,"severity");
                ctx.Sync.SaveIncident(item,0);Refresh(ctx);return ToolResult.Success(Json.ToJson(new{saved=true,sync="已保存到电脑，等待手机同步",Incident=item}));
            }
            if(Name=="incident.update")
            {
                if(ctx.Sync==null)return ToolResult.Fail("现场事件服务未就绪");
                if(ctx.Confirm==null||!ctx.Confirm("修改现场事件："+Get(args,"id")+"\n"+Changes(args)))return ToolResult.Fail("事件修改未获确认");
                if(ctx.Cancellation.IsCancellationRequested)return ToolResult.Fail("操作已取消");
                var record=ctx.Sync.Sync.Records.FirstOrDefault(r=>r.EntityType=="incident"&&r.Id==Get(args,"id")&&!r.Deleted);long version;
                if(record==null||!long.TryParse(Get(args,"expected_version"),out version))return ToolResult.Fail("请先incident.list获取事件ID及expected_version");
                var item=Json.FromJson<Sync.IncidentDto>(record.Json);if(item==null || (ctx.Project!=null && item.ProjectId!=ctx.Project.Id))return ToolResult.Fail("当前范围无法读取事件");
                if(args.ContainsKey("description"))item.Description=Get(args,"description");if(args.ContainsKey("project_id"))item.ProjectId=Get(args,"project_id");if(args.ContainsKey("equipment"))item.Equipment=Get(args,"equipment");if(args.ContainsKey("owner"))item.Owner=Get(args,"owner");if(args.ContainsKey("severity"))item.Severity=Get(args,"severity");if(args.ContainsKey("status"))item.Status=Get(args,"status");if(args.ContainsKey("cause"))item.Cause=Get(args,"cause");if(args.ContainsKey("solution"))item.Solution=Get(args,"solution");
                if(!ValidProject(ctx,item.ProjectId))return ToolResult.Fail("项目ID无效，请先project.list");
                ctx.Sync.SaveIncident(item,version);Refresh(ctx);return ToolResult.Success("事件已更新，将通过现有同步服务回传手机");
            }
            if(Name=="workflow.list")return ctx.Workflows==null?ToolResult.Fail("工作流服务未就绪"):ToolResult.Success(Json.ToJson(ctx.Workflows.Workflows.Select(w=>new{w.Id,w.Name,w.Enabled,w.Trigger,w.IntervalMinutes}).ToArray()));
            if (Name == "tia.read")
                return ctx.TiaRead == null ? ToolResult.Fail("当前入口未提供博图上下文") : ToolResult.Success(ctx.TiaRead(Get(args, "name")));
            if(Name=="app.navigate")
            {
                var view=Get(args,"view");if(!new[]{"home","desktop","projects","engineering","tia","ai","automation","search","tasks","incidents","weather","monitor","mobile","history","notifications","filehistory","library","disk","clipboard","settings"}.Contains(view)||ctx.Navigate==null)return ToolResult.Fail("页面不存在或界面服务不可用");
                ctx.Navigate(view);return ToolResult.Success("已打开页面："+view);
            }
            var visibleTasks = ctx.Store.Tasks.Where(task => ctx.Project == null || ctx.Project.IncludesTask(task)).ToList();
            return ToolResult.Success(Json.ToJson(new{Tasks=visibleTasks.Count,OpenTasks=visibleTasks.Count(t=>!t.Done),Projects=ctx.Projects==null?0:ctx.Projects.Items.Count(p=>ctx.Project==null||p.Id==ctx.Project.Id),Incidents=ctx.Sync==null?0:ctx.Sync.Incidents.Count(i=>ctx.Project==null||i.ProjectId==ctx.Project.Id),CloudStatus=ctx.Cloud==null?"服务未就绪":ctx.Cloud.Status,FileAccess=ctx.FileAccessEnabled==null||ctx.FileAccessEnabled(),AllowedRoots=ctx.AllowedRoots.Where(root=>ctx.Project==null||ctx.Project.CanAccessPath(root)).ToArray(),Tools=ToolRegistry.Instance.ToolsForProject(ctx.Project).Select(t=>new{t.Name,t.Description,t.RequiresConfirm}).ToArray()}));
        }
        private static string Changes(Dictionary<string,string> args){return string.Join("\n",args.Where(p=>p.Key!="expected"&&p.Key!="expected_version"&&p.Key!="id").Select(p=>p.Key+"："+p.Value).ToArray());}
        internal static bool ValidProject(ToolContext ctx,string id){return ctx.Project!=null ? id==ctx.Project.Id : string.IsNullOrEmpty(id)||(ctx.Projects!=null&&ctx.Projects.Items.Any(p=>p.Id==id));}
        private static void Refresh(ToolContext ctx){if(ctx.RefreshApp!=null)ctx.RefreshApp();}
    }
}
