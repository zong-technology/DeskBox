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
      <InternalElement ID='cpu' Name='CPU A'>
        <Attribute Name='PositionNumber'><Value>1</Value></Attribute>
        <Attribute Name='TypeIdentifier'><Value>OrderNumber:6ES7 TEST/V2.0</Value></Attribute>
        <SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/DeviceItem'/>
        <InternalElement ID='port-a' Name='Port A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/CommunicationPort'/>
          <ExternalInterface Name='CommunicationPortInterface'/></InternalElement>
        <InternalElement ID='node' Name='Ethernet A'><SupportedRoleClass RefRoleClassPath='AutomationProjectConfigurationRoleClassLib/Node'/>
          <ExternalInterface Name='LogicalEndPoint_Node'/></InternalElement>
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
            var caX = PlcEngineering.ReadExports(new[] { aml }, CancellationToken.None);
            var cpuAsset = caX.Objects.Single(o => o.Name == "CPU A");
            Check(cpuAsset.OrderNumber == "6ES7 TEST" && cpuAsset.Firmware == "V2.0" && cpuAsset.Slot == "1" &&
                cpuAsset.ParentAssetId == caX.Objects.Single(o => o.Name == "Station A").AssetId, "CAx AML preserves hardware part, firmware, slot and parent evidence");
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
            var backup = Path.Combine(temp, "snapshot"); var copy = Path.Combine(temp, "work");
            var manifest = TiaOpenness.CopyTree(source, backup, CancellationToken.None);
            Check(manifest.SequenceEqual(TiaOpenness.CopyTree(backup, copy, CancellationToken.None)), "Binary copy and snapshot manifest match");
            File.WriteAllText(Path.Combine(copy, "Unit.ap20"), "candidate");
            Check(File.ReadAllText(project) == "original" && File.ReadAllText(Path.Combine(backup, "Unit.ap20")) == "original", "Changes in work copy preserve source and restore snapshot");
            Reject(() => TiaOpenness.CopyTree(source, Path.Combine(source, "nested"), CancellationToken.None), "Nested destination rejected");
            Reject(() => TiaOpenness.CopyTree(source, backup, CancellationToken.None), "An existing destination is never overwritten");
            Reject(() => TiaOpenness.CopyTree(source, Path.Combine(temp, "cancel"), new CancellationToken(true)), "Copy respects cancellation");
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
            Reject(() => TiaOpenness.Run(new TiaJob { Operation = "download", ProjectFile = project }, CancellationToken.None), "Online write request rejected before native environment detection");
            var env = TiaEnvironmentInspector.Inspect(); Console.WriteLine(env.Describe());
            if (!env.Installations.Any(x => x.Version == 20 && x.AssemblyPath != null))
                Reject(() => TiaOpenness.Run(new TiaJob { ProjectFile = project }, CancellationToken.None), "Missing V20 prevents all native transactions");
#if NETFRAMEWORK || TIA_WINFORMS
            var jsonDraft = Json.ToJson(new PlcDraft { Kind = "FB", Name = "FB_AnalogScale", Purpose = "Scale", Scl = PlcEngineering.AnalogScale("FB_AnalogScale") });
            Check(TiaPlanning.ParseDraft(jsonDraft).Name == "FB_AnalogScale", "Model JSON accepts one matching SCL draft");
            Reject(() => TiaPlanning.ParseDraft(jsonDraft.Replace("END_FUNCTION_BLOCK", "// END_FUNCTION_BLOCK")), "A commented closing marker cannot validate an incomplete draft");
            Native(temp, evidence, new[] { xml, csv, scl, aml });
