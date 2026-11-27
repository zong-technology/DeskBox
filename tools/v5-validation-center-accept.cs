using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using DeskBox;
using DeskBox.AppServices;
using DeskBox.Ai;
using DeskBox.Data;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;

public static class ValidationCenterAcceptance
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static string root;
    private static int checks;
    private static ProjectContext projectA, projectB;
    private static ValidationService service;
    private static ValidationRequirement urs, fs, ds;
    private static TestProtocol protocol;
    private static TestCase test;
    private static TestExecution failure, retest;
    private static Evidence original, derived;
    private static Deviation deviation;
    private static EngineeringTag tag;
    private static ValidationImport importer;

    public static int Main(string[] args)
    {
        root = Path.Combine(Path.GetTempPath(), "db-v4-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        try
        {
            Setup();
            ImportAndReviseRequirements();
            ProtocolExecutionAndEvidence();
            TraceReviewAndRetest();
            AdditionalRecords();
            ImpactBaselinesAndReports(args.Length == 0 ? null : Path.GetFullPath(args[0]));
            IsolationAndRecovery();
            Console.WriteLine("PASS " + checks + " validation checks with synthetic engineering data and native file I/O.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine("Validation center: PASS " + checks + " / FAIL 1");
            return 1;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void Setup()
    {
        AppPaths.DataDir = Store.DataDir = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
        var store = new Store();
        store.Init();
        store.Save();
        var work = new WorkspaceStore();
        var first = work.Add("Synthetic validation", Directory.CreateDirectory(Path.Combine(root, "a")).FullName, "PLC");
        var second = work.Add("Synthetic validation", Directory.CreateDirectory(Path.Combine(root, "b")).FullName, "DCS");
        work.SetCurrent(first.Id);
        projectA = work.Context;
        work.SetCurrent(second.Id);
        projectB = work.Context;
        service = new ValidationService();
        importer = new ValidationImport(service);
        var empty = ValidationTrace.Statistics(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(Metric(empty, "requirement-coverage").Denominator == 0 && Metric(empty, "requirement-coverage").Rate == null &&
            Metric(empty, "test-pass").Rate == null, "empty coverage and pass denominators remain unavailable");
    }

    private static void ImportAndReviseRequirements()
    {
        var originalPath = Write(projectA, "URS-R01.csv",
            "Req Key,Detail,Title,System,Equipment,Risk\r\n" +
            "URS-001,Measure pressure offline,Pressure verification,PLC-1,LINE-1,medium\r\n" +
            "URS-002,Legacy alarm test,Legacy alarm,PLC-1,LINE-1,low\r\n");
        var options = new ValidationImportOptions
        {
            Stage = "URS", DocumentNumber = "SYN-URS", DocumentVersion = "R01",
            Mapping = new Dictionary<string, int> { { "Number", 0 }, { "Description", 1 }, { "Title", 2 },
                { "SystemId", 3 }, { "EquipmentId", 4 }, { "RiskLevel", 5 } }
        };
        var initial = importer.Preview(projectA, originalPath, options, None);
        Check(initial.Rows.Count == 2 && initial.Issues.All(item => !item.Blocking) && initial.Source.Sha256.Length == 64 &&
            service.Read(projectA, None).Records.Count == 0, "mapped URS preview leaves the formal ledger unchanged");
        var committed = importer.Commit(projectA, initial, false, None);
        Check(committed.Added == 2 && committed.SavedIds.Distinct().Count() == 2 &&
            service.Read(projectA, None).All<ValidationDocument>().Single().DocumentVersion == "R01",
            "URS records and source document commit together with distinct IDs");
        var first = service.Read(projectA, None).All<ValidationRequirement>().Single(item => item.Number == "URS-001");
        Check(ValidationImport.Locate(projectA, first.Source, None).Any(item => item.Contains("Measure pressure offline")) &&
            first.Source.Location == "L2" && first.DocumentVersion == "R01", "CSV evidence references the original requirement text, row and version");
        Throws<UnauthorizedAccessException>(() => importer.Commit(projectB, initial, false, None), "foreign project rejects an existing import preview");
        Throws<UnauthorizedAccessException>(() => service.Review(projectA, first.Id, first.Revision, "approved", "Synthetic review", None),
            "approval is refused before a project owner or reviewer is established");

        var currentPath = Write(projectA, "URS-R02.csv",
            "Req Key,Detail,Title,System,Equipment,Risk\r\n" +
            "URS-001,Measure pressure against reference,Pressure verification,PLC-1,LINE-1,medium\r\n" +
            "URS-003,Retain engineering export,Engineering export,PLC-1,LINE-1,high\r\n");
        options.DocumentVersion = "R02";
        var revised = importer.Preview(projectA, currentPath, options, None);
        Check(revised.Rows.Count(row => row.State == "modified") == 1 && revised.Rows.Count(row => row.State == "added") == 1 &&
            revised.Rows.Count(row => row.State == "removed") == 1, "URS preview identifies the added, removed and modified rows");
        var applied = importer.Commit(projectA, revised, true, None, true);
        var snapshot = service.Read(projectA, None);
        urs = snapshot.Find<ValidationRequirement>(first.Id);
        Check(applied.Updated == 1 && applied.Added == 1 && applied.Retired == 1 && urs.Revision == 2 &&
            snapshot.All<ValidationRequirement>().Single(item => item.Number == "URS-002").Voided,
            "explicit full revision retains stable IDs and voids removed requirements");
        Check(importer.CompareRequirement(projectA, urs.Id, 1, 2, None).Any(row => row.Field == "Description" && row.Before.Contains("offline")) &&
            snapshot.History.OfType<ValidationRequirement>().Any(item => item.Id == urs.Id && item.Source.Document == "URS-R01.csv"),
            "historical requirement data and its original source remain available");
        Check(ValidationImport.Locate(projectA, urs.Source, None).Any(item => item.Contains("against reference")), "new requirement revision resolves the new source");

        var word = Path.Combine(projectA.Root, "FS-R01.docx");
        WriteWordFixture(word, "Functional specification fixture", "FS-001: Retain the exported pressure record.");
        var wordOptions = new ValidationImportOptions { Stage = "FS", DocumentNumber = "SYN-FS", DocumentVersion = "R01" };
        var proposal = importer.Preview(projectA, word, wordOptions, None);
        Check(proposal.Rows.Count == 1 && proposal.Rows[0].Location == "段落 2" &&
            !service.Read(projectA, None).All<ValidationRequirement>().Any(item => item.Number == "FS-001"),
            "Word extraction records a real paragraph and waits for explicit commit");
        var wordResult = importer.Commit(projectA, proposal, false, None);
        fs = service.Read(projectA, None).Find<ValidationRequirement>(wordResult.SavedIds.Single());
        Check(ValidationImport.Locate(projectA, fs.Source, None).Any(item => item.Contains("exported pressure record")),
            "confirmed Word proposal can re-read the original paragraph by hash");
        var designPath = Write(projectA, "Design-R01.txt", "Synthetic design specification\r\nTrace an offline pressure reading and retain the test attachment.");
        var design = Save(projectA, new ValidationDocument { Number = "SYN-DS", Title = "Synthetic design source", Stage = "DS",
            DocumentVersion = "R01", Source = ValidationService.CaptureSource(projectA, designPath, "L2", "R01", None) });
        ds = Save(projectA, new ValidationRequirement { Number = "DS-001", Title = "Design requirement", Description = "Retain the pressure record",
            Stage = "DS", DocumentId = design.Id, SystemId = "PLC-1", EquipmentId = "LINE-1", RiskLevel = "medium" });

        var invalid = Write(projectA, "bad.csv", "Number,Description\r\nURS-DUP,first\r\nURS-DUP,second\r\n,missing number\r\n");
        var blocked = importer.Preview(projectA, invalid, new ValidationImportOptions
            { Stage = "URS", DocumentNumber = "SYN-BAD", DocumentVersion = "R01" }, None);
        int ledgerCount = service.Read(projectA, None).Records.Count;
        Check(blocked.Issues.Count(issue => issue.Blocking) >= 2 &&
            blocked.Issues.Any(issue => issue.Location.StartsWith("L")), "previews identify duplicates and missing content at source rows");
        Throws<InvalidOperationException>(() => importer.Commit(projectA, blocked, true, None), "blocked import does not publish its source document or rows");
        Check(service.Read(projectA, None).Records.Count == ledgerCount, "failed import preserves all related records");
        var changedPath = Write(projectA, "Change.csv", "Number,Description\r\nURS-C,original\r\n");
        var stale = importer.Preview(projectA, changedPath, new ValidationImportOptions
            { Stage = "URS", DocumentNumber = "SYN-CHANGE", DocumentVersion = "R01" }, None);
        File.AppendAllText(changedPath, "URS-D,added after preview\r\n");
        Throws<IOException>(() => importer.Commit(projectA, stale, false, None), "subsequent file changes invalidate a preview");
    }

    private static void ProtocolExecutionAndEvidence()
    {
        protocol = Save(projectA, new TestProtocol { Number = "SYN-OQ", Title = "Synthetic OQ protocol", Stage = "OQ" });
        var cases = Write(projectA, "Cases.csv",
            "Number,Title,Preconditions,StepAction,StepExpected\r\n" +
            "OQ-001,Offline pressure comparison,Approved fixture,Read the fixture sample,Reading equals saved reference\r\n" +
            "OQ-002,Unlinked fixture check,Approved fixture,Read the second sample,Reading is available\r\n");
        var proposal = importer.Preview(projectA, cases, new ValidationImportOptions { Kind = "testcase", Stage = "OQ", ProtocolId = protocol.Id,
            DocumentNumber = "SYN-OQ-CASES", DocumentVersion = "R01" }, None);
        var batch = importer.Commit(projectA, proposal, false, None);
        test = service.Read(projectA, None).All<TestCase>().Single(item => item.Number == "OQ-001");
        Check(batch.Added == 2 && test.Steps.Count == 1 && test.Steps[0].Id.Length == 32 &&
            test.Steps[0].ProjectId == projectA.Id && test.ProtocolId == protocol.Id, "batch test import persists a stable executable step with expected result");
        Throws<ArgumentException>(() => service.RecordExecution(projectA,
            new TestExecution { TestCaseId = test.Id, Status = "passed" }, 0, None), "a passing test requires a recorded actual result");
        Throws<InvalidOperationException>(() => service.RecordExecution(projectA,
            new TestExecution { TestCaseId = test.Id, Status = "passed", ResultText = "No step observations supplied." }, 0, None),
            "a passing procedure with steps requires observed results for every step");

        var run = service.RecordExecution(projectA, new TestExecution { TestCaseId = test.Id, Status = "running" }, 0, None);
        Check(run.TestCaseRevision == test.Revision && run.PerformedBy == ValidationService.CurrentActor && run.PerformedAt > 0,
            "execution is attributable to the current OS account and pinned to the actual case revision");
        run.Status = "failed";
        run.ResultText = "Synthetic observation: the recorded value differed from the reference.";
        run.StepResults.Add(new ValidationStepResult { StepId = test.Steps[0].Id, Outcome = "failed", Actual = "Fixture result differs." });
        failure = service.RecordExecution(projectA, run, run.Revision, None);
        Check(failure.Status == "failed" && service.Read(projectA, None).History.OfType<TestExecution>().Single(item => item.Id == failure.Id).Status == "running",
            "running to failed retains the original execution state");
        var active = service.Read(projectA, None).Find<TestExecution>(failure.Id);
        active.Status = "passed";
        Throws<InvalidOperationException>(() => service.RecordExecution(projectA, active, active.Revision, None),
            "an ended execution cannot be changed into a pass");
        var beforeEvidence = ValidationTrace.Query(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(beforeEvidence.MissingEvidenceExecutionIds.Contains(failure.Id), "RTM reports a final execution with no evidence available");

        var file = Write(projectA, "observation.txt", "DeskBox synthetic test evidence\r\nDifference observed in fixture only.\r\n");
        original = service.ArchiveEvidence(projectA, new Evidence { ExecutionId = failure.Id,
            Source = new ValidationSource { Version = "R01", Location = "L2" } }, file, None);
        var duplicate = service.ArchiveEvidence(projectA, new Evidence { ExecutionId = failure.Id }, file, None);
        Check(original.Id != duplicate.Id && original.Sha256 == duplicate.Sha256 && original.Size == new FileInfo(file).Length &&
            service.EvidencePath(projectA, original, None) == service.EvidencePath(projectA, duplicate, None),
            "duplicate evidence records point to one verified archived blob");
        var derivative = Write(projectA, "observation-annotated.txt", "Synthetic derivative: annotated for engineering review.");
        derived = service.ArchiveEvidence(projectA, new Evidence { ExecutionId = failure.Id, ParentEvidenceId = original.Id }, derivative, None);
        Check(derived.ParentEvidenceId == original.Id && derived.Sha256 != original.Sha256 &&
            service.VerifyEvidence(projectA, original, None) == "verified" && service.VerifyEvidence(projectA, derived, None) == "verified",
            "a derived evidence version does not change the verified original bytes");
        Throws<InvalidOperationException>(() => service.Save(projectA, original, original.Revision, None), "ordinary validation edits cannot overwrite archived evidence");
        var export = Path.Combine(projectA.Root, "evidence-export.txt");
        service.ExportEvidence(projectA, original, export, None);
        Check(File.ReadAllBytes(export).SequenceEqual(File.ReadAllBytes(file)), "evidence export reproduces the archived bytes");
        Throws<IOException>(() => service.ExportEvidence(projectA, original, file, None), "evidence export cannot replace an existing source");

        var oldStep = test.Steps.Single().Id;
        test.Purpose = "Reviewed fixture comparison method";
        test = Save(projectA, test, test.Revision);
        Check(test.Revision == 2 && test.Steps.Single().Id == oldStep && failure.TestCaseRevision == 1 &&
            service.Read(projectA, None).History.OfType<TestCase>().Single(item => item.Id == test.Id).Steps.Single().Action == "Read the fixture sample",
            "case edits preserve step identity and historical executed case revisions");
    }

    private static void TraceReviewAndRetest()
    {
        var plan = Save(projectA, new ValidationPlan { Title = "Synthetic validation plan", Number = "SYN-PLAN", Stages = new List<string>
            { "URS", "FS", "DS", "OQ", "Custom fixture check" }, RiskRule = "Fixture-only severity assessment" });
        Check(plan.OwnerId == ValidationService.CurrentActor && plan.Stages.Contains("Custom fixture check"),
            "project verification plan records the local owner and a custom stage");
        Throws<UnauthorizedAccessException>(() => service.Review(projectB, urs.Id, urs.Revision, "approved", "Foreign", None),
            "review privilege and target are confined to the selected project");

        var risk = Save(projectA, new RiskAssessment { Number = "R-001", Title = "Recorded fixture risk", Stage = "OQ",
            Level = "medium", Control = "Retain the original sample", ResidualRisk = "Review pending", Method = "Project matrix" });
        tag = new TagCenter().Save(projectA, new EngineeringTag { SystemId = "PLC-1", TagName = "PT-100", EquipmentId = "LINE-1",
            DataType = "REAL", EngineeringUnit = "kPa" }, 0, None);
        service.Link(projectA, urs.Id, fs.Id, "derives", None);
        service.Link(projectA, fs.Id, ds.Id, "derives", None);
        service.Link(projectA, ds.Id, risk.Id, "risk", None);
        service.Link(projectA, risk.Id, test.Id, "verifies", None);
        service.Link(projectA, urs.Id, tag.TagId, "tag", None);
        service.Link(projectA, test.Id, tag.TagId, "tag", None);
        service.Link(projectA, urs.Id, original.Id, "evidence", None);
        Check(ValidationTrace.Related(projectA, service.Read(projectA, None), fs.Id, None, service).Count == 2,
            "RTM exposes incoming URS and outgoing DS trace links");
        var trace = ValidationTrace.Query(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(trace.Rows.Any(row => row.RequirementId == urs.Id && row.TestCaseId == test.Id && row.Covered && row.RiskIds.Contains(risk.Id)) &&
            trace.OrphanTestCaseIds.Count == 1 && trace.UncoveredRequirementIds.Any() && trace.VoidedRequirementIds.Any(),
            "directed multi-hop RTM distinguishes valid coverage, untested requirements, orphan cases and voided originals");
        Check(trace.Rows.Any(row => row.TestCaseId == test.Id && row.ExecutionStatus == "not-executed" &&
            row.HistoricalExecutionCount >= 1), "a previous case revision does not count as execution of the revised case");

        var review = service.Review(projectA, urs.Id, urs.Revision, "approved", "Reviewed against synthetic source", None);
        Check(review.CreatedBy == ValidationService.CurrentActor && review.TargetRevision == urs.Revision &&
            ValidationTrace.Query(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service).Rows
                .Any(row => row.RequirementId == urs.Id && row.ReviewStatus == "approved"), "an attributable human review appears in RTM at exactly its reviewed version");

        deviation = Save(projectA, new Deviation { Number = "DEV-001", Title = "Fixture threshold discrepancy", Stage = "OQ",
            ExecutionId = failure.Id, Status = "investigating", Expected = "Reference observation", Actual = failure.ResultText,
            RiskLevel = "medium", Investigation = "Checked only the synthetic offline fixture" });
        service.Link(projectA, original.Id, deviation.Id, "deviation", None);
        Throws<InvalidOperationException>(() => service.CloseDeviation(projectA, deviation.Id, deviation.Revision, None),
            "a failure-related deviation cannot close without its current review");

        var previousStats = ValidationTrace.Statistics(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(Metric(previousStats, "executed-tests").Numerator == 0 && Metric(previousStats, "test-pass").Rate == null &&
            Metric(previousStats, "open-deviations").Numerator == 1, "case revision and open deviation have independent metrics");
        retest = service.RecordExecution(projectA, new TestExecution { TestCaseId = test.Id, RetestOfId = failure.Id, Status = "passed",
            ResultText = "Synthetic re-test observation matched the recorded fixture reference.",
            StepResults = new List<ValidationStepResult> { new ValidationStepResult { StepId = test.Steps[0].Id,
                Outcome = "passed", Actual = "Fixture value matched the saved reference." } }
        }, 0, None);
        var retestEvidence = Write(projectA, "retest.txt", "Synthetic successful fixture observation, recorded by the acceptance harness.");
        service.ArchiveEvidence(projectA, new Evidence { ExecutionId = retest.Id }, retestEvidence, None);
        trace = ValidationTrace.Query(projectA, service.Read(projectA, None), new ValidationTraceFilter { TagId = tag.TagId }, None, service);
        Check(trace.Rows.Any(row => row.RequirementId == urs.Id && row.ExecutionId == retest.Id && row.OpenDeviationIds.Contains(deviation.Id)),
            "passing retest leaves the original open deviation visible in the current RTM");
        deviation.RetestExecutionId = retest.Id;
        deviation.Impact = "Limited to synthetic fixture comparison.";
        deviation.RootCause = "Synthetic input row was intentionally varied.";
        deviation.Action = "Recorded offline correction and rerun.";
        deviation.Status = "review-pending";
        deviation = Save(projectA, deviation, deviation.Revision);
        var closingReview = service.Review(projectA, deviation.Id, deviation.Revision, "approved", "Reviewed fixture failure and attached retest", None);
        deviation = service.CloseDeviation(projectA, deviation.Id, deviation.Revision, None);
        Check(deviation.Status == "closed" && deviation.ClosedAt > 0 && deviation.ClosureReviewId == closingReview.Id && retest.RetestOfId == failure.Id &&
            service.Read(projectA, None).Find<TestExecution>(failure.Id).Status == "failed", "reviewed close retains both failed original and successful retest");
        var totals = ValidationTrace.Statistics(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(Metric(totals, "executed-tests").Numerator == 1 && Metric(totals, "test-cases").Numerator == 2 &&
            Metric(totals, "test-pass").Numerator == 1 && Metric(totals, "test-pass").Denominator == 1 &&
            totals.HistoricalExecutions == 2, "current pass statistics exclude old failed revisions and count history separately");
        trace = ValidationTrace.Query(projectA, service.Read(projectA, None), new ValidationTraceFilter { TagId = tag.TagId }, None, service);
        Check(trace.Rows.Any(row => row.RequirementId == urs.Id && row.ExecutionId == retest.Id && !row.EvidenceMissing &&
            row.DeviationIds.Contains(deviation.Id) && !row.OpenDeviationIds.Contains(deviation.Id)) &&
            trace.InvalidLinkIds.Count == 0, "Tag filter resolves current passing execution and verified evidence");
    }

    private static void AdditionalRecords()
    {
        var capa = Save(projectA, new CAPARecord { Number = "CAPA-01", Title = "Fixture corrective and preventive actions",
            Owner = "Synthetic owner", RootCause = "Fixture row was altered", Correction = "Repeat comparison", Prevention = "Retain fixture hash" });
        service.Link(projectA, deviation.Id, capa.Id, "capa", None);
        var punch = Save(projectA, new PunchItem { Number = "PUNCH-01", Title = "Fixture follow-up", SystemId = "PLC-1", EquipmentId = "LINE-1",
            Owner = "Synthetic engineering", Priority = "low" });
        service.Link(projectA, punch.Id, original.Id, "evidence", None);
        var doc = service.Read(projectA, None).All<ValidationDocument>().First(item => item.Number == "SYN-DS");
        Save(projectA, new Deliverable { Number = "DEL-001", Title = "Fixture design deliverable", Stage = "交付", Status = "ready", DocumentId = doc.Id });
        Save(projectA, new ComplianceCheck { Number = "CHK-001", Title = "Fixture integrity checklist", Framework = "ALCOA+",
            BasisVersion = "Project training draft", BasisLocation = "Synthetic checklist item 1", Observation = "Review remains independent." });
        var totals = ValidationTrace.Statistics(projectA, service.Read(projectA, None), new ValidationTraceFilter(), None, service);
        Check(Metric(totals, "deliverable-completeness").Numerator == 1 && Metric(totals, "deliverable-completeness").Denominator == 1 &&
            service.Read(projectA, None).Find<CAPARecord>(capa.Id).Prevention.Length > 0, "manual deliverable and CAPA records contribute actual linked data");
    }

    private static void IsolationAndRecovery()
    {
        var unrelated = Save(projectB, new ValidationRequirement { Number = urs.Number, Description = "Other project fixture",
            Stage = urs.Stage, DocumentNumber = urs.DocumentNumber });
        var otherTag = new TagCenter().Save(projectB, new EngineeringTag { SystemId = tag.SystemId, TagName = tag.TagName, DataType = "REAL" }, 0, None);
        var sameName = Write(projectB, original.FileName, "Different project fixture contents.");
        var foreign = service.ArchiveEvidence(projectB, new Evidence(), sameName, None);
        Check(unrelated.Id != urs.Id && otherTag.TagId != tag.TagId && foreign.FileName == original.FileName &&
            service.Read(projectB, None).All<ValidationRequirement>().Single().Id == unrelated.Id &&
            foreign.Sha256 != original.Sha256, "same business numbers, Tag names and evidence names do not cross projects");
        Throws<InvalidOperationException>(() => service.Link(projectA, urs.Id, otherTag.TagId, "tag", None), "cross-project Tag link is refused");
        Throws<UnauthorizedAccessException>(() => service.VerifyEvidence(projectB, original, None), "cross-project evidence lookup is refused");
        Throws<InvalidOperationException>(() => service.Link(projectB, unrelated.Id, fs.Id, "derives", None), "cross-project requirement edge is refused");

        var path = service.EvidencePath(projectA, original, None);
        var bytes = File.ReadAllBytes(path);
        File.Delete(path);
        Check(service.VerifyEvidence(projectA, original, None) == "missing", "missing evidence is reported explicitly");
        File.WriteAllBytes(path, new byte[bytes.Length]);
        Check(service.VerifyEvidence(projectA, original, None) == "corrupt", "same-length substituted evidence fails SHA-256 verification");
        Throws<InvalidOperationException>(() => service.Review(projectA, original.Id, original.Revision, "approved", "Synthetic corrupt file", None),
            "a reviewer cannot approve corrupt evidence");
        File.WriteAllBytes(path, bytes);

        var backup = DataBackupService.CreateBackup("validation-fixture");
        Check(backup.Ok && Directory.GetFiles(backup.Directory, "*.bin", SearchOption.AllDirectories).Length >= 3,
            "whole-application backup includes actual archived evidence binaries");
        File.Delete(path);
        string restoreMessage;
        Check(DataBackupService.ScheduleRestore(backup.Directory).Ok && DataBackupService.ApplyPendingRestore(out restoreMessage) &&
            service.VerifyEvidence(projectA, original, None) == "verified", "full restore brings back missing evidence bytes without changing any reference ID");

        var current = service.Read(projectA, None).Find<ValidationRequirement>(urs.Id);
        var stale = service.Read(projectA, None).Find<ValidationRequirement>(urs.Id);
        current.Title = "New current title";
        current = Save(projectA, current, current.Revision);
        stale.Title = "Stale write should fail";
        Throws<InvalidOperationException>(() => service.Save(projectA, stale, stale.Revision, None), "optimistic revision control rejects a stale update");
        Check(service.Read(projectA, None).Find<ValidationRequirement>(urs.Id).Title == "New current title", "failed revision keeps durable current contents");

        var recordsFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "records.json");
        File.WriteAllText(recordsFile, "{\"Version\":1,\"Payload\":\"damaged\",\"Hash\":\"invalid\"}");
        Check(service.Read(projectA, None).Recovered, "invalid record checksum switches the affected project to a backup view");
        Throws<InvalidOperationException>(() => service.Save(projectA, new ValidationRequirement
            { Number = "LOCKED", Description = "Do not save to a recovered view." }, 0, None), "recovered snapshot is read-only until operator restore");
        service.Restore(projectA, None);
        Check(!service.Read(projectA, None).Recovered && Directory.GetFiles(Path.GetDirectoryName(recordsFile), "*.damaged-*").Length == 1 &&
            service.Read(projectB, None).Find<ValidationRequirement>(unrelated.Id) != null,
            "atomic explicit restore preserves damaged metadata and the other project");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Throws<OperationCanceledException>(() => service.ArchiveEvidence(projectA, new Evidence(), sameName, cancel.Token), "cancelled archive never writes metadata");
        }
        string occupied = Path.Combine(root, "occupied");
        File.WriteAllText(occupied, "occupied");
        Throws<IOException>(() => new ValidationService(occupied).Save(projectA, new ValidationRequirement
            { Number = "FAIL", Description = "Disk failure" }, 0, None), "unwritable repository reports an actual I/O failure");
        Check(File.ReadAllText(occupied) == "occupied", "failed write does not overwrite unrelated occupied path");
    }

    private static void ImpactBaselinesAndReports(string sampleDirectory)
    {
        var baseline = ValidationTrace.CaptureBaseline(service, projectA, "Synthetic baseline A", ValidationService.CurrentActor, None);
        var ledger = new TagCenter();
        var oldFile = Write(projectA, "io-before.csv", "Tag,System,DataType,EngineeringUnit\r\nPT-100,PLC-1,REAL,kPa\r\n");
        var newFile = Write(projectA, "io-after.csv", "Tag,System,DataType,EngineeringUnit\r\nPT-100,PLC-1,REAL,bar\r\nPT-200,PLC-1,REAL,bar\r\n");
        ledger.Commit(projectA, ledger.Preview(projectA, oldFile, "R01", "", None), false, None);
        ledger.Commit(projectA, ledger.Preview(projectA, newFile, "R02", "", None), true, None);
        var diff = EngineeringDocuments.Compare(projectA, oldFile, newFile, None, beforeVersion: "R01", afterVersion: "R02");
        var impact = ValidationTrace.RecordImpact(service, projectA, diff, "Synthetic change impact", ValidationService.CurrentActor, None);
        Check(impact.Status == "pending-assessment" && impact.Before.Sha256 == diff.Before.Sha256 &&
            impact.After.Sha256 == diff.After.Sha256 && impact.TagIds.Contains(tag.TagId), "impact compares real source bytes and binds stable Tag identity using location");
        Check(impact.Affected.Any(item => item.Id == urs.Id) && impact.Affected.Any(item => item.Id == test.Id) &&
            impact.Affected.Any(item => item.Id == original.Id), "impact propagates through requirements, tests and evidence");
        Check(service.Read(projectA, None).Find<TestExecution>(retest.Id).Status == "passed", "impact leaves historical human execution outcomes intact");
        var tagImpact = ValidationTrace.RecordTagImpact(service, projectA, tag.TagId, 1,
            ledger.History(projectA, tag.TagId, None).Max(item => item.Revision), "Tag revision impact", ValidationService.CurrentActor, None);
        Check(tagImpact.TagIds.Single() == tag.TagId && tagImpact.BeforeSnapshotSha256.Length == 64, "stable Tag ledger revision impact retains both snapshot digests");
        var current = ValidationTrace.CaptureBaseline(service, projectA, "Synthetic baseline B", ValidationService.CurrentActor, None);
        var changes = ValidationTrace.CompareBaselines(projectA, baseline, current, None);
        Check(changes.Any(item => item.Id == tag.TagId && item.Difference == "modified") &&
            service.Read(projectA, None).Find<ValidationBaseline>(baseline.Id).Revision == 1, "baseline comparison preserves original pins and detects updated Tag revisions");

        var reports = new ValidationReports(service);
        var matrix = Path.Combine(projectA.Root, "synthetic-validation-rtm.xlsx");
        var word = Path.Combine(projectA.Root, "synthetic-validation-summary-draft.docx");
        var impactWord = Path.Combine(projectA.Root, "synthetic-validation-impact-draft.docx");
        var baselineExport = Path.Combine(projectA.Root, "synthetic-validation-baseline.json");
        reports.Export(projectA, "rtm", matrix, None);
        reports.Export(projectA, "summary", word, None);
        reports.Export(projectA, "impact", impactWord, None);
        ValidationTrace.ExportBaseline(projectA, current, baselineExport, None);
        Check(ArchiveXml(matrix, "xl/worksheets/sheet1.xml").Contains(urs.Id) && ArchiveXml(matrix, "xl/worksheets/sheet1.xml").Contains(retest.Id), "XLSX RTM carries recorded requirement and execution IDs");
        Check(ArchiveXml(word, "word/document.xml").Contains("待审核草稿") && ArchiveXml(word, "word/document.xml").Contains(projectA.Id), "Word summary is a draft derived from current project records");
        Check(ArchiveXml(impactWord, "word/document.xml").Contains(diff.Before.Sha256) && File.ReadAllText(baselineExport).Contains(current.Id),
            "impact and baseline exports carry captured source hashes and snapshot identity");
        var template = Path.Combine(projectA.Root, "synthetic-template.docx");
        var mapped = Path.Combine(projectA.Root, "synthetic-validation-template-draft.docx");
        WriteWordFixture(template, "{{CompanyTitle}}", "{{Body}}");
        reports.Export(projectA, "summary", mapped, None, template, new Dictionary<string, string> { { "CompanyTitle", "Title" } });
        Check(ArchiveXml(mapped, "word/document.xml").Contains("验证总结报告") &&
            ArchiveXml(mapped, "word/document.xml").Contains("待审核草稿"), "DOCX template mapping retains the draft marker");
        Throws<IOException>(() => reports.Export(projectA, "rtm", matrix, None), "report export refuses to overwrite existing output");

        var runtime = new AgentRuntime(new AgentTaskStore());
        string reply = "";
        var request = new AgentRunRequest { Project = projectA, SessionId = "synthetic-validation",
            Text = "工具 {\"name\":\"validation.query\",\"args\":{\"view\":\"requirements\",\"query\":\"URS-001\"}}" };
        var task = runtime.Run(request, None, reply: (body, error, kind) => reply = body);
        Check(task.State == "succeeded" && reply.Contains(urs.Id) && reply.Contains("URS-R02.csv"), "local Agent query cites true requirement source");
        request.Text = "工具 {\"name\":\"validation.query\",\"args\":{\"view\":\"approve\"}}";
        task = runtime.Run(request, None);
        Check(task.State == "failed" && service.Read(projectA, None).Find<TestExecution>(retest.Id).Revision == retest.Revision, "validation Agent rejects attempts to change approval or execution");
        if (!string.IsNullOrWhiteSpace(sampleDirectory))
        {
            Directory.CreateDirectory(sampleDirectory);
            foreach (var sample in new[] { matrix, word, impactWord, mapped, baselineExport })
                File.Copy(sample, Path.Combine(sampleDirectory, Path.GetFileName(sample)), true);
        }
    }

    private static string ArchiveXml(string path, string entry)
    {
        using (var archive = ZipFile.OpenRead(path))
        using (var stream = new StreamReader(archive.GetEntry(entry).Open()))
            return stream.ReadToEnd();
    }

    private static ValidationMetric Metric(ValidationStatistics stats, string key)
    {
        return stats.Items.Single(metric => metric.Key == key);
    }

    private static T Save<T>(ProjectContext project, T item, int revision = 0) where T : ValidationRecord
    {
        return (T)service.Save(project, item, revision, None);
    }

    private static string Write(ProjectContext project, string filename, string contents)
    {
        var path = Path.Combine(project.Root, filename);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static void WriteWordFixture(string path, params string[] paragraphs)
    {
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            ZipEntry(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            ZipEntry(archive, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            var content = new XDocument(new XElement(word + "document", new XElement(word + "body",
                paragraphs.Select(text => new XElement(word + "p", new XElement(word + "r", new XElement(word + "t", text)))))));
            ZipEntry(archive, "word/document.xml", content.ToString());
        }
    }

    private static void ZipEntry(ZipArchive archive, string name, string contents)
    {
        using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(contents);
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
        checks++;
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, name); return; }
        throw new InvalidOperationException(name);
    }
}
