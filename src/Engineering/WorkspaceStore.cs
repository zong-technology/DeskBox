using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;

namespace DeskBox.Engineering
{
    public class EngineerWorkspace
    {
        public string Id = "";
        public string Name = "";
        public string Root = "";
        public string Kind = "PLC";
        public string Customer = "";
        public string System = "";
        public string Number = "";
        public string Equipment = "";
        public string Plc = "";
        public string Dcs = "";
        public string Hmi = "";
        public string Protocol = "";
        public string Owner = "";
        public string Start = "";
        public string Deadline = "";
        public string Risk = "medium";
        public string Notes = "";
        public Dictionary<string, double> StageProgress = DefaultProgress();
        public Dictionary<string, double> StageWeights = DefaultWeights();
        public bool Pinned = true;
        public long CreatedAt;
        public long LastOpenedAt;
        public long UpdatedAt;
        public List<ValidationStep> ValidationSteps;

        public static Dictionary<string, double> DefaultProgress()
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "Design", 0 }, { "PLC", 0 }, { "DCS", 0 }, { "HMI", 0 }, { "FAT", 0 }, { "SAT", 0 }, { "Validation", 0 }, { "Delivery", 0 } };
        }

        public static Dictionary<string, double> DefaultWeights()
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { { "Design", 10 }, { "PLC", 20 }, { "DCS", 15 }, { "HMI", 10 }, { "FAT", 15 }, { "SAT", 10 }, { "Validation", 15 }, { "Delivery", 5 } };
        }
    }

    public class WorkspaceStore
    {
        private readonly object _lock = new object();
        private List<EngineerWorkspace> _items;
        private CurrentProjectState _state;
        private string FilePath { get { return Path.Combine(AppPaths.DataDir, "engineering-workspaces.json"); } }
        private string StateFile { get { return Path.Combine(AppPaths.DataDir, "engineering-current.json"); } }
        public event Action Changed;

        public WorkspaceStore()
        {
            _items = Json.Load(FilePath, new List<EngineerWorkspace>()) ?? new List<EngineerWorkspace>();
            _items = _items.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Root)).ToList();
            foreach (var item in _items) Normalize(item);
            _state = Json.Load(StateFile, new CurrentProjectState()) ?? new CurrentProjectState();
        }

        public List<EngineerWorkspace> Items { get { lock (_lock) return _items.OrderByDescending(x => x.Pinned).ThenByDescending(x => x.LastOpenedAt).ToList(); } }
        public EngineerWorkspace Current { get { lock (_lock) { var x = _items.FirstOrDefault(i => i.Id == _state.CurrentId); return x ?? _items.OrderByDescending(i => i.Pinned).ThenByDescending(i => i.LastOpenedAt).FirstOrDefault(); } } }

        public EngineerWorkspace Add(string name, string root, string kind)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) throw new DirectoryNotFoundException("工作区目录不存在：" + root);
            var full = Path.GetFullPath(root);
            lock (_lock)
            {
                var existing = _items.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Root), full, StringComparison.OrdinalIgnoreCase));
                if (existing != null) return existing;
                var item = new EngineerWorkspace { Id = "ws_" + Guid.NewGuid().ToString("N").Substring(0, 10), Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full) : name.Trim(), Root = full, Kind = string.IsNullOrWhiteSpace(kind) ? "PLC" : kind, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                item.UpdatedAt = item.CreatedAt;
                _items.Add(item); SaveLocked(); Raise(); return item;
            }
        }

        public void Remove(string id) { lock (_lock) { _items.RemoveAll(x => x.Id == id); SaveLocked(); } Raise(); }
        public void SetCurrent(string id) { lock (_lock) { if (_items.Any(x => x.Id == id)) { _state.CurrentId = id; Json.Save(StateFile, _state); } } Raise(); }
        public void SetStageProgress(string id, string stage, double percent)
        {
            lock (_lock)
            {
                var x = _items.FirstOrDefault(i => i.Id == id); if (x == null) return; Normalize(x);
                var key = x.StageProgress.Keys.FirstOrDefault(k => string.Equals(k, stage, StringComparison.OrdinalIgnoreCase));
                if (key == null) throw new ArgumentException("未知工程阶段：" + stage);
                x.StageProgress[key] = Math.Max(0, Math.Min(100, percent)); x.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveLocked();
            }
            Raise();
        }

        public void SaveValidation(string id, List<ValidationStep> steps, string start, string end, string notes, string root)
        {
            ValidationProgress.CheckDates(start, end);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("项目目录不存在：" + root);
            var full = Path.GetFullPath(root);
            var expected = ValidationProgress.Create();
            if (steps == null || steps.Count != expected.Count || expected.Any(x => steps.Count(y => y.Id == x.Id) != 1)) throw new ArgumentException("验证流程节点不完整或重复");
            foreach (var step in steps)
            {
                if (step.Percent.HasValue && (double.IsNaN(step.Percent.Value) || double.IsInfinity(step.Percent.Value) || step.Percent < 0 || step.Percent > 100)) throw new ArgumentException(step.Name + "：进度必须为 0–100 或 N/A");
                if(double.IsNaN(step.Weight)||double.IsInfinity(step.Weight)||step.Weight<0||step.Weight>1000000)throw new ArgumentException(step.Name+"：权重应在0–1000000之间，0表示不计入总进度");
                ValidationProgress.CheckDates(step.Start, step.End);
            }
            lock (_lock)
            {
                var project = _items.FirstOrDefault(x => x.Id == id);
                if (project == null) throw new ArgumentException("项目已不存在");
                if (_items.Any(x => x.Id != id && string.Equals(Path.GetFullPath(x.Root), full, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("此目录已绑定其他项目");
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var saved = new List<ValidationStep>();
                foreach (var definition in expected)
                {
                    var value = steps.First(x => x.Id == definition.Id);
                    var old = project.ValidationSteps == null ? null : project.ValidationSteps.FirstOrDefault(x => x.Id == value.Id);
                    bool changed = old == null || old.Percent != value.Percent || old.Weight != value.Weight || old.Start != value.Start || old.End != value.End || old.Notes != value.Notes;
                    saved.Add(new ValidationStep { Id = definition.Id, Name = definition.Name, Percent = value.Percent, Weight=value.Weight, Start = value.Start ?? "", End = value.End ?? "", Notes = value.Notes ?? "", UpdatedAt = changed ? now : old.UpdatedAt });
                }
                project.ValidationSteps = saved; project.Start = start; project.Deadline = end; project.Notes = notes; project.Root = full; project.UpdatedAt = now;
                SaveLocked();
            }
            Raise();
        }
        public void ApplyMobileProject(DeskBox.Sync.MobileProjectDto value)
        {
            if (value == null) return;
            lock (_lock) { var x = _items.FirstOrDefault(i => i.Id == value.Id); if (x == null) return; x.Name = value.Name; x.Number = value.Number; x.Customer = value.Customer; x.Equipment = value.Equipment; x.Owner = value.Owner; x.Deadline = value.Deadline; x.Risk = value.Risk; x.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveLocked(); }
            Raise();
        }
        public void Update(string id, string name, string kind, string customer, string system, string notes)
        {
            lock (_lock)
            {
                var x = _items.FirstOrDefault(i => i.Id == id);
                if (x == null) return;
                if (!string.IsNullOrWhiteSpace(name)) x.Name = name.Trim();
                if (!string.IsNullOrWhiteSpace(kind)) x.Kind = kind.Trim();
                x.Customer = (customer ?? "").Trim();
                x.System = (system ?? "").Trim();
                x.Notes = (notes ?? "").Trim();
                x.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SaveLocked();
            }
            Raise();
        }
        public void UpdateProjectDetails(string id, string number, string equipment, string owner, string start, string deadline, string risk)
        {
            lock (_lock)
            {
                var x = _items.FirstOrDefault(i => i.Id == id); if (x == null) return;
                x.Number = (number ?? "").Trim(); x.Equipment = (equipment ?? "").Trim(); x.Owner = (owner ?? "").Trim();
                x.Start = (start ?? "").Trim(); x.Deadline = (deadline ?? "").Trim();
                var normalizedRisk = (risk ?? "medium").Trim().ToLowerInvariant();
                if (normalizedRisk != "low" && normalizedRisk != "medium" && normalizedRisk != "high" && normalizedRisk != "critical") normalizedRisk = "medium";
                x.Risk = normalizedRisk; x.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveLocked();
            }
            Raise();
        }
        public void TogglePin(string id) { lock (_lock) { var x = _items.FirstOrDefault(i => i.Id == id); if (x != null) { x.Pinned = !x.Pinned; SaveLocked(); } } Raise(); }
        public void Touch(string id) { lock (_lock) { var x = _items.FirstOrDefault(i => i.Id == id); if (x != null) { x.LastOpenedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveLocked(); } } Raise(); }
        private void SaveLocked() { Json.Save(FilePath, _items); }
        private void Raise() { var h = Changed; if (h != null) h(); }

        private static void Normalize(EngineerWorkspace item)
        {
            if (item.StageProgress == null) item.StageProgress = EngineerWorkspace.DefaultProgress();
            if (item.StageWeights == null) item.StageWeights = EngineerWorkspace.DefaultWeights();
            foreach (var kv in EngineerWorkspace.DefaultProgress()) if (!item.StageProgress.ContainsKey(kv.Key)) item.StageProgress[kv.Key] = kv.Value;
            foreach (var kv in EngineerWorkspace.DefaultWeights()) if (!item.StageWeights.ContainsKey(kv.Key)) item.StageWeights[kv.Key] = kv.Value;
            if (string.IsNullOrEmpty(item.Risk)) item.Risk = "medium";
        }

        public class CurrentProjectState { public string CurrentId = ""; }
    }
}
