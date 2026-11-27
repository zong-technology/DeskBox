using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Integration;

namespace DeskBox.AppServices
{
    public sealed partial class ValidationService
    {
        private const long MaxEvidence = 512L * 1024 * 1024;
        private readonly ValidationStore store;
        internal readonly TagCenter Tags;

        private static readonly string[] DeviationStates =
            { "draft", "new", "assessing", "investigating", "handling", "retest-pending", "review-pending", "closed", "withdrawn" };
        private static readonly string[] ExecutionStates = { "running", "passed", "failed", "blocked", "not-applicable", "withdrawn" };
        public static readonly IReadOnlyList<string> StandardStages = Array.AsReadOnly(new[]
            { "URS", "FS", "DS", "DQ", "FAT", "SAT", "IQ", "OQ", "PQ", "CSV", "交付" });

        public ValidationService() : this(AppPaths.DataDir) { }

        public ValidationService(string dataDirectory)
        {
            store = new ValidationStore(dataDirectory);
            Tags = new TagCenter(dataDirectory);
        }

        public static string CurrentActor
        {
            get
            {
                try { using (var identity = WindowsIdentity.GetCurrent()) if (identity != null && !string.IsNullOrEmpty(identity.Name)) return identity.Name; }
                catch (PlatformNotSupportedException) { }
                return Environment.UserName;
            }
        }

        public ValidationSnapshot Read(ProjectContext project, CancellationToken token)
        {
            var id = Scope(project);
            return store.Access(id, token, directory => store.Load(directory, id));
        }

        internal T Transact<T>(ProjectContext project, CancellationToken token, Func<ValidationSnapshot, T> change)
        {
            var id = Scope(project);
            return store.Access(id, token, directory =>
            {
                var snapshot = store.Load(directory, id);
                RequireWritable(snapshot);
                var result = change(snapshot);
                token.ThrowIfCancellationRequested();
                store.Save(directory, snapshot, token);
                return result;
            });
        }

        public ValidationRecord Save(ProjectContext project, ValidationRecord record, int expectedRevision, CancellationToken token)
        {
            if (record == null) throw new ArgumentNullException("record");
            if (record is TestExecution || record is Evidence || record is ReviewRecord || record is TraceLink || record is ValidationBaseline || record is TestStep)
                throw new InvalidOperationException("此记录使用单独的验证操作。");
            Scope(project);
            return Transact(project, token, snapshot =>
            {
                var existing = snapshot.Find<ValidationRecord>(record.Id);
                if (record.Source != null && !string.IsNullOrEmpty(record.Source.Document))
                {
                    var source = existing == null ? null : existing.Source;
                    if (source == null || !Same(source.Document, record.Source.Document) || source.Sha256 != record.Source.Sha256 ||
                        source.Type != record.Source.Type || source.Version != record.Source.Version || source.Location != record.Source.Location)
                    {
                        var captured = CaptureSource(project, Path.Combine(project.Root, record.Source.Document), record.Source.Location,
                            record.Source.Version, token, record.Source.Type);
                        if (!string.IsNullOrEmpty(record.Source.Sha256) && captured.Sha256 != record.Source.Sha256)
                            throw new IOException("来源文件已变化。请明确选择对应来源及版本。");
                        captured.Encoding = record.Source.Encoding;
                        record.Source = captured;
                    }
                }
                var plan = record as ValidationPlan;
                if (plan != null)
                {
                    var old = snapshot.All<ValidationPlan>().SingleOrDefault();
                    if (old != null && (!Same(old.OwnerId, CurrentActor) || old.Id != plan.Id))
                        throw new UnauthorizedAccessException("只有项目验证计划所有者可以修改审核授权与阶段。");
                    plan.OwnerId = old == null ? CurrentActor : old.OwnerId;
                    if (plan.Stages == null) throw new ArgumentException("阶段数据无效。");
                    plan.Stages = CleanList(plan.Stages, "项目阶段");
                    plan.ReviewerIds = CleanList(plan.ReviewerIds, "审核账户");
                }
                var deviation = record as Deviation;
                if (deviation != null)
                {
                    var prior = snapshot.Find<Deviation>(deviation.Id);
                    if (!DeviationStates.Contains(deviation.Status) || deviation.Status == "closed" ||
                        (prior != null && (prior.Status == "closed" || prior.Status == "withdrawn")))
                        throw new InvalidOperationException("偏差状态不允许通过该操作变更。");
                    deviation.ClosedAt = 0;
                    deviation.ClosureReviewId = "";
                }
                var impact = record as ChangeImpact;
                if (impact != null) throw new InvalidOperationException("变更影响由资料差异服务生成与维护。");
                return Put(snapshot, record, expectedRevision);
            });
        }

