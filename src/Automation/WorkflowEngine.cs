using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Automation
{
    public class WorkflowStep
    {
        public string Type = "";
        public Dictionary<string, string> Params = new Dictionary<string, string>();
        public string Get(string key, string def)
        {
            string v;
            return Params != null && Params.TryGetValue(key, out v) && v != null ? v : def;
        }
    }

    public class Workflow
    {
        public string Id = "";
        public string Name = "";
        public bool Enabled = true;
        public string Trigger = "manual";     // manual | startup | interval
        public int IntervalMinutes = 60;
        public List<WorkflowStep> Conditions = new List<WorkflowStep>();
        public List<WorkflowStep> Actions = new List<WorkflowStep>();
        public long CreatedAt = 0;
        public long LastRunAt = 0;
        public string LastResult = "";
        // v2：节点流水线（为空时走旧的 Conditions+Actions 逻辑，保证旧配置可用）
        public List<WorkflowNode> Nodes = new List<WorkflowNode>();
        public string Description = "";
        public bool UseNodes = false;
    }

    public class WorkflowRun
    {
        public string Id = "";
        public string WorkflowId = "";
        public string WorkflowName = "";
        public long Ts = 0;
        public bool Ok = false;
        public int Affected = 0;
        public string Message = "";
        public bool DryRun = false;
        public List<string> Steps = new List<string>();
        public bool Cancelled = false;
        public List<NodeRecord> NodeRecords = new List<NodeRecord>();
        public long ElapsedMs = 0;
    }

    /// <summary>声明式自动化：触发器 → 条件 → 动作，全部真实执行并记录；删除类动作只能进备份目录。</summary>
    public class WorkflowEngine
    {
        private readonly Store _store;
        private Timer _timer;
        private Timer _startupTimer;
        private readonly object _lock = new object();

        public List<Workflow> Workflows = new List<Workflow>();
        public List<WorkflowRun> Runs = new List<WorkflowRun>();
        public event Action<WorkflowRun> RunCompleted;

        private static string WfFile { get { return Path.Combine(AppPaths.DataDir, "workflows.json"); } }
        private static string RunFile { get { return Path.Combine(AppPaths.DataDir, "workflow-runs.jsonl"); } }

        /// <summary>v2 节点执行器（AI 节点需要真实服务商）。</summary>
        public WorkflowRunner Runner;
        /// <summary>应用内通知（UI 注入）。</summary>
        public Action<string, string> Notify;
        /// <summary>高危操作确认（UI 注入）。</summary>
        public Func<string, bool> ConfirmDanger;

        public WorkflowEngine(Store store)
        {
            _store = store;
            Load();
            try
            {
                var providers = new DeskBox.Ai.ProviderStore();
                int conc = store != null ? Math.Max(1, store.Settings.WorkflowConcurrency) : 1;
                Runner = new WorkflowRunner(store, providers, conc);
            }
            catch (Exception ex) { AppLog.Write("warn", "automation", "工作流执行器初始化失败: " + ex.Message); }
        }

        /// <summary>v2 执行：按 Nodes 顺序执行，支持数据传递/取消/超时/失败策略/重试。</summary>
        public WorkflowRun RunV2(string workflowId, bool dryRun)
        {
            var wf = Workflows.FirstOrDefault(w => w.Id == workflowId);
            if (wf == null)
                return new WorkflowRun { Id = "r_none", Ok = false, Message = "工作流不存在：" + workflowId, Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
            if (wf.Nodes == null || wf.Nodes.Count == 0)
                return Run(workflowId, dryRun);          // 旧配置回退
            if (Runner == null)
            {
                try
                {
                    var providers = new DeskBox.Ai.ProviderStore();
                    Runner = new WorkflowRunner(_store, providers, 1);
                }
                catch (Exception ex) { return new WorkflowRun { Id = "r_err", Ok = false, Message = "执行器不可用：" + ex.Message }; }
            }
            Runner.Notify = Notify;
            Runner.ConfirmDanger = ConfirmDanger;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var run = Runner.Run(wf, wf.Nodes, dryRun, null);
            sw.Stop();
            run.ElapsedMs = sw.ElapsedMilliseconds;
            wf.LastRunAt = run.Ts;
            wf.LastResult = run.Message;
            Runs.Insert(0, run);
            if (Runs.Count > 500) Runs = Runs.Take(500).ToList();
            Save();
            try { Json.AppendLine(RunFile, run); } catch { }
            var h = RunCompleted;
            if (h != null) h(run);
            return run;
        }

        /// <summary>取消正在运行的工作流。</summary>
        public void Cancel(string runId)
        {
            if (Runner != null) Runner.Cancel(runId);
        }

        public void CancelAll()
        {
            if (Runner != null) Runner.CancelAll();
        }

        /// <summary>复制工作流（含节点）。</summary>
        public Workflow Copy(string workflowId)
        {
            var src = Workflows.FirstOrDefault(w => w.Id == workflowId);
            if (src == null) return null;
            var copy = new Workflow
            {
                Id = "wf_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = src.Name + " 副本",
                Description = src.Description,
                Enabled = src.Enabled,
                Trigger = src.Trigger,
                IntervalMinutes = src.IntervalMinutes,
                CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                UseNodes = src.UseNodes,
                Conditions = CloneSteps(src.Conditions),
                Actions = CloneSteps(src.Actions),
                Nodes = CloneNodes(src.Nodes)
            };
            Workflows.Insert(0, copy);
            Save();
            return copy;
        }

        private static List<WorkflowStep> CloneSteps(List<WorkflowStep> src)
        {
            var list = new List<WorkflowStep>();
            if (src == null) return list;
            foreach (var s in src)
                list.Add(new WorkflowStep
                {
                    Type = s.Type,
                    Params = s.Params == null ? new Dictionary<string, string>() : new Dictionary<string, string>(s.Params)
                });
            return list;
        }

        private static List<WorkflowNode> CloneNodes(List<WorkflowNode> src)
        {
            var list = new List<WorkflowNode>();
            if (src == null) return list;
            foreach (var n in src)
                list.Add(new WorkflowNode
                {
                    Id = "n_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Type = n.Type, Kind = n.Kind,
                    Enabled = n.Enabled, Retries = n.Retries, TimeoutMs = n.TimeoutMs,
                    Params = n.Params == null ? new Dictionary<string, string>() : new Dictionary<string, string>(n.Params)
                });
            return list;
        }

        public void Load()
        {
            var saved = Json.Load<List<Workflow>>(WfFile, null);
            Workflows = saved ?? new List<Workflow>();
            Runs = Json.ReadLines<WorkflowRun>(RunFile, 200);
        }

        public void Save()
        {
            Json.Save(WfFile, Workflows);
        }

        public Workflow Add(string name, string trigger, int intervalMinutes)
        {
            var w = new Workflow
            {
                Id = "wf_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = string.IsNullOrWhiteSpace(name) ? "未命名工作流" : name,
                Trigger = trigger,
                IntervalMinutes = Math.Max(1, intervalMinutes),
                CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                Enabled = true
            };
            Workflows.Add(w);
            Save();
            return w;
        }

        public void Remove(string id)
        {
            Workflows.RemoveAll(w => w.Id == id);
            Save();
        }

        /// <summary>执行（dryRun=true 只出计划，不动文件）。</summary>
        public WorkflowRun Run(string workflowId, bool dryRun)
        {
            var wf = Workflows.FirstOrDefault(w => w.Id == workflowId);
            var run = new WorkflowRun
            {
                Id = "run_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                WorkflowId = workflowId,
                WorkflowName = wf == null ? "?" : wf.Name,
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                DryRun = dryRun
            };
            if (wf == null) { run.Message = "工作流不存在"; Finish(wf, run); return run; }

            try
            {
                // 1) 收集候选文件（所有动作共用 root/pattern/days）
                string root = null, pattern = "*", daysStr = "0";
                if (wf.Actions.Count > 0) { root = wf.Actions[0].Get("root", null); pattern = wf.Actions[0].Get("pattern", "*"); daysStr = wf.Actions[0].Get("days", "0"); }
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    run.Message = "目录不存在：" + root;
                    Finish(wf, run); return run;
                }
                int days; int.TryParse(daysStr, out days);
                var files = CollectFiles(root, pattern, days);
                run.Steps.Add("匹配 " + files.Count + " 个文件（" + root + " / " + pattern + "）");

                // 2) 条件
                foreach (var c in wf.Conditions)
                {
                    bool pass = true;
                    switch ((c.Type ?? "").ToLowerInvariant())
                    {
                        case "filecount>":
                            int min; int.TryParse(c.Get("value", "1"), out min);
                            pass = files.Count > min;
                            run.Steps.Add("条件 fileCount>" + min + " → " + (pass ? "通过" : "不满足(" + files.Count + ")"));
                            break;
                        case "size>":
                            long mb; long.TryParse(c.Get("value", "0"), out mb);
                            long total = files.Sum(f => SafeSize(f));
                            pass = total > mb * 1024L * 1024;
                            run.Steps.Add("条件 总大小>" + mb + "MB → " + (pass ? "通过" : "不满足"));
                            break;
                        case "extis":
                            var want = c.Get("value", "").ToLowerInvariant();
                            pass = files.Any(f => Path.GetExtension(f).ToLowerInvariant() == want);
                            run.Steps.Add("条件 存在 " + want + " → " + (pass ? "通过" : "不满足"));
                            break;
                    }
                    if (!pass) { run.Message = "条件不满足，未执行动作"; Finish(wf, run); return run; }
                }

                // 3) 动作（单次最多 200 个文件）
                if (files.Count > 200) { files = files.Take(200).ToList(); run.Steps.Add("已截断到 200 个文件"); }
                int affected = 0;
                foreach (var action in wf.Actions)
                {
                    string type = (action.Type ?? "").ToLowerInvariant();
                    string target = action.Get("targetDir", "");
                    foreach (var f in files)
                    {
                        try
                        {
                            if (type == "log") { affected++; continue; }
                            if (type == "move" || type == "copy")
                            {
                                if (string.IsNullOrEmpty(target)) { run.Steps.Add("目标目录为空，跳过"); break; }
                                Directory.CreateDirectory(target);
                                var dst = Path.Combine(target, Path.GetFileName(f));
                                if (File.Exists(dst)) dst = Path.Combine(target, Path.GetFileNameWithoutExtension(f) + "_" + DateTime.Now.ToString("HHmmss") + Path.GetExtension(f));
                                if (!dryRun)
                                {
                                    if (type == "move") File.Move(f, dst); else File.Copy(f, dst, false);
                                }
                                affected++;
                            }
                            else if (type == "backup" || type == "deletetobackup")
                            {
                                var dir = Path.Combine(AppPaths.BackupDir, "wf_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                                if (!dryRun)
                                {
                                    Directory.CreateDirectory(dir);
                                    File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                                    if (type == "deletetobackup") File.Delete(f);
                                }
                                affected++;
                            }
                            else if (type == "rename")
                            {
                                var find = action.Get("find", "");
                                var repl = action.Get("replace", "");
                                var newName = Path.GetFileNameWithoutExtension(f).Replace(find, repl) + Path.GetExtension(f);
                                if (!dryRun && newName != Path.GetFileName(f))
                                {
                                    var dst = Path.Combine(Path.GetDirectoryName(f), newName);
                                    if (!File.Exists(dst)) File.Move(f, dst);
                                }
                                affected++;
                            }
                        }
                        catch (Exception ex)
                        {
                            run.Steps.Add("失败：" + Path.GetFileName(f) + " - " + ex.Message);
                        }
                    }
                    run.Steps.Add((dryRun ? "[计划] " : "") + type + " → " + affected + " 个文件");
                }

                run.Affected = affected;
                run.Ok = true;
                run.Message = (dryRun ? "预演完成，未改动文件。将影响 " : "执行完成，影响 ") + affected + " 个文件";
                if (dryRun && affected == 0) run.Message = "预演完成：没有匹配到文件";
            }
            catch (Exception ex)
            {
                run.Ok = false;
                run.Message = "执行异常：" + ex.Message;
            }

            Finish(wf, run);
            return run;
        }

        private void Finish(Workflow wf, WorkflowRun run)
        {
            if (wf != null)
            {
                wf.LastRunAt = run.Ts;
                wf.LastResult = run.Message;
                Save();
            }
            Runs.Insert(0, run);
            if (Runs.Count > 500) Runs = Runs.Take(500).ToList();
            Json.AppendLine(RunFile, run);
            try { _store.Log(run.Ok ? "info" : "error", "system", "工作流「" + run.WorkflowName + "」：" + run.Message); } catch { }
            var h = RunCompleted;
            if (h != null) h(run);
        }

        private static long SafeSize(string p)
        {
            try { return new FileInfo(p).Length; } catch { return 0; }
        }

        private static List<string> CollectFiles(string root, string pattern, int days)
        {
            var list = new List<string>();
            try
            {
                var opt = SearchOption.TopDirectoryOnly;
                foreach (var f in Directory.EnumerateFiles(root, string.IsNullOrEmpty(pattern) ? "*" : pattern, opt))
                {
                    if (days > 0)
                    {
                        try { if (File.GetLastWriteTime(f) > DateTime.Now.AddDays(-days)) continue; } catch { }
                    }
                    list.Add(f);
                }
            }
            catch { }
            return list;
        }

        public void StartScheduler()
        {
            StopScheduler();
            _timer = new Timer(OnTick, null, 30000, 60000);
            // 启动触发器：延迟 5 秒执行一次
            var startup = Workflows.Where(w => w.Enabled && w.Trigger == "startup").ToList();
            if (startup.Count > 0)
            {
                _startupTimer = new Timer(delegate
                {
                    foreach (var w in startup) { try { Run(w.Id, false); } catch { } }
                    var timer = _startupTimer;
                    _startupTimer = null;
                    if (timer != null) timer.Dispose();
                }, null, 5000, Timeout.Infinite);
            }
        }

        public void StopScheduler()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
            if (_startupTimer != null) { _startupTimer.Dispose(); _startupTimer = null; }
        }

        private void OnTick(object state)
        {
            lock (_lock)
            {
                foreach (var wf in Workflows.Where(w => w.Enabled && w.Trigger == "interval").ToList())
                {
                    if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - wf.LastRunAt < wf.IntervalMinutes * 60000L) continue;
                    try { Run(wf.Id, false); } catch { }
                }
            }
        }
    }
}
