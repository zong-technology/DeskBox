using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Engineering.Tia
{
    public enum TiaProgrammingScope { Draft, NewFB, NewFC, NewDB, NewUDT, EditBlock, Analyze, Variables, Hardware }

    public sealed class TiaProgrammingSource
    {
        public string Name = "", Version = "", Origin = "", Fingerprint = "", Copy = "", ReadUtc = "";
        public string[] Files = new string[0], Cpus = new string[0];
        public bool Native;

        public static string ExportFingerprint(IEnumerable<string> files, CancellationToken token)
        {
            var entries = new List<string>();
            long total = 0;
            foreach (var path in files.Select(PlcEngineering.FullLocalPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var hash = SHA256.Create())
                {
                    if (entries.Count >= 200 || stream.Length > 8 * 1024 * 1024 || (total += stream.Length) > 32L * 1024 * 1024)
                        throw new InvalidDataException("导出文件超过可核验的数量或大小。");
                    entries.Add(path + "\t" + Convert.ToBase64String(hash.ComputeHash(stream)));
                }
            }
            if (entries.Count == 0) throw new InvalidDataException("未选择导出资料。");
            return PlcEngineering.Hash(string.Join("\n", entries));
        }

        public void Verify(CancellationToken token)
        {
            if (Files.Length == 0 || string.IsNullOrEmpty(Fingerprint)) throw new InvalidDataException("缺少工程版本证据，请重新读取并绑定。");
            string actual;
            if (Native)
            {
                var path = PlcEngineering.FullLocalPath(Files.Single());
                if (!File.Exists(path)) throw new FileNotFoundException("原始工程已找不到，请重新选择。", path);
                actual = PlcEngineering.Hash(string.Join("\n", TiaOpenness.CopyTree(Path.GetDirectoryName(path), null, token)));
            }
            else actual = ExportFingerprint(Files, token);
            if (!string.Equals(actual, Fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException("资料已变化，请重新读取和确认当前编程目标。");
        }
    }

    public sealed class TiaProgrammingTarget
    {
        public string BindingId = "", Cpu = "", ObjectIdentity = "", ObjectName = "", ObjectKind = "", Scope = "";
        public string Device = "", Model = "", OrderNumber = "", Firmware = "";
        public TiaProgrammingSource Source;

        public bool CanTryCompile() { return Source != null && Source.Native && !string.IsNullOrEmpty(Cpu) && Source.Cpus.Contains(Cpu); }
        public bool CanTryInsert() { return CanTryCompile() && (Scope == TiaProgrammingScope.NewFB.ToString() || Scope == TiaProgrammingScope.NewFC.ToString()); }

        public static TiaProgrammingTarget Bind(TiaProgrammingSource source, PlcSnapshot snapshot, string cpu, TiaProgrammingScope scope, PlcObject item, bool fixture)
        {
            if (source == null || snapshot == null) throw new InvalidOperationException("请先读取工程或导出资料。");
            cpu = cpu ?? "";
            if (source.Cpus.Length != 0 && !source.Cpus.Contains(cpu) || source.Cpus.Length == 0 && cpu.Length != 0)
                throw new InvalidOperationException("请选择当前资料中可确认的目标 PLC。");
            if (scope == TiaProgrammingScope.EditBlock && item == null)
                throw new InvalidOperationException("请选择要分析和生成替换草稿的已有程序块。");
            if (item != null && (!snapshot.Objects.Contains(item) || !Allowed(item, cpu, scope) ||
                snapshot.Objects.Count(o => o.Identity == item.Identity && Allowed(o, cpu, scope)) != 1))
                throw new InvalidOperationException("工程对象无法唯一验证，或不属于当前 PLC 与范围。");
            if (fixture && source.Native) throw new InvalidOperationException("原生工程来源已由实际读取接口确认。");
            var hardware = snapshot.Objects.Where(o => PlcEngineering.IsHardware(o) && cpu.Length != 0 &&
                (o.Controller == cpu || o.Source == "Openness/" + cpu)).Take(2).ToArray();
            var target = new TiaProgrammingTarget { BindingId = Guid.NewGuid().ToString("N"), Cpu = cpu, Scope = scope.ToString(),
                ObjectIdentity = item == null ? "" : item.Identity, ObjectName = item == null ? "" : item.Name, ObjectKind = item == null ? "" : item.Kind, Source = source };
            if (fixture) target.Source = new TiaProgrammingSource { Name = source.Name, Version = source.Version, Origin = "测试资料", Fingerprint = source.Fingerprint,
                Copy = source.Copy, ReadUtc = source.ReadUtc, Files = source.Files, Cpus = source.Cpus };
            if (hardware.Length == 1)
            {
                var device = hardware[0];
                var parent = snapshot.Objects.FirstOrDefault(o => !string.IsNullOrEmpty(device.ParentAssetId) && o.AssetId == device.ParentAssetId);
                target.Device = parent == null ? device.Name : parent.Name + " / " + device.Name;
                target.Model = device.HardwareType;
                target.OrderNumber = device.OrderNumber;
                target.Firmware = device.Firmware;
            }
            return target;
        }

        public static bool Allowed(PlcObject item, string cpu, TiaProgrammingScope scope)
        {
            bool hardware = PlcEngineering.IsHardware(item);
            if (hardware) return scope == TiaProgrammingScope.Hardware || scope == TiaProgrammingScope.Analyze;
            if (item.Controller != (cpu ?? "")) return false;
            if (scope == TiaProgrammingScope.Variables) return item.Kind == "PlcTagTable" || item.Kind == "PlcTag";
            if (scope == TiaProgrammingScope.Hardware) return false;
            return scope == TiaProgrammingScope.Analyze || !item.Kind.StartsWith("PlcTag", StringComparison.Ordinal) &&
                item.Kind != "Subnet" && !item.Kind.StartsWith("Network", StringComparison.Ordinal);
        }

        public bool SameSelection(TiaProgrammingTarget other)
        {
            return other != null && Source != null && other.Source != null && Source.Native == other.Source.Native && Source.Origin == other.Source.Origin &&
                Source.Fingerprint == other.Source.Fingerprint && Source.Version == other.Source.Version && Source.Files.SequenceEqual(other.Source.Files, StringComparer.OrdinalIgnoreCase) &&
                Cpu == other.Cpu && Scope == other.Scope && ObjectIdentity == other.ObjectIdentity;
        }
    }

    public enum TiaProgrammingState
    {
        WAITING_FOR_PROJECT, READING_PROJECT, ANALYZING_CONTEXT, GENERATING_CODE, STATIC_CHECK, AWAITING_REVIEW,
        APPLYING_TO_COPY, COMPILING, COMPLETED, BLOCKED, FAILED, CANCELLED, VERIFYING, PARTIAL_SUCCESS, ANSWERED
    }

    public sealed class TiaProgrammingStep
    {
        public string State = "", StartedUtc = "", FinishedUtc = "", Target = "", Summary = "", Error = "";
        public string[] Inputs = new string[0], Outputs = new string[0];
        public bool NeedsApproval;
    }

    public sealed class TiaProgrammingTask
    {
        public string Id = "", State = "", Requirement = "", Next = "", DraftFile = "", DraftHash = "", EditorHash = "", UpdatedUtc = "";
        public bool Isolated, Historical;
        public static string HashEditorText(string source) { return PlcEngineering.Hash(source.Replace("\r\n", "\n").Replace('\r', '\n')); }
        public TiaProgrammingTarget Target;
        public List<TiaProgrammingStep> Steps = new List<TiaProgrammingStep>();

        public override string ToString()
        {
            return UpdatedUtc + "   " + State + "   " + Id + "   " + (Target == null ? "通用草稿" : Target.Source.Name);
        }
    }

    public sealed class TiaProgrammingStore
    {
        private readonly string root;
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public TiaProgrammingStore(string root) { this.root = PlcEngineering.FullLocalPath(root); }

        public TiaProgrammingTask Start(string prompt, TiaProgrammingTarget target, bool isolated)
        {
            return new TiaProgrammingTask { Id = Guid.NewGuid().ToString("N"), Requirement = prompt, Target = target, Isolated = isolated };
        }

        public string Folder(TiaProgrammingTask task)
        {
            Guid id;
            if (!Guid.TryParseExact(task.Id, "N", out id)) throw new InvalidDataException("任务 ID 无效。");
            return PlcEngineering.FullLocalPath(Path.Combine(root, task.Id));
        }

        public void Step(TiaProgrammingTask task, TiaProgrammingState state, string summary, string next, string error = "", bool approval = false, params string[] outputs)
        {
            string time = DateTime.UtcNow.ToString("O");
            var last = task.Steps.LastOrDefault();
            if (last != null) last.FinishedUtc = time;
            task.State = state.ToString();
            task.UpdatedUtc = time;
            task.Next = next;
            var target = task.Target;
            task.Steps.Add(new TiaProgrammingStep { State = task.State, StartedUtc = time, Summary = summary, Error = error, NeedsApproval = approval, Outputs = outputs,
                Target = target == null ? "通用草稿" : target.Source.Name + " / " + target.Cpu + " / " + target.ObjectIdentity,
                Inputs = target == null ? new string[0] : target.Source.Files });
            Write(task, "task.json", Json.ToJson(task));
        }

        public string Write(TiaProgrammingTask task, string name, string value)
        {
            if (name != Path.GetFileName(name)) throw new InvalidDataException("任务输出名称无效。");
            string directory = Folder(task);
            Directory.CreateDirectory(directory);
            string path = PlcEngineering.FullLocalPath(Path.Combine(directory, name));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, value, Utf8);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return path;
        }

        public TiaProgrammingTask[] Recent()
        {
            if (!Directory.Exists(root)) return new TiaProgrammingTask[0];
            return Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).Take(100).Select(path =>
            {
                Guid id;
                if (!Guid.TryParseExact(Path.GetFileName(path), "N", out id)) return null;
                return Json.Load<TiaProgrammingTask>(PlcEngineering.FullLocalPath(Path.Combine(path, "task.json")), null);
            }).Where(t => t != null && (t.Target == null || t.Target.Source != null)).OrderByDescending(t => t.UpdatedUtc).ToArray();
        }

        public string ReadDraft(TiaProgrammingTask task)
        {
            if (string.IsNullOrEmpty(task.DraftFile) || task.DraftFile != Path.GetFileName(task.DraftFile)) throw new InvalidDataException("任务没有可恢复的草稿文件。");
            string path = PlcEngineering.FullLocalPath(Path.Combine(Folder(task), task.DraftFile));
            if (new FileInfo(path).Length > 100000) throw new InvalidDataException("草稿文件超过预览长度。");
            string source = File.ReadAllText(path, Utf8);
            if (PlcEngineering.Hash(source) != task.DraftHash) throw new InvalidDataException("草稿与任务记录的 SHA-256 不一致。");
            return source;
        }
    }
}
