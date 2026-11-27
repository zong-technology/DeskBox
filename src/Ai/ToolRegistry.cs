using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DeskBox.Core;
using DeskBox.Engineering;

namespace DeskBox.Ai
{
    public class ToolContext
    {
        public Store Store;
        public ProjectContext Project;
        public List<string> AllowedRoots = new List<string>();
        public List<string> AllowedFiles = new List<string>();
        public System.Threading.CancellationToken Cancellation;
        public Action RefreshAuthorization;
        public Func<bool> FileAccessEnabled;
        public Func<string, bool> Confirm;      // 高危操作二次确认（UI 注入）
        public AiService Ai;
        public Automation.WorkflowEngine Workflows;
        public Engineering.WorkspaceStore Projects;
        public Sync.LocalSyncCoordinator Sync;
        public Sync.CloudSyncAgent Cloud;
        public Fs.FileOps Files;
        public Action<string> Navigate;
        public Func<string, string> TiaRead;
        public Action RefreshApp;
        public Func<Func<ToolResult>,ToolResult> UiInvoke;
    }

    public class ToolResult
    {
        public bool Ok;
        public string Output = "";
        public string Error = "";
        public string ReceiptId = "";
        public bool NeedConfirm;
        public string ConfirmText = "";
        public static ToolResult Success(string output) { return new ToolResult { Ok = true, Output = output }; }
        public static ToolResult Fail(string error) { return new ToolResult { Ok = false, Error = error }; }
    }

    public interface IAiTool
    {
        string Name { get; }
        string Description { get; }
        bool RequiresConfirm { get; }
        ToolResult Invoke(ToolContext ctx, Dictionary<string, string> args);
    }

    /// <summary>
    /// AI 可调用工具白名单。只有这里注册的工具能被调用；所有路径必须落在授权根目录内；
    /// 写文件/删除等高风险动作要求二次确认（由 UI 注入 Confirm 回调）。
    /// </summary>
    public class ToolRegistry
    {
        private static readonly ToolRegistry _instance = new ToolRegistry();
        public static ToolRegistry Instance { get { return _instance; } }

        private readonly List<IAiTool> _tools = new List<IAiTool>();

        private ToolRegistry()
        {
            _tools.Add(new SearchTool());
            _tools.Add(new ReadTool());
            _tools.Add(new SummarizeTool());
            _tools.Add(new SaveFileTool());
            _tools.Add(new CreateTaskTool());
            _tools.Add(new RunWorkflowTool());
            _tools.Add(new WebReadTool());
            foreach(var name in new[]{"app.context","app.navigate","tia.read","task.list","task.update","project.list","incident.list","incident.create","incident.update","workflow.list"})_tools.Add(new ApplicationTool(name));
            foreach(var name in new[]{"fs.list","fs.move","fs.copy","fs.rename","fs.trash","fs.undo"})_tools.Add(new FileManagementTool(name));
            _tools.Add(new Scale420Tool());
            _tools.Add(new S7AddressTool());
            _tools.Add(new ModbusFloatTool());
            _tools.Add(new ValidationTemplateTool());
            _tools.Add(new DocumentTool("read"));
            _tools.Add(new DocumentTool("search"));
            _tools.Add(new DocumentTool("create"));
            _tools.Add(new DocumentTool("ocr"));
            _tools.Add(new DocumentTool("edit"));
        }

        public List<IAiTool> Tools { get { return ToolsForProject(null); } }
        internal List<IAiTool> ToolsForProject(ProjectContext project)
        {
            return _tools.Where(tool => AiEnvironment.ToolAllowed(tool.Name) && ProjectAllowsTool(tool.Name, project)).ToList();
        }

