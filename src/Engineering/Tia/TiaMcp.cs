using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskBox.Core;
using Message = System.Collections.Generic.Dictionary<string, object>;

namespace DeskBox.Engineering.Tia
{
    // One local stdio server per approved job. The MCP caller cannot redirect its scope,
    // modify the staged approval, or supply a new operation via tools/call arguments.
    internal static class TiaMcp
    {
        private const string Protocol = "2025-06-18";
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly Dictionary<string, string> Tools = new Dictionary<string, string>
        {
            { "discover", "tia_discover_projects" },
            { "read-open", "tia_read_open_project" },
            { "inspect", "tia_read_offline_copy" },
            { "compile", "tia_compile_offline_copy" },
            { "scale", "tia_insert_scale_and_compile_copy" },
            { "scl", "tia_insert_scl_and_compile_copy" },
            { "catalog", "tia_read_hardware_catalog" }
        };

        internal static IEnumerable<KeyValuePair<string, string>> Capabilities { get { return Tools.ToArray(); } }
        internal static bool RequiresOfflineApproval(string operation)
        {
            return operation == "compile" || operation == "scale" || operation == "scl";
        }

        private static object InputSchema()
        {
            return new { type = "object", properties = new { jobId = new { type = "string", pattern = "^[0-9a-f]{32}$" } },
                required = new[] { "jobId" }, additionalProperties = false };
        }

        private static object OutputSchema()
        {
            return new { type = "object", properties = new
                { jobId = new { type = "string" }, operation = new { type = "string" }, succeeded = new { type = "boolean" } },
                required = new[] { "jobId", "operation", "succeeded" }, additionalProperties = false };
        }

        private static object Get(Message message, string key)
        {
            object value;
            return message != null && message.TryGetValue(key, out value) ? value : null;
        }

        public static bool Run(string root, string id, TiaJob job, CancellationToken token)
        {
            var directory = Path.Combine(root, id);
            var tool = Tools[job.Operation];
            var auditPath = Path.Combine(directory, "mcp-audit.jsonl");
            var start = new ProcessStartInfo(typeof(TiaMcp).Assembly.Location, "--tia-mcp " + id + " \"" + root.TrimEnd('\\') + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, StandardOutputEncoding = Utf8,
                RedirectStandardError = true, StandardErrorEncoding = Utf8
            };
            var time = Stopwatch.StartNew();
            string step = "initialize";
            var status = "interrupted";
            try
            {
                using (var process = Process.Start(start))
                try
                {
                    var stderr = process.StandardError.ReadToEndAsync();
                    var initialized = Request(process, stderr, 1, step, new { protocolVersion = Protocol, capabilities = new { },
                        clientInfo = new { name = "DeskBox", version = "1.0" } }, token, 10000);
                    if (!Equals(Get(initialized, "protocolVersion"), Protocol) || Get(Get(initialized, "capabilities") as Message, "tools") == null)
                        throw new InvalidDataException("TIA MCP 协议版本不兼容，或未声明工具能力。");
                    Send(process.StandardInput, new { jsonrpc = "2.0", method = "notifications/initialized" });

                    step = "tools/list";
                    var listing = Request(process, stderr, 2, step, new { }, token, 10000);
                    var entries = Get(listing, "tools") as System.Collections.IEnumerable;
                    var match = entries == null ? null : entries.OfType<Message>().SingleOrDefault(x => Equals(Get(x, "name"), tool));
                    if (match == null) throw new InvalidDataException("TIA MCP 缺少当前作业工具 " + tool + "。");
                    if (Json.ToJson(Get(match, "inputSchema")) != Json.ToJson(InputSchema()) ||
                        Json.ToJson(Get(match, "outputSchema")) != Json.ToJson(OutputSchema()))
                        throw new InvalidDataException("TIA MCP 工具参数或回执结构不兼容：" + tool);

                    if (job.ProbeOnly)
                    {
                        process.StandardInput.Close();
                        if (!process.WaitForExit(5000) || process.ExitCode != 0)
                            throw new IOException("TIA MCP 探测进程未正常结束。");
                        status = "probe_succeeded";
                        return true;
                    }
                    step = "tools/call";
                    var response = Request(process, stderr, 3, step, new { name = tool, arguments = new { jobId = id } }, token,
                        job.Operation == "discover" || job.Operation == "read-open" ? 60000 : 300000);
                    var receipt = Get(response, "structuredContent") as Message;
                    var succeeded = Get(receipt, "succeeded");
                    if (receipt == null || receipt.Count != 3 || !Equals(Get(receipt, "jobId"), id) ||
                        !Equals(Get(receipt, "operation"), job.Operation) || !(succeeded is bool) ||
                        !Equals(Get(response, "isError"), !(bool)succeeded))
                        throw new InvalidDataException("TIA MCP 回执与当前工程作业不匹配。");
                    status = (bool)succeeded ? "tool_succeeded" : "tool_failed";
                    return (bool)succeeded;
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                    }
                }
            }
            catch (Exception e)
            {
                status = e.GetType().Name + " · " + e.Message;
                throw;
            }
            finally
            {
                File.AppendAllText(auditPath, Json.ToJson(new { utc = DateTime.UtcNow.ToString("O"), tool, step, jobId = id,
                    project = job.ProjectFile, cpu = job.Cpu, pid = job.PortalProcessId, block = job.Block,
                    sourceSha256 = job.ApprovedHash, status, elapsedMs = time.ElapsedMilliseconds }) + "\n", Utf8);
            }
        }