        internal static ValidationRecord Put(ValidationSnapshot snapshot, ValidationRecord record, int expectedRevision)
        {
            if (record.ProjectId != "" && record.ProjectId != snapshot.ProjectId) throw new UnauthorizedAccessException("记录属于另一个项目。");
            var previous = snapshot.Find<ValidationRecord>(record.Id);
            if (previous == null ? expectedRevision != 0 || record.Id.Length != 0 :
                previous.GetType() != record.GetType() || previous.Revision != expectedRevision)
                throw new InvalidOperationException("验证记录已变化或不存在，请刷新。");

            record.Stage = Clean(record.Stage, 120);
            record.Number = Clean(record.Number, 240);
            record.Title = Clean(record.Title, 1024);
            record.Description = Clean(record.Description, 32000);
            record.SystemId = Clean(record.SystemId, 240);
            record.EquipmentId = Clean(record.EquipmentId, 240);
            record.Status = Clean(record.Status, 120);
            if (record.Source == null) throw new ArgumentException("记录需要来源。");
            foreach (var field in record.GetType().GetFields())
                if (field.FieldType == typeof(string) && ((string)field.GetValue(record) ?? "").Length > 32000)
                    throw new ArgumentException("验证字段超过长度限制：" + field.Name);
            if (!(record is TraceLink) && !(record is Evidence) && !(record is ReviewRecord) && !(record is TestExecution) &&
                record.Title.Length == 0 && record.Number.Length == 0)
                throw new ArgumentException("请填写记录编号或标题。");

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var actor = CurrentActor;
            record.Id = previous == null ? Guid.NewGuid().ToString("N") : previous.Id;
            record.ProjectId = snapshot.ProjectId;
            record.Revision = previous == null ? 1 : checked(previous.Revision + 1);
            record.CreatedBy = previous == null ? actor : previous.CreatedBy;
            record.CreatedAt = previous == null ? now : previous.CreatedAt;
            record.UpdatedBy = actor;
            record.UpdatedAt = now;
            ValidateRecord(snapshot, record, previous, actor, now);
            if (previous == null) snapshot.Records.Add(record);
            else
            {
                snapshot.History.Add(previous);
                snapshot.Records[snapshot.Records.IndexOf(previous)] = record;
            }
            return record;
        }

