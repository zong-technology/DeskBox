using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Engineering.Tia;
#if NETFRAMEWORK || TIA_WINFORMS
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Threading.Tasks;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.UI;
using DeskBox.UI.Views;
#endif

public static class TiaEngineerAcceptance
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        passed++; Console.WriteLine("PASS " + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch { Check(true, name); return; }
        Check(false, name);
    }
    [STAThread]
    public static void Main(string[] args)
    {
        string evidence = Path.GetFullPath(args[0]); Directory.CreateDirectory(evidence);
        string temp = Path.Combine(Path.GetTempPath(), "DeskBox-TIA-QA-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var xml = Path.Combine(temp, "export.xml");
            File.WriteAllText(xml, "<Document><HW.Device><AttributeList><Name>PLC_1</Name></AttributeList><ObjectList><HW.DeviceItem><AttributeList><Name>CPU_Test</Name><TypeIdentifier>FixtureOnly</TypeIdentifier></AttributeList></HW.DeviceItem></ObjectList></HW.Device><SW.Blocks.FB><AttributeList><Name>CheckValve</Name><ProgrammingLanguage>LAD</ProgrammingLanguage><Interface><Sections><Section Name='Input'><Member Name='Feedback' Datatype='Bool'/></Section></Sections></Interface></AttributeList><ObjectList><SW.Blocks.CompileUnit><AttributeList><NetworkSource><FlgNet><Parts><Access><Symbol><Component Name='Feedback'/></Symbol></Access><Part Name='Contact'/></Parts></FlgNet></NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB></Document>");
            var csv = Path.Combine(temp, "io.csv");
            File.WriteAllText(csv, "name,address,type,device,order,signal\nPressure,%IW0,WORD,CPU_Test,,AI\n\"Duplicate,Bit\",%I1.0,BOOL,CPU_Test,,DI\nSpare,,BOOL,CPU_Test,,DI\nPumpFeedback,%I0.0,REAL,CPU_Test,,DI\n");
            var scl = Path.Combine(temp, "scale.scl");
            File.WriteAllText(scl, PlcEngineering.AnalogScale("FB_AnalogScale"));
            var data = PlcEngineering.ReadExports(new[] { xml, csv, scl }, CancellationToken.None);
            Check(data.Objects.Count == 8, "Read devices, SimaticML block, CSV tags and actual SCL source");
            Check(data.Objects.Single(o => o.Name == "CPU_Test").ParentAssetId == data.Objects.Single(o => o.Name == "PLC_1").AssetId &&
                data.Objects.Single(o => o.Name == "CPU_Test").Slot == "", "SimaticML hardware preserves explicit nesting and leaves missing positions unassigned");
            var ladder = data.Objects.Single(x => x.Name == "CheckValve");
            Check(ladder.Language == "LAD" && ladder.References.Contains("Feedback") && ladder.Content.Contains("Contact") && ladder.Interface.Contains("Bool"), "LAD network elements, interface and references");
            Check(ladder.Source.StartsWith("export.xml:"), "Report retains object provenance");
            var graph = Path.Combine(temp, "network.xml");
            File.WriteAllText(graph, "<Document><SW.Blocks.FB><AttributeList><Name>Caller</Name><ProgrammingLanguage>FBD</ProgrammingLanguage></AttributeList><ObjectList><SW.Blocks.CompileUnit><FlgNet><Parts><Call UId='21'><CallInfo Name='Worker'/></Call></Parts><Wires><Wire UId='23'><IdentCon UId='20'/><NameCon UId='21' Name='EN'/></Wire></Wires></FlgNet></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB><SW.Blocks.FC><AttributeList><Name>Worker</Name></AttributeList></SW.Blocks.FC></Document>");
            var network = PlcEngineering.ReadExports(new[] { graph }, CancellationToken.None);
            var caller = network.Objects.Single(x => x.Name == "Caller");
            var callee = network.Objects.Single(x => x.Name == "Worker");
            Check(caller.Content.Contains("NameCon 21 EN") && caller.References.Contains("Worker"), "FBD reports exported wire endpoints and called block");
            Check(PlcEngineering.Related(network, caller).Contains(callee.Source) && PlcEngineering.Related(network, callee).Contains(caller.Source), "Project cross references include source evidence in both directions");
            Check(PlcEngineering.ReferenceGraph(network, caller).Single().Target == callee &&
                PlcEngineering.ReferenceGraph(network, callee).Single().Relationship == "被调用", "Structured FBD graph identifies both directions of an explicit call");
            var duplicateTarget = new PlcObject { Kind = "FC", Name = "Worker", Controller = "Another PLC" };
            network.Objects.Add(duplicateTarget);
            Check(PlcEngineering.ReferenceGraph(network, caller).Single().Target == null, "Ambiguous call names do not create a link to either target");
            caller.Controller = callee.Controller = "First PLC";
            Check(PlcEngineering.ReferenceGraph(network, caller).Single().Target == callee, "A known controller avoids cross-PLC name matches");
            network.Objects.Remove(duplicateTarget);
            var checks = PlcEngineering.Check(data);
            Check(checks.Any(x => x.Contains("地址重叠")) && checks.Any(x => x.Contains("宽度不一致")) && checks.Any(x => x.Contains("地址待确认")), "IO overlap, width mismatch and unresolved address");
            var ioPlan = PlcEngineering.PlanIo(data, 0, 0);
            Check(ioPlan.Contains("Spare · BOOL · I2.0") && ioPlan.Contains("订货号 待确认") && ioPlan.Contains("DI · 3 点"), "IO recommendations avoid occupied inputs and report channel demand and unknown part numbers");
            var controller = new PlcSnapshot { Objects = new List<PlcObject> {
                new PlcObject { Kind = "PlcTag", Name = "In1", Controller = "CPU-A", Scope = "CPU-A/Tags/One", Address = "%I0.0", DataType = "BOOL" },
                new PlcObject { Kind = "PlcTag", Name = "In2", Controller = "CPU-A", Scope = "CPU-A/Tags/Two", Signal = "DI", DataType = "BOOL" }
            }};
            Check(PlcEngineering.PlanIo(controller, 0, 0).Contains("In2 · BOOL · I0.1"), "Tags in different tables share the address space of their controller");
            Check(data.Objects.Single(x => x.Kind == "FB" && x.Language == "SCL").References.Contains("#Raw"), "SCL variables extracted from source");
            Check(data.Notes.Count == 0, "SCL template boundaries and scoped references are complete");
            var aml = Path.Combine(temp, "hardware.aml");
            const string fixture = @"<CAEXFile xmlns='urn:deskbox-test:caex'>
  <InstanceHierarchy Name='Project'>
    <InternalElement ID='station' Name='Station A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/Device'/>
      <InternalElement ID='di' Name='Digital input'>
        <Attribute Name='PositionNumber'><Value>10</Value></Attribute>
        <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
      </InternalElement>
      <InternalElement ID='cpu' Name='CPU A'>
        <Attribute Name='PositionNumber'><Value>1</Value></Attribute>
        <Attribute Name='TypeIdentifier'><Value>OrderNumber:6ES7 TEST/V2.0</Value></Attribute>
        <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
        <InternalElement ID='iface-a' Name='PROFINET A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/CommunicationInterface'/>
          <InternalElement ID='port-a' Name='Port A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/CommunicationPort'/>
            <ExternalInterface Name='CommunicationPortInterface'/></InternalElement>
          <InternalElement ID='node' Name='Ethernet A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/Node'/>
            <ExternalInterface Name='LogicalEndPoint_Node'/></InternalElement>
          <InternalElement ID='adapter' Name='Pluggable adapter'>
            <Attribute Name='PositionNumber'><Value>5</Value></Attribute>
            <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
          </InternalElement>
        </InternalElement>
      </InternalElement>
      <InternalElement ID='ai' Name='Analog input'>
        <Attribute Name='PositionNumber'><Value>2</Value></Attribute>
        <Attribute Name='OrderNumber'><Value>TEST-ANALOG</Value></Attribute>
        <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
        <InternalElement ID='sub' Name='Input submodule'>
          <Attribute Name='PositionNumber'><Value>1</Value></Attribute>
          <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
        </InternalElement>
      </InternalElement>
      <InternalElement ID='unassigned' Name='Unpositioned module'>
        <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
      </InternalElement>
    </InternalElement>
    <InternalElement ID='station-b' Name='Station B'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/Device'/>
      <InternalElement ID='port-b' Name='Port B'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/CommunicationPort'/>
        <ExternalInterface Name='CommunicationPortInterface'/></InternalElement>
    </InternalElement>
    <InternalElement ID='subnet' Name='Network 1'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/Subnet'/>
      <ExternalInterface Name='LogicalEndPoint_Subnet'/></InternalElement>
    <InternalLink Name='LinkToPort' RefPartnerSideA='port-a:CommunicationPortInterface' RefPartnerSideB='port-b:CommunicationPortInterface'/>
    <InternalLink Name='Member' RefPartnerSideA='node:LogicalEndPoint_Node' RefPartnerSideB='subnet:LogicalEndPoint_Subnet'/>
    <InternalLink Name='MissingPort' RefPartnerSideA='port-a:CommunicationPortInterface' RefPartnerSideB='port-b:Missing'/>
  </InstanceHierarchy>
</CAEXFile>";
            File.WriteAllText(aml, fixture);
            if (args.Length > 1 && args[1] == "--capture-inputs")
            {
                foreach (var file in new[] { xml, csv, scl, graph, aml })
                    File.Copy(file, Path.Combine(evidence, "fixture-" + Path.GetFileName(file)), true);
                return;
            }
            var caX = PlcEngineering.ReadExports(new[] { aml }, CancellationToken.None);
            var cpuAsset = caX.Objects.Single(o => o.Name == "CPU A");
            Check(cpuAsset.OrderNumber == "6ES7 TEST" && cpuAsset.Firmware == "V2.0" && cpuAsset.Slot == "1" &&
                cpuAsset.ParentAssetId == caX.Objects.Single(o => o.Name == "Station A").AssetId, "CAx AML preserves hardware part, firmware, slot and parent evidence");
            Check(caX.Objects.Single(o => o.Name == "Pluggable adapter").ParentAssetId == caX.Objects.Single(o => o.Name == "PROFINET A").AssetId,
                "CAx records a pluggable module under its explicit communications interface");
            Check(caX.TopologyLinks.Count == 2 && caX.TopologyLinks.All(l => l.Source.StartsWith("hardware.aml:")) &&
                caX.TopologyLinks.Any(l => l.Kind == "端口连线") && caX.TopologyLinks.Any(l => l.Kind == "子网成员") &&
                caX.Notes.Any(n => n.Contains("未展示 1 条")), "Only explicitly resolved CAx port links and subnet members are displayed");
            File.WriteAllText(aml, fixture.Replace("RefPartnerSideB='port-b:CommunicationPortInterface'", "RefPartnerSideB='port-b:Missing'"));
            Check(PlcEngineering.Diff(caX, PlcEngineering.ReadExports(new[] { aml }, CancellationToken.None)).Contains("删除连接 · 端口连线"),
                "Project comparison detects a removed CAx connection while all hardware stays the same");
            File.WriteAllText(aml, fixture);
            var invalidScl = Path.Combine(temp, "invalid.scl");
            File.WriteAllText(invalidScl, "FUNCTION_BLOCK FB_Review\nVAR_OUTPUT\n Result : Real;\n Spare : Bool;\n Spare : Bool;\nEND_VAR\nBEGIN\n #Result := #Unknown;\nEND_FUNCTION_BLOCK\n");
            var invalidNotes = PlcEngineering.ReadExports(new[] { invalidScl }, CancellationToken.None).Notes;
            Check(invalidNotes.Any(x => x.Contains("重复局部变量 Spare")) && invalidNotes.Any(x => x.Contains("#Unknown")) && invalidNotes.Any(x => x.Contains("#Spare")), "Static SCL detects duplicate, unresolved and unused locals");
            var located = PlcEngineering.ReviewScl(File.ReadAllText(invalidScl), "editor").Diagnostics;
            Check(located.Any(d => d.Severity == "Error" && d.Line == 5 && d.Column == 2) &&
                located.Any(d => d.Message.Contains("#Unknown") && d.Line == 8 && d.Column == 13), "Static diagnostics point to the duplicate declaration and unresolved reference");
            var flow = PlcEngineering.ReviewScl("FUNCTION_BLOCK Walk\r\nBEGIN\r\n REGION IF ELSE WHILE\r\n" +
                " IF \"END_IF\" THEN\r\n  WHILE TRUE DO\r\n   EXIT;\r\n  END_WHILE;\r\n ELSIF FALSE THEN\r\n  RETURN;\r\n" +
                " ELSE\r\n  CASE 2 OF\r\n   2: RETURN;\r\n  END_CASE;\r\n END_IF;\r\n END_REGION\r\nEND_FUNCTION_BLOCK", "valid-flow.scl");
            Check(flow.Diagnostics.Count == 0 && flow.Objects.Single().ControlFlow.Contains("3 个 · 最大嵌套 2 层"),
                "Control flow counts documented nested statements without reading region titles or quoted identifiers as keywords");
            var brokenFlow = PlcEngineering.ReviewScl("FUNCTION_BLOCK Walk\nBEGIN\n IF TRUE\n ELSE\n ELSE\n END_IF;\n REPEAT\n" +
                "  RETURN;\n END_REPEAT;\n EXIT;\nEND_FUNCTION_BLOCK", "broken-flow.scl").Diagnostics;
            Check(brokenFlow.Any(d => d.Line == 3 && d.Column == 2 && d.Severity == "Error" && d.Message.Contains("THEN")) &&
                brokenFlow.Any(d => d.Line == 5 && d.Message.Contains("ELSE")) && brokenFlow.Any(d => d.Line == 7 && d.Message.Contains("UNTIL")) &&
                brokenFlow.Any(d => d.Line == 10 && d.Message.Contains("循环之外")), "Missing separators, repeat conditions and illegal branches get precise diagnostics");
            var crossed = PlcEngineering.ReviewScl("FUNCTION_BLOCK Walk\nBEGIN\n IF TRUE THEN\n WHILE TRUE DO\n END_IF;\n" +
                " END_WHILE;\n IF FALSE THEN\n END_IF;\nEND_FUNCTION_BLOCK", "crossed.scl").Diagnostics;
            Check(crossed.Count == 3 && crossed.All(d => d.Line <= 6) && crossed.Any(d => d.Line == 4 && d.Message.Contains("未找到 END_WHILE")),
                "Mismatched nesting reports the evidence and recovers before later valid statements");
            var largeFlow = PlcEngineering.ReviewScl("FUNCTION_BLOCK Walk\nBEGIN\n" + string.Concat(Enumerable.Repeat(" IF TRUE THEN\n", 129)) +
                string.Concat(Enumerable.Repeat(" END_IF;\n", 129)) + "END_FUNCTION_BLOCK", "deep-flow.scl");
            Check(largeFlow.Diagnostics.Count == 1 && largeFlow.Diagnostics[0].Severity == "Warning", "Deeply nested input has an explicit static review limit");
            const string brackets = "(\"port[ignore]\" + '[ignore]' (* nested (* [ *) ] *) + [1])";
            var pairs = SclEditing.Brackets(brackets);
            Check(pairs.Count == 4 && pairs[0] == brackets.Length - 1, "SCL brackets skip comments, strings and quoted identifiers");
            Check(PlcEngineering.ReviewScl("FUNCTION_BLOCK Test\nBEGIN\n (\nEND_FUNCTION_BLOCK", "editor").Diagnostics
                .Any(d => d.Severity == "Error" && d.Line == 3 && d.Column == 2), "Unmatched source bracket has a precise diagnostic");
            Check(SclEditing.ReplaceLiteral("Foo . foo", "FOO", "$1") == "$1 . $1", "SCL replacement is literal and case insensitive");
            string lineDiff = SclEditing.Diff("One\r\nOld\r\nThree", "One\nNew\nThree");
            Check(lineDiff.Contains("@@ -2,1 +2,1 @@") && lineDiff.Contains("-Old") && lineDiff.Contains("+New") && !lineDiff.Contains("Three"),
                "SCL preview reports the changed lines and excludes unchanged prefix and suffix");
            var draft = PlcEngineering.ReviewScl("// fixture\n\n" + PlcEngineering.AnalogScale("FB_AnalogScale"), "editor");
            Check(draft.Objects.Single().Source == "editor:3" && draft.Notes.Count == 0, "Editor checks actual line numbers using the source parser");
            Check(PlcEngineering.ScaleTests.Contains("12 → 50") && draft.Objects.Single().Interface.Contains("OutOfRange"), "Scaling template includes interface and expected test results");
            string previous = File.ReadAllText(csv);
            File.WriteAllText(csv, previous.Replace("%IW0", "%IW4"));
            var changed = PlcEngineering.ReadExports(new[] { xml, csv, scl }, CancellationToken.None);
            Check(PlcEngineering.Diff(data, changed).Contains("%IW0 → %IW4"), "Diff detects actual IO address changes");
            Reject(() => PlcEngineering.AnalogScale("FB_1\"\nEND_FUNCTION_BLOCK"), "Block identifier cannot inject source");
            string unsafeXml = Path.Combine(temp, "dtd.xml");
            File.WriteAllText(unsafeXml, "<!DOCTYPE Document SYSTEM 'file:///missing'><Document/>");
            Reject(() => PlcEngineering.ReadExports(new[] { unsafeXml }, CancellationToken.None), "DTD and external XML resolution rejected");
            Reject(() => PlcEngineering.FullLocalPath(@"\\server\share\project.ap20"), "Network engineering paths blocked before opening");
            Reject(() => PlcEngineering.ReadExports(new[] { xml }, new CancellationToken(true)), "Read respects cancellation");
            var source = Path.Combine(temp, "source"); Directory.CreateDirectory(source); Directory.CreateDirectory(Path.Combine(source, "System"));
            var project = Path.Combine(source, "Unit.ap20"); File.WriteAllText(project, "original"); File.WriteAllText(Path.Combine(source, "System", "sample"), "fixture");
            string approvedDraft = PlcEngineering.AnalogScale("FB_Reviewed") + "\n// Reviewed locally\n";
            var prepared = TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", "source-sha", approvedDraft);
            Check(prepared.Block == "FB_Reviewed" && prepared.ApprovedHash == PlcEngineering.Hash(approvedDraft) && prepared.Source == approvedDraft,
                "Approval binds the exact reviewed SCL source");
            Reject(() => TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", "source-sha", approvedDraft + "// 已审核"),
                "Openness external source rejects unsupported non-ASCII before consuming the approval");
            Reject(() => TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", "source-sha", approvedDraft + PlcEngineering.AnalogScale("Other")),
                "Insertion rejects a second block before starting any native operation");
            Reject(() => TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", "source-sha", "VERSION : 0.1\n" + approvedDraft),
                "Insertion rejects source outside the single approved block");
            prepared.Source += "// changed after approval";
            try { TiaOpenness.Run(prepared, CancellationToken.None); Check(false, "Edited approved source must fail"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("批准记录不一致"), "Changed approved source is rejected before TIA detection"); }
            const string fc = "FUNCTION FC_Scale : Real\nVAR_INPUT\n Raw : Real;\nEND_VAR\nBEGIN\n #FC_Scale := #Raw;\nEND_FUNCTION";
            var approvedFc = TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", "source-sha", fc);
            Check(approvedFc.Block == "FC_Scale" && approvedFc.BlockKind == "FC" && approvedFc.ApprovedHash == PlcEngineering.Hash(fc),
                "An FC approval binds source, type and target");
            approvedFc.BlockKind = "FB";
            try { TiaOpenness.Run(approvedFc, CancellationToken.None); Check(false, "Changed approved FC type must fail"); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("类型与批准记录不一致"), "Changed FC type is rejected before native operation"); }
            var backup = Path.Combine(temp, "snapshot"); var copy = Path.Combine(temp, "work");
            var manifest = TiaOpenness.CopyTree(source, backup, CancellationToken.None);
            Check(manifest.SequenceEqual(TiaOpenness.CopyTree(backup, copy, CancellationToken.None)), "Binary copy and snapshot manifest match");
            File.WriteAllText(Path.Combine(copy, "Unit.ap20"), "candidate");
            Check(File.ReadAllText(project) == "original" && File.ReadAllText(Path.Combine(backup, "Unit.ap20")) == "original", "Changes in work copy preserve source and restore snapshot");
            Reject(() => TiaOpenness.CopyTree(source, Path.Combine(source, "nested"), CancellationToken.None), "Nested destination rejected");
            Reject(() => TiaOpenness.CopyTree(source, backup, CancellationToken.None), "An existing destination is never overwritten");
            Reject(() => TiaOpenness.CopyTree(source, Path.Combine(temp, "cancel"), new CancellationToken(true)), "Copy respects cancellation");
            var previousData = DeskBox.Core.AppPaths.DataDir;
            try
            {
                DeskBox.Core.AppPaths.DataDir = temp;
#if NETFRAMEWORK || TIA_WINFORMS
                TestMcp(temp, evidence);
#endif
                string jobDir = Path.Combine(temp, "tia-jobs", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(jobDir);
                string recorded = Path.Combine(jobDir, "snapshot"), work = Path.Combine(jobDir, "work");
                var originalManifest = TiaOpenness.CopyTree(source, recorded, CancellationToken.None);
                TiaOpenness.CopyTree(source, work, CancellationToken.None);
                string modified = Path.Combine(work, "Unit.ap20");
                File.WriteAllText(modified, "approved changes fixture");
                File.WriteAllLines(Path.Combine(jobDir, "source-sha256.txt"), originalManifest);
                var saved = new TiaResult { Copy = modified, SourceHash = PlcEngineering.Hash(string.Join("\n", originalManifest)), Saved = true, Compiled = true };
#if NETFRAMEWORK || TIA_WINFORMS
                var request = new TiaJob { Operation = "scale", ProjectFile = project, Cpu = "Rack/CPU", Block = "Approved", ExpectedSource = saved.SourceHash,
                    ApprovedHash = PlcEngineering.Hash(PlcEngineering.AnalogScale("Approved")) };
                string receiptPath = Path.Combine(jobDir, "result.json"), requestPath = Path.Combine(jobDir, "approved-operation.json");
                File.WriteAllText(receiptPath, Json.ToJson(saved));
                File.WriteAllText(requestPath, Json.ToJson(request));
                var reloaded = TiaOpenness.ReadRecoveryReceipt(receiptPath);
                Check(reloaded.Saved && reloaded.Copy == modified && reloaded.SourceHash == saved.SourceHash, "A saved offline job can be selected from its historical receipt");
                Reject(() => TiaOpenness.ReadRecoveryReceipt(Path.Combine(temp, "result.json")), "Receipts outside the local TIA job root cannot be selected");
                request.ExpectedSource = "wrong approval";
                File.WriteAllText(requestPath, Json.ToJson(request));
                Reject(() => TiaOpenness.ReadRecoveryReceipt(receiptPath), "Recovery receipt must match the approved source hash");
                request.ExpectedSource = saved.SourceHash;
                File.WriteAllText(requestPath, Json.ToJson(request));
                saved.Saved = false;
                File.WriteAllText(receiptPath, Json.ToJson(saved));
                Reject(() => TiaOpenness.ReadRecoveryReceipt(receiptPath), "Unsaved job receipts cannot be used for recovery");
                var failed = TiaOpenness.PrepareSclInsertion(project, "Rack/CPU", saved.SourceHash, approvedDraft);
                File.WriteAllText(requestPath, Json.ToJson(failed));
                saved.Compiled = false;
                saved.WriteStarted = true;
                saved.Error = "Import failure fixture";
                File.WriteAllText(receiptPath, Json.ToJson(saved));
                var recoverable = TiaOpenness.ReadRecoveryReceipt(receiptPath);
                Check(recoverable.WriteStarted && !recoverable.Saved, "A failed write that has a confirmed snapshot can be recovered");
                string checkpoint = Path.Combine(jobDir, "write-started.json");
                File.Move(receiptPath, checkpoint);
                Check(TiaOpenness.ReadRecoveryReceipt(checkpoint).WriteStarted, "Interrupted writes can recover from a durable checkpoint without a final receipt");
                saved = reloaded;
#endif
                string restored = TiaOpenness.RestoreSnapshot(saved, CancellationToken.None);
                Check(Path.GetDirectoryName(restored) != work && File.ReadAllText(modified) == "approved changes fixture" &&
                    TiaOpenness.CopyTree(Path.GetDirectoryName(restored), null, CancellationToken.None).SequenceEqual(originalManifest) &&
                    File.Exists(Path.GetDirectoryName(restored) + ".txt"), "Snapshot recovery produces and logs a hash-verified independent project");
                File.AppendAllText(Path.Combine(recorded, "Unit.ap20"), "tampered");
                Reject(() => TiaOpenness.RestoreSnapshot(saved, CancellationToken.None), "A modified snapshot cannot be recovered under its original approved hash");
                Check(Directory.GetDirectories(jobDir, "restored-*").Length == 1, "A failed verification leaves no new recovery directory");
            }
            finally { DeskBox.Core.AppPaths.DataDir = previousData; }
            var originalBlock = new PlcObject { Name = "Original", Kind = "FC", Scope = "CPU/Blocks", Fingerprint = "before" };
            var newBlock = new PlcObject { Name = "Approved", Kind = "FB", Scope = "CPU/Blocks", Interface = "Raw : Real", Fingerprint = "new" };
            var before = new PlcSnapshot { Objects = new List<PlcObject> { originalBlock } };
            var after = new PlcSnapshot { Objects = new List<PlcObject> { originalBlock, newBlock } };
            TiaOpenness.VerifyNewScaleBlock(before, after, "CPU", "Approved");
            Check(true, "Copy save permits one re-read approved FB");
            after.Objects[0] = new PlcObject { Name = "Original", Kind = "FC", Scope = "CPU/Blocks", Fingerprint = "changed" };
            Reject(() => TiaOpenness.VerifyNewScaleBlock(before, after, "CPU", "Approved"), "Unapproved modification of an existing object blocks save");
            after.Objects[0] = originalBlock;
            Reject(() => TiaOpenness.VerifyNewScaleBlock(before, after, "OtherCPU", "Approved"), "FB in another controller cannot satisfy the approval");
            var newFc = new PlcObject { Name = "FC_Scale", Kind = "FC", Scope = "CPU/Blocks", Interface = "Raw : Real", Fingerprint = "fc" };
            after.Objects[1] = newFc;
            Check(TiaOpenness.VerifyNewBlock(before, after, "CPU", "FC_Scale", "FC") == newFc, "An FC readback must match the approved name, target and type");
            Reject(() => TiaOpenness.VerifyNewBlock(before, after, "CPU", "FC_Scale", "FB"), "FC readback cannot satisfy an FB approval");
            Reject(() => TiaOpenness.Run(new TiaJob { Operation = "download", ProjectFile = project }, CancellationToken.None), "Online write request rejected before native environment detection");
            TestProgrammingRecords(temp);
            var env = TiaEnvironmentInspector.Inspect(); Console.WriteLine(env.Describe());
            if (!env.Installations.Any(x => x.Version == 20 && x.AssemblyPath != null))
                Reject(() => TiaOpenness.Run(new TiaJob { ProjectFile = project }, CancellationToken.None), "Missing V20 prevents all native transactions");
#if NETFRAMEWORK || TIA_WINFORMS
            var jsonDraft = Json.ToJson(new PlcDraft { Kind = "FB", Name = "FB_AnalogScale", Purpose = "Scale", Scl = PlcEngineering.AnalogScale("FB_AnalogScale") });
            Check(TiaPlanning.ParseDraft(jsonDraft).Name == "FB_AnalogScale", "Model JSON accepts one matching SCL draft");
            Reject(() => TiaPlanning.ParseDraft(jsonDraft.Replace("END_FUNCTION_BLOCK", "// END_FUNCTION_BLOCK")), "A commented closing marker cannot validate an incomplete draft");
            Native(temp, evidence, new[] { xml, csv, scl, aml, graph });
#endif
            Console.WriteLine("PASS " + passed + " DeskBox TIA checks. Synthetic inputs; no TIA Portal or PLC connection used.");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
    }
#if NETFRAMEWORK || TIA_WINFORMS
    private static void TestMcp(string root, string evidence)
    {
        var result = TiaOpenness.Run(new TiaJob { Operation = "discover" }, CancellationToken.None);
        var audit = File.ReadAllText(result.McpAudit);
        Check(audit.Contains("tia_discover_projects") && audit.Contains("tools/call") &&
            audit.Contains(result.Error.Length == 0 ? "tool_succeeded" : "tool_failed"), "Real DeskBox processes negotiate MCP, discover the tool and record its outcome");
        Check(result.Error.Length == 0 ? result.OpennessVersion.Length != 0 : result.Snapshot == null && result.OpenProjects.Count == 0,
            "Unavailable Openness returns a real MCP tool error with no engineering data");
        var evidenceFolder = Path.GetDirectoryName(result.McpAudit);
        File.Copy(result.McpAudit, Path.Combine(evidence, "mcp-discovery-audit.jsonl"), true);
        File.Copy(Path.Combine(evidenceFolder, "result.json"), Path.Combine(evidence, "mcp-discovery-result.json"), true);

        var id = Guid.NewGuid().ToString("N");
        var jobRoot = Path.Combine(root, "tia-jobs");
        var job = Path.Combine(jobRoot, id);
        Directory.CreateDirectory(job);
        File.WriteAllText(Path.Combine(job, "approved-operation.json"), Json.ToJson(new TiaJob { Operation = "discover" }));
        var start = new System.Diagnostics.ProcessStartInfo(typeof(TiaOpenness).Assembly.Location, "--tia-mcp " + id + " \"" + jobRoot + "\"")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
        };
        using (var process = System.Diagnostics.Process.Start(start))
        {
            try
            {
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}");
                Check(ReadMcpTestResponse(process).Contains("2025-06-18"), "The stdio server returns an MCP initialize response");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
                string listing = ReadMcpTestResponse(process);
                Check(listing.Contains("tia_discover_projects") && !listing.Contains("tia_compile_offline_copy"), "A discovery job advertises only its read capability");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"tia_compile_offline_copy\",\"arguments\":{\"jobId\":\"" + id + "\"}}}");
                Check(ReadMcpTestResponse(process).Contains("-32602"), "MCP denies switching discovery to compile");
                process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"tia_discover_projects\",\"arguments\":{\"jobId\":\"unknown\"}}}");
                Check(ReadMcpTestResponse(process).Contains("-32602") && !File.Exists(Path.Combine(job, "started.lock")), "Invalid MCP arguments never start an engineering operation");
                process.StandardInput.Close();
                Check(process.WaitForExit(5000) && process.ExitCode == 0, "Closing MCP stdin stops the idle server");
            }
            finally { if (!process.HasExited) process.Kill(); }
        }
    }

    private static string ReadMcpTestResponse(System.Diagnostics.Process process)
    {
        var pending = process.StandardOutput.ReadLineAsync();
        if (!pending.Wait(5000) || pending.Result == null) throw new IOException("MCP test server did not reply.");
        return pending.Result;
    }
