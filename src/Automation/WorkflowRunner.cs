using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;

namespace DeskBox.Automation
{
    /// <summary>工作流节点（v2）：触发 / 条件 / AI / 动作，按顺序执行，节点间可传递数据。</summary>
    public class WorkflowNode
    {
        public string Id = "";
        public string Type = "action";      // trigger | condition | ai | action
        public string Kind = "";            // 见 WorkflowRunner.NodeKinds
        public Dictionary<string, string> Params = new Dictionary<string, string>();
        public bool Enabled = true;
        public int Retries = 0;
        public int TimeoutMs = 60000;

        public string Get(string key, string def)
        {
            string v;
            return Params != null && Params.TryGetValue(key, out v) && v != null ? v : def;
        }

        public string Label
        {
            get
            {
                var k = string.IsNullOrEmpty(Kind) ? Type : Kind;
                switch (k)
                {
                    case "manual": return "手动触发";
                    case "schedule": return "定时触发";
                    case "fileexists": return "条件：文件存在";
                    case "textcontains": return "条件：文本包含";
                    case "resultok": return "条件：上一步成功";
                    case "taskcount": return "条件：任务数量";
                    case "summarize": return "AI：总结";
                    case "classify": return "AI：分类";
                    case "extract": return "AI：提取";
                    case "generate": return "AI：生成";
                    case "search": return "搜索文件";
                    case "read": return "读取文件";
                    case "write": return "保存文件";
                    case "task": return "创建任务";
                    case "report": return "生成报告";
                    case "notify": return "应用内通知";
                    case "runwf": return "调用其他工作流";
                    default: return k;
                }
            }
        }
    }

    /// <summary>单个节点的执行记录（规格 5.3：输入摘要、输出摘要、耗时、结果）。</summary>
    public class NodeRecord
    {
        public string NodeId = "";
        public string Type = "";
        public string Kind = "";
        public string Name = "";
        public bool Ok = false;
        public bool Skipped = false;
        public string InputSummary = "";
        public string OutputSummary = "";
        public string Error = "";
        public long ElapsedMs = 0;
        public int Attempts = 0;

        public string Describe()
        {
            var flag = Skipped ? "跳过" : (Ok ? "成功" : "失败");
            return "[" + flag + "] " + Name + " · " + ElapsedMs + "ms"
                 + (string.IsNullOrEmpty(Error) ? "" : " · " + Error);
        }
    }

    /// <summary>节点之间传递数据的上下文：上一步输出 {{prev}}、命名变量 {{var:name}}、文件列表。</summary>
    public class WorkflowContext
    {
        public string LastOutput = "";
        public readonly Dictionary<string, string> Vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Files = new List<string>();
        public bool LastOk = true;
        public StringBuilder Log = new StringBuilder();

        public string Render(string template)
        {
            if (string.IsNullOrEmpty(template)) return "";
            var s = template.Replace("{{prev}}", LastOutput ?? "");
            foreach (var kv in Vars) s = s.Replace("{{var:" + kv.Key + "}}", kv.Value ?? "");
            s = s.Replace("{{files}}", string.Join("\r\n", Files.ToArray()));
            s = s.Replace("{{files.count}}", Files.Count.ToString());
            s = s.Replace("{{date}}", DateTime.Now.ToString("yyyy-MM-dd"));
            s = s.Replace("{{time}}", DateTime.Now.ToString("HH:mm:ss"));
            s = s.Replace("{{datetime}}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            return s;
        }
    }

    /// <summary>
    /// 节点执行器：顺序执行、数据传递、失败策略、重试、超时、取消、并发限制、全程记录。
    /// AI 节点使用用户配置的真实服务商与模型，未配置时明确失败而不是编造内容。
    /// </summary>
    public class WorkflowRunner
    {
        private readonly Store _store;
        private readonly ProviderStore _providers;
        private readonly SemaphoreSlim _gate;
        private readonly object _lock = new object();
        private readonly Dictionary<string, CancellationTokenSource> _active = new Dictionary<string, CancellationTokenSource>();

