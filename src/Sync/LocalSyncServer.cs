using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Sync
{
    public class PairRequest { public string Code = ""; public string DeviceName = ""; public string Platform = "ios"; }
    public class PairResponse { public bool Ok; public string DeviceId = ""; public string AccessToken = ""; public long ExpiresAt; public string Error = ""; }
    public class SyncPushResponse { public bool Ok; public string Result = ""; }

    /// <summary>Small TLS HTTP/1.1 server for local pairing and record sync.</summary>
    public sealed class LocalSyncServer : IDisposable
    {
        private readonly X509Certificate2 _certificate;
        private readonly SyncStore _sync;
        private readonly DeviceTrustStore _devices;
        private readonly IPAddress _address;
        private readonly int _requestedPort;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _clients;
        public event Action BeforePull;
        public event Action<SyncRecord, SyncApplyResult> RecordMerged;

        public LocalSyncServer(X509Certificate2 certificate, SyncStore sync, DeviceTrustStore devices, IPAddress address, int port)
        {
            if (certificate == null || !certificate.HasPrivateKey) throw new ArgumentException("TLS certificate with private key required");
            _certificate = certificate; _sync = sync; _devices = devices; _address = address ?? IPAddress.Loopback; _requestedPort = port;
        }

        public bool Running { get { return _running; } }
        public int Port { get; private set; }
        public string CertificateThumbprint { get { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(_certificate.RawData)).Replace("-", ""); } }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(_address, _requestedPort); _listener.Start(); Port = ((IPEndPoint)_listener.LocalEndpoint).Port; _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "DeskBox-LocalSync" }; _acceptThread.Start();
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try { client = _listener.AcceptTcpClient(); }
                catch { if (!_running) break; }
                if (client == null) continue;
                if (Interlocked.Increment(ref _clients) > 8) { Interlocked.Decrement(ref _clients); client.Close(); continue; }
                ThreadPool.QueueUserWorkItem(delegate { try { Handle(client); } catch (Exception ex) { AppLog.Write("warn", "sync", "客户端连接已结束：" + ex.GetType().Name); } finally { try { client.Close(); } catch { } Interlocked.Decrement(ref _clients); } });
            }
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 10000; client.SendTimeout = 10000;
            using (var tls = new SslStream(client.GetStream(), false))
            {
                try { tls.AuthenticateAsServer(_certificate, false, SslProtocols.Tls12, false); }
                catch { return; }
                Request request;
                try { request = ReadRequest(tls); } catch { Write(tls, 400, new { error = "bad_request" }); return; }
                if (request == null) return;
                Route(tls, request);
            }
        }

        private void Route(Stream stream, Request request)
        {
            if (request.Method == "GET" && request.Path == "/health") { Write(stream, 200, new { ok = true, service = "DeskBox Local Sync", thumbprint = CertificateThumbprint }); return; }
            if (request.Method == "POST" && request.Path == "/pair")
            {
                var p = Json.FromJson<PairRequest>(request.Body);
                var credential = p == null ? null : _devices.CompletePairing(p.Code, p.DeviceName, p.Platform, 30);
                Write(stream, credential == null ? 401 : 200, credential == null ? new PairResponse { Error = "invalid_or_expired_code" } : new PairResponse { Ok = true, DeviceId = credential.DeviceId, AccessToken = credential.AccessToken, ExpiresAt = credential.ExpiresAt }); return;
            }
            string deviceId, token;
            if (!request.Headers.TryGetValue("x-device-id", out deviceId) || !request.Headers.TryGetValue("authorization", out token) || !token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || !_devices.Validate(deviceId, token.Substring(7)))
            { Write(stream, 401, new { error = "unauthorized" }); return; }
            if (request.Method == "POST" && request.Path == "/device/renew")
            {
                var expiry = _devices.ExtendTrust(deviceId, token.Substring(7));
                Write(stream, expiry > 0 ? 200 : 401, new { ExpiresAt = expiry }); return;
            }
            if (request.Method == "GET" && request.Path.StartsWith("/sync/pull", StringComparison.Ordinal))
            {
                var before = BeforePull; if (before != null) try { before(); } catch { }
                long since = 0; var i = request.Path.IndexOf("since=", StringComparison.OrdinalIgnoreCase); if (i >= 0) long.TryParse(request.Path.Substring(i + 6).Split('&')[0], out since);
                Write(stream, 200, _sync.Records.Where(x => x.UpdatedAt > since).ToList()); return;
            }
            if (request.Method == "POST" && request.Path == "/sync/push")
            {
                var record = Json.FromJson<SyncRecord>(request.Body);
                if(record!=null&&record.EntityType=="incident"&&!record.Deleted){var item=Json.FromJson<IncidentDto>(record.Json);if(!LocalSyncCoordinator.ValidIncident(item)||item.Id!=record.Id){Write(stream,400,new SyncPushResponse{Ok=false,Result="Invalid"});return;}}
                var result = _sync.MergeRemote(record);
                var merged = RecordMerged; if (merged != null && result == SyncApplyResult.Applied) try { merged(record, result); } catch { }
                Write(stream, result == SyncApplyResult.Invalid ? 400 : 200, new SyncPushResponse { Ok = result != SyncApplyResult.Invalid, Result = result.ToString() }); return;
            }
            Write(stream, 404, new { error = "not_found" });
        }

        private static Request ReadRequest(Stream stream)
        {
            var header = new MemoryStream(); int state = 0;
            while (header.Length < 65536)
            {
                int b = stream.ReadByte(); if (b < 0) return null; header.WriteByte((byte)b);
                state = state == 0 && b == 13 ? 1 : state == 1 && b == 10 ? 2 : state == 2 && b == 13 ? 3 : state == 3 && b == 10 ? 4 : 0;
                if (state == 4) break;
            }
            if (state != 4) throw new InvalidDataException();
            var text = Encoding.ASCII.GetString(header.ToArray()); var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None); var first = lines[0].Split(' ');
            if (first.Length < 2) throw new InvalidDataException();
            var request = new Request { Method = first[0].ToUpperInvariant(), Path = first[1] };
            for (int x = 1; x < lines.Length; x++) { int colon = lines[x].IndexOf(':'); if (colon > 0) request.Headers[lines[x].Substring(0, colon).Trim().ToLowerInvariant()] = lines[x].Substring(colon + 1).Trim(); }
            string lengthText; int length = request.Headers.TryGetValue("content-length", out lengthText) ? int.Parse(lengthText) : 0; if (length < 0 || length > 2 * 1024 * 1024) throw new InvalidDataException();
            var body = new byte[length]; int read = 0; while (read < length) { int n = stream.Read(body, read, length - read); if (n <= 0) throw new EndOfStreamException(); read += n; }
            request.Body = Encoding.UTF8.GetString(body); return request;
        }

        private static void Write(Stream stream, int status, object value)
        {
            var body = Encoding.UTF8.GetBytes(Json.ToJson(value)); var reason = status == 200 ? "OK" : status == 400 ? "Bad Request" : status == 401 ? "Unauthorized" : "Not Found";
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length); stream.Flush();
        }

        public void Stop() { _running = false; try { if (_listener != null) _listener.Stop(); } catch { } try { if (_acceptThread != null) _acceptThread.Join(2000); } catch { } }
        public void Dispose() { Stop(); }
        private class Request { public string Method = ""; public string Path = ""; public string Body = ""; public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }
}