#endif
    private static void TestProgrammingRecords(string root)
    {
        var file = Path.Combine(root, "binding-source.csv");
        File.WriteAllText(file, "name,address,type,device\nTempA,%IW0,WORD,PLC_A\nTempB,%IW2,WORD,PLC_B\n");
        var source = new TiaProgrammingSource { Name = "Test export", Origin = "导出资料", Files = new[] { file }, Cpus = new[] { "PLC_A", "PLC_B" },
            Fingerprint = TiaProgrammingSource.ExportFingerprint(new[] { file }, CancellationToken.None) };
        var snapshot = PlcEngineering.ReadExports(new[] { file }, CancellationToken.None);
        Reject(() => TiaProgrammingTarget.Bind(source, snapshot, "", TiaProgrammingScope.Draft, null, false), "Two PLCs require an explicit binding");
        Reject(() => TiaProgrammingTarget.Bind(source, snapshot, "PLC_A", TiaProgrammingScope.Variables, snapshot.Objects.Single(o => o.Name == "TempB"), false), "A variable from a different PLC is rejected");
        var a = TiaProgrammingTarget.Bind(source, snapshot, "PLC_A", TiaProgrammingScope.NewFB, null, true);
        var b = TiaProgrammingTarget.Bind(source, snapshot, "PLC_B", TiaProgrammingScope.NewFB, null, true);
        Check(!a.SameSelection(b) && a.Source.Origin == "测试资料" && !a.CanTryInsert() && !a.CanTryCompile(), "Exact PLC binding and fixture source never grant native writes");
        a.Source.Verify(CancellationToken.None);
        var store = new TiaProgrammingStore(Path.Combine(root, "programming-records"));
        var first = store.Start("Scale the sensor", a, false);
        var second = store.Start("Create offline sample", null, true);
        Check(first.Id != second.Id, "Two programming requests get unique task IDs");
#if NETFRAMEWORK || TIA_WINFORMS
        store.Step(first, TiaProgrammingState.READING_PROJECT, "Fixture version checked", "Generate the draft");
        var scl = PlcEngineering.AnalogScale("FB_Example");
        first.DraftFile = "FB_Example.scl";
        first.DraftHash = PlcEngineering.Hash(scl);
        var artifact = store.Write(first, first.DraftFile, scl);
        store.Step(first, TiaProgrammingState.AWAITING_REVIEW, "Fixture draft saved", "Review", approval: true, outputs: new[] { artifact });
        var restored = new TiaProgrammingStore(Path.Combine(root, "programming-records")).Recent().Single();
        Check(restored.Target.SameSelection(a) && restored.Steps[0].FinishedUtc.Length != 0 && restored.Steps[1].NeedsApproval && store.ReadDraft(restored) == scl,
            "Task can be reopened with matching target, ordered timestamps, approval and editable SCL");
        File.AppendAllText(artifact, "\r\n// changed after recording");
        Reject(() => store.ReadDraft(restored), "A task refuses a tampered draft");
#endif
        File.AppendAllText(file, "More,%IW4,WORD,PLC_A\n");
        Reject(() => a.Source.Verify(CancellationToken.None), "Changing a source file invalidates the binding version");
        File.Delete(file);
        Reject(() => a.Source.Verify(CancellationToken.None), "An absent source blocks task recovery");
    }