        private static void ValidateRecord(ValidationSnapshot snapshot, ValidationRecord record, ValidationRecord previous, string actor, long now)
        {
            var requirement = record as ValidationRequirement;
            if (requirement != null)
            {
                if (requirement.Number.Length == 0 || requirement.Description.Length == 0)
                    throw new ArgumentException("需求需要编号和内容。");
                var doc = Ref<ValidationDocument>(snapshot, requirement.DocumentId);
                if (doc != null)
                {
                    if (!string.IsNullOrEmpty(requirement.DocumentNumber) && !Same(requirement.DocumentNumber, doc.Number) ||
                        !string.IsNullOrEmpty(requirement.DocumentVersion) && !Same(requirement.DocumentVersion, doc.DocumentVersion))
                        throw new ArgumentException("需求文档编号或版本与来源记录不一致。");
                    requirement.DocumentNumber = doc.Number;
                    requirement.DocumentVersion = doc.DocumentVersion;
                    if (requirement.Source.Document.Length == 0) requirement.Source = doc.Source;
                }
                if (snapshot.All<ValidationRequirement>().Any(item => item.Id != record.Id && Same(item.Number, requirement.Number) &&
                    Same(item.Stage, requirement.Stage) && Same(item.DocumentNumber, requirement.DocumentNumber)))
                    throw new InvalidOperationException("同一验证阶段和逻辑文档已有该需求编号。");
            }
            var design = record as DesignReference;
            if (design != null) Ref<ValidationDocument>(snapshot, design.DocumentId);
            var protocol = record as TestProtocol;
            if (protocol != null) Ref<ValidationDocument>(snapshot, protocol.DocumentId);
            var test = record as TestCase;
            if (test != null)
            {
                Ref<TestProtocol>(snapshot, test.ProtocolId);
                if (test.Number.Length == 0 || test.Title.Length == 0) throw new ArgumentException("测试需填写编号和标题。");
                if (snapshot.All<TestCase>().Any(item => item.Id != test.Id && Same(item.Number, test.Number) && Same(item.Stage, test.Stage) && item.ProtocolId == test.ProtocolId))
                    throw new InvalidOperationException("同一阶段及方案已有该测试编号。");
                ValidateSteps(test, previous as TestCase, actor, now);
            }
            var deviation = record as Deviation;
            if (deviation != null)
            {
                var execution = Ref<TestExecution>(snapshot, deviation.ExecutionId);
                var retest = Ref<TestExecution>(snapshot, deviation.RetestExecutionId);
                Ref<TestCase>(snapshot, deviation.TestCaseId);
                if (execution != null)
                {
                    if (deviation.TestCaseId.Length == 0) deviation.TestCaseId = execution.TestCaseId;
                    if (execution.TestCaseId != deviation.TestCaseId) throw new InvalidOperationException("偏差与执行的测试用例不一致。");
                }
                if (retest != null && (execution == null || retest.RetestOfId != execution.Id))
                    throw new InvalidOperationException("重新测试必须直接指向偏差关联的原执行记录。");
            }
            var deliverable = record as Deliverable;
            if (deliverable != null) Ref<ValidationDocument>(snapshot, deliverable.DocumentId);
            var checklist = record as ComplianceCheck;
            if (checklist != null && (string.IsNullOrWhiteSpace(checklist.Framework) || string.IsNullOrWhiteSpace(checklist.BasisVersion) ||
                string.IsNullOrWhiteSpace(checklist.BasisLocation)))
                throw new ArgumentException("合规辅助记录需要检查依据、版本和条款或来源位置。");
        }

        private static void ValidateSteps(TestCase test, TestCase previous, string actor, long now)
        {
            if (test.Steps == null || test.Steps.Count > 300 || test.Steps.Any(step => step == null || string.IsNullOrWhiteSpace(step.Action) || string.IsNullOrWhiteSpace(step.ExpectedResult)))
                throw new ArgumentException("步骤必须填写操作和预期结果，最多 300 步。");
            var prior = previous == null ? new Dictionary<string, TestStep>() : previous.Steps.ToDictionary(step => step.Id);
            var used = new HashSet<string>();
            for (int i = 0; i < test.Steps.Count; i++)
            {
                var step = test.Steps[i];
                TestStep old = null;
                if (!string.IsNullOrEmpty(step.Id) && (!prior.TryGetValue(step.Id, out old) || !used.Add(step.Id)))
                    throw new InvalidOperationException("测试步骤 ID 无效或重复。");
                step.Action = Clean(step.Action, 8000);
                step.ExpectedResult = Clean(step.ExpectedResult, 8000);
                step.Order = i + 1;
                step.Id = old == null ? Guid.NewGuid().ToString("N") : old.Id;
                step.ProjectId = test.ProjectId;
                step.Revision = old == null ? 1 : checked(old.Revision + 1);
                step.CreatedAt = old == null ? now : old.CreatedAt;
                step.CreatedBy = old == null ? actor : old.CreatedBy;
                step.UpdatedAt = now;
                step.UpdatedBy = actor;
                step.Stage = test.Stage;
                step.Source = test.Source;
            }
        }