        /// <summary>应用内通知（UI 注入）。</summary>
        public Action<string, string> Notify;
        /// <summary>高危操作确认（UI 注入）；为 null 时一律拒绝（fail-closed）。</summary>
        public Func<string, bool> ConfirmDanger;

        public WorkflowRunner(Store store, ProviderStore providers, int concurrency)
        {
            _store = store;
            _providers = providers;
            _gate = new SemaphoreSlim(Math.Max(1, Math.Min(4, concurrency)), Math.Max(1, Math.Min(4, concurrency)));
        }

        public bool IsRunning(string runId)
        {
            lock (_lock) return _active.ContainsKey(runId);
        }

        public int ActiveCount { get { lock (_lock) return _active.Count; } }

        public void Cancel(string runId)
        {
            lock (_lock)
            {
                CancellationTokenSource cts;
                if (_active.TryGetValue(runId, out cts))
                {
                    try { cts.Cancel(); } catch { }
                }
            }
        }

        public void CancelAll()
        {
            lock (_lock)
            {
                foreach (var kv in _active) { try { kv.Value.Cancel(); } catch { } }
            }
        }

        /// <summary>执行 v2 节点流水线。</summary>
        public WorkflowRun Run(Workflow wf, List<WorkflowNode> nodes, bool dryRun, string runId)
        {
            var run = new WorkflowRun
            {
                Id = string.IsNullOrEmpty(runId) ? ("r_" + Guid.NewGuid().ToString("N").Substring(0, 10)) : runId,
                WorkflowId = wf == null ? "" : wf.Id,
                WorkflowName = wf == null ? "" : wf.Name,
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                DryRun = dryRun,
                NodeRecords = new List<NodeRecord>()
            };

            bool acquired = false;
            var cts = new CancellationTokenSource();
            try
            {
                acquired = _gate.Wait(TimeSpan.FromSeconds(30));
                if (!acquired)
                {
                    run.Ok = false;
                    run.Message = "并发已达上限，本次未执行";
                    return run;
                }
                lock (_lock) { _active[run.Id] = cts; }

                var ctx = new WorkflowContext();
                int index = 0;
                foreach (var node in nodes)
                {
                    index++;
                    if (cts.IsCancellationRequested) break;
                    if (node == null || !node.Enabled) continue;

                    var rec = new NodeRecord
                    {
                        NodeId = node.Id,
                        Type = node.Type,
                        Kind = node.Kind,
                        Name = index + ". " + node.Label
                    };

                    // 触发节点只做判定，不产生副作用
                    if (string.Equals(node.Type, "trigger", StringComparison.OrdinalIgnoreCase))
                    {
                        rec.Ok = true;
                        rec.Skipped = true;
                        rec.OutputSummary = "触发节点（手动/定时由调度器处理）";
                        run.NodeRecords.Add(rec);
                        run.Steps.Add(rec.Describe());
                        continue;
                    }

                    int attempts = 0;
                    int maxAttempts = Math.Max(1, Math.Min(3, node.Retries + 1));
                    Exception lastEx = null;
                    bool ok = false;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string output = "";

                    while (attempts < maxAttempts && !cts.IsCancellationRequested)
                    {
                        attempts++;
                        sw.Restart();
                        try
                        {
                            ok = ExecuteNode(wf, node, ctx, dryRun, cts.Token, rec, out output);
                            lastEx = null;
                            break;
                        }
                        catch (OperationCanceledException)
                        {
                            lastEx = null;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastEx = ex;
                            ok = false;
                            if (attempts >= maxAttempts) break;
                            try { Thread.Sleep(200 * attempts); } catch { }
                        }
                    }
                    sw.Stop();

                    rec.Attempts = attempts;
                    rec.ElapsedMs = sw.ElapsedMilliseconds;
                    rec.Ok = ok;
                    rec.Error = lastEx != null ? Shorten(lastEx.Message, 200) : "";
                    if (string.IsNullOrEmpty(rec.OutputSummary)) rec.OutputSummary = Shorten(output, 400);
                    run.NodeRecords.Add(rec);
                    run.Steps.Add(rec.Describe());

                    if (cts.IsCancellationRequested)
                    {
                        run.Cancelled = true;
                        run.Ok = false;
                        run.Message = "已被取消（在节点 " + index + " 处中断）";
                        break;
                    }

                    if (!ok)
                    {
                        run.Ok = false;
                        var mode = _store != null ? _store.Settings.WorkflowFailMode : "stop";
                        if (string.Equals(mode, "skip", StringComparison.OrdinalIgnoreCase))
                        {
                            run.Steps.Add("节点失败，按设置跳过并继续");
                            ctx.LastOk = false;
                            continue;
                        }
                        run.Message = "节点失败，已停止：" + rec.Name + (string.IsNullOrEmpty(rec.Error) ? "" : "（" + rec.Error + "）");
                        break;
                    }

                    ctx.LastOk = true;
                    if (!string.IsNullOrEmpty(output)) ctx.LastOutput = output;
                }

                if (string.IsNullOrEmpty(run.Message))
                {
                    run.Ok = true;
                    run.Affected = run.NodeRecords.Count(r => r.Ok && !r.Skipped);
                    run.Message = (dryRun ? "预演完成，未执行实际写入。" : "执行完成。") + "成功节点 " + run.Affected + " 个";
                }
            }
            catch (Exception ex)
            {
                run.Ok = false;
                run.Message = "执行异常：" + ex.Message;
            }
            finally
            {
                lock (_lock) { _active.Remove(run.Id); }
                try { cts.Dispose(); } catch { }
                if (acquired) { try { _gate.Release(); } catch { } }
            }

            try
            {
                if (_store != null)
                    _store.Log(run.Ok ? "info" : "warn", "automation",
                        "工作流「" + run.WorkflowName + "」" + (run.DryRun ? "[预演]" : "") + "：" + run.Message
                        + " · 节点 " + run.NodeRecords.Count + " 个 · " + run.Id);
            }
            catch { }
            return run;
        }