#if NETFRAMEWORK || TIA_WINFORMS
    private static void Native(string root, string evidence, string[] files)
    {
        AppPaths.DataDir = root; Store.DataDir = root;
        var store = new Store(); store.Init(); store.Settings.AiFileAccess = true; store.Settings.ReferenceUi = true; store.Settings.TakeoverIcons = false;
        store.Settings.Theme = "cyber";
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Exception failure = null;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        AiEnvironment.Select("local");
        new ProviderStore().Add(new AiProvider { Name = "本机测试模型", BaseUrl = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1", Model = "tia-test", TimeoutMs = 8000, MaxRetries = 0 });
        try
        {
            using (var form = new MainForm(store))
            {
                form.Shown += async delegate
                {
                    try { await ExerciseWorkbench(form, store, evidence, files, listener); }
                    catch (Exception e) { failure = e; }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }
        }
        finally { listener.Stop(); }
        if (failure != null) throw new InvalidOperationException("Native workbench failed", failure);
    }
    private static async Task ExerciseWorkbench(MainForm form, Store store, string evidence, string[] files, TcpListener listener)
    {
        await Task.Yield();
        form.Bounds = new Rectangle(0, 0, 1600, 900);
        var nav = form.Controls.Find("nav-tia", true).FirstOrDefault();
        Check(nav != null && nav.Visible, "TIA has an independent reference navigation button");
        form.SwitchView("tia");
        var view = form.Controls.Find("page-tia", true).OfType<TiaEngineerView>().Single();
        var tree = Children(view).OfType<TreeView>().Single(); var tabs = view.Controls.Find("tia-work-tabs", true).OfType<TabControl>().Single();
        var mode = Children(view).OfType<ComboBox>().First(x => x.AccessibleName == "操作模式");
        var approve = Children(view).OfType<DarkButton>().Single(x => x.Name == "tia-approve-insertion");
        Check(mode.SelectedIndex == 0 && !approve.Enabled, "Default read-only mode and write approval gate");
        Check(view.Controls.Find("tia-state-0", true).Single().Text.Contains("尚无 SCL 草稿") && !tree.Visible,
            "An empty installed workbench names both the missing draft and the missing engineering tree");
        await Capture(form, evidence, "tia-empty.png");
        var discover = view.Controls.Find("tia-discover-projects", true).OfType<DarkButton>().Single();
        await Click(discover);
        var detectionDeadline = DateTime.UtcNow.AddSeconds(12);
        while (!mode.Enabled && DateTime.UtcNow < detectionDeadline) await Task.Delay(50);
        Check(mode.Enabled && view.Controls.Find("tia-connection-status", true).Single().Text.Contains("MCP 已响应"),
            "The workbench discovery command receives and displays an actual MCP response");
        tabs.SelectedIndex = 6;
        var detect = Children(view).OfType<DarkButton>().Single(b => b.Text == "查看能力与环境");
        await Click(detect);
        detectionDeadline = DateTime.UtcNow.AddSeconds(12);
        while (!mode.Enabled && DateTime.UtcNow < detectionDeadline) await Task.Delay(50);
        Check(mode.Enabled && tabs.SelectedIndex == 6 && Children(view).OfType<RichTextBox>()
            .Single(area => area.AccessibleName == "编译和操作日志").Text.Contains("连接状态"), "Environment detection publishes actionable status for the actual Windows host");
        await Capture(form, evidence, "tia-environment.png");
        view.LoadExports(files);
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!mode.Enabled && DateTime.UtcNow < until) await Task.Delay(50);
        Console.WriteLine("TIA view visible=" + view.Visible + " tabs=" + tabs.TabPages.Count + " roots=" + string.Join(" | ", tree.Nodes.Cast<TreeNode>().Select(n => n.Text)));
        Console.WriteLine("TIA text panes: " + string.Join(" | ", Children(view).OfType<RichTextBox>().Where(box => box.TextLength > 0).Select(box => box.AccessibleName + ":" + box.Text.Substring(0, Math.Min(100, box.TextLength)))));
        await Capture(form, evidence, "tia-loaded.png");
        Check(mode.Enabled, "Export job reaches its completion callback before UI assertions");
        Check(view.Visible && tabs.TabPages.Count == 7, "Native workbench displays all seven work tabs");
        Check(tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("PLC Blocks")) && tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("Devices")) && tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("PLC Tags")), "Native workbench loads block, device and tag objects");
        var hardwareList = Children(view).OfType<ListView>().Single(list => list.AccessibleName == "硬件清单");
        var topologyList = Children(view).OfType<ListView>().Single(list => list.AccessibleName == "拓扑连接");
        Check(hardwareList.Items.Cast<ListViewItem>().Any(row => row.Text == "CPU A" && row.SubItems[2].Text == "6ES7 TEST") &&
            topologyList.Items.Count == 2, "Hardware and topology panels expose only source-backed inventory and connections");
        tabs.SelectedIndex = 2;
        var containers = Children(view).OfType<ComboBox>().Single(c => c.AccessibleName == "机架容器");
        var rack = Children(view).Single(c => c.AccessibleName == "机架槽位视图");
        var modules = Children(rack).OfType<Button>().ToArray();
        Check(((PlcObject)containers.SelectedItem).Name == "Station A" && modules.Select(m => ((PlcObject)m.Tag).Name)
            .SequenceEqual(new[] { "CPU A", "Analog input", "Digital input", "Unpositioned module" }) && modules.Last().Text.StartsWith("未记录槽位"),
            "Rack shows numerically ordered direct modules and retains unknown positions without merging nested modules");
        await Click(modules[1]);
        var hardwareDetails = Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "硬件目录及建议");
        Check(hardwareList.SelectedItems.Count == 1 && hardwareList.SelectedItems[0].Text == "Analog input" &&
            hardwareDetails.Text.Contains("TEST-ANALOG") && hardwareDetails.Text.Contains("hardware.aml:"), "Physical rack clicks synchronize the inventory and show original module properties");
        await Capture(form, evidence, "tia-rack-layout.png");
        var lowerLevel = containers.Items.Cast<PlcObject>().Single(o => o.Name == "Analog input");
        containers.SelectedItem = lowerLevel;
        Check(Children(rack).OfType<Button>().Single().AccessibleName.Contains("Input submodule"), "Selecting a nested container reveals its own slot level");
        containers.SelectedItem = containers.Items.Cast<PlcObject>().Single(o => o.Name == "PROFINET A");
        await Click(Children(rack).OfType<Button>().Single());
        Check(hardwareList.SelectedItems[0].Text == "Pluggable adapter" && ((PlcObject)containers.SelectedItem).Name == "PROFINET A",
            "A communications-interface container exposes its adapter without moving it into the surrounding rack");
        var hardwareRoot = tree.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("Devices"));
        var stationBranch = hardwareRoot.Nodes.Cast<TreeNode>().Single(n => n.Tag is List<PlcObject> && n.Text.StartsWith("Station A  "));
        stationBranch.Expand();
        tree.SelectedNode = stationBranch.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("Digital input  "));
        Check(tabs.SelectedIndex == 2 && ((PlcObject)containers.SelectedItem).Name == "Station A" &&
            hardwareList.SelectedItems[0].Text == "Digital input", "Opening a hardware tree node locates its actual rack and module row");
        tabs.SelectedIndex = 3;
        var canvas = view.Controls.Find("tia-topology-canvas", true).Single();
        var portNode = Children(canvas).OfType<DarkButton>().Single(node => node.Text == "Port A");
        var originalWidth = portNode.Width;
        await Click(Children(view).OfType<DarkButton>().Single(button => button.Text == "放大"));
        Check(portNode.Width > originalWidth && !Children(canvas).OfType<DarkButton>().Any(node => node.Text == "MissingPort"),
            "Network zoom resizes only nodes backed by resolved source connections");
        await Click(Children(view).OfType<DarkButton>().Single(button => button.Text == "恢复 100%"));
        topologyList.Items[0].Selected = true;
        Check(Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "网络与设备").Text.Contains("hardware.aml:"),
            "Selecting a completed wire shows the original hardware export evidence");
        var endpointA = Children(view).OfType<DarkButton>().Single(b => b.AccessibleName == "定位拓扑端点 A 的硬件");
        var endpointB = Children(view).OfType<DarkButton>().Single(b => b.AccessibleName == "定位拓扑端点 B 的硬件");
        await Capture(form, evidence, "tia-network-endpoints.png");
        await Click(endpointA);
        Check(tabs.SelectedIndex == 2 && hardwareList.SelectedItems[0].Text == "CPU A" && hardwareDetails.Text.Contains("hardware.aml:") &&
            hardwareDetails.Text.Contains("6ES7 TEST"), "An endpoint click navigates through the recorded interface to its source-backed hardware owner");
        tabs.SelectedIndex = 3;
        Check(topologyList.Items[0].Selected, "Returning to topology keeps the current connection selected");
        topologyList.Items[1].Selected = true;
        Check(endpointA.Enabled && !endpointB.Enabled && endpointB.Text.Contains("Network 1"), "An unowned subnet endpoint has evidence but no hardware shortcut");
        var scopedTags = tree.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("PLC Tags")).Nodes[0];
        Check(scopedTags.Nodes.Count == 1 && scopedTags.Nodes[0].Tag == null, "Scoped tree nodes defer materializing their objects until expanded");
        scopedTags.Expand();
        Check(scopedTags.Nodes.Count == 4 && scopedTags.Nodes[0].Tag is PlcObject, "Tree expansion resolves the source-backed tag objects");
        var query = Children(view).OfType<DarkInput>().Single(input => input.Placeholder.Contains("路径"));
        query.Text = "CheckValve";
        Check(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes[0].Text.Contains("CheckValve"), "Engineering tree search filters by object name");
        query.Text = "";
        var blocks = tree.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("PLC Blocks"));
        tree.SelectedNode = blocks.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("Caller "));
        var references = Children(view).OfType<ListView>().Single(list => list.AccessibleName == "程序关系");
        var callRow = references.Items.Cast<ListViewItem>().Single(row => row.SubItems[1].Text == "Worker");
        Check(callRow.Text == "调用" && callRow.SubItems[3].Text.Contains("network.xml:"),
            "Program analysis exposes source-backed call rows");
        await Capture(form, evidence, "tia-references.png");
        var voice = new ToolContext { Store = store, TiaRead = view.ReadForVoice };
        var call = new Dictionary<string, string> { { "name", "CheckValve" } };
        var denied = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(!denied.Ok && denied.Error.Contains("语音查询授权"), "Voice tool requires explicit project authorization");
        Check(!ToolRegistry.Instance.Invoke("tia.write", call, voice).Ok, "No TIA write tool is exposed to an assistant");
        Children(view).OfType<CheckBox>().Single(x => x.Text.Contains("语音") || x.Text.Contains("查阅已加载")).Checked = true;
        var lookup = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(lookup.Ok && lookup.Output.Contains("export.xml:") && tabs.SelectedIndex == 1, "Approved voice lookup returns provenance and selects analysis tab " + lookup.Error);
        Check(((PlcObject)tree.SelectedNode.Tag).Name == "CheckValve", "Voice tool highlights the exact program block in the tree");
        call["name"] = "Pressure";
        var tagLookup = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        var variableLines = Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "变量与 IO");
        var variableRows = view.Controls.Find("tia-variable-table", true).OfType<ListView>().Single();
        Check(tagLookup.Ok && tabs.SelectedIndex == 4 && variableRows.SelectedItems.Count == 1 && variableRows.SelectedItems[0].Text == "Pressure" &&
            variableLines.Text.Contains("io.csv:"), "Voice lookup highlights a variable row in the actual variable view");
        var variableFilter = view.Controls.Find("tia-variable-filter", true).OfType<DarkInput>().Single();
        variableFilter.Text = "%I1.0";
        Check(variableRows.Items.Count == 1 && variableRows.Items[0].Text == "Duplicate,Bit" && variableRows.Items[0].SubItems[4].Text.StartsWith("io.csv"),
            "Filtering the variable table keeps its matched address and original source");
        variableFilter.Text = "";
        call["name"] = "CPU A";
        var hardwareLookup = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(hardwareLookup.Ok && tabs.SelectedIndex == 2 && hardwareList.SelectedItems.Count == 1 && hardwareList.SelectedItems[0].Text == "CPU A", "Voice lookup locates the exact owning hardware row");
        var sourceNode = blocks.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("FB_AnalogScale "));
        tree.SelectedNode = sourceNode;
        Check(Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "程序分析").Text.Contains("最大嵌套"),
            "SCL objects expose the actual control-flow summary in program analysis");
        await Click(Children(view).OfType<DarkButton>().Single(button => button.Text == "创建 SCL 草稿"));
        var selectedDraft = Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "SCL 草稿");
        Check(tabs.SelectedIndex == 0 && selectedDraft.Text.Contains("FUNCTION_BLOCK") && selectedDraft.Text.Contains("FB_AnalogScale"),
            "A user action opens the complete selected SCL block as a local draft");
        selectedDraft.Clear();
        await ExerciseRequirement(form, store, view, tabs, listener, evidence);
        Check(tabs.Width > 350, "Main work area retains room at narrow width");
        form.Bounds = new Rectangle(0, 0, 1600, 900);
        await Task.Delay(100);
        var logPane = view.Controls.Find("tia-log-pane", true).Single();
        var previousHeight = logPane.Height;
        await DragDivider(view.Controls.Find("tia-log-divider", true).Single(), 0, -30);
        var persisted = Json.Load<PersistedState>(AppPaths.File("state.json"), null);
        Check(logPane.Height > previousHeight && persisted.Settings.TiaLogHeight == store.Settings.TiaLogHeight && store.Settings.TiaLogHeight > new AppSettings().TiaLogHeight,
            "Dragging the native log divider persists its adjusted height");
        var navigation = view.Controls.Find("tia-tree-pane", true).Single();
        var assistant = view.Controls.Find("tia-assistant-pane", true).Single();
        int oldNavigation = navigation.Width, oldAssistant = assistant.Width;
        await DragDivider(view.Controls.Find("tia-tree-divider", true).Single(), 24, 0);
        await DragDivider(view.Controls.Find("tia-assistant-divider", true).Single(), -24, 0);
        persisted = Json.Load<PersistedState>(AppPaths.File("state.json"), null);
        Check(navigation.Width > oldNavigation && assistant.Width > oldAssistant && persisted.Settings.TiaTreeWidth > 260 && persisted.Settings.TiaAssistantWidth > 350,
            "Navigation and assistant dividers move independently and persist their widths");
        await Click(view.Controls.Find("tia-toggle-tree", true).Single());
        await Click(view.Controls.Find("tia-toggle-assistant", true).Single());
        await Click(view.Controls.Find("tia-toggle-output", true).Single());
        persisted = Json.Load<PersistedState>(AppPaths.File("state.json"), null);
        Check(!navigation.Visible && !assistant.Visible && logPane.Height < previousHeight && persisted.Settings.TiaTreeCollapsed
            && persisted.Settings.TiaAssistantCollapsed && persisted.Settings.TiaLogCollapsed, "All three workspace areas collapse and persist independently");
        await Click(view.Controls.Find("tia-reset-layout", true).Single());
        persisted = Json.Load<PersistedState>(AppPaths.File("state.json"), null);
        Check(persisted.Settings.TiaLogHeight == new AppSettings().TiaLogHeight && persisted.Settings.TiaTreeWidth == 260 && persisted.Settings.TiaAssistantWidth == 350 && logPane.Height == previousHeight,
            "Reset restores and persists the default workbench dimensions");
        await Click(Children(view).OfType<DarkButton>().Single(button => button.Text == "操作日志"));
        var workLog = Children(view).OfType<RichTextBox>().Single(box => box.AccessibleName == "工程操作日志");
        Check(workLog.Visible && workLog.Text.Contains("开始 · 检测本机 TIA"), "The output dock shows recorded engineering operations instead of filler content");
        await Capture(form, evidence, "tia-operation-history.png");
        await ExerciseBoundProgramming(form, view, listener, evidence, files);
    }

    private static async Task ExerciseBoundProgramming(MainForm form, TiaEngineerView view, TcpListener listener, string evidence, string[] oldFiles)
    {
        string pair = Path.Combine(AppPaths.DataDir, "fixture-two-plcs.csv");
        File.WriteAllText(pair, "name,address,type,device\nScaleSourceA,%IW0,WORD,PLC_A\nScaleSourceB,%IW2,WORD,PLC_B\n");
        var button = new Func<string, DarkButton>(label => Children(view).OfType<DarkButton>().Single(b => b.Text == label));
        view.LoadExports(new[] { pair });
        await WaitEnabled(button("绑定工程"));
        await Click(button("绑定工程"));
        var wizard = Application.OpenForms.Cast<Form>().Single(f => f.Text.StartsWith("绑定工程 · PLC"));
        var cpuSelection = wizard.Controls.Find("tia-bind-cpu", true).OfType<ComboBox>().Single();
        var confirm = Children(wizard).OfType<Button>().Single(b => b.Text == "确认绑定");
        Check(cpuSelection.Items.Count == 3 && cpuSelection.SelectedIndex == 0 && !confirm.Enabled,
            "Binding wizard requires the operator to choose one of two distinct PLCs");
        cpuSelection.SelectedItem = "PLC_B";
        Check(confirm.Enabled && Children(wizard).OfType<RichTextBox>().Single().Text.Contains(pair), "The wizard shows the verified full source path before confirmation");
        await Capture(wizard, evidence, "tia-v43-binding.png");
        confirm.PerformClick();
        await Task.Delay(140);
        Check(view.Controls.Find("tia-context-details", true).Single().Text.Contains("PLC_B"), "The confirmed target is visible in the workbench");

        var prompt = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "工程需求");
        var editor = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "SCL 草稿");
        var store = new TiaProgrammingStore(AppPaths.File("tia-programming-local"));
        await Click(button("输入工程需求"));
        editor.Clear();
        prompt.Text = "编写一个离线模拟量线性缩放 FB，并在静态检查后保存草稿。";
        using (var received = new ManualResetEventSlim())
        using (var respond = new ManualResetEventSlim(true))
        {
            var request = Task.Run(() => ServePlan(listener, received, respond, "FB_BoundScale"));
            await Click(button("生成 SCL 草稿"));
            await WaitEnabled(button("生成 SCL 草稿"));
            Check(request.IsCompleted, "The bound programming operation completes its actual model exchange");
            string payload = await request;
            Check(payload.Contains("PLC_B") && payload.Contains("ScaleSourceB") && !payload.Contains("ScaleSourceA"), "Only the bound PLC context reaches the explicit model request");
        }
        var task = store.Recent().First();
        Check(task.Target.Cpu == "PLC_B" && task.State == "AWAITING_REVIEW" && editor.Text.Contains("FB_BoundScale") && !editor.ReadOnly,
            "One real bound flow produces editable code and waits for human review");
        Check(view.Controls.Find("tia-composer-target", true).Single().Text.Contains("PLC_B") &&
            view.Controls.Find("tia-assistant-stage", true).Single().Text == "待审查草稿",
            "The submission target and task stage match the actual bound draft awaiting review");
        Check(task.Steps.Select(s => s.State).SequenceEqual(new[] { "WAITING_FOR_PROJECT", "READING_PROJECT", "ANALYZING_CONTEXT", "GENERATING_CODE", "STATIC_CHECK", "AWAITING_REVIEW" }),
            "Task log records the stages that actually ran without claiming native compilation");
        var assistantTab = Children(view).OfType<TabControl>().Single(tc => tc.TabPages.Cast<TabPage>().Any(p => p.Text == "编程任务"));
        assistantTab.SelectedIndex = 1;
        await Capture(form, evidence, "tia-v43-programming.png");
        string output = Path.Combine(evidence, "v43-programming-output");
        Directory.CreateDirectory(output);
        foreach (string item in new[] { "task.json", task.DraftFile, "review.diff", "static-review.txt" })
            File.Copy(Path.Combine(store.Folder(task), item), Path.Combine(output, item), true);

        view.LoadExports(oldFiles);
        await WaitEnabled(button("绑定工程"));
        var invalidated = store.Recent().Single(item => item.Id == task.Id);
        Check(invalidated.State == "BLOCKED" && store.ReadDraft(invalidated).Contains("FB_BoundScale"), "Switching projects invalidates the old approval and retains its SCL file");

        await Click(button("输入工程需求"));
        prompt.Text = "生成量程未确认的模拟量缩放 FB 草稿。";
        using (var received = new ManualResetEventSlim())
        using (var respond = new ManualResetEventSlim(true))
        {
            var request = Task.Run(() => ServePlan(listener, received, respond, planOnly: true));
            await Click(button("生成 SCL 草稿"));
            await WaitEnabled(button("生成 SCL 草稿"));
            await request;
            var blocked = store.Recent().First();
            Check(blocked.State == "BLOCKED" && blocked.DraftFile.Length == 0 && blocked.Next.Contains("生成隔离草稿") && blocked.Steps.Last().Error.Contains("量程"),
                "A planning-only reply ends with a concrete blocker and an isolated-draft action");
        }
        assistantTab.SelectedIndex = 1;
        using (var received = new ManualResetEventSlim())
        using (var respond = new ManualResetEventSlim(true))
        {
            var request = Task.Run(() => ServePlan(listener, received, respond, "FB_IsolatedScale"));
            await Click(button("生成隔离草稿"));
            await WaitEnabled(button("生成 SCL 草稿"));
            var payload = await request;
            var isolated = store.Recent().First();
            Check(payload.Contains("本机隔离草稿") && isolated.Isolated && isolated.Target == null && store.ReadDraft(isolated).Contains("FB_IsolatedScale"),
                "The isolated action completes a new local draft after a planning-only response");
        }
        using (var received = new ManualResetEventSlim())
        using (var respond = new ManualResetEventSlim(true))
        {
            var request = Task.Run(() => ServePlan(listener, received, respond, disconnect: true));
            await Click(button("生成 SCL 草稿"));
            await WaitEnabled(button("生成 SCL 草稿"));
            await request;
            Check(store.Recent().First().State == "FAILED" && store.ReadDraft(invalidated).Contains("FB_BoundScale"),
                "A model disconnect records failure while the previous task artifact remains readable");
        }
        await Click(view.Controls.Find("tia-toggle-assistant", true).Single());
        var stop = view.Controls.Find("tia-cancel-task", true).Single();
        string unsent = prompt.Text, beforeStop = editor.Text;
        using (var received = new ManualResetEventSlim())
        using (var respond = new ManualResetEventSlim())
        {
            var request = Task.Run(() => ServePlan(listener, received, respond, disconnect: true));
            await Click(button("生成 SCL 草稿"));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!received.IsSet && DateTime.UtcNow < deadline) await Task.Delay(30);
            try
            {
                Check(received.IsSet && stop.Enabled && !assistantTab.Visible, "A pending task can be stopped with the assistant collapsed");
                await Click(stop);
            }
            finally { respond.Set(); }
            await request;
            await WaitEnabled(button("生成 SCL 草稿"));
            var cancelled = store.Recent().First();
            Check(cancelled.State == "CANCELLED" && cancelled.DraftFile.Length == 0 && !stop.Enabled &&
                prompt.Text == unsent && editor.Text == beforeStop,
                "Stopping a model request records cancellation and keeps demand and editor contents");
        }
        await Click(view.Controls.Find("tia-toggle-assistant", true).Single());
    }

    private static async Task WaitEnabled(Control control)
    {
        var end = DateTime.UtcNow.AddSeconds(12);
        while (!control.Enabled && DateTime.UtcNow < end) await Task.Delay(40);
        if (!control.Enabled) throw new TimeoutException(control.AccessibleName + " stayed disabled");
    }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private static async Task DragDivider(Control divider, int dx, int dy)
    {
        var position = divider.PointToScreen(new Point(divider.Width / 2, divider.Height / 2));
        Check(WindowFromPoint(position) == divider.Handle, divider.Name + " is reachable by the native pointer");
        Cursor.Position = position;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        try
        {
            await Task.Delay(60);
            Cursor.Position = new Point(position.X + dx, position.Y + dy);
            await Task.Delay(100);
        }
        finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
        await Task.Delay(160);
    }
    private static async Task Click(Control control)
    {
        var point = control.PointToScreen(new Point(control.ClientSize.Width / 2, control.ClientSize.Height / 2));
        var hit = WindowFromPoint(point);
        if (hit != control.Handle) throw new InvalidOperationException("Mouse hit-test failed for " + control.AccessibleName + " at " + point + " hit " + Control.FromChildHandle(hit));
        Cursor.Position = point;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(100);
    }
    private static async Task ClickComposerMenu(TiaEngineerView view, string action)
    {
        Control source = view.Controls.Find("tia-composer-more", true).Single();
        await Click(source);
        var menu = source.ContextMenuStrip;
        Check(menu != null && menu.Visible, "The requirement menu is visible for " + action);
        var choice = menu.Items.Cast<ToolStripItem>().Single(item => item.Text == action);
        var point = menu.PointToScreen(new Point(choice.Bounds.Left + choice.Bounds.Width / 2, choice.Bounds.Top + choice.Bounds.Height / 2));
        if (WindowFromPoint(point) != menu.Handle) throw new InvalidOperationException("Requirement menu item is obscured: " + action);
        Cursor.Position = point;
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(120);
    }
    private static async Task ExerciseRequirement(MainForm form, Store store, TiaEngineerView view, TabControl tabs, TcpListener listener, string evidence)
    {
        var button = new Func<string, DarkButton>(label => Children(view).OfType<DarkButton>().Single(b => b.Text == label));
        var prompt = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "工程需求");
        var draft = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "SCL 草稿");
        var assistantPane = view.Controls.Find("tia-assistant-pane", true).Single();
        Check(prompt.Parent.Name == "tia-composer" && !tabs.Contains(prompt) && !assistantPane.Contains(prompt),
            "The fixed composer is independent of the assistant and center tabs");
        await Click(view.Controls.Find("tia-toggle-assistant", true).Single());
        await Click(prompt);
        Check(!assistantPane.Visible && prompt.Focused && button("停止任务").Visible,
            "Collapsing the assistant keeps task input and cancellation available from the bottom dock");
        await Click(view.Controls.Find("tia-toggle-assistant", true).Single());
        await Click(button("输入工程需求"));
        Check(tabs.SelectedIndex == 0 && prompt.Focused && prompt.TabStop && !prompt.ReadOnly, "Top entry focuses the visible editable native requirement input");
        await Click(prompt);
        SendKeys.SendWait("S7-1200");
        SendKeys.SendWait("+{ENTER}");
        Clipboard.SetText("中文工程需求");
        SendKeys.SendWait("^v");
        Check(prompt.Focused && prompt.Text.Contains("S7-1200") && prompt.Text.Contains("中文工程需求") && prompt.Lines.Length == 2, "Mouse, English typing, Unicode paste and Shift+Enter work in the requirement field");
        SendKeys.SendWait("^a");
        SendKeys.SendWait("^c");
        Check(prompt.SelectionLength == prompt.TextLength && Clipboard.GetText().Contains("中文工程需求"), "Ctrl+A and Ctrl+C select and copy the full multiline requirement");
        var initial = prompt.Text;
        await ClickComposerMenu(view, "保存需求草稿");
        Check(Json.Load(AppPaths.File("tia-requirement-local.json"), "") == initial, "Explicit draft save writes the complete requirement to the isolated profile");
        await ClickComposerMenu(view, "配置 AI 服务商");
        var settings = Application.OpenForms.Cast<Form>().SingleOrDefault(window => window != form && window.Controls.Find("ai-environment", true).Any(c => c.Visible));
        Check(settings != null, "Provider shortcut opens the actual AI settings page");
        var enterSetting = settings.Controls.Find("tia-enter-sends", true).OfType<CheckBox>().Single();
        enterSetting.Checked = false;
        settings.Close();
        await Task.Delay(100);
        form.SwitchView("tia");
        await Click(button("输入工程需求"));
        Check(prompt.Text == initial, "Switching to settings and back preserves the unsent requirement");
        prompt.Select(prompt.TextLength, 0);
        SendKeys.SendWait("{ENTER}");
        Check(prompt.Lines.Length >= 2 && prompt.Text.Length > initial.Length && !listener.Pending() && !store.Settings.TiaEnterSends, "With Enter sending disabled, Enter adds a line without sending");
        using (var requestReceived = new ManualResetEventSlim())
        using (var canReply = new ManualResetEventSlim())
        {
            var request = Task.Run(() => ServePlan(listener, requestReceived, canReply));
            SendKeys.SendWait("Confirm");
            SendKeys.SendWait("^{ENTER}");
            var until = DateTime.UtcNow.AddSeconds(6);
            while (!requestReceived.IsSet && DateTime.UtcNow < until) await Task.Delay(30);
            Check(requestReceived.IsSet && !button("生成 SCL 草稿").Enabled, "Configured Ctrl+Enter sends a real request to the local model fixture");
            Check(view.Controls.Find("tia-cancel-task", true).Single().Enabled &&
                view.Controls.Find("tia-assistant-stage", true).Single().Text == "生成 SCL",
                "An active provider request exposes the actual stage and enables the independent stop control");
            SendKeys.SendWait("Pending");
            Check(prompt.Focused && prompt.Text.Contains("Pending"), "Input remains editable while a request is in progress");
            await Capture(form, evidence, "tia-task-running.png");
            canReply.Set();
            string payload = await request;
            until = DateTime.UtcNow.AddSeconds(8);
            while (!button("生成 SCL 草稿").Enabled && DateTime.UtcNow < until) await Task.Delay(30);
            Check(payload.Contains("Confirm") && payload.Contains("中文工程需求") && !payload.Contains("Pending") &&
                draft.Text.Contains("FB_ReviewedScale") && prompt.Text.Contains("Pending"), "Only submitted text reaches the model, its structured reply opens the SCL editor, and later input is retained");
            var localTask = new TiaProgrammingStore(AppPaths.File("tia-programming-local")).Recent().First();
            Check(localTask.Target == null && localTask.Isolated && localTask.DraftFile == "FB_ReviewedScale.scl", "An unbound task keeps a local deliverable and carries no project write authority");
            var conversation = Children(view).OfType<RichTextBox>().Single(box => box.AccessibleName == "工程对话记录");
            Check(conversation.Text.Contains("Confirm") && conversation.Text.Contains("AI 工程师") && !conversation.Text.Contains("Pending"),
                "Assistant transcript reflects the submitted exchange and excludes the unsent draft");
        }
        using (var received = new ManualResetEventSlim())
        using (var reply = new ManualResetEventSlim(true))
        {
            var response = Task.Run(() => ServePlan(listener, received, reply, "FB_SecondDraft"));
            await ClickComposerMenu(view, "重生成上次需求");
            var until = DateTime.UtcNow.AddSeconds(8);
            while ((!response.IsCompleted || tabs.SelectedIndex != 5) && DateTime.UtcNow < until) await Task.Delay(30);
            Check(response.IsCompleted && tabs.SelectedIndex == 5 && draft.Text.Contains("FB_ReviewedScale"), "A new model draft opens the change preview without replacing existing source");
            await response;
            var lineRows = view.Controls.Find("tia-scl-diff-lines", true).OfType<ListView>().Single();
            await Click(button("下一处"));
            Check(lineRows.Visible && lineRows.Columns.Count == 2 && lineRows.SelectedItems.Count == 1 &&
                lineRows.Items.Cast<ListViewItem>().Any(row => row.SubItems[1].Text.Contains("FB_SecondDraft")), "SCL review keeps one synchronized row per change and supports navigation");
            await Capture(form, evidence, "tia-diff.png");
            await Click(button("应用已预览草稿"));
            Check(tabs.SelectedIndex == 0 && draft.Text.Contains("FB_SecondDraft"), "Applying the preview explicitly replaces the editor draft");
        }
        await Capture(form, evidence, "tia-workbench-1600.png");
        await Click(button("工程语音"));
        var voiceForm = Application.OpenForms.Cast<Form>().Single(window => window.Name == "tia-voice-window");
        Check(voiceForm.Visible && !voiceForm.Modal && view.Visible, "TIA voice opens in an owned modeless window while engineering stays visible");
        await Capture(form, evidence, "tia-voice.png");
        voiceForm.Close();
        var voiceClosedBy = DateTime.UtcNow.AddSeconds(4);
        while (!voiceForm.IsDisposed && DateTime.UtcNow < voiceClosedBy) await Task.Delay(40);
        Check(voiceForm.IsDisposed && view.Visible && prompt.Text.Contains("Pending"), "Closing TIA voice leaves the unsent engineering draft intact");
        string goodSource = draft.Text;
        draft.Text = "FUNCTION_BLOCK Test\nVAR_INPUT\n Known : Bool;\nEND_VAR\nBEGIN\n IF #Unknown THEN\n END_IF;\nEND_FUNCTION_BLOCK";
        await Click(button("静态检查草稿"));
        var list = Children(view).OfType<ListView>().Single(item => item.AccessibleName == "工程诊断列表");
        var severity = Children(view).OfType<ComboBox>().Single(item => item.AccessibleName == "诊断级别");
        Check(tabs.SelectedIndex == 6 && list.Items.Count == 2 &&
            view.Controls.Find("tia-diagnostic-origin", true).Single().Text == "SCL 静态检查",
            "Static source review shows the diagnostic origin with two diagnostic rows");
        severity.SelectedIndex = 1;
        Check(list.Items.Count == 0, "Error filter excludes warnings");
        severity.SelectedIndex = 2;
        var unknown = list.Items.Cast<ListViewItem>().Single(item => item.SubItems[2].Text.Contains("#Unknown"));
        unknown.Selected = true;
        list.Focus();
        await Capture(form, evidence, "tia-diagnostics.png");
        await Click(view.Controls.Find("tia-locate-diagnostic", true).Single());
        Check(tabs.SelectedIndex == 0 && draft.SelectionStart == draft.GetFirstCharIndexFromLine(5) + 4,
            "Locating the selected warning lands on the SCL reference column");
        severity.SelectedIndex = 0;
        string diagnosedSource = draft.Text;
        string correctedSource = diagnosedSource.Replace("#Unknown", "#Known");
        tabs.SelectedIndex = 6;
        using (var received = new ManualResetEventSlim())
        using (var reply = new ManualResetEventSlim(true))
        {
            var repair = Task.Run(() => ServePlan(listener, received, reply, "Test", correctedSource));
            await Click(button("生成修复草稿"));
            var until = DateTime.UtcNow.AddSeconds(10);
            while ((!repair.IsCompleted || tabs.SelectedIndex != 5) && DateTime.UtcNow < until) await Task.Delay(30);
            Check(repair.IsCompleted && tabs.SelectedIndex == 5 && draft.Text == diagnosedSource,
                "Repair sends the diagnosed block and returns a preview without changing source");
            Check((await repair).Contains("#Unknown"), "Repair request carries the exact unresolved symbol");
            await Click(button("应用已预览草稿"));
            Check(draft.Text == correctedSource && tabs.SelectedIndex == 0, "The reviewed repair applies only upon explicit approval");
        }
        draft.Text = goodSource;
        var verification = Children(view).OfType<RichTextBox>().Single(box => box.AccessibleName == "工程验证状态");
        Check(verification.Text.Contains("需重新检查"), "Editing a checked draft marks its previous result as stale immediately");
        await Click(button("高亮"));
        draft.Focus();
        draft.ClearUndo();
        draft.Select(0, 0);
        Clipboard.SetText("// edited through the clipboard\n");
        SendKeys.SendWait("^v");
        Check(draft.Text.StartsWith("// edited through the clipboard"), "The SCL editor pastes from the native clipboard");
        SendKeys.SendWait("^z");
        Check(draft.Text == goodSource, "The SCL editor undoes the actual paste");
        draft.Select(0, 14);
        SendKeys.SendWait("^c");
        Check(Clipboard.GetText() == draft.Text.Substring(0, 14), "SCL selection copies actual source text");
        var pages = new[] { "tia-analysis", "tia-hardware", "tia-networks", "tia-variables", "tia-diff", "tia-diagnostics" };
        for (int i = 0; i < pages.Length; i++)
        {
            tabs.SelectedIndex = i + 1;
            if (i != 4 && i != 5) await Capture(form, evidence, pages[i] + ".png");
        }
        form.Bounds = new Rectangle(0, 0, 1920, 1080);
        await Task.Delay(100);
        await Click(button("输入工程需求"));
        await Click(prompt);
        Console.WriteLine("TIA window " + form.Size + ", observed DPI " + form.DeviceDpi);
        Check(prompt.Focused && form.Width == 1920, "The large workbench exposes the same working input control");
        var workArea = view.Controls.Find("tia-work-area", true).Single();
        Check(workArea.Width > 800 && view.Controls.Find("tia-tree-pane", true).Single().Width >= 240 && view.Controls.Find("tia-assistant-pane", true).Single().Width >= 320
            && view.Controls.Find("tia-log-pane", true).Single().Height >= 180, "The 1920-wide workbench reserves room for the editor and uses the target pane dimensions at 96 DPI");
        await Capture(form, evidence, "tia-workbench-1920.png");
        form.Size = new Size(1366, 768);
        await Task.Delay(120);
        await Click(prompt);
        Check(prompt.Focused && form.Width == 1366, "The 1366-wide workbench still hit-tests the input");
        form.Size = new Size(1024, 768);
        await Task.Delay(140);
        await Click(button("输入工程需求"));
        await Click(prompt);
        Check(prompt.Focused && prompt.Bounds.Height >= 60 && !assistantPane.Visible,
            "Narrow layout keeps the prompt usable and frees code space from the desktop assistant");
        await Capture(form, evidence, "tia-workbench-1024.png");
        draft.Focus();
        await Task.Delay(120);
        Console.WriteLine("Narrow editor " + draft.Size + ", focused=" + draft.Focused);
        Check(draft.Focused && draft.Height >= 100, "The narrow programming page keeps a usable SCL editor above the fixed composer");
        await Capture(form, evidence, "tia-editor-narrow.png");
    }
    private static string ServePlan(TcpListener listener, ManualResetEventSlim requestReceived, ManualResetEventSlim canReply, string name = "FB_ReviewedScale", string source = null, bool planOnly = false, bool disconnect = false)
    {
        using (var client = listener.AcceptTcpClient())
        using (var stream = client.GetStream())
        {
            stream.ReadTimeout = 5000;
            var headers = new StringBuilder();
            while (headers.Length < 16384)
            {
                int next = stream.ReadByte();
                if (next < 0) throw new EndOfStreamException();
                headers.Append((char)next);
                if (headers.Length >= 4 && headers.ToString(headers.Length - 4, 4) == "\r\n\r\n") break;
            }
            if (headers.ToString().IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var acknowledge = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                stream.Write(acknowledge, 0, acknowledge.Length);
            }
            int length = int.Parse(headers.ToString().Split('\n').Single(line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
            var body = new byte[length];
            int offset = 0;
            while (offset < body.Length)
            {
                int read = stream.Read(body, offset, body.Length - offset);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            requestReceived.Set();
            if (!canReply.Wait(TimeSpan.FromSeconds(4))) throw new TimeoutException("Waiting for editor input");
            if (disconnect) return Encoding.UTF8.GetString(body);
            var plan = planOnly ? new PlcDraft { Kind = "PLAN", Purpose = "待确认设备数据。", MissingParameters = new[] { "请确认量程。" } } :
                new PlcDraft { Kind = "FB", Name = name, Purpose = "审查草稿", Scl = source ?? PlcEngineering.AnalogScale(name), Tests = new[] { "复核静态诊断及预期行为" } };
            var reply = Encoding.UTF8.GetBytes(Json.ToJson(new { choices = new[] { new { message = new { content = Json.ToJson(plan) } } } }));
            var header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + reply.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(reply, 0, reply.Length);
            return Encoding.UTF8.GetString(body);
        }
    }
    private static async Task Capture(Form form, string directory, string file)
    {
        form.TopMost = true;
        form.Activate(); form.Refresh();
        await Task.Delay(180);
        RenderV4Ui.CaptureNative(form, Path.Combine(directory, file));
    }
    private static System.Collections.Generic.IEnumerable<Control> Children(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
#endif
}
