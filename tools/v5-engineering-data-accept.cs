using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using DeskBox.AppServices;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Integration;

public static class EngineeringDataAcceptance
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static int checks;
    private static ProjectContext a, b;
    private static string root;

    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "deskbox-v5-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            AppPaths.DataDir = DeskBox.Store.DataDir = Path.Combine(root, "data");
            var projects = new WorkspaceStore();
            var pa = projects.Add("Shared label", Directory.CreateDirectory(Path.Combine(root, "A")).FullName, "PLC");
            var pb = projects.Add("Shared label", Directory.CreateDirectory(Path.Combine(root, "B")).FullName, "DCS");
            projects.SetCurrent(pa.Id);
            a = projects.Context;
            projects.SetCurrent(pb.Id);
            b = projects.Context;
            TestFhx();
            TestPlc();
            TestTags();
            Console.WriteLine("PASS " + checks + " engineering data checks using isolated offline engineering-format fixtures.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void TestFhx()
    {
        var original = @"/* Sample written for parser acceptance; offline. */
AREA NAME=""Line 1"" {
  MODULE_INSTANCE NAME=""Pump-A"" DESCRIPTION=""Tank {A} \""check\"""" {
    ATTRIBUTE_INSTANCE NAME=""PV"" DATA_TYPE=REAL {
      VALUE { CV=12.5 }
      ENG_UNITS=""kPa""
    }
    ALARM NAME=""High"" LIMIT=45
    {
      TEXT=""Greater than 45""
    }
    TAG NAME=""P101"" SYSTEM_ID=PLC-A DATA_TYPE=REAL ENG_UNITS=kPa ADDRESS=""AI/01"" {}
    VENDOR_EXTENSION Code=1 { VALUE { CV=2 } }
  }
  MODULE_CLASS NAME=""PumpFamily"" {}
  PHASE_CLASS NAME=""Start"" {}
  RECIPE_DEFINITION NAME=""Mix"" {}
}
";
        var file = Write(a, "scope.fhx", original, Encoding.Unicode);
        var same = Write(b, "scope.fhx", original.Replace("Pump-A", "Different"), Encoding.Unicode);
        var document = EngineeringDocuments.Read(a, file, "v1", None);
        Check(document.Source.Encoding == "utf-16" && document.Source.Sha256.Length == 64 && document.Records.Count == 11, "FHX UTF-16 hash, complete hierarchy and object list");
        Check(new[] { "Area", "Module", "Parameter", "Alarm", "Tag", "ModuleClass", "Phase", "Recipe" }
            .All(kind => document.Records.Any(record => record.Kind == kind && record.Recognized)), "recognized FHX grammar objects");
        var parameter = document.Records.Single(item => item.Kind == "Parameter");
        Check(parameter.Parent.Contains("Pump-A") && parameter.Fields["ENG_UNITS"] == "kPa" && parameter.StartLine == 4 && parameter.EndLine == 7,
            "nested parameter carries true source span and source fields");
        Check(document.Records.Any(item => item.Kind == "VENDOR_EXTENSION" && !item.Recognized && item.Fields["Code"] == "1"), "unknown FHX fields and object retained");
        Check(EngineeringDocuments.Excerpt(a, document.Source, parameter.StartLine, 7, None).Contains("ATTRIBUTE_INSTANCE NAME=\"PV\""), "exact source excerpt");
        var reopened = EngineeringDocuments.Read(a, file, "v1-copy", None);
        Check(reopened.Source.Version == "v1-copy" && reopened.Records.Count == document.Records.Count && reopened.Source.Document == "scope.fhx", "cached parse binds current source metadata");
        var foreign = EngineeringDocuments.Read(b, same, "b", None);
        Check(foreign.Source.Sha256 != document.Source.Sha256 && foreign.Records.Any(item => item.Name == "Different"), "same file name isolated by project");
        Throws<UnauthorizedAccessException>(() => EngineeringDocuments.Read(b, file, "b", None), "foreign source rejected");

        var newer = Write(a, "updated.fhx", original.Replace("LIMIT=45", "LIMIT=47").Replace("CV=12.5", "CV=13.5")
            .Replace("  RECIPE_DEFINITION NAME=\"Mix\" {}\r\n", "").Replace("  RECIPE_DEFINITION NAME=\"Mix\" {}\n", "")
            .Replace("PHASE_CLASS NAME=\"Start\" {}", "PHASE_CLASS NAME=\"Start\" {}\n  PHASE_CLASS NAME=\"Stop\" {}"));
        var diff = EngineeringDocuments.Compare(a, file, newer, None);
        Check(diff.Added == 1 && diff.Removed == 1 && diff.Modified == 2 && diff.Unclassified > 0 &&
            diff.Changes.Any(item => item.Field == "LIMIT" && item.Before == "45" && item.After == "47"), "FHX adds, removals and deterministic field-level diffs");
        var report = Path.Combine(a.Root, "fhx-diff.json");
        EngineeringDocuments.ExportDiff(a, diff, report, None);
        Check(File.ReadAllText(report).Contains(diff.Before.Sha256) && File.ReadAllText(report).Contains("L8"), "report carries source hashes and positions");
        Throws<IOException>(() => EngineeringDocuments.ExportDiff(a, diff, report, None), "cannot overwrite an existing report or original");

        var broken = Write(a, "broken.fhx", "MODULE_INSTANCE NAME=\"Broken\" {\n VALUE { CV=45 }\n");
        var failure = EngineeringDocuments.Read(a, broken, "", None);
        Check(failure.Issues.Any(issue => issue.Blocking && issue.Location.StartsWith("L1")), "unbalanced FHX keeps diagnostic line");
        Check(EngineeringDocuments.Compare(a, file, broken, None).Changes.Count == 0, "no difference conclusion from broken FHX");
        var duplicate = Write(a, "duplicate.fhx", "MODULE_INSTANCE NAME=\"D\" {}\nMODULE_INSTANCE NAME=\"D\" {}");
        Check(EngineeringDocuments.Read(a, duplicate, "", None).Issues.Any(issue => issue.Blocking && issue.Message.Contains("重复")), "ambiguous duplicate FHX identity diagnosed");
        try
        {
            var invalid = Path.Combine(a.Root, "bad-encoding.fhx");
            File.WriteAllBytes(invalid, new byte[] { 0xc0, 0xff, 0xe1 });
            Throws<InvalidDataException>(() => EngineeringDocuments.Read(a, invalid, "", None), "invalid encoding produces a real error");
            using (var token = new CancellationTokenSource())
            {
                token.Cancel();
                Throws<OperationCanceledException>(() => EngineeringDocuments.Read(a, file, "", token.Token), "cancelled read never serves cached results");
            }
        }
        finally { }
        var many = new StringBuilder();
        for (int i = 0; i < 12000; i++) many.AppendLine("MODULE_INSTANCE NAME=\"UNIT_" + i + "\" SYSTEM=\"Archive\" { VALUE { CV=12 } }");
        var large = Write(a, "large.fhx", many.ToString());
        Check(EngineeringDocuments.Read(a, large, "bulk", None).Records.Count == 24000, "stream parser processes 24000 nested objects");
    }

    private static void TestPlc()
    {
        var txt = "ORGANIZATION_BLOCK OB_Main\nBEGIN\nEND_ORGANIZATION_BLOCK\nFUNCTION_BLOCK FB_Drive\nBEGIN\nEND_FUNCTION_BLOCK\n" +
            "FUNCTION FC_Diagnostic : Void\nBEGIN\nEND_FUNCTION\nDATA_BLOCK DB_Data\nBEGIN\nEND_DATA_BLOCK\n";
        var path = Write(a, "logic.scl", txt);
        var newer = Write(a, "logic-v2.scl", txt.Replace("FC_Diagnostic : Void\nBEGIN", "FC_Diagnostic : Void\nBEGIN\n#Reading := #Previous;"));
        var result = EngineeringDocuments.Read(a, path, "r1", None);
        Check(new[] { "OB", "FB", "FC", "DB" }.All(kind => result.Records.Any(row => row.Kind == kind && row.StartLine > 0)), "existing SCL reader exposes four block kinds and locations");
        Check(EngineeringDocuments.Compare(a, path, newer, None).Changes.Any(row => row.Key.Contains("FC_Diagnostic") && row.Field == "Fingerprint" &&
            row.BeforeLocation.StartsWith("L") && row.AfterLocation.StartsWith("L")), "SCL program change trace identifies block and file location");
        var diagnostics = Write(a, "compile-export.json", Json.ToJson(new[] { new PlcDiagnostic { Source = "logic.scl", Severity = "Error", Line = 7, Column = 2,
            Message = "Isolated diagnostic fixture" } }));
        Check(EngineeringDocuments.Read(a, diagnostics, "", None).Records.Single().Location.Contains("logic.scl:7"), "compile diagnostics retain supplied source location");
        var receiptFile = Write(a, "result.json", Json.ToJson(new TiaResult { CompileState = "Offline fixture error",
            Diagnostics = new List<PlcDiagnostic> { new PlcDiagnostic { Source = "logic.scl", Severity = "Error", Line = 7, Message = "isolated" } } }));
        var receipt = EngineeringDocuments.Read(a, receiptFile, "", None);
        Check(receipt.Records.Count == 1 && receipt.Issues.Any(issue => issue.Message.Contains("Offline fixture error")),
            "native TIA receipt shape can be reviewed offline without representing it as a fresh compilation");
        var xml = Write(a, "tags.xml", "<Document><SW.Tags.PlcTagTable><AttributeList><Name>Tags</Name></AttributeList><ObjectList>" +
            "<SW.Tags.PlcTag><AttributeList><Name>P101</Name><LogicalAddress>%ID10</LogicalAddress><DataTypeName>Real</DataTypeName>" +
            "</AttributeList></SW.Tags.PlcTag></ObjectList></SW.Tags.PlcTagTable></Document>");
        var plc = EngineeringDocuments.Read(a, xml, "io", None);
        Check(plc.Records.Any(row => row.Kind == "PlcTag" && row.Name == "P101" && row.Fields["Address"] == "%ID10"), "Siemens offline XML variable reuses actual adapter");
        var xxe = Write(a, "bad.xml", "<!DOCTYPE a [<!ENTITY b SYSTEM 'file:///missing'>]><Document>&b;</Document>");
        Throws<System.Xml.XmlException>(() => EngineeringDocuments.Read(a, xxe, "", None), "PLC adapter rejects XML external declarations");
    }

    private static void TestTags()
    {
        var service = new TagCenter();
        var v1 = Write(a, "io.csv", "名称,系统,单位,类型,low,high,说明\nP101,PLC-A,kPa,REAL,0,50,\"first\nmultiline\"\nP101,PLC-B,kPa,REAL,0,50,second\n");
        var v2 = Write(a, "io-v2.csv", "名称,系统,单位,类型,low,high,说明\nP101,PLC-A,bar,REAL,0,75,changed\nP102,PLC-A,kPa,REAL,0,50,new\n");
        var separate = Write(b, "io.csv", "名称,系统,单位,类型\nP101,PLC-A,L/min,REAL\n");
        var mapping = TagCenter.DefaultMapping(EngineeringDocuments.Headers(a, v1, "", "auto", None));
        mapping["Description"] = 6;
        var pending = service.Preview(a, v1, "r1", "", None, mapping);
        Check(pending.Rows.Count == 2 && pending.Rows[1].Value.SourceLocation == "L4" && !pending.Issues.Any(issue => issue.Blocking),
            "actual CSV multiline source rows, explicit field mapping and duplicate names across systems");
        Throws<UnauthorizedAccessException>(() => service.Commit(b, pending, false, None), "pending A import cannot commit to B");
        var first = service.Commit(a, pending, false, None);
        Check(first.Added == 2 && first.TagIds.Distinct().Count() == 2, "one operation adds two stable system-scoped identities");
        var bReport = service.Commit(b, service.Preview(b, separate, "b", "", None), false, None);
        Check(bReport.TagIds[0] != first.TagIds[0] && service.Query(b, "", "", "name", false, 0, 100, None).Items.Single().EngineeringUnit == "L/min",
            "same project label and source file name never share Tag rows");
        var current = service.Query(a, "P101", "PLC-A", "system", false, 0, 1, None).Items.Single();
        var proposal = service.Preview(a, v2, "r2", "", None, mapping);
        Check(proposal.Rows.Any(row => row.State == "conflict") && proposal.Issues.Any(issue => issue.Message.Contains("工程单位")),
            "preview flags mapped unit and range conflict");
        var link = service.Commit(a, proposal, false, None);
        var linked = service.Query(a, "P101", "PLC-A", "name", false, 0, 10, None).Items.Single();
        Check(link.Linked == 1 && link.Added == 1 && linked.TagId == current.TagId && linked.EngineeringUnit == "kPa" && linked.Sources.Count == 2,
            "default import adds source link and keeps existing data");
        var applied = service.Commit(a, service.Preview(a, v2, "r2", "", None, mapping), true, None);
        var updated = service.Query(a, "P101", "PLC-A", "name", false, 0, 10, None).Items.Single();
        Check(applied.Updated == 1 && updated.TagId == current.TagId && updated.EngineeringUnit == "bar" && updated.RangeHigh == 75 &&
            updated.Revision == 3, "explicit confirmation updates mapped fields and preserves stable ID");
        Check(service.CompareRevisions(a, updated.TagId, 1, 3, None).Changes.Any(row => row.Field == "EngineeringUnit" && row.Before == "kPa" && row.After == "bar"),
            "Tag versions support field-level diff");
        var versionDiff = EngineeringDocuments.Compare(a, v1, v2, None, beforeMapping: mapping, afterMapping: mapping);
        Check(versionDiff.Added == 1 && versionDiff.Removed == 1 && versionDiff.Modified == 1 && versionDiff.Changes.Any(row => row.Field == "RangeHigh"),
            "two IO List versions compare across field mapping");

        var xml = Path.Combine(a.Root, "tags.xml");
        var fhx = Path.Combine(a.Root, "scope.fhx");
        service.Commit(a, service.Preview(a, xml, "PLC R1", "PLC-A", None), false, None);
        service.Commit(a, service.Preview(a, fhx, "DCS R1", "DeltaV", None), false, None);
        var joint = service.Query(a, "P101", "PLC-A", "name", false, 0, 10, None).Items.Single();
        Check(joint.TagId == current.TagId && new[] { "xml", "fhx", "csv" }.All(kind => joint.Sources.Any(source => source.SourceType == kind)),
            "PLC, DeltaV and IO List references are linked to the same project Tag");
        Check(EngineeringDocuments.SourceLocation(a, joint.Sources.Last(), None).Contains("TAG NAME"), "cross-module source opens with original hash");

        var workbook = Path.Combine(a.Root, "real-export.xlsx");
        var csv = Path.Combine(a.Root, "real-export.csv");
        service.Export(a, workbook, None);
        service.Export(a, csv, None);
        var readback = service.Preview(a, workbook, "roundtrip", "", None, worksheet: "Tags");
        Check(readback.Rows.Any(row => row.Value.TagName == "P101" && row.Value.EngineeringUnit == "bar") &&
            readback.Rows.All(row => row.Value.SourceLocation.StartsWith("Tags!L")) && File.ReadAllText(csv).Contains("SourceSha256"), "real XLSX and CSV export carry source and worksheet coordinates");
        Throws<IOException>(() => service.Export(a, v1, None), "Tag export preserves source file");

        var duplicate = Write(a, "duplicate.csv", "Tag,system,datatype,low,high\nD,S,real,1,2\nD,S,real,3,2\nE,,GARBAGE,x,2");
        var bad = service.Preview(a, duplicate, "bad", "", None);
        Check(bad.Issues.Count(issue => issue.Blocking) >= 3 && bad.Issues.Any(issue => issue.Message.Contains("自定义")),
            "duplicate, malformed range, missing system and unknown datatype reported");
        Throws<InvalidOperationException>(() => service.Commit(a, bad, true, None), "blocking import rolls back all rows");

        var latest = service.Preview(a, v2, "repeat", "", None, mapping);
        service.Save(a, new EngineeringTag { TagName = "Manual", SystemId = "QA", DataType = "REAL" }, 0, None);
        Throws<InvalidOperationException>(() => service.Commit(a, latest, false, None), "previews detect concurrent ledger writes");
        var changed = Write(a, "pending.csv", "Tag,system,type\nX,S,REAL\n");
        var plan = service.Preview(a, changed, "", "", None);
        File.AppendAllText(changed, "Y,S,REAL\n");
        Throws<IOException>(() => service.Commit(a, plan, false, None), "source mutation after preview blocks commit");
        using (var cancellation = new CancellationTokenSource())
        {
            var ready = service.Preview(a, changed, "", "", None);
            cancellation.Cancel();
            Throws<OperationCanceledException>(() => service.Commit(a, ready, false, cancellation.Token), "cancelled commit makes no project writes");
        }
        string dataPath = Path.Combine(AppPaths.DataDir, "engineering-tags", PlcEngineering.Hash(a.Id) + ".json");
        File.WriteAllText(dataPath, "{\"Version\":1,\"Payload\":\"bad\",\"Sha256\":\"forged\"}");
        Check(service.Query(a, "", "", "name", false, 0, 100, None).Recovered, "checksum failure loads only the backup");
        Throws<InvalidOperationException>(() => service.Save(a, new EngineeringTag { TagName = "Hold", SystemId = "QA" }, 0, None), "backup view blocks writes until explicit restore");
        service.Restore(a, None);
        Check(!new TagCenter().Query(a, "", "", "name", false, 0, 100, None).Recovered &&
            Directory.GetFiles(Path.GetDirectoryName(dataPath), "*.damaged-*").Length == 1, "restore is atomic and preserves damaged bytes");
        Check(service.Query(b, "", "", "name", false, 0, 100, None).Total == 1, "repair cannot change unrelated project data");

        File.WriteAllText(dataPath, "{\"Version\":42}");
        Throws<InvalidDataException>(() => service.Query(a, "", "", "name", false, 0, 100, None), "future store version is protected");
        var badDisk = Path.Combine(root, "file-instead-of-data-path");
        File.WriteAllText(badDisk, "held");
        Throws<IOException>(() => new TagCenter(badDisk).Save(a, new EngineeringTag { TagName = "X", SystemId = "S" }, 0, None),
            "storage I/O error prevents mutation");
        Check(File.ReadAllText(badDisk) == "held", "storage error retains existing bytes");
    }

    private static string Write(ProjectContext project, string name, string body, Encoding encoding = null)
    {
        string file = Path.Combine(project.Root, name);
        File.WriteAllText(file, body, encoding ?? new UTF8Encoding(false));
        return file;
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
