using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.AppServices;
using DeskBox.Domain;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;
using DeskBox.Fs;
using DeskBox.Sync;
using DeskBox.UI;
using DeskBox.UI.Views;

public static class V5ContextAcceptance
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int passed;
    private static string root;
    private static string aFile, bFile, dcsFile;
    private static Store store;
    private static WorkspaceStore projects;
    private static EngineerWorkspace a, b;
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr key, IntPtr data);

    [STAThread]
    public static int Main(string[] args)
    {
        root = Path.Combine(Path.GetTempPath(), "DeskBoxV5-Accept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SetUp();
            CheckCore();
            if (args.Length > 0) CheckUi(Path.GetFullPath(args[0]), args.Length > 1 ? uint.Parse(args[1]) : 96);
            Console.WriteLine("V5 context: PASS " + passed + " / FAIL 0");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            Console.WriteLine("V5 context: PASS " + passed + " / FAIL 1");
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void SetUp()
    {
        Store.DataDir = AppPaths.DataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(AppPaths.DataDir);
        var first = Path.Combine(root, "Export A");
        var second = Path.Combine(first, "Separate project B");
        Directory.CreateDirectory(second);
        aFile = Path.Combine(first, "A_Review.scl");
        bFile = Path.Combine(second, "B_Review.scl");
        dcsFile = Path.Combine(second, "DCS_Export.fhx");
        File.WriteAllText(aFile, "FUNCTION_BLOCK A_Review\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n");
        File.WriteAllText(bFile, "FUNCTION_BLOCK B_Review\r\nBEGIN\r\nEND_FUNCTION_BLOCK\r\n");
        File.WriteAllText(dcsFile, "/* Offline FHX format acceptance fixture */\r\nMODULE_INSTANCE NAME=\"B_Review\"\r\n{\r\n  TAG NAME=\"Flow.PV\" SYSTEM_ID=DeltaV DATA_TYPE=REAL {}\r\n}\r\n");
        store = new Store();
        store.Init();
        store.Settings.ReferenceUi = true;
        store.Settings.TakeoverIcons = false;
        store.Settings.DesktopDir = first;
        store.Settings.LocalSyncEnabled = false;
        projects = new WorkspaceStore();
        a = projects.Add("Name collision", first, "PLC");
        b = projects.Add("Name collision", second, "DCS");
        projects.SetCurrent(a.Id);
        string due = DateTime.Today.ToString("yyyy-MM-dd");
        store.Tasks.Add(new TaskItem { Id = "task-a", Project = a.Id, Title = "Review PLC A", Due = due, Priority = "high" });
        store.Tasks.Add(new TaskItem { Id = "task-b", Project = b.Id, Title = "Review DCS B", Due = due });
        store.Tasks.Add(new TaskItem { Id = "legacy", Project = "Name collision", Title = "Legacy name task", Due = due });
        store.Save();
        var index = FileIndex.Instance;
        index.Configure(new List<string> { first }, new List<string>());
        index.Rebuild();
        WaitFor(() => !index.IsIndexing, "file index", false);
        Check(index.Count >= 3, "real fixture files indexed");
    }

    private static void CheckCore()
    {
        var first = projects.Context;
        Check(first.CanAccessPath(aFile) && !first.CanAccessPath(bFile), "nested workspace stays isolated");
        Check(!first.IncludesPath(a.Root + "-sibling" + Path.DirectorySeparatorChar + "file.txt"), "path prefix is component bounded");
        Check(!first.IncludesTask(store.Tasks.Single(task => task.Id == "legacy")), "ambiguous historical task is excluded");
        projects.Update(b.Id, "DCS engineering B", "DCS", "B customer", "DCS", "");
        first = projects.Context;
        Check(first.IncludesTask(store.Tasks.Single(task => task.Id == "legacy")), "unique historical task still resolves");
        ExpectFailure(() => projects.SaveContext(a.Id, "FAT", bFile, "", "", "1", "", false), "cannot configure foreign PLC file");
        projects.SaveContext(a.Id, "FAT", aFile, "", a.Root, "R03", "task-a", false);
        projects.SaveContext(b.Id, "OQ", bFile, dcsFile, b.Root, "R08", "task-b", false);
        first = projects.Context;
        Check(first.PlcPath == aFile && first.Version == "R03" && !first.AllowExternalAi, "version and external AI policy persisted");
        Check(!first.PermitsModel(new AiProvider { BaseUrl = "https://example.invalid/v1" }) &&
            first.PermitsModel(new AiProvider { BaseUrl = "http://127.0.0.1:8080/v1" }), "project model rule defaults to numeric loopback");
        var validation = ValidationProgress.Create();
        validation[0].Percent = 100;
        projects.SaveValidation(a.Id, validation, "", "", "Evidence for A", a.Root);
        validation[0].Percent = 10;
        projects.SaveValidation(b.Id, validation, "", "", "Evidence for B", b.Root);

        using (var sync = new LocalSyncCoordinator(store, projects))
        {
            sync.SaveIncident(new IncidentDto { Id = "incident-a", ProjectId = a.Id, Description = "Issue A" }, 0);
            sync.SaveIncident(new IncidentDto { Id = "incident-b", ProjectId = b.Id, Description = "Issue B" }, 0);
            var registry = ToolRegistry.Instance;
            var context = new ToolContext { Store = store, Projects = projects, Project = first, Sync = sync,
                AllowedRoots = new List<string> { a.Root }, FileAccessEnabled = () => true, Confirm = detail => true };
            var tasks = registry.Invoke("task.list", new Dictionary<string, string> { { "project_id", a.Id } }, context);
            Check(tasks.Ok && tasks.Output.Contains("task-a") && tasks.Output.Contains("legacy") && !tasks.Output.Contains("task-b"), "AI task results follow project ID");
            var write = registry.Invoke("task.update", new Dictionary<string, string> { { "id", "task-b" },
                { "title", "Foreign change" }, { "expected", ApplicationTool.TaskStamp(store.Tasks.Single(item => item.Id == "task-b")) } }, context);
            Check(!write.Ok && store.Tasks.Any(item => item.Title == "Review DCS B"), "AI cannot change a different project's task");
            Check(!registry.Invoke("incident.list", null, context).Output.Contains("Issue B"), "AI incident results stay in scope");
            Check(!registry.Invoke("project.list", null, context).Output.Contains(b.Id), "AI project listing stays in scope");
            Check(!registry.Invoke("workflow.run", null, context).Ok, "project AI rejects unscoped workflow execution");
            var hit = registry.Invoke("fs.search", new Dictionary<string, string> { { "root", a.Root }, { "keyword", "Review" } }, context);
            Check(hit.Ok && hit.Output.Contains("A_Review.scl") && !hit.Output.Contains("B_Review.scl"), "AI file search excludes the nested project");
            Check(!registry.Invoke("fs.read", new Dictionary<string, string> { { "path", bFile } }, context).Ok, "AI file read rejects an outside attachment");
        }

        var providers = new ProviderStore();
        var chat = new ChatService(store, providers);
        var globalId = chat.Current.Id;
        chat.SetProjectContext(first);
        Check(chat.Current.Id != globalId && chat.Current.ProjectId == a.Id, "existing unbound session remains separate");
        Check(chat.AttachPath(aFile) && !chat.AttachPath(bFile), "attachment authorizations checked");
        chat.Send("查找：Review");
        WaitFor(() => !chat.Busy, "AI local fallback", false);
        string answer = chat.Current.Messages.Last().Content;
        Check(answer.Contains("本地文件名检索") && answer.Contains(aFile) && !answer.Contains(bFile), "actual local search fallback and source path");
        string aSession = chat.Current.Id;
        var oldReader = Json.Load(AppPaths.File("chats.json"), new List<ChatSession>());
        Check(oldReader.Any(session => session.Id == globalId) && oldReader.All(session => string.IsNullOrEmpty(session.ProjectId)),
            "older release history file contains only unbound conversations");
        var scopedOnDisk = Json.Load(AppPaths.File("chats-projects.json"), new List<ChatSession>());
        Check(scopedOnDisk.Any(session => session.Id == aSession && session.ProjectId == a.Id),
            "project conversation is isolated on disk");
        projects.SetCurrent(b.Id);
        chat.SetProjectContext(projects.Context);
        Check(chat.AllowedFiles.Count == 0 && chat.Sessions.All(session => session.ProjectId == b.Id), "switch clears temporary grants and shows only B sessions");
        chat.SwitchSession(aSession);
        Check(chat.Current.ProjectId == b.Id, "cannot force a different project's session ID");
        chat.Send("Review");
        WaitFor(() => !chat.Busy, "B local fallback", false);
        Check(chat.Current.Messages.Last().Content.Contains(bFile) && !chat.Current.Messages.Last().Content.Contains(aFile), "B search has its own scope");
        var restored = new WorkspaceStore();
        chat = new ChatService(store, providers);
        chat.SetProjectContext(restored.Context);
        Check(restored.Current.Id == b.Id && restored.Current.ValidationSteps[0].Percent == 10 && chat.Current.ProjectId == b.Id, "project, validation and session survive reload");
        projects.SetCurrent(a.Id);
        chat.SetProjectContext(projects.Context);
        Check(chat.Current.Id == aSession && chat.Current.Messages.Last().Content.Contains(aFile), "A history restored without B messages");
        projects.SetCurrent("");
        Check(new WorkspaceStore().Current == null, "explicit global mode survives reload");
        projects.SetCurrent(a.Id);

        var statePath = AppPaths.File("engineering-current.json");
        var shadow = statePath + ".fixture";
        File.Move(statePath, shadow);
        Directory.CreateDirectory(statePath);
        try
        {
            ExpectFailure(() => projects.SetCurrent(b.Id), "failed write reports actual storage error");
            Check(projects.Current.Id == a.Id, "failed write preserves in-memory selection");
        }
        finally { Directory.Delete(statePath); File.Move(shadow, statePath); }
        projects.SetCurrent(b.Id);
        File.WriteAllText(statePath, "{corrupt");
        projects = new WorkspaceStore();
        Check(projects.Current.Id == a.Id && Directory.GetFiles(AppPaths.DataDir, "engineering-current.json.bad-*").Length == 1,
            "corrupt selection recovered from previous commit and input preserved");
        projects.SetCurrent(a.Id);

        new TiaMcpPolicy { Enabled = false, OfflineEngineeringEnabled = false }.Save();
        ExpectFailure(() => TiaOpenness.Run(new TiaJob { Operation = "discover", ProjectId = a.Id }, CancellationToken.None, projects.Context), "MCP disable enforced before starting child");
        new TiaMcpPolicy { Enabled = true, OfflineEngineeringEnabled = false }.Save();
        ExpectFailure(() => TiaOpenness.Run(new TiaJob { Operation = "read-open", ProjectFile = bFile, ProjectId = a.Id, PortalProcessId = 123 }, CancellationToken.None, projects.Context), "foreign engineering file rejected by TIA job boundary");
        Check(!TiaMcpPolicy.Load().OfflineEngineeringEnabled, "MCP capability policy reloads");
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            var foreign = Path.Combine(b.Root, "deliberately-not-a-TIA-project.ap20");
            ExpectFailure(() => TiaOpenness.Run(new TiaJob { Operation = "read-open", ProjectFile = foreign,
                ProjectId = a.Id, PortalProcessId = 1 }, CancellationToken.None, projects.Context), "project scope blocks foreign native jobs before Openness");
        }
    }

    private static void CheckUi(string output, uint expectedDpi)
    {
        Directory.CreateDirectory(output);
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = new MainForm(store))
        {
            form.TopMost = true;
            form.Show();
            form.Activate();
            Pump();
            uint dpi = GetDpiForWindow(form.Handle);
            Check(dpi == expectedDpi, "actual Windows DPI " + dpi + " (requested " + expectedDpi + ")");
            var workspace = form.Controls.OfType<ReferenceWorkspace>().Single();
            var picker = (DarkSelect)Find(workspace, "active-project");
            var scopes = (WorkspaceStore)Field(form, "_workspaces");
            form.SwitchView("tasks");
            var taskView = Find(workspace, "page-tasks").Controls.OfType<TasksView>().Single();
            var tasks = (DarkList)Field(taskView, "_list");
            Check(tasks.Rows.Any(row => (string)row.Tag == "task-a") && tasks.Rows.All(row => (string)row.Tag != "task-b"), "task page initially shows A");
            picker.SelectedIndex = picker.Inner.FindStringExact("DCS engineering B");
            Check(scopes.Current.Id == b.Id && tasks.Rows.Count == 1 && (string)tasks.Rows[0].Tag == "task-b", "header selector updates the visible task page");
            form.SwitchView("incidents");
            var issues = Descendants(Find(workspace, "page-incidents")).OfType<DarkList>().Single(list => list.Rows.Any(row => row.Tag is SyncRecord));
            Check(issues.Rows.Count == 1 && ((SyncRecord)issues.Rows[0].Tag).Id == "incident-b", "incident page follows selected project");
            form.SwitchView("search");
            var search = Descendants(Find(workspace, "page-search")).OfType<SearchView>().Single();
            search.SetProjectScope(a.Root, "foreign folder");
            Check(search.FolderScope == b.Root && ((DarkList)Field(search, "_list")).Rows.All(row => ((FileEntry)row.Tag).Path != aFile), "search cannot keep scope from A");
            form.SwitchView("dcs");
            var docs = (DarkList)Find(workspace, "dcs-project-files");
            Check(docs.RowCount == 1 && (string)docs.Rows[0].Tag == dcsFile, "DCS list uses configured real source");
            docs.SelectIndex(0);
            Press(docs, Keys.Enter);
            var preview = (RichTextBox)Find(workspace, "dcs-export-preview");
            WaitFor(() => preview.Text.Contains("MODULE_INSTANCE") || preview.Text.Contains("读取失败"), "DCS text preview", true);
            Check(preview.Text.Contains("MODULE_INSTANCE"), "actual FHX text visible: " + (preview.Text.Contains("MODULE_INSTANCE") ? "loaded" : preview.Text));
            CaptureWindow(form, Path.Combine(output, "dcs-preview-dpi" + dpi + ".png"));
            UseDialog(Find(workspace, "dcs-structured-analysis"), "engineering-data-dialog", panel =>
            {
                var list = (ListView)Find(panel, "engineering-document-list");
                WaitFor(() => list.Items.Count == 2, "DCS structured parser visible", true);
                Check(list.Items.Cast<ListViewItem>().Any(item => item.SubItems[1].Text == "B_Review") &&
                    list.Items.Cast<ListViewItem>().Any(item => item.SubItems[1].Text == "Flow.PV"), "DCS hierarchy and real tag render");
                var import = Descendants(panel).OfType<Button>().Single(button => button.Text == "映射并预览 Tag…");
                import.PerformClick();
                WaitFor(() => list.Items.Count == 1 && list.Items[0].SubItems[1].Text == "Flow.PV", "DCS Tag preview visible", true);
                Check(Descendants(panel).OfType<Button>().Single(button => button.Text == "导入／补充来源").Enabled, "DCS can preview an actual Tag import");
                CaptureWindow(panel, Path.Combine(output, "dcs-structured-tag-preview-dpi" + dpi + ".png"));
            });
            form.SwitchView("tia");
            var tia = (TiaEngineerView)Find(workspace, "page-tia");
            tia.LoadExports(new[] { bFile });
            WaitFor(() => Field(tia, "snapshot") != null, "offline TIA export", true);
            picker.SelectedIndex = picker.Inner.FindStringExact("Name collision");
            var newTia = (TiaEngineerView)Find(workspace, "page-tia");
            Check(tia.IsDisposed && newTia != tia && Field(newTia, "snapshot") == null, "PLC switch discards previous workbench");
            newTia.LoadExports(new[] { aFile });
            WaitFor(() => Field(newTia, "snapshot") != null, "A export after scope switch", true);
            ((TabControl)Field(newTia, "tabs")).SelectedIndex = 4;
            Pump();
            var plcData = Descendants(newTia).OfType<DarkButton>().Single(button => button.Text == "统一 Tag 与比较");
            UseDialog(plcData, "engineering-data-dialog", panel =>
            {
                var list = (ListView)Find(panel, "engineering-document-list");
                WaitFor(() => list.Items.Count == 1, "loaded PLC file reaches engineering-data entry", true);
                Check(list.Items[0].SubItems[1].Text == "A_Review", "PLC structured view links current export and preserves block name");
                CaptureWindow(panel, Path.Combine(output, "plc-export-source-dpi" + dpi + ".png"));
            });
            new TagCenter().Save(scopes.Context, new EngineeringTag
                { TagName = "A-MANUAL", SystemId = "PLC-A", DataType = "Bool" }, 0, CancellationToken.None);
            form.SwitchView("project-context");
            UseDialog(Find(workspace, "project-tag-center"), "engineering-data-dialog", panel =>
            {
                var list = (ListView)Find(panel, "engineering-tags-list");
                WaitFor(() => list.Items.Count == 1, "project Tag ledger loads", true);
                Check(list.Items[0].SubItems[1].Text == "A-MANUAL", "project route queries actual project Tag data");
                CaptureWindow(panel, Path.Combine(output, "project-tags-dpi" + dpi + ".png"));
            });
            var validation = new ValidationService();
            validation.Save(scopes.Context, new ValidationRequirement { Number = "URS-UI", Stage = "URS",
                Description = "Synthetic validation input for native UI acceptance." }, 0, CancellationToken.None);
            form.SwitchView("validation");
            UseDialog(Find(workspace, "validation-workbench-open"), "validation-workbench", panel =>
            {
                var table = (DataGridView)Find(panel, "validation-records");
                var sectionPicker = (ComboBox)Find(panel, "validation-section");
                WaitFor(() => sectionPicker.Enabled && table.Rows.Count >= 14, "validation overview uses persisted data", true);
                CaptureWindow(panel, Path.Combine(output, "validation-overview-dpi" + dpi + ".png"));
                sectionPicker.SelectedIndex = sectionPicker.FindStringExact("需求管理");
                WaitFor(() => table.Rows.Count == 1, "validation requirements are readable", true);
                Check(table.Rows[0].Cells[0].Value.ToString() == "URS-UI", "validation workbench opens current project's real ledger");
                sectionPicker.SelectedIndex = sectionPicker.FindStringExact("RTM");
                WaitFor(() => sectionPicker.Enabled && table.Rows.Count == 1, "validation RTM computes persisted uncovered requirement", true);
                Check(table.Rows[0].Cells[0].Value.ToString() == "URS-UI", "RTM surfaces uncovered requirement through native route");
                CaptureWindow(panel, Path.Combine(output, "validation-rtm-dpi" + dpi + ".png"));
            });
            var instruments = new InstrumentService();
            var asset = instruments.Save(scopes.Context, new InstrumentAsset { Number = "PT-UI", Name = "Synthetic pressure transmitter",
                Kind = "压力变送器" }, 0, CancellationToken.None);
            var date = DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            instruments.Record(scopes.Context, new InstrumentActivity { AssetId = asset.Id, Kind = "calibration", Date = date, DueDate = date,
                Notes = "Synthetic native UI record." }, CancellationToken.None);
            form.SwitchView("incidents");
            UseDialog(Find(workspace, "instrument-workbench-open"), "instrument-workbench", panel =>
            {
                var list = (DataGridView)Find(panel, "instrument-assets");
                var records = (DataGridView)Find(panel, "instrument-history");
                WaitFor(() => list.Rows.Count == 1, "instrument ledger loads from field operations", true);
                list.CurrentCell = list.Rows[0].Cells[0];
                Check(list.Rows[0].Cells[0].Value.ToString() == "PT-UI" && list.Rows[0].Cells[5].Value.ToString() == "今日到期",
                    "instrument ledger shows the recorded calibration due date");
                WaitFor(() => records.Rows.Count == 1, "instrument calibration history is visible", true);
                CaptureWindow(panel, Path.Combine(output, "instrument-ledger-dpi" + dpi + ".png"));
            });
            var notices = (NotificationStore)Field(form, "_notifications");
            scopes.SetCurrent(b.Id);
            Check(!notices.Items.Any(item => item.Kind == "calibration"), "calibration notices are scoped on project change");
            scopes.SetCurrent(a.Id);
            WaitFor(() => notices.Items.Any(item => item.Kind == "calibration"), "automatic calibration notice for active project", true);
            form.SwitchView("home");
            var entry = Find(workspace, "dashboard-notifications-open");
            Check(entry.Text.StartsWith("通知 ", StringComparison.Ordinal), "dashboard displays unread notification count");
            UseDialog(entry, "notifications-dialog", panel =>
            {
                var list = (DarkList)Find(panel, "notification-list");
                list.SelectIndex(list.Rows.FindIndex(row => ((AppNotification)row.Tag).Kind == "calibration"));
                var open = Find(panel, "notification-open-calibration");
                Check(open.Enabled && open.Focus(), "calibration action is reachable by keyboard");
                CaptureWindow(panel, Path.Combine(output, "calibration-notification-dpi" + dpi + ".png"));
                UseDialog(open, "instrument-workbench", ledger =>
                {
                    Check(((CheckBox)Find(ledger, "instrument-due-only")).Checked, "notification opens the due-only ledger");
                    var table = (DataGridView)Find(ledger, "instrument-assets");
                    WaitFor(() => table.Rows.Count == 1, "instrument notice opens real ledger rows", true);
                    Check(table.Rows[0].Cells[0].Value.ToString() == "PT-UI", "notice shows only the current project's due asset");
                });
            });
            form.SwitchView("ai");
            var chat = (ChatService)Field(form, "_chat");
            Check(chat.Current.ProjectId == a.Id, "AI page and header share project");
            chat.Send("工具 {\"name\":\"eng.scale_4_20\",\"args\":{\"mA\":12,\"low\":0,\"high\":100}}");
            WaitFor(() => !chat.Busy, "local Agent command via text chat", true);
            Check(chat.Current.Messages.Last().Content.Contains("工程值=50"), "local Agent command reached the normal chat route");
            UseDialog(Find(workspace, "agent-tasks-open"), "agent-tasks-dialog", panel =>
            {
                var log = Find(panel, "agent-task-receipt");
                WaitFor(() => log.Text.Contains("local.tool"), "agent tasks read from persistent store", true);
                Check(log.Text.Contains("engineering-calculator") && log.Text.Contains("完成"), "execution record shows real successful step");
                Check(log.ClientSize.Height > panel.ClientSize.Height / 2, "execution record has room to display steps");
                CaptureWindow(panel, Path.Combine(output, "agent-task-record-dpi" + dpi + ".png"));
            });
            form.SwitchView("mcp");
            var mcp = Find(workspace, "page-mcp");
            var probe = Descendants(mcp).OfType<DarkButton>().Single(button => button.Text == "检测 MCP 协议");
            Press(probe, Keys.Enter);
            var receipt = Find(workspace, "mcp-actual-receipt");
            WaitFor(() => receipt.Text.Contains("probe_succeeded") || receipt.Text.Contains("检测失败"), "MCP handshake", true);
            Check(receipt.Text.Contains("probe_succeeded"), "actual stdio MCP initialize and tool discovery");
            CaptureWindow(form, Path.Combine(output, "mcp-probe-dpi" + dpi + ".png"));
            string python = Environment.GetEnvironmentVariable("DESKBOX_MCP_TEST_PYTHON");
            if (string.IsNullOrEmpty(python)) throw new InvalidOperationException("CI must provide the Python stdio fixture executable.");
            VerifyThirdPartyDialog(Find(mcp, "mcp-gateway-open"), output, dpi, python);

            foreach (var size in new[] { new Size(1366, 768), new Size(1600, 900), new Size(1920, 1080),
                new Size(2560, 1440), new Size(3840, 2160) })
            {
                if (Screen.PrimaryScreen.Bounds.Width < size.Width || Screen.PrimaryScreen.Bounds.Height < size.Height)
                { Console.WriteLine("BLOCKED capture " + size + " display " + Screen.PrimaryScreen.Bounds.Size); continue; }
                form.Bounds = new Rectangle(Point.Empty, size);
                Pump();
                var header = Find(workspace, "engineering-header");
                var rail = (Panel)Find(workspace, "engineering-navigation");
                Check(!rail.HorizontalScroll.Visible, "primary navigation has no horizontal overflow at " + size);
                Check(header.Height >= (54 * dpi / 96), "header height follows measured DPI at " + size);
                var visible = header.Controls.Cast<Control>().Where(control => control.Visible).ToArray();
                Check(visible.All(control => header.ClientRectangle.Contains(control.Bounds)) &&
                    visible.SelectMany((left, index) => visible.Skip(index + 1).Select(right => !left.Bounds.IntersectsWith(right.Bounds))).All(ok => ok),
                    "header contained and no overlaps at " + size);
                foreach (var route in ReferenceWorkspace.ReferencePages)
                {
                    var nav = Find(workspace, "nav-" + route);
                    nav.Focus();
                    Press(nav, Keys.Enter);
                    Pump();
                    var page = Find(workspace, "page-" + route);
                    Check(workspace.CurrentPage == route && page.Visible && page.Width > 0 && page.Height > 0, "keyboard route " + route + " at " + size);
                    if (new[] { "home", "dcs", "validation" }.Contains(route))
                    {
                        var title = Find(page, "engineering-page-title");
                        Check(title.Height >= title.Font.Height, "page title room at " + route + " " + size);
                    }
                    string name = Path.Combine(output, size.Width + "x" + size.Height + "-dpi" + dpi + "-" + route + ".png");
                    CaptureWindow(form, name);
                }
            }
            form.Close();
        }
    }

    private static void VerifyThirdPartyDialog(Control entry, string output, uint dpi, string python)
    {
        var gateway = new McpGateway();
        string fixture = Path.GetFullPath("tools/v5-mcp-stdio-fixture.py");
        AiEnvironment.Select("network");
        var draft = gateway.SaveServer(a.Id, new McpServer { Name = "Acceptance stdio", Transport = "stdio",
            Address = python, Arguments = "\"" + fixture + "\"" });
        draft.Enabled = true;
        gateway.SaveServer(a.Id, draft);
        UseDialog(entry, "mcp-gateway-dialog", panel =>
        {
            var status = Find(panel, "mcp-operation-status");
            var discover = (Button)Find(panel, "mcp-discover");
            Check(discover.Visible && discover.Enabled, "third-party MCP entry and discover button are usable");
            discover.PerformClick();
            var tools = (ListBox)Find(panel, "mcp-discovered-tools");
            WaitFor(() => tools.Items.Count == 1 || status.Text.StartsWith("操作结束"), "native third-party stdio discovery", true);
            Check(tools.Items.Count == 1 && ((McpTool)tools.Items[0]).Name == "echo" && status.Text.Contains("发现完成") &&
                Find(panel, "mcp-call-receipt").Text.Contains("succeeded"),
                "third-party MCP dialog displays actual discovered tool and receipt");
            Check(!Find(panel, "mcp-invoke").Enabled && Find(panel, "mcp-allow-tool").Enabled, "manual call UI respects stored grant");
            CaptureWindow(panel, Path.Combine(output, "third-party-mcp-dpi" + dpi + ".png"));
            UseDialog(Find(panel, "mcp-register"), "mcp-registration-dialog", registration =>
            {
                Find(registration, "mcp-name").Text = "Registered from Windows UI";
                Find(registration, "mcp-address").Text = "https://fixture.invalid/mcp";
                ((Button)Find(registration, "mcp-save")).PerformClick();
            });
            bool recovered;
            Check(gateway.Servers(a.Id, out recovered).Any(item => item.Name == "Registered from Windows UI" && !item.Enabled),
                "registration form persists real disabled registration with no network access");
        });
    }

    private static void UseDialog(Control entry, string name, Action<Form> check)
    {
        Exception failure = null;
        bool opened = false;
        using (var timer = new System.Windows.Forms.Timer { Interval = 60 })
        {
            timer.Tick += delegate
            {
                var panel = Application.OpenForms.Cast<Form>().FirstOrDefault(form => form.Name == name && form.Visible);
                if (panel == null) return;
                timer.Stop();
                opened = true;
                try { Pump(); check(panel); }
                catch (Exception error) { failure = error; }
                finally { if (!panel.IsDisposed && panel.Visible) panel.Close(); }
            };
            timer.Start();
            var standardButton = entry as Button;
            if (standardButton == null) Press(entry, Keys.Enter);
            else standardButton.PerformClick();
        }
        if (failure != null) throw failure;
        Check(opened, "dialog opened from " + entry.Name);
    }

    private static Control Find(Control rootControl, string name) { return rootControl.Controls.Find(name, true).Single(); }

    private static IEnumerable<Control> Descendants(Control rootControl)
    {
        return rootControl.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    }

    private static object Field(object item, string name)
    {
        return item.GetType().GetField(name, Private).GetValue(item);
    }

    private static void Press(Control control, Keys key)
    {
        SendMessage(control.Handle, 0x0100, (IntPtr)key, IntPtr.Zero);
        SendMessage(control.Handle, 0x0101, (IntPtr)key, IntPtr.Zero);
    }

    private static void CaptureWindow(Form window, string path)
    {
        using (var image = new Bitmap(window.Width, window.Height))
        {
            using (var graphics = Graphics.FromImage(image))
            {
                var dc = graphics.GetHdc();
                try { if (!PrintWindow(window.Handle, dc, 2)) throw new IOException("Win32 PrintWindow failed."); }
                finally { graphics.ReleaseHdc(dc); }
            }
            if (image.GetPixel(0, 0).ToArgb() == image.GetPixel(image.Width / 2, image.Height / 2).ToArgb() &&
                image.GetPixel(0, 0).ToArgb() == image.GetPixel(image.Width - 1, image.Height - 1).ToArgb())
                throw new InvalidDataException("Window capture appears blank: " + Path.GetFileName(path));
            image.Save(path, ImageFormat.Png);
        }
    }

    private static void Pump() { Application.DoEvents(); Thread.Sleep(45); Application.DoEvents(); }

    private static void WaitFor(Func<bool> ready, string name, bool ui)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(20);
        while (!ready() && DateTime.UtcNow < end) { if (ui) Pump(); else Thread.Sleep(25); }
        if (!ready()) throw new TimeoutException(name);
    }

    private static void ExpectFailure(Action action, string label)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, label); return; }
        catch (InvalidOperationException) { Check(true, label); return; }
        catch (IOException) { Check(true, label); return; }
        catch (UnauthorizedAccessException) { Check(true, label); return; }
        Check(false, label);
    }

    private static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        passed++;
        Console.WriteLine("PASS " + label);
    }
}