        public TestExecution RecordExecution(ProjectContext project, TestExecution draft, int expectedRevision, CancellationToken token)
        {
            if (draft == null || !ExecutionStates.Contains(draft.Status)) throw new ArgumentException("测试执行状态无效。");
            return Transact(project, token, snapshot =>
            {
                var prior = snapshot.Find<TestExecution>(draft.Id);
                if (prior != null && prior.Status == "withdrawn") throw new InvalidOperationException("已撤回执行不可再修改。");
                var test = Require<TestCase>(snapshot, draft.TestCaseId);
                var retest = Ref<TestExecution>(snapshot, draft.RetestOfId);
                if (retest != null && (retest.TestCaseId != test.Id || retest.Id == draft.Id || retest.Status == "running" || retest.Status == "withdrawn"))
                    throw new InvalidOperationException("重新测试需使用同一用例已结束的另一条执行记录。");
                if (prior != null)
                {
                    if (prior.TestCaseId != draft.TestCaseId || prior.RetestOfId != draft.RetestOfId)
                        throw new InvalidOperationException("历史执行的关联不可更改。");
                    if (prior.Status != "running" || draft.Status == "withdrawn")
                    {
                        if (draft.Status != "withdrawn" || string.IsNullOrWhiteSpace(draft.Description))
                            throw new InvalidOperationException("已结束的执行须保留，撤回时填写原因。");
                        string reason = draft.Description;
                        draft = new TestExecution { Id = prior.Id, ProjectId = prior.ProjectId, TestCaseId = prior.TestCaseId,
                            TestCaseRevision = prior.TestCaseRevision, RetestOfId = prior.RetestOfId, ResultText = prior.ResultText,
                            PerformedBy = prior.PerformedBy, PerformedAt = prior.PerformedAt, Stage = prior.Stage, Number = prior.Number,
                            Title = prior.Title, Source = prior.Source, StepResults = prior.StepResults, Status = "withdrawn", Description = reason };
                        return (TestExecution)Put(snapshot, draft, expectedRevision);
                    }
                }
                else if (draft.Status == "withdrawn") throw new InvalidOperationException("新执行不能直接撤回。");
                var pinned = prior == null || prior.TestCaseRevision == test.Revision ? test :
                    snapshot.History.OfType<TestCase>().SingleOrDefault(item => item.Id == test.Id && item.Revision == prior.TestCaseRevision);
                if (pinned == null || (draft.TestCaseRevision != 0 && draft.TestCaseRevision != pinned.Revision))
                    throw new InvalidOperationException("执行对应的测试版本已变化。");
                if (draft.Status != "running" && string.IsNullOrWhiteSpace(draft.ResultText))
                    throw new ArgumentException("结束执行需要记录实际结果或原因。");
                if (!pinned.Applicable && draft.Status != "not-applicable" && draft.Status != "blocked")
                    throw new InvalidOperationException("用例标为不适用。");
                ValidateStepResults(pinned, draft);
                draft.TestCaseRevision = pinned.Revision;
                draft.Stage = pinned.Stage;
                draft.Title = pinned.Title;
                draft.Number = pinned.Number;
                draft.Source = new ValidationSource { Type = "manual", Version = pinned.Revision.ToString(CultureInfo.InvariantCulture) };
                draft.PerformedBy = CurrentActor;
                draft.PerformedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return (TestExecution)Put(snapshot, draft, expectedRevision);
            });
        }

        private static void ValidateStepResults(TestCase test, TestExecution execution)
        {
            var steps = new HashSet<string>(test.Steps.Select(step => step.Id));
            var seen = new HashSet<string>();
            if (execution.StepResults == null || execution.StepResults.Any(row => row == null || !steps.Contains(row.StepId) ||
                !seen.Add(row.StepId) || !ExecutionStates.Contains(row.Outcome) || (row.Actual ?? "").Length > 16000))
                throw new ArgumentException("执行步骤必须指向所选测试版本，且使用有效结果。");
            if (execution.Status == "passed" && (execution.StepResults.Count != steps.Count ||
                execution.StepResults.Any(row => row.Outcome != "passed" || string.IsNullOrWhiteSpace(row.Actual))))
                throw new InvalidOperationException("通过测试前，请人工记录所有步骤的实际结果。");
        }

