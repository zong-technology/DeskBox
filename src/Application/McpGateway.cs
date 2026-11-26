using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed class McpGateway
    {
        private static readonly object Gate = new object();
        private static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly McpStore store;

        public McpGateway() : this(AppPaths.DataDir) { }
        public McpGateway(string dataDirectory) { store = new McpStore(dataDirectory); }

        public List<McpServer> Servers(string projectId, out bool recovered)
        {
            lock (Gate) return store.Load(out recovered).Servers.Where(server => server.ProjectId == Scope(projectId))
                .OrderBy(server => server.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public McpServer SaveServer(string projectId, McpServer draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            string scope = Scope(projectId);
            var server = Validate(draft, scope);
            lock (Gate)
            {
                bool recovered;
                var settings = store.Load(out recovered);
                if (string.IsNullOrEmpty(draft.Id)) server.Id = Guid.NewGuid().ToString("N");
                else
                {
                    var current = Find(settings, scope, draft.Id);
                    RequireIdle(current.Id);
                    if (draft.Revision != current.Revision) throw new InvalidOperationException("MCP 配置已变更，请重新加载后修改。");
                    server.Id = current.Id;
                    server.Revision = current.Revision;
                    if (McpProtocol.Profile(current) == McpProtocol.Profile(server))
                        server.Grants = current.Grants;
                    else server.Enabled = false;
                    settings.Servers.Remove(current);
                }
                CheckServerLimit(settings);
                server.Revision++;
                settings.Servers.Add(server);
                store.Save(settings, recovered);
                return server;
            }
        }

        private static void CheckServerLimit(McpStore.Settings settings)
        {
            if (settings.Servers.Count >= 500) throw new InvalidOperationException("MCP 注册已达 500 个服务上限。");
        }

        public McpServer SetToolGrant(string projectId, McpCatalog catalog, string name, bool approved)
        {
            if (catalog == null || catalog.ProjectId != Scope(projectId))
                throw new UnauthorizedAccessException("工具目录超出当前工程范围。");
            var tool = catalog.Tools.SingleOrDefault(item => item.Name == name);
            if (tool == null) throw new ArgumentException("工具必须来自当前服务的真实发现结果。");
            if (!approved) return RevokeTool(projectId, catalog.ServerId, name);
            if (tool.ProhibitedByHints)
                throw new UnauthorizedAccessException("服务声明该工具具有写入或破坏能力，无法在通用网关授权。");
            lock (Gate)
            {
                bool recovered;
                var settings = store.Load(out recovered);
                var server = Find(settings, catalog.ProjectId, catalog.ServerId);
                RequireIdle(server.Id);
                if (!server.Enabled || catalog.Profile != McpProtocol.Profile(server))
                    throw new InvalidOperationException("服务配置或状态已变更，请重新发现工具。");
                server.Grants.RemoveAll(grant => grant.Tool == name);
                server.Grants.Add(new McpGrant { Tool = name, Fingerprint = Fingerprint(catalog, tool) });
                server.Revision++;
                store.Save(settings, recovered);
                return server;
            }
        }

        public McpServer RevokeTool(string projectId, string serverId, string name)
        {
            lock (Gate)
            {
                bool recovered;
                var settings = store.Load(out recovered);
                var server = Find(settings, Scope(projectId), serverId);
                RequireIdle(server.Id);
                if (server.Grants.RemoveAll(grant => grant.Tool == name) == 0) return server;
                server.Revision++;
                store.Save(settings, recovered);
                return server;
            }
        }

        public List<McpReceipt> Receipts(string projectId, string serverId)
        {
            lock (Gate)
            {
                bool ignored;
                var server = Find(store.Load(out ignored), Scope(projectId), serverId);
                return store.ReadReceipts(server.ProjectId, server.Id, Active.Contains(ActiveKey(server.Id)));
            }
        }

        public McpResult Discover(string projectId, string serverId, string bearer, CancellationToken cancellation)
        {
            return Run(projectId, serverId, "", null, false, bearer, cancellation);
        }

        public McpResult Invoke(string projectId, string serverId, string name, string argumentsJson,
            bool operatorApproved, string bearer, CancellationToken cancellation)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Any(char.IsControl))
                throw new ArgumentException("请选择有效的工具。");
            return Run(projectId, serverId, name, argumentsJson, operatorApproved, bearer, cancellation);
        }

        private McpResult Run(string projectId, string serverId, string name, string json,
            bool approved, string bearer, CancellationToken cancellation)
        {
            McpServer server;
            var clock = Stopwatch.StartNew();
            var environment = AiEnvironment.Token;
            var receipt = new McpReceipt { Id = Guid.NewGuid().ToString("N"), Tool = name,
                Operation = name.Length == 0 ? "discover" : "call", StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            lock (Gate)
            {
                bool ignored;
                server = Find(store.Load(out ignored), Scope(projectId), serverId);
                RequireIdle(serverId);
                receipt.ProjectId = server.ProjectId;
                receipt.ServerId = server.Id;
                Active.Add(ActiveKey(server.Id));
                try { store.SaveReceipt(receipt); }
                catch { Active.Remove(ActiveKey(server.Id)); throw; }
            }
            var result = new McpResult { Receipt = receipt, Output = "", Error = "" };
            try
            {
                using (var deadline = new CancellationTokenSource(server.TimeoutMs))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(new[] { cancellation, environment, deadline.Token }))
                {
                    McpWire wire = null;
                    try
                    {
                        var token = linked.Token;
                        token.ThrowIfCancellationRequested();
                        RequireEnabled(server, bearer);
                        var arguments = name.Length == 0 ? null : ReadArguments(server, name, json, approved);
                        wire = McpWire.Connect(server, bearer, token);
                        var protocol = new McpProtocol(wire, token);
                        result.Catalog = protocol.Discover(server, version => receipt.ProtocolVersion = version);
                        receipt.ToolCount = result.Catalog.Tools.Count;
                        token.ThrowIfCancellationRequested();
                        if (name.Length != 0)
                        {
                            var tool = result.Catalog.Tools.SingleOrDefault(item => item.Name == name);
                            if (tool == null || tool.ProhibitedByHints || !server.Grants.Any(grant =>
                                grant.Tool == name && grant.Fingerprint == Fingerprint(result.Catalog, tool)))
                                throw new McpFault("policy", "服务端工具 Schema、版本或能力声明已变化，请重新发现并授权。");
                            receipt.Dispatched = true;
                            store.SaveReceipt(receipt);
                            token.ThrowIfCancellationRequested();
                            var output = protocol.Call(name, arguments);
                            result.Output = Json.ToJson(output);
                            receipt.OutputCharacters = result.Output.Length;
                            if (Equals(McpProtocol.Value(output, "isError"), true))
                                throw new McpFault("tool_error", "工具返回 isError=true，详细结果见响应。");
                        }
                        receipt.State = "succeeded";
                    }
                    catch (Exception error)
                    {
                        if (cancellation.IsCancellationRequested || environment.IsCancellationRequested)
                            Fail(result, "cancelled", "已取消。");
                        else if (deadline.IsCancellationRequested)
                            Fail(result, "timeout", "MCP 操作超时。");
                        else
                        {
                            var fault = error as McpFault;
                            if (fault != null)
                            {
                                receipt.RpcCode = fault.Code;
                                Fail(result, fault.Kind, fault.Message);
                            }
                            else if (error is DecoderFallbackException)
                                Fail(result, "protocol", "MCP 服务返回了无效的 UTF-8 消息。");
                            else if (error is IOException || error is Win32Exception || error is UnauthorizedAccessException)
                                Fail(result, "io", "MCP 进程、网络或回执存储发生 I/O 错误（" + error.GetType().Name + "）。");
                            else Fail(result, "runtime", "MCP 操作失败（" + error.GetType().Name + "）。");
                        }
                    }
                    finally
                    {
                        if (wire != null)
                        {
                            receipt.HttpStatus = wire.HttpStatus;
                            wire.Dispose();
                        }
                    }
                }
                if (receipt.Dispatched && !new[] { "succeeded", "tool_error", "rpc" }.Contains(
                    receipt.State == "succeeded" ? receipt.State : receipt.ErrorKind))
                    result.Error += " tools/call 可能已送达服务，请核对服务端状态。";
                return result;
            }
            finally
            {
                lock (Gate)
                {
                    try
                    {
                        receipt.ElapsedMs = clock.ElapsedMilliseconds;
                        store.SaveReceipt(receipt);
                        try { store.PruneReceipts(server.Id); }
                        catch (IOException) { Core.AppLog.Write("warn", "mcp", "MCP 旧回执整理失败。"); }
                        catch (UnauthorizedAccessException) { Core.AppLog.Write("warn", "mcp", "MCP 旧回执整理权限不足。"); }
                    }
                    finally { Active.Remove(ActiveKey(server.Id)); }
                }
            }
        }

        private static Dictionary<string, object> ReadArguments(McpServer server, string tool, string json, bool approved)
        {
            if (!approved || !server.Grants.Any(grant => grant.Tool == tool))
                throw new McpFault("policy", "需要工具权限及本次人工确认。");
            if (string.IsNullOrWhiteSpace(json) || json.Length > 256 * 1024 || !json.TrimStart().StartsWith("{", StringComparison.Ordinal))
                throw new McpFault("arguments", "工具参数必须为 JSON 对象，长度不得超过 256 Ki 字符。");
            var args = Json.FromJson<Dictionary<string, object>>(json);
            if (args == null) throw new McpFault("arguments", "工具参数 JSON 无效。");
            return args;
        }

        private static void Fail(McpResult result, string kind, string error)
        {
            result.Receipt.State = kind == "cancelled" ? "cancelled" : kind == "policy" ? "denied" : "failed";
            result.Receipt.ErrorKind = kind;
            result.Error = error;
        }

        private static void RequireEnabled(McpServer server, string bearer)
        {
            if (!server.Enabled) throw new McpFault("policy", "服务器未启用。");
            if (!string.IsNullOrEmpty(bearer) && (bearer.Length > 4096 || HttpMcpWire.HasNonVisibleAscii(bearer)))
                throw new McpFault("arguments", "访问令牌格式无效。");
            if (server.Transport == "stdio")
            {
                if (AiEnvironment.Local) throw new McpFault("policy", "本地隔离模式已禁用第三方进程启动。");
                if (!string.IsNullOrEmpty(bearer)) throw new McpFault("arguments", "stdio 不使用 HTTP Bearer 令牌。");
                if (!File.Exists(server.Address)) throw new McpFault("disconnected", "stdio 可执行文件不存在。");
            }
            else
            {
                try { AiEnvironment.RequireEndpoint(server.Address); }
                catch (InvalidOperationException) { throw new McpFault("policy", "本地隔离模式只允许本机数值回环地址。"); }
            }
        }

        private static McpServer Validate(McpServer draft, string scope)
        {
            var server = new McpServer
            {
                ProjectId = scope, Name = (draft.Name ?? "").Trim(), Address = (draft.Address ?? "").Trim(),
                Transport = (draft.Transport ?? "").Trim(), Arguments = (draft.Arguments ?? "").Trim(),
                TimeoutMs = draft.TimeoutMs, Enabled = !string.IsNullOrEmpty(draft.Id) && draft.Enabled
            };
            if (server.Name.Length == 0 || server.Name.Length > 120 || server.Name.Any(char.IsControl))
                throw new ArgumentException("请填写 1 至 120 字符的服务名称。");
            if (server.Transport != "http" && server.Transport != "stdio") throw new ArgumentException("请选择 HTTP 或 stdio 传输。");
            if (server.TimeoutMs < 1000 || server.TimeoutMs > 120000) throw new ArgumentException("超时须为 1 至 120 秒。");
            if (server.Address.Length == 0 || server.Address.Length > 2048 || server.Arguments.Length > 4096 ||
                server.Address.Any(char.IsControl) || server.Arguments.Any(char.IsControl))
                throw new ArgumentException("服务地址或启动参数无效。");
            if (server.Transport == "http")
            {
                Uri endpoint;
                if (!Uri.TryCreate(server.Address, UriKind.Absolute, out endpoint) ||
                    (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && AiEnvironment.Loopback(endpoint))) ||
                    endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || server.Arguments.Length != 0)
                    throw new ArgumentException("输入 HTTPS MCP 地址或数值回环 HTTP 地址。令牌请在调用时单独输入。");
                server.Address = endpoint.AbsoluteUri;
            }
            else
            {
                if (!Path.IsPathRooted(server.Address) || server.Address.StartsWith(@"\\", StringComparison.Ordinal) ||
                    (Path.DirectorySeparatorChar == '\\' && (server.Address.Length < 3 || server.Address[1] != ':' || server.Address[2] != '\\')))
                    throw new ArgumentException("stdio 需要本机可执行文件的绝对路径。");
                server.Address = Path.GetFullPath(server.Address);
                if (!File.Exists(server.Address)) throw new ArgumentException("stdio 可执行文件不存在。");
            }
            return server;
        }

        private void RequireIdle(string id)
        {
            if (Active.Contains(ActiveKey(id))) throw new InvalidOperationException("该服务正在执行操作，请取消或等待结束。");
        }

        private string ActiveKey(string id) { return store.ConfigPath + "|" + id; }

        private static McpServer Find(McpStore.Settings settings, string scope, string id)
        {
            var server = settings.Servers.FirstOrDefault(value => value.Id == id && value.ProjectId == scope);
            if (server == null) throw new UnauthorizedAccessException("当前工程范围内未注册该 MCP 服务。");
            return server;
        }

        private static string Scope(string projectId)
        {
            if (projectId != null && projectId.Length > 256) throw new ArgumentException("项目 ID 无效。");
            return projectId ?? "";
        }

        private static string Fingerprint(McpCatalog catalog, McpTool tool)
        {
            var body = Json.ToJson(new[] { catalog.ServerName, catalog.ServerVersion, catalog.ProtocolVersion, tool.Fingerprint });
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(body)));
        }
    }
}
