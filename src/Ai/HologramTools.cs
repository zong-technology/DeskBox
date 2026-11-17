using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;

namespace DeskBox.Ai
{
    internal sealed class VoiceToolCall
    {
        public string Id, Name;
        public IDictionary<string,object> Arguments;
    }
    internal sealed class VoiceToolResult
    {
        public string Id, Name;
        public object Response;
    }
    internal sealed class HologramInformation
    {
        public string Title, Content;
        public DateTime ExpiresUtc;
    }

    // Voice and text share the registered application tools and their existing authorization.
    internal sealed class HologramTools
    {
        private readonly Func<ToolContext> context;
        internal HologramInformation Information { get; private set; }
        internal HologramTools(Func<ToolContext> context) { this.context=context; }
        internal void Clear() { Information=null; }
        internal void Expire(DateTime now) { if(Information!=null && now>=Information.ExpiresUtc)Clear(); }

        internal VoiceToolResult Execute(VoiceToolCall call,CancellationToken cancellation=default(CancellationToken))
        {
            object response;
            try { response=Run(call,cancellation); }
            catch(ArgumentException error){response=new {ok=false,error=error.Message};}
            catch(Exception){response=new {ok=false,error="未能读取或展示信息，请稍后重试。"};}
            return new VoiceToolResult {Id=call.Id,Name=call.Name,Response=response};
        }

        private object Run(VoiceToolCall call,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var args=call.Arguments ?? new Dictionary<string,object>();
            if(call.Name=="hologram_clear"){Clear();return new {ok=true,message="临时投影已收起"};}
            if(call.Name=="hologram_show"){
                string title=Text(args,"title"),content=Text(args,"content");int duration=30;
                if(args.ContainsKey("seconds") && !int.TryParse(Text(args,"seconds"),out duration))throw new ArgumentException("seconds 必须是整数秒。");
                if(title.Length==0 || title.Length>48 || content.Length==0 || content.Length>480 || content.Split('\n').Length>8)
                    throw new ArgumentException("投影需要 1–48 字标题、1–480 字正文，正文最多 8 行，请概括要点。");
                duration=Math.Max(5,Math.Min(120,duration));
                Information=new HologramInformation {Title=title,Content=content,ExpiresUtc=DateTime.UtcNow.AddSeconds(duration)};
                return new {ok=true,message="已展示临时全息信息",seconds=duration};
            }
            if(call.Name=="deskbox_execute"){
                var operationContext=context==null?null:context();if(operationContext!=null)operationContext.Cancellation=cancellation;
                var operationResult=MobileAiRunner.Execute(Text(args,"tool"),Text(args,"arguments"),operationContext);
                return new {ok=operationResult.Ok,result=operationResult.Output,error=operationResult.Error};
            }
            string tool;
            switch(call.Name){
                case "deskbox_task_create":tool="task.create";break;
                case "deskbox_task_update":tool="task.update";break;
                case "deskbox_incident_create":tool="incident.create";break;
                case "deskbox_incident_update":tool="incident.update";if(args.ContainsKey("expected"))args["expected_version"]=args["expected"];break;
                case "deskbox_context":tool="app.context";break;
                case "deskbox_tasks":tool="task.list";break;
                case "deskbox_projects":tool="project.list";break;
                case "deskbox_incidents":tool="incident.list";break;
                default:throw new ArgumentException("此实时会话未提供该工具。");
            }
            if(context==null)return new {ok=false,error="软件数据服务未连接"};
            var current=context();if(current==null)return new {ok=false,error="软件数据服务未连接"};
            current.Cancellation=cancellation;
            var query=new Dictionary<string,string>();
            foreach(var pair in args)query[pair.Key]=Text(args,pair.Key);
            if(!query.ContainsKey("limit"))query["limit"]="12";
            int limit;if(!int.TryParse(query["limit"],out limit))limit=12;query["limit"]=Math.Max(1,Math.Min(30,limit)).ToString(CultureInfo.InvariantCulture);
            Func<ToolResult> read=()=>ToolRegistry.Instance.Invoke(tool,query,current);
            var result=read();
            string output=result.Ok ? result.Output : result.Error;
            bool truncated=output.Length>16000;if(truncated)output=output.Substring(0,16000);
            return new {ok=result.Ok,result=output,truncated=truncated};
        }

        private static string Text(IDictionary<string,object> args,string name)
        {
            object value;return args.TryGetValue(name,out value) ? Convert.ToString(value,CultureInfo.InvariantCulture).Replace("\r\n","\n").Trim() : "";
        }

        internal static object[] Declarations()
        {
            var filter=new {type="object",properties=new Dictionary<string,object> {
                {"query",new {type="string",description="按名称或内容搜索"}},
                {"project_id",new {type="string",description="先查询项目，使用其真实ID限定范围"}},
                {"status",new {type="string",description="仅任务支持 all、open、done"}},
                {"offset",new {type="integer",description="分页起点"}},
                {"limit",new {type="integer",description="最多30项，默认12项"}}
            }};
            var empty=new {type="object",properties=new Dictionary<string,object>()};
            return new object[]{
                new {name="deskbox_context",description="读取 DeskBox 当前项目、任务与事件数量等真实应用状态。",parameters=(object)empty},
                new {name="deskbox_tasks",description="读取软件真实任务。投影任务前先查询；结果不足时可分页。",parameters=(object)filter},
                new {name="deskbox_projects",description="读取软件真实项目与进度。",parameters=(object)filter},
                new {name="deskbox_incidents",description="读取软件真实现场事件与处理措施。",parameters=(object)filter},
                new {name="deskbox_execute",description="执行DeskBox已注册操作；先deskbox_context获取工具名称、说明和参数。支持任务、事件、授权文件、文档与工作流，保留已有确认和文件权限。",parameters=(object)new {type="object",properties=new {tool=new {type="string"},arguments=new {type="string",description="工具参数JSON对象字符串"}},required=new[]{"tool","arguments"}}},
                new {name="hologram_show",description="用户需要展示信息时，在全息投影旁临时显示纯文字要点，取代上次展示；不是视频或三维建模。",parameters=(object)new {
                    type="object",properties=new Dictionary<string,object>{
                        {"title",new {type="string",description="简短标题，最多48字"}},
                        {"content",new {type="string",description="纯文字要点，最多480字、8行；真实数据注明来源，推测要明确标注"}},
                        {"seconds",new {type="integer",description="显示5–120秒，默认30秒"}}},required=new[]{"title","content"}}},
                new {name="hologram_clear",description="按用户要求收起临时全息信息。",parameters=(object)empty}
            };
        }
        internal static object[] QwenDeclarations()
        {
            var result=new List<object>();
            foreach(var item in Declarations())result.Add(new {type="function",function=item});
            return result.ToArray();
        }
    }
}