        private bool ExecuteNode(Workflow wf, WorkflowNode node, WorkflowContext ctx, bool dryRun, CancellationToken ct, NodeRecord rec, out string output)
        {
            output = "";
            string kind = (node.Kind ?? "").ToLowerInvariant();

            switch (kind)
            {
                // ---------- 条件节点 ----------
                case "fileexists":
                {
                    var path = ctx.Render(node.Get("path", ""));
                    rec.InputSummary = "路径：" + Shorten(path, 160);
                    bool exists = File.Exists(path) || Directory.Exists(path);
                    output = exists ? "存在" : "不存在";
                    rec.OutputSummary = "文件" + output;
                    return exists;
                }
                case "textcontains":
                {
                    var needle = ctx.Render(node.Get("value", ""));
                    var hay = ctx.LastOutput ?? "";
                    bool hit = needle.Length > 0 && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                    rec.InputSummary = "查找：" + Shorten(needle, 80) + "（在上一节点输出中）";
                    rec.OutputSummary = hit ? "命中" : "未命中";
                    output = ctx.LastOutput;
                    return hit;
                }
                case "resultok":
                {
                    rec.InputSummary = "上一节点状态：" + (ctx.LastOk ? "成功" : "失败");
                    rec.OutputSummary = ctx.LastOk ? "通过" : "不通过";
                    output = ctx.LastOutput;
                    return ctx.LastOk;
                }
                case "taskcount":
                {
                    int min; int.TryParse(node.Get("value", "1"), out min);
                    int n = _store == null ? 0 : _store.Tasks.Count(t => !t.Done);
                    rec.InputSummary = "未完成任务数：" + n + "，期望 >" + min;
                    rec.OutputSummary = n > min ? "通过" : "不满足";
                    output = n.ToString();
                    return n > min;
                }

                // ---------- AI 节点 ----------
                case "summarize":
                case "classify":
                case "extract":
                case "generate":
                {
                    var input = ResolveInput(node, ctx);
                    rec.InputSummary = "输入 " + input.Length + " 字符";
                    if (string.IsNullOrWhiteSpace(input))
                    {
                        rec.OutputSummary = "输入为空，跳过";
                        return false;
                    }
                    if (dryRun)
                    {
                        rec.OutputSummary = "[预演] 将调用 AI（" + kind + "），未实际请求";
                        output = ctx.LastOutput;
                        return true;
                    }
                    if (_providers == null || _providers.DefaultProvider == null)
                        throw new InvalidOperationException("未配置 AI 服务商，无法执行 AI 节点（请在设置 → AI 服务商中添加）");
                    if (!_providers.DefaultProvider.Enabled)
                        throw new InvalidOperationException("默认 AI 服务商已禁用");

                    var ans = AskAi(_providers.DefaultProvider, kind, node, input, ct);
                    rec.OutputSummary = Shorten(ans, 400);
                    output = ans;
                    return true;
                }

                // ---------- 动作节点 ----------
                case "search":
                {
                    var root = ctx.Render(node.Get("root", ""));
                    var pattern = ctx.Render(node.Get("pattern", "*"));
                    if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                        throw new DirectoryNotFoundException("目录不存在：" + root);
                    var files = CollectFiles(root, pattern, node.Get("days", "0"), 200, ct);
                    ctx.Files = files;
                    rec.InputSummary = root + " / " + pattern;
                    rec.OutputSummary = "匹配 " + files.Count + " 个文件";
                    output = string.Join("\r\n", files.Take(5).ToArray());
                    return true;
                }
                case "read":
                {
                    var path = ResolveInput(node, ctx);
                    if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("未指定要读取的文件（可用 {{prev}} 或 {{var:path}}）");
                    // 输入可能是上一个「搜索」节点输出的多行文件列表：取第一个真实存在的文件
                    if (path.IndexOf('\n') >= 0 || path.IndexOf('\r') >= 0)
                    {
                        var first = path.Replace("\r\n", "\n").Split('\n')
                                        .Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && File.Exists(x));
                        if (first == null) throw new FileNotFoundException("列表中没有可读取的文件");
                        path = first;
                    }
                    if (!File.Exists(path)) throw new FileNotFoundException("文件不存在：" + path);
                    int max = 200000;
                    int.TryParse(node.Get("maxChars", "200000"), out max);
                    var r = DeskBox.Fs.PreviewService.Produce(path, max);
                    if (r != null && !string.IsNullOrEmpty(r.Error)) throw new InvalidOperationException(r.Error);
                    if (r != null && (r.Kind == "image" || r.Kind == "unsupported"))
                        throw new InvalidOperationException("不支持读取该类型：" + r.Kind);
                    output = r == null ? "" : (r.Text ?? "");
                    rec.InputSummary = Path.GetFileName(path);
                    rec.OutputSummary = "读取 " + output.Length + " 字符";
                    ctx.Vars["path"] = path;
                    return !string.IsNullOrEmpty(output);
                }
                case "write":
                case "report":
                {
                    var path = ctx.Render(node.Get("path", ""));
                    if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("未指定保存路径");
                    var content = ResolveInput(node, ctx);
                    if (string.IsNullOrEmpty(content)) content = ctx.LastOutput ?? "";
                    var header = node.Get("header", "");
                    if (!string.IsNullOrEmpty(header)) content = ctx.Render(header) + "\r\n\r\n" + content;
                    if (dryRun)
                    {
                        rec.OutputSummary = "[预演] 将写入 " + path + "（" + content.Length + " 字符）";
                        output = content;
                        return true;
                    }
                    if (!Confirm("将写入文件：\r\n" + path + "\r\n\r\n内容 " + content.Length + " 字符"))
                        throw new InvalidOperationException("未获确认，拒绝写入（需要用户在确认框中允许）");
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(path, content, new UTF8Encoding(false));
                    rec.OutputSummary = "已写入 " + path;
                    output = content;
                    try { if (_store != null) _store.Log("info", "automation", "工作流写入文件：" + path); } catch { }
                    return true;
                }
                case "task":
                {
                    var title = ctx.Render(node.Get("title", ""));
                    if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("任务标题为空");
                    if (dryRun)
                    {
                        rec.OutputSummary = "[预演] 将创建任务：" + title;
                        return true;
                    }
                    var t = new TaskItem
                    {
                        Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                        Title = title,
                        Priority = node.Get("priority", "normal"),
                        Due = ctx.Render(node.Get("due", "")),
                        Notes = Shorten(ctx.LastOutput, 500),
                        CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                    };
                    _store.Tasks.Add(t);
                    _store.SaveTasksOnly();
                    rec.OutputSummary = "已创建任务：" + title;
                    try { _store.Log("info", "task", "工作流创建任务：" + title); } catch { }
                    return true;
                }
                case "notify":
                {
                    var text = ctx.Render(node.Get("text", "{{prev}}"));
                    rec.OutputSummary = Shorten(text, 200);
                    if (!dryRun)
                    {
                        var h = Notify;
                        if (h != null) { try { h(wf == null ? "自动化" : wf.Name, text); } catch { } }
                        try { if (_store != null) _store.Log("info", "automation", "工作流通知：" + Shorten(text, 120)); } catch { }
                    }
                    output = text;
                    return true;
                }
                case "runwf":
                {
                    var name = ctx.Render(node.Get("name", ""));
                    rec.InputSummary = "目标工作流：" + name;
                    if (dryRun)
                    {
                        rec.OutputSummary = "[预演] 将调用工作流：" + name;
                        return true;
                    }
                    rec.OutputSummary = "子工作流需在自动化页面手动执行（避免递归），已跳过";
                    return true;
                }
                default:
                    throw new InvalidOperationException("未知节点类型：" + kind);
            }
        }

