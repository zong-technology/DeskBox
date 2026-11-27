using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed class TagCenter
    {
        private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            {"TagName", new[] {"Name", "Tag", "Tag Name", "变量名", "名称", "位号", "Tag 名称"}},
            {"SystemId", new[] {"System", "系统", "PLC", "Controller", "控制器"}},
            {"EquipmentId", new[] {"Equipment", "Device", "设备", "设备编号"}},
            {"Description", new[] {"Comment", "描述", "注释", "说明"}},
            {"DataType", new[] {"Type", "Data Type", "数据类型", "类型"}},
            {"SignalType", new[] {"Signal", "Signal Type", "信号类型", "信号"}},
            {"EngineeringUnit", new[] {"Unit", "EU", "ENG_UNITS", "工程单位", "单位"}},
            {"RangeLow", new[] {"Low", "LRV", "量程下限", "下限"}},
            {"RangeHigh", new[] {"High", "URV", "量程上限", "上限"}},
            {"Address", new[] {"LogicalAddress", "地址", "逻辑地址"}}
        };
        private static readonly string[] FieldNames =
        {
            "TagName", "SystemId", "EquipmentId", "Description", "DataType", "SignalType", "EngineeringUnit", "RangeLow", "RangeHigh", "Address"
        };
        private static readonly Regex KnownType = new Regex(@"^(BOOL|BOOLEAN|BYTE|WORD|DWORD|LWORD|[US]?(INT|DINT|LINT)|SINT|INT(8|16|32|64)|UINT(8|16|32|64)|REAL|LREAL|FLOAT(32|64)?|DOUBLE|DURATION|CHAR|WCHAR|STRING(\[\d+\])?|WSTRING|DATE|TIME|LTIME|TIME_OF_DAY|DATETIME|DTL|ENUM|STRUCT|ARRAY\[.+\].*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly EngineeringTagStore store;

        public TagCenter() : this(AppPaths.DataDir) { }
        public TagCenter(string dataDirectory) { store = new EngineeringTagStore(dataDirectory); }

        public static IReadOnlyList<string> Fields { get { return Array.AsReadOnly(FieldNames); } }

        public static IDictionary<string, int> DefaultMapping(string[] headers) { return Map(headers, null); }

        internal static Dictionary<string, int> Map(string[] headers, IDictionary<string, int> explicitMapping)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (explicitMapping != null)
            {
                var columns = new HashSet<int>();
                foreach (var pair in explicitMapping)
                {
                    string field = FieldNames.FirstOrDefault(name => string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase));
                    if (field == null || pair.Value < 0 || pair.Value >= headers.Length || !columns.Add(pair.Value))
                        throw new ArgumentException("字段映射无效或重复使用列：" + pair.Key);
                    result.Add(field, pair.Value);
                }
                return result;
            }
            var claimed = new HashSet<int>();
            foreach (var field in FieldNames)
            {
                var matches = Enumerable.Range(0, headers.Length).Where(i => !claimed.Contains(i) &&
                    Aliases[field].Concat(new[] { field }).Any(name => Normalize(name) == Normalize(headers[i]))).ToArray();
                if (matches.Length == 1) { result.Add(field, matches[0]); claimed.Add(matches[0]); }
            }
            return result;
        }

        private static string Normalize(string value)
        {
            return new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        }

        internal static string Identity(string system, string name)
        {
            return (system ?? "").Trim().Replace("%", "%25").Replace("/", "%2F") + "/" + (name ?? "").Trim();
        }

        public TagPage Query(ProjectContext project, string search, string system, string sort, bool descending,
            int skip, int take, CancellationToken token)
        {
            string id = Require(project);
            return store.Access(id, token, file =>
            {
                bool recovered;
                var data = store.Load(file, id, out recovered);
                string query = (search ?? "").Trim();
                var rows = data.Tags.Where(tag => string.IsNullOrWhiteSpace(system) || string.Equals(tag.SystemId, system, StringComparison.OrdinalIgnoreCase))
                    .Where(tag => query.Length == 0 || new[] { tag.TagName, tag.Description, tag.SystemId, tag.EquipmentId, tag.Address, tag.DataType, tag.EngineeringUnit }
                        .Any(value => (value ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                token.ThrowIfCancellationRequested();
                Func<EngineeringTag, string> key = tag => sort == "system" ? tag.SystemId : sort == "equipment" ? tag.EquipmentId :
                    sort == "source" ? tag.SourceDocument : tag.TagName;
                var ordered = descending ? rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(key, StringComparer.OrdinalIgnoreCase);
                return new TagPage { Total = rows.Count, Recovered = recovered,
                    Items = ordered.ThenBy(tag => tag.TagId).Skip(Math.Max(0, skip)).Take(Math.Max(1, Math.Min(take, 500))).ToList() };
            });
        }

        public List<EngineeringTag> History(ProjectContext project, string id, CancellationToken token)
        {
            var scope = Require(project);
            return store.Access(scope, token, file =>
            {
                bool recovered;
                var state = store.Load(file, scope, out recovered);
                return state.History.Concat(state.Tags).Where(tag => tag.TagId == id).OrderByDescending(tag => tag.Revision).ToList();
            });
        }

        internal TagPage Snapshot(ProjectContext project, CancellationToken token)
        {
            var scope = Require(project);
            return store.Access(scope, token, file =>
            {
                bool recovered;
                var data = store.Load(file, scope, out recovered);
                return new TagPage { Total = data.Tags.Count, Recovered = recovered, Items = data.Tags };
            });
        }

        public EngineeringTag Save(ProjectContext project, EngineeringTag draft, int expectedRevision, CancellationToken token)
        {
            var id = Require(project);
            if (draft == null) throw new ArgumentNullException("draft");
            var next = Copy(draft);
            if (!string.IsNullOrEmpty(next.ProjectId) && next.ProjectId != id) throw new UnauthorizedAccessException("Tag 属于其他工程。");
            next.ProjectId = id;
            var problems = ValidateTag(next);
            if (problems.Any(issue => issue.Blocking)) throw new ArgumentException(string.Join(" ", problems.Select(issue => issue.Message)));
            return store.Access(id, token, file =>
            {
                var ledger = Writable(file, id);
                var old = ledger.Tags.FirstOrDefault(tag => tag.TagId == next.TagId);
                if (old == null && (!string.IsNullOrEmpty(next.TagId) || expectedRevision != 0) ||
                    old != null && old.Revision != expectedRevision)
                    throw new InvalidOperationException("Tag 版本已变化，请重新读取后编辑。");
                if (ledger.Tags.Any(tag => tag.TagId != next.TagId && string.Equals(Identity(tag.SystemId, tag.TagName), Identity(next.SystemId, next.TagName), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("同一项目与系统内已经有同名 Tag。");
                next.SourceType = old == null ? "manual" : old.SourceType;
                next.SourceDocument = old == null ? "" : old.SourceDocument;
                next.SourceLocation = old == null ? "" : old.SourceLocation;
                next.SourceVersion = old == null ? project.Version : old.SourceVersion;
                next.SourceSha256 = old == null ? "" : old.SourceSha256;
                next.Sources = old == null ? new List<TagSourceLink>() : old.Sources;
                next.Relations = old == null ? new List<TagRelation>() : old.Relations;
                Put(ledger, old, next);
                store.Save(file, ledger, token);
                return next;
            });
        }

        public EngineeringTag Link(ProjectContext project, string tagId, int expectedRevision, string kind, string entityId, CancellationToken token)
        {
            var projectId = Require(project);
            if (!new[] { "instrument", "requirement", "test", "incident", "document" }.Contains(kind) ||
                string.IsNullOrWhiteSpace(entityId) || entityId.Length > 256)
                throw new ArgumentException("关联类型或实体 ID 无效。");
            return store.Access(projectId, token, file =>
            {
                var ledger = Writable(file, projectId);
                var old = ledger.Tags.FirstOrDefault(tag => tag.TagId == tagId);
                if (old == null || old.Revision != expectedRevision) throw new InvalidOperationException("Tag 版本已变化。");
                if (old.Relations.Any(link => link.Kind == kind && link.EntityId == entityId)) return old;
                var next = Copy(old);
                next.Relations.Add(new TagRelation { Kind = kind, EntityId = entityId });
                Put(ledger, old, next);
                store.Save(file, ledger, token);
                return next;
            });
        }

        public TagImportPreview Preview(ProjectContext project, string path, string version, string defaultSystem, CancellationToken token,
            IDictionary<string, int> mapping = null, string worksheet = "", string encoding = "auto")
        {
            var id = Require(project);
            string full = EngineeringFiles.Require(project, path);
            EngineeringSource source;
            var issues = new List<EngineeringIssue>();
            var items = new List<TagImportRow>();
            if (EngineeringDocuments.IsTable(EngineeringDocuments.SourceType(path)))
            {
                var table = EngineeringDocuments.ReadTable(project, path, version, worksheet, encoding, token, out source);
                var columns = Map(table.Headers, mapping);
                if (!columns.ContainsKey("TagName"))
                    issues.Add(new EngineeringIssue { Location = source.Document, Message = "请选择 TagName 列。", Blocking = true });
                else
                    foreach (var row in table.Rows)
                    {
                        token.ThrowIfCancellationRequested();
                        var tag = FromSource(id, source, row.Location, defaultSystem);
                        var supplied = columns.ToDictionary(pair => pair.Key, pair => row.Fields[EngineeringTables.Index(pair.Value)]);
                        items.Add(ImportRow(tag, supplied, issues));
                    }
            }
            else
            {
                var document = EngineeringDocuments.Read(project, path, version, token, encoding, worksheet);
                source = document.Source;
                issues.AddRange(document.Issues.Take(1000));
                if (document.Issues.Skip(1000).Any(item => item.Blocking))
                    issues.Add(new EngineeringIssue { Location = source.Document, Message = "还有未显示的阻塞解析诊断。", Blocking = true });
                var parents = document.Records.GroupBy(row => row.Key).ToDictionary(group => group.Key, group => group.First());
                foreach (var row in document.Records)
                {
                    token.ThrowIfCancellationRequested();
                    Dictionary<string, string> supplied;
                    if (row.Kind == "PlcTag")
                        supplied = new Dictionary<string, string> { { "TagName", row.Name }, { "DataType", Get(row, "DataType") },
                            { "SignalType", Get(row, "SignalType") }, { "Address", Get(row, "Address") }, { "SystemId", Get(row, "Controller") } };
                    else if (source.Type == "fhx" && row.Recognized && (row.Kind == "Tag" || row.Kind == "Parameter"))
                    {
                        EngineeringRecord parent;
                        string name = Get(row, "TAG_NAME");
                        if (name.Length == 0)
                        {
                            if (row.Kind == "Tag") name = row.Name;
                            else if (parents.TryGetValue(row.Parent, out parent) && parent.Name.Length > 0 && row.Name.Length > 0 &&
                                ReadAlias(row, "DATA_TYPE", "DATATYPE", "TYPE").Length > 0)
                                name = parent.Name + "/" + row.Name;
                        }
                        if (name.Length == 0) continue;
                        supplied = new Dictionary<string, string> { { "TagName", name }, { "SystemId", ReadAlias(row, "SYSTEM_ID", "SYSTEM") },
                            { "Description", ReadAlias(row, "DESCRIPTION", "DESCRIPTOR") }, { "DataType", ReadAlias(row, "DATA_TYPE", "DATATYPE", "TYPE") },
                            { "EngineeringUnit", ReadAlias(row, "ENG_UNITS", "EU", "UNIT") }, { "Address", Get(row, "ADDRESS") },
                            { "RangeLow", ReadAlias(row, "RANGE_LOW", "LRV") }, { "RangeHigh", ReadAlias(row, "RANGE_HIGH", "URV") },
                            { "SignalType", Get(row, "SIGNAL_TYPE") }, { "EquipmentId", Get(row, "EQUIPMENT_ID") } };
                    }
                    else continue;
                    foreach (string empty in supplied.Keys.Where(key => key != "TagName" && string.IsNullOrWhiteSpace(supplied[key])).ToArray())
                        supplied.Remove(empty);
                    items.Add(ImportRow(FromSource(id, source, row.Location, defaultSystem), supplied, issues));
                }
            }
            if (items.Count == 0) issues.Add(new EngineeringIssue { Location = source.Document, Message = "没有可导入的 Tag 行。", Blocking = true });
            return store.Access(id, token, file =>
            {
                bool recovered;
                var ledger = store.Load(file, id, out recovered);
                if (recovered) issues.Add(new EngineeringIssue { Location = source.Document, Message = "台账从备份读取。先恢复台账，再导入。", Blocking = true });
                var existing = ledger.Tags.ToDictionary(tag => Identity(tag.SystemId, tag.TagName), StringComparer.OrdinalIgnoreCase);
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    var row = item.Value;
                    var key = Identity(row.SystemId, row.TagName);
                    if (!found.Add(key))
                        issues.Add(new EngineeringIssue { Location = row.SourceLocation, Message = "源文件同一系统重复 Tag：" + row.TagName, Blocking = true });
                    EngineeringTag old;
                    if (!existing.TryGetValue(key, out old)) item.State = "new";
                    else
                    {
                        item.ExistingId = old.TagId;
                        var fields = Differences(old, row, item.Fields);
                        item.State = fields.Count == 0 ? "match" : "conflict";
                        if (fields.Count > 0) issues.Add(new EngineeringIssue { Location = row.SourceLocation, Message = "与台账字段不同：" + string.Join(", ", fields) });
                        if (fields.Contains("EngineeringUnit") && old.EngineeringUnit.Length != 0 && row.EngineeringUnit.Length != 0)
                            issues.Add(new EngineeringIssue { Location = row.SourceLocation, Message = "同一 Tag 的工程单位不同，请核对：" + old.EngineeringUnit + " / " + row.EngineeringUnit });
                    }
                }
                var preview = new TagImportPreview { ProjectId = id, Source = source, Rows = items.AsReadOnly(), Issues = issues.AsReadOnly(),
                    StoreStamp = ledger.Stamp, SourcePath = full };
                preview.PayloadHash = PlanHash(preview);
                return preview;
            });
        }

        public TagImportResult Commit(ProjectContext project, TagImportPreview preview, bool replaceExistingFields, CancellationToken token)
        {
            string id = Require(project);
            if (preview == null || preview.ProjectId != id) throw new UnauthorizedAccessException("导入预览超出当前工程范围。");
            if (preview.Issues.Any(issue => issue.Blocking) || PlanHash(preview) != preview.PayloadHash)
                throw new InvalidOperationException("导入预览存在阻塞错误或已变化。请重新预览。");
            EngineeringSource source;
            using (EngineeringFiles.Open(project, preview.SourcePath, preview.Source.Type, preview.Source.Version, 128L * 1024 * 1024, token, out source))
            {
                if (source.Sha256 != preview.Source.Sha256) throw new IOException("源文件已变化，请重新预览。");
                return store.Access(id, token, file =>
                {
                    var ledger = Writable(file, id);
                    if (preview.StoreStamp != ledger.Stamp) throw new InvalidOperationException("Tag 台账已变化，请重新预览。");
                    var result = new TagImportResult { ProjectId = id, Source = preview.Source };
                    var byId = ledger.Tags.Select((tag, index) => new { tag.TagId, index }).ToDictionary(item => item.TagId, item => item.index);
                    foreach (var row in preview.Rows)
                    {
                        token.ThrowIfCancellationRequested();
                        if (row.ExistingId.Length == 0)
                        {
                            var next = Copy(row.Value);
                            next.Sources.Add(SourceLink(next, preview.Source.Encoding));
                            Put(ledger, null, next);
                            result.TagIds.Add(next.TagId);
                            result.Added++;
                            continue;
                        }
                        int position = byId[row.ExistingId];
                        var old = ledger.Tags[position];
                        result.TagIds.Add(old.TagId);
                        bool change = replaceExistingFields && Differences(old, row.Value, row.Fields).Count != 0;
                        var link = SourceLink(row.Value, preview.Source.Encoding);
                        bool newLink = !old.Sources.Any(item => SameLink(item, link));
                        if (!change && !newLink) { result.Unchanged++; continue; }
                        var updated = Copy(old);
                        if (newLink) updated.Sources.Add(link);
                        if (change)
                        {
                            foreach (var field in row.Fields) Set(updated, field, Value(row.Value, field));
                            updated.SourceType = row.Value.SourceType;
                            updated.SourceDocument = row.Value.SourceDocument;
                            updated.SourceLocation = row.Value.SourceLocation;
                            updated.SourceVersion = row.Value.SourceVersion;
                            updated.SourceSha256 = row.Value.SourceSha256;
                            if (ValidateTag(updated).Any(issue => issue.Blocking))
                                throw new InvalidOperationException("合并后的量程或字段无效，导入未写入。");
                            result.Updated++;
                        }
                        else result.Linked++;
                        Put(ledger, old, updated, position);
                    }
                    if (result.Added + result.Updated + result.Linked > 0) store.Save(file, ledger, token);
                    return result;
                });
            }
        }

        public void Restore(ProjectContext project, CancellationToken token)
        {
            string id = Require(project);
            store.Access(id, token, file =>
            {
                bool recovered;
                var ledger = store.Load(file, id, out recovered);
                if (!recovered) throw new InvalidOperationException("主台账可读，无需恢复。");
                if (File.Exists(file)) File.Copy(file, file + ".damaged-" + Guid.NewGuid().ToString("N"), false);
                store.Save(file, ledger, token, false);
                return true;
            });
        }

        public void Export(ProjectContext project, string path, CancellationToken token)
        {
            string id = Require(project);
            var rows = store.Access(id, token, file =>
            {
                bool recovered;
                var data = store.Load(file, id, out recovered);
                return data.Tags.OrderBy(tag => tag.SystemId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(tag => tag.TagName, StringComparer.OrdinalIgnoreCase).ToList();
            });
            string kind = EngineeringDocuments.SourceType(path);
            if (kind != "csv" && kind != "xlsx") throw new ArgumentException("导出路径应为 CSV 或 XLSX。");
            var metadata = new[] { "TagId", "Revision", "SourceType", "SourceDocument", "SourceLocation", "SourceVersion", "SourceSha256", "ProjectId" };
            var cells = new[] { FieldNames.Concat(metadata).ToArray() }.Concat(rows.Select(tag =>
                FieldNames.Select(field => Value(tag, field)).Concat(new[] { tag.TagId, tag.Revision.ToString(CultureInfo.InvariantCulture),
                    tag.SourceType, tag.SourceDocument, tag.SourceLocation, tag.SourceVersion, tag.SourceSha256, tag.ProjectId }).ToArray()));
            EngineeringFiles.Export(project, path, stream =>
            {
                if (kind == "csv") EngineeringTables.WriteCsv(stream, cells, token);
                else EngineeringTables.WriteWorkbook(stream, cells, token);
            }, token);
        }

        public EngineeringDiff CompareRevisions(ProjectContext project, string tagId, int firstRevision, int secondRevision, CancellationToken token)
        {
            var history = History(project, tagId, token);
            var a = history.SingleOrDefault(tag => tag.Revision == firstRevision);
            var b = history.SingleOrDefault(tag => tag.Revision == secondRevision);
            if (a == null || b == null) throw new ArgumentException("找不到所选 Tag 版本。");
            return EngineeringDocuments.CompareRecords(project.Id, ForRevision(a), ForRevision(b), token);
        }

        private static EngineeringDocument ForRevision(EngineeringTag tag)
        {
            var doc = new EngineeringDocument { Source = new EngineeringSource { Type = tag.SourceType, Document = tag.SourceDocument,
                Version = "Tag revision " + tag.Revision + " / " + tag.SourceVersion, Sha256 = tag.SourceSha256 } };
            var row = new EngineeringRecord { Key = tag.TagId, Kind = "Tag", Name = tag.TagName, Location = tag.SourceLocation };
            foreach (var field in FieldNames) row.Fields[field] = Value(tag, field);
            row.Fields["SourceLinks"] = string.Join("\n", tag.Sources.Select(link => link.SourceDocument + ":" + link.SourceLocation + " " + link.Sha256));
            doc.Records.Add(row);
            return doc;
        }

        private static string PlanHash(TagImportPreview plan)
        {
            return PlcEngineering.Hash(Json.ToJson(new { plan.Source, Rows = plan.Rows.Select(row => new { row.Value, row.State, row.ExistingId, row.Fields }), plan.Issues }));
        }

        private static EngineeringTag FromSource(string id, EngineeringSource source, string location, string defaultSystem)
        {
            return new EngineeringTag { ProjectId = id, SystemId = (defaultSystem ?? "").Trim(), SourceType = source.Type,
                SourceDocument = source.Document, SourceLocation = location, SourceVersion = source.Version, SourceSha256 = source.Sha256 };
        }

        private static TagImportRow ImportRow(EngineeringTag tag, Dictionary<string, string> values, List<EngineeringIssue> issues)
        {
            var row = new TagImportRow { Value = tag };
            foreach (var pair in values)
            {
                string value = pair.Value == null ? "" : pair.Value.Trim();
                if (pair.Key == "SystemId" && value.Length == 0) continue;
                try { Set(tag, pair.Key, pair.Key == "Description" ? pair.Value : value); row.Fields.Add(pair.Key); }
                catch (FormatException) { RangeIssue(tag, pair.Key, issues); }
                catch (OverflowException) { RangeIssue(tag, pair.Key, issues); }
            }
            issues.AddRange(ValidateTag(tag));
            return row;
        }

        private static void RangeIssue(EngineeringTag tag, string field, List<EngineeringIssue> issues)
        {
            issues.Add(new EngineeringIssue { Location = tag.SourceLocation, Message = field + " 需要有效十进制数。", Blocking = true });
        }

        private static List<EngineeringIssue> ValidateTag(EngineeringTag tag)
        {
            var issues = new List<EngineeringIssue>();
            Action<string, bool> issue = (message, blocking) => issues.Add(new EngineeringIssue { Location = tag.SourceLocation, Message = message, Blocking = blocking });
            if (string.IsNullOrWhiteSpace(tag.TagName)) issue("缺少 TagName。", true);
            if (string.IsNullOrWhiteSpace(tag.SystemId)) issue("缺少 SystemId。请映射该列或填写默认系统。", true);
            foreach (var field in FieldNames)
            {
                var value = Value(tag, field) ?? "";
                if (value.Length > (field == "Description" ? 8192 : 512) || value.Any(ch => char.IsControl(ch) &&
                    (field != "Description" || ch != '\t' && ch != '\r' && ch != '\n')))
                    issue(field + " 长度或字符无效。", true);
            }
            if (tag.RangeLow.HasValue && tag.RangeHigh.HasValue && tag.RangeLow > tag.RangeHigh) issue("量程下限高于上限。", true);
            if ((tag.RangeLow.HasValue || tag.RangeHigh.HasValue) && string.IsNullOrWhiteSpace(tag.EngineeringUnit)) issue("已填写量程但工程单位为空。", false);
            if (string.IsNullOrWhiteSpace(tag.DataType)) issue("未提供 DataType。", false);
            else if (tag.DataType.Length <= 512 && !KnownType.IsMatch(tag.DataType)) issue("非标准或项目自定义数据类型，请确认：" + tag.DataType, false);
            return issues;
        }

        private TagLedger Writable(string path, string id)
        {
            bool recovered;
            var ledger = store.Load(path, id, out recovered);
            if (recovered) throw new InvalidOperationException("Tag 台账需要先从备份恢复。");
            return ledger;
        }

        private static void Put(TagLedger data, EngineeringTag old, EngineeringTag next, int position = -1)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            next.TagId = old == null ? Guid.NewGuid().ToString("N") : old.TagId;
            next.CreatedAt = old == null ? now : old.CreatedAt;
            next.UpdatedAt = now;
            next.Revision = old == null ? 1 : checked(old.Revision + 1);
            if (old == null) data.Tags.Add(next);
            else
            {
                data.History.Add(old);
                data.Tags[position < 0 ? data.Tags.IndexOf(old) : position] = next;
            }
        }

        private static TagSourceLink SourceLink(EngineeringTag row, string encoding)
        {
            return new TagSourceLink { SourceType = row.SourceType, SourceDocument = row.SourceDocument, SourceLocation = row.SourceLocation,
                SourceVersion = row.SourceVersion, Sha256 = row.SourceSha256, Encoding = encoding };
        }

        private static bool SameLink(TagSourceLink a, TagSourceLink b)
        {
            return a.Sha256 == b.Sha256 && a.SourceDocument == b.SourceDocument && a.SourceLocation == b.SourceLocation && a.SourceVersion == b.SourceVersion;
        }

        private static string Require(ProjectContext project)
        {
            if (project == null) throw new UnauthorizedAccessException("请先选择工程项目。");
            return project.Id;
        }

        private static EngineeringTag Copy(EngineeringTag value)
        {
            return Json.FromJson<EngineeringTag>(Json.ToJson(value));
        }

        private static List<string> Differences(EngineeringTag old, EngineeringTag current, IEnumerable<string> fields)
        {
            return fields.Where(field => !string.Equals(Value(old, field), Value(current, field), StringComparison.Ordinal)).ToList();
        }

        private static string Get(EngineeringRecord row, string field)
        {
            string value;
            return row.Fields.TryGetValue(field, out value) ? value ?? "" : "";
        }

        private static string ReadAlias(EngineeringRecord row, params string[] names)
        {
            return names.Select(name => Get(row, name)).FirstOrDefault(value => value.Length != 0) ?? "";
        }

        internal static string Value(EngineeringTag tag, string field)
        {
            switch (field)
            {
                case "TagName": return tag.TagName;
                case "SystemId": return tag.SystemId;
                case "EquipmentId": return tag.EquipmentId;
                case "Description": return tag.Description;
                case "DataType": return tag.DataType;
                case "SignalType": return tag.SignalType;
                case "EngineeringUnit": return tag.EngineeringUnit;
                case "RangeLow": return tag.RangeLow.HasValue ? tag.RangeLow.Value.ToString("G29", CultureInfo.InvariantCulture) : "";
                case "RangeHigh": return tag.RangeHigh.HasValue ? tag.RangeHigh.Value.ToString("G29", CultureInfo.InvariantCulture) : "";
                case "Address": return tag.Address;
                default: throw new ArgumentException("未知字段：" + field);
            }
        }

        public static void Set(EngineeringTag tag, string field, string value)
        {
            value = value ?? "";
            if (field != "Description") value = value.Trim();
            switch (field)
            {
                case "TagName": tag.TagName = value; break;
                case "SystemId": tag.SystemId = value; break;
                case "EquipmentId": tag.EquipmentId = value; break;
                case "Description": tag.Description = value; break;
                case "DataType": tag.DataType = value; break;
                case "SignalType": tag.SignalType = value; break;
                case "EngineeringUnit": tag.EngineeringUnit = value; break;
                case "RangeLow": tag.RangeLow = ParseRange(value); break;
                case "RangeHigh": tag.RangeHigh = ParseRange(value); break;
                case "Address": tag.Address = value; break;
                default: throw new ArgumentException("未知字段：" + field);
            }
        }

        private static decimal? ParseRange(string value)
        {
            if (value.Length == 0) return null;
            return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
