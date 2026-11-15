using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace DeskBox.Ai
{
    public class AiMessage
    {
        public string Role = "user";      // system | user | assistant
        public string Content = "";
    }

    public class AiChatRequest
    {
        public List<AiMessage> Messages = new List<AiMessage>();
        public string Model = "";
        public double Temperature = 0.7;
        public int MaxTokens = 0;         // 0 = 不指定
    }

    public class AiChatResult
    {
        public bool Ok = false;
        public string Content = "";
        public string Error = "";
        public int HttpStatus = 0;
        public string ErrorKind = "";     // auth|model|url|network|timeout|ratelimit|server|protocol|unknown
        public long ElapsedMs = 0;
        public int PromptTokens = 0;
        public int CompletionTokens = 0;
        public bool Cancelled = false;
    }

    public class AiStreamChunk
    {
        public string Delta = "";
        public bool Done = false;
        public string Error = "";
        public string ErrorKind = "";
        public string Stage = "";
    }

    /// <summary>
    /// OpenAI 兼容 HTTP 客户端：支持 chat/completions、responses、自定义头/参数、SSE 流式、
    /// 超时、重试与错误分类。所有异常内部捕获，不向 UI 抛出。
    /// </summary>
    public class AiClient
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };

        private readonly AiProvider _provider;

        public AiClient(AiProvider p)
        {
            _provider = p ?? new AiProvider();
            EnsureTls();
        }

        /// <summary>底层服务商（TestConnection 会更新其 LastStatus）。</summary>
        public AiProvider Provider { get { return _provider; } }

        // ---------------- 非流式 ----------------

        public AiChatResult Chat(AiChatRequest req, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var invalid = Validate(req);
            if (invalid != null)
            {
                invalid.ElapsedMs = sw.ElapsedMilliseconds;
                LogFailure(invalid);
                return invalid;
            }

            int retries = Math.Min(5, Math.Max(0, _provider.MaxRetries));
            AiChatResult last = null;
            for (int i = 0; i <= retries; i++)
            {
                if (ct.IsCancellationRequested) { last = CancelledResult(); break; }
                last = ChatOnce(req, ct);
                if (last.Ok || last.Cancelled) break;
                if (!IsRetryable(last.ErrorKind) || i >= retries) break;
                if (WaitBackoff(i, ct)) { last = CancelledResult(); break; }
            }
            if (last == null) last = CancelledResult();
            last.ElapsedMs = sw.ElapsedMilliseconds;
            LogFailure(last);
            return last;
        }

        private AiChatResult ChatOnce(AiChatRequest req, CancellationToken ct)
        {
            var res = new AiChatResult();
            var sw = Stopwatch.StartNew();
            HttpWebRequest http = null;
            try
            {
                http = CreateRequest();
                string json = BuildBody(req, false);
                using (ct.Register(delegate { TryAbort(http); }))
                {
                    WriteBody(http, json);

                    HttpWebResponse resp;
                    try
                    {
                        resp = (HttpWebResponse)http.GetResponse();
                    }
                    catch (WebException we)
                    {
                        HandleWebException(we, res, ct);
                        return res;
                    }

                    using (resp)
                    {
                        res.HttpStatus = (int)resp.StatusCode;
                        string body = ReadAll(resp);
                        if (res.HttpStatus < 200 || res.HttpStatus >= 300)
                        {
                            res.ErrorKind = Classify(res.HttpStatus, body);
                            res.Error = DescribeError(res.ErrorKind, Redact(body));
                            return res;
                        }
                        ParseSuccess(body, res);
                        return res;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    res.Cancelled = true;
                    res.ErrorKind = "timeout";
                    res.Error = "已取消";
                }
                else
                {
                    res.ErrorKind = "network";
                    res.Error = DescribeError("network", Redact(ex.Message));
                }
                return res;
            }
            finally
            {
                res.ElapsedMs = sw.ElapsedMilliseconds;
            }
        }

        // ---------------- 流式 ----------------

        public void ChatStream(AiChatRequest req, CancellationToken ct, Action<AiStreamChunk> onChunk)
        {
            if (onChunk == null) onChunk = delegate { };
            var invalid = Validate(req);
            if (invalid != null)
            {
                // 错误帧不置 Done，随后补一个空 Done 帧：兼容“先判 Done”的调用方
                onChunk(new AiStreamChunk { Error = invalid.Error, ErrorKind = invalid.ErrorKind });
                onChunk(new AiStreamChunk { Done = true });
                return;
            }

            int retries = Math.Min(5, Math.Max(0, _provider.MaxRetries));
            var sw = Stopwatch.StartNew();
            StreamOutcome last = null;
            for (int i = 0; i <= retries; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    onChunk(new AiStreamChunk { Error = "已取消", ErrorKind = "timeout" });
                    onChunk(new AiStreamChunk { Done = true });
                    AiLog.Write("warn", "AI 流式请求已取消 [" + ProviderLabel() + "]");
                    return;
                }
                last = StreamOnce(req, ct, onChunk);
                if (last.Ok) break;
                if (last.Cancelled || last.AnyDelta || !IsRetryable(last.ErrorKind) || i >= retries) break;
                if (WaitBackoff(i, ct))
                {
                    last = new StreamOutcome();
                    last.Cancelled = true;
                    last.ErrorKind = "timeout";
                    last.Error = "已取消";
                    break;
                }
            }
            sw.Stop();

            if (last == null)
            {
                last = new StreamOutcome();
                last.ErrorKind = "unknown";
                last.Error = DescribeError("unknown", "");
            }
            if (last.Ok)
            {
                onChunk(new AiStreamChunk { Done = true });
                return;
            }
            // 错误帧 Done=false，再补空 Done 帧；成功只有 Done=true
            onChunk(new AiStreamChunk { Error = last.Error, ErrorKind = last.ErrorKind });
            onChunk(new AiStreamChunk { Done = true });
            AiLog.Write(last.Cancelled ? "warn" : "error",
                "AI 流式请求失败 [" + ProviderLabel() + "] " + last.ErrorKind + " - " + last.Error + "（" + sw.ElapsedMilliseconds + "ms）");
        }

        private StreamOutcome StreamOnce(AiChatRequest req, CancellationToken ct, Action<AiStreamChunk> onChunk)
        {
            var outcome = new StreamOutcome();
            HttpWebRequest http = null;
            try
            {
                http = CreateRequest();
                string json = BuildBody(req, true);
                using (ct.Register(delegate { TryAbort(http); }))
                {
                    WriteBody(http, json);

                    HttpWebResponse resp;
                    try
                    {
                        resp = (HttpWebResponse)http.GetResponse();
                    }
                    catch (WebException we)
                    {
                        FillWebException(we, outcome, ct);
                        return outcome;
                    }

                    using (resp)
                    {
                        outcome.HttpStatus = (int)resp.StatusCode;
                        if (outcome.HttpStatus < 200 || outcome.HttpStatus >= 300)
                        {
                            string body = ReadAll(resp);
                            outcome.ErrorKind = Classify(outcome.HttpStatus, body);
                            outcome.Error = DescribeError(outcome.ErrorKind, Redact(body));
                            return outcome;
                        }

                        using (Stream stream = resp.GetResponseStream())
                        {
                            if (stream == null)
                            {
                                outcome.ErrorKind = "protocol";
                                outcome.Error = DescribeError("protocol", "空响应");
                                return outcome;
                            }
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                string line;
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (ct.IsCancellationRequested)
                                    {
                                        outcome.Cancelled = true;
                                        outcome.ErrorKind = "timeout";
                                        outcome.Error = "已取消";
                                        return outcome;
                                    }
                                    if (line.Length == 0) continue;
                                    onChunk(new AiStreamChunk()); // Network activity, including SSE heartbeats.
                                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // 忽略 event:/心跳
                                    string payload = line.Substring(5).Trim();
                                    if (payload.Length == 0) continue;
                                    if (payload == "[DONE]") { outcome.Ok = true; return outcome; }

                                    bool completed = HandlePayload(payload, outcome, onChunk);
                                    if (outcome.ErrorKind != null) return outcome;
                                    if (completed) { outcome.Ok = true; return outcome; }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    outcome.ErrorKind = "timeout";
                    outcome.Error = "已取消";
                }
                else
                {
                    outcome.ErrorKind = "network";
                    outcome.Error = DescribeError("network", Redact(ex.Message));
                }
                return outcome;
            }
            outcome.Ok = true;   // 有些服务商不发 [DONE]，正常 EOF 也视为完成
            return outcome;
        }

        /// <summary>解析一条 SSE data：返回 true 表示流已结束。</summary>
        private bool HandlePayload(string payload, StreamOutcome outcome, Action<AiStreamChunk> onChunk)
        {
            object parsed;
            try { parsed = Ser.DeserializeObject(payload); }
            catch { return false; }               // 忽略非 JSON 心跳
            var dict = parsed as Dictionary<string, object>;
            if (dict == null) return false;

            if (GetValue(dict, "error") != null)
            {
                outcome.ErrorKind = ClassifyErrorMessage(payload);
                outcome.Error = DescribeError(outcome.ErrorKind, Redact(payload));
                return false;
            }

            if (IsResponses())
            {
                string type = GetString(dict, "type") ?? "";
                if (type == "response.output_text.delta")
                {
                    string delta = GetString(dict, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        outcome.AnyDelta = true;
                        onChunk(new AiStreamChunk { Delta = delta });
                    }
                    return false;
                }
                if (type == "response.completed") return true;
                if (type == "response.failed" || type == "error")
                {
                    outcome.ErrorKind = "server";
                    outcome.Error = DescribeError("server", Redact(payload));
                }
                return false;
            }

            var choices = GetArray(dict, "choices");
            if (choices != null && choices.Length > 0)
            {
                var first = choices[0] as Dictionary<string, object>;
                string text = null;
                if (first != null)
                {
                    var delta = GetDict(first, "delta");
                    if (delta != null)
                    {
                        text = GetString(delta, "content");
                        if (!string.IsNullOrEmpty(GetString(delta, "reasoning_content"))) onChunk(new AiStreamChunk { Stage = "服务商正在生成回答 · 已收到推理进度" });
                    }
                    if (string.IsNullOrEmpty(text))
                    {
                        var msg = GetDict(first, "message");
                        if (msg != null) text = GetString(msg, "content");
                    }
                    if (string.IsNullOrEmpty(text)) text = GetString(first, "text");
                }
                if (!string.IsNullOrEmpty(text))
                {
                    outcome.AnyDelta = true;
                    onChunk(new AiStreamChunk { Delta = text });
                }
            }
            return false;
        }

        // ---------------- 连接测试 ----------------

        public AiChatResult TestConnection(CancellationToken ct)
        {
            var req = new AiChatRequest();
            req.Messages = new List<AiMessage>();
            req.Messages.Add(new AiMessage { Role = "user", Content = "ping" });
            req.Model = _provider.Model;
            req.Temperature = 0;
            req.MaxTokens = 1;

            var r = Chat(req, ct);
            if (r.Ok)
            {
                _provider.LastStatus = "ok: " + r.ElapsedMs + "ms";
                AiLog.Write("info", "AI 连接测试成功 [" + ProviderLabel() + "] " + r.ElapsedMs + "ms");
            }
            else
            {
                _provider.LastStatus = "err: " + r.ErrorKind + " " + r.Error;
            }
            _provider.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return r;
        }

        // ---------------- 错误文案 / 分类 ----------------

        public static string DescribeError(string kind, string raw)
        {
            string text;
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "auth": text = "认证失败（401/403）：API Key 无效或没有访问权限"; break;
                case "model": text = "模型不可用：模型名不存在或当前 Key 无权访问该模型"; break;
                case "url": text = "接口地址错误（404）：请检查 BaseUrl"; break;
                case "network": text = "网络连接失败：请检查网络、代理或 BaseUrl"; break;
                case "timeout": text = "请求超时：服务无响应或响应过慢"; break;
                case "ratelimit": text = "请求过于频繁（429）：请稍后重试"; break;
                case "server": text = "服务端错误（5xx）：服务商暂时不可用"; break;
                case "protocol": text = "响应格式异常：返回内容不是 OpenAI 兼容格式"; break;
                default: text = "未知错误"; break;
            }
            string detail = Sanitize(raw);
            if (detail.Length > 0) text = text + "｜" + detail;
            return text;
        }

        internal static string Classify(int status, string body)
        {
            string b = (body ?? "").ToLowerInvariant();
            if (status == 401 || status == 403) return "auth";
            if (status == 404) return b.Contains("model") ? "model" : "url";
            if (status == 400) return b.Contains("model") ? "model" : "unknown";
            if (status == 408) return "timeout";
            if (status == 429) return "ratelimit";
            if (status >= 500) return "server";
            return "unknown";
        }

        internal static string ClassifyErrorMessage(string raw)
        {
            string b = (raw ?? "").ToLowerInvariant();
            if (b.Contains("model")) return "model";
            if (b.Contains("api key") || b.Contains("apikey") || b.Contains("unauthorized") ||
                b.Contains("invalid key") || b.Contains("auth")) return "auth";
            if (b.Contains("rate limit") || b.Contains("ratelimit") || b.Contains("too many")) return "ratelimit";
            return "unknown";
        }

        private static bool IsRetryable(string kind)
        {
            return kind == "network" || kind == "timeout" || kind == "server";
        }

        private static bool WaitBackoff(int attempt, CancellationToken ct)
        {
            int ms = attempt == 0 ? 500 : 1500;
            return ct.WaitHandle.WaitOne(ms);
        }

        // ---------------- 请求构造 ----------------

        private AiChatResult Validate(AiChatRequest req)
        {
            if (req == null) return Fail("unknown", "请求为空");
            if (req.Messages == null || req.Messages.Count == 0) return Fail("unknown", "消息列表为空");
            if (string.IsNullOrWhiteSpace(EffectiveModel(req))) return Fail("model", "未配置模型名");
            if (string.IsNullOrWhiteSpace(_provider.BaseUrl)) return Fail("url", "未配置 BaseUrl");
            return null;
        }

        private static AiChatResult Fail(string kind, string detail)
        {
            var r = new AiChatResult();
            r.ErrorKind = kind;
            r.Error = DescribeError(kind, detail);
            return r;
        }

        private static AiChatResult CancelledResult()
        {
            var r = new AiChatResult();
            r.Cancelled = true;
            r.ErrorKind = "timeout";
            r.Error = "已取消";
            return r;
        }

        private string EffectiveModel(AiChatRequest req)
        {
            if (req != null && !string.IsNullOrWhiteSpace(req.Model)) return req.Model.Trim();
            return (_provider.Model ?? "").Trim();
        }

        private bool IsResponses()
        {
            return string.Equals(_provider.Protocol, "openai-responses", StringComparison.OrdinalIgnoreCase);
        }

        private string Endpoint()
        {
            string baseUrl = NormalizeBaseUrl(_provider.BaseUrl, _provider.Protocol);
            if (IsResponses()) return baseUrl + "/responses";
            return baseUrl + "/chat/completions";
        }

        /// <summary>兼容 https://api.deepseek.com 与 https://api.openai.com/v1；也容许粘贴完整端点。</summary>
        internal static string NormalizeBaseUrl(string baseUrl, string protocol)
        {
            string u = (baseUrl ?? "").Trim();
            while (u.EndsWith("/")) u = u.Substring(0, u.Length - 1);
            if (u.Length == 0) u = "https://api.openai.com";
            if (u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                u = u.Substring(0, u.Length - "/chat/completions".Length).TrimEnd('/');
            if (u.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
                u = u.Substring(0, u.Length - "/responses".Length).TrimEnd('/');
            if (u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return u;
            // custom 协议保留用户填写的完整 base，不自动补 /v1
            if (string.Equals(protocol, "custom", StringComparison.OrdinalIgnoreCase)) return u;
            return u + "/v1";
        }

        private string BuildBody(AiChatRequest req, bool stream)
        {
            var body = new Dictionary<string, object>();
            body["model"] = EffectiveModel(req);

            var msgs = new List<object>();
            foreach (var m in req.Messages)
            {
                var d = new Dictionary<string, object>();
                d["role"] = string.IsNullOrWhiteSpace(m.Role) ? "user" : m.Role.Trim();
                d["content"] = m.Content ?? "";
                msgs.Add(d);
            }
            if (IsResponses())
            {
                body["input"] = msgs;
                if (req.MaxTokens > 0) body["max_output_tokens"] = req.MaxTokens;
            }
            else
            {
                body["messages"] = msgs;
                if (req.MaxTokens > 0) body["max_tokens"] = req.MaxTokens;
            }
            body["temperature"] = req.Temperature;
            body["stream"] = stream;
            ApplyExtraParams(body);
            return Ser.Serialize(body);
        }

        private void ApplyExtraParams(Dictionary<string, object> body)
        {
            if (_provider.ExtraParams == null) return;
            foreach (var kv in _provider.ExtraParams)
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                body[kv.Key.Trim()] = ParseParamValue(kv.Value);
            }
        }

        private static object ParseParamValue(string v)
        {
            if (v == null) return "";
            string s = v.Trim();
            bool b;
            if (bool.TryParse(s, out b)) return b;
            long l;
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            double d;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return v;
        }

        private HttpWebRequest CreateRequest()
        {
            var http = (HttpWebRequest)WebRequest.Create(Endpoint());
            http.Method = "POST";
            http.ContentType = "application/json; charset=utf-8";
            http.Accept = "application/json";
            http.UserAgent = "DeskBox/3.0";
            int timeout = _provider.TimeoutMs > 0 ? _provider.TimeoutMs : 60000;
            timeout = Math.Min(600000, Math.Max(1000, timeout));
            http.Timeout = timeout;
            http.ReadWriteTimeout = timeout;
            http.KeepAlive = false;
            http.AllowAutoRedirect = true;
            ApplyHeaders(http);
            return http;
        }

        private void ApplyHeaders(HttpWebRequest http)
        {
            bool hasAuth = false;
            string lines = (_provider.ExtraHeaders ?? "").Replace("\r\n", "\n");
            foreach (var raw in lines.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int idx = line.IndexOf(':');
                if (idx <= 0) continue;
                string name = line.Substring(0, idx).Trim();
                string value = line.Substring(idx + 1).Trim();
                if (name.Length == 0) continue;
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) hasAuth = true;
                try { http.Headers[name] = value; }
                catch
                {
                    try { http.Headers.Add(name, value); } catch { }
                }
            }
            if (!hasAuth && !string.IsNullOrEmpty(_provider.ApiKey))
            {
                try { http.Headers["Authorization"] = "Bearer " + _provider.ApiKey.Trim(); } catch { }
            }
        }

        private static void WriteBody(HttpWebRequest http, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? "");
            http.ContentLength = bytes.Length;
            using (var s = http.GetRequestStream())
            {
                s.Write(bytes, 0, bytes.Length);
            }
        }

        private static void TryAbort(HttpWebRequest http)
        {
            try { if (http != null) http.Abort(); } catch { }
        }

        private static void EnsureTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        // ---------------- 响应解析 ----------------

        private void HandleWebException(WebException we, AiChatResult res, CancellationToken ct)
        {
            var http = we.Response as HttpWebResponse;
            if (http != null)
            {
                using (http)
                {
                    res.HttpStatus = (int)http.StatusCode;
                    string body = ReadAll(http);
                    res.ErrorKind = Classify(res.HttpStatus, body);
                    res.Error = DescribeError(res.ErrorKind, Redact(body));
                }
                return;
            }
            if (ct.IsCancellationRequested || we.Status == WebExceptionStatus.RequestCanceled)
            {
                res.Cancelled = true;
                res.ErrorKind = "timeout";
                res.Error = "已取消";
            }
            else if (we.Status == WebExceptionStatus.Timeout)
            {
                res.ErrorKind = "timeout";
                res.Error = DescribeError("timeout", we.Message);
            }
            else
            {
                res.ErrorKind = "network";
                res.Error = DescribeError("network", Redact(we.Message));
            }
        }

        private void FillWebException(WebException we, StreamOutcome outcome, CancellationToken ct)
        {
            var http = we.Response as HttpWebResponse;
            if (http != null)
            {
                using (http)
                {
                    outcome.HttpStatus = (int)http.StatusCode;
                    string body = ReadAll(http);
                    outcome.ErrorKind = Classify(outcome.HttpStatus, body);
                    outcome.Error = DescribeError(outcome.ErrorKind, Redact(body));
                }
                return;
            }
            if (ct.IsCancellationRequested || we.Status == WebExceptionStatus.RequestCanceled)
            {
                outcome.Cancelled = true;
                outcome.ErrorKind = "timeout";
                outcome.Error = "已取消";
            }
            else if (we.Status == WebExceptionStatus.Timeout)
            {
                outcome.ErrorKind = "timeout";
                outcome.Error = DescribeError("timeout", we.Message);
            }
            else
            {
                outcome.ErrorKind = "network";
                outcome.Error = DescribeError("network", Redact(we.Message));
            }
        }

        private void ParseSuccess(string body, AiChatResult res)
        {
            object parsed;
            try { parsed = Ser.DeserializeObject(body); }
            catch
            {
                res.ErrorKind = "protocol";
                res.Error = DescribeError("protocol", Redact(body));
                return;
            }
            var dict = parsed as Dictionary<string, object>;
            if (dict == null)
            {
                res.ErrorKind = "protocol";
                res.Error = DescribeError("protocol", Redact(body));
                return;
            }
            string content = IsResponses() ? ExtractResponsesText(dict) : ExtractChatText(dict);
            if (content == null)
            {
                res.ErrorKind = "protocol";
                res.Error = DescribeError("protocol", Redact(body));
                return;
            }
            res.Ok = true;
            res.Content = content;
            var usage = GetDict(dict, "usage");
            if (usage != null)
            {
                res.PromptTokens = GetInt(usage, "prompt_tokens", GetInt(usage, "input_tokens", 0));
                res.CompletionTokens = GetInt(usage, "completion_tokens", GetInt(usage, "output_tokens", 0));
            }
        }

        private static string ExtractChatText(Dictionary<string, object> dict)
        {
            var choices = GetArray(dict, "choices");
            if (choices == null || choices.Length == 0) return null;
            var first = choices[0] as Dictionary<string, object>;
            if (first == null) return null;
            var msg = GetDict(first, "message");
            if (msg != null)
            {
                string content = ContentToString(GetValue(msg, "content"));
                if (content != null) return content;
                string reasoning = ContentToString(GetValue(msg, "reasoning_content"));
                if (reasoning != null) return reasoning;
            }
            return ContentToString(GetValue(first, "text"));
        }

        private static string ExtractResponsesText(Dictionary<string, object> dict)
        {
            string direct = GetString(dict, "output_text");
            if (direct != null) return direct;
            var output = GetArray(dict, "output");
            if (output == null) return null;
            var sb = new StringBuilder();
            foreach (var item in output)
            {
                var od = item as Dictionary<string, object>;
                if (od == null) continue;
                var contentArr = GetArray(od, "content");
                if (contentArr == null) continue;
                foreach (var part in contentArr)
                {
                    var pd = part as Dictionary<string, object>;
                    if (pd == null) continue;
                    string t = GetString(pd, "text");
                    if (!string.IsNullOrEmpty(t)) sb.Append(t);
                }
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string ContentToString(object v)
        {
            if (v == null) return null;
            if (v is string) return (string)v;
            var arr = v as object[];
            if (arr != null)
            {
                var sb = new StringBuilder();
                foreach (var item in arr)
                {
                    var d = item as Dictionary<string, object>;
                    if (d != null)
                    {
                        string t = GetString(d, "text");
                        if (!string.IsNullOrEmpty(t)) sb.Append(t);
                    }
                    else if (item is string) sb.Append((string)item);
                }
                return sb.ToString();
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null) return GetString(dict, "text");
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            try
            {
                using (Stream stream = resp.GetResponseStream())
                {
                    if (stream == null) return "";
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch { return ""; }
        }

        // ---------------- 脱敏 / 日志 ----------------

        private string Redact(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw;
            if (!string.IsNullOrEmpty(_provider.ApiKey)) s = s.Replace(_provider.ApiKey, "***");
            return Sanitize(s);
        }

        internal static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                s = Regex.Replace(s, @"sk-[A-Za-z0-9_\-]{6,}", "sk-***");
                s = Regex.Replace(s, @"(?i)bearer\s+[A-Za-z0-9_\-\.]{6,}", "Bearer ***");
                s = Regex.Replace(s, "(?i)(\"(?:api[_-]?key|authorization|token)\"\\s*:\\s*\")[^\"]{6,}", "$1***");
                s = Regex.Replace(s, @"(?i)\b(api[_-]?key|authorization|token)\b\s*[:=]\s*[^\s,;""}]{6,}", "$1: ***");
            }
            catch { }
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 300) s = s.Substring(0, 300) + "…";
            return s;
        }

        private string ProviderLabel()
        {
            string name = string.IsNullOrWhiteSpace(_provider.Name) ? "未命名" : _provider.Name;
            string model = string.IsNullOrWhiteSpace(_provider.Model) ? "(未设置模型)" : _provider.Model;
            return name + " / " + model + " / " + ProviderStore.MaskKey(_provider.ApiKey);
        }

        private void LogFailure(AiChatResult r)
        {
            if (r == null || r.Ok) return;
            AiLog.Write(r.Cancelled ? "warn" : "error",
                "AI 请求失败 [" + ProviderLabel() + "] " + r.ErrorKind + " - " + r.Error + "（" + r.ElapsedMs + "ms）");
        }

        // ---------------- JSON 取值小工具 ----------------

        private static object GetValue(Dictionary<string, object> d, string key)
        {
            if (d == null) return null;
            object v;
            return d.TryGetValue(key, out v) ? v : null;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            return GetValue(d, key) as Dictionary<string, object>;
        }

        private static object[] GetArray(Dictionary<string, object> d, string key)
        {
            return GetValue(d, key) as object[];
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v = GetValue(d, key);
            if (v == null) return null;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            object v = GetValue(d, key);
            if (v == null) return fallback;
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            if (v is double) return (int)(double)v;
            int n;
            if (int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            return fallback;
        }

        private class StreamOutcome
        {
            public bool Ok = false;
            public bool Cancelled = false;
            public bool AnyDelta = false;
            public string ErrorKind = null;
            public string Error = "";
            public int HttpStatus = 0;
        }
    }
}