        private string ResolveInput(WorkflowNode node, WorkflowContext ctx)
        {
            var raw = node.Get("input", "");
            if (!string.IsNullOrEmpty(raw)) return ctx.Render(raw);
            return ctx.LastOutput ?? "";
        }

        private bool Confirm(string text)
        {
            var c = ConfirmDanger;
            if (c == null) return false;      // fail-closed：没有确认通道就拒绝
            try { return c(text); } catch { return false; }
        }

        private string AskAi(AiProvider provider, string kind, WorkflowNode node, string input, CancellationToken ct)
        {
            var prompt = node.Get("prompt", "");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                switch (kind)
                {
                    case "summarize": prompt = "请用中文总结下面的内容，输出要点列表，不超过 200 字。"; break;
                    case "classify": prompt = "请把下面的内容归类，输出类别名称与一句理由。"; break;
                    case "extract": prompt = "请从下面的内容中提取关键信息（人名/日期/金额/结论等），用列表输出。"; break;
                    default: prompt = node.Get("instruction", "请根据下面的内容生成结果。"); break;
                }
            }
            var req = new AiChatRequest
            {
                Model = string.IsNullOrWhiteSpace(provider.Model) ? "default" : provider.Model,
                Temperature = 0.3,
                MaxTokens = 0,
                Messages = new List<AiMessage>
                {
                    new AiMessage { Role = "system", Content = "你是 DeskBox 自动化流程中的文本处理节点。只输出处理结果，不要解释过程。" },
                    new AiMessage { Role = "user", Content = prompt + "\r\n\r\n---\r\n" + input }
                }
            };
            var client = new AiClient(provider);
            var r = client.Chat(req, ct);
            if (!r.Ok) throw new InvalidOperationException("AI 调用失败[" + r.ErrorKind + "]：" + Shorten(r.Error, 160));
            return r.Content ?? "";
        }

        private List<string> CollectFiles(string root, string pattern, string daysStr, int limit, CancellationToken ct)
        {
            var list = new List<string>();
            int days; int.TryParse(daysStr, out days);
            var cutoff = days > 0 ? DateTime.Now.AddDays(-days) : DateTime.MinValue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); }
            catch { files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly); }
            foreach (var f in files)
            {
                if (ct.IsCancellationRequested) break;
                if (list.Count >= limit) break;
                if (cutoff > DateTime.MinValue)
                {
                    DateTime mt;
                    try { mt = File.GetLastWriteTime(f); } catch { continue; }
                    if (mt < cutoff) continue;
                }
                list.Add(f);
            }
            return list;
        }

        private static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }

    internal static class NodeKinds
    {
        public static readonly string[] All = new[]
        {
            "manual", "schedule",
            "fileexists", "textcontains", "resultok", "taskcount",
            "summarize", "classify", "extract", "generate",
            "search", "read", "write", "task", "report", "notify", "runwf"
        };
    }
}
