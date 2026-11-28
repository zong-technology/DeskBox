using System;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering.Tia;

namespace DeskBox.Data
{
    internal sealed class InstrumentStore
    {
        private sealed class Envelope
        {
            public int Version;
            public string Payload, Sha256;
        }

        private readonly string root;

        internal InstrumentStore(string dataDirectory)
        {
            root = Path.GetFullPath(Path.Combine(dataDirectory, "instruments"));
        }

        internal T Access<T>(string projectId, CancellationToken token, Func<string, T> action)
        {
            string path = Path.Combine(root, PlcEngineering.Hash(projectId).Substring(0, 32) + ".json");
            string lockPath = Path.DirectorySeparatorChar == '\\' ? path.ToUpperInvariant() : path;
            using (var mutex = new Mutex(false, "DeskBox.Instruments." + PlcEngineering.Hash(lockPath)))
            {
                token.ThrowIfCancellationRequested();
                try { if (WaitHandle.WaitAny(new WaitHandle[] { mutex, token.WaitHandle }) != 0) token.ThrowIfCancellationRequested(); }
                catch (AbandonedMutexException) { }
                try { token.ThrowIfCancellationRequested(); return action(path); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        internal static InstrumentRegister Load(string path, string projectId)
        {
            var current = Read(path, projectId);
            if (current != null) return current;
            var previous = Read(path + ".bak", projectId);
            if (previous != null) { previous.Recovered = true; return previous; }
            if (File.Exists(path) || File.Exists(path + ".bak") || Directory.Exists(path) || Directory.Exists(path + ".bak"))
                throw new InvalidDataException("无法校验仪表台账，且没有可读取的上一版。原文件已保留。");
            return new InstrumentRegister { ProjectId = projectId };
        }

        private static InstrumentRegister Read(string path, string projectId)
        {
            var envelope = Json.Load<Envelope>(path, null);
            if (envelope == null) return null;
            if (envelope.Version > 1) throw new InvalidDataException("仪表台账来自更高版本。");
            if (envelope.Version != 1 || string.IsNullOrEmpty(envelope.Payload) ||
                PlcEngineering.Hash(envelope.Payload) != envelope.Sha256) return null;
            var data = Json.FromJson<InstrumentRegister>(envelope.Payload);
            if (data == null || data.ProjectId != projectId || data.Assets == null || data.History == null || data.Activities == null)
                return null;
            if (data.Assets.Concat(data.History).Any(asset => asset == null || asset.ProjectId != projectId ||
                !ValidId(asset.Id) || !ValidId(asset.RevisionToken) || string.IsNullOrWhiteSpace(asset.Number) || asset.Revision < 1)) return null;
            var ids = data.Assets.Select(asset => asset.Id).ToList();
            if (ids.Distinct().Count() != ids.Count) return null;
            var assets = data.Assets.ToDictionary(asset => asset.Id);
            if (data.History.Any(item => !assets.ContainsKey(item.Id) || assets[item.Id].Revision <= item.Revision) ||
                data.Activities.Any(item => item == null || item.ProjectId != projectId || !ValidId(item.Id) || !assets.ContainsKey(item.AssetId)) ||
                data.Activities.Select(item => item.Id).Distinct().Count() != data.Activities.Count) return null;
            return data;
        }

        private static bool ValidId(string value)
        {
            Guid id;
            return Guid.TryParseExact(value, "N", out id);
        }

        internal static void Save(string path, InstrumentRegister data, CancellationToken token, bool backup)
        {
            string payload = Json.ToJson(data);
            if (payload.Length > 16 * 1024 * 1024) throw new IOException("仪表台账达到单项目容量上限。");
            token.ThrowIfCancellationRequested();
            Json.SaveAtomic(path, new Envelope { Version = 1, Payload = payload, Sha256 = PlcEngineering.Hash(payload) }, backup);
        }
    }
}
