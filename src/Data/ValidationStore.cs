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
    internal sealed class ValidationStore
    {
        internal const string FolderName = "validation";
        private sealed class Envelope
        {
            public int Version;
            public string Payload, Hash;
        }

        private sealed class Payload
        {
            public string ProjectId, Stamp;
            public List<Entry> Records, History;
        }

        private sealed class Entry
        {
            public string Kind, Json;
        }

        private static readonly Dictionary<string, Type> Types = new[]
        {
            typeof(ValidationPlan), typeof(ValidationDocument), typeof(ValidationRequirement), typeof(DesignReference),
            typeof(RiskAssessment), typeof(TestProtocol), typeof(TestCase), typeof(TestExecution), typeof(Evidence), typeof(Deviation),
            typeof(CAPARecord), typeof(PunchItem), typeof(ReviewRecord), typeof(TraceLink), typeof(ChangeImpact),
            typeof(ValidationBaseline), typeof(Deliverable), typeof(ComplianceCheck)
        }.ToDictionary(t => t.Name);

        private readonly string root;

        internal ValidationStore(string directory) { root = Path.GetFullPath(Path.Combine(directory, FolderName)); }

        internal string ProjectDirectory(string projectId) { return Path.Combine(root, ProjectKey(projectId)); }

        private static string ProjectKey(string projectId) { return PlcEngineering.Hash(projectId).Substring(0, 32); }

        internal T Access<T>(string projectId, CancellationToken token, Func<string, T> callback)
        {
            return WithDirectory(ProjectDirectory(projectId), token, callback);
        }

        internal static T WithDirectory<T>(string directory, CancellationToken token, Func<string, T> callback)
        {
            directory = Path.GetFullPath(directory);
            var normalized = Path.DirectorySeparatorChar == '\\' ? directory.ToUpperInvariant() : directory;
            using (var mutex = new Mutex(false, "DeskBox.Validation." + PlcEngineering.Hash(normalized)))
            {
                token.ThrowIfCancellationRequested();
                try { if (WaitHandle.WaitAny(new WaitHandle[] { mutex, token.WaitHandle }) != 0) token.ThrowIfCancellationRequested(); }
                catch (AbandonedMutexException) { }
                try { token.ThrowIfCancellationRequested(); return callback(directory); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        internal ValidationSnapshot Load(string directory, string projectId)
        {
            string path = Path.Combine(directory, "records.json");
            var found = Read(path, projectId);
            if (found != null) return found;
            found = Read(path + ".bak", projectId);
            if (found != null) { found.Recovered = true; return found; }
            if (File.Exists(path) || File.Exists(path + ".bak")) throw new InvalidDataException("验证台账无法校验或没有可恢复的备份。");
            return new ValidationSnapshot { ProjectId = projectId };
        }

        internal static List<Evidence> BackupEvidence(string directory)
        {
            var records = Path.Combine(directory, "records.json");
            foreach (var path in new[] { records, records + ".bak" })
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("验证台账不能使用重解析路径。");
            var primary = Read(records, null);
            var previous = Read(records + ".bak", null);
            if (primary == null && previous == null &&
                (File.Exists(records) || File.Exists(records + ".bak")))
                throw new InvalidDataException("验证台账及其上一版本均不可读。");
            var sources = new[] { primary, previous }.Where(snapshot => snapshot != null).ToArray();
            if (sources.Any(snapshot => !string.Equals(Path.GetFileName(directory), ProjectKey(snapshot.ProjectId), StringComparison.Ordinal)))
                throw new InvalidDataException("验证台账的项目标识与备份目录不符。");
            return sources.SelectMany(snapshot => snapshot.Records.Concat(snapshot.History)).OfType<Evidence>().ToList();
        }

        internal void Save(string directory, ValidationSnapshot snapshot, CancellationToken token, bool backup = true)
        {
            var records = new Payload { ProjectId = snapshot.ProjectId, Stamp = Guid.NewGuid().ToString("N"),
                Records = snapshot.Records.Select(Row).ToList(), History = snapshot.History.Select(Row).ToList() };
            string payload = Json.ToJson(records);
            if (payload.Length > 16 * 1024 * 1024) throw new IOException("验证台账达到单项目容量上限。变更未保存。");
            var envelope = new Envelope { Version = 1, Payload = payload, Hash = PlcEngineering.Hash(payload) };
            token.ThrowIfCancellationRequested();
            Json.SaveAtomic(Path.Combine(directory, "records.json"), envelope, backup);
            snapshot.Stamp = records.Stamp;
        }

        private static ValidationSnapshot Read(string path, string projectId)
        {
            var envelope = Json.Load<Envelope>(path, null);
            if (envelope == null) return null;
            if (envelope.Version > 1) throw new InvalidDataException("验证台账来自更高版本，禁止覆盖。");
            if (envelope.Version != 1 || string.IsNullOrEmpty(envelope.Payload) ||
                PlcEngineering.Hash(envelope.Payload) != envelope.Hash) return null;
            var payload = Json.FromJson<Payload>(envelope.Payload);
            if (payload == null || string.IsNullOrEmpty(payload.ProjectId) || projectId != null && payload.ProjectId != projectId ||
                string.IsNullOrEmpty(payload.Stamp) || payload.Records == null || payload.History == null)
                return null;
            try
            {
                var result = new ValidationSnapshot { ProjectId = payload.ProjectId, Stamp = payload.Stamp,
                    Records = payload.Records.Select(Parse).ToList(), History = payload.History.Select(Parse).ToList() };
                var current = result.Records.ToDictionary(item => item.Id);
                Guid id;
                if (result.Records.Concat(result.History).Any(item => item.ProjectId != payload.ProjectId || item.Revision <= 0 ||
                        item.Source == null || !Guid.TryParseExact(item.Id, "N", out id)) ||
                    result.History.Any(item => !current.ContainsKey(item.Id) || current[item.Id].Revision <= item.Revision))
                    return null;
                return result;
            }
            catch (ArgumentException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (System.Reflection.TargetInvocationException) { return null; }
        }

        private static Entry Row(ValidationRecord value)
        {
            return new Entry { Kind = value.GetType().Name, Json = Json.ToJson(value) };
        }

        private static ValidationRecord Parse(Entry entry)
        {
            Type type;
            if (entry == null || string.IsNullOrEmpty(entry.Kind) || !Types.TryGetValue(entry.Kind, out type))
                throw new InvalidDataException("未知验证记录类型。");
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
            var record = serializer.Deserialize(entry.Json, type) as ValidationRecord;
            if (record == null) throw new InvalidDataException("验证记录损坏。");
            return record;
        }
    }
}
