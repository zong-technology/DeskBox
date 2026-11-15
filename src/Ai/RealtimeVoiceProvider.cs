using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskBox.Ai
{
    // The owner runs one receive loop and serializes sends. Implementations own one session.
    internal interface IRealtimeVoiceProvider : IDisposable
    {
        int InputSampleRate { get; }
        int OutputSampleRate { get; }
        Task ConnectAsync(CancellationToken cancellation);
        Task SendAudioAsync(byte[] pcm16, CancellationToken cancellation);
        Task EndInputAsync(CancellationToken cancellation);
        Task<VoicePacket> ReceiveAsync(CancellationToken cancellation);
    }

    internal sealed class VoicePacket
    {
        public bool Ready, Interrupted, TurnComplete, ReconnectRequested;
        public string Caption;
        public readonly List<byte[]> Audio = new List<byte[]>();
        public bool InputEnded;
        public string UserCaption;
    }

    internal interface IVoiceInput : IDisposable { int Read(short[] samples); }
    internal interface IVoiceOutput : IDisposable { void Play(byte[] pcm); void Flush(); double? PlaybackLevel(); }

    internal sealed class GeminiLiveVoiceProvider : IRealtimeVoiceProvider
    {
        public const string DefaultEndpoint = "wss://generativelanguage.googleapis.com/ws/google.ai.generativelanguage.v1beta.GenerativeService.BidiGenerateContent";
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly Uri _endpoint;
        private readonly string _model, _key;


        public GeminiLiveVoiceProvider(string endpoint, string model, string apiKey)
        {
            Uri uri;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out uri) || uri.Scheme != "wss" ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
                throw new ArgumentException("语音服务器必须是无查询参数的 WSS 地址。");
            if (string.IsNullOrWhiteSpace(model) || string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("需要语音模型名称和 API 密钥。");
            _endpoint = uri;
            _model = model.Trim();
            _key = apiKey.Trim();
        }

        public int InputSampleRate { get { return 16000; } }
        public int OutputSampleRate { get { return 24000; } }

        public async Task ConnectAsync(CancellationToken cancellation)
        {
            var uri = new UriBuilder(_endpoint) { Query = "key=" + Uri.EscapeDataString(_key) };
            try
            {
                await _socket.ConnectAsync(uri.Uri, cancellation).ConfigureAwait(false);
                await SendAsync(SetupJson(_model), cancellation).ConfigureAwait(false);
                var packet = await ReceiveAsync(cancellation).ConfigureAwait(false);
                if (!packet.Ready) throw new InvalidDataException("Gemini Live 未确认会话设置。");
            }
            catch (WebSocketException)
            {
                // WebSocket exceptions can include the authenticated URI.
                throw new IOException("无法连接 Gemini Live，请检查网络、模型和 API 配额。");
            }
        }

        public Task SendAudioAsync(byte[] pcm16, CancellationToken cancellation)
        {
            return SendAsync(AudioJson(pcm16), cancellation);
        }

        public Task EndInputAsync(CancellationToken cancellation)
        {
            return SendAsync("{\"realtimeInput\":{\"audioStreamEnd\":true}}", cancellation);
        }

        public async Task<VoicePacket> ReceiveAsync(CancellationToken cancellation)
        {
            return ParsePacket(await VoiceWire.ReceiveAsync(_socket, cancellation).ConfigureAwait(false));
        }

        private Task SendAsync(string json, CancellationToken cancellation)
        {
            return VoiceWire.SendAsync(_socket, json, cancellation);
        }

        public void Dispose() { _socket.Dispose(); }

        internal static string SetupJson(string model)
        {
            return new JavaScriptSerializer().Serialize(new { setup = new {
                model = model.StartsWith("models/", StringComparison.Ordinal) ? model : "models/" + model,
                generationConfig = new { responseModalities = new[] { "AUDIO" } },
                outputAudioTranscription = new object(),
                inputAudioTranscription = new object(),
                systemInstruction = new { parts = new[] { new { text = "默认使用简体中文回答。没有设备实时数据时，明确说明是工程示意。" } } }
            } });
        }

        internal static string AudioJson(byte[] pcm16)
        {
            if (pcm16 == null || pcm16.Length == 0 || pcm16.Length % 2 != 0)
                throw new ArgumentException("输入必须是完整的 PCM16 采样。");
            return "{\"realtimeInput\":{\"audio\":{\"mimeType\":\"audio/pcm;rate=16000\",\"data\":\"" +
                Convert.ToBase64String(pcm16) + "\"}}}";
        }

        internal static VoicePacket ParsePacket(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            if (root == null || Read(root, "error") != null)
                throw new InvalidDataException("语音服务返回错误。");
            var content = Read(root, "serverContent") as IDictionary<string, object>;
            var packet = new VoicePacket {
                Ready = Read(root, "setupComplete") != null,
                ReconnectRequested = Read(root, "goAway") != null,
                Interrupted = object.Equals(Read(content, "interrupted"), true),
                TurnComplete = object.Equals(Read(content, "turnComplete"), true)
            };
            var transcription = Read(content, "outputTranscription") as IDictionary<string, object>;
            packet.Caption = Read(transcription, "text") as string;
            var input = Read(content, "inputTranscription") as IDictionary<string, object>;
            packet.UserCaption = Read(input, "text") as string;
            // On interruption the owner must flush its existing playback queue too.
            if (packet.Interrupted) return packet;
            var turn = Read(content, "modelTurn") as IDictionary<string, object>;
            var parts = Read(turn, "parts") as IList;
            if (parts == null) return packet;
            foreach (var part in parts)
            {
                var blob = Read(part as IDictionary<string, object>, "inlineData") as IDictionary<string, object>;
                var mime = Read(blob, "mimeType") as string;
                var data = Read(blob, "data") as string;
                if (data == null || mime == null) continue;
                if (!mime.StartsWith("audio/pcm", StringComparison.OrdinalIgnoreCase)) continue;
                if (!mime.Equals("audio/pcm", StringComparison.OrdinalIgnoreCase) &&
                    !mime.Equals("audio/pcm;rate=24000", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("语音服务输出采样率不受支持。");
                var audio = Convert.FromBase64String(data);
                if (audio.Length % 2 != 0) throw new InvalidDataException("音频采样块不完整。");
                if (audio.Length != 0) packet.Audio.Add(audio);
            }
            return packet;
        }

        private static object Read(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }
    }

    internal static class VoiceWire
    {
        internal static Task SendAsync(ClientWebSocket socket, string json, CancellationToken token)
        {
            return socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
                WebSocketMessageType.Text, true, token);
        }

        internal static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                    if (part.MessageType == WebSocketMessageType.Close)
                        throw new IOException("语音连接已关闭。");
                    if (message.Length + part.Count > 2 * 1024 * 1024)
                        throw new InvalidDataException("语音服务器消息过大。");
                    message.Write(buffer, 0, part.Count);
                } while (!part.EndOfMessage);
                return Encoding.UTF8.GetString(message.ToArray());
            }
        }
    }
}
