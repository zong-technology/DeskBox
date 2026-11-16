using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskBox.Ai
{
    internal sealed class QwenLiveVoiceProvider : IRealtimeVoiceProvider
    {
        public const string DefaultModel = "qwen3.8-omni-flash-realtime";
        public const string DefaultEndpoint = "wss://YOUR-WORKSPACE.cn-beijing.maas.aliyuncs.com/api-ws/v1/realtime";
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly Uri _endpoint;
        private readonly string _model;
        private readonly RealtimeVoiceSettings _profile;
        private bool _discardAudio;
        private readonly List<VoiceToolCall> _pendingTools=new List<VoiceToolCall>();

        public QwenLiveVoiceProvider(string endpoint, string model, string key, RealtimeVoiceSettings profile = null)
        {
            _endpoint = Endpoint(endpoint);
            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("请填写百炼模型 ID 和 API Key。");
            _model = model.Trim();
            _profile=profile ?? new RealtimeVoiceSettings();
            _socket.Options.SetRequestHeader("Authorization", "Bearer " + key.Trim());
        }

        public int InputSampleRate { get { return 16000; } }
        public int OutputSampleRate { get { return 24000; } }

        public async Task ConnectAsync(CancellationToken token)
        {
            try
            {
                var target = new UriBuilder(_endpoint) { Query = "model=" + Uri.EscapeDataString(_model) };
                await _socket.ConnectAsync(target.Uri, token).ConfigureAwait(false);
                await SendAsync(Setup(_model,_profile), token).ConfigureAwait(false);
                while (!(await ReceiveAsync(token).ConfigureAwait(false)).Ready) { }
            }
            catch (WebSocketException) { throw new IOException("无法连接百炼，请检查工作空间地址、API Key 和模型权限。"); }
        }

        public Task SendAudioAsync(byte[] pcm16, CancellationToken token)
        {
            return SendAsync(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(pcm16) }, token);
        }

        public Task EndInputAsync(CancellationToken token)
        {
            return SendAsync(new { type = "input_audio_buffer.clear" }, token);
        }

        public async Task CompleteToolsAsync(IList<VoiceToolResult> results, CancellationToken token)
        {
            foreach(var result in results)await SendAsync(ToolOutput(result),token).ConfigureAwait(false);
            await SendAsync(new {type="response.create"},token).ConfigureAwait(false);
        }

        internal static object ToolOutput(VoiceToolResult result)
        {
            return new {type="conversation.item.create",item=new {type="function_call_output",call_id=result.Id,
                output=new JavaScriptSerializer().Serialize(result.Response)}};
        }

        private Task SendAsync(object payload, CancellationToken token)
        {
            return VoiceWire.SendAsync(_socket, new JavaScriptSerializer().Serialize(payload), token);
        }

        public async Task<VoicePacket> ReceiveAsync(CancellationToken token)
        {
            return ParsePacket(await VoiceWire.ReceiveAsync(_socket, token).ConfigureAwait(false));
        }

        internal VoicePacket ParsePacket(string json)
        {
            var evt = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            var type = Read(evt, "type") as string;
            if (type == "error") throw new InvalidDataException("百炼拒绝了语音请求，请检查模型权限和会话配置。");
            var packet = new VoicePacket { Ready = type == "session.updated", TurnComplete = type == "response.done", ResponseStarted=type=="response.created" };
            if (type == "response.created") {_discardAudio = false;_pendingTools.Clear();}
            var response = Read(evt, "response") as IDictionary<string, object>;
            if (type == "response.done" && object.Equals(Read(response, "status"), "failed"))
                throw new InvalidDataException("百炼语音回复失败，请检查配额和模型权限。");
            if (type == "input_audio_buffer.speech_started" ||
                (type == "response.done" && object.Equals(Read(response, "status"), "cancelled")))
            {
                packet.Interrupted = true;
                _discardAudio = true;
                _pendingTools.Clear();
            }
            packet.InputEnded = type == "input_audio_buffer.speech_stopped";
            packet.InputStarted=type=="input_audio_buffer.speech_started";
            if(packet.InputStarted)packet.UserTurnId=Read(evt,"item_id") as string;
            if (type == "conversation.item.input_audio_transcription.completed") {
                packet.UserCaption = Read(evt, "transcript") as string;packet.UserTurnId=Read(evt,"item_id") as string;
            }
            if(type=="response.function_call_arguments.done" && !_discardAudio){
                IDictionary<string,object> arguments=null;
                try {arguments=new JavaScriptSerializer().DeserializeObject(Convert.ToString(Read(evt,"arguments"))) as IDictionary<string,object>;}
                catch(ArgumentException) { }
                if(_pendingTools.Count>=8)throw new InvalidDataException("一次回复的工具请求过多。");
                _pendingTools.Add(new VoiceToolCall {Id=Read(evt,"call_id") as string,Name=Read(evt,"name") as string,Arguments=arguments});
            }
            // Wait for the current response to finish before returning outputs and requesting one continuation.
            if(packet.TurnComplete && !_discardAudio){packet.ToolCalls.AddRange(_pendingTools);_pendingTools.Clear();}
            if (type == "response.audio_transcript.delta" && !_discardAudio)
                packet.Caption = Read(evt, "delta") as string;
            if (type == "response.audio.delta" && !_discardAudio)
            {
                var bytes = Convert.FromBase64String((string)Read(evt, "delta"));
                if (bytes.Length % 2 != 0) throw new InvalidDataException("百炼返回了不完整的 PCM16 音频。");
                if (bytes.Length != 0) packet.Audio.Add(bytes);
            }
            return packet;
        }

        internal static object Setup(string model, RealtimeVoiceSettings profile = null)
        {
            string instructions=(profile ?? new RealtimeVoiceSettings()).Instructions;
            if (model.StartsWith("qwen3.8-", StringComparison.OrdinalIgnoreCase))
            {
                var session = new Dictionary<string, object> {
                    { "modalities", new[] { "text", "audio" } },
                    { "instructions", instructions },
                    { "tools", HologramTools.QwenDeclarations() },
                    { "input_audio_transcription", new {model="qwen3-asr-flash-realtime"} },
                    { "turn_detection", new { type = "semantic_vad_ec" } },
                    { "audio", new {
                        input = new { format = new { type = "pcm", sample_rate = 16000 } },
                        output = new { voice = "Tina", format = new { type = "pcm", sample_rate = 24000 } }
                    } }
                };
                return new { type = "session.update", session };
            }
            bool supportsTools=model.StartsWith("qwen3.5-",StringComparison.OrdinalIgnoreCase);
            var legacy=new Dictionary<string,object> {
                {"modalities",new[]{"text","audio"}}, {"instructions",instructions+(supportsTools ? "" : "\n此模型不提供工具：不能查询软件数据或实际展示信息，请如实说明能力范围。")},
                {"voice",supportsTools ? "Tina" : "Cherry"}, {"input_audio_transcription",new {model="qwen3-asr-flash-realtime"}},
                {"turn_detection",new {type="server_vad"}}, {"input_audio_format","pcm"}, {"output_audio_format","pcm"}
            };
            if(supportsTools)legacy["tools"]=HologramTools.QwenDeclarations();
            return new {type="session.update",session=legacy};
        }

        internal static Uri Endpoint(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "wss" ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new ArgumentException("请填写不含参数的 WSS 地址，模型 ID 单独填写。");
            if (uri.Host.StartsWith("your-workspace.", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请把 WSS 地址中的 YOUR-WORKSPACE 换成你的百炼业务空间 ID。");
            return uri;
        }

        internal static async Task<string[]> ListModelsAsync(string endpoint, string key, CancellationToken token)
        {
            var origin = Endpoint(endpoint);
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("请先填写百炼 API Key。");
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int pageSize = 20;
            for (int page = 1; ; page++)
            {
                var url = new UriBuilder(origin) { Scheme = "https", Port = origin.IsDefaultPort ? -1 : origin.Port,
                    Path = "/api/v1/models", Query = "providers=qwen&capabilities=Realtime-Omni&page_size=" + pageSize + "&page_no=" + page };
                var json = await VoiceModelCatalog.GetAsync(url.Uri, "Authorization", "Bearer " + key.Trim(), token).ConfigureAwait(false);
                int total;
                var models = ReadCatalogPage(json, out total);
                foreach (var id in models) result.Add(id);
                if ((long)page * pageSize >= total) break;
            }
            return result.OrderBy(x => x).ToArray();
        }

        internal static string[] ReadCatalogPage(string json, out int total)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            var output = Read(root, "output") as IDictionary<string, object>;
            var models = Read(output, "models") as IList;
            if (models == null || !int.TryParse(Convert.ToString(Read(output, "total")), out total) || total < 0)
                throw new InvalidDataException("百炼未返回可识别的模型目录。");
            return models.Cast<object>().Select(m => Read(m as IDictionary<string, object>, "model") as string)
                .Where(id => !string.IsNullOrWhiteSpace(id) && id.IndexOf("-omni-", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    id.IndexOf("-realtime", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        }

        internal static object Read(IDictionary<string, object> data, string key)
        {
            object value;
            return data != null && data.TryGetValue(key, out value) ? value : null;
        }

        public void Dispose() { _socket.Dispose(); }
    }

    internal static class VoiceModelCatalog
    {
        internal static async Task<string[]> ListGeminiAsync(string endpoint, string key, CancellationToken token)
        {
            if (endpoint.Trim() != GeminiLiveVoiceProvider.DefaultEndpoint)
                throw new ArgumentException("自定义 Gemini 地址请手动填写模型 ID。");
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("请先填写 Gemini API Key。");
            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pages = new HashSet<string>(StringComparer.Ordinal);
            string next = "";
            do
            {
                var url = new Uri("https://generativelanguage.googleapis.com/v1beta/models?pageSize=100" +
                    (string.IsNullOrEmpty(next) ? "" : "&pageToken=" + Uri.EscapeDataString(next)));
                var json = await GetAsync(url, "x-goog-api-key", key.Trim(), token).ConfigureAwait(false);
                var root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
                if (root == null) throw new InvalidDataException("Gemini 未返回可识别的模型目录。");
                var entries = QwenLiveVoiceProvider.Read(root, "models") as IList;
                if (entries != null)
                    foreach (var entry in entries)
                    {
                        var model = entry as IDictionary<string, object>;
                        var methods = QwenLiveVoiceProvider.Read(model, "supportedGenerationMethods") as IList;
                        var name = QwenLiveVoiceProvider.Read(model, "name") as string;
                        if (!string.IsNullOrWhiteSpace(name) && methods != null && methods.Cast<object>().Any(
                            method => string.Equals(method as string, "bidiGenerateContent", StringComparison.OrdinalIgnoreCase)))
                            models.Add(name);
                    }
                next = QwenLiveVoiceProvider.Read(root, "nextPageToken") as string;
                if (!string.IsNullOrEmpty(next) && !pages.Add(next))
                    throw new InvalidDataException("Gemini 模型目录返回了重复的分页标记。");
            } while (!string.IsNullOrEmpty(next));
            return models.OrderBy(x => x).ToArray();
        }

        internal static async Task<string> GetAsync(Uri url, string header, string value, CancellationToken token)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.AllowAutoRedirect = false;
            request.Headers[header] = value;
            try
            {
                using (token.Register(request.Abort))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.OK)
                        throw CatalogHttpError((int)response.StatusCode);
                    using (var reader = new StreamReader(response.GetResponseStream()))
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                }
            }
            catch (WebException error)
            {
                token.ThrowIfCancellationRequested();
                using (var response = error.Response as HttpWebResponse)
                {
                    if (response != null) throw CatalogHttpError((int)response.StatusCode);
                }
                throw new IOException("连接模型目录失败（" + error.Status + "）。");
            }
        }

        private static IOException CatalogHttpError(int status)
        {
            string message;
            switch (status)
            {
                case 400: message = "目录请求参数无效，请核对工作空间地址。"; break;
                case 401: message = "API Key 未通过认证，请核对密钥及其所属平台和地域。"; break;
                case 403: message = "账号无权读取该目录，请核对业务空间和 API Key 权限。"; break;
                case 404: message = "未找到模型目录，请核对业务空间 ID 和地域。"; break;
                case 429: message = "目录请求已限流，请稍后重试。"; break;
                default: message = "模型目录请求失败。"; break;
            }
            return new IOException("HTTP " + status + " · " + message);
        }
    }
}