        public ReviewRecord Review(ProjectContext project, string targetId, int targetRevision, string decision, string comment, CancellationToken token)
        {
            if (!(decision == "approved" || decision == "rejected") || string.IsNullOrWhiteSpace(comment))
                throw new ArgumentException("审核需选择结果并填写审核意见。");
            return Transact(project, token, snapshot =>
            {
                RequireReviewer(snapshot);
                var target = Require<ValidationRecord>(snapshot, targetId);
                if (target.Revision != targetRevision || target is ReviewRecord || target is TraceLink || target.Status == "withdrawn" ||
                    target is TestExecution && target.Status == "running")
                    throw new InvalidOperationException("该版本已变化或当前记录不可审核。");
                var evidence = target as Evidence;
                if (decision == "approved" && evidence != null && VerifyEvidence(project, evidence, token) != "verified")
                    throw new InvalidOperationException("证据缺失或损坏，不能批准。");
                var review = new ReviewRecord { TargetId = target.Id, TargetRevision = target.Revision, Stage = target.Stage,
                    Title = "审核 " + target.Number + " " + target.Title, Decision = decision, Status = "completed", Description = comment };
                return (ReviewRecord)Put(snapshot, review, 0);
            });
        }

        public Deviation CloseDeviation(ProjectContext project, string id, int expectedRevision, CancellationToken token)
        {
            return Transact(project, token, snapshot =>
            {
                RequireReviewer(snapshot);
                var old = Require<Deviation>(snapshot, id);
                var review = snapshot.All<ReviewRecord>().LastOrDefault(item => item.TargetId == id && item.TargetRevision == expectedRevision);
                if (old.Revision != expectedRevision || old.Status != "review-pending" || review == null || review.Decision != "approved" ||
                    string.IsNullOrWhiteSpace(old.Impact) || string.IsNullOrWhiteSpace(old.RootCause) || string.IsNullOrWhiteSpace(old.Action))
                    throw new InvalidOperationException("关闭需要待审核状态、当前版本已批准及已记录的影响、原因和处理。");
                var retest = Ref<TestExecution>(snapshot, old.RetestExecutionId);
                if (retest != null && retest.Status != "passed")
                    throw new InvalidOperationException("已指定的重新测试尚未通过。");
                var next = Json.FromJson<Deviation>(Json.ToJson(old));
                next.Status = "closed";
                next.ClosureReviewId = review.Id;
                next.ClosedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return (Deviation)Put(snapshot, next, expectedRevision);
            });
        }

        public ChangeImpact AssessImpact(ProjectContext project, string id, int expectedRevision, string assessment, string retestDecision, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(assessment) || !new[] { "required", "not-required", "undecided" }.Contains(retestDecision))
                throw new ArgumentException("请填写影响评估并选择重新测试决定。");
            return Transact(project, token, snapshot =>
            {
                RequireReviewer(snapshot);
                var previous = Require<ChangeImpact>(snapshot, id);
                var next = Json.FromJson<ChangeImpact>(Json.ToJson(previous));
                next.Assessment = Clean(assessment, 16000);
                next.RetestDecision = retestDecision;
                next.Status = retestDecision == "undecided" ? "pending-assessment" : "assessed";
                return (ChangeImpact)Put(snapshot, next, expectedRevision);
            });
        }

        internal static string RequireReviewer(ValidationSnapshot snapshot)
        {
            var plan = snapshot.All<ValidationPlan>().SingleOrDefault();
            string user = CurrentActor;
            if (plan == null || !Same(plan.OwnerId, user) && !plan.ReviewerIds.Any(id => Same(id, user)))
                throw new UnauthorizedAccessException("当前操作系统账户未获该项目审核权限。");
            return user;
        }

        public TraceLink Link(ProjectContext project, string fromId, string toId, string kind, CancellationToken token)
        {
            if (fromId == toId) throw new ArgumentException("追踪关系不能指向自身。");
            return Transact(project, token, snapshot =>
            {
                var from = Require<ValidationRecord>(snapshot, fromId);
                var to = snapshot.Find<ValidationRecord>(toId);
                string external = "";
                if (kind == "tag")
                {
                    if (!(from is ValidationRequirement || from is TestCase || from is Evidence || from is Deviation || from is RiskAssessment)
                        || !Tags.Snapshot(project, token).Items.Any(item => item.TagId == toId)) throw new InvalidOperationException("关联目标并非当前项目 Tag。");
                    external = project.Id;
                }
                else if (kind == "task" || kind == "incident")
                    throw new InvalidOperationException("请使用具备项目归属核验的任务或事件入口。");
                else if (!AllowedLink(from, to, kind)) throw new InvalidOperationException("该追踪关系的实体类型无效。");
                var same = snapshot.All<TraceLink>().FirstOrDefault(item => item.FromId == fromId && item.ToId == toId && item.Kind == kind && item.Status != "withdrawn");
                return same ?? AddLink(snapshot, from, toId, kind, external);
            });
        }

