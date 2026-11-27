using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed class ValidationReports
    {
        private const string Draft = "待审核草稿 · 未签核";
        private static readonly IReadOnlyDictionary<string, string> ReportKinds = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "requirements", "验证需求台账" }, { "rtm", "验证需求追踪矩阵 RTM" },
                { "fat", "FAT 测试报告" }, { "sat", "SAT 测试报告" }, { "qualification", "IQ / OQ / PQ 执行汇总" },
                { "execution", "测试执行结果" }, { "deviations", "偏差报告" }, { "capa", "CAPA 台账" }, { "punch", "Punch List" },
                { "risks", "验证风险评估台账" }, { "impact", "工程变更影响报告" },
                { "summary", "验证总结报告" }, { "deliverables", "项目交付资料清单" }, { "baselines", "验证基线快照" }
            });

        private readonly ValidationService service;

        public ValidationReports() : this(new ValidationService()) { }

        public ValidationReports(ValidationService validation)
        {
            if (validation == null) throw new ArgumentNullException("validation");
            service = validation;
        }

        public static IReadOnlyDictionary<string, string> Kinds { get { return ReportKinds; } }

        public void Export(ProjectContext project, string kind, string destination, CancellationToken token,
            string templatePath = null, IDictionary<string, string> templateMapping = null, string stage = null)
        {
            if (project == null) throw new UnauthorizedAccessException("请选择工程项目。");
            string title;
            if (kind == null || !ReportKinds.TryGetValue(kind, out title)) throw new ArgumentException("未知的验证报告类型。");
            string extension = Path.GetExtension(destination ?? "").ToLowerInvariant();
            if (extension != ".docx" && extension != ".xlsx") throw new ArgumentException("验证报告仅支持 XLSX 和 DOCX。");
            bool template = !string.IsNullOrWhiteSpace(templatePath);
            if (template && (extension != ".docx" || !string.Equals(Path.GetExtension(templatePath), ".docx", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("企业报告模板需为 DOCX，导出类型也需为 DOCX。");
            if (templateMapping != null && templateMapping.Count > 0 && !template)
                throw new ArgumentException("请先选用 DOCX 模板，再配置占位字段映射。");
            string selected = kind == "fat" ? "FAT" : kind == "sat" ? "SAT" : (stage ?? "").Trim();
            var snapshot = service.Read(project, token);
            var report = Build(project, snapshot, kind, selected, token);
            var timestamp = DateTimeOffset.UtcNow.ToString("u", CultureInfo.InvariantCulture);
            var scope = selected.Length == 0 ? "全部适用阶段" : selected;
            var recovery = snapshot.Recovered ? "数据源为只读恢复快照，请核对主数据。" : "";
            var meta = new[] { Draft, title, "项目 · " + project.Name + " · " + project.Id, "验证阶段 · " + scope,
                "数据快照 · " + IfEmpty(snapshot.Stamp), "导出时间 UTC · " + timestamp, recovery };
            if (extension == ".xlsx")
            {
                EngineeringFiles.Export(project, destination, output =>
                    EngineeringTables.WriteWorkbook(output, SpreadsheetRows(meta, report, token), token, "Validation"), token);
                return;
            }

            var body = WordRows(report, token);
            if (!template)
            {
                EngineeringFiles.Export(project, destination, output => ValidationWord.Write(output, meta.Concat(body), token), token);
                return;
            }
            var fields = TemplateFields(project, title, scope, timestamp, snapshot.Stamp, recovery, templateMapping);
            EngineeringSource source;
            using (var input = EngineeringFiles.Open(project, templatePath, "docx", "", 64 * 1024 * 1024, token, out source))
                EngineeringFiles.Export(project, destination, output =>
                    ValidationWord.FillTemplate(output, input, fields, meta.Skip(2).Concat(body), token), token);
        }

        private Table Build(ProjectContext project, ValidationSnapshot snapshot, string kind, string stage, CancellationToken token)
        {
            var scope = snapshot.Records.Where(record => stage.Length == 0 || EqualsStage(record.Stage, stage)).ToList();
            var reviews = snapshot.All<ReviewRecord>().ToLookup(record => record.TargetId);
            var tests = snapshot.All<TestCase>().ToDictionary(record => record.Id, StringComparer.Ordinal);
            var protocols = snapshot.All<TestProtocol>().ToDictionary(record => record.Id, StringComparer.Ordinal);
            var docs = snapshot.All<ValidationDocument>().ToDictionary(record => record.Id, StringComparer.Ordinal);
            var proof = snapshot.All<Evidence>().ToLookup(record => record.ExecutionId);
            var deviations = snapshot.All<Deviation>().ToLookup(record => record.ExecutionId);
            switch (kind)
            {
                case "requirements":
                    return Make(new[] { "需求编号", "需求 ID", "当前版本", "阶段", "标题", "需求原文", "类别", "风险", "适用", "作废", "当前审核",
                        "文档编号", "文档 ID", "文档版本", "系统", "设备", "来源文件", "来源位置", "来源版本", "SHA-256" },
                        scope.OfType<ValidationRequirement>().Select(item => WithSource(new[] { item.Number, item.Id, Integer(item.Revision),
                            item.Stage, item.Title, item.Description, item.Category, item.RiskLevel, Flag(item.Applicable), Flag(item.Voided),
                            Review(reviews, item), item.DocumentNumber, item.DocumentId, item.DocumentVersion, item.SystemId, item.EquipmentId }, item.Source)));
                case "rtm":
                    return Trace(project, snapshot, stage, token);
                case "fat": case "sat": case "execution": case "qualification":
                    var rows = snapshot.All<TestExecution>().Where(record =>
                    {
                        TestCase test;
                        tests.TryGetValue(record.TestCaseId, out test);
                        var executionStage = test == null ? record.Stage : test.Stage;
                        return kind == "qualification" && stage.Length == 0 ? new[] { "IQ", "OQ", "PQ" }.Contains(executionStage, StringComparer.OrdinalIgnoreCase) :
                            stage.Length == 0 || EqualsStage(executionStage, stage);
                    }).OrderBy(record => record.PerformedAt).ThenBy(record => record.Id, StringComparer.Ordinal);
                    return Make(new[] { "测试方案编号", "测试方案 ID", "测试编号", "测试 ID", "测试标题", "所执行用例版本", "当前用例版本", "执行 ID",
                        "执行记录版本", "执行状态", "实际结果", "执行人", "执行时间 UTC", "审核决定", "重测原执行 ID", "步骤结果", "证据 ID",
                        "原件文件和 SHA-256", "偏差 ID", "来源文件", "来源位置", "来源版本", "来源 SHA-256" },
                        rows.Select(item => ExecutionRow(item, tests, protocols, reviews, proof, deviations)));
                case "deviations":
                    return Make(new[] { "偏差编号", "偏差 ID", "版本", "阶段", "状态", "描述", "用例 ID", "执行 ID", "预期", "实际", "影响", "风险",
                        "调查", "根因", "措施", "重测执行 ID", "审核", "关闭时间 UTC", "来源文件", "来源位置", "来源版本", "SHA-256" },
                        scope.OfType<Deviation>().Select(item => WithSource(new[] { item.Number, item.Id, Integer(item.Revision), item.Stage, item.Status,
                            item.Description, item.TestCaseId, item.ExecutionId, item.Expected, item.Actual, item.Impact, item.RiskLevel,
                            item.Investigation, item.RootCause, item.Action, item.RetestExecutionId, Review(reviews, item), Date(item.ClosedAt) }, item.Source)));
                case "punch":
                    return Make(new[] { "编号", "Punch ID", "版本", "阶段", "系统", "设备", "状态", "问题", "责任方", "优先级", "截止日期",
                        "处理", "当前审核", "来源文件", "来源位置", "来源版本", "SHA-256" },
                        scope.OfType<PunchItem>().Select(item => WithSource(new[] { item.Number, item.Id, Integer(item.Revision), item.Stage, item.SystemId,
                            item.EquipmentId, item.Status, item.Description, item.Owner, item.Priority, item.Due, item.Treatment, Review(reviews, item) }, item.Source)));
                case "capa":
                    return Make(new[] { "编号", "CAPA ID", "版本", "阶段", "状态", "描述", "负责人", "截止日期", "根因", "纠正", "预防",
                        "实施", "有效性", "当前审核", "来源文件", "来源位置", "来源版本", "SHA-256" },
                        scope.OfType<CAPARecord>().Select(item => WithSource(new[] { item.Number, item.Id, Integer(item.Revision), item.Stage, item.Status,
                            item.Description, item.Owner, item.Due, item.RootCause, item.Correction, item.Prevention, item.Implementation, item.Effectiveness,
                            Review(reviews, item) }, item.Source)));
                case "risks":
                    return Make(new[] { "风险编号", "风险 ID", "版本", "阶段", "风险描述", "影响类别", "评价方法", "风险等级", "控制措施",
                        "剩余风险", "审核", "来源文件", "来源位置", "来源版本", "SHA-256" },
                        scope.OfType<RiskAssessment>().Select(item => WithSource(new[] { item.Number, item.Id, Integer(item.Revision), item.Stage,
                            item.Description, item.ImpactCategory, item.Method, item.Level, item.Control, item.ResidualRisk, Review(reviews, item) }, item.Source)));
                case "summary":
                    var stats = ValidationTrace.Statistics(project, snapshot, new ValidationTraceFilter { Stage = stage }, token, service);
                    return Make(new[] { "指标", "分子或数量", "分母", "比例", "计算边界" },
                        stats.Items.Select(metric => new[] { metric.Label, Integer(metric.Numerator), metric.Denominator.HasValue ?
                            Integer(metric.Denominator.Value) : "", metric.Rate.HasValue ? metric.Rate.Value.ToString("P2", CultureInfo.InvariantCulture) :
                            metric.Denominator.HasValue ? "不适用 · 分母为零" : "", metric.Definition }).Concat(new[]
                        {
                            new[] { "全部历史执行次数", Integer(stats.HistoricalExecutions), "", "", "包含历史版本和重新测试，区别于当前用例通过率。" }
                        }));
                case "deliverables":
                    return Make(new[] { "交付编号", "交付 ID", "版本", "阶段", "标题", "是否必需", "状态", "文档 ID", "文档编号", "文档版本",
                        "来源文件", "来源位置", "来源版本", "SHA-256" }, scope.OfType<Deliverable>().Select(item =>
                        {
                            ValidationDocument doc;
                            docs.TryGetValue(item.DocumentId, out doc);
                            return WithSource(new[] { item.Number, item.Id, Integer(item.Revision), item.Stage, item.Title, Flag(item.Required),
                                item.Status, item.DocumentId, doc == null ? "" : doc.Number, doc == null ? "" : doc.DocumentVersion }, doc == null ? item.Source : doc.Source);
                        }));
                case "impact":
                    return Make(new[] { "影响记录 ID", "版本", "阶段", "状态", "条目类别", "关联 ID 或差异键", "修改字段", "之前", "之后",
                        "原坐标", "新坐标", "判断依据", "旧文件", "旧版本", "旧 SHA-256", "新文件", "新版本", "新 SHA-256" },
                        scope.OfType<ChangeImpact>().SelectMany(item => ImpactRows(item, token)));
                case "baselines":
                    return Make(new[] { "基线 ID", "基线版本", "基线标题", "创建人", "创建时间 UTC", "状态快照", "引用种类", "引用 ID", "引用版本", "状态",
                        "审核", "来源文件", "来源位置", "来源版本", "SHA-256" }, scope.OfType<ValidationBaseline>().SelectMany(item =>
                            item.Records.Concat(item.TagRevisions).Select(entry => WithSource(new[] { item.Id, Integer(item.Revision), item.Title, item.CreatedBy,
                                Date(item.CreatedAt), item.StateStamp, entry.Kind, entry.Id, Integer(entry.Revision), entry.Status, entry.ReviewStatus }, entry.Source))));
                default:
                    throw new ArgumentException("未知的验证报告类型。");
            }
        }

        private Table Trace(ProjectContext project, ValidationSnapshot snapshot, string stage, CancellationToken token)
        {
            var trace = ValidationTrace.Query(project, snapshot, new ValidationTraceFilter { Stage = stage }, token, service);
            var docs = snapshot.All<ValidationDocument>().ToDictionary(doc => doc.Id, StringComparer.Ordinal);
            var requirements = snapshot.All<ValidationRequirement>().ToDictionary(req => req.Id, StringComparer.Ordinal);
            var values = trace.Rows.Select(row =>
            {
                ValidationDocument doc;
                ValidationRequirement requirement;
                docs.TryGetValue(row.RequirementDocumentId, out doc);
                requirements.TryGetValue(row.RequirementId, out requirement);
                return new[] { row.OrphanTest ? "无来源测试端" : row.Voided ? "已作废需求" : "需求/测试追踪", row.RequirementNumber,
                    row.RequirementId, Integer(row.RequirementRevision), row.RequirementStage, row.RequirementText, requirement != null &&
                    !string.IsNullOrWhiteSpace(requirement.DocumentNumber) ? requirement.DocumentNumber : doc == null ? "" : doc.Number,
                    row.RequirementDocumentId, row.RequirementDocumentVersion, Flag(row.Applicable), row.ReviewStatus, Flag(row.Covered),
                    row.TestCaseNumber, row.TestCaseId, row.TestCaseTitle, Integer(row.CaseRevision), row.TestStage, row.TestReviewStatus,
                    row.ExecutionId, Integer(row.ExecutionCaseRevision), row.ExecutionStatus, row.ExecutionResult, row.ExecutionActor, Date(row.ExecutionAt),
                    row.ExecutionReviewStatus, Integer(row.HistoricalExecutionCount), Flag(row.Failure), Flag(row.EvidenceMissing), Flag(row.EvidenceChecked),
                    Join(row.TagIds), Join(row.RelatedRequirementIds), Join(row.DesignIds), Join(row.RiskIds), Join(row.EvidenceIds), Join(row.UnavailableEvidenceIds),
                    Join(row.DeviationIds), Join(row.OpenDeviationIds) }.Concat(SourceFields(row.ObjectSource)).Concat(SourceFields(row.TestSource))
                    .Concat(SourceFields(row.ExecutionSource)).ToArray();
            }).Concat(trace.InvalidLinkIds.Select(id => new[] { "无效追踪关系", "", id }));
            return Make(new[] { "追踪类型", "需求编号", "需求 ID / 问题 ID", "需求版本", "需求阶段", "需求原文", "文档编号", "文档 ID", "文档版本",
                "适用", "需求审核", "覆盖", "测试编号", "测试 ID", "标题", "用例版本", "测试阶段", "测试审核", "执行 ID", "已执行用例版本",
                "执行状态", "实际结果", "执行人员", "执行时间 UTC", "执行审核", "历史执行次数", "测试失败", "缺证据", "已检查证据文件",
                "Tag ID", "关联需求 ID", "设计 ID", "风险 ID", "证据 ID", "不可用证据 ID", "偏差 ID", "未关闭偏差 ID", "需求来源文件", "位置",
                "源版本", "源 SHA-256", "用例来源文件", "位置", "源版本", "源 SHA-256", "执行来源文件", "位置", "源版本", "源 SHA-256" }, values);
        }

        private static IEnumerable<string[]> ImpactRows(ChangeImpact item, CancellationToken token)
        {
            yield return ImpactRow(item, "统计", "", "", "", "", "", "", "新增 " + item.Added + "、删除 " + item.Removed +
                "、修改 " + item.Modified + "、未分类 " + item.Unclassified + "、超出报告明细 " + item.Unavailable);
            foreach (var change in item.Changes)
            {
                token.ThrowIfCancellationRequested();
                yield return ImpactRow(item, change.State, change.Key, change.Field, change.Before, change.After, change.BeforeLocation, change.AfterLocation, "确定性差异");
            }
            foreach (var issue in item.Issues)
            {
                token.ThrowIfCancellationRequested();
                yield return ImpactRow(item, issue.Blocking ? "阻塞" : "诊断", "", "", "", "", "", issue.Location, issue.Message);
            }
            foreach (var match in item.Matches)
                yield return ImpactRow(item, "Tag 来源命中", match.TagId, "", "", "", match.Location, "", match.ChangeKey + " · " + match.Side + " · " + match.Document + " · " + match.Sha256);
            foreach (var record in item.Affected)
                yield return ImpactRow(item, "待评估 " + record.Kind, record.Id, "", "", "", "", "",
                    "修订 " + record.Revision + " · " + record.Status + " · " + Join(SourceFields(record.Source)));
            foreach (var key in item.UnresolvedChangeKeys)
                yield return ImpactRow(item, "未对应", key, "", "", "", "", "", "没有可证实的稳定 Tag 来源关联。");
        }

        private static string[] ImpactRow(ChangeImpact item, string type, string key, string field, string before, string after, string oldLocation, string newLocation, string basis)
        {
            return new[] { item.Id, Integer(item.Revision), item.Stage, item.Status, type, key, field, before, after, oldLocation, newLocation, basis,
                item.Before == null ? "" : item.Before.Document, item.Before == null ? "" : item.Before.Version, item.Before == null ? "" : item.Before.Sha256,
                item.After == null ? "" : item.After.Document, item.After == null ? "" : item.After.Version, item.After == null ? "" : item.After.Sha256 };
        }

        private static string[] ExecutionRow(TestExecution item, IDictionary<string, TestCase> tests, IDictionary<string, TestProtocol> protocols,
            ILookup<string, ReviewRecord> reviews, ILookup<string, Evidence> proof, ILookup<string, Deviation> deviations)
        {
            TestCase test;
            tests.TryGetValue(item.TestCaseId, out test);
            TestProtocol plan;
            protocols.TryGetValue(test == null ? "" : test.ProtocolId, out plan);
            return WithSource(new[] { plan == null ? "" : plan.Number, plan == null ? "" : plan.Id, test == null ? "" : test.Number, item.TestCaseId,
                test == null ? "" : test.Title, Integer(item.TestCaseRevision), test == null ? "" : Integer(test.Revision), item.Id, Integer(item.Revision),
                item.Status, item.ResultText, item.PerformedBy, Date(item.PerformedAt), Review(reviews, item), item.RetestOfId,
                Join(item.StepResults.Select(step => Join(new[] { step.StepId, step.Outcome, step.Actual }))), Join(proof[item.Id].Select(evidence => evidence.Id)),
                Join(proof[item.Id].Select(evidence => evidence.FileName + " " + evidence.Sha256)), Join(deviations[item.Id].Select(record => record.Id)) }, item.Source);
        }

        private static string Review(ILookup<string, ReviewRecord> reviews, ValidationRecord item)
        {
            var match = reviews[item.Id].Where(record => record.TargetRevision == item.Revision).OrderByDescending(record => record.UpdatedAt)
                .ThenByDescending(record => record.Id, StringComparer.Ordinal).FirstOrDefault();
            return match == null ? "待审核" : Join(new[] { match.Decision, match.CreatedBy, Date(match.CreatedAt), match.Id });
        }

        private static string[] SourceFields(ValidationSource source)
        {
            return source == null ? new[] { "", "", "", "" } : new[] { source.Document, source.Location, source.Version, source.Sha256 };
        }

        private static string[] WithSource(string[] fields, ValidationSource source) { return fields.Concat(SourceFields(source)).ToArray(); }
        private static string Join(IEnumerable<string> items) { return string.Join(" · ", items.Where(value => !string.IsNullOrWhiteSpace(value))); }
        private static bool EqualsStage(string original, string selected) { return string.Equals(original, selected, StringComparison.OrdinalIgnoreCase); }
        private static string Date(long value) { return value <= 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(value).ToString("u", CultureInfo.InvariantCulture); }
        private static string Flag(bool value) { return value ? "是" : "否"; }
        private static string Integer(int value) { return value.ToString(CultureInfo.InvariantCulture); }
        private static string IfEmpty(string value) { return string.IsNullOrWhiteSpace(value) ? "未提供" : value; }

        private static IDictionary<string, string> TemplateFields(ProjectContext project, string title, string stage, string time,
            string snapshot, string recovery, IDictionary<string, string> mapping)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Title", title }, { "Project", project.Name }, { "ProjectId", project.Id }, { "Stage", stage }, { "GeneratedUtc", time },
                { "SnapshotId", IfEmpty(snapshot) }, { "RecoveryNotice", recovery }, { "DraftStatus", Draft }, { "Body", "$validation-body" }
            };
            if (mapping != null)
                foreach (var pair in mapping)
                {
                    string name = (pair.Key ?? "").Trim();
                    if (name.StartsWith("{{", StringComparison.Ordinal) && name.EndsWith("}}", StringComparison.Ordinal)) name = name.Substring(2, name.Length - 4).Trim();
                    string value;
                    if (name.Length == 0 || name.Length > 80 || !fields.TryGetValue(pair.Value ?? "", out value) ||
                        fields.ContainsKey(name) && name != pair.Value)
                        throw new ArgumentException("报告模板字段映射无效：" + pair.Key);
                    fields[name] = value;
                }
            return fields;
        }

        private static IEnumerable<string[]> SpreadsheetRows(string[] meta, Table table, CancellationToken token)
        {
            foreach (var row in meta.Where(value => value.Length != 0)) yield return new[] { Cell(row) };
            yield return new string[0];
            yield return table.Headers;
            bool hasRows = false;
            foreach (var row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                hasRows = true;
                yield return Enumerable.Range(0, table.Headers.Length).Select(index => index < row.Length ? Cell(IfEmpty(row[index])) : "未提供").ToArray();
            }
            if (!hasRows) yield return new[] { "当前范围无已录入记录。" };
        }

        private static IEnumerable<string> WordRows(Table table, CancellationToken token)
        {
            int number = 0;
            long length = 0;
            foreach (var row in table.Rows)
            {
                token.ThrowIfCancellationRequested();
                yield return "";
                yield return "记录 " + (++number).ToString(CultureInfo.InvariantCulture);
                for (int index = 0; index < table.Headers.Length; index++)
                {
                    var field = table.Headers[index] + " · " + (index < row.Length ? IfEmpty(row[index]) : "未提供");
                    length += field.Length;
                    if (length > 8L * 1024 * 1024) throw new InvalidDataException("Word 草稿超过 8 Mi 字符，请缩小阶段范围或导出 XLSX。");
                    yield return field;
                }
            }
            if (number == 0) yield return "当前范围无已录入记录。";
        }

        private static string Cell(string value)
        {
            if (value.Length > 32767) value = value.Substring(0, 32751) + " [已截断]";
            var text = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                    text.Append(character).Append(value[++index]);
                else text.Append(XmlConvert.IsXmlChar(character) ? character : '\uFFFD');
            }
            return text.ToString();
        }

        private static Table Make(string[] headers, IEnumerable<string[]> rows) { return new Table { Headers = headers, Rows = rows }; }
        private sealed class Table { internal string[] Headers; internal IEnumerable<string[]> Rows; }
    }
}
