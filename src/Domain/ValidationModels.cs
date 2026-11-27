using System.Collections.Generic;
using System.Linq;

namespace DeskBox.Domain
{
    public sealed class ValidationSource
    {
        public string Type = "manual", Document = "", Location = "", Version = "", Sha256 = "", Encoding = "";
    }

    public abstract class ValidationRecord
    {
        public string Id = "", ProjectId = "", CreatedBy = "", UpdatedBy = "";
        public long CreatedAt, UpdatedAt;
        public int Revision;
        public ValidationSource Source = new ValidationSource();
        public string Stage = "", Number = "", Title = "", Description = "", SystemId = "", EquipmentId = "", Status = "draft";
    }

    public sealed class ValidationPlan : ValidationRecord
    {
        public string OwnerId = "", RiskRule = "";
        public List<string> ReviewerIds = new List<string>();
        public List<string> Stages = new List<string>();
    }

    public sealed class ValidationDocument : ValidationRecord
    {
        public string DocumentVersion = "", DocumentType = "";
        public bool Applicable = true;
    }

    public sealed class ValidationRequirement : ValidationRecord
    {
        public string DocumentId = "", DocumentNumber = "", DocumentVersion = "", Category = "", RiskLevel = "";
        public bool Applicable = true, Voided;
    }

    public sealed class DesignReference : ValidationRecord
    {
        public string DocumentId = "";
    }

    public sealed class RiskAssessment : ValidationRecord
    {
        public string Level = "", ImpactCategory = "", Control = "", ResidualRisk = "", Method = "";
    }

    public sealed class TestProtocol : ValidationRecord
    {
        public string DocumentId = "";
    }

    public sealed class TestCase : ValidationRecord
    {
        public string ProtocolId = "", Purpose = "", Preconditions = "", ExpectedResult = "";
        public bool Applicable = true;
        public List<TestStep> Steps = new List<TestStep>();
    }

    public sealed class TestStep : ValidationRecord
    {
        public int Order;
        public string Action = "", ExpectedResult = "";
    }

    public sealed class ValidationStepResult
    {
        public string StepId = "", Actual = "", Outcome = "";
    }

    public sealed class TestExecution : ValidationRecord
    {
        public string TestCaseId = "", ResultText = "", PerformedBy = "", RetestOfId = "";
        public int TestCaseRevision;
        public long PerformedAt;
        public List<ValidationStepResult> StepResults = new List<ValidationStepResult>();
    }

    public sealed class Evidence : ValidationRecord
    {
        public string TestCaseId = "", ExecutionId = "", ParentEvidenceId = "", FileName = "", Sha256 = "",
            StoredPath = "", ArchivedBy = "", FileType = "";
        public long Size, ArchivedAt;
    }

    public sealed class Deviation : ValidationRecord
    {
        public string ExecutionId = "", TestCaseId = "", RetestExecutionId = "", Expected = "", Actual = "", Impact = "",
            RiskLevel = "", Investigation = "", RootCause = "", Action = "";
        public long ClosedAt;
        public string ClosureReviewId = "";
    }

    public sealed class CAPARecord : ValidationRecord
    {
        public string Owner = "", Due = "", Correction = "", Prevention = "", Implementation = "", Effectiveness = "", RootCause = "";
    }

    public sealed class PunchItem : ValidationRecord
    {
        public string Owner = "", Due = "", Priority = "", Treatment = "";
    }

    public sealed class ReviewRecord : ValidationRecord
    {
        public string TargetId = "", Decision = "";
        public int TargetRevision;
    }

    public sealed class TraceLink : ValidationRecord
    {
        public string FromId = "", ToId = "", Kind = "", TargetProjectId = "";
    }

    public sealed class Deliverable : ValidationRecord
    {
        public bool Required = true;
        public string DocumentId = "";
    }

    public sealed class ComplianceCheck : ValidationRecord
    {
        public string Framework = "", BasisVersion = "", BasisLocation = "", Observation = "";
        public bool Applicable = true;
    }

    public sealed class ValidationSnapshot
    {
        public string ProjectId = "", Stamp = "";
        public bool Recovered;
        public List<ValidationRecord> Records = new List<ValidationRecord>();
        public List<ValidationRecord> History = new List<ValidationRecord>();

        public IEnumerable<T> All<T>() where T : ValidationRecord { return Records.OfType<T>(); }
        public T Find<T>(string id) where T : ValidationRecord { return All<T>().FirstOrDefault(record => record.Id == id); }
    }
}