        public TraceLink LinkTask(ProjectContext project, string fromId, TaskItem task, CancellationToken token)
        {
            Scope(project);
            if (task == null || !project.IncludesTask(task) || string.IsNullOrEmpty(task.Id)) throw new UnauthorizedAccessException("任务不属于当前项目。");
            return LinkExternal(project, fromId, task.Id, "task", token);
        }

        public TraceLink LinkIncident(ProjectContext project, string fromId, string incidentId, string owningProjectId, CancellationToken token)
        {
            if (Scope(project) != owningProjectId || string.IsNullOrWhiteSpace(incidentId)) throw new UnauthorizedAccessException("事件不属于当前项目。");
            return LinkExternal(project, fromId, incidentId, "incident", token);
        }

        private TraceLink LinkExternal(ProjectContext project, string fromId, string externalId, string kind, CancellationToken token)
        {
            return Transact(project, token, snapshot =>
            {
                var from = Require<PunchItem>(snapshot, fromId);
                var link = snapshot.All<TraceLink>().FirstOrDefault(item => item.FromId == from.Id && item.ToId == externalId && item.Kind == kind && item.Status != "withdrawn");
                return link ?? AddLink(snapshot, from, externalId, kind, project.Id);
            });
        }

        private TraceLink AddLink(ValidationSnapshot snapshot, ValidationRecord from, string target, string kind, string external)
        {
            return (TraceLink)Put(snapshot, new TraceLink { FromId = from.Id, ToId = target, Kind = kind,
                TargetProjectId = external, Stage = from.Stage, Status = "active" }, 0);
        }

        private static bool AllowedLink(ValidationRecord from, ValidationRecord to, string kind)
        {
            switch (kind)
            {
                case "derives": return from is ValidationRequirement && to is ValidationRequirement;
                case "design": return from is ValidationRequirement && (to is ValidationRequirement || to is DesignReference);
                case "risk": return (from is ValidationRequirement || from is DesignReference) && to is RiskAssessment;
                case "verifies": return (from is ValidationRequirement || from is DesignReference || from is RiskAssessment) && to is TestCase;
                case "evidence": return (from is ValidationRequirement || from is TestCase || from is TestExecution || from is PunchItem) && to is Evidence;
                case "deviation": return (from is Evidence || from is TestExecution || from is TestCase) && to is Deviation;
                case "retest": return from is Deviation && to is TestExecution;
                case "capa": return from is Deviation && to is CAPARecord;
                case "impact": return from is ChangeImpact && (to is ValidationRequirement || to is TestCase || to is RiskAssessment || to is Evidence);
                case "document": return to is ValidationDocument && !(from is TraceLink) && !(from is ReviewRecord);
                default: return false;
            }
        }

        public TraceLink WithdrawLink(ProjectContext project, string id, int expectedRevision, CancellationToken token)
        {
            return Transact(project, token, snapshot =>
            {
                var prior = Require<TraceLink>(snapshot, id);
                var next = Json.FromJson<TraceLink>(Json.ToJson(prior));
                next.Status = "withdrawn";
                return (TraceLink)Put(snapshot, next, expectedRevision);
            });
        }

        public static ValidationSource CaptureSource(ProjectContext project, string path, string location, string version, CancellationToken token, string type = "file")
        {
            EngineeringSource found;
            using (EngineeringFiles.Open(project, path, type, version, MaxEvidence, token, out found))
                return new ValidationSource { Type = type, Document = found.Document, Location = Clean(location, 1000),
                    Version = Clean(version, 240), Sha256 = found.Sha256 };
        }

