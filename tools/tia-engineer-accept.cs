using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Engineering.Tia;
#if NETFRAMEWORK || TIA_WINFORMS
using System.Drawing;
using System.Drawing.Imaging;
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
            var invalidScl = Path.Combine(temp, "invalid.scl");
            File.WriteAllText(invalidScl, "FUNCTION_BLOCK FB_Review\nVAR_OUTPUT\n Result : Real;\n Spare : Bool;\n Spare : Bool;\nEND_VAR\nBEGIN\n #Result := #Unknown;\nEND_FUNCTION_BLOCK\n");
            var invalidNotes = PlcEngineering.ReadExports(new[] { invalidScl }, CancellationToken.None).Notes;
            Check(invalidNotes.Any(x => x.Contains("重复局部变量 Spare")) && invalidNotes.Any(x => x.Contains("#Unknown")) && invalidNotes.Any(x => x.Contains("#Spare")), "Static SCL detects duplicate, unresolved and unused locals");
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
            Native(temp, evidence, new[] { xml, csv, scl });
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
        using (var form = new MainForm(store))
        {
            form.Shown += async delegate
            {
                try { await ExerciseWorkbench(form, evidence, files); }
                catch (Exception e) { failure = e; }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        if (failure != null) throw new InvalidOperationException("Native workbench failed", failure);
    }
    private static async Task ExerciseWorkbench(MainForm form, string evidence, string[] files)
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
        view.LoadExports(files);
        var until = DateTime.UtcNow.AddSeconds(12);
        while (!mode.Enabled && DateTime.UtcNow < until) await Task.Delay(50);
        Console.WriteLine("TIA view visible=" + view.Visible + " tabs=" + tabs.TabPages.Count + " roots=" + string.Join(" | ", tree.Nodes.Cast<TreeNode>().Select(n => n.Text)));
        Console.WriteLine("TIA text panes: " + string.Join(" | ", Children(view).OfType<RichTextBox>().Where(box => box.TextLength > 0).Select(box => box.AccessibleName + ":" + box.Text.Substring(0, Math.Min(100, box.TextLength)))));
        await Capture(form, evidence, "tia-workbench-1600.png");
        Check(mode.Enabled, "Export job reaches its completion callback before UI assertions");
        Check(view.Visible && tabs.TabPages.Count == 7, "Native workbench displays all seven work tabs");
        Check(tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("PLC Blocks")) && tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("Devices")) && tree.Nodes.Cast<TreeNode>().Any(n => n.Text.StartsWith("PLC Tags")), "Native workbench loads block, device and tag objects");
        var voice = new ToolContext { TiaRead = view.ReadForVoice };
        var call = new Dictionary<string, string> { { "name", "CheckValve" } };
        Check(!ToolRegistry.Instance.Invoke("tia.read", call, voice).Ok, "Voice tool requires explicit project authorization");
        Check(!ToolRegistry.Instance.Invoke("tia.write", call, voice).Ok, "No TIA write tool is exposed to an assistant");
        Children(view).OfType<CheckBox>().Single().Checked = true;
        var lookup = ToolRegistry.Instance.Invoke("tia.read", call, voice);
        Check(lookup.Ok && lookup.Output.Contains("export.xml:") && tabs.SelectedIndex == 1, "Approved voice lookup returns provenance and selects analysis tab");
        form.Size = new Size(1024, 768);
        await Task.Yield();
        await Capture(form, evidence, "tia-workbench-1024.png");
        Check(tabs.Width > 350, "Main work area retains room at narrow width");
    }
    private static async Task Capture(Form form, string directory, string file)
    {
        form.TopMost = true;
        form.Activate(); form.Refresh();
        await Task.Delay(180);
        using (var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height))
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, image.Size);
            image.Save(Path.Combine(directory, file), ImageFormat.Png);
        }
    }
    private static System.Collections.Generic.IEnumerable<Control> Children(Control control)
    {
        foreach (Control child in control.Controls) { yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
#endif
}
