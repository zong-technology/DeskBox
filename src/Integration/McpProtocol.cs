using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.Integration
{
    internal sealed class McpProtocol
    {
        private static readonly string[] Versions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
        private readonly McpWire wire;
        private readonly CancellationToken token;
        private int id;

        internal McpProtocol(McpWire wire, CancellationToken token)
        {
            this.wire = wire;
            this.token = token;
        }

        internal McpCatalog Discover(McpServer server, Action<string> negotiated)
        {
            var hello = Request("initialize", new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "DeskBox", version = "5.0.0-alpha.3" }
            });
            string version = ReadString(hello, "protocolVersion");
            if (!Versions.Contains(version) || (server.Transport == "http" && version == "2024-11-05"))
                throw new McpFault("protocol", "MCP 服务选择了当前传输不支持的协议版本。");
            var identity = Object(hello, "serverInfo");
            var capabilities = Object(hello, "capabilities");
            string name = ReadString(identity, "name"), serverVersion = ReadString(identity, "version");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(serverVersion) || capabilities == null)
                throw new McpFault("protocol", "MCP 初始化响应缺少服务器标识或能力声明。");
            wire.Version = version;
            negotiated(version);
            wire.Exchange(Json.ToJson(new { jsonrpc = "2.0", method = "notifications/initialized" }), null, token);
            var tools = Object(capabilities, "tools") == null ? new List<McpTool>() : ReadTools();
            return new McpCatalog
            {
                ServerId = server.Id, ProjectId = server.ProjectId, ServerName = name, ServerVersion = serverVersion,
                ProtocolVersion = version, Tools = tools.AsReadOnly(), Profile = Profile(server)
            };
        }

        private List<McpTool> ReadTools()
        {
            var found = new List<McpTool>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var seenCursors = new HashSet<string>(StringComparer.Ordinal);
            int characters = 0;
            string cursor = null;
            do
            {
                token.ThrowIfCancellationRequested();
                var page = Request("tools/list", cursor == null ? (object)new { } : new { cursor = cursor });
                var rows = Value(page, "tools") as IList;
                if (rows == null) throw new McpFault("protocol", "tools/list 响应缺少 tools 数组。");
                foreach (var item in rows)
                {
                    var row = item as Dictionary<string, object>;
                    string toolName = ReadString(row, "name");
                    var input = Object(row, "inputSchema");
                    if (string.IsNullOrWhiteSpace(toolName) || toolName.Length > 128 || toolName.Any(char.IsControl) ||
                        !names.Add(toolName) || input == null)
                        throw new McpFault("protocol", "发现重复的工具名、无效名称或无效输入 Schema。");
                    var annotations = Object(row, "annotations");
                    var inputJson = Json.ToJson(Canonical(input));
                    var outputJson = Json.ToJson(Canonical(Object(row, "outputSchema")));
                    var hintsJson = Json.ToJson(Canonical(annotations));
                    var description = ReadString(row, "description") ?? "";
                    characters += inputJson.Length + outputJson.Length + hintsJson.Length + description.Length;
                    if (found.Count >= 800 || characters > 8 * 1024 * 1024)
                        throw new McpFault("response_limit", "MCP 工具目录超过数量或总长度限制。");
                    var isDestructive = Value(annotations, "destructiveHint");
                    var isReadOnly = Value(annotations, "readOnlyHint");
                    found.Add(new McpTool
                    {
                        Name = toolName, Description = description, InputSchema = inputJson, OutputSchema = outputJson,
                        Annotations = hintsJson, ProhibitedByHints = Equals(isDestructive, true) || Equals(isReadOnly, false),
                        AgentReadOnly = Equals(isReadOnly, true) && !Equals(isDestructive, true) &&
                            Equals(Value(annotations, "openWorldHint"), false) && McpInputSchema.CanValidate(inputJson),
                        Fingerprint = Json.ToJson(new[] { toolName, description, inputJson, outputJson, hintsJson })
                    });
                }
                cursor = ReadString(page, "nextCursor");
                if (string.IsNullOrEmpty(cursor)) break;
                if (seenCursors.Count >= 24 || !seenCursors.Add(cursor))
                    throw new McpFault("response_limit", "MCP 分页过多或分页标记重复。");
            } while (true);
            return found;
        }

        internal Dictionary<string, object> Call(string name, Dictionary<string, object> arguments)
        {
            var result = Request("tools/call", new { name = name, arguments = arguments });
            if (!(Value(result, "content") is IList) && Object(result, "structuredContent") == null)
                throw new McpFault("protocol", "MCP 工具响应缺少有效 content 或 structuredContent。");
            var error = Value(result, "isError");
            if (error != null && !(error is bool)) throw new McpFault("protocol", "MCP 工具响应的 isError 类型无效。");
            return result;
        }

        private Dictionary<string, object> Request(string method, object parameters)
        {
            int next = ++id;
            return wire.Exchange(Json.ToJson(new { jsonrpc = "2.0", id = next, method = method, @params = parameters }), next, token);
        }

        internal static string Profile(McpServer server)
        {
            return Json.ToJson(new[] { server.Transport, server.Address, server.Arguments });
        }

        private static object Canonical(object value)
        {
            var dictionary = value as Dictionary<string, object>;
            if (dictionary != null) return new SortedDictionary<string, object>(
                dictionary.ToDictionary(pair => pair.Key, pair => Canonical(pair.Value)), StringComparer.Ordinal);
            var list = value as IList;
            if (list != null) return list.Cast<object>().Select(Canonical).ToArray();
            return value;
        }

        internal static object Value(Dictionary<string, object> obj, string name)
        {
            object value;
            return obj != null && obj.TryGetValue(name, out value) ? value : null;
        }

        internal static string ReadString(Dictionary<string, object> obj, string name) { return Value(obj, name) as string; }
        internal static Dictionary<string, object> Object(Dictionary<string, object> obj, string name)
        {
            return Value(obj, name) as Dictionary<string, object>;
        }
    }
}