        public Evidence ArchiveEvidence(ProjectContext project, Evidence draft, string file, CancellationToken token)
        {
            string id = Scope(project);
            if (draft == null || !string.IsNullOrEmpty(draft.Id) || draft.Revision != 0 ||
                !string.IsNullOrEmpty(draft.ProjectId) && draft.ProjectId != id) throw new ArgumentException("证据只允许创建独立归档记录。");
            return store.Access(id, token, directory =>
            {
                var snapshot = store.Load(directory, id);
                RequireWritable(snapshot);
                var parent = Ref<Evidence>(snapshot, draft.ParentEvidenceId);
                var execution = Ref<TestExecution>(snapshot, draft.ExecutionId);
                if (execution != null && draft.TestCaseId.Length == 0) draft.TestCaseId = execution.TestCaseId;
                Ref<TestCase>(snapshot, draft.TestCaseId);
                if ((execution != null && execution.TestCaseId != draft.TestCaseId) ||
                    (parent != null && (parent.TestCaseId != draft.TestCaseId || parent.ExecutionId != draft.ExecutionId)))
                    throw new InvalidOperationException("证据关联和衍生版本必须位于相同的测试范围。");
                EngineeringSource source;
                using (var input = EngineeringFiles.Open(project, file, "evidence", draft.Source == null ? "" : draft.Source.Version, MaxEvidence, token, out source))
                {
                    draft.Sha256 = source.Sha256;
                    draft.Size = source.Bytes;
                    draft.FileName = Path.GetFileName(file);
                    draft.FileType = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    draft.StoredPath = draft.Sha256 + ".bin";
                    draft.ArchivedBy = CurrentActor;
                    draft.ArchivedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    draft.Source = new ValidationSource { Type = "evidence", Document = source.Document, Sha256 = source.Sha256,
                        Version = source.Version, Location = draft.Source == null ? "" : Clean(draft.Source.Location, 1000) };
                    draft.Status = "archived";
                    string destination = Blob(directory, draft);
                    bool added = false;
                    try
                    {
                        if (File.Exists(destination))
                        {
                            if (VerifyBlob(destination, draft, token) != "verified") throw new IOException("同哈希档案损坏，需要先恢复原始证据。");
                        }
                        else
                        {
                            CopyBlob(input, destination, draft, token, false);
                            added = true;
                        }
                        var saved = (Evidence)Put(snapshot, draft, 0);
                        store.Save(directory, snapshot, token);
                        return saved;
                    }
                    catch
                    {
                        if (added && File.Exists(destination))
                        {
                            // Deletion is safe only if the failed save did not publish references.
                            try
                            {
                                var persisted = store.Load(directory, id);
                                if (!persisted.All<Evidence>().Any(record => record.Sha256 == draft.Sha256)) File.Delete(destination);
                            }
                            catch (IOException) { }
                            catch (UnauthorizedAccessException) { }
                        }
                        throw;
                    }
                }
            });
        }

