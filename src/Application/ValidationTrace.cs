using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public static partial class ValidationTrace
    {
        private const int MaxTraceRows = 50000;

        public static ValidationTraceResult Query(ProjectContext project, ValidationSnapshot snapshot, ValidationTraceFilter filter,
            CancellationToken token, ValidationService service = null)
        {
            return BuildTrace(new Graph(project, snapshot, service, token), filter ?? new ValidationTraceFilter());
        }

        public static List<TraceLink> Related(ProjectContext project, ValidationSnapshot snapshot, string id,
            CancellationToken token, ValidationService service = null)
        {
            var graph = new Graph(project, snapshot, service, token);
            if (!graph.Records.ContainsKey(id ?? "") && !graph.Tags.ContainsKey(id ?? ""))
                throw new KeyNotFoundException("未找到当前项目的追踪对象。");
            return graph.Outgoing[id].Concat(graph.Incoming[id]).OrderBy(link => link.Kind, StringComparer.Ordinal)
                .ThenBy(link => link.Id, StringComparer.Ordinal).ToList();
        }

        public static ValidationStatistics Statistics(ProjectContext project, ValidationSnapshot snapshot,
            ValidationTraceFilter filter, CancellationToken token, ValidationService service = null)
        {
            filter = filter ?? new ValidationTraceFilter();
            var graph = new Graph(project, snapshot, service, token);
            var trace = BuildTrace(graph, filter);
            var result = new ValidationStatistics { ProjectId = project.Id };
            var requirementRows = trace.Rows.Where(row => row.Applicable && row.RequirementId.Length != 0).ToList();
            var requirementIds = new HashSet<string>(requirementRows.Select(row => row.RequirementId), StringComparer.Ordinal);
            var caseIds = new HashSet<string>(trace.Rows.Where(row => row.TestCaseId.Length != 0).Select(row => row.TestCaseId), StringComparer.Ordinal);
            var tests = snapshot.All<TestCase>().Where(test => caseIds.Contains(test.Id) && Active(test) && test.Applicable).ToList();
            var executions = tests.Select(graph.LatestExecution).Where(execution => execution != null).ToList();
            int executed = executions.Count(execution => Terminal(execution.Status));
            int passed = executions.Count(execution => Is(execution.Status, "passed"));
            int failed = executions.Count(execution => Is(execution.Status, "failed"));
            var deviations = snapshot.All<Deviation>().Where(deviation => !Is(deviation.Status, "withdrawn") &&
                (Selected(graph, deviation, filter) || trace.Rows.Any(row => row.DeviationIds.Contains(deviation.Id)))).ToList();
            int closed = deviations.Count(deviation => Is(deviation.Status, "closed"));
            var reviewTargetIds = new HashSet<string>(requirementIds, StringComparer.Ordinal);
            reviewTargetIds.UnionWith(tests.Select(test => test.Id));
            reviewTargetIds.UnionWith(executions.Where(execution => Terminal(execution.Status)).Select(execution => execution.Id));
            reviewTargetIds.UnionWith(trace.Rows.SelectMany(row => row.RiskIds.Concat(row.DesignIds)).Where(graph.Records.ContainsKey));
            reviewTargetIds.UnionWith(trace.Rows.SelectMany(row => row.EvidenceIds.Concat(row.DeviationIds)));
            foreach (var record in snapshot.Records)
            {
                token.ThrowIfCancellationRequested();
                if (Active(record) && (record is Evidence || record is Deviation || record is CAPARecord || record is RiskAssessment ||
                    record is DesignReference || record is ChangeImpact || record is ValidationDocument || record is PunchItem) &&
                    Selected(graph, record, filter)) reviewTargetIds.Add(record.Id);
            }
            var reviewTargets = reviewTargetIds.Select(id => graph.Records[id]).Where(Active).ToList();
            int reviewed = reviewTargets.Count(record => !Is(graph.Decision(record), "pending"));
            var impacts = snapshot.All<ChangeImpact>().Where(item => Active(item) && Selected(graph, item, filter)).ToList();
            var deliverables = snapshot.All<Deliverable>().Where(item => Active(item) && item.Required && Selected(graph, item, filter)).ToList();
            int available = deliverables.Count(item => CompleteDeliverable(graph, item));
            int covered = requirementRows.Where(row => row.Covered).Select(row => row.RequirementId).Distinct().Count();
            result.HistoricalExecutions = snapshot.All<TestExecution>().Count(execution => caseIds.Contains(execution.TestCaseId));
            Add(result, "applicable-requirements", "适用需求总数", requirementIds.Count, null, "已筛选的适用且未作废需求，按稳定 ID 去重。");
            Add(result, "covered-requirements", "有效测试覆盖的需求数", covered, null, "至少经有效的直接或下游追踪关系关联一项适用测试的需求。");
            Add(result, "requirement-coverage", "需求覆盖率", covered, requirementIds.Count, "有效测试覆盖需求 / 适用需求。");
            Add(result, "test-cases", "测试用例总数", tests.Count, null, "筛选范围中适用且有效的当前测试用例。");
            Add(result, "executed-tests", "已执行测试数", executed, null, "当前版本最新未撤回的执行为通过、失败或阻塞的用例数。");
            Add(result, "test-execution", "测试执行率", executed, tests.Count, "已执行测试用例 / 适用且有效的当前测试用例。");
            Add(result, "test-pass", "测试通过率", passed, passed + failed, "已通过的当前用例 / 已有通过或失败结果的当前用例。排除阻塞、运行中和不适用。");
            Add(result, "pending-reviews", "待审核记录数", reviewTargets.Count - reviewed, null, "适用需求、测试、终态执行及纳入范围的设计、风险、证据、偏差、CAPA、Punch、文件和影响记录，尚无本版本审核决定。");
            Add(result, "review-completion", "审核完成率", reviewed, reviewTargets.Count, "有有效当版人工批准或驳回记录 / 待履行审核的记录。");
            Add(result, "open-deviations", "开放偏差数", deviations.Count - closed, null, "未关闭且未撤回的偏差。");
            Add(result, "deviation-closure", "偏差关闭率", closed, deviations.Count, "已关闭偏差 / 所有未撤回偏差。");
            Add(result, "missing-evidence", "缺失证据的执行数", trace.MissingEvidenceExecutionIds.Count, null,
                service == null ? "当前终态执行中缺少证据关联的执行。本次未校验归档文件。" : "当前终态执行中缺少通过哈希校验关联证据的执行。");
            Add(result, "pending-changes", "待评估变更数", impacts.Count(item => Is(item.Status, "pending-assessment") || Is(item.Status, "draft")), null,
                "状态为待评估或草稿的工程影响记录。");
            Add(result, "deliverable-completeness", "交付资料完整率", available, deliverables.Count, "已确认完成、具有项目内资料引用及源哈希的必需交付项 / 有效必需交付项。");
            return result;
        }

        private static ValidationTraceResult BuildTrace(Graph graph, ValidationTraceFilter filter)
        {
            var result = new ValidationTraceResult { ProjectId = graph.Project.Id, InvalidLinkIds = graph.InvalidLinkIds };
            var allCoveredTests = new HashSet<string>(StringComparer.Ordinal);
            var requirements = graph.Snapshot.All<ValidationRequirement>().OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal);
            foreach (var req in requirements)
            {
                graph.Token.ThrowIfCancellationRequested();
                var path = graph.CoveragePath(req.Id);
                var tests = path.OfType<TestCase>().OrderBy(test => test.Number, StringComparer.OrdinalIgnoreCase).ThenBy(test => test.Id, StringComparer.Ordinal).ToList();
                if (Applicable(req)) allCoveredTests.UnionWith(tests.Select(test => test.Id));
                if (tests.Count == 0) tests.Add(null);
                var related = path.OfType<ValidationRequirement>().Concat(graph.RequirementAncestors(req.Id)).Where(item => item.Id != req.Id)
                    .Select(item => item.Id).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();

                foreach (var test in tests)
                {
                    var relevant = test == null ? path : graph.PathTo(req.Id, test.Id, path);
                    var row = MakeRow(graph, req, test, relevant);
                    row.RelatedRequirementIds = related;
                    if (Matches(graph, filter, row, req, test)) Append(result, row);
                }
            }
            foreach (var test in graph.Snapshot.All<TestCase>().Where(test => !allCoveredTests.Contains(test.Id))
                .OrderBy(test => test.Number, StringComparer.OrdinalIgnoreCase).ThenBy(test => test.Id, StringComparer.Ordinal))
            {
                graph.Token.ThrowIfCancellationRequested();
                var row = MakeRow(graph, null, test, new[] { test });
                if (Matches(graph, filter, row, null, test)) Append(result, row);
            }
            result.UncoveredRequirementIds = Ids(result.Rows.Where(row => row.Applicable).GroupBy(row => row.RequirementId)
                .Where(group => !group.Any(row => row.Covered)).Select(group => group.Key));
            result.PendingRequirementIds = Ids(result.Rows.Where(row => row.Applicable && Is(row.ReviewStatus, "pending")).Select(row => row.RequirementId));
            result.VoidedRequirementIds = Ids(result.Rows.Where(row => row.Voided).Select(row => row.RequirementId));
            result.OrphanTestCaseIds = Ids(result.Rows.Where(row => row.OrphanTest).Select(row => row.TestCaseId));
            result.MissingEvidenceExecutionIds = Ids(result.Rows.Where(row => row.EvidenceMissing).Select(row => row.ExecutionId));
            result.FailedExecutionIds = Ids(result.Rows.Where(row => row.Failure).Select(row => row.ExecutionId));
            result.OpenDeviationIds = Ids(result.Rows.SelectMany(row => row.OpenDeviationIds));
            return result;
        }

        private static void Append(ValidationTraceResult output, ValidationTraceRow row)
        {
            if (output.Rows.Count >= MaxTraceRows)
                throw new InvalidOperationException("追踪结果超过 50000 行，请缩小阶段、设备或 Tag 筛选。");
            output.Rows.Add(row);
        }

        private static ValidationTraceRow MakeRow(Graph graph, ValidationRequirement req, TestCase test, IEnumerable<ValidationRecord> context)
        {
            var items = context.ToList();
            var row = new ValidationTraceRow
            {
                TagIds = Ids(items.SelectMany(record => graph.TagIds(record.Id))),
                RiskIds = Ids(items.OfType<RiskAssessment>().Select(item => item.Id)),
                DesignIds = Ids(items.OfType<DesignReference>().Select(item => item.Id))
            };
            if (req != null)
            {
                row.RequirementId = req.Id;
                row.RequirementNumber = req.Number;
                row.RequirementStage = req.Stage;
                row.RequirementText = req.Description;
                row.RequirementDocumentId = req.DocumentId;
                row.RequirementRevision = req.Revision;
                row.RequirementDocumentVersion = req.DocumentVersion;
                row.ObjectSource = req.Source;
                row.Applicable = Applicable(req);
                row.Voided = req.Voided || Is(req.Status, "voided");
                row.ReviewStatus = graph.Decision(req);
            }
            if (test == null) return row;
            row.TestCaseId = test.Id;
            row.TestCaseNumber = test.Number;
            row.TestCaseTitle = test.Title;
            row.TestStage = test.Stage;
            row.TestSource = test.Source;
            row.TestReviewStatus = graph.Decision(test);
            row.CaseRevision = test.Revision;
            row.OrphanTest = req == null && test.Applicable && Active(test);
            row.Covered = req != null && row.Applicable && test.Applicable && Active(test);
            row.HistoricalExecutionCount = graph.Executions[test.Id].Count();
            var execution = graph.LatestExecution(test);
            if (execution == null) return row;
            row.ExecutionId = execution.Id;
            row.ExecutionCaseRevision = execution.TestCaseRevision;
            row.ExecutionStatus = execution.Status;
            row.ExecutionSource = execution.Source;
            row.ExecutionActor = execution.PerformedBy;
            row.ExecutionResult = execution.ResultText;
            row.ExecutionAt = execution.PerformedAt;
            row.ExecutionReviewStatus = graph.Decision(execution);
            row.Failure = Is(execution.Status, "failed");
            row.EvidenceIds = Ids(graph.EvidenceByExecution[execution.Id].Where(Active).Select(item => item.Id)
                .Concat(graph.Neighbors<Evidence>(execution.Id, "evidence").Where(Active).Select(item => item.Id)));
            row.EvidenceChecked = graph.Service != null;
            row.UnavailableEvidenceIds = Ids(row.EvidenceIds.Where(id => !graph.Available((Evidence)graph.Records[id])));
            row.EvidenceMissing = Terminal(execution.Status) && row.EvidenceIds.Count == row.UnavailableEvidenceIds.Count;
            var deviations = graph.DeviationsByExecution[execution.Id].Concat(graph.Neighbors<Deviation>(execution.Id, "deviation"))
                .Where(item => !Is(item.Status, "withdrawn")).ToList();
            row.DeviationIds = Ids(deviations.Select(item => item.Id));
            row.OpenDeviationIds = Ids(deviations.Where(item => !Is(item.Status, "closed")).Select(item => item.Id));
            return row;
        }

        private static bool Matches(Graph graph, ValidationTraceFilter filter, ValidationTraceRow row, ValidationRequirement req, TestCase test)
        {
            var tags = row.TagIds.Select(id => graph.Tags[id]);
            return Match(filter.Stage, req == null ? "" : req.Stage, test == null ? "" : test.Stage) &&
                Match(filter.SystemId, req == null ? "" : req.SystemId, test == null ? "" : test.SystemId,
                    tags.Select(tag => tag.SystemId).ToArray()) &&
                Match(filter.EquipmentId, req == null ? "" : req.EquipmentId, test == null ? "" : test.EquipmentId,
                    tags.Select(tag => tag.EquipmentId).ToArray()) &&
                Match(filter.RiskLevel, req == null ? "" : req.RiskLevel, "",
                    row.RiskIds.Select(id => ((RiskAssessment)graph.Records[id]).Level).ToArray()) &&
                (string.IsNullOrWhiteSpace(filter.TagId) || row.TagIds.Contains(filter.TagId));
        }

        private static bool Selected(Graph graph, ValidationRecord record, ValidationTraceFilter filter)
        {
            var tags = graph.TagIds(record.Id).Select(id => graph.Tags[id]);
            return Match(filter.Stage, record.Stage, "") &&
                Match(filter.SystemId, record.SystemId, "", tags.Select(tag => tag.SystemId).ToArray()) &&
                Match(filter.EquipmentId, record.EquipmentId, "", tags.Select(tag => tag.EquipmentId).ToArray()) &&
                (string.IsNullOrWhiteSpace(filter.TagId) || tags.Any(tag => tag.TagId == filter.TagId)) &&
                Match(filter.RiskLevel, record is RiskAssessment ? ((RiskAssessment)record).Level : "", "");
        }

        private static bool CompleteDeliverable(Graph graph, Deliverable item)
        {
            if (!Is(item.Status, "complete") && !Is(item.Status, "ready") && !Is(item.Status, "delivered")) return false;
            ValidationRecord document;
            return graph.Records.TryGetValue(item.DocumentId ?? "", out document) && document is ValidationDocument && Active(document) &&
                document.Source != null && document.Source.Sha256 != null && document.Source.Sha256.Length == 64 && !string.IsNullOrWhiteSpace(document.Source.Document);
        }

        private static void Add(ValidationStatistics output, string key, string label, int count, int? denominator, string definition)
        {
            output.Items.Add(new ValidationMetric { Key = key, Label = label, Numerator = count, Denominator = denominator, Definition = definition });
        }

        private static List<string> Ids(IEnumerable<string> ids)
        {
            return ids.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        }

        private static bool Is(string value, string expected) { return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase); }
        private static bool Match(string filter, string a, string b, params string[] values)
        {
            return string.IsNullOrWhiteSpace(filter) || Is(a, filter) || Is(b, filter) || values.Any(value => Is(value, filter));
        }

        private static bool Applicable(ValidationRequirement req) { return req.Applicable && !req.Voided && Active(req); }
        private static bool Active(ValidationRecord record)
        {
            return record != null && !Is(record.Status, "withdrawn") && !Is(record.Status, "voided") && !Is(record.Status, "superseded") &&
                (!(record is ValidationRequirement) || !((ValidationRequirement)record).Voided);
        }
        private static bool Terminal(string state) { return Is(state, "passed") || Is(state, "failed") || Is(state, "blocked"); }

        private sealed class Graph
        {
            internal readonly ProjectContext Project;
            internal readonly ValidationSnapshot Snapshot;
            internal readonly ValidationService Service;
            internal readonly CancellationToken Token;
            internal readonly Dictionary<string, ValidationRecord> Records;
            internal readonly Dictionary<string, EngineeringTag> Tags;
            internal readonly ILookup<string, TraceLink> Outgoing, Incoming;
            internal readonly ILookup<string, TestExecution> Executions;
            internal readonly ILookup<string, Evidence> EvidenceByExecution;
            internal readonly ILookup<string, Deviation> DeviationsByExecution;
            internal readonly List<string> InvalidLinkIds;
            internal readonly bool RecoveredTags;
            private readonly ILookup<string, ReviewRecord> reviews;
            private readonly Dictionary<string, bool> availableEvidence = new Dictionary<string, bool>(StringComparer.Ordinal);

            internal Graph(ProjectContext project, ValidationSnapshot snapshot, ValidationService service, CancellationToken token, bool allTags = false)
            {
                if (project == null || snapshot == null || project.Id != snapshot.ProjectId ||
                    snapshot.Records.Any(record => record.ProjectId != project.Id))
                    throw new UnauthorizedAccessException("验证资料不属于当前工程。");
                Project = project;
                Snapshot = snapshot;
                Service = service;
                Token = token;
                Token.ThrowIfCancellationRequested();
                Records = snapshot.Records.ToDictionary(record => record.Id, StringComparer.Ordinal);
                var traceLinks = snapshot.All<TraceLink>().Where(Active).ToList();
                var tags = allTags || traceLinks.Any(link => Is(link.Kind, "tag")) ?
                    (service == null ? new TagCenter() : service.Tags).Snapshot(project, token) : new TagPage();
                Tags = tags.Items.ToDictionary(tag => tag.TagId, StringComparer.Ordinal);
                RecoveredTags = tags.Recovered;
                var valid = new List<TraceLink>();
                InvalidLinkIds = new List<string>();
                foreach (var link in traceLinks)
                {
                    Token.ThrowIfCancellationRequested();
                    if (!Valid(link)) InvalidLinkIds.Add(link.Id);
                    else valid.Add(link);
                }
                InvalidLinkIds.Sort(StringComparer.Ordinal);
                Outgoing = valid.ToLookup(link => link.FromId, StringComparer.Ordinal);
                Incoming = valid.ToLookup(link => link.ToId, StringComparer.Ordinal);
                Executions = snapshot.All<TestExecution>().ToLookup(execution => execution.TestCaseId, StringComparer.Ordinal);
                EvidenceByExecution = snapshot.All<Evidence>().ToLookup(evidence => evidence.ExecutionId, StringComparer.Ordinal);
                DeviationsByExecution = snapshot.All<Deviation>().ToLookup(deviation => deviation.ExecutionId, StringComparer.Ordinal);
                reviews = snapshot.All<ReviewRecord>().Where(Active).ToLookup(review => review.TargetId, StringComparer.Ordinal);
            }

            private bool Valid(TraceLink link)
            {
                if (string.IsNullOrWhiteSpace(link.FromId) || string.IsNullOrWhiteSpace(link.ToId) || link.FromId == link.ToId) return false;
                ValidationRecord from, to;
                if (!Records.TryGetValue(link.FromId, out from)) return false;
                if (Is(link.Kind, "tag")) return Tags.ContainsKey(link.ToId);
                if (Is(link.Kind, "task") || Is(link.Kind, "incident")) return true; // External endpoints are checked when the link is created.
                return Records.TryGetValue(link.ToId, out to);
            }

            internal string Decision(ValidationRecord record)
            {
                var latest = reviews[record.Id].LastOrDefault(review => review.TargetRevision == record.Revision &&
                    (Is(review.Decision, "approved") || Is(review.Decision, "rejected")));
                if (latest != null) return latest.Decision;
                var deviation = record as Deviation;
                if (deviation != null && Is(deviation.Status, "closed") && reviews[record.Id].Any(review =>
                    review.Id == deviation.ClosureReviewId && review.TargetRevision == deviation.Revision - 1 && Is(review.Decision, "approved")))
                    return "approved";
                return "pending";
            }

            internal TestExecution LatestExecution(TestCase test)
            {
                return Executions[test.Id].Where(item => item.TestCaseRevision == test.Revision && !Is(item.Status, "withdrawn"))
                    .OrderByDescending(item => item.PerformedAt).ThenByDescending(item => item.CreatedAt).ThenBy(item => item.Id, StringComparer.Ordinal)
                    .FirstOrDefault();
            }

            internal IEnumerable<string> TagIds(string record)
            {
                return Outgoing[record].Where(link => Is(link.Kind, "tag")).Select(link => link.ToId);
            }

            internal bool Available(Evidence evidence)
            {
                if (Service == null) return true;
                bool value;
                if (!availableEvidence.TryGetValue(evidence.Id, out value))
                {
                    value = Is(Service.VerifyEvidence(Project, evidence, Token), "verified");
                    availableEvidence.Add(evidence.Id, value);
                }
                return value;
            }

            internal IEnumerable<T> Neighbors<T>(string record, string kind) where T : ValidationRecord
            {
                return Outgoing[record].Where(link => Is(link.Kind, kind) && Records.ContainsKey(link.ToId)).Select(link => Records[link.ToId])
                    .Concat(Incoming[record].Where(link => Is(link.Kind, kind)).Select(link => Records[link.FromId])).OfType<T>();
            }

            internal List<ValidationRecord> CoveragePath(string start)
            {
                return Walk(start, id => Outgoing[id].Where(CoverageEdge).Select(link => link.ToId), true).ToList();
            }

            internal IEnumerable<ValidationRequirement> RequirementAncestors(string start)
            {
                return Walk(start, id => Incoming[id].Where(CoverageEdge).Select(link => link.FromId), true).OfType<ValidationRequirement>();
            }

            internal List<ValidationRecord> PathTo(string start, string end, IEnumerable<ValidationRecord> reachable)
            {
                var prior = new HashSet<string>(Walk(end, id => Incoming[id].Where(CoverageEdge).Select(link => link.FromId), true).Select(record => record.Id), StringComparer.Ordinal);
                prior.Add(start);
                return reachable.Where(record => prior.Contains(record.Id)).ToList();
            }

            private IEnumerable<ValidationRecord> Walk(string start, Func<string, IEnumerable<string>> neighbors, bool activeOnly)
            {
                var todo = new Queue<string>();
                var visited = new HashSet<string>(StringComparer.Ordinal);
                todo.Enqueue(start);
                visited.Add(start);
                while (todo.Count > 0)
                {
                    Token.ThrowIfCancellationRequested();
                    string current = todo.Dequeue();
                    ValidationRecord record;
                    if (!Records.TryGetValue(current, out record) || activeOnly && current != start && !Active(record)) continue;
                    yield return record;
                    foreach (var next in neighbors(current))
                        if (visited.Add(next)) todo.Enqueue(next);
                }
            }

            private static bool CoverageEdge(TraceLink link)
            {
                return Is(link.Kind, "derives") || Is(link.Kind, "design") || Is(link.Kind, "risk") || Is(link.Kind, "verifies");
            }
        }
    }
}
