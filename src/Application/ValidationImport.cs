using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed class ValidationImport
    {
        private const long MaxInput = 64L * 1024 * 1024;
        private static readonly string[] RequirementFields =
        {
            "Number", "Description", "Title", "Category", "RiskLevel", "SystemId", "EquipmentId", "Applicable"
        };
        private static readonly string[] TestCaseFields =
        {
            "Number", "Title", "Description", "Purpose", "Preconditions", "ExpectedResult", "SystemId", "EquipmentId", "Applicable", "StepAction", "StepExpected"
        };
        private static readonly Dictionary<string, string[]> Aliases = new Dictionary<string, string[]>
        {
            { "Number", new[] { "ID", "编号", "需求编号", "测试编号", "需求ID", "测试ID", "RequirementNumber", "TestNumber", "CaseID" } },
            { "Description", new[] { "内容", "需求", "描述", "需求描述", "说明", "Requirement", "RequirementText" } },
            { "Title", new[] { "标题", "名称", "测试标题", "需求标题" } },
            { "Category", new[] { "类别", "需求类别", "分类" } },
            { "RiskLevel", new[] { "风险", "风险等级", "风险级别" } },
            { "SystemId", new[] { "系统", "系统编号", "System" } },
            { "EquipmentId", new[] { "设备", "设备编号", "Equipment" } },
            { "Applicable", new[] { "适用", "适用性", "适用状态", "是否适用" } },
            { "Purpose", new[] { "目标", "测试目的", "目的", "Objective" } },
            { "Preconditions", new[] { "前置条件", "前提", "Prerequisites" } },
            { "ExpectedResult", new[] { "预期结果", "总体预期结果", "Expected" } },
            { "StepAction", new[] { "测试步骤", "步骤", "操作", "Action", "Step" } },
            { "StepExpected", new[] { "步骤预期", "步骤预期结果", "StepResult" } },
        };

        private readonly ValidationService service;
        public ValidationImport() : this(new ValidationService()) { }
        public ValidationImport(ValidationService validation)
        {
            if (validation == null) throw new ArgumentNullException("validation");
            service = validation;
        }

        public static IReadOnlyList<string> Fields(string kind)
        {
            if (kind == "requirement") return Array.AsReadOnly(RequirementFields);
            if (kind == "testcase") return Array.AsReadOnly(TestCaseFields);
            throw new ArgumentException("验证导入只支持需求或测试用例。");
        }

        public static string[] Headers(ProjectContext project, string path, string worksheet, CancellationToken token, int wordTable = 0, string encoding = "auto")
        {
            EngineeringSource ignored;
            return Load(project, path, "", worksheet, wordTable, encoding, token, out ignored).Headers;
        }

        public static Dictionary<string, int> DefaultMapping(string kind, string[] headers)
        {
            if (headers == null) throw new ArgumentNullException("headers");
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in Fields(kind))
            {
                var aliases = new HashSet<string>(Aliases[field].Concat(new[] { field }).Select(Normalize), StringComparer.Ordinal);
                var indices = Enumerable.Range(0, headers.Length).Where(index => !result.ContainsValue(index) && aliases.Contains(Normalize(headers[index]))).ToArray();
                if (indices.Length == 1) result[field] = indices[0];
            }
            return result;
        }

        public ValidationImportPreview Preview(ProjectContext project, string path, ValidationImportOptions options, CancellationToken token)
        {
            if (project == null) throw new UnauthorizedAccessException("请选择工程项目。");
            if (options == null) throw new ArgumentNullException("options");
            Fields(options.Kind);
            var supplied = Json.FromJson<ValidationImportOptions>(Json.ToJson(options));
            supplied.Stage = (supplied.Stage ?? "").Trim();
            supplied.DocumentNumber = (supplied.DocumentNumber ?? "").Trim();
            supplied.DocumentVersion = (supplied.DocumentVersion ?? "").Trim();
            supplied.ProtocolId = (supplied.ProtocolId ?? "").Trim();
            var sourcePath = EngineeringFiles.Require(project, path);
            var ledger = service.Read(project, token);
            EngineeringSource source;
            var table = Load(project, sourcePath, supplied.DocumentVersion, supplied.Worksheet, supplied.WordTable, supplied.Encoding, token, out source);
            var issues = new List<EngineeringIssue>();
            var rows = new List<ValidationImportRow>();
            var mapping = supplied.Mapping == null ? DefaultMapping(supplied.Kind, table.Headers) :
                ExplicitMapping(supplied.Kind, supplied.Mapping, table.Headers.Length);
            var requirements = supplied.Kind == "requirement";
            if (ledger.Recovered) Issue(issues, source.Document, "当前验证数据处于只读恢复状态。请先恢复后导入。");
            if (supplied.Stage.Length == 0 || supplied.DocumentNumber.Length == 0 || supplied.DocumentVersion.Length == 0)
                Issue(issues, source.Document, "请填写验证阶段、逻辑文档编号和文档版本。");
            if (supplied.Stage.Length > 120 || supplied.DocumentNumber.Length > 120 || supplied.DocumentVersion.Length > 120)
                Issue(issues, source.Document, "阶段、文档编号及版本各限 120 个字符。");
            if (source.Type == "docx" && !requirements) Issue(issues, source.Document, "DOCX 辅助提取仅支持验证需求。");
            if (!requirements)
            {
                var protocol = ledger.Find<TestProtocol>(supplied.ProtocolId);
                if (protocol == null) Issue(issues, source.Document, "请选择当前项目中已保存的测试方案。");
                else if (!Equal(protocol.Stage, supplied.Stage)) Issue(issues, source.Document, "导入阶段与测试方案阶段不一致。");
            }
            var docs = ledger.All<ValidationDocument>().Where(item => MatchesDocument(item, supplied)).ToArray();
            if (docs.Length > 1)
                Issue(issues, source.Document, "存在多份同阶段、同编号和版本的文档，需先确认并消除歧义。");
            else if (docs.Length == 1 && !Equal(docs[0].Source.Sha256, source.Sha256))
                Issue(issues, source.Document, "同版本文件的 SHA-256 已改变，请使用正确文件或输入新文档版本。");
            if (!mapping.ContainsKey("Number") || !mapping.ContainsKey(requirements ? "Description" : "Title"))
                Issue(issues, source.Document, requirements ? "至少映射编号与需求描述。" : "至少映射编号与测试标题。");
            else
                Classify(ledger, supplied, table, mapping, rows, issues, token);
            if (rows.All(item => item.State == "removed")) Issue(issues, source.Document, "当前来源没有可确认的导入行。");
            var preview = new ValidationImportPreview
            {
                ProjectId = project.Id, Source = source, Rows = rows.AsReadOnly(), Issues = issues.AsReadOnly(),
                Options = supplied, Path = sourcePath, Stamp = ledger.Stamp
            };
            preview.Checksum = Checksum(preview);
            return preview;
        }

        public ValidationImportResult Commit(ProjectContext project, ValidationImportPreview preview, bool applyRevision, CancellationToken token, bool retireMissing = false)
        {
            if (project == null || preview == null || !string.Equals(project.Id, preview.ProjectId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("验证导入预览与当前项目不一致。");
            if (Checksum(preview) != preview.Checksum || preview.Issues.Any(issue => issue.Blocking))
                throw new InvalidOperationException("验证导入预览存在阻塞问题或已更改。请重新预览。");
            var options = preview.Options;
            if (retireMissing && (!applyRevision || options.Kind != "requirement" || preview.Source.Type == "docx"))
                throw new InvalidOperationException("仅可在明确确认完整的表格需求修订时，作废新版已移除的需求。");
            EngineeringSource current;
            using (EngineeringFiles.Open(project, preview.Path, preview.Source.Type, options.DocumentVersion, MaxInput, token, out current))
            {
                if (current.Sha256 != preview.Source.Sha256) throw new IOException("原始文件已变化，请重新预览。");
                return service.Transact(project, token, snapshot =>
                {
                    if (snapshot.Stamp != preview.Stamp) throw new InvalidOperationException("验证台账已更新，请重新预览。");
                    var output = new ValidationImportResult { Source = preview.Source };
                    var document = snapshot.All<ValidationDocument>().SingleOrDefault(item => MatchesDocument(item, options));
                    if (document == null)
                    {
                        document = new ValidationDocument
                        {
                            Stage = options.Stage, Number = options.DocumentNumber, Title = options.DocumentNumber,
                            DocumentVersion = options.DocumentVersion, DocumentType = options.Kind == "requirement" ? "requirements" : "testcases",
                            Source = ToSource(current, "")
                        };
                        ValidationService.Put(snapshot, document, 0);
                    }
                    var existing = snapshot.Records.ToDictionary(item => item.Id, StringComparer.Ordinal);
                    foreach (var row in preview.Rows)
                    {
                        token.ThrowIfCancellationRequested();
                        ValidationRecord old;
                        existing.TryGetValue(row.ExistingId ?? "", out old);
                        if (row.State == "removed")
                        {
                            if (!retireMissing || old == null) { output.Skipped++; continue; }
                            var revised = Clone((ValidationRequirement)old);
                            revised.Voided = true;
                            revised.Applicable = false;
                            revised.Status = "voided";
                            ValidationService.Put(snapshot, revised, old.Revision);
                            output.SavedIds.Add(revised.Id);
                            output.Retired++;
                            continue;
                        }
                        if (old != null && (!applyRevision || (row.State == "unchanged" &&
                            old.Source.Document == current.Document && old.Source.Sha256 == current.Sha256 && old.Source.Location == row.Location &&
                            old.Source.Version == current.Version)))
                        {
                            output.Skipped++;
                            continue;
                        }
                        ValidationRecord next = options.Kind == "requirement" ?
                            (ValidationRecord)Requirement(row, options, document.Id, old as ValidationRequirement) :
                            Case(row, options, old as TestCase);
                        next.Source = ToSource(current, row.Location);
                        ValidationService.Put(snapshot, next, old == null ? 0 : old.Revision);
                        output.SavedIds.Add(next.Id);
                        if (old == null) output.Added++;
                        else if (row.State == "modified") output.Updated++;
                        else output.SourceRefreshed++;
                    }
                    return output;
                });
            }
        }

        public IReadOnlyList<ValidationRevisionDiff> CompareRequirement(ProjectContext project, string requirementId, int baselineRevision, int targetRevision, CancellationToken token)
        {
            var snapshot = service.Read(project, token);
            var versions = snapshot.Records.Concat(snapshot.History).OfType<ValidationRequirement>().Where(item => item.Id == requirementId).ToList();
            var baseline = versions.SingleOrDefault(item => item.Revision == baselineRevision);
            var target = versions.SingleOrDefault(item => item.Revision == targetRevision);
            if (baseline == null || target == null) throw new InvalidOperationException("找不到所选需求的两个版本。");
            var first = Values(baseline);
            var second = Values(target);
            return first.Keys.Where(key => first[key] != second[key])
                .Select(key => new ValidationRevisionDiff { Field = key, Before = first[key], After = second[key] }).ToList().AsReadOnly();
        }

        public static string[] Locate(ProjectContext project, ValidationSource record, CancellationToken token)
        {
            if (project == null || record == null || string.IsNullOrWhiteSpace(record.Document) || string.IsNullOrWhiteSpace(record.Location))
                throw new ArgumentException("该记录没有可定位的导入原文。");
            var path = Path.Combine(project.Root, record.Document);
            int table = 0;
            string sheet = null;
            if (record.Type == "docx" && record.Location.StartsWith("表格 ", StringComparison.Ordinal))
            {
                var pieces = record.Location.Split(' ');
                if (pieces.Length < 4 || !int.TryParse(pieces[1], out table)) throw new InvalidDataException("Word 来源坐标无效。");
            }
            if (record.Type == "xlsx")
            {
                var mark = record.Location.LastIndexOf("!L", StringComparison.Ordinal);
                if (mark <= 0) throw new InvalidDataException("工作表来源坐标无效。");
                sheet = record.Location.Substring(0, mark);
            }
            EngineeringSource live;
            var source = Load(project, path, record.Version, sheet, table, "auto", token, out live);
            if (!Equal(live.Sha256, record.Sha256)) throw new IOException("原始文件已变化，无法验证此定位。");
            var row = source.Rows.SingleOrDefault(item => item.Location == record.Location);
            if (row == null) throw new InvalidDataException("原始文件中不存在所记录的位置。");
            return source.Headers.Select((label, index) => label + " · " + row.Fields[EngineeringTables.Index(index)]).ToArray();
        }

        private static void Classify(ValidationSnapshot ledger, ValidationImportOptions options, EngineeringTable table,
            IDictionary<string, int> mapping, List<ValidationImportRow> rows, List<EngineeringIssue> issues, CancellationToken token)
        {
            bool requirement = options.Kind == "requirement";
            IEnumerable<ValidationRecord> records = requirement ?
                ledger.All<ValidationRequirement>().Where(item => Equal(item.Stage, options.Stage) && Equal(item.DocumentNumber, options.DocumentNumber)).Cast<ValidationRecord>() :
                ledger.All<TestCase>().Where(item => item.ProtocolId == options.ProtocolId).Cast<ValidationRecord>();
            var byNumber = records.GroupBy(record => record.Number, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                var values = mapping.ToDictionary(pair => pair.Key, pair => row.Fields[EngineeringTables.Index(pair.Value)].Trim(), StringComparer.OrdinalIgnoreCase);
                var item = new ValidationImportRow
                {
                    Number = Get(values, "Number"),
                    Title = Get(values, "Title"),
                    Description = Get(values, "Description"),
                    Location = row.Location,
                    State = "added",
                    Fields = values
                };
                rows.Add(item);
                if (item.Number.Length == 0 || Get(values, requirement ? "Description" : "Title").Length == 0)
                {
                    item.State = "invalid";
                    Issue(issues, row.Location, requirement ? "需求编号与描述必填。" : "测试编号与标题必填。");
                    continue;
                }
                if (item.Number.Length > 120 || item.Title.Length > 500 || item.Description.Length > 12000)
                    Issue(issues, row.Location, "编号、标题或描述超过允许长度。");
                if (!present.Add(item.Number)) Issue(issues, row.Location, "输入文件内重复编号：" + item.Number);
                if (values.ContainsKey("Applicable") && !string.IsNullOrWhiteSpace(values["Applicable"]) && !ApplicableText(values["Applicable"]).HasValue)
                    Issue(issues, row.Location, "适用性需要明确的 是／否、true／false 或 1／0。");
                if (Get(values, "StepExpected").Length != 0 && Get(values, "StepAction").Length == 0)
                    Issue(issues, row.Location, "填写步骤预期时需同时填写步骤动作。");
                List<ValidationRecord> previous;
                if (!byNumber.TryGetValue(item.Number, out previous)) continue;
                if (previous.Count != 1)
                {
                    Issue(issues, row.Location, "台账此编号存在多个匹配项：" + item.Number);
                    continue;
                }
                var old = previous[0];
                var proposal = requirement ? (ValidationRecord)Requirement(item, options, "", (ValidationRequirement)old) : Case(item, options, (TestCase)old);
                item.ExistingId = old.Id;
                item.State = SameBusiness(old, proposal) ? "unchanged" : "modified";
            }
            if (requirement)
                foreach (var group in byNumber.Where(pair => !present.Contains(pair.Key) && pair.Value.Count == 1))
                {
                    token.ThrowIfCancellationRequested();
                    var old = (ValidationRequirement)group.Value[0];
                    if (old.Voided) continue;
                    rows.Add(new ValidationImportRow { Number = old.Number, Title = old.Title, Description = old.Description, Location = old.Source.Location,
                        ExistingId = old.Id, State = "removed" });
                }
        }

        private static ValidationRequirement Requirement(ValidationImportRow row, ValidationImportOptions options, string documentId, ValidationRequirement old)
        {
            var item = old == null ? new ValidationRequirement() : Clone(old);
            Common(item, row, options);
            item.DocumentId = documentId;
            item.DocumentNumber = options.DocumentNumber;
            item.DocumentVersion = options.DocumentVersion;
            item.Category = Pick(row, "Category", item.Category);
            item.RiskLevel = Pick(row, "RiskLevel", item.RiskLevel);
            item.Applicable = ApplicableText(Pick(row, "Applicable", "")) ?? item.Applicable;
            if (old != null && old.Voided)
            {
                item.Voided = false;
                if (!row.Fields.ContainsKey("Applicable") || string.IsNullOrWhiteSpace(row.Fields["Applicable"])) item.Applicable = true;
            }
            item.Status = "draft";
            return item;
        }

        private static TestCase Case(ValidationImportRow row, ValidationImportOptions options, TestCase old)
        {
            var item = old == null ? new TestCase() : Clone(old);
            Common(item, row, options);
            item.ProtocolId = options.ProtocolId;
            item.Purpose = Pick(row, "Purpose", item.Purpose);
            item.Preconditions = Pick(row, "Preconditions", item.Preconditions);
            item.ExpectedResult = Pick(row, "ExpectedResult", item.ExpectedResult);
            item.Applicable = ApplicableText(Pick(row, "Applicable", "")) ?? item.Applicable;
            string action = Pick(row, "StepAction", "");
            if (action.Length != 0)
            {
                if (item.Steps.Count == 0)
                    item.Steps.Add(new TestStep { Order = 1, Action = action, ExpectedResult = Pick(row, "StepExpected", item.ExpectedResult) });
                else
                {
                    var step = item.Steps.OrderBy(previous => previous.Order).First();
                    step.Action = action;
                    step.ExpectedResult = Pick(row, "StepExpected", step.ExpectedResult);
                }
            }
            item.Status = "draft";
            return item;
        }

        private static void Common(ValidationRecord item, ValidationImportRow row, ValidationImportOptions options)
        {
            item.Stage = options.Stage;
            item.Number = row.Number;
            item.Title = Pick(row, "Title", item.Title);
            if (string.IsNullOrWhiteSpace(item.Title)) item.Title = row.Number;
            item.Description = Pick(row, "Description", item.Description);
            item.SystemId = Pick(row, "SystemId", item.SystemId);
            item.EquipmentId = Pick(row, "EquipmentId", item.EquipmentId);
        }

        private static bool SameBusiness(ValidationRecord first, ValidationRecord second)
        {
            if (first.Title != second.Title || first.Description != second.Description || first.SystemId != second.SystemId || first.EquipmentId != second.EquipmentId)
                return false;
            var before = first as ValidationRequirement;
            var after = second as ValidationRequirement;
            if (before != null) return before.Category == after.Category && before.RiskLevel == after.RiskLevel && before.Applicable == after.Applicable && before.Voided == after.Voided;
            var a = (TestCase)first;
            var b = (TestCase)second;
            return a.Applicable == b.Applicable && a.Purpose == b.Purpose && a.Preconditions == b.Preconditions &&
                a.ExpectedResult == b.ExpectedResult && a.Steps.Select(step => Tuple.Create(step.Order, step.Action, step.ExpectedResult))
                    .SequenceEqual(b.Steps.Select(step => Tuple.Create(step.Order, step.Action, step.ExpectedResult)));
        }

        private static Dictionary<string, string> Values(ValidationRequirement item)
        {
            return new Dictionary<string, string>
            {
                { "Number", item.Number }, { "Title", item.Title }, { "Description", item.Description }, { "Stage", item.Stage },
                { "Category", item.Category }, { "RiskLevel", item.RiskLevel }, { "SystemId", item.SystemId }, { "EquipmentId", item.EquipmentId },
                { "Applicable", item.Applicable.ToString() }, { "Voided", item.Voided.ToString() }, { "Status", item.Status },
                { "DocumentNumber", item.DocumentNumber }, { "DocumentVersion", item.DocumentVersion }, { "SourceDocument", item.Source.Document },
                { "SourceLocation", item.Source.Location }, { "SourceSha256", item.Source.Sha256 }
            };
        }

        private static bool MatchesDocument(ValidationDocument item, ValidationImportOptions options)
        {
            return Equal(item.Stage, options.Stage) && Equal(item.Number, options.DocumentNumber) && Equal(item.DocumentVersion, options.DocumentVersion);
        }

        private static EngineeringTable Load(ProjectContext project, string path, string version, string sheet, int wordTable, string encoding,
            CancellationToken token, out EngineeringSource source)
        {
            string type = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!new[] { "xlsx", "csv", "tsv", "docx" }.Contains(type))
                throw new ArgumentException("验证导入支持 XLSX、CSV、TSV 或 DOCX。");
            using (var stream = EngineeringFiles.Open(project, path, type, version, MaxInput, token, out source))
                return type == "docx" ? ValidationWord.Extract(stream, wordTable, token) :
                    EngineeringTables.Read(stream, source, sheet, encoding, token);
        }

        private static IDictionary<string, int> ExplicitMapping(string kind, IDictionary<string, int> supplied, int count)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in supplied)
            {
                var key = Fields(kind).SingleOrDefault(name => Equal(name, pair.Key));
                if (key == null || pair.Value < 0 || pair.Value >= count || result.ContainsKey(key) || result.ContainsValue(pair.Value))
                    throw new ArgumentException("字段映射重复、未知或超出来源列范围：" + pair.Key);
                result.Add(key, pair.Value);
            }
            return result;
        }

        private static ValidationSource ToSource(EngineeringSource file, string location)
        {
            return new ValidationSource { Type = file.Type, Document = file.Document, Location = location, Version = file.Version, Sha256 = file.Sha256 };
        }

        private static string Normalize(string label) { return new string((label ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray()); }
        private static bool Equal(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static string Get(IDictionary<string, string> values, string key) { string found; return values.TryGetValue(key, out found) ? found ?? "" : ""; }
        private static string Pick(ValidationImportRow row, string key, string old) { return row.Fields.ContainsKey(key) ? Get(row.Fields, key) : old; }
        private static T Clone<T>(T item) where T : class { return Json.FromJson<T>(Json.ToJson(item)); }
        private static void Issue(List<EngineeringIssue> values, string location, string error)
        {
            if (values.Count < 1000) values.Add(new EngineeringIssue { Location = location, Message = error, Blocking = true });
        }
        private static string Checksum(ValidationImportPreview item)
        {
            return PlcEngineering.Hash(Json.ToJson(new { item.ProjectId, item.Source, item.Options, item.Rows, item.Issues }));
        }
        private static bool? ApplicableText(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "yes": case "true": case "1": case "是": case "适用": return true;
                case "no": case "false": case "0": case "否": case "不适用": return false;
                default: return null;
            }
        }
    }
}