#endif
            Console.WriteLine("PASS " + passed + " DeskBox TIA checks. Synthetic inputs; no TIA Portal or PLC connection used.");
        }
        finally { try { Directory.Delete(temp, true); } catch (IOException) { } }
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
        var tree = Children(view).OfType<TreeView>().Single(); var tabs = Children(view).OfType<TabControl>().Single();
        var mode = Children(view).OfType<ComboBox>().First(x => x.AccessibleName == "操作模式");
        var approve = Children(view).OfType<DarkButton>().Single(x => x.Text == "批准新增模板并编译");
        Check(mode.SelectedIndex == 0 && !approve.Enabled, "Default read-only mode and write approval gate");
        await Capture(form, evidence, "tia-empty.png");
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
        tabs.SelectedIndex = 3;
        topologyList.Items[0].Selected = true;
        Check(Children(view).OfType<RichTextBox>().Single(area => area.AccessibleName == "网络与设备").Text.Contains("hardware.aml:"),
            "Selecting a completed wire shows the original hardware export evidence");
        var scopedTags = tree.Nodes.Cast<TreeNode>().Single(n => n.Text.StartsWith("PLC Tags")).Nodes[0];
        Check(scopedTags.Nodes.Count == 1 && scopedTags.Nodes[0].Tag == null, "Scoped tree nodes defer materializing their objects until expanded");
        scopedTags.Expand();
        Check(scopedTags.Nodes.Count == 4 && scopedTags.Nodes[0].Tag is PlcObject, "Tree expansion resolves the source-backed tag objects");
        var query = Children(view).OfType<DarkInput>().Single(input => input.Placeholder.Contains("路径"));
        query.Text = "CheckValve";
        Check(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes[0].Text.Contains("CheckValve"), "Engineering tree search filters by object name");
        query.Text = "";
        var voice = new ToolContext { Store = store, TiaRead = view.ReadForVoice };
        var call = new Dictionary<string, string> { { "name", "CheckValve" } };
        var denied = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(!denied.Ok && denied.Error.Contains("语音查询授权"), "Voice tool requires explicit project authorization");
        Check(!ToolRegistry.Instance.Invoke("tia.write", call, voice).Ok, "No TIA write tool is exposed to an assistant");
        Children(view).OfType<CheckBox>().Single(x => x.Text.Contains("语音") || x.Text.Contains("查阅已加载")).Checked = true;
        var lookup = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(lookup.Ok && lookup.Output.Contains("export.xml:") && tabs.SelectedIndex == 1, "Approved voice lookup returns provenance and selects analysis tab " + lookup.Error);
        await ExerciseRequirement(form, store, view, tabs, listener, evidence);
        Check(tabs.Width > 350, "Main work area retains room at narrow width");
    }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
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
    private static async Task ExerciseRequirement(MainForm form, Store store, TiaEngineerView view, TabControl tabs, TcpListener listener, string evidence)
    {
        var button = new Func<string, DarkButton>(label => Children(view).OfType<DarkButton>().Single(b => b.Text == label));
        var prompt = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "工程需求");
        var draft = Children(view).OfType<RichTextBox>().Single(b => b.AccessibleName == "SCL 草稿");
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
        await Click(button("保存需求草稿"));
        Check(Json.Load(AppPaths.File("tia-requirement-local.json"), "") == initial, "Explicit draft save writes the complete requirement to the isolated profile");
        await Click(button("配置 AI 服务商"));
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
            Check(requestReceived.IsSet && !button("生成工程计划").Enabled, "Configured Ctrl+Enter sends a real request to the local model fixture");
            SendKeys.SendWait("Pending");
            Check(prompt.Focused && prompt.Text.Contains("Pending"), "Input remains editable while a request is in progress");
            await Capture(form, evidence, "tia-task-running.png");
            canReply.Set();
            string payload = await request;
            until = DateTime.UtcNow.AddSeconds(8);
            while (!button("生成工程计划").Enabled && DateTime.UtcNow < until) await Task.Delay(30);
            Check(payload.Contains("Confirm") && payload.Contains("中文工程需求") && !payload.Contains("Pending") &&
                draft.Text.Contains("FB_ReviewedScale") && prompt.Text.Contains("Pending"), "Only submitted text reaches the model, its structured reply opens the SCL editor, and later input is retained");
        }
        using (var received = new ManualResetEventSlim())
        using (var reply = new ManualResetEventSlim(true))
        {
            var response = Task.Run(() => ServePlan(listener, received, reply, "FB_SecondDraft"));
            await Click(button("重生成上次需求"));
            var until = DateTime.UtcNow.AddSeconds(8);
            while ((!response.IsCompleted || tabs.SelectedIndex != 5) && DateTime.UtcNow < until) await Task.Delay(30);
            Check(response.IsCompleted && tabs.SelectedIndex == 5 && draft.Text.Contains("FB_ReviewedScale"), "A new model draft opens the change preview without replacing existing source");
            await response;
            await Capture(form, evidence, "tia-diff.png");
            await Click(button("应用已预览草稿"));
            Check(tabs.SelectedIndex == 0 && draft.Text.Contains("FB_SecondDraft"), "Applying the preview explicitly replaces the editor draft");
        }
        await Capture(form, evidence, "tia-workbench-1600.png");
        string goodSource = draft.Text;
        draft.Text = "FUNCTION_BLOCK Test\nVAR_INPUT\n Known : Bool;\nEND_VAR\nBEGIN\n IF #Unknown THEN\n END_IF;\nEND_FUNCTION_BLOCK";
        await Click(button("静态检查草稿"));
        var list = Children(view).OfType<ListView>().Single(item => item.AccessibleName == "工程诊断列表");
        var severity = Children(view).OfType<ComboBox>().Single(item => item.AccessibleName == "诊断级别");
        Check(tabs.SelectedIndex == 6 && list.Items.Count == 2, "Static source review produces two diagnostic rows");
        severity.SelectedIndex = 1;
        Check(list.Items.Count == 0, "Error filter excludes warnings");
        severity.SelectedIndex = 2;
        var unknown = list.Items.Cast<ListViewItem>().Single(item => item.SubItems[2].Text.Contains("#Unknown"));
        unknown.Selected = true;
        list.Focus();
        await Capture(form, evidence, "tia-diagnostics.png");
        SendKeys.SendWait("{ENTER}");
        Check(tabs.SelectedIndex == 0 && draft.SelectionStart == draft.GetFirstCharIndexFromLine(5) + 4,
            "Activating a warning lands on the SCL reference column");
        severity.SelectedIndex = 0;
        draft.Text = goodSource;
        await Click(button("高亮"));
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
        await Capture(form, evidence, "tia-workbench-1920.png");
        form.Size = new Size(1366, 768);
        await Task.Delay(120);
        await Click(prompt);
        Check(prompt.Focused && form.Width == 1366, "The 1366-wide workbench still hit-tests the input");
        form.Size = new Size(1024, 768);
        await Task.Delay(140);
        await Click(button("输入工程需求"));
        await Click(prompt);
        Check(prompt.Focused && prompt.Bounds.Height >= 60, "Narrow layout still hit-tests the prompt and gives it keyboard focus");
        await Capture(form, evidence, "tia-workbench-1024.png");
        draft.Focus();
        await Task.Delay(120);
        Check(draft.Focused && draft.Height >= 100, "The narrow programming page scrolls to a usable SCL editor");
        await Capture(form, evidence, "tia-editor-narrow.png");
    }
    private static string ServePlan(TcpListener listener, ManualResetEventSlim requestReceived, ManualResetEventSlim canReply, string name = "FB_ReviewedScale")
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
            var plan = new PlcDraft { Kind = "FB", Name = name, Purpose = "审查模拟量缩放", Scl = PlcEngineering.AnalogScale(name), Tests = new[] { "验证上下限、越界和无效量程" } };
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
