using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed class InstrumentService
    {
        private const long MaxDocument = 512L * 1024 * 1024;
        private readonly InstrumentStore store;
        private readonly TagCenter tags;
        private readonly ValidationService evidence;

        public InstrumentService() : this(AppPaths.DataDir) { }

        public InstrumentService(string dataDirectory)
        {
            store = new InstrumentStore(dataDirectory);
            tags = new TagCenter(dataDirectory);
            evidence = new ValidationService(dataDirectory);
        }

        public InstrumentRegister Read(ProjectContext project, CancellationToken token)
        {
            var id = Scope(project, null);
            return store.Access(id, token, path => InstrumentStore.Load(path, id));
        }

        public InstrumentAsset Save(ProjectContext project, InstrumentAsset draft, int expectedRevision, CancellationToken token,
            string manualFile = null, bool removeManual = false)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            var id = Scope(project, draft.ProjectId);
            if (removeManual && !string.IsNullOrWhiteSpace(manualFile))
                throw new ArgumentException("请选择替换或移除手册。");
            var next = Json.FromJson<InstrumentAsset>(Json.ToJson(draft));
            next.Number = Clean(next.Number, 128);
            next.Name = Clean(next.Name, 256);
            next.Kind = Clean(next.Kind, 128);
            next.TagId = Clean(next.TagId, 128);
            next.Manufacturer = Clean(next.Manufacturer, 256);
            next.Model = Clean(next.Model, 256);
            next.SerialNumber = Clean(next.SerialNumber, 256);
            next.Location = Clean(next.Location, 512);
            next.Signal = Clean(next.Signal, 128);
            next.Unit = Clean(next.Unit, 64);
            if (next.Number.Length == 0 || next.Name.Length == 0 || next.Kind.Length == 0)
                throw new ArgumentException("请填写资产编号、名称和类型。");
            if (next.RangeLow.HasValue != next.RangeHigh.HasValue || next.RangeLow > next.RangeHigh)
                throw new ArgumentException("请同时填写量程上下限，且下限不得大于上限。");
            if (next.TagId.Length != 0 && !tags.History(project, next.TagId, token).Any())
                throw new InvalidOperationException("当前项目没有该 Tag ID。");
            var manual = ReadSource(project, manualFile, "instrument-manual", token);
            return store.Access(id, token, path =>
            {
                var data = Writable(path, id);
                var previous = data.Assets.FirstOrDefault(asset => asset.Id == next.Id);
                if (previous == null ? !string.IsNullOrEmpty(next.Id) || expectedRevision != 0 :
                    previous.Revision != expectedRevision || previous.RevisionToken != next.RevisionToken)
                    throw new InvalidOperationException("仪表版本已变化，请重新读取。");
                if (data.Assets.Any(asset => asset.Id != next.Id && string.Equals(asset.Number, next.Number, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("当前项目中已使用此资产编号。");
                next.Id = previous == null ? Guid.NewGuid().ToString("N") : previous.Id;
                next.ProjectId = id;
                next.Revision = checked(expectedRevision + 1);
                next.RevisionToken = Guid.NewGuid().ToString("N");
                next.UpdatedAt = Now();
                next.UpdatedBy = ValidationService.CurrentActor;
                next.Manual = removeManual ? null : manual ?? (previous == null ? null : previous.Manual);
                if (previous != null) { data.Assets.Remove(previous); data.History.Add(previous); }
                data.Assets.Add(next);
                InstrumentStore.Save(path, data, token, true);
                return next;
            });
        }

        public InstrumentActivity Record(ProjectContext project, InstrumentActivity draft, CancellationToken token, string sourceFile = null)
        {
            if (draft == null) throw new ArgumentNullException("draft");
            var id = Scope(project, draft.ProjectId);
            if (!string.IsNullOrEmpty(draft.Id))
                throw new ArgumentException("维护及校准记录采用追加方式。");
            var next = Json.FromJson<InstrumentActivity>(Json.ToJson(draft));
            next.Notes = Clean(next.Notes, 12000);
            next.CertificateNumber = Clean(next.CertificateNumber, 256);
            next.EvidenceId = Clean(next.EvidenceId, 128);
            next.Date = Clean(next.Date, 10);
            next.DueDate = Clean(next.DueDate, 10);
            DateTime date, due;
            if ((next.Kind != "calibration" && next.Kind != "maintenance") || !ParseDate(next.Date, out date) || next.Notes.Length == 0)
                throw new ArgumentException("请填写有效的记录类型、日期及实际工作记录。");
            if (date > DateTime.Today) throw new ArgumentException("实际记录日期不能晚于今天。");
            if (next.Kind == "maintenance" && (next.DueDate.Length > 0 || next.CertificateNumber.Length > 0))
                throw new ArgumentException("校准到期日及证书编号用于校准记录。");
            if (next.DueDate.Length > 0 && (!ParseDate(next.DueDate, out due) || due < date))
                throw new ArgumentException("下次校准日期不能早于实际校准日期。");
            if (next.EvidenceId.Length > 0 && !string.IsNullOrWhiteSpace(sourceFile))
                throw new ArgumentException("请选择工程原件引用或已归档证据。");

            next.Source = ReadSource(project, sourceFile, next.Kind, token);
            if (next.EvidenceId.Length > 0)
            {
                var item = RequireEvidence(project, next.EvidenceId, token);
                if (evidence.VerifyEvidence(project, item, token) != "verified")
                    throw new IOException("归档附件缺失或校验失败。");
                next.Source = new EngineeringSource { Type = "evidence", Document = item.Source.Document,
                    Sha256 = item.Sha256, Bytes = item.Size, Version = item.Source.Version };
            }
            return store.Access(id, token, path =>
            {
                var data = Writable(path, id);
                RequireAsset(data, next.AssetId);
                next.Id = Guid.NewGuid().ToString("N");
                next.ProjectId = id;
                next.RecordedBy = ValidationService.CurrentActor;
                next.RecordedAt = Now();
                data.Activities.Add(next);
                InstrumentStore.Save(path, data, token, true);
                return next;
            });
        }

        public string VerifyAttachment(ProjectContext project, string assetId, string activityId, CancellationToken token)
        {
            var data = Read(project, token);
            var asset = RequireAsset(data, assetId);
            var entry = string.IsNullOrEmpty(activityId) ? null :
                data.Activities.SingleOrDefault(item => item.Id == activityId && item.AssetId == assetId);
            if (!string.IsNullOrEmpty(activityId) && entry == null) throw new InvalidOperationException("当前项目内没有该记录。");
            var source = entry == null ? asset.Manual : entry.Source;
            if (source == null) throw new InvalidOperationException("该记录未关联附件。");
            if (entry != null && entry.EvidenceId.Length > 0)
            {
                var item = RequireEvidence(project, entry.EvidenceId, token);
                if (item.Sha256 != source.Sha256 || item.Size != source.Bytes)
                    throw new InvalidDataException("证据记录与仪表台账的原始哈希不符。");
                evidence.EvidencePath(project, item, token);
                return "";
            }
            string file = Path.Combine(project.Root, source.Document.Replace('/', Path.DirectorySeparatorChar));
            var actual = ReadSource(project, file, source.Type, token);
            if (actual.Sha256 != source.Sha256 || actual.Bytes != source.Bytes)
                throw new InvalidDataException("文件内容已变化，SHA-256 与记录不符。");
            return EngineeringFiles.Require(project, file);
        }

        public static InstrumentActivity LatestCalibration(IEnumerable<InstrumentActivity> entries)
        {
            return entries.Where(item => item.Kind == "calibration").OrderByDescending(item => item.Date, StringComparer.Ordinal)
                .ThenByDescending(item => item.RecordedAt).ThenByDescending(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        public static string DueStatus(InstrumentActivity calibration, DateTime today)
        {
            DateTime due;
            if (calibration == null || !ParseDate(calibration.DueDate, out due)) return "未设到期日";
            int days = (due - today.Date).Days;
            return days < 0 ? "已逾期" : days == 0 ? "今日到期" : days <= 30 ? "30 天内到期" : "未到期";
        }

        public void Export(ProjectContext project, string destination, CancellationToken token)
        {
            var data = Read(project, token);
            var names = tags.Snapshot(project, token).Items.ToDictionary(tag => tag.TagId, tag => tag.SystemId + " / " + tag.TagName);
            var today = DateTime.Today;
            var kind = Path.GetExtension(destination).ToLowerInvariant();
            if (kind != ".csv" && kind != ".xlsx") throw new ArgumentException("请选择 CSV 或 XLSX 文件。");
            var rows = ExportRows(data, names, today);
            EngineeringFiles.Export(project, destination, stream =>
            {
                if (kind == ".csv") EngineeringTables.WriteCsv(stream, rows, token);
                else EngineeringTables.WriteWorkbook(stream, rows, token, "Instruments");
            }, token);
        }

        private static IEnumerable<string[]> ExportRows(InstrumentRegister data, Dictionary<string, string> names, DateTime today)
        {
            yield return new[] { "项目 ID", "资产 ID", "资产编号", "名称", "类型", "厂商", "型号", "序列号", "位置", "信号",
                "单位", "量程下限", "量程上限", "Tag ID", "Tag", "版本", "最近校准", "校准到期", "到期状态", "记录 ID",
                "记录类型", "实际日期", "记录人", "说明", "证书编号", "文件", "SHA-256", "归档证据 ID" };
            var entries = data.Activities.ToLookup(item => item.AssetId);
            foreach (var asset in data.Assets.OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase))
            {
                string tag;
                names.TryGetValue(asset.TagId, out tag);
                var latest = LatestCalibration(entries[asset.Id]);
                var common = new[] { data.ProjectId, asset.Id, asset.Number, asset.Name, asset.Kind, asset.Manufacturer, asset.Model,
                    asset.SerialNumber, asset.Location, asset.Signal, asset.Unit,
                    asset.RangeLow.HasValue ? asset.RangeLow.Value.ToString(CultureInfo.InvariantCulture) : "",
                    asset.RangeHigh.HasValue ? asset.RangeHigh.Value.ToString(CultureInfo.InvariantCulture) : "",
                    asset.TagId, tag ?? "", asset.Revision.ToString(CultureInfo.InvariantCulture), latest == null ? "" : latest.Date,
                    latest == null ? "" : latest.DueDate, DueStatus(latest, today) };
                yield return common.Concat(new[] { "", "仪表档案", "", asset.UpdatedBy, "", "", Document(asset.Manual), Hash(asset.Manual), "" }).ToArray();
                foreach (var entry in entries[asset.Id].OrderByDescending(item => item.Date).ThenByDescending(item => item.RecordedAt))
                    yield return common.Concat(new[] { entry.Id, entry.Kind, entry.Date, entry.RecordedBy, entry.Notes,
                        entry.CertificateNumber, Document(entry.Source), Hash(entry.Source), entry.EvidenceId }).ToArray();
            }
        }

        public void Restore(ProjectContext project, CancellationToken token)
        {
            var id = Scope(project, null);
            store.Access(id, token, path =>
            {
                var data = InstrumentStore.Load(path, id);
                if (!data.Recovered) throw new InvalidOperationException("当前仪表台账无需恢复。");
                if (File.Exists(path)) File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N").Substring(0, 10));
                data.Recovered = false;
                InstrumentStore.Save(path, data, token, false);
                return true;
            });
        }

        private Evidence RequireEvidence(ProjectContext project, string id, CancellationToken token)
        {
            var item = evidence.Read(project, token).Find<Evidence>(id);
            if (item == null) throw new InvalidOperationException("当前项目内没有该归档证据。");
            return item;
        }

        private static InstrumentRegister Writable(string path, string id)
        {
            var data = InstrumentStore.Load(path, id);
            if (data.Recovered) throw new InvalidOperationException("当前为备份只读状态，请先恢复台账。");
            return data;
        }

        private static InstrumentAsset RequireAsset(InstrumentRegister data, string id)
        {
            var item = data.Assets.SingleOrDefault(asset => asset.Id == id);
            if (item == null) throw new InvalidOperationException("当前项目内没有该仪表。");
            return item;
        }

        private static EngineeringSource ReadSource(ProjectContext project, string path, string kind, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            EngineeringSource source;
            using (EngineeringFiles.Open(project, path, kind, project.Version, MaxDocument, token, out source)) { }
            return source;
        }

        private static string Scope(ProjectContext project, string incoming)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.Id) || (!string.IsNullOrEmpty(incoming) && incoming != project.Id))
                throw new UnauthorizedAccessException("请选择正确的工程项目。");
            return project.Id;
        }

        private static string Clean(string value, int max)
        {
            value = (value ?? "").Trim();
            if (value.Length > max) throw new ArgumentException("字段过长，最多 " + max + " 个字符。");
            return value;
        }

        private static bool ParseDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static string Document(EngineeringSource source) { return source == null ? "" : source.Document; }
        private static string Hash(EngineeringSource source) { return source == null ? "" : source.Sha256; }
        private static long Now() { return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    }
}