        private static Message Request(Process process, Task<string> stderr, int id, string method, object args, CancellationToken token, int timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            Send(process.StandardInput, new { jsonrpc = "2.0", id, method, @params = args });
            var pending = process.StandardOutput.ReadLineAsync();
            if (!pending.Wait(timeoutMs, token)) throw new TimeoutException("TIA MCP " + method + " 超时。请检查 Openness 授权提示或 TIA 状态。");
            if (pending.Result == null)
            {
                string detail = stderr.Wait(500, token) ? stderr.Result.Trim() : "";
                throw new IOException("TIA MCP 进程在返回 " + method + " 前退出。" + (detail.Length == 0 ? "" : "\r\n" + detail));
            }
            var message = Json.FromJson<Message>(pending.Result);
            if (!Equals(Get(message, "jsonrpc"), "2.0") || !Equals(Get(message, "id"), id))
                throw new InvalidDataException("TIA MCP JSON-RPC 回执无效：" + method);
            var error = Get(message, "error") as Message;
            if (error != null) throw new IOException("TIA MCP " + method + " · " + Get(error, "message"));
            var result = Get(message, "result") as Message;
            if (result == null) throw new InvalidDataException("TIA MCP 缺少结果：" + method);
            return result;
        }

        private static void Send(TextWriter output, object message)
        {
            output.WriteLine(Json.ToJson(message));
            output.Flush();
        }

        public static int Serve(string root, string jobId, TiaJob job, TextReader input, TextWriter output)
        {
            string line;
            var tool = Tools[job.Operation];
            bool initializing = false, ready = false;
            while ((line = input.ReadLine()) != null)
            {
                var request = Json.FromJson<Message>(line);
                var args = Get(request, "params") as Message;
                var id = Get(request, "id");
                var method = Get(request, "method") as string;
                if (!Equals(Get(request, "jsonrpc"), "2.0"))
                {
                    Error(output, null, -32600, "Invalid JSON-RPC request");
                    continue;
                }
                if (!initializing && id != null && method == "initialize")
                {
                    Send(output, new { jsonrpc = "2.0", id, result = new { protocolVersion = Protocol, capabilities = new { tools = new { } },
                        serverInfo = new { name = "DeskBox TIA Openness", version = "1.0" } } });
                    initializing = true;
                }
                else if (initializing && method == "notifications/initialized" && id == null) ready = true;
                else if (id == null) continue;
                else if (ready && method == "tools/list")
                    Send(output, new { jsonrpc = "2.0", id, result = new { tools = new[] { new { name = tool,
                        description = "Executes the locally staged " + job.Operation + " job on Siemens Openness.",
                        inputSchema = InputSchema(), outputSchema = OutputSchema() } } } });
                else if (ready && method == "tools/call" && !job.ProbeOnly)
                {
                    var arguments = Get(args, "arguments") as Message;
                    if (!Equals(Get(args, "name"), tool) || arguments == null || arguments.Count != 1 || !Equals(Get(arguments, "jobId"), jobId))
                    {
                        Error(output, id, -32602, "Tool or arguments do not match the staged job");
                        continue;
                    }
                    var succeeded = TiaOpenness.RunWorker(jobId, root, job) == 0;
                    var receipt = new { jobId, operation = job.Operation, succeeded };
                    Send(output, new { jsonrpc = "2.0", id, result = new { isError = !succeeded,
                        content = new[] { new { type = "text", text = Json.ToJson(receipt) } }, structuredContent = receipt } });
                    return succeeded ? 0 : 1;
                }
                else Error(output, id, -32601, "Unknown or unavailable MCP method");
            }
            return 0;
        }

        private static void Error(TextWriter output, object id, int code, string message)
        {
            Send(output, new { jsonrpc = "2.0", id, error = new { code, message } });
        }
    }
}