        public IAiTool Get(string name)
        {
            return _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ProjectAllowsTool(string name, ProjectContext project)
        {
            return project == null || !new[] { "workflow.run", "workflow.list", "web.read" }.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        public string DescribeForPrompt(ProjectContext project = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("可用工具（需要时输出 ```tool 块，内容为 JSON：{\"name\":\"工具名\",\"args\":{...}}）：");
            foreach (var t in ToolsForProject(project)) sb.AppendLine("- " + t.Name + "：" + t.Description);
            return sb.ToString();
        }

        public ToolResult Invoke(string name, Dictionary<string, string> args, ToolContext ctx)
        {
            var tool = Get(name);
            if (tool == null) return ToolResult.Fail("未注册的工具：" + name);
            if(!AiEnvironment.ToolAllowed(tool.Name))return ToolResult.Fail("本地隔离环境未开放可能联网或启动外部程序的工作流。");
            try
            {
                ctx=ctx??new ToolContext();args=args??new Dictionary<string,string>();
                if (ctx.Cancellation.IsCancellationRequested) return ToolResult.Fail("操作已取消");
                if (!ProjectAllowsTool(tool.Name, ctx.Project)) return ToolResult.Fail("当前项目未授权全局工作流或外部网页工具。");
                if (ctx.UiInvoke != null && (tool is ApplicationTool || tool is CreateTaskTool))
                    return ctx.UiInvoke(() => ctx.Cancellation.IsCancellationRequested ? ToolResult.Fail("操作已取消") : tool.Invoke(ctx, args));
                return tool.Invoke(ctx,args);
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "ai", "工具执行异常 " + name + ": " + ex.Message);
                return ToolResult.Fail("工具执行失败：" + ex.Message);
            }
        }

        internal static bool IsAllowed(ToolContext ctx, string path, out string error)
        {
            if(AiEnvironment.Local && (path??"").StartsWith(@"\\",StringComparison.Ordinal)) { error="本地隔离模式不能读取网络共享。";return false; }
            error = "";
            try
            {
                if (ctx != null && ctx.RefreshAuthorization != null) ctx.RefreshAuthorization();
                if (ctx != null && ctx.Cancellation.IsCancellationRequested) { error = "文件任务已取消或授权发生变化"; return false; }
                if (ctx != null && ctx.FileAccessEnabled != null && !ctx.FileAccessEnabled()) { error = "AI 文件访问已关闭，请在设置 → 文件与隐私中启用"; return false; }
                var full = Path.GetFullPath(path);
                if (ctx != null && ctx.Project != null && !ctx.Project.CanAccessPath(full))
                {
                    error = "路径超出当前工程资料边界";
                    return false;
                }
                AiEnvironment.RequireLocalPath(full);
                var component = full;
                while (!string.IsNullOrEmpty(component))
                {
                    if ((File.Exists(component) || Directory.Exists(component)) && (File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) { error = "AI 不访问符号链接或重解析路径：" + component; return false; }
                    component = Path.GetDirectoryName(component);
                }
                if (ctx != null && ctx.AllowedFiles != null && ctx.AllowedFiles.Any(x => string.Equals(Path.GetFullPath(x), full, StringComparison.OrdinalIgnoreCase))) return true;
                if (ctx == null || ctx.AllowedRoots == null || ctx.AllowedRoots.Count == 0)
                {
                    error = "未配置授权目录，已拒绝访问（请在设置 → 文件与隐私 中添加授权目录）";
                    return false;
                }
                foreach (var root in ctx.AllowedRoots)
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    if (ProjectContext.Under(full, root))
                        return true;
                }
                error = "路径不在授权目录内：" + full;
                return false;
            }
            catch (Exception ex)
            {
                error = "路径无效：" + ex.Message;
                return false;
            }
        }

        // ---------------- 具体工具 ----------------

        private class SearchTool : IAiTool
        {
            public string Name { get { return "fs.search"; } }
            public string Description { get { return "在授权目录内按关键词搜索文件；args: {root, keyword, limit}"; } }
            public bool RequiresConfirm { get { return false; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string root, keyword; int limit = 20;
                a.TryGetValue("root", out root); a.TryGetValue("keyword", out keyword);
                string ls; if (a.TryGetValue("limit", out ls)) int.TryParse(ls, out limit);
                if (string.IsNullOrEmpty(root)) return ToolResult.Fail("缺少 root");
                string err;
                if (!IsAllowed(ctx, root, out err)) return ToolResult.Fail(err);
                if (!Directory.Exists(root)) return ToolResult.Fail("目录不存在：" + root);

                var hits = new List<string>();
                bool stopped=false;
                try
                {
                    var directories=new Stack<string>();directories.Push(root);var clock=System.Diagnostics.Stopwatch.StartNew();
                    while(directories.Count>0&&hits.Count<Math.Max(1,Math.Min(200,limit)))
                    {
                        ctx.Cancellation.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>15000){stopped=true;break;}
                        var directory=directories.Pop();
                        try{foreach(var entry in Directory.EnumerateFileSystemEntries(directory)){
                            ctx.Cancellation.ThrowIfCancellationRequested();if(!IsAllowed(ctx,entry,out err))continue;
                            if(Directory.Exists(entry))directories.Push(entry);
                            else if(string.IsNullOrEmpty(keyword)||Path.GetFileName(entry).IndexOf(keyword,StringComparison.OrdinalIgnoreCase)>=0){hits.Add(entry+"  ("+new FileInfo(entry).Length+" B)");if(hits.Count>=Math.Max(1,Math.Min(200,limit)))break;}
                            if(clock.ElapsedMilliseconds>15000){stopped=true;break;}
                        }}catch(UnauthorizedAccessException){}catch(DirectoryNotFoundException){}
                        if(stopped)break;
                    }
                }
                catch (Exception ex) { return ToolResult.Fail("搜索失败：" + ex.Message); }
                return ToolResult.Success((hits.Count == 0 ? "没有匹配文件" : "找到 " + hits.Count + " 个：\n" + string.Join("\n", hits.ToArray()))+(stopped?"\n[搜索达到15秒上限，请缩小root范围继续]":""));
            }
        }

        private class ReadTool : IAiTool
        {
            public string Name { get { return "fs.read"; } }
            public string Description { get { return "读取授权文件正文；PDF/Word/Excel/Visio请优先用doc.read。args:{path,maxChars}"; } }
            public bool RequiresConfirm { get { return false; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string path; a.TryGetValue("path", out path);
                int max = 8000;
                string ms; if (a.TryGetValue("maxChars", out ms)) int.TryParse(ms, out max);
                if (string.IsNullOrEmpty(path)) return ToolResult.Fail("缺少 path");
                string err;
                if (!IsAllowed(ctx, path, out err)) return ToolResult.Fail(err);
                if (!File.Exists(path)) return ToolResult.Fail("文件不存在：" + path);
                try
                {
                    return DocumentSkills.Execute("read", ctx, new Dictionary<string, string> { { "path", path }, { "maxChars", Math.Max(100, Math.Min(200000, max)).ToString() } });
                }
                catch (Exception ex) { return ToolResult.Fail(ex.Message); }
            }
        }

        private class SummarizeTool : IAiTool
        {
            public string Name { get { return "doc.summarize"; } }
            public string Description { get { return "对授权目录内的文档做本地摘要（不联网）；args: {path}"; } }
            public bool RequiresConfirm { get { return false; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string path; a.TryGetValue("path", out path);
                if (string.IsNullOrEmpty(path)) return ToolResult.Fail("缺少 path");
                string err;
                if (!IsAllowed(ctx, path, out err)) return ToolResult.Fail(err);
                var ai = ctx.Ai ?? AiService.Instance;
                var ans = ai.Ask(new AiRequest { Kind = "summarize", Paths = new List<string> { path } });
                if (!ans.Ok) return ToolResult.Fail(ans.Error);
                var sb = new StringBuilder();
                sb.AppendLine(ans.Text);
                foreach (var b in ans.Bullets) sb.AppendLine("• " + b);
                return ToolResult.Success(sb.ToString());
            }
        }

        private class SaveFileTool : IAiTool
        {
            public string Name { get { return "fs.write"; } }
            public string Description { get { return "把文本保存为文件（需确认，限定授权目录）；args: {path, content}"; } }
            public bool RequiresConfirm { get { return true; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string path, content;
                a.TryGetValue("path", out path); a.TryGetValue("content", out content);
                if (string.IsNullOrEmpty(path)) return ToolResult.Fail("缺少 path");
                string err;
                if (!IsAllowed(ctx, path, out err)) return ToolResult.Fail(err);
                // fail-closed：没有确认回调时必须拒绝，不能默认放行
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".docx" || extension == ".xlsx" || extension == ".pdf") return DocumentSkills.Execute("create", ctx, a);
                if (new[] { ".doc", ".xls", ".xlsm", ".vsd", ".vsdx", ".ppt", ".pptx", ".zip", ".exe", ".dll" }.Contains(extension)) return ToolResult.Fail("fs.write 不能把纯文本伪装成二进制/Office 文件，请使用支持的 doc.create 格式");
                if (ctx.Confirm == null) return ToolResult.Fail("该操作需要用户确认，但当前没有确认通道，已拒绝执行");
                if (!ctx.Confirm("将写入文件：\n" + path + "\n\n内容长度 " + (content ?? "").Length + " 字符"))
                    return ToolResult.Fail("用户取消写入");
                try
                {
                    if(!IsAllowed(ctx,path,out err))return ToolResult.Fail(err);
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    string backup="";
                    if(File.Exists(path)){var folder=Path.Combine(AppPaths.BackupDir,"ai-edits",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);backup=Path.Combine(folder,Path.GetFileName(path));File.Copy(path,backup,false);}
                    var temporary=path+".deskbox-"+Guid.NewGuid().ToString("N")+".tmp";
                    try{File.WriteAllText(temporary,content??"",new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
                    if (ctx.Store != null) ctx.Store.Log("info", "backup", "AI 写入文件：" + path);
                    return ToolResult.Success("已写入 " + path+(backup.Length==0?"":"\n原件备份："+backup));
                }
                catch (Exception ex) { return ToolResult.Fail(ex.Message); }
            }
        }

        private class CreateTaskTool : IAiTool
        {
            public string Name { get { return "task.create"; } }
            public string Description { get { return "创建并保存真实项目任务；args: {title, due=yyyy-MM-dd, priority=low|normal|high, project_id, notes, owner}；先project.list定位项目，返回真实ID与expected。"; } }
            public bool RequiresConfirm { get { return false; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                if (ctx.Store == null) return ToolResult.Fail("任务服务不可用");
                if(ctx.Cancellation.IsCancellationRequested)return ToolResult.Fail("操作已取消");
                string title, due, prio;
                a.TryGetValue("title", out title); a.TryGetValue("due", out due); a.TryGetValue("priority", out prio);
                if (string.IsNullOrWhiteSpace(title)) return ToolResult.Fail("缺少 title");
                string project,notes,owner;a.TryGetValue("project_id",out project);a.TryGetValue("notes",out notes);a.TryGetValue("owner",out owner);
                if (ctx.Project != null && string.IsNullOrEmpty(project)) project = ctx.Project.Id;
                if(!ApplicationTool.ValidProject(ctx,project))return ToolResult.Fail("项目ID无效，请先project.list确定项目");
                DateTime date;
                if(!string.IsNullOrEmpty(due)&&!DateTime.TryParseExact(due,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out date))return ToolResult.Fail("日期需要yyyy-MM-dd");
                if(!string.IsNullOrEmpty(prio)&&!new[]{"low","normal","high"}.Contains(prio))return ToolResult.Fail("优先级无效");
                var t = new TaskItem
                {
                    Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                    Title = title.Trim(), Project=project??"", Notes=notes??"", Owner=owner??"",
                    Due = due ?? "",
                    Priority = string.IsNullOrEmpty(prio) ? "normal" : prio,
                    CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                };
                ctx.Store.Tasks.Add(t);
                try{ctx.Store.SaveTasksOnly();}catch{ctx.Store.Tasks.Remove(t);throw;}
                ctx.Store.Log("info", "task", "AI 创建任务：" + t.Title);
                if(ctx.RefreshApp!=null)ctx.RefreshApp();
                return ToolResult.Success(DeskBox.Core.Json.ToJson(new{saved=true,sync="已保存到电脑，等待手机同步",Task=t,Expected=ApplicationTool.TaskStamp(t)}));
            }
        }

        private class RunWorkflowTool : IAiTool
        {
            public string Name { get { return "workflow.run"; } }
            public string Description { get { return "按名称触发已配置的工作流（需确认，先预演）；args: {name}"; } }
            public bool RequiresConfirm { get { return true; } }

            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                if (ctx.Workflows == null) return ToolResult.Fail("自动化引擎不可用");
                string name; a.TryGetValue("name", out name);
                var wf = ctx.Workflows.Workflows.FirstOrDefault(w => w.Name == name)
                      ?? ctx.Workflows.Workflows.FirstOrDefault(w => w.Name.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);
                if (wf == null) return ToolResult.Fail("找不到工作流：" + name);
                var useNodes = wf.Nodes != null && wf.Nodes.Count > 0;
                var dry = useNodes ? ctx.Workflows.RunV2(wf.Id, true) : ctx.Workflows.Run(wf.Id, true);
                if (ctx.Confirm == null) return ToolResult.Fail("该操作需要用户确认，但当前没有确认通道，已拒绝执行");
                if (!ctx.Confirm("即将执行工作流「" + wf.Name + "」\n\n预演结果：\n" + dry.Message))
                    return ToolResult.Fail("用户取消执行");
                if(ctx.Cancellation.IsCancellationRequested)return ToolResult.Fail("操作已取消");
                var run = useNodes ? ctx.Workflows.RunV2(wf.Id, false) : ctx.Workflows.Run(wf.Id, false);
                return run.Ok ? ToolResult.Success(run.Message + "\n" + string.Join("\n", run.Steps.ToArray()))
                              : ToolResult.Fail(run.Message);
            }
        }

        private class Scale420Tool : IAiTool
        {
            public string Name { get { return "eng.scale_4_20"; } }
            public string Description { get { return "确定性计算 4–20mA 对应工程值；args: {mA, low, high}"; } }
            public bool RequiresConfirm { get { return false; } }
            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                double ma, low, high;
                if (!TryD(a, "mA", out ma) || !TryD(a, "low", out low) || !TryD(a, "high", out high)) return ToolResult.Fail("mA/low/high 必须是数字");
                var r = EngineeringTools.FromCurrent(ma, low, high);
                return r.Ok ? ToolResult.Success("工程值=" + r.Value.ToString("0.######") + "，量程百分比=" + r.Percent.ToString("0.###") + "%") : ToolResult.Fail(r.Error);
            }
        }

        private class S7AddressTool : IAiTool
        {
            public string Name { get { return "eng.s7_address"; } }
            public string Description { get { return "解析 Siemens S7 地址并检查格式；args: {address}"; } }
            public bool RequiresConfirm { get { return false; } }
            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string address; a.TryGetValue("address", out address);
                var r = EngineeringTools.ParseS7Address(address);
                return r.Ok ? ToolResult.Success(r.Canonical + "：" + r.Description) : ToolResult.Fail(r.Error);
            }
        }

        private class ModbusFloatTool : IAiTool
        {
            public string Name { get { return "eng.modbus_float"; } }
            public string Description { get { return "把两个 Modbus 16 位寄存器解码为 Float32；args: {register1, register2, order=ABCD|BADC|CDAB|DCBA}"; } }
            public bool RequiresConfirm { get { return false; } }
            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                try
                {
                    string x, y, order; a.TryGetValue("register1", out x); a.TryGetValue("register2", out y); a.TryGetValue("order", out order);
                    var v = EngineeringTools.DecodeModbusFloat(EngineeringTools.ParseRegister(x), EngineeringTools.ParseRegister(y), order);
                    return ToolResult.Success("Float32=" + v.ToString("G9", System.Globalization.CultureInfo.InvariantCulture));
                }
                catch (Exception ex) { return ToolResult.Fail(ex.Message); }
            }
        }

        private class ValidationTemplateTool : IAiTool
        {
            public string Name { get { return "validation.template"; } }
            public string Description { get { return "生成制药设备 URS/DQ/IQ/OQ/PQ/CSV/Part11 Markdown 检查清单；args: {type, system, owner}"; } }
            public bool RequiresConfirm { get { return false; } }
            public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> a)
            {
                string type, system, owner; a.TryGetValue("type", out type); a.TryGetValue("system", out system); a.TryGetValue("owner", out owner);
                return ToolResult.Success(ValidationTemplates.Generate(type, system, owner));
            }
        }

        private static bool TryD(Dictionary<string, string> a, string key, out double value)
        {
            value = 0; string s;
            return a.TryGetValue(key, out s) && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
