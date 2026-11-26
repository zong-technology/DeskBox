using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.Integration
{
    internal sealed class McpFault : Exception
    {
        internal readonly string Kind;
        internal readonly int? Code;
        internal McpFault(string kind, string message, int? code = null) : base(message) { Kind = kind; Code = code; }
    }

    internal abstract class McpWire : IDisposable
    {
        protected const int MaxMessage = 1024 * 1024;
        internal int HttpStatus { get; set; }
        internal string Version { get; set; }
        internal abstract Dictionary<string, object> Exchange(string json, int? id, CancellationToken token);
        public abstract void Dispose();

        internal static McpWire Connect(McpServer server, string bearer, CancellationToken token)
        {
            if (server.Transport == "stdio") return new StdioMcpWire(server, token);
            return new HttpMcpWire(server.Address, bearer, server.TimeoutMs);
        }

        protected static Dictionary<string, object> Decode(string json, int id)
        {
            var envelope = Json.FromJson<Dictionary<string, object>>(json);
            if (envelope == null || McpProtocol.ReadString(envelope, "jsonrpc") != "2.0")
                throw new McpFault("protocol", "收到无效的 MCP JSON-RPC 消息。");
            object value = McpProtocol.Value(envelope, "id");
            if (McpProtocol.Value(envelope, "method") != null)
            {
                if (value != null) throw new McpFault("protocol", "服务器请求的客户端能力未开放。");
                return null;
            }
            if (!(value is int) || (int)value != id)
                throw new McpFault("protocol", "服务器响应 ID 与请求不一致。");
            var error = McpProtocol.Object(envelope, "error");
            var result = McpProtocol.Object(envelope, "result");
            if (error != null && result == null)
            {
                var code = McpProtocol.Value(error, "code");
                if (!(code is int)) throw new McpFault("protocol", "MCP 错误响应缺少有效代码。");
                throw new McpFault("rpc", "MCP 服务返回 JSON-RPC 错误 " + code + "。", (int)code);
            }
            if (result == null || error != null) throw new McpFault("protocol", "MCP 响应缺少有效的 result。");
            return result;
        }

        protected static string ReadBounded(TextReader reader, bool line)
        {
            var text = new StringBuilder();
            int c;
            while ((c = reader.Read()) >= 0)
            {
                if (line && c == '\n') return text.ToString().TrimEnd('\r');
                if (text.Length >= MaxMessage) throw new McpFault("response_limit", "MCP 响应超过 1 Mi 字符限制。");
                text.Append((char)c);
            }
            return text.Length == 0 ? null : text.ToString();
        }
    }

    internal sealed class HttpMcpWire : McpWire
    {
        private readonly string address, bearer;
        private readonly int timeoutMs;
        private string sessionId;

        internal HttpMcpWire(string address, string bearer, int timeoutMs)
        {
            this.address = address;
            this.bearer = bearer;
            this.timeoutMs = timeoutMs;
        }

        internal override Dictionary<string, object> Exchange(string json, int? id, CancellationToken token)
        {
            var request = CreateRequest("POST", timeoutMs);
            HttpStatus = 0;
            request.ContentType = "application/json";
            request.Accept = "application/json, text/event-stream";
            var body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;
            using (token.Register(() => Abort(request)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        HttpStatus = (int)response.StatusCode;
                        if (HttpStatus < 200 || HttpStatus > 299)
                            throw new McpFault("http", "MCP HTTP 响应状态 " + HttpStatus + "。");
                        var receivedSession = response.Headers["Mcp-Session-Id"];
                        if (!string.IsNullOrEmpty(receivedSession))
                        {
                            if (receivedSession.Length > 256 || HasNonVisibleAscii(receivedSession) ||
                                (sessionId != null && sessionId != receivedSession))
                                throw new McpFault("protocol", "MCP 会话 ID 无效或发生变化。");
                            sessionId = receivedSession;
                        }
                        if (!id.HasValue)
                        {
                            if (HttpStatus != 202 && HttpStatus != 204)
                                throw new McpFault("protocol", "MCP 初始化通知未得到接受。");
                            return null;
                        }
                        using (var stream = response.GetResponseStream())
                        using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
                        {
                            var type = response.ContentType ?? "";
                            if (type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                            {
                                var item = Decode(ReadBounded(reader, false), id.Value);
                                if (item != null) return item;
                            }
                            else if (type.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
                            {
                                var item = ReadSse(reader, id.Value, token);
                                if (item != null) return item;
                            }
                            else throw new McpFault("protocol", "MCP 响应类型必须是 JSON 或 SSE。");
                        }
                    }
                }
                catch (WebException failure)
                {
                    token.ThrowIfCancellationRequested();
                    using (var response = failure.Response as HttpWebResponse)
                    {
                        if (response != null)
                        {
                            HttpStatus = (int)response.StatusCode;
                            throw new McpFault(HttpStatus == 401 || HttpStatus == 403 ? "auth" : "http",
                                "MCP HTTP 响应状态 " + HttpStatus + "。");
                        }
                    }
                    if (failure.Status == WebExceptionStatus.Timeout) throw new McpFault("timeout", "MCP HTTP 请求超时。");
                    throw new McpFault("network", "无法连接 MCP 服务（" + failure.Status + "）。");
                }
            }
            throw new McpFault("protocol", "MCP 连接结束时未收到匹配的响应。");
        }

        private static Dictionary<string, object> ReadSse(TextReader reader, int id, CancellationToken token)
        {
            var data = new StringBuilder();
            string line;
            while ((line = ReadBounded(reader, true)) != null)
            {
                token.ThrowIfCancellationRequested();
                if (line.Length == 0)
                {
                    if (data.Length == 0) continue;
                    var result = Decode(data.ToString(), id);
                    if (result != null) return result;
                    data.Clear();
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (data.Length + line.Length > MaxMessage) throw new McpFault("response_limit", "MCP SSE 消息超过限制。");
                    data.AppendLine(line.Substring(5));
                }
            }
            return null;
        }

        private HttpWebRequest CreateRequest(string method, int timeout)
        {
            AiEnvironment.RequireEndpoint(address);
            var request = (HttpWebRequest)WebRequest.Create(address);
            request.Method = method;
            request.Timeout = request.ReadWriteTimeout = timeout;
            request.AllowAutoRedirect = false;
            request.AllowWriteStreamBuffering = false;
            request.KeepAlive = false;
            request.Proxy = null;
            if (!string.IsNullOrEmpty(bearer)) request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            if (sessionId != null) request.Headers["Mcp-Session-Id"] = sessionId;
            if (Version != null) request.Headers["MCP-Protocol-Version"] = Version;
            return request;
        }

        internal static bool HasNonVisibleAscii(string value)
        {
            foreach (char c in value) if (c < 33 || c > 126) return true;
            return false;
        }

        private static void Abort(HttpWebRequest request) { try { request.Abort(); } catch (InvalidOperationException) { } }

        public override void Dispose()
        {
            if (sessionId == null) return;
            // A server can decline DELETE. Close cleanup must never overwrite the operation receipt.
            try { using (CreateRequest("DELETE", 500).GetResponse()) { } }
            catch (WebException error) { if (error.Response != null) error.Response.Dispose(); }
            catch (InvalidOperationException) { }
        }
    }

    internal sealed class StdioMcpWire : McpWire
    {
        private readonly Process process;
        private readonly StreamWriter writer;
        private readonly CancellationTokenRegistration abort;

        internal StdioMcpWire(McpServer server, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(server.Address, server.Arguments)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true),
                WorkingDirectory = Path.GetDirectoryName(server.Address)
            };
            start.EnvironmentVariables.Clear();
            foreach (var name in new[] { "SystemRoot", "PATH", "TEMP", "TMP", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA" })
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (value != null) start.EnvironmentVariables[name] = value;
            }
            process = Process.Start(start);
            try
            {
                abort = token.Register(Kill);
                writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                process.StandardError.BaseStream.CopyToAsync(Stream.Null).ContinueWith(task => { var ignored = task.Exception; },
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }
            catch { Dispose(); throw; }
        }

        internal override Dictionary<string, object> Exchange(string json, int? id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            writer.WriteLine(json);
            if (!id.HasValue) return null;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var line = ReadBounded(process.StandardOutput, true);
                if (line == null) throw new McpFault("disconnected", "MCP 进程在返回完整响应前退出。");
                var result = Decode(line, id.Value);
                if (result != null) return result;
            }
        }

        private void Kill()
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        public override void Dispose()
        {
            abort.Dispose();
            Kill();
            try { if (writer != null) writer.Dispose(); }
            catch (IOException) { }
            process.Dispose();
        }
    }
}