        private static void CopyBlob(Stream source, string path, Evidence draft, CancellationToken token, bool replacing)
        {
            var dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            string temp = Path.Combine(dir, ".evidence-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536];
                    int count;
                    long total = 0;
                    while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        total += count;
                        if (total > draft.Size) throw new IOException("原始证据在归档期间增长。");
                        output.Write(buffer, 0, count);
                    }
                    output.Flush(true);
                }
                if (VerifyBlob(temp, draft, token) != "verified") throw new IOException("归档写入的内容与原始证据不一致。");
                token.ThrowIfCancellationRequested();
                if (replacing && File.Exists(path))
                {
                    var damaged = Path.Combine(dir, ".damaged-" + Guid.NewGuid().ToString("N") + ".bin");
                    File.Replace(temp, path, damaged);
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public void RestoreEvidence(ProjectContext project, string evidenceId, string sourceFile, CancellationToken token)
        {
            var id = Scope(project);
            store.Access(id, token, directory =>
            {
                var snapshot = store.Load(directory, id);
                RequireWritable(snapshot);
                var evidence = Require<Evidence>(snapshot, evidenceId);
                var blob = Blob(directory, evidence);
                if (VerifyBlob(blob, evidence, token) == "verified") throw new InvalidOperationException("证据当前可完整读取，无需恢复。");
                EngineeringSource source;
                using (var input = EngineeringFiles.Open(project, sourceFile, "evidence", "", MaxEvidence, token, out source))
                {
                    if (source.Sha256 != evidence.Sha256 || source.Bytes != evidence.Size)
                        throw new InvalidDataException("恢复文件与证据归档时的原始哈希不一致。");
                    CopyBlob(input, blob, evidence, token, true);
                }
                return true;
            });
        }

        public string VerifyEvidence(ProjectContext project, Evidence evidence, CancellationToken token)
        {
            string id = Scope(project);
            if (evidence == null || evidence.ProjectId != id) throw new UnauthorizedAccessException("证据不属于当前项目。");
            return VerifyBlob(Blob(store.ProjectDirectory(id), evidence), evidence, token);
        }

        public string EvidencePath(ProjectContext project, Evidence evidence, CancellationToken token)
        {
            if (VerifyEvidence(project, evidence, token) != "verified") throw new IOException("证据缺失或校验失败。");
            return Blob(store.ProjectDirectory(project.Id), evidence);
        }

        public void ExportEvidence(ProjectContext project, Evidence evidence, string destination, CancellationToken token)
        {
            string path = EvidencePath(project, evidence, token);
            EngineeringFiles.Export(project, destination, output =>
            {
                using (var input = File.OpenRead(path))
                {
                    var buffer = new byte[65536];
                    int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                }
            }, token);
        }

        private static string Blob(string directory, Evidence evidence)
        {
            if (evidence.Sha256 == null || evidence.Sha256.Length != 64 || evidence.Sha256.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("证据哈希无效。");
            return Path.Combine(directory, "evidence", evidence.Sha256.ToLowerInvariant() + ".bin");
        }

        private static string VerifyBlob(string path, Evidence evidence, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!File.Exists(path)) return "missing";
            try
            {
                using (var stream = File.OpenRead(path))
                using (var hash = SHA256.Create())
                {
                    if (stream.Length != evidence.Size) return "corrupt";
                    var bytes = new byte[65536];
                    int count;
                    while ((count = stream.Read(bytes, 0, bytes.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        hash.TransformBlock(bytes, 0, count, bytes, 0);
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return Same(BitConverter.ToString(hash.Hash).Replace("-", ""), evidence.Sha256) ? "verified" : "corrupt";
                }
            }
            catch (FileNotFoundException) { return "missing"; }
            catch (DirectoryNotFoundException) { return "missing"; }
            catch (IOException) { return "unreadable"; }
            catch (UnauthorizedAccessException) { return "unreadable"; }
        }

        public void Restore(ProjectContext project, CancellationToken token)
        {
            string id = Scope(project);
            store.Access(id, token, directory =>
            {
                var snapshot = store.Load(directory, id);
                if (!snapshot.Recovered) throw new InvalidOperationException("当前验证台账不需要从备份恢复。");
                string path = Path.Combine(directory, "records.json");
                if (File.Exists(path)) File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N").Substring(0, 12));
                store.Save(directory, snapshot, token, false);
                return true;
            });
        }

        private static T Require<T>(ValidationSnapshot snapshot, string id) where T : ValidationRecord
        {
            var record = snapshot.Find<T>(id);
            if (record == null) throw new InvalidOperationException("当前项目内缺少关联记录或实体类型无效。");
            return record;
        }

        private static T Ref<T>(ValidationSnapshot snapshot, string id) where T : ValidationRecord
        {
            return string.IsNullOrWhiteSpace(id) ? null : Require<T>(snapshot, id);
        }

        private static string Scope(ProjectContext project)
        {
            if (project == null || string.IsNullOrWhiteSpace(project.Id)) throw new UnauthorizedAccessException("请选择工程项目。");
            return project.Id;
        }

        private static void RequireWritable(ValidationSnapshot snapshot)
        {
            if (snapshot.Recovered) throw new InvalidOperationException("验证台账正在使用备份，先恢复才能写入。");
        }

        private static bool Same(string left, string right) { return string.Equals(left, right, StringComparison.OrdinalIgnoreCase); }

        private static string Clean(string value, int limit)
        {
            var text = (value ?? "").Trim();
            if (text.Length > limit || text.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                throw new ArgumentException("记录字段过长或包含不支持的字符。");
            return text;
        }

        private static List<string> CleanList(List<string> values, string label)
        {
            if (values == null || values.Count > 200) throw new ArgumentException(label + "超出范围。");
            return values.Select(value => Clean(value, 120)).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
