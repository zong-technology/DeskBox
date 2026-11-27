using System.Collections.Generic;

namespace DeskBox.Domain
{
    public sealed class ValidationTraceFilter
    {
        public string Stage = "", SystemId = "", EquipmentId = "", TagId = "", RiskLevel = "";
    }

    public sealed class ValidationTraceRow
    {
        public string RequirementId = "", RequirementNumber = "", RequirementStage = "", RequirementText = "";
        public string RequirementDocumentId = "", RequirementDocumentVersion = "";
        public ValidationSource ObjectSource, TestSource, ExecutionSource;
        public int RequirementRevision;
        public string TestCaseId = "", TestCaseNumber = "", TestCaseTitle = "", TestStage = "";
        public int CaseRevision, ExecutionCaseRevision, HistoricalExecutionCount;
        public string ExecutionId = "", ExecutionStatus = "not-executed", ExecutionResult = "", ExecutionActor = "";
        public long ExecutionAt;
        public string ReviewStatus = "pending", TestReviewStatus = "pending", ExecutionReviewStatus = "pending";
        public bool Covered, Applicable, Voided, OrphanTest, EvidenceMissing, EvidenceChecked, Failure;
        public List<string> TagIds = new List<string>(), RiskIds = new List<string>(), DesignIds = new List<string>();
        public List<string> RelatedRequirementIds = new List<string>(), EvidenceIds = new List<string>();
        public List<string> UnavailableEvidenceIds = new List<string>(), DeviationIds = new List<string>(), OpenDeviationIds = new List<string>();
    }

    public sealed class ValidationTraceResult
    {
        public string ProjectId = "";
        public List<ValidationTraceRow> Rows = new List<ValidationTraceRow>();
        public List<string> InvalidLinkIds = new List<string>();
        public List<string> UncoveredRequirementIds = new List<string>(), PendingRequirementIds = new List<string>(), VoidedRequirementIds = new List<string>();
        public List<string> OrphanTestCaseIds = new List<string>(), MissingEvidenceExecutionIds = new List<string>();
        public List<string> FailedExecutionIds = new List<string>(), OpenDeviationIds = new List<string>();
    }

    public sealed class ValidationMetric
    {
        public string Key = "", Label = "", Definition = "";
        public int Numerator;
        public int? Denominator;
        public double? Rate
        {
            get
            {
                return Denominator.HasValue && Denominator.Value > 0 ?
                    (double?)Numerator / Denominator.Value : null;
            }
        }
    }

    public sealed class ValidationStatistics
    {
        public string ProjectId = "";
        public int HistoricalExecutions;
        public List<ValidationMetric> Items = new List<ValidationMetric>();
    }

    public sealed class ValidationBaselineEntry
    {
        public string Id = "", Kind = "", Number = "", Status = "";
        public int Revision;
        public ValidationSource Source;
        public string FromId = "", ToId = "", LinkKind = "";
        public string ReviewStatus = "";
    }

    public sealed class ValidationBaseline : ValidationRecord
    {
        public string StateStamp = "";
        public List<ValidationBaselineEntry> Records = new List<ValidationBaselineEntry>();
        public List<ValidationBaselineEntry> TagRevisions = new List<ValidationBaselineEntry>();
        public List<string> InvalidLinkIds = new List<string>();
    }

    public sealed class ValidationBaselineDifference
    {
        public string Id = "", Kind = "", Difference = "";
        public ValidationBaselineEntry Before, After;
    }

    public sealed class ChangeImpact : ValidationRecord
    {
        public EngineeringSource Before, After;
        public string BeforeSnapshotSha256 = "", AfterSnapshotSha256 = "";
        public string Assessment = "", RetestDecision = "";
        public List<EngineeringDelta> Changes = new List<EngineeringDelta>();
        public List<EngineeringIssue> Issues = new List<EngineeringIssue>();
        public List<string> TagIds = new List<string>(), UnresolvedChangeKeys = new List<string>();
        public List<ValidationBaselineEntry> Affected = new List<ValidationBaselineEntry>();
        public List<ValidationImpactMatch> Matches = new List<ValidationImpactMatch>();
        public int Added, Removed, Modified, Unclassified, Unavailable;
    }

    public sealed class ValidationImpactMatch
    {
        public string ChangeKey = "", ChangeState = "", TagId = "", Side = "", Document = "", Sha256 = "", Location = "";
        public int SourceRevisionBefore, SourceRevisionAfter;
    }
}
