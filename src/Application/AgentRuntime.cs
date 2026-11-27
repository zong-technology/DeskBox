using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Fs;

namespace DeskBox.AppServices
{
    public sealed class AgentRunRequest
    {
        public string SessionId = "", Model = "", Text = "";
        public ProjectContext Project;
        public AiProvider Provider;
        public int ToolRounds = 2;
        public Func<AiChatRequest> BuildPrompt;
        public Func<CancellationToken, ToolContext> CreateToolContext;
        public Func<string, string, bool> ConfirmTool;
    }

    public sealed class AgentRuntime
    {
        private const int MaxReplyCharacters = 1024 * 1024;
        private readonly AgentTaskStore store;

        public AgentRuntime(AgentTaskStore taskStore) { store = taskStore; }

        public AgentTask Run(AgentRunRequest request, CancellationToken cancellation, Action<string> started = null,
            Action<string> status = null, Action beginReply = null, Action<string> delta = null,
            Action<string, string, string> reply = null, Action<string, ToolResult> toolResult = null)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Text)) throw new ArgumentException("请输入任务内容。");
            string mode;
            var environment = AiEnvironment.Capture(out mode);
            var provider = request.Provider == null ? null : request.Provider.Clone();
            string input = request.Text.Trim();
            bool explicitlyLocal = input.StartsWith("工具 ", StringComparison.Ordinal) || input.StartsWith("本地查找 ", StringComparison.Ordinal);
            bool configured = provider != null && provider.Enabled;
            Uri endpoint;
            bool allowedLocally = mode != "local" || (configured && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out endpoint) &&
                AiEnvironment.Loopback(endpoint));
            bool offline = explicitlyLocal || !configured || !allowedLocally ||
                (request.Project != null && !request.Project.PermitsModel(provider));
            var task = new AgentTask
            {
                Id = Guid.NewGuid().ToString("N"), ProjectId = request.Project == null ? "" : request.Project.Id,
                SessionId = request.SessionId, Environment = mode, Mode = offline ? "local" : "model",
                ProviderId = offline ? "" : provider.Id,
                Model = offline ? "" : (string.IsNullOrWhiteSpace(request.Model) ? provider.Model : request.Model.Trim()),
                StartedAt = Now()
            };
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, environment))
            {
                var token = linked.Token;
                store.Begin(task);
                try
                {
                    if (started != null) started(task.Id);
                    token.ThrowIfCancellationRequested();
                    if (offline)
                        Local(request, task, explicitlyLocal ? "显式本地命令" : !configured ? "未配置模型" :
                            !allowedLocally ? "本地隔离限制" : "项目未授权该模型", token, status, beginReply, reply);
                    else
                        Model(request, provider, task, token, status, beginReply, delta, reply, toolResult);
                    if (task.State == "running") task.State = token.IsCancellationRequested ? "cancelled" : "succeeded";
                }
                catch (OperationCanceledException)
                {
                    task.State = task.ErrorKind = "cancelled";
                }
                catch (Exception error)
                {
                    task.State = "failed";
                    task.ErrorKind = error is IOException || error is UnauthorizedAccessException ? "storage" : "runtime";
                    if (reply != null) reply(null, "执行中止（" + task.ErrorKind + "） · " + error.GetType().Name, task.ErrorKind);
                }
                finally
                {
                    try
                    {
                        task.EndedAt = Now();
                        foreach (var step in task.Steps.Where(item => item.State == "running"))
                            Finish(step, task.State, task.ErrorKind);
                        store.Write(task);
                    }
                    finally { store.Release(task); }
                }
            }
            if (status != null) status(task.State == "succeeded" ? "完成" : task.State == "cancelled" ? "已取消" : "结束 · " + task.ErrorKind);
            return task;
        }

        public static ModelProbe ProbeModel(AiProvider provider, CancellationToken cancellation)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            var snapshot = provider.Clone();
            snapshot.MaxRetries = 0;
            var result = new ModelProbe { Model = snapshot.Model, Protocol = snapshot.Protocol };
            var watch = Stopwatch.StartNew();
            var request = new AiChatRequest { Model = snapshot.Model, Temperature = 0, MaxTokens = 16 };
            request.Messages.Add(new AiMessage { Role = "user", Content = "Reply with OK." });
            new AiClient(snapshot).ChatStream(request, cancellation, chunk =>
            {
                if (!string.IsNullOrEmpty(chunk.Delta)) result.StreamedText |= chunk.TextDelta;
                if (!string.IsNullOrEmpty(chunk.Error)) { result.Error = chunk.Error; result.ErrorKind = chunk.ErrorKind; }
                if (chunk.HttpStatus != 0) result.HttpStatus = chunk.HttpStatus;
                if (chunk.Done)
                {
                    result.TerminalFrame = chunk.TerminalFrame;
                    result.InputTokens = chunk.InputTokens;
                    result.OutputTokens = chunk.OutputTokens;
                }
            });
            result.ElapsedMs = watch.ElapsedMilliseconds;
            result.Ok = result.StreamedText && result.TerminalFrame && string.IsNullOrEmpty(result.Error);
            if (!result.Ok && string.IsNullOrEmpty(result.Error))
            {
                result.ErrorKind = "protocol";
                result.Error = "服务未返回可确认的文本流和结束帧。";
            }
            return result;
        }

        private void Model(AgentRunRequest request, AiProvider provider, AgentTask task, CancellationToken token,
            Action<string> status, Action beginReply, Action<string> delta, Action<string, string, string> reply,
            Action<string, ToolResult> output)
        {
            var client = new AiClient(provider);
            bool failedTool = false;
            int rounds = Math.Max(0, Math.Min(4, request.ToolRounds));
            for (int round = 0; round <= rounds; round++)
            {
                token.ThrowIfCancellationRequested();
                var step = BeginStep(task, "model.stream", task.Model);
                if (beginReply != null) beginReply();
                if (status != null) status("正在连接模型服务");
                var prompt = request.BuildPrompt == null
                    ? new AiChatRequest { Messages = new List<AiMessage> { new AiMessage { Content = request.Text } } }
                    : request.BuildPrompt();
                var tools = AgentEngineeringTools.Advertise(prompt, request.Project, provider);
                prompt.Model = task.Model;
                var received = new StringBuilder();
                string error = "", errorKind = "";
                int lastPublish = Environment.TickCount;
                bool limited = false;
                bool timedOut;
                using (var stream = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    stream.CancelAfter(TimeSpan.FromMinutes(15));
                    client.ChatStream(prompt, stream.Token, chunk =>
                    {
                        if (!string.IsNullOrEmpty(chunk.Stage) && status != null) status(chunk.Stage);
                        if (!string.IsNullOrEmpty(chunk.Error)) { error = chunk.Error; errorKind = chunk.ErrorKind; }
                        if (chunk.HttpStatus != 0) step.HttpStatus = chunk.HttpStatus;
                        if (chunk.Attempts != 0) step.Attempts = chunk.Attempts;
                        if (chunk.Done)
                        {
                            step.TerminalFrame = chunk.TerminalFrame;
                            step.InputTokens = chunk.InputTokens;
                            step.OutputTokens = chunk.OutputTokens;
                        }
                        if (string.IsNullOrEmpty(chunk.Delta)) return;
                        step.StreamedText |= chunk.TextDelta;
                        if (received.Length + (long)chunk.Delta.Length > MaxReplyCharacters)
                        {
                            limited = true;
                            stream.Cancel();
                            return;
                        }
                        if (received.Length == 0 && status != null) status("正在接收回复 · 流式输出");
                        received.Append(chunk.Delta);
                        if (delta != null && (received.Length == chunk.Delta.Length ||
                            unchecked((uint)(Environment.TickCount - lastPublish)) >= 50))
                        {
                            delta(received.ToString());
                            lastPublish = Environment.TickCount;
                        }
                    });
                    timedOut = stream.IsCancellationRequested && !limited;
                }
                if (token.IsCancellationRequested) { errorKind = "cancelled"; error = "已取消，已保留收到的内容。"; }
                else if (limited) { errorKind = "response_limit"; error = "模型回复超过长度上限。"; }
                else if (timedOut) { errorKind = "timeout"; error = "模型步骤超时，已保留收到的内容。"; }
                else if (string.IsNullOrEmpty(error) && (received.Length == 0 || !step.TerminalFrame))
                {
                    errorKind = "protocol";
                    error = received.Length == 0 ? "模型接口未返回文本。" : "模型流缺少结束帧，已保留收到的内容。";
                }

                string text = received.ToString();
                step.OutputCharacters = text.Length;
                var calls = new List<ToolCall>();
                if (string.IsNullOrEmpty(error) && !ToolCallParser.TryParseComplete(text, out calls))
                {
                    errorKind = "protocol";
                    error = "模型工具请求不完整或无法解析，本轮未执行任何工具。";
                }
                if (round == rounds && calls.Count > 0)
                {
                    errorKind = "tool_round_limit";
                    error = "已达到工具调用轮次上限，本轮工具未执行。";
                }
                var state = string.IsNullOrEmpty(error) ? "succeeded" : errorKind == "cancelled" ? "cancelled" : "failed";
                CompleteStep(task, step, state, errorKind);
                if (reply != null) reply(text, error, errorKind);
                if (state != "succeeded") { task.State = state; task.ErrorKind = errorKind; return; }
                if (calls.Count == 0)
                {
                    if (failedTool) { task.State = "partial"; task.ErrorKind = "tool_failed"; }
                    return;
                }
                foreach (var call in calls.Take(8))
                {
                    token.ThrowIfCancellationRequested();
                    var toolStep = BeginStep(task, "tool.call", ToolRegistry.Instance.Get(call.Name) != null ||
                        AgentEngineeringTools.Handles(call.Name) ? call.Name : "unregistered");
                    if (status != null) status("正在执行工具 · " + toolStep.Name);
                    string kind;
                    var result = ExecuteTool(request, call, token, status, tools, AgentEngineeringTools.PermitsValidation(provider), out kind);
                    toolStep.ReceiptId = result.ReceiptId;
                    if (kind == "cancelled")
                    {
                        CompleteStep(task, toolStep, "cancelled", kind);
                        if (output != null) output(toolStep.Name, result);
                        token.ThrowIfCancellationRequested();
                    }
                    toolStep.OutputCharacters = result.Ok ? (result.Output ?? "").Length : 0;
                    CompleteStep(task, toolStep, result.Ok ? "succeeded" : "failed", kind);
                    if (output != null) output(toolStep.Name, result);
                    failedTool |= !result.Ok;
                }
                if (calls.Count > 8)
                {
                    var skipped = BeginStep(task, "tool.call", "batch-limit");
                    CompleteStep(task, skipped, "failed", "tool_batch_limit");
                    failedTool = true;
                }
            }
        }

        private static ToolResult ExecuteTool(AgentRunRequest request, ToolCall call, CancellationToken token,
            Action<string> status, IReadOnlyList<McpAgentTool> tools, bool validationAllowed, out string errorKind)
        {
            var registry = ToolRegistry.Instance;
            var tool = registry.ToolsForProject(request.Project)
                .FirstOrDefault(item => string.Equals(item.Name, call.Name, StringComparison.OrdinalIgnoreCase));
            bool engineering = AgentEngineeringTools.Handles(call.Name);
            if ((!engineering && tool == null) || request.CreateToolContext == null)
            {
                errorKind = "permission";
                return ToolResult.Fail("当前环境或工程未授权调用该工具。");
            }
            if (tool != null && AgentEngineeringTools.NeedsConfirmation(tool))
            {
                if (status != null) status("等待人工确认 · " + tool.Name);
                if (request.ConfirmTool == null || !request.ConfirmTool(tool.Name, "AI 请求执行操作：" + tool.Name + "\n\n" + call.Describe()))
                {
                    errorKind = "permission";
                    return ToolResult.Fail("人工确认未授权执行。");
                }
            }
            var context = request.CreateToolContext(token);
            if (context == null || !ReferenceEquals(context.Project, request.Project))
                throw new InvalidOperationException("工具上下文与本轮工程不一致。");
            token.ThrowIfCancellationRequested();
            if (engineering)
                return AgentEngineeringTools.Execute(call, context, tools, validationAllowed, request.ConfirmTool, status, token, out errorKind);
            var result = registry.Invoke(tool.Name, call.Args, context);
            errorKind = result.Ok ? "" : "tool_error";
            return result;
        }

        private void Local(AgentRunRequest request, AgentTask task, string reason, CancellationToken token,
            Action<string> status, Action beginReply, Action<string, string, string> reply)
        {
            if (beginReply != null) beginReply();
            if (status != null) status(reason + " · 本地工具");
            string text = request.Text.Trim();
            bool command = text.StartsWith("工具 ", StringComparison.Ordinal);
            var step = BeginStep(task, command ? "local.tool" : "local.search", command ? "engineering-calculator" : "file-name-index");
            var context = request.CreateToolContext == null ? new ToolContext { Project = request.Project, Cancellation = token }
                : request.CreateToolContext(token);
            if (context == null || !ReferenceEquals(context.Project, request.Project)) throw new InvalidOperationException("本地工具上下文无效。");
            if (text.StartsWith("本地查找 ", StringComparison.Ordinal)) text = text.Substring(5).Trim();
            var result = command ? LocalCalculation(text.Substring(3), context) : Search(text, context);
            token.ThrowIfCancellationRequested();
            string kind = result.Ok ? "" : "local";
            var answer = result.Ok ? "本地工具 · " + reason + "\r\n" + result.Output : "";
            step.OutputCharacters = answer.Length;
            CompleteStep(task, step, result.Ok ? "succeeded" : "failed", kind);
            if (reply != null) reply(answer, result.Error, kind);
            if (!result.Ok) { task.State = "failed"; task.ErrorKind = kind; }
        }

        private static ToolResult LocalCalculation(string input, ToolContext context)
        {
            var call = ToolCallParser.ParseOne(input);
            if (call != null && call.Name == AgentValidationTools.Name) return AgentValidationTools.Run(call, context, context.Cancellation);
            if (call == null || !new[] { "eng.scale_4_20", "eng.s7_address", "eng.modbus_float" }.Contains(call.Name))
                return ToolResult.Fail("本地工具只接受工程计算命令。请输入 工具 后接包含 name 与 args 的 JSON 对象。");
            return ToolRegistry.Instance.Invoke(call.Name, call.Args, context);
        }

        private static ToolResult Search(string text, ToolContext context)
        {
            var project = context.Project;
            if (text.StartsWith("查找：", StringComparison.Ordinal) || text.StartsWith("查找:", StringComparison.Ordinal))
                text = text.Substring(3).Trim();
            if (string.IsNullOrWhiteSpace(text)) return ToolResult.Fail("请输入文件名关键词。");
            if (context.FileAccessEnabled != null && !context.FileAccessEnabled()) return ToolResult.Fail("AI 文件访问已关闭。");
            if (project == null && context.AllowedRoots.Count == 0 && context.AllowedFiles.Count == 0)
                return ToolResult.Fail("未配置 AI 服务商或可检索目录。可在设置添加服务商或授权目录。");
            var query = new SearchQuery { Text = text, Kind = "file", Limit = 20 };
            if (project != null) query.PathFilter = project.IncludesPath;
            else query.PathFilter = path => context.AllowedRoots.Any(root => ProjectContext.Under(path, root)) ||
                context.AllowedFiles.Any(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));
            var result = FileIndex.Instance.Search(query, context.Cancellation);
            var paths = result.Hits.Select(hit => hit.Entry.Path).Where(path =>
            {
                context.Cancellation.ThrowIfCancellationRequested();
                if (project != null)
                {
                    try { AiEnvironment.RequireLocalPath(path); }
                    catch (InvalidOperationException) { return false; }
                    return project.CanAccessPath(path) && File.Exists(path);
                }
                string error;
                return ToolRegistry.IsAllowed(context, path, out error) && File.Exists(path);
            }).ToArray();
            return ToolResult.Success("本地文件名检索\r\n" +
                (paths.Length == 0 ? "授权范围内没有匹配文件。" : string.Join("\r\n", paths)) +
                (string.IsNullOrEmpty(result.Message) ? "" : "\r\n" + result.Message));
        }

        private AgentStep BeginStep(AgentTask task, string kind, string name)
        {
            var step = new AgentStep { Kind = kind, Name = name, StartedAt = Now() };
            task.Steps.Add(step);
            store.Write(task);
            return step;
        }

        private void CompleteStep(AgentTask task, AgentStep step, string state, string kind)
        {
            Finish(step, state, kind);
            store.Write(task);
        }

        private static void Finish(AgentStep step, string state, string kind)
        {
            step.State = state;
            step.ErrorKind = kind;
            step.ElapsedMs = Math.Max(0, Now() - step.StartedAt);
        }

        private static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    }
}
