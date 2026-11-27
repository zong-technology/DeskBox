using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering.Tia;

namespace DeskBox.Data
{
    internal sealed class TagLedger
    {
        public string ProjectId = "", Stamp = "";
        public List<EngineeringTag> Tags = new List<EngineeringTag>();
        public List<EngineeringTag> History = new List<EngineeringTag>();
    }

    internal sealed class TagEnvelope
    {
        public int Version;
        public string Payload = "", Sha256 = "";
    }

    internal sealed class EngineeringTagStore
    {
        private readonly string root;

        internal EngineeringTagStore(string dataDirectory)
        {
            root = Path.GetFullPath(Path.Combine(dataDirectory, "engineering-tags"));
        }

        internal T Access<T>(string projectId, CancellationToken token, Func<string, T> handler)
        {
            if (string.IsNullOrWhiteSpace(projectId)) throw new UnauthorizedAccessException("Tag 台账需要已选择的项目。");
            string file = Path.Combine(root, PlcEngineering.Hash(projectId) + ".json");
            var mutexPath = Path.DirectorySeparatorChar == '\\' ? file.ToUpperInvariant() : file;
            using (var mutex = new Mutex(false, "DeskBox.EngineeringTags." + PlcEngineering.Hash(mutexPath)))
            {
                if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();
                try
                {
                    if (WaitHandle.WaitAny(new WaitHandle[] { mutex, token.WaitHandle }) != 0) token.ThrowIfCancellationRequested();
                }
                catch (AbandonedMutexException) { }
                try { token.ThrowIfCancellationRequested(); return handler(file); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        internal TagLedger Load(string file, string projectId, out bool recovered)
        {
            recovered = false;
            bool primary = File.Exists(file);
            string backup = file + ".bak";
            var main = Read(file, projectId);
            if (main != null) return main;
            if (!primary && !File.Exists(backup)) return new TagLedger { ProjectId = projectId };
            main = Read(backup, projectId);
            if (main == null) throw new InvalidDataException("Tag 台账损坏，无可用备份。原文件保留。");
            recovered = true;
            return main;
        }

        private static TagLedger Read(string file, string projectId)
        {
            var record = Json.Load<TagEnvelope>(file, null);
            if (record == null) return null;
            if (record.Version > 1) throw new InvalidDataException("Tag 台账来自不支持的未来版本。");
            if (record.Version != 1 || string.IsNullOrWhiteSpace(record.Payload) || PlcEngineering.Hash(record.Payload) != record.Sha256) return null;
            var result = Json.FromJson<TagLedger>(record.Payload);
            if (result == null || result.ProjectId != projectId || string.IsNullOrEmpty(result.Stamp) || result.Tags == null || result.History == null)
                return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (result.Tags.Any(tag => !Valid(tag, projectId) || !ids.Add(tag.TagId))
                || result.History.Any(tag => !Valid(tag, projectId) || !ids.Contains(tag.TagId))) return null;
            return result;
        }

        private static bool Valid(EngineeringTag tag, string project)
        {
            Guid id;
            return tag != null && tag.ProjectId == project && Guid.TryParseExact(tag.TagId, "N", out id) &&
                !string.IsNullOrWhiteSpace(tag.TagName) && !string.IsNullOrWhiteSpace(tag.SystemId) &&
                tag.Revision >= 1 && tag.Sources != null && tag.Relations != null;
        }

        internal void Save(string file, TagLedger ledger, CancellationToken token, bool backup = true)
        {
            ledger.Stamp = Guid.NewGuid().ToString("N");
            var payload = Json.ToJson(ledger);
            if (payload.Length > 22 * 1024 * 1024) throw new IOException("Tag 台账含版本数据，已达到单项目存储限额。变更未保存。");
            var envelope = new TagEnvelope { Version = 1, Payload = payload, Sha256 = PlcEngineering.Hash(payload) };
            token.ThrowIfCancellationRequested();
            Json.SaveAtomic(file, envelope, backup);
        }
    }
}
