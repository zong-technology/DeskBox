using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskBox.Ai;
using DeskBox.AppServices;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Engineering;

internal static class V5RuntimeAccept
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string FixtureToken = "fixture-http-bearer";
    private const string PrivateValue = "fixture-arguments-not-for-receipts";
    private const string Calc = "{\"name\":\"eng.scale_4_20\",\"args\":{\"mA\":12,\"low\":0,\"high\":100}}";
    private static int checks;

    public static int Main(string[] args)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "deskbox-v5-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            AppPaths.DataDir = DeskBox.Store.DataDir = sandbox;
            AiEnvironment.Select("network");
            using (var fixture = new Fixture())
            {
                TestAgent(sandbox, fixture);
                TestMcp(sandbox, fixture, args);
                fixture.AssertHealthy();
            }
            Console.WriteLine("PASS " + checks + " checks with loopback HTTP/SSE and a separate stdio fixture.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(sandbox, true); }
    }

    private static void TestAgent(string root, Fixture fixture)
    {
        var store = new AgentTaskStore(root);
        var agent = new AgentRuntime(store);
        var projectStore = new WorkspaceStore();
        var folder = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var project = projectStore.Add("A", folder, "PLC");
        projectStore.SetCurrent(project.Id);
        var scope = projectStore.Context;
        var model = new AiProvider { Id = "fixture", Model = "multi", BaseUrl = fixture.Base + "/v1", TimeoutMs = 2000, MaxRetries = 0 };
        var recorded = new List<AiMessage> { new AiMessage { Content = "fixture-model-input" } };
        var reports = new List<ToolResult>();
        var request = new AgentRunRequest
        {
            Project = scope, SessionId = "fixture-session", Text = "fixture question", Provider = model,
            BuildPrompt = () => new AiChatRequest { Messages = recorded.ToList() },
            CreateToolContext = token => new ToolContext { Project = scope, Cancellation = token }
        };
        var run = agent.Run(request, None, toolResult: (name, result) =>
        {
            reports.Add(result);
            recorded.Add(new AiMessage { Content = result.Output });
        });
        Check(run.State == "succeeded" && run.Steps.Select(step => step.Kind).SequenceEqual(new[] { "model.stream", "tool.call", "model.stream" }),
            "model → real local tool → model phases are journaled");
        Check(reports.Count == 1 && reports[0].Ok && reports[0].Output.Contains("工程值=50") && fixture.AgentSawTool,
            "registered calculator output reached the second real HTTP request");
        Check(run.Steps[2].HttpStatus == 200 && run.Steps[2].InputTokens == 11 &&
            run.Steps[2].OutputTokens == 3 && run.Steps[2].StreamedText && run.Steps[2].TerminalFrame, "verified stream and provider-reported usage");
        Check(store.List(scope.Id, "network").Single().SessionId == "fixture-session" &&
            store.List("other-project", "network").Count == 0 && store.List(scope.Id, "local").Count == 0,
            "agent history is scoped by stable project and environment");
        Check(AgentRuntime.ProbeModel(model, None).Ok, "probe verifies text deltas and completion over HTTP");

        request.BuildPrompt = null;
        model.Model = "incomplete";
        reports.Clear();
        run = agent.Run(request, None, toolResult: (name, result) => reports.Add(result));
        Check(run.State == "failed" && run.ErrorKind == "protocol" && reports.Count == 0 && run.Steps[0].OutputCharacters > 0,
            "unterminated model stream keeps the text and never executes its tool request");

        model.Model = "denied";
        string target = Path.Combine(folder, "no-write.txt");
        fixture.WriteTarget = target;
        request.ConfirmTool = (name, text) => false;
        request.BuildPrompt = () => new AiChatRequest { Messages = recorded.ToList() };
        recorded.Clear();
        recorded.Add(new AiMessage { Content = "request" });
        run = agent.Run(request, None, toolResult: (name, result) => recorded.Add(new AiMessage { Content = "denied-result" }));
        Check(run.State == "partial" && run.Steps.Any(step => step.ErrorKind == "permission") && !File.Exists(target),
            "a denied tool stays denied even if the model writes a final answer");
        request.BuildPrompt = null;

        model.Model = "retry";
        model.MaxRetries = 1;
        run = agent.Run(request, None);
        Check(run.State == "succeeded" && run.Steps[0].Attempts == 2 && fixture.RetryRequests == 2, "finite retry counts the actual model requests");

        var responses = model.Clone();
        responses.Protocol = "openai-responses";
        responses.Model = "responses";
        request.Provider = responses;
        run = agent.Run(request, None);
        Check(run.State == "succeeded" && run.Steps[0].InputTokens == 8 && run.Steps[0].OutputTokens == 2,
            "Responses protocol uses its native endpoint, text event and usage fields");
        request.Provider = model;

        model.MaxRetries = 0;
        model.Model = "large";
        run = agent.Run(request, None);
        Check(run.ErrorKind == "response_limit" && run.Steps[0].OutputCharacters > 0, "large model response is bounded and classified");

        model.Model = "silent";
        model.TimeoutMs = 1000;
        using (var cancellation = new CancellationTokenSource())
        {
            fixture.ModelWaiting.Reset();
            var pending = Task.Run(() => agent.Run(request, cancellation.Token));
            Check(fixture.ModelWaiting.Wait(3500), "active model network request reached the loopback endpoint");
            Check(store.List(scope.Id, "network").First().State == "running", "active lease is visible during the network operation");
            cancellation.Cancel();
            Check(pending.Wait(5000) && pending.Result.State == "cancelled", "cancellation aborts the live HTTP stream");
        }
        fixture.ModelWaiting.Reset();
        run = agent.Run(request, None);
        Check(run.State == "failed" && run.ErrorKind == "timeout", "configured idle timeout is reported on a live HTTP request");

        request.Text = "工具 " + Calc;
        run = agent.Run(request, None);
        Check(run.State == "succeeded" && run.Mode == "local" && run.Steps[0].Kind == "local.tool",
            "explicit deterministic tool lowers to the local path with a model configured");
        string currentId = run.Id;
        string path = Directory.GetFiles(Path.Combine(root, "agent-tasks"), currentId + ".json", SearchOption.AllDirectories).Single();
        File.WriteAllText(path, "broken");
        Check(new AgentTaskStore(root).List(scope.Id, "network").Any(item => item.Id == currentId && item.State == "interrupted"),
            "corrupt running receipt is projected as interrupted from its atomic previous snapshot");
        File.Delete(path);
        Check(new AgentTaskStore(root).List(scope.Id, "network").Any(item => item.Id == currentId), "missing main record is recovered from the backup");
        Check(!Directory.GetFiles(Path.Combine(root, "agent-tasks"), "*.json*", SearchOption.AllDirectories)
            .Any(file => File.ReadAllText(file).Contains("fixture-model-input")), "agent receipts omit prompts and tool bodies");
        Check(new AgentTaskStore(root).List(scope.Id, "network", 2).Count == 2, "history query respects its result bound");

        request.Provider = null;
        request.Text = "工具 {\"name\":\"fs.write\",\"args\":{}}";
        run = agent.Run(request, None);
        Check(run.State == "failed" && run.Mode == "local", "no-provider mode rejects an unapproved tool command");
        request.Project = null;
        request.CreateToolContext = null;
        request.Text = "find a file";
        run = agent.Run(request, None);
        Check(run.State == "failed" && run.Mode == "local", "no-provider mode refuses unbound file search without roots");
        Throws<ArgumentException>(() => agent.Run(new AgentRunRequest(), None), "blank agent request rejected");
    }

    private static void TestMcp(string root, Fixture fixture, string[] args)
    {
        var gateway = new McpGateway(root);
        bool recovered;
        Check(gateway.Servers("A", out recovered).Count == 0 && !recovered, "first-run MCP registry is empty");
        var server = gateway.SaveServer("A", new McpServer
        {
            Name = "HTTP fixture", Address = fixture.Base + "/mcp", TimeoutMs = 1500, Enabled = true
        });
        Check(!server.Enabled && gateway.Discover("A", server.Id, "", None).Receipt.State == "denied" && fixture.Initializes == 0,
            "new registry is disabled and the deny occurs before network");
        Throws<ArgumentException>(() => gateway.SaveServer("A", new McpServer { Name = "invalid", Address = "http://example.invalid/mcp" }),
            "cleartext non-loopback MCP registration is rejected");
        Throws<ArgumentException>(() => gateway.SaveServer("A", new McpServer { Name = "invalid", Address = fixture.Base + "/mcp?token=x" }),
            "credential query is rejected");
        server.Enabled = true;
        var stale = server;
        server = gateway.SaveServer("A", server);
        Throws<InvalidOperationException>(() => gateway.SaveServer("A", stale), "stale registration update is rejected");
        Throws<UnauthorizedAccessException>(() => gateway.Discover("B", server.Id, FixtureToken, None), "cross-project discovery denied");
        Check(gateway.Servers("B", out recovered).Count == 0 && gateway.Servers("", out recovered).Count == 0,
            "cross-project registry is empty");
        Check(gateway.Discover("A", server.Id, "invalid", None).Receipt.ErrorKind == "auth", "HTTP 401 classified");
        var result = gateway.Discover("A", server.Id, FixtureToken, None);
        var catalog = result.Catalog;
        Check(result.Receipt.State == "succeeded" && catalog.ProtocolVersion == "2025-06-18" && catalog.Tools.Count == 2 &&
            catalog.Tools.Any(tool => tool.Name == "echo" && tool.InputSchema.Contains("properties")) && fixture.SawSession, "real paginated schema discovery over JSON and SSE");
        Check(fixture.Deletes > 0, "HTTP session close reached the loopback server");

        result = gateway.Invoke("A", server.Id, "echo", "{}", true, FixtureToken, None);
        Check(result.Receipt.State == "denied" && !result.Receipt.Dispatched && fixture.Calls == 0,
            "discovered tool remains blocked until explicit grant");
        Throws<UnauthorizedAccessException>(() => gateway.SetToolGrant("A", catalog, "write", true), "explicit mutating tool hints block grants");
        Throws<UnauthorizedAccessException>(() => gateway.SetToolGrant("B", catalog, "echo", true), "other project cannot grant tools");
        gateway.SetToolGrant("A", catalog, "echo", true);
        result = gateway.Invoke("A", server.Id, "echo", "{}", false, FixtureToken, None);
        Check(result.Receipt.State == "denied" && fixture.Calls == 0, "stored grant alone cannot dispatch a call");
        result = gateway.Invoke("A", server.Id, "echo", "[]", true, FixtureToken, None);
        Check(result.Receipt.ErrorKind == "arguments" && fixture.Calls == 0, "non-object JSON rejected before dispatch");

        fixture.ReorderSchema = true;
        string arguments = Json.ToJson(new { text = PrivateValue });
        result = gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, None);
        Check(result.Receipt.State == "succeeded" && result.Receipt.Dispatched && fixture.Calls == 1 &&
            result.Output.Contains(PrivateValue) && result.Receipt.HttpStatus == 200, "verified result and receipt for one actual call");
        Check(!Directory.GetFiles(Path.Combine(root, "mcp-gateway"), "*", SearchOption.AllDirectories)
            .Any(path => File.ReadAllText(path).Contains(PrivateValue) || File.ReadAllText(path).Contains(FixtureToken)),
            "MCP persistence excludes arguments, raw result and bearer token");

        fixture.SchemaDrift = true;
        result = gateway.Invoke("A", server.Id, "echo", "{}", true, FixtureToken, None);
        Check(result.Receipt.State == "denied" && fixture.Calls == 1 && !result.Receipt.Dispatched,
            "fresh discovered schema drift invalidates old authorization");
        gateway.SetToolGrant("A", result.Catalog, "echo", true);
        fixture.CallMode = "tool-error";
        result = gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, None);
        Check(result.Receipt.ErrorKind == "tool_error" && result.Output.Contains(PrivateValue), "tool error flag does not yield a success receipt");
        fixture.CallMode = "rpc-error";
        result = gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, None);
        Check(result.Receipt.ErrorKind == "rpc" && result.Receipt.RpcCode == -32602, "real JSON-RPC error has numeric receipt code");

        using (var cancel = new CancellationTokenSource())
        {
            fixture.CallWaiting.Reset();
            fixture.CallMode = "stall";
            var pending = Task.Run(() => gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, cancel.Token));
            Check(fixture.CallWaiting.Wait(4000), "call dispatch reached the HTTP fixture before cancellation");
            Throws<InvalidOperationException>(() => gateway.Discover("A", server.Id, FixtureToken, None), "per-server active operation rejects concurrent handshake");
            cancel.Cancel();
            Check(pending.Wait(4000) && pending.Result.Receipt.State == "cancelled" && pending.Result.Receipt.Dispatched,
                "active MCP call cancellation records uncertain dispatch");
        }
        int calls = fixture.Calls;
        result = gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, None);
        Check(result.Receipt.ErrorKind == "timeout" && fixture.Calls == calls + 1, "MCP deadline interrupts without replay");
        fixture.CallMode = "disconnect";
        calls = fixture.Calls;
        result = gateway.Invoke("A", server.Id, "echo", arguments, true, FixtureToken, None);
        Check(result.Receipt.State == "failed" && result.Receipt.Dispatched && fixture.Calls == calls + 1, "disconnect is a failure with no automatic repeat");
        fixture.CallMode = "";

        var remote = gateway.SaveServer("A", new McpServer { Name = "external guard", Address = "https://fixture.invalid/mcp" });
        remote.Enabled = true;
        gateway.SaveServer("A", remote);
        AiEnvironment.Select("local");
        Check(gateway.Discover("A", remote.Id, FixtureToken, None).Receipt.State == "denied", "local environment blocks external MCP before network");
        Check(gateway.Discover("A", server.Id, FixtureToken, None).Receipt.State == "succeeded", "loopback HTTP works in local environment");
        AiEnvironment.Select("network");
        var restored = new McpGateway(root);
        Check(restored.Receipts("A", server.Id).Any(item => item.State == "cancelled") &&
            restored.Servers("A", out recovered).Any(item => item.Id == server.Id && item.Grants.Count == 1), "grants and actual failure records survive reload");

        if (args.Length != 2) throw new ArgumentException("Supply absolute Python executable and stdio fixture paths.");
        string diagnosticPath = Path.Combine(root, "stdio-fixture-diagnostic.txt");
        var stdio = gateway.SaveServer("A", new McpServer { Name = "stdio fixture", Transport = "stdio", Address = args[0],
            Arguments = "\"" + args[1] + "\" \"" + diagnosticPath + "\""});
        stdio.Enabled = true;
        gateway.SaveServer("A", stdio);
        result = gateway.Discover("A", stdio.Id, "", None);
        if (result.Receipt.State != "succeeded")
        {
            Console.Error.WriteLine("stdio fixture: " + result.Receipt.ErrorKind + " / " + result.Error);
            if (File.Exists(diagnosticPath)) Console.Error.WriteLine(File.ReadAllText(diagnosticPath));
        }
        Check(result.Receipt.State == "succeeded" && result.Catalog.Tools.Single().Name == "echo" &&
            result.Catalog.ProtocolVersion == "2024-11-05", "separate native stdio subprocess handshake and real discovery");
        gateway.SetToolGrant("A", result.Catalog, "echo", true);
        result = gateway.Invoke("A", stdio.Id, "echo", Json.ToJson(new { text = "stdio 中文" }), true, "", None);
        Check(result.Receipt.State == "succeeded" && result.Output.Contains("stdio") && result.Output.Contains("中文"),
            "stdio UTF-8 call returns actual fixture result");
        AiEnvironment.Select("local");
        Check(gateway.Discover("A", stdio.Id, "", None).Receipt.State == "denied", "local environment does not spawn third-party executables");
        AiEnvironment.Select("network");

        gateway.SetToolGrant("A", result.Catalog, "echo", false);
        Check(gateway.Invoke("A", stdio.Id, "echo", "{}", true, "", None).Receipt.State == "denied", "revoked permission is enforced");
        var config = Path.Combine(root, "mcp-gateway", "servers.json");
        File.WriteAllText(config, "corrupted");
        Check(gateway.Servers("A", out recovered).All(item => !item.Enabled && item.Grants.Count == 0) && recovered,
            "corrupt configuration loads the backup in safe disabled state");
        Check(gateway.Discover("A", server.Id, FixtureToken, None).Receipt.State == "denied", "recovery does not resurrect obsolete authorization");
        File.WriteAllText(config, "{\"Version\":42,\"Servers\":[]}");
        Throws<InvalidDataException>(() => gateway.Servers("A", out recovered), "unsupported future store version is protected");
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks++;
        Console.WriteLine("PASS " + label);
    }

    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        Check(false, label);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly Task server;
        private readonly ManualResetEventSlim shutdown = new ManualResetEventSlim();
        private volatile Exception error;
        internal readonly ManualResetEventSlim CallWaiting = new ManualResetEventSlim(), ModelWaiting = new ManualResetEventSlim();
        internal volatile bool AgentSawTool, SawSession, SchemaDrift, ReorderSchema;
        internal volatile string CallMode = "", WriteTarget = "";
        internal int Calls, Initializes, Deletes, RetryRequests;
        internal readonly string Base;

        internal Fixture()
        {
            listener.Start();
            Base = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
            server = Task.Run(delegate
            {
                while (!shutdown.IsSet)
                {
                    try
                    {
                        var next = listener.AcceptTcpClient();
                        Task.Run(() => Serve(next));
                    }
                    catch (SocketException) { if (!shutdown.IsSet) throw; }
                    catch (ObjectDisposedException) { if (!shutdown.IsSet) throw; }
                }
            });
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            {
                try
                {
                    stream.ReadTimeout = stream.WriteTimeout = 5000;
                    string first = ReadHeader(stream);
                    if (first.Length == 0) return;
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line;
                    while ((line = ReadHeader(stream)).Length > 0)
                    {
                        int colon = line.IndexOf(':');
                        headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim();
                    }
                    string length, expect;
                    headers.TryGetValue("Content-Length", out length);
                    if (headers.TryGetValue("Expect", out expect) && expect == "100-continue") Write(stream, "HTTP/1.1 100 Continue\r\n\r\n");
                    var bytes = new byte[int.Parse(length ?? "0")];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int n = stream.Read(bytes, offset, bytes.Length - offset);
                        if (n == 0) throw new IOException("EOF before request body.");
                        offset += n;
                    }
                    string text = Encoding.UTF8.GetString(bytes);
                    if (first.Contains(" /mcp ")) Mcp(stream, first, headers, text);
                    else Model(stream, first, text);
                }
                catch (IOException) { } // Expected when the client aborts big or stalled responses.
                catch (SocketException) { }
                catch (Exception failure) { error = failure; }
            }
        }

        private void Model(Stream stream, string first, string text)
        {
            var request = Json.FromJson<Dictionary<string, object>>(text);
            var mode = (string)request["model"];
            switch (mode)
            {
                case "retry":
                    if (Interlocked.Increment(ref RetryRequests) == 1) { Respond(stream, "503 Service Unavailable", "application/json", "{\"error\":\"busy\"}"); return; }
                    break;
                case "silent":
                    Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
                    ModelWaiting.Set();
                    shutdown.Wait(4200);
                    return;
                case "large":
                    Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
                    for (int i = 0; i < 5; i++) Write(stream, Delta(new string('x', 300000)));
                    return;
                case "incomplete":
                    Respond(stream, "200 OK", "text/event-stream", Delta(Tool(Calc)));
                    return;
                case "multi":
                    bool hadTool = text.Contains("工程值=50");
                    AgentSawTool |= hadTool;
                    if (!hadTool && text.Contains("fixture-model-input"))
                    {
                        Respond(stream, "200 OK", "text/event-stream", Delta(Tool(Calc)) + "data: [DONE]\n\n");
                        return;
                    }
                    break;
                case "denied":
                    if (!text.Contains("denied-result"))
                    {
                        var input = Json.ToJson(new { name = "fs.write", args = new { path = WriteTarget, content = "fixture" } });
                        Respond(stream, "200 OK", "text/event-stream", Delta(Tool(input)) + "data: [DONE]\n\n");
                        return;
                    }
                    break;
                case "responses":
                    if (!first.Contains(" /v1/responses ") || !request.ContainsKey("input")) throw new Exception("Incorrect Responses endpoint/body.");
                    Respond(stream, "200 OK", "text/event-stream",
                        "data: {\"type\":\"response.output_text.delta\",\"delta\":\"OK\"}\n\n" +
                        "data: {\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":8,\"output_tokens\":2}}}\n\n");
                    return;
            }
            Respond(stream, "200 OK", "text/event-stream", Delta("OK") +
                "data: {\"choices\":[{\"finish_reason\":\"stop\"}]}\n\n" +
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":3}}\n\n" +
                "data: [DONE]\n\n");
        }

        private void Mcp(Stream stream, string first, Dictionary<string, string> headers, string text)
        {
            string bearer;
            if (!headers.TryGetValue("Authorization", out bearer) || bearer != "Bearer " + FixtureToken)
            {
                Respond(stream, "401 Unauthorized", "application/json", "");
                return;
            }
            if (first.StartsWith("DELETE ", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Deletes);
                Respond(stream, "204 No Content", "", "");
                return;
            }
            var request = Json.FromJson<Dictionary<string, object>>(text);
            var method = (string)request["method"];
            object id;
            request.TryGetValue("id", out id);
            if (method == "initialize")
            {
                Interlocked.Increment(ref Initializes);
                Respond(stream, "200 OK", "application/json", Envelope(id, new
                {
                    protocolVersion = "2025-06-18", serverInfo = new { name = "HTTP fixture", version = "1" },
                    capabilities = new { tools = new { } }
                }), "Mcp-Session-Id: fixture-session\r\n");
                return;
            }
            if (headers["Mcp-Session-Id"] != "fixture-session" || headers["MCP-Protocol-Version"] != "2025-06-18")
                throw new Exception("Missing negotiated MCP wire headers.");
            SawSession = true;
            if (method == "notifications/initialized") { Respond(stream, "202 Accepted", "", ""); return; }
            var args = (Dictionary<string, object>)request["params"];
            if (method == "tools/list")
            {
                if (args.ContainsKey("cursor"))
                {
                    var definitions = new[] { new { name = "write", inputSchema = new { type = "object" },
                        annotations = new { readOnlyHint = false, destructiveHint = true } } };
                    Respond(stream, "200 OK", "text/event-stream", "data: " + Envelope(id, new { tools = definitions }) + "\n\n");
                }
                else
                {
                    var props = new { text = new { type = SchemaDrift ? "integer" : "string" } };
                    object schema = ReorderSchema ? (object)new { properties = props, type = "object" } : new { type = "object", properties = props };
                    var definitions = new[] { new { name = "echo", description = "Test echo", inputSchema = schema } };
                    Respond(stream, "200 OK", "application/json", Envelope(id, new { tools = definitions, nextCursor = "fixture-next" }));
                }
                return;
            }
            if (method != "tools/call" || (string)args["name"] != "echo") throw new Exception("Unexpected fixture method " + method);
            Interlocked.Increment(ref Calls);
            var mode = CallMode;
            if (mode == "stall")
            {
                Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n");
                CallWaiting.Set();
                shutdown.Wait(4200);
                return;
            }
            if (mode == "disconnect") return;
            if (mode == "rpc-error")
            {
                Respond(stream, "200 OK", "application/json", Json.ToJson(new { jsonrpc = "2.0", id = id, error = new { code = -32602, message = "fixture" } }));
                return;
            }
            var input = (Dictionary<string, object>)args["arguments"];
            Respond(stream, "200 OK", "application/json", Envelope(id, new { content = new[] { new { type = "text", text = (string)input["text"] } },
                isError = mode == "tool-error" }));
        }

        private static string Tool(string args) { return "```tool\n" + args + "\n```"; }
        private static string Delta(string value) { return "data: " + Json.ToJson(new { choices = new[] { new { delta = new { content = value } } } }) + "\n\n"; }
        private static string Envelope(object id, object reply) { return Json.ToJson(new { jsonrpc = "2.0", id = id, result = reply }); }

        private static void Respond(Stream stream, string status, string mime, string body, string headers = "")
        {
            Write(stream, "HTTP/1.1 " + status + "\r\nContent-Type: " + mime + "\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) +
                "\r\nConnection: close\r\n" + headers + "\r\n" + body);
        }

        private static void Write(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string ReadHeader(Stream stream)
        {
            var value = new StringBuilder();
            int next;
            while ((next = stream.ReadByte()) >= 0 && next != '\n') value.Append((char)next);
            return value.ToString().TrimEnd('\r');
        }

        internal void AssertHealthy() { if (error != null) throw new InvalidOperationException("Fixture failed", error); }

        public void Dispose()
        {
            shutdown.Set();
            listener.Stop();
            server.Wait(5000);
        }
    }
}
