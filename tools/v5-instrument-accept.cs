using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.AppServices;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;

internal static class InstrumentAcceptance
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static int count;

    private static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "db-instr-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            AppPaths.DataDir = Store.DataDir = Directory.CreateDirectory(Path.Combine(root, "data")).FullName;
            var store = new Store();
            store.Init();
            store.Save();
            var work = new WorkspaceStore();
            var a = work.Add("Synthetic instrument project", Directory.CreateDirectory(Path.Combine(root, "a")).FullName, "PLC");
            var b = work.Add("Synthetic instrument project", Directory.CreateDirectory(Path.Combine(root, "b")).FullName, "DCS");
            work.SetCurrent(a.Id);
            var first = work.Context;
            work.SetCurrent(b.Id);
            var second = work.Context;
            var service = new InstrumentService();
            var tags = new TagCenter();
            var tagA = tags.Save(first, new EngineeringTag { SystemId = "PLC", TagName = "PT_010", DataType = "Real" }, 0, None);
            var tagB = tags.Save(second, new EngineeringTag { SystemId = "PLC", TagName = "PT_010", DataType = "Real" }, 0, None);
            var manual = Write(first, "manual.txt", "Synthetic instrument manual.");
            var cert = Write(first, "certificate.txt", "Synthetic original gauge certificate description.");
            var foreign = Write(second, "certificate.txt", "Synthetic second project.");
            Check(service.Read(first, None).Assets.Count == 0, "empty register uses the selected project");
            Throws<InvalidOperationException>(() => service.Save(first, Draft(tagB.TagId), 0, None), "foreign Tag ID rejected");
            Throws<UnauthorizedAccessException>(() => service.Save(first, Draft(tagA.TagId), 0, None, foreign), "foreign manual source rejected");
            var asset = service.Save(first, Draft(tagA.TagId), 0, None, manual);
            var other = service.Save(second, Draft(tagB.TagId), 0, None);
            Check(asset.Id != other.Id && asset.TagId != other.TagId && service.Read(first, None).Assets.Single().Id == asset.Id,
                "same asset number and Tag label stay isolated");
            Throws<InvalidOperationException>(() => service.Save(first, Draft(tagA.TagId), 0, None), "duplicate asset numbers refused");
            Throws<UnauthorizedAccessException>(() => service.Save(second, asset, 1, None), "foreign stored asset refused");
            Check(service.VerifyAttachment(first, asset.Id, null, None) == manual && asset.Manual.Sha256.Length == 64,
                "manual is recorded with real project-relative provenance");
            Throws<ArgumentException>(() => service.Save(first, new InstrumentAsset { Number = "RANGE", Name = "Fixture",
                Kind = "压力", RangeLow = 9, RangeHigh = 1 }, 0, None), "backward measurement range refused");

            var copy = Json.FromJson<InstrumentAsset>(Json.ToJson(asset));
            copy.Location = "Fixture skid A";
            var edited = service.Save(first, copy, 1, None);
            var loaded = new InstrumentService().Read(first, None);
            Check(edited.Id == asset.Id && edited.Revision == 2 && loaded.History.Single().Location.Length == 0
                && loaded.Assets.Single().Manual.Sha256 == asset.Manual.Sha256, "edit pins original revision and retains manual");
            Throws<InvalidOperationException>(() => service.Save(first, copy, 1, None), "stale edit refused");

            var today = DateTime.Today;
            var original = new InstrumentActivity { AssetId = asset.Id, Kind = "calibration", Date = Date(today.AddDays(-10)),
                DueDate = Date(today.AddDays(5)), CertificateNumber = "CAL-SYN-01", Notes = "Synthetic observed result recorded by the test." };
            var recorded = service.Record(first, original, None, cert);
            Check(recorded.Source.Document == "certificate.txt" && recorded.Source.Bytes == new FileInfo(cert).Length &&
                service.VerifyAttachment(first, asset.Id, recorded.Id, None) == cert, "certificate reference verifies source bytes");
            Throws<InvalidOperationException>(() => service.Record(second, original, None, foreign), "foreign asset refused for activity");
            Throws<ArgumentException>(() => service.Record(first, new InstrumentActivity { AssetId = asset.Id, Kind = "calibration",
                Date = Date(today), DueDate = Date(today.AddDays(-1)), Notes = "Synthetic" }, None), "due date before observation refused");
            Throws<ArgumentException>(() => service.Record(first, new InstrumentActivity { AssetId = asset.Id, Kind = "maintenance",
                Date = Date(today.AddDays(5)), Notes = "Synthetic" }, None), "future actual maintenance refused");
            var proof = new ValidationService().ArchiveEvidence(first, new Evidence(), cert, None);
            var next = service.Record(first, new InstrumentActivity { AssetId = asset.Id, Kind = "calibration", Date = Date(today),
                DueDate = Date(today), CertificateNumber = "CAL-SYN-02", Notes = "Synthetic second observed result.", EvidenceId = proof.Id }, None);
            service.Record(first, new InstrumentActivity { AssetId = asset.Id, Kind = "maintenance", Date = Date(today),
                Notes = "Synthetic maintenance history." }, None);
            Check(next.EvidenceId == proof.Id && next.Source.Sha256 == proof.Sha256, "archived verification evidence reused by stable ID");
            File.WriteAllText(cert, "Synthetic modified reference copy.");
            Throws<InvalidDataException>(() => service.VerifyAttachment(first, asset.Id, recorded.Id, None), "modified original reference detected");
            Check(service.VerifyAttachment(first, asset.Id, next.Id, None) == "", "byte-preserved archive verifies after source changes");
            File.Delete(manual);
            Throws<FileNotFoundException>(() => service.VerifyAttachment(first, asset.Id, null, None), "missing manual detected");

            var selected = InstrumentService.LatestCalibration(service.Read(first, None).Activities);
            Check(selected.Id == next.Id && InstrumentService.DueStatus(selected, today) == "今日到期"
                && InstrumentService.DueStatus(selected, today.AddDays(1)) == "已逾期"
                && InstrumentService.DueStatus(recorded, today) == "30 天内到期"
                && InstrumentService.DueStatus(recorded, today.AddDays(-30)) == "未到期"
                && InstrumentService.DueStatus(null, today) == "未设到期日", "due states use actual date and ignore maintenance");
            string csv = Path.Combine(first.Root, "assets.csv"), xlsx = Path.Combine(first.Root, "assets.xlsx");
            service.Export(first, csv, None);
            service.Export(first, xlsx, None);
            Check(File.ReadAllText(csv).Contains(proof.Sha256) && File.ReadAllText(csv).Contains(tagA.TagId)
                && !File.ReadAllText(csv).Contains(tagB.TagId) && EngineeringDocuments.Read(first, xlsx, "", None).Records.Count == 4,
                "CSV and XLSX export traceable scoped asset and activity rows");
            Throws<IOException>(() => service.Export(first, csv, None), "export does not overwrite");
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                Throws<OperationCanceledException>(() => service.Save(first, Draft(), 0, stop.Token), "cancelled save leaves no ledger write");
            }

            var backup = DataBackupService.CreateBackup("synthetic-instruments");
            Check(backup.Ok && Directory.GetFiles(Path.Combine(backup.Directory, "instruments"), "*.json").Length == 2,
                "whole-application backup includes both separate project ledgers");
            var meta = Directory.GetFiles(Path.Combine(AppPaths.DataDir, "instruments"), "*.json")
                .Single(file => File.ReadAllText(file).Contains(asset.Id));
            var preDamage = service.Read(first, None).Assets.Single();
            preDamage.Location = "new unbacked location";
            var stale = service.Save(first, preDamage, preDamage.Revision, None);
            File.WriteAllText(meta, "{damaged main");
            var recovered = service.Read(first, None);
            Check(recovered.Recovered && recovered.Assets.Single().Location == "Fixture skid A", "corrupt metadata falls back to checked previous copy");
            Throws<InvalidOperationException>(() => service.Record(first, original, None), "recovered ledger is read-only");
            service.Restore(first, None);
            var replacement = service.Read(first, None).Assets.Single();
            replacement.Location = "new valid replacement";
            replacement = service.Save(first, replacement, replacement.Revision, None);
            Throws<InvalidOperationException>(() => service.Save(first, stale, stale.Revision, None), "revision token rejects a stale edit after restore reuses a revision number");
            Check(replacement.Location == "new valid replacement" && File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(meta),
                "*.damaged-*").Single()) == "{damaged main", "restore preserves damaged file and accepts new writes");
            string message;
            Check(DataBackupService.ScheduleRestore(backup.Directory).Ok && DataBackupService.ApplyPendingRestore(out message)
                && service.Read(first, None).Assets.Single().Location == "Fixture skid A"
                && service.Read(second, None).Assets.Single().Id == other.Id,
                "whole-app restore restores both project histories and preserves identities");

            Console.WriteLine("PASS " + count + " instrument acceptance checks (isolated synthetic fixtures).");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static InstrumentAsset Draft(string tag = "")
    {
        return new InstrumentAsset { Number = "PT-010", Name = "Synthetic pressure transmitter", Kind = "压力变送器",
            Manufacturer = "Fixture", RangeLow = 0, RangeHigh = 10, Unit = "bar", TagId = tag };
    }

    private static string Write(ProjectContext project, string name, string text)
    {
        string path = Path.Combine(project.Root, name);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Date(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException(label);
        count++;
        Console.WriteLine("PASS " + label);
    }

    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, label); return; }
        Check(false, label);
    }
}
