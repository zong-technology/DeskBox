using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed partial class TiaEngineerView : ViewBase
    {
        private enum Phase { Idle, Planning, WaitingForInput, WaitingForApproval, Executing, Validating, Completed, Failed, Cancelled }
        private readonly ProviderStore providers;
        private readonly WorkspaceStore projects;
        private readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, AccessibleName = "TIA 工程对象" };
        private readonly DarkInput treeQuery = new DarkInput { Dock = DockStyle.Top, Placeholder = "搜索名称、类型或路径" };
        private readonly TiaWorkbenchTabs tabs = new TiaWorkbenchTabs { Dock = DockStyle.Fill, Name = "tia-work-tabs" };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = "操作模式" };
        private readonly ComboBox cpu = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 275, AccessibleName = "目标 PLC" };
        private readonly RichTextBox requirement, editor, analysis, hardware, topology, tags, diff, output, assistant;
        private readonly ListView diagnosticList = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, AccessibleName = "工程诊断列表" };
        private readonly ListView hardwareInventory = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, AccessibleName = "硬件清单" };
        private readonly TiaRackView rack = new TiaRackView();
        private readonly TiaTopologyCanvas topologyCanvas = new TiaTopologyCanvas();
        private readonly Dictionary<PlcObject, ListViewItem> hardwareRows = new Dictionary<PlcObject, ListViewItem>();
        private readonly ListView networkLinks = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, AccessibleName = "拓扑连接" };
        private readonly ListView referenceGraph = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, AccessibleName = "程序关系" };
        private readonly DarkButton networkFrom = new DarkButton("") { Dock = DockStyle.Fill, Visible = false, AccessibleName = "定位拓扑端点 A 的硬件" };
        private readonly DarkButton networkTo = new DarkButton("") { Dock = DockStyle.Fill, Visible = false, AccessibleName = "定位拓扑端点 B 的硬件" };
        private readonly ComboBox diagnosticFilter = new ComboBox { Width = 105, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "诊断级别" };
        private readonly TextBox scaleName = new TextBox { Text = "FB_AnalogScale", Width = 128, AccessibleName = "缩放块名称" };
        private readonly TextBox catalogQuery = new TextBox { Width = 140, AccessibleName = "硬件目录型号或订货号" };
        private readonly NumericUpDown inputStart = new NumericUpDown { Maximum = 65535, Width = 70, AccessibleName = "输入起始字节" };
        private readonly NumericUpDown outputStart = new NumericUpDown { Maximum = 65535, Width = 70, AccessibleName = "输出起始字节" };
        private readonly CheckBox voiceAccess = new CheckBox { Text = "授权桌面 AI 查阅已加载工程", AutoSize = true };
        private readonly CheckBox useProjectContext = new CheckBox { Text = "引用已加载的工程", AutoSize = true };
        private readonly List<DarkButton> actions = new List<DarkButton>();
        private readonly DarkButton cancel, generate;
        private readonly Label modelStatus;
        private readonly Dictionary<string, string> unsent = new Dictionary<string, string>();
        private Dictionary<string, PlcObject> assets = new Dictionary<string, PlcObject>();
        private string draftEnvironment, submittedRequirement;
        private PlcSnapshot snapshot, baseline;
        private PlcObject selected;
        private TiaResult inspected;
        private TiaResult savedJob;
        private string apFile, templateName, nativeDiagnosticBlock;
        private string lastAiAnalysis = "";
        private string pendingScl, reviewedEditor, pendingTemplateName, diagnosticDraft;
        private IList<PlcDiagnostic> currentDiagnostics = new List<PlcDiagnostic>();
        private DarkButton applyDraft, repairDraft, approveInsertion, compileCopy, locateIssue;
        private Label diagnosticOrigin;
        private CancellationTokenSource operation;
        private CancellationToken voiceScope;
        private Phase phase;
        private TiaOpenProject liveProject;
        private string mcpStatus = "MCP 待检测";
        private string liveStatus = "TIA 待检测";

        public TiaEngineerView(Store store, ProviderStore providers, WorkspaceStore projects, Action configureModel = null,
            Action openVoice = null) : base(store, "博图 AI 工程师", "只读模式 · TIA Portal、SCL 与离线工程验证")
        {
            this.providers = providers; this.projects = projects;
            Name = "tia-engineer";
            foreach (Control control in new Control[] { mode, cpu, scaleName, catalogQuery, inputStart, outputStart, diagnosticFilter })
                control.Width = TiaWorkbenchStyle.Pixels(this, control.Width);
            mode.Items.AddRange(new object[] { "只读分析", "辅助设计", "自动离线工程" }); mode.SelectedIndex = 0;
            mode.SelectedIndexChanged += delegate { RefreshHeading(); UpdateButtons(); };
            cpu.Items.Add("选择工程 PLC"); cpu.SelectedIndex = 0;
            cpu.SelectedIndexChanged += delegate
            {
                if (programmingTarget != null && programmingTarget.Source.Native && programmingTarget.Cpu != Convert.ToString(cpu.SelectedItem))
                    InvalidateProgrammingTarget("验证页中的 PLC 选择已变更。");
                RefreshHeading(); UpdateButtons();
            };
            var top = Bar();
            var start = Button("输入工程需求", FocusRequirement);
            start.Primary = true;
            start.Name = "tia-focus-requirement";
            var bindTarget = Button("绑定工程", BindEngineeringTarget);
            bindTarget.Name = "tia-bind-project";
            var discover = Button("发现打开的工程", () => DiscoverOpenProjects(false));
            discover.Name = "tia-discover-projects";
            top.Controls.AddRange(new Control[] { start, bindTarget, mode, discover, Button("打开导出资料", ChooseExports), Button("读取 .ap20 副本", ChooseProject), Button("导出报告", ExportReport) });
            if (openVoice != null) top.Controls.Add(Button("工程语音", openVoice));
            cancel = new DarkButton("停止任务") { Width = TiaWorkbenchStyle.Pixels(this, 116), Height = TiaWorkbenchStyle.Pixels(this, TiaWorkbenchStyle.ControlHeight), Enabled = false, Palette = Palette };
            cancel.Name = "tia-cancel-task";
            cancel.AccessibleName = "停止当前任务";
            cancel.Click += delegate { if (operation != null) { operation.Cancel(); UpdateButtons(); UpdateComposerStatus(); } };

            requirement = TextArea("工程需求", false);
            requirement.Name = "tia-requirement";
            requirement.Font = UiKit.CachedFont("Microsoft YaHei UI", 11, FontStyle.Regular);
            requirement.BorderStyle = BorderStyle.FixedSingle;
            requirement.MaxLength = 4000;
            requirement.TabStop = true;
            requirement.ImeMode = ImeMode.On;
            requirement.AllowDrop = true;
            requirement.EnableAutoDragDrop = false;
            requirement.AccessibleDescription = "输入 PLC 系列、输入输出、约束或测试要求。Shift+Enter 换行。";
            requirement.TextChanged += delegate { UpdateButtons(); };
            requirement.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Control && e.KeyCode == Keys.A) { requirement.SelectAll(); e.SuppressKeyPress = true; return; }
                if (e.KeyCode != Keys.Enter || e.Shift || e.Alt || (!e.Control && !Store.Settings.TiaEnterSends) || IsComposing(requirement.Handle)) return;
                e.SuppressKeyPress = true;
                if (generate.Enabled) RunAction(Generate);
            };
            requirement.DragEnter += delegate(object sender, DragEventArgs e)
            {
                e.Effect = operation == null && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            requirement.DragDrop += delegate(object sender, DragEventArgs e)
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (operation == null && files != null) LoadExports(files, true);
            };
            editor = TextArea("SCL 草稿", false); editor.WordWrap = false; editor.BorderStyle = BorderStyle.FixedSingle;
            editor.HideSelection = false;
            editor.TextChanged += delegate { UpdateButtons(); };
            editor.KeyDown += IndentCode;
            analysis = TextArea("程序分析"); hardware = TextArea("硬件目录及建议"); topology = TextArea("网络与设备");
            tags = TextArea("变量与 IO"); diff = TextArea("工程差异"); output = TextArea("编译和操作日志"); assistant = TextArea("AI 工程助手");
            diff.Font = UiKit.CachedFont("Consolas", 10, FontStyle.Regular);
            generate = Button("发送工程需求", Generate);
            generate.Name = "tia-send-requirement";
            generate.Primary = true;
            isolatedDraft = Button("生成隔离草稿", () =>
                SubmitRequirement(string.IsNullOrWhiteSpace(requirement.Text) ? submittedRequirement : requirement.Text, true));
            modelStatus = Caption("");
            modelStatus.Name = "tia-model-status";
            BuildProgrammingPage();
            Page("程序分析", "选择程序块或变量查看调用及引用。只读工程可另行授权读取块正文。", BuildReferencePanel(), Button("读取块正文", ReadBlockContent), Button("创建 SCL 草稿", OpenSelectedScl), Button("解释所选对象", Explain), Button("检查工程", Check));
            Page("硬件组态", "从机架选择模块查看槽位、订货号和来源。可加载 CAx AML 或查询本机 V20 硬件目录。", BuildHardwarePanel(), catalogQuery, Button("查本机型号", Catalog), Button("AI 硬件建议", ExplainHardware), Button("导出硬件清单", ExportHardware));
            Page("网络拓扑", "选择一条已记录的连接，查看来源或点击端点定位所属硬件。", BuildNetworkPanel());
            Page("变量管理", "从工程变量或 IO CSV 检索地址。地址建议保留在详情区，可复核后再使用。", BuildVariablesPage(), Button("变量引用", () => SelectTab(1)), Button("检查 IO", Check), new Label { Text = "输入 I 起点", AutoSize = true }, inputStart, new Label { Text = "输出 Q 起点", AutoSize = true }, outputStart, Button("生成地址建议", PlanIo));
            applyDraft = Button("应用已预览草稿", ApplyDraft);
            Page("工程对比", "查看草稿的逐行变更，或用当前工程和另一份同类资料对比。", BuildDiffPage(), applyDraft, Button("设为基线", Remember), Button("对比另一批文件", Compare), Button("对比 .ap20 副本", CompareProject));
            repairDraft = Button("生成修复草稿", RepairDraft);
            var restoreBackup = Button("从快照恢复副本", RestoreBackup);
            compileCopy = Button("编译副本", () => Compile(false));
            approveInsertion = Button("批准新增 SCL 块并编译", () => Compile(true));
            approveInsertion.Name = "tia-approve-insertion";
            Page("测试验证", "静态检查、TIA 离线编译和现场验证各自记录。", BuildValidationSummary(), cpu, compileCopy, approveInsertion, restoreBackup, Button("加入待办", AddReviewTask), repairDraft, Button("解释编译诊断", ExplainCompile), Button("查看能力与环境", Inspect));
            BuildWorkbenchLayout(top, configureModel);
            tabs.SelectedIndexChanged += delegate { if (compact) LayoutWorkbench(); RefreshPageStates(); };
            tree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { ShowObject(e.Node == null ? null : e.Node.Tag as PlcObject); };
            tree.NodeMouseDoubleClick += delegate(object sender, TreeNodeMouseClickEventArgs e) { ShowObject(e.Node.Tag as PlcObject); };
            tree.NodeMouseClick += delegate(object sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Button != MouseButtons.Right) return;
                tree.SelectedNode = e.Node;
                var item = e.Node.Tag as PlcObject;
                if (item == null) return;
                var menu = MenuBuilder.Create(Palette);
                menu.Items.Add(MenuBuilder.Item("查看对象", () => ShowObject(item)));
                menu.Items.Add(MenuBuilder.Item("复制工程路径", () => Clipboard.SetText(item.Identity)));
                menu.Items.Add(MenuBuilder.Item("复制来源", () => Clipboard.SetText(item.Source)));
                menu.Closed += delegate { menu.Dispose(); };
                menu.Show(tree, e.Location);
            };
            assistant.Text = "输入工程需求后可查看计划、代码建议及审批步骤。导入工程资料后可引用项目上下文。";
            output.Text = "没有编译回执。可先检查 SCL 草稿，或在「测试验证」编译已加载的 V20 副本。";
            ApplyPalette(Palette);
            RestoreRequirement();
            RefreshHeading();
            HandleCreated += delegate
            {
                BeginInvoke(new Action(delegate
                {
                    if (IsDisposed || operation != null || snapshot != null) return;
                    if (TiaEnvironmentInspector.RunningPortalIds().Any()) RunAction(() => DiscoverOpenProjects(true));
                    else { liveStatus = "TIA 未运行"; RefreshHeading(); }
                }));
            };
        }

        private FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(2, 2, 2, 3) }; }
        private Label Caption(string text) { return new Label { Text = text, Dock = DockStyle.Top, Height = TiaWorkbenchStyle.Pixels(this, 24), Margin = Padding.Empty, AutoEllipsis = true, UseMnemonic = false, ForeColor = Palette.TextDim, Font = UiKit.CachedFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular) }; }
        private RichTextBox TextArea(string name, bool readOnly = true)
        {
            return new RichTextBox { Dock = DockStyle.Fill, ReadOnly = readOnly, AccessibleName = name, BorderStyle = BorderStyle.None, DetectUrls = false, Font = UiKit.CachedFont(readOnly ? "Microsoft YaHei UI" : "Consolas", 10.5f, FontStyle.Regular), MaxLength = 24000 };
        }
        private Control BuildCodeEditor()
        {
            var code = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var lines = new Panel { Dock = DockStyle.Left, Width = TiaWorkbenchStyle.Pixels(this, 40) };
            lines.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.Clear(Palette.HeaderBg);
                if (!editor.IsHandleCreated || editor.ClientSize.Height <= 0) return;
                int line = editor.GetLineFromCharIndex(editor.GetCharIndexFromPosition(Point.Empty));
                int caretLine = editor.GetLineFromCharIndex(editor.SelectionStart);
                int y = editor.Top;
                for (; y < lines.Height; line++)
                {
                    int start = editor.GetFirstCharIndexFromLine(line);
                    if (start < 0) break;
                    y = editor.Top + editor.GetPositionFromCharIndex(start).Y;
                    if (y >= lines.Height) break;
                    if (line == caretLine)
                        using (var activeLine = new SolidBrush(Palette.Hover)) e.Graphics.FillRectangle(activeLine, 0, y, lines.Width, editor.Font.Height);
                    TextRenderer.DrawText(e.Graphics, (line + 1).ToString(), editor.Font,
                        new Rectangle(0, y, lines.Width - 5, editor.Font.Height), line == caretLine ? Palette.Accent : Palette.TextDim,
                        TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                }
            };
            editor.VScroll += delegate { lines.Invalidate(); };
            editor.TextChanged += delegate { lines.Invalidate(); };
            editor.SizeChanged += delegate { lines.Invalidate(); };
            editor.SelectionChanged += delegate { lines.Invalidate(); };
            code.Controls.Add(editor);
            code.Controls.Add(lines);
            return code;
        }
        private void IndentCode(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || e.Control || e.Alt || editor.ReadOnly) return;
            int line = editor.GetLineFromCharIndex(editor.SelectionStart);
            string source = editor.Lines.ElementAtOrDefault(line) ?? "";
            editor.SelectedText = "\n" + new string(source.TakeWhile(c => c == ' ' || c == '\t').ToArray());
            e.SuppressKeyPress = true;
        }
        private void FindCode()
        {
            string term = Prompt.Input(this, "查找 SCL", "搜索草稿中的文字", editor.SelectedText, Palette);
            if (string.IsNullOrWhiteSpace(term)) return;
            int index = editor.Text.IndexOf(term, Math.Min(editor.TextLength, editor.SelectionStart + editor.SelectionLength), StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = editor.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0) { assistant.Text = "草稿中未找到 " + term; return; }
            editor.Select(index, term.Length);
            editor.Focus();
            editor.ScrollToCaret();
        }
        private void ReplaceCode()
        {
            string term = Prompt.Input(this, "查找并替换 SCL", "输入要匹配的文本（忽略大小写）", editor.SelectedText, Palette);
            if (string.IsNullOrEmpty(term)) return;
            string replacement = Prompt.Input(this, "查找并替换 SCL", "输入替换文本，留空表示删除。", "", Palette);
            if (replacement == null) return;
            string updated = SclEditing.ReplaceLiteral(editor.Text, term, replacement);
            if (updated.Length > editor.MaxLength) throw new InvalidOperationException("替换结果超过 SCL 草稿字数上限。");
            if (SameSource(editor.Text, updated)) { assistant.Text = "未找到需要替换的文本。"; return; }
            ShowDraftPreview(updated);
            UpdateButtons();
            SetPhase(Phase.WaitingForApproval);
        }
        private void MatchBracket()
        {
            var pairs = SclEditing.Brackets(editor.Text);
            int index = editor.SelectionStart;
            int match;
            if (!pairs.TryGetValue(index, out match) && !pairs.TryGetValue(index - 1, out match))
            {
                assistant.Text = "请把光标放到成对的括号上或括号后，再点击「匹配括号」。注释和字面量中的括号不参与匹配。";
                return;
            }
            editor.Select(match, 1);
            editor.Focus();
            editor.ScrollToCaret();
        }
        private Control BuildValidationPanel()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, TiaWorkbenchStyle.Pixels(this, 34)));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            diagnosticFilter.Items.AddRange(new object[] { "全部诊断", "错误", "警告", "其他" });
            diagnosticFilter.SelectedIndex = 0;
            diagnosticFilter.SelectedIndexChanged += delegate { RefreshDiagnostics(); };
            diagnosticList.Columns.Add("级别", 65);
            diagnosticList.Columns.Add("来源与位置", 200);
            diagnosticList.Columns.Add("诊断说明", 540);
            diagnosticList.ShowItemToolTips = true;
            diagnosticList.ItemActivate += delegate { LocateSelectedDiagnostic(); };
            locateIssue = Button("定位所选", LocateSelectedDiagnostic, false);
            locateIssue.Name = "tia-locate-diagnostic";
            locateIssue.Enabled = false;
            locateIssue.Dock = DockStyle.Right;
            locateIssue.Width = TiaWorkbenchStyle.Pixels(this, 90);
            diagnosticList.SelectedIndexChanged += delegate { locateIssue.Enabled = diagnosticList.SelectedItems.Count == 1; };
            diagnosticOrigin = Caption("尚无检查或编译诊断");
            diagnosticOrigin.Name = "tia-diagnostic-origin";
            diagnosticOrigin.Dock = DockStyle.Fill;
            diagnosticOrigin.TextAlign = ContentAlignment.MiddleLeft;
            diagnosticOrigin.Padding = new Padding(TiaWorkbenchStyle.Pixels(this, 6), 0, 0, 0);
            diagnosticFilter.Dock = DockStyle.Left;
            var controls = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            controls.Controls.AddRange(new Control[] { diagnosticOrigin, locateIssue, diagnosticFilter });
            output.TextChanged += delegate { SetDiagnostics(null); };
            panel.Controls.Add(controls, 0, 0);
            panel.Controls.Add(diagnosticList, 0, 1);
            return panel;
        }
        private Control BuildHardwarePanel()
        {
            FormatCombo(rack.Containers);
            rack.AssetSelected += item => FocusHardware(item, true);
            foreach (var col in new[] { "名称", "槽位", "订货号", "固件", "所属设备" })
                hardwareInventory.Columns.Add(col, col == "槽位" || col == "固件" ? 65 : 165);
            hardwareInventory.SelectedIndexChanged += delegate
            {
                if (hardwareInventory.SelectedItems.Count == 1)
                    FocusHardware((PlcObject)hardwareInventory.SelectedItems[0].Tag, false);
            };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, TiaWorkbenchStyle.Pixels(this, 147)));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(rack, 0, 0);
            panel.SetColumnSpan(rack, 2);
            panel.Controls.Add(WithHeading("硬件清单", hardwareInventory), 0, 1);
            panel.Controls.Add(WithHeading("设备属性", hardware), 1, 1);
            hardware.Text = "加载含硬件数据的工程或 CAx AML 后，可选择槽位与模块查看属性。";
            return panel;
        }

        private Control WithHeading(string title, Control content)
        {
            var pane = new Panel { Dock = DockStyle.Fill };
            pane.Controls.AddRange(new[] { content, Heading(title) });
            return pane;
        }

        private void FocusHardware(PlcObject item, bool selectRow)
        {
            selected = item;
            UpdateWorkbenchContext();
            rack.FocusAsset(item);
            hardware.Text = DescribeHardware(item);
            ListViewItem row;
            if (!selectRow) return;
            if (hardwareRows.TryGetValue(item, out row))
            {
                if (!row.Selected) row.Selected = true;
                row.EnsureVisible();
            }
            else hardwareInventory.SelectedIndices.Clear();
        }
        private Control BuildNetworkPanel()
        {
            networkLinks.Columns.Add("关系", 90);
            networkLinks.Columns.Add("端点 A", 230);
            networkLinks.Columns.Add("端点 B", 230);
            networkLinks.Columns.Add("来源", 125);
            var diagram = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            diagram.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            diagram.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TiaWorkbenchStyle.Pixels(this, 65)));
            diagram.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var connection = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
            diagram.Controls.Add(networkFrom, 0, 0);
            diagram.Controls.Add(connection, 1, 0);
            diagram.Controls.Add(networkTo, 2, 0);
            foreach (var button in new[] { networkFrom, networkTo })
                button.Click += delegate(object sender, EventArgs e)
                {
                    var owner = ((Control)sender).Tag as PlcObject;
                    if (owner != null) ShowObject(owner);
                };
            networkLinks.SelectedIndexChanged += delegate
            {
                var pair = networkLinks.SelectedItems.Count == 1
                    ? networkLinks.SelectedItems[0].Tag as Tuple<PlcTopologyLink, PlcObject, PlcObject> : null;
                connection.Text = pair == null ? "" : "⟷";
                ShowNetworkEndpoint(networkFrom, pair == null ? null : pair.Item2);
                ShowNetworkEndpoint(networkTo, pair == null ? null : pair.Item3);
                topologyCanvas.SelectLink(pair == null ? null : pair.Item1);
                if (pair != null)
                    topology.Text = pair.Item1.Kind + " · 来源 " + pair.Item1.Source + "\r\n\r\n" +
                        DescribeHardware(pair.Item2) + "\r\n\r\n" + DescribeHardware(pair.Item3);
            };
            topologyCanvas.EndpointSelected += endpoint =>
            {
                var row = networkLinks.Items.Cast<ListViewItem>().FirstOrDefault(candidate =>
                {
                    var pair = (Tuple<PlcTopologyLink, PlcObject, PlcObject>)candidate.Tag;
                    return pair.Item2 == endpoint || pair.Item3 == endpoint;
                });
                if (row == null) return;
                row.Selected = true;
                row.EnsureVisible();
            };
            var zoom = Bar();
            zoom.Controls.AddRange(new Control[] { Button("缩小", () => topologyCanvas.ZoomBy(-1), false), Button("放大", () => topologyCanvas.ZoomBy(1), false),
                Button("恢复 100%", () => topologyCanvas.ResetZoom(), false) });
            var canvas = new Panel { Dock = DockStyle.Fill };
            canvas.Controls.AddRange(new Control[] { topologyCanvas, zoom });
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            panel.Controls.Add(canvas, 0, 0);
            panel.Controls.Add(WithHeading("连接清单", networkLinks), 1, 0);
            var details = WithHeading("连接与端点详情", topology);
            diagram.Dock = DockStyle.Bottom;
            diagram.Height = TiaWorkbenchStyle.Pixels(this, 34);
            details.Controls.Add(diagram);
            panel.Controls.Add(details, 0, 1);
            panel.SetColumnSpan(details, 2);
            topology.Text = "载入包含端点和连线的 CAx AML，可在画布或清单中定位设备。";
            return panel;
        }

        private void ShowNetworkEndpoint(DarkButton button, PlcObject endpoint)
        {
            PlcObject owner = endpoint;
            for (int depth = 0; owner != null && !PlcEngineering.IsHardware(owner) && depth < 64; depth++)
                assets.TryGetValue(owner.ParentAssetId, out owner);
            if (owner != null && !PlcEngineering.IsHardware(owner)) owner = null;
            button.Tag = owner;
            button.Visible = endpoint != null;
            button.Enabled = owner != null;
            button.Text = endpoint == null ? "" : endpoint.Scope + " / " + endpoint.Name;
            button.AccessibleDescription = owner == null ? "未找到端点所属硬件。" : "定位 " + owner.Name + " · " + endpoint.Source;
        }
        private Control BuildReferencePanel()
        {
            referenceGraph.Columns.Add("关系", 75);
            referenceGraph.Columns.Add("符号", 155);
            referenceGraph.Columns.Add("目标对象", 240);
            referenceGraph.Columns.Add("引用方来源", 250);
            referenceGraph.ItemActivate += delegate
            {
                if (referenceGraph.SelectedItems.Count != 1) return;
                var item = referenceGraph.SelectedItems[0].Tag as PlcObject;
                if (item != null) ShowObject(item, true);
            };
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            analysis.Text = "先打开工程资料，再选择程序块或变量。这里显示静态发现、来源位置和分析建议。";
            panel.Controls.Add(WithHeading("分析结论与来源", analysis), 0, 0);
            panel.Controls.Add(WithHeading("调用与变量引用", referenceGraph), 0, 1);
            return panel;
        }
        private static string DescribeHardware(PlcObject item)
        {
            return item.Kind + " · " + item.Scope + " / " + item.Name + "\r\n来源 " + item.Source +
                "\r\n型号 " + item.HardwareType + " · 订货号 " + item.OrderNumber + " · 固件 " + item.Firmware +
                " · 槽位 " + item.Slot + (item.Address.Length == 0 ? "" : " · 地址 " + item.Address) + "\r\n" + item.Content;
        }
        private void FillHardware(PlcSnapshot value)
        {
            var hardwareAssets = value.Objects.Where(PlcEngineering.IsHardware).ToList();
            hardwareRows.Clear();
            hardwareInventory.BeginUpdate();
            try
            {
                hardwareInventory.Items.Clear();
                foreach (var item in hardwareAssets.Take(2000))
                {
                    var row = hardwareInventory.Items.Add(new ListViewItem(new[] { item.Name, item.Slot, item.OrderNumber, item.Firmware, item.Scope }) { Tag = item });
                    hardwareRows.Add(item, row);
                }
            }
            finally { hardwareInventory.EndUpdate(); }
            rack.LoadAssets(value.Objects);
            hardware.Text = hardwareInventory.Items.Count == 0 ? "请加载 CAx AML 或 TIA 工程。当前资料没有设备清单。" :
                "选择模块查看源数据。列表最多显示前 2000 个硬件对象，导出包含全部已读取硬件。";

            assets = value.Objects.Where(o => o.AssetId.Length != 0).ToDictionary(o => o.AssetId);
            networkLinks.BeginUpdate();
            networkLinks.Items.Clear();
            foreach (var link in value.TopologyLinks.Take(2000))
            {
                var a = assets[link.First];
                var b = assets[link.Second];
                networkLinks.Items.Add(new ListViewItem(new[] { link.Kind, a.Scope + " / " + a.Name, b.Scope + " / " + b.Name, link.Source })
                    { Tag = Tuple.Create(link, a, b) });
            }
            networkLinks.EndUpdate();
            topologyCanvas.LoadNetwork(assets, value.TopologyLinks);
            topology.Text = (value.TopologyLinks.Count == 0 ? "当前资料未提供可解析的端点连接。可加载含拓扑的 Siemens CAx AML。" :
                "已解析 " + value.TopologyLinks.Count + " 条连接。选择一条查看端点与来源，列表最多显示前 2000 条。") +
                "\r\n\r\n子网\r\n" + string.Join(Environment.NewLine, value.Objects.Where(o => o.Kind == "Subnet").Take(2000)
                    .Select(o => o.Name + " · " + o.Source));
        }
        private void ExportHardware()
        {
            if (snapshot == null || !snapshot.Objects.Any(PlcEngineering.IsHardware)) throw new InvalidOperationException("请先加载含设备的 CAx AML 或工程资料。");
            Save("Markdown 清单 (*.md)|*.md", "DeskBox-Hardware.md", "# 工程硬件清单\r\n\r\n" +
                string.Join("\r\n\r\n", snapshot.Objects.Where(PlcEngineering.IsHardware).Select(DescribeHardware)));
        }
        private void SetDiagnostics(IList<PlcDiagnostic> issues, string source = null, string nativeBlock = null, string origin = null)
        {
            currentDiagnostics = issues ?? new List<PlcDiagnostic>();
            diagnosticDraft = source;
            nativeDiagnosticBlock = nativeBlock;
            diagnosticOrigin.Text = origin ?? (issues == null ? "尚无检查或编译诊断" : source == null ? "工程资料检查" : "SCL 静态检查");
            workbenchTips.SetToolTip(diagnosticOrigin, diagnosticOrigin.Text);
            RefreshDiagnostics();
            if (diagnosisCount != null)
            {
                int errors = currentDiagnostics.Count(d => d.Severity == "Error");
                int warnings = currentDiagnostics.Count(d => d.Severity == "Warning");
                diagnosisCount.Text = currentDiagnostics.Count == 0 ? "无诊断记录" : errors + " 错误  ·  " + warnings + " 警告  ·  " + (currentDiagnostics.Count - errors - warnings) + " 其他";
                diagnosisCount.ForeColor = errors > 0 ? TiaWorkbenchStyle.Error(Palette) : warnings > 0 ? TiaWorkbenchStyle.Warning(Palette) : Palette.TextDim;
            }
            UpdateButtons();
            RefreshPageStates();
        }
        private void RefreshDiagnostics()
        {
            diagnosticList.BeginUpdate();
            diagnosticList.Items.Clear();
            try
            {
                foreach (var issue in currentDiagnostics.OrderBy(i => i.Severity == "Error" ? 0 : i.Severity == "Warning" ? 1 : 2).ThenBy(i => i.Line).ThenBy(i => i.Column))
                {
                    int severity = issue.Severity == "Error" ? 1 : issue.Severity == "Warning" ? 2 : 3;
                    if (diagnosticFilter.SelectedIndex > 0 && diagnosticFilter.SelectedIndex != severity) continue;
                    var row = new ListViewItem(severity == 1 ? "错误" : severity == 2 ? "警告" : "其他");
                    row.SubItems.Add(issue.Source + (issue.Line > 0 ? " · " + issue.Line + ":" + issue.Column : ""));
                    row.SubItems.Add(issue.Message);
                    row.ToolTipText = issue.ToString();
                    row.Tag = issue;
                    diagnosticList.Items.Add(row);
                }
            }
            finally { diagnosticList.EndUpdate(); }
        }
        private void LocateSelectedDiagnostic()
        {
            if (diagnosticList.SelectedItems.Count == 1)
                LocateDiagnostic((PlcDiagnostic)diagnosticList.SelectedItems[0].Tag);
        }

        private void LocateDiagnostic(PlcDiagnostic issue)
        {
            if (issue.Line <= 0 && nativeDiagnosticBlock != null && MatchesBlock(issue, nativeDiagnosticBlock) && SameSource(diagnosticDraft, editor.Text))
            {
                SelectTab(0);
                editor.Select(0, editor.Lines.Length == 0 ? 0 : editor.Lines[0].Length);
                editor.Focus();
                editor.ScrollToCaret();
                assistant.Text = "已定位已提交的 SCL 块。TIA 回执提供对象路径，具体行号请在 TIA 中核对。";
                return;
            }
            if (issue.Line > 0 && diagnosticDraft != null && (nativeDiagnosticBlock == null || MatchesBlock(issue, nativeDiagnosticBlock)))
            {
                if (!SameSource(diagnosticDraft, editor.Text))
                {
                    assistant.Text = "草稿已变化。请重新静态检查，更新诊断位置。";
                    return;
                }
                SelectTab(0);
                int start = editor.GetFirstCharIndexFromLine(issue.Line - 1);
                if (start >= 0)
                {
                    int column = Math.Min(editor.Lines[issue.Line - 1].Length, Math.Max(0, issue.Column - 1));
                    editor.Select(start + column, 0);
                    editor.Focus();
                    editor.ScrollToCaret();
                }
                return;
            }
            string source = issue.Source.Replace('\\', '/').Trim('/');
            var match = snapshot == null ? new List<PlcObject>() : snapshot.Objects.Where(o =>
                string.Equals(o.Source, issue.Source, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(o.Identity, issue.Source, StringComparison.OrdinalIgnoreCase) ||
                (o.Controller.Length != 0 && source.EndsWith("/" + o.Scope + "/" + o.Name, StringComparison.OrdinalIgnoreCase))).Take(2).ToList();
            if (issue.Source.Length != 0 && match.Count == 1) ShowObject(match[0]);
            else assistant.Text = "当前资料无法唯一定位这条诊断。请在 TIA Portal 中核对诊断路径。";
        }

        private static bool MatchesBlock(PlcDiagnostic issue, string block)
        {
            return issue.Source.Split('/', '\\').Any(part => part.Trim().Equals(block, StringComparison.OrdinalIgnoreCase) ||
                part.Trim().Equals(block + ".scl", StringComparison.OrdinalIgnoreCase) ||
                part.Trim().StartsWith(block + " [", StringComparison.OrdinalIgnoreCase));
        }
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hwnd);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hwnd, IntPtr context);
        [DllImport("imm32.dll", CharSet = CharSet.Unicode)] private static extern int ImmGetCompositionString(IntPtr context, int index, IntPtr buffer, int length);
        private static bool IsComposing(IntPtr hwnd)
        {
            var context = ImmGetContext(hwnd);
            if (context == IntPtr.Zero) return false;
            try { return ImmGetCompositionString(context, 8, IntPtr.Zero, 0) > 0; }
            finally { ImmReleaseContext(hwnd, context); }
        }
        private void FocusRequirement()
        {
            SelectTab(0);
            requirement.Focus();
        }
        internal bool AppendVoiceRequirement(string transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return false;
            string addition = (requirement.TextLength == 0 ? "" : "\n") + transcript.Trim();
            if (requirement.TextLength + addition.Length > requirement.MaxLength) return false;
            requirement.AppendText(addition);
            FocusRequirement();
            return true;
        }
        private void FillExample()
        {
            if (string.IsNullOrWhiteSpace(requirement.Text))
                requirement.Text = "为 S7-1200 写一个模拟量线性缩放 FB。输入为原始值、原始上下限和工程量上下限，输出为缩放值、有效标记和越界标记。请列出端点、中点、越界和无效量程的测试用例。实际模块和量程待确认。";
            FocusRequirement();
        }
        private void RestoreRequirement()
        {
            string current = AiEnvironment.Suffix;
            if (current == draftEnvironment) return;
            if (draftEnvironment != null) unsent[draftEnvironment] = requirement.Text;
            draftEnvironment = current;
            ResetEngineeringConversation();
            programmingTask = null;
            latestProgrammingStage = "";
            UpdateAssistantStage();
            if (programmingProgress != null) programmingProgress.Text = "在当前环境提交 SCL 需求或打开任务记录。";
            if (assistantTabs != null) assistantTabs.SelectedIndex = 0;
            string draft;
            requirement.Text = unsent.TryGetValue(current, out draft)
                ? draft : Json.Load(AppPaths.File("tia-requirement" + current + ".json"), "");
        }
        private void SaveRequirement()
        {
            RestoreRequirement();
            var path = AppPaths.File("tia-requirement" + draftEnvironment + ".json");
            Json.Save(path, requirement.Text);
            if (Json.Load(path, (string)null) != requirement.Text) throw new IOException("未能保存需求草稿。");
            assistant.Text = "工程需求草稿已保存在当前 AI 环境的本机资料目录。";
            requirement.Focus();
        }
        private DarkButton Button(string label, Action action, bool blocksDuringOperation = true)
        {
            var button = new DarkButton(label) { Height = TiaWorkbenchStyle.Pixels(this, TiaWorkbenchStyle.ControlHeight), Palette = Palette, AccessibleName = label };
            button.Font = UiKit.CachedFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
            button.Width = Math.Max(TiaWorkbenchStyle.Pixels(this, 84), TextRenderer.MeasureText(label, button.Font).Width + TiaWorkbenchStyle.Pixels(this, 20));
            button.Click += delegate { RunAction(action); };
            if (blocksDuringOperation) actions.Add(button);
            return button;
        }
        private void RunAction(Action action)
        {
            try { action(); } catch (Exception e) { Failed(e.Message); }
        }
        private void FormatCombo(ComboBox box)
        {
            box.FlatStyle = FlatStyle.Flat;
            box.DrawMode = DrawMode.OwnerDrawFixed;
            box.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var fill = new SolidBrush(selected ? Palette.Hover : Palette.PanelAlt)) e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, box.GetItemText(box.Items[e.Index]), box.Font, e.Bounds, Palette.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                e.DrawFocusRectangle();
            };
        }
        private void Page(string title, string guidance, Control target, params Control[] buttons)
        {
            var page = new TabPage(title) { Padding = new Padding(4) };
            page.Controls.Add(target);
            if (buttons.Length > 0) { var bar = Bar(); bar.Controls.AddRange(buttons); page.Controls.Add(bar); }
            AddWorkbenchPage(page, guidance);
        }
        private void SelectTab(int index) { tabs.SelectedIndex = index; }
        private void UpdateButtons()
        {
            if (cancel == null || editor == null || generate == null) return;
            foreach (DarkButton b in actions)
                b.Enabled = operation == null && (mode.SelectedIndex > 0 || !new[] { "替换 SCL", "保存 SCL", "AI 硬件建议", "生成地址建议" }.Contains(b.Text));
            bool nativeReady = operation == null && AllowsNativeCompilation(false);
            if (compileCopy != null) compileCopy.Enabled = nativeReady && mode.SelectedIndex > 0;
            if (approveInsertion != null) approveInsertion.Enabled = nativeReady && AllowsNativeCompilation(true) && mode.SelectedIndex == 2 && editor.TextLength != 0 && pendingScl == null;
            generate.Enabled = operation == null && !string.IsNullOrWhiteSpace(requirement.Text);
            if (isolatedDraft != null) isolatedDraft.Enabled = operation == null &&
                (!string.IsNullOrWhiteSpace(requirement.Text) || !string.IsNullOrWhiteSpace(submittedRequirement));
            useProjectContext.Enabled = operation == null && programmingTarget != null && Store.Settings.AiFileAccess;
            useProjectContext.Visible = programmingTarget != null && Store.Settings.AiFileAccess;
            if (applyDraft != null) applyDraft.Enabled = operation == null && pendingScl != null && mode.SelectedIndex > 0;
            if (repairDraft != null) repairDraft.Enabled = operation == null && diagnosticDraft != null && currentDiagnostics.Count > 0 && SameSource(diagnosticDraft, editor.Text);
            bool stopping = operation != null && operation.IsCancellationRequested;
            cancel.Enabled = operation != null && !stopping;
            cancel.Text = stopping ? "停止中…" : "停止任务";
            mode.Enabled = operation == null; cpu.Enabled = operation == null;
            editor.ReadOnly = operation != null || mode.SelectedIndex == 0;
        }
        private void RefreshHeading()
        {
            HintLabel.Text = Convert.ToString(mode.SelectedItem) + " · " + (snapshot == null ? "未加载工程" : snapshot.Name + " / " + snapshot.Origin) + " · " +
                (cpu.SelectedIndex > 0 ? "PLC " + cpu.SelectedItem + " · " : "") +
                "任务：" + PhaseLabel(phase);
            UpdateWorkbenchContext();
            UpdateAssistantStage();
        }

        private void UpdateComposerStatus()
        {
            if (modelStatus == null) return;
            if (activeWorkbenchAction != null)
            {
                string step = activeWorkbenchAction == EngineeringChatAction && latestProgrammingStage.Length != 0 ? latestProgrammingStage : activeWorkbenchAction;
                modelStatus.Text = operation != null && operation.IsCancellationRequested ? "正在停止 · " + step : "当前操作 · " + step;
            }
            else
            {
                var provider = providers == null ? null : providers.DefaultProvider;
                modelStatus.Text = provider == null || !provider.Enabled
                    ? "未配置 AI 服务商 · 可载入本机 SCL 模板"
                    : provider.Name + " · " + provider.Model + " · " + (Store.Settings.TiaEnterSends ? "Enter 发送" : "Ctrl+Enter 发送") + "，Shift+Enter 换行";
            }
            workbenchTips.SetToolTip(modelStatus, modelStatus.Text);
        }

        private static string PhaseLabel(Phase value)
        {
            switch (value)
            {
                case Phase.Planning: return "生成计划";
                case Phase.WaitingForInput: return "待补充";
                case Phase.WaitingForApproval: return "待批准";
                case Phase.Executing: return "执行中";
                case Phase.Validating: return "验证中";
                case Phase.Completed: return "已完成";
                case Phase.Failed: return "失败";
                case Phase.Cancelled: return "已取消";
                default: return "就绪";
            }
        }
        private void SetPhase(Phase value) { phase = value; RefreshHeading(); }
        protected override void OnActivated()
        {
            base.OnActivated();
            RestoreRequirement();
            UpdateButtons();
            if (voiceScope.IsCancellationRequested) voiceAccess.Checked = false;
            RefreshHeading();
        }
        public override void ApplyPalette(Palette palette)
        {
            if (tabs != null) foreach (TabPage page in tabs.TabPages) page.BackColor = palette.Bg;
            base.ApplyPalette(palette);
            if (assistant == null) return;
            foreach (var box in new[] { requirement, editor, analysis, hardware, topology, tags, diff, output, assistant })
            { box.BackColor = palette.CardBg; box.ForeColor = palette.Text; }
            requirement.BackColor = requirement.Focused ? UiKit.Mix(palette.PanelAlt, palette.Accent, 0.15) : palette.PanelAlt;
            tree.BackColor = palette.CardBg; tree.ForeColor = palette.Text; tree.LineColor = palette.Line;
            foreach (var list in new[] { diagnosticList, hardwareInventory, networkLinks, referenceGraph, variableGrid, lineChanges })
            { list.BackColor = palette.CardBg; list.ForeColor = palette.Text; }
            rack.ApplyPalette(palette);
            foreach (Control control in new Control[] { mode, cpu, scaleName, catalogQuery, voiceAccess, useProjectContext }) { control.BackColor = palette.CardBg; control.ForeColor = palette.Text; }
            ApplyWorkbenchPalette(palette);
        }
        private async void Work<T>(string action, Func<CancellationToken, T> work, Action<T> display,
            bool keepPage = false, Phase running = Phase.Executing, Func<T, Phase> outcome = null, Action<Exception> ended = null, Action<T> after = null)
        {
            if (operation != null) return;
            operation = CancellationTokenSource.CreateLinkedTokenSource(AiEnvironment.Token);
            var active = operation;
            lastWorkbenchError = null;
            activeWorkbenchAction = action;
            SetPhase(running);
            UpdateButtons(); assistant.Text = "正在执行 · " + action;
            AppendOperation("开始 · " + action);
            Store.Log("info", "engineering", "TIA · 开始 " + action + ProjectLabel());
            T value = default(T);
            bool finished = false;
            try
            {
                value = await Task.Run(() => work(active.Token), active.Token);
                active.Token.ThrowIfCancellationRequested();
                if (!IsDisposed)
                {
                    display(value);
                    SetPhase(outcome == null ? Phase.Completed : outcome(value));
                    AppendOperation(PhaseLabel(phase) + " · " + action);
                    Store.Log(phase == Phase.Failed ? "warn" : "info", "engineering", "TIA · " + PhaseLabel(phase) + " · " + action + ProjectLabel());
                    finished = phase != Phase.Failed;
                }
            }
            catch (OperationCanceledException ex)
            {
                if (ended != null) ended(ex);
                if (!IsDisposed)
                {
                    assistant.Text = keepPage ? "任务已取消，可修改需求后重试。" : "任务已取消。已生成的隔离副本和回执可核对。";
                    AppendOperation("已取消 · " + action);
                    SetPhase(Phase.Cancelled);
                }
            }
            catch (Exception e) { if (ended != null) ended(e); if (!IsDisposed) Failed(e.Message, keepPage); }
            finally { operation = null; activeWorkbenchAction = null; active.Dispose(); if (!IsDisposed) { UpdateButtons(); UpdateAssistantStage(); } }
            if (finished && after != null && !IsDisposed && Visible) RunAction(() => after(value));
        }
        private void Failed(string message, bool keepPage = false)
        {
            assistant.Text = message; output.AppendText(Environment.NewLine + message);
            AppendOperation(message.Split('\n')[0]);
            lastWorkbenchError = message;
            SetPhase(Phase.Failed);
            if (!keepPage) { SelectTab(6); OpenOutput(0); }
            Store.Log("warn", "engineering", "TIA · " + message.Split('\n')[0]);
        }
        private void Inspect()
        {
            Work("检测本机 TIA", t => TiaEnvironmentInspector.Inspect(), result =>
            {
                environmentReport = result;
                bool readyToAttempt = result.OpennessGroup && result.Installations.Any(item => item.Version == 20 && item.AssemblyPath != null);
                v20Checked = true;
                v20Discovered = readyToAttempt;
                string connection = readyToAttempt ? "已发现 V20 接口与权限线索，仍需打开实际工程确认许可和能力。" : "缺少 V20 接口或当前用户权限，原生操作不可用。";
                output.Text = result.Describe() + "\r\n\r\n连接状态\r\n" + connection +
                    "\r\n\r\n工程、设备与程序块：支持读取导出文件；V20 可尝试读取离线副本。" +
                    "\r\nSCL 导入：V20 支持经审批新增单个 FC/FB。离线编译需选定目标 PLC。" +
                    "\r\n变量表：读取、静态检查和建议。硬件：目录查询和设备建议。当前无写入入口。" +
                    "\r\n网络、LAD/FBD：查看已读取的结构或导出连线。当前无图形编辑入口。" +
                    "\r\n实际项目、真实编译与 PLCSIM 验收分别记录。";
                assistant.Text = connection;
                SelectTab(6);
                OpenOutput(0);
            });
        }
        private void DiscoverOpenProjects(bool automatic)
        {
            var job = new TiaJob { Operation = "discover", ProjectId = ProjectId() };
            Work("发现打开的 TIA 工程", token => TiaOpenness.Run(job, token), result =>
            {
                if (!automatic) ShowReceipt(result);
                else if (result.McpAudit.Length != 0) mcpStatus = "MCP 已响应";
                if (result.Error.Length != 0)
                {
                    liveStatus = "TIA 不可用";
                    string message = "无法发现打开的工程。请核对已安装的 TIA Portal 版本和 Openness 授权。\r\n" + result.Error;
                    if (!automatic)
                    {
                        assistant.Text = message;
                        output.Text = message + "\r\n" + result.Report + "\r\nMCP 审计 · " + result.McpAudit;
                    }
                    return;
                }
                liveStatus = result.CompatibleProcesses == 0 ? "V20 无实例" : result.OpenProjects.Count == 0 ? "V20 无打开工程" : "V20 已发现工程";
                assistant.Text = result.Report + "\r\n" + (result.OpenProjects.Count == 0
                    ? "请在 TIA Portal V20 打开独立 .ap20 工程，或使用导出资料继续。"
                    : automatic ? "点击「发现打开的工程」选择要读取的工程。" : "请选择要只读读取的工程。");
            }, outcome: ReceiptPhase, keepPage: automatic, ended: error => { mcpStatus = "MCP 连接异常"; liveStatus = "TIA 待确认"; },
                after: result =>
                {
                    if (automatic) return;
                    var target = PickOpenProject(result.OpenProjects);
                    if (target != null) ReadOpenProject(target);
                });
        }

        private TiaOpenProject PickOpenProject(IList<TiaOpenProject> entries)
        {
            if (entries.Count == 0) return null;
            using (var dialog = ReviewWindow("选择打开的 V20 工程", 820, 420))
            {
                var projectsList = new ListBox { Dock = DockStyle.Fill, AccessibleName = "已打开的 V20 工程",
                    BackColor = Palette.CardBg, ForeColor = Palette.Text, HorizontalScrollbar = true };
                projectsList.Items.AddRange(entries.Select(p => p.Name + " · PID " + p.ProcessId + " · " + p.Path).Cast<object>().ToArray());
                var buttons = Bar(); buttons.Dock = DockStyle.Bottom;
                var read = new Button { Text = "读取所选工程", AutoSize = true, Enabled = false, DialogResult = DialogResult.OK };
                var back = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
                projectsList.SelectedIndexChanged += delegate { read.Enabled = projectsList.SelectedIndex >= 0; };
                buttons.Controls.AddRange(new Control[] { read, back });
                dialog.Controls.AddRange(new Control[] { projectsList, buttons });
                dialog.CancelButton = back;
                if (entries.Count == 1) projectsList.SelectedIndex = 0;
                return dialog.ShowDialog(FindForm()) == DialogResult.OK && projectsList.SelectedIndex >= 0 ? entries[projectsList.SelectedIndex] : null;
            }
        }

        private void ReadOpenProject(TiaOpenProject target)
        {
            var job = new TiaJob { Operation = "read-open", ProjectFile = target.Path, PortalProcessId = target.ProcessId, ProjectId = ProjectId() };
            Work("只读附加 V20 工程", token => TiaOpenness.Run(job, token), result =>
            {
                ShowReceipt(result);
                if (result.Error.Length != 0 || result.Snapshot == null) { liveStatus = "V20 读取失败"; return; }
                InvalidateProgrammingTarget("当前已切换为打开工程的只读视图。");
                programmingSource = null; inspected = null; apFile = null; lastNativeValidation = ""; lastAiAnalysis = "";
                liveProject = target;
                liveStatus = "V20 已读工程";
                ResetCpus();
                cpu.Items.AddRange(result.Cpus.Cast<object>().ToArray());
                if (cpu.Items.Count == 2) cpu.SelectedIndex = 1;
                Display(result.Snapshot);
                assistant.Text = target.Path + "\r\n" + (result.Cpus.Count > 1 ? "请选择目标 PLC。" : result.Cpus.Count == 0 ? "未枚举到 PLC。" : "已预选唯一 PLC。") +
                    "\r\n当前为元数据只读。离线操作从已关闭工程的独立副本开始。";
            }, outcome: ReceiptPhase, ended: error => { mcpStatus = "MCP 连接异常"; liveStatus = "TIA 待确认"; });
        }

        private string[] SelectExports()
        {
            using (var dialog = new OpenFileDialog { Title = "选择工程导出文件", Multiselect = true, CheckFileExists = true, Filter = "工程导出 (*.aml;*.xml;*.scl;*.udt;*.db;*.csv)|*.aml;*.xml;*.scl;*.udt;*.db;*.csv" })
            {
                var project = projects == null ? null : projects.Current;
                if (project != null && Directory.Exists(project.Root)) dialog.InitialDirectory = project.Root;
                return dialog.ShowDialog(FindForm()) == DialogResult.OK ? dialog.FileNames : null;
            }
        }
        private void ChooseExports()
        {
            var files = SelectExports(); if (files == null) return;
            LoadExports(files);
        }
        public void LoadExports(string[] files) { LoadExports(files, false); }

        private void LoadExports(string[] files, bool attach, bool bind = false)
        {
            Work("读取工程导出文件", t => ReadProgrammingExports(files, t), value =>
            {
                InvalidateProgrammingTarget("工程资料已切换。");
                liveProject = null;
                programmingSource = value.Item2;
                inspected = null; apFile = null; lastAiAnalysis = ""; lastNativeValidation = ""; ResetCpus(); Display(value.Item1);
                if (attach)
                {
                    useProjectContext.Checked = true;
                    FocusRequirement();
                }
            }, keepPage: attach, after: value => { if (bind) ConfirmTargetSelection(); });
        }
        private void ChooseProject() { OpenProgrammingProject(false); }

        private void OpenProgrammingProject(bool bind)
        {
            using (var dialog = new OpenFileDialog { Title = "选择关闭的独立 V20 离线工程或归档", Filter = "TIA Portal V20 (*.ap20;*.zap20)|*.ap20;*.zap20", CheckFileExists = true })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                string path = PlcEngineering.FullLocalPath(dialog.FileName);
                if (!string.Equals(Path.GetExtension(path), ".ap20", StringComparison.OrdinalIgnoreCase))
                {
                    Failed("请选择独立 .ap20 工程。若文件为 .zap20 归档，请先在 Siemens TIA Portal V20 恢复。\r\n" + path);
                    return;
                }
                var job = new TiaJob { ProjectFile = path, ProjectId = ProjectId() };
                Work("生成隔离副本并读取", t => TiaOpenness.Run(job, t), result =>
                {
                    ShowReceipt(result);
                    if (result.Error.Length != 0 || result.Snapshot == null) return;
                    InvalidateProgrammingTarget("原生工程目标已切换。");
                    lastNativeValidation = "";
                    liveProject = null;
                    inspected = result; apFile = path; lastAiAnalysis = ""; SetNativeProgrammingSource(path, result); ResetCpus(); cpu.Items.AddRange(result.Cpus.Cast<object>().ToArray());
                    if (cpu.Items.Count == 2) cpu.SelectedIndex = 1;
                    Display(result.Snapshot);
                }, outcome: ReceiptPhase, after: result => { if (bind && result.Snapshot != null) ConfirmTargetSelection(); });
            }
        }
        private void ResetCpus() { cpu.Items.Clear(); cpu.Items.Add("选择工程 PLC"); cpu.SelectedIndex = 0; }
        private void ReadBlockContent()
        {
            if (inspected == null || NativeFingerprint().Length == 0) throw new InvalidOperationException("请先只读打开一个 V20 工程副本。");
            if (MessageBox.Show(this, "在另一个独立副本尝试读取可导出的程序块正文，供本地查看。\r\n是否继续？",
                "授权读取块正文", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var job = new TiaJob { ProjectFile = apFile, ExpectedSource = NativeFingerprint(), ReadBlockContent = true, ProjectId = ProjectId() };
            Work("在独立副本读取块正文", token => TiaOpenness.Run(job, token), result =>
            {
                ShowReceipt(result);
                if (result.Error.Length != 0 || result.Snapshot == null) return;
                InvalidateProgrammingTarget("已重新读取原生工程及源码，请重新确认当前目标。");
                inspected = result; lastAiAnalysis = "";
                SetNativeProgrammingSource(apFile, result);
                Display(result.Snapshot);
            }, outcome: ReceiptPhase);
        }
        private void Display(PlcSnapshot value)
        {
            snapshot = value; selected = null; voiceAccess.Checked = false; useProjectContext.Checked = false; RefreshHeading();
            RefreshTree();
            referenceGraph.Items.Clear();
            analysis.Text = value.Report();
            assistant.Text = "已读取 " + value.Objects.Count + " 个工程对象。可在工程树选择对象，或绑定 PLC 后提交需求。";
            DisplayTags();
            FillHardware(value);
            SelectTab(1);
        }
        private void RefreshTree()
        {
            if (snapshot == null) return;
            string query = treeQuery.Text.Trim();
            tree.BeginUpdate(); tree.Nodes.Clear();
            try
            {
                foreach (var group in snapshot.Objects.Where(o => query.Length == 0 ||
                    (o.Identity + " " + o.Source).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).GroupBy(o => Category(o.Kind)))
                {
                    var category = tree.Nodes.Add(group.Key + "  ·  " + group.Count());
                    foreach (var scope in group.GroupBy(x => x.Scope))
                    {
                        var items = scope.ToList();
                        if (scope.Key.Length == 0) PopulateTree(category, items);
                        else
                        {
                            var branch = category.Nodes.Add(scope.Key + "  ·  " + items.Count);
                            branch.Tag = items;
                            branch.ToolTipText = scope.Key;
                            branch.Nodes.Add("展开查看对象");
                            if (query.Length != 0) branch.Expand();
                        }
                    }
                    category.Expand();
                }
            }
            finally { tree.EndUpdate(); }
            RefreshPageStates();
        }
        private static void PopulateTree(TreeNode branch, List<PlcObject> items)
        {
            foreach (var item in items.Take(2000))
                branch.Nodes.Add(new TreeNode(item.Name + "  [" + item.Kind + "]") { Tag = item, ToolTipText = item.Identity + "\n" + item.Source });
            if (items.Count > 2000) branch.Nodes.Add("显示前 2000 项。可用搜索定位其余对象。");
        }
        private static string Category(string kind)
        {
            if (kind == "Device" || kind == "DeviceItem") return "Devices / Hardware";
            if (kind == "Subnet" || kind.StartsWith("Network", StringComparison.Ordinal)) return "Networks";
            if (kind.StartsWith("PlcTag", StringComparison.Ordinal)) return "PLC Tags";
            if (kind == "PlcStruct" || kind == "UDT" || kind == "PlcType") return "Data Types";
            return "PLC Blocks";
        }
        private void ShowObject(PlcObject item, bool stayInAnalysis = false)
        {
            selected = item;
            UpdateWorkbenchContext();
            referenceGraph.Items.Clear();
            if (item == null) return;
            if (!stayInAnalysis && PlcEngineering.IsHardware(item))
            {
                SelectTab(2);
                FocusHardware(item, true);
                return;
            }
            if (snapshot != null)
                foreach (var link in PlcEngineering.ReferenceGraph(snapshot, item))
                    referenceGraph.Items.Add(new ListViewItem(new[] { link.Relationship, link.Symbol,
                        link.Target == null ? "" : link.Target.Identity, link.Evidence }) { Tag = link.Target });
            analysis.Text = Describe(item, 16000);
            var findings = snapshot == null ? new List<PlcDiagnostic>() : snapshot.Diagnostics.Where(issue => issue.Source.Length != 0 &&
                (issue.Source == item.Source || issue.Source == item.Identity)).ToList();
            if (findings.Count != 0)
                analysis.Text = "静态发现  ·  " + findings.Count(i => i.Severity == "Error") + " 错误  ·  " + findings.Count(i => i.Severity == "Warning") + " 警告\r\n" +
                    string.Join("\r\n", findings.Select(issue => issue.ToString())) + "\r\n\r\n" + analysis.Text;
            else analysis.Text = "当前对象暂无可直接匹配的诊断。请结合源码与引用复核。\r\n\r\n" + analysis.Text;
            bool showTagTable = !stayInAnalysis && item.Kind == "PlcTag";
            SelectTab(showTagTable ? 4 : 1);
            if (showTagTable) DisplayTags(item);
        }
        private void OpenSelectedScl()
        {
            if (selected == null || selected.Language != "SCL") throw new InvalidOperationException("请先选择带有 SCL 源文本的程序块。");
            var source = selected.Content;
            var review = PlcEngineering.ReviewScl(source, selected.Source);
            if (review.Objects.Count != 1 || review.Objects[0].Name != selected.Name)
                throw new InvalidOperationException("当前对象没有可用于草稿的完整单块 SCL 源文本。");
            if (mode.SelectedIndex == 0) mode.SelectedIndex = 1;
            if (editor.TextLength == 0 || SameSource(editor.Text, source)) LoadScl(source);
            else
            {
                ShowDraftPreview(source);
                UpdateButtons();
                SetPhase(Phase.WaitingForApproval);
            }
        }
        private void DisplayTags(PlcObject focus = null)
        {
            FillVariableGrid(focus);
            if (focus != null) return;
            var visible = snapshot.Objects.Where(x => x.Kind == "PlcTag").Take(1500).ToList();
            var rows = visible.Select(x => x.Name + "  " + x.DataType + "  " + x.Address + "  · " + x.Content + "  · " + x.Source).ToList();
            tags.Text = string.Join(Environment.NewLine, rows);
            tags.WordWrap = false;
            tags.HideSelection = false;
        }
        private string Describe(PlcObject item, int limit)
        {
            return Limit(item.Identity + "  ·  " + item.Language + "\r\n来源 " + item.Source + "\r\n" + item.Address + " " + item.DataType + "\r\n\r\n" +
                (item.ControlFlow.Length == 0 ? "" : item.ControlFlow + "\r\n\r\n") + item.Interface + "\r\n\r\n" + item.Content + "\r\n\r\n引用线索 · " + string.Join(", ", item.References) +
                "\r\n" + PlcEngineering.Related(snapshot, item), limit);
        }
        private static string Limit(string text, int limit) { return text.Length <= limit ? text : text.Substring(0, limit) + "\r\n（已截断）"; }
        public string ReadForVoice(string name)
        {
            if (!Store.Settings.AiFileAccess || !voiceAccess.Checked || voiceScope.IsCancellationRequested || snapshot == null) throw new InvalidOperationException("请先在博图工作台加载资料并打开本会话的语音查询授权。");
            if (string.IsNullOrWhiteSpace(name)) return Limit(snapshot.Report() + "\r\n对象 · " + string.Join(", ", snapshot.Objects.Take(50).Select(x => x.Name)), 3600);
            var exact = snapshot.Objects.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Identity, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (exact.Count != 1) throw new InvalidOperationException("对象未找到或名称不唯一，请提供完整工程路径。");
            var item = exact[0];
            var root = tree.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text.StartsWith(Category(item.Kind) + "  ·  ", StringComparison.Ordinal));
            var branch = root == null || item.Scope.Length == 0 ? root : root.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.ToolTipText == item.Scope);
            if (branch != null) branch.Expand();
            var node = branch == null ? null : branch.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag == item);
            if (node != null && tree.SelectedNode != node) { tree.SelectedNode = node; node.EnsureVisible(); }
            else ShowObject(item);
            return Describe(item, 4200);
        }
        private void Check()
        {
            if (snapshot == null) throw new InvalidOperationException("请先读取工程或导出资料。");
            output.Text = snapshot.Report();
            SetDiagnostics(snapshot.Diagnostics);
            SelectTab(6);
            OpenOutput(currentDiagnostics.Count == 0 ? 0 : 1);
        }
        private AiProvider ApproveModel(string context, bool scopedContext = false, bool continuingConversation = false)
        {
            if (context.Length != 0 && !Store.Settings.AiFileAccess) throw new InvalidOperationException("请先在文件与隐私中启用 AI 文件访问。");
            var model = providers == null ? null : providers.DefaultProvider;
            if (model == null || !model.Enabled) throw new InvalidOperationException("当前环境未配置默认 AI 服务商。");
            if (!AiEnvironment.Local)
            {
                SetPhase(Phase.WaitingForApproval);
                string amount = scopedContext ? "发送已绑定 PLC 与所选对象的授权资料，最多 15000 字。" : "工程上下文约 " + context.Length + " 字。";
                if (continuingConversation) amount += "\r\n同时发送本会话最近三轮需求、回复及生成草稿。";
                if (MessageBox.Show(FindForm(), "将本次需求和当前范围的资料发送给 " + model.Name + "。\r\n" + amount + "\r\n是否继续？", "发送工程资料", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    SetPhase(Phase.Cancelled);
                    return null;
                }
            }
            return model.Clone();
        }
        private string Context()
        {
            if (snapshot == null) return "";
            string origin = inspected == null ? "来源 · 文件导出\r\n" : "来源 · TIA V20 Openness " + inspected.OpennessVersion + " · 工程 SHA-256 " + NativeFingerprint() + "\r\n";
            return Limit(origin + (selected == null ? "" : Describe(selected, 9000) + "\r\n\r\n") + Limit(snapshot.Report(), 3500) +
                "\r\n工程对象（" + snapshot.Objects.Count + " 项，最多列出 80 项）\r\n" +
                string.Join("\r\n", snapshot.Objects.Take(80).Select(x => x.Identity + " · " + x.Language + " · " + x.Source)), 16000);
        }
        private void Generate() { SubmitRequirement(requirement.Text); }

        private void RepairDraft()
        {
            if (diagnosticDraft == null || !SameSource(diagnosticDraft, editor.Text) || currentDiagnostics.Count == 0)
                throw new InvalidOperationException("请先获取当前草稿的静态诊断或 TIA 编译诊断。");
            string source = editor.Text;
            var evidence = currentDiagnostics.Where(issue => nativeDiagnosticBlock == null || MatchesBlock(issue, nativeDiagnosticBlock)).ToList();
            var model = ApproveModel(source + "\n" + string.Join("\n", evidence.Take(20)));
            if (model == null) return;
            if (mode.SelectedIndex == 0) mode.SelectedIndex = 1;
            RequestPlan("依据工程诊断生成修复草稿", token => TiaPlanning.Repair(model, source, evidence, token));
        }
        private void RequestPlan(string action, Func<CancellationToken, PlcDraft> factory)
        {
            var originalOwner = editorTask;
            Work(action, factory, draft =>
            {
                assistant.Text = draft.Plan();
                AddConversation("AI 工程师", draft.Plan());
                lastAiAnalysis = draft.Plan();
                pendingScl = null;
                reviewedEditor = null;
                pendingTemplateName = null;
                if (draft.Scl.Length == 0) SelectTab(0);
                else if (editor.TextLength == 0 || SameSource(editor.Text, draft.Scl)) LoadScl(draft.Scl, owner: originalOwner);
                else ShowDraftPreview(draft.Scl, owner: originalOwner);
                var review = draft.Scl.Length == 0 ? null : PlcEngineering.ReviewScl(draft.Scl, "SCL 草稿");
                output.Text = "AI 草稿 · " + draft.Kind + " " + draft.Name + "\r\n" + draft.Plan() + "\r\n" +
                    (review == null ? "无 SCL 草稿。" : DraftReview(review));
                if (review != null && pendingScl == null) SetDiagnostics(review.Diagnostics, editor.Text);
            }, keepPage: true, running: Phase.Planning,
                outcome: draft => draft.Kind == "PLAN" || (draft.MissingParameters != null && draft.MissingParameters.Any(text => !string.IsNullOrWhiteSpace(text)))
                    ? Phase.WaitingForInput : pendingScl != null ? Phase.WaitingForApproval : Phase.Completed);
        }
        private void LoadScl(string scl, string approvedTemplateName = null, TiaProgrammingTask owner = null)
        {
            pendingScl = null;
            reviewedEditor = null;
            pendingTemplateName = null;
            templateName = approvedTemplateName;
            previewTask = null;
            editorTask = owner;
            editor.Text = scl;
            Highlight();
            RefreshDiffView();
            SelectTab(0);
        }
        private void ShowDraftPreview(string proposed, string proposedTemplateName = null, TiaProgrammingTask owner = null)
        {
            pendingScl = proposed;
            reviewedEditor = editor.Text;
            pendingTemplateName = proposedTemplateName;
            previewTask = owner;
            diff.Text = "待确认的 SCL 草稿替换\r\n\r\n" + SclEditing.Diff(reviewedEditor, pendingScl);
            SelectTab(5);
        }
        private void ApplyDraft()
        {
            if (pendingScl == null || mode.SelectedIndex == 0) return;
            if (!SameSource(reviewedEditor, editor.Text) || !diff.Text.StartsWith("待确认的 SCL 草稿替换", StringComparison.Ordinal))
            {
                ShowDraftPreview(pendingScl, pendingTemplateName, previewTask);
                assistant.Text = "当前草稿已改变，替换预览已更新。请重新核对后再应用。";
                return;
            }
            LoadScl(pendingScl, pendingTemplateName, previewTask);
            UpdateButtons();
            SetPhase(Phase.Completed);
        }
        private static string DraftReview(PlcSnapshot review)
        {
            return (review.Notes.Count == 0 ? "静态检查 · 未发现当前规则覆盖的问题。" : "静态检查 · 有待复核项。") +
                "\r\n" + review.Report() + "\r\nTIA 编译未执行。";
        }
        private void CheckDraft()
        {
            SetPhase(Phase.Validating);
            var review = PlcEngineering.ReviewScl(editor.Text, "SCL 草稿");
            verifiedDraft = editor.Text;
            verificationStatic = review.Diagnostics.Count == 0 ? "已检查，当前规则未发现问题" : review.Diagnostics.Count + " 项诊断待复核";
            output.Text = DraftReview(review);
            SetDiagnostics(review.Diagnostics, editor.Text);
            diagnosticFilter.SelectedIndex = 0;
            SelectTab(6);
            OpenOutput(currentDiagnostics.Count == 0 ? 0 : 1);
            SetPhase(Phase.Completed);
        }
        private void Explain() { if (selected == null) throw new InvalidOperationException("请选择工程对象。"); RunExplanation("解释当前对象、可见条件和引用线索。", Context(), analysis, 1); }
        private void ExplainHardware() { RunExplanation("根据 IO 清单设计模块分组、地址规划及现场核对项。明确建议和待确认，未获取订货号或固件时留空。" + requirement.Text, Limit(Context() + "\r\n" + tags.Text, 16000), hardware, 2); }
        private void ExplainCompile() { RunExplanation("分析以下编译诊断，建议可审查的修复步骤。", Limit(output.Text, 16000), assistant, 6); }
        private void RunExplanation(string prompt, string context, RichTextBox target, int tab)
        {
            var model = ApproveModel(context); if (model == null) return;
            Work("AI 工程分析", t => TiaPlanning.Explain(model, prompt, context, t), text => { lastAiAnalysis = text; target.Text = text; assistant.Text = text; SelectTab(tab); });
        }
        private void Scale()
        {
            string name = scaleName.Text.Trim();
            string proposed = PlcEngineering.AnalogScale(name);
            if (mode.SelectedIndex == 0) mode.SelectedIndex = 1;
            if (editor.TextLength != 0 && !SameSource(editor.Text, proposed))
            {
                ShowDraftPreview(proposed, name);
                SetPhase(Phase.WaitingForApproval);
            }
            else LoadScl(proposed, name);
            assistant.Text = "模拟量线性缩放 · FB 草稿\r\n\r\n" + PlcEngineering.ScaleTests;
            output.Text = assistant.Text + "\r\n\r\nTIA 编译未执行。";
            UpdateButtons();
        }
        private static bool SameSource(string a, string b) { return a.Replace("\r\n", "\n").Replace('\r', '\n') == b.Replace("\r\n", "\n").Replace('\r', '\n'); }
        private void Highlight()
        {
            int start = editor.SelectionStart, length = editor.SelectionLength;
            editor.SuspendLayout();
            try
            {
                editor.SelectAll(); editor.SelectionColor = Palette.Text;
                const string tokens = @"(?<comment>//[^\r\n]*|\(\*[\s\S]*?\*\))|(?<literal>""(?:""""|[^""\r\n])*""|'(?:''|[^'\r\n])*')|(?<number>\b\d+(?:\.\d+)?\b)|(?<type>\b(?:BOOL|BYTE|WORD|DWORD|SINT|INT|DINT|UINT|UDINT|REAL|LREAL|TIME|STRING|ARRAY|STRUCT)\b)|\b(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK|TYPE|END_[A-Z_]+|VAR(?:_[A-Z_]+)?|BEGIN|IF|THEN|ELSIF|ELSE|CASE|OF|FOR|WHILE|REPEAT|UNTIL|DO|TO|RETURN|TRUE|FALSE|OR|AND|NOT)\b";
                foreach (Match match in Regex.Matches(editor.Text, tokens, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    editor.Select(match.Index, match.Length);
                    editor.SelectionColor = match.Groups["comment"].Success ? TiaWorkbenchStyle.Success(Palette) :
                        match.Groups["literal"].Success || match.Groups["number"].Success ? TiaWorkbenchStyle.Warning(Palette) :
                        match.Groups["type"].Success ? Palette.Accent : Palette.IsLight ? Color.FromArgb(100, 65, 160) : Color.FromArgb(205, 169, 249);
                }
                editor.Select(start, length);
            }
            finally { editor.ResumeLayout(); }
        }
        private void Remember() { if (snapshot == null) throw new InvalidOperationException("请先读取工程。"); baseline = snapshot; diff.Text = "已设置基线 · " + baseline.Name; }
        private void Compare()
        {
            var current = baseline ?? snapshot; if (current == null) throw new InvalidOperationException("请先读取并设置对比基线。");
            var files = SelectExports(); if (files == null) return;
            Work("比较工程对象、接口及地址", t => PlcEngineering.Diff(current, PlcEngineering.ReadExports(files, t)), text => { diff.Text = text; assistant.Text = "差异报告已生成。"; SelectTab(5); });
        }
        private void CompareProject()
        {
            var current = baseline ?? snapshot;
            if (current == null) throw new InvalidOperationException("请先读取并设置对比基线。");
            using (var dialog = new OpenFileDialog { Filter = "TIA Portal V20 工程 (*.ap20)|*.ap20", CheckFileExists = true })
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                var job = new TiaJob { ProjectFile = dialog.FileName, ProjectId = ProjectId() };
                Work("读取另一版本的独立工程副本", t =>
                {
                    var result = TiaOpenness.Run(job, t);
                    if (result.Error.Length != 0) throw new InvalidOperationException(result.Error);
                    return PlcEngineering.Diff(current, result.Snapshot);
                }, text => { diff.Text = text; assistant.Text = "差异报告已生成。"; SelectTab(5); });
            }
        }
        private void PlanIo()
        {
            if (snapshot == null) throw new InvalidOperationException("请先读取变量清单。");
            var source = snapshot; int input = (int)inputStart.Value, output = (int)outputStart.Value;
            Work("生成不覆盖已用地址的 IO 建议", t => PlcEngineering.PlanIo(source, input, output), plan => { tags.Text = plan; assistant.Text = "已生成建议地址。请按本机目录和实际槽位复核。"; SelectTab(4); });
        }
        private void Catalog()
        {
            var job = new TiaJob { Operation = "catalog", CatalogQuery = catalogQuery.Text.Trim(), ProjectId = ProjectId() };
            Work("查询 V20 硬件目录", t => TiaOpenness.Run(job, t), result => { ShowReceipt(result); hardware.Text = result.Error.Length == 0 ? result.Report : result.Error; SelectTab(2); }, outcome: ReceiptPhase);
        }
        private void Compile(bool add)
        {
            if (mode.SelectedIndex == 0 || add && mode.SelectedIndex != 2) return;
            if (!AllowsNativeCompilation(add)) throw new InvalidOperationException("请重新核对工程版本、目标 PLC、批准范围和待执行草稿。");
            string target = Convert.ToString(cpu.SelectedItem);
            var job = add ? TiaOpenness.PrepareSclInsertion(apFile, target, NativeFingerprint(), editor.Text, ProjectId()) :
                new TiaJob { Operation = "compile", ProjectFile = apFile, Cpu = target, ExpectedSource = NativeFingerprint(), ProjectId = ProjectId() };
            if (add && programmingTarget.Scope != "New" + job.BlockKind)
                throw new InvalidOperationException("SCL 块类型与绑定范围不一致。");
            string summary = "工程 " + snapshot.Name + "\r\n路径 " + apFile + "\r\nPLC " + target + "\r\n" + (add ? "新增单个 " + job.BlockKind + " " + job.Block + "，成功编译和复读后保存独立副本。\r\nSCL SHA-256 " + job.ApprovedHash : "在新副本离线编译并读取诊断。") + "\r\n将先保留原文件快照。";
            string sourceDiff = add ? SclEditing.Diff("", job.Source) : "";
            if (add) diff.Text = "待批准的新增 SCL 块差异\r\n\r\n" + sourceDiff;
            SetPhase(Phase.WaitingForApproval);
            bool approved = add ? ConfirmInsertion(summary, sourceDiff) : MessageBox.Show(this, summary, "批准离线编译", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (!approved)
            {
                SetPhase(Phase.Cancelled);
                return;
            }
            if (add) diff.Text = "已批准的新增 SCL 块差异\r\n\r\n" + sourceDiff;
            var task = add ? editorTask : null;
            var records = task == null ? null : ProgrammingStore();
            if (task != null)
            {
                records.Step(task, TiaProgrammingState.APPLYING_TO_COPY, "用户已批准新增 SCL 块。正在核对源哈希并启动独立副本作业。", "等待 Siemens 工作进程返回实际回执。");
                ShowProgrammingTask(records, task);
            }
            Work(add ? "副本新增与离线编译" : "离线编译", t => TiaOpenness.Run(job, t), result =>
            {
                if (task != null) RecordNativeProgrammingResult(records, task, result);
                if (result.Verified && result.Saved && result.Snapshot != null && result.SavedCopyHash.Length != 0)
                {
                    InvalidateProgrammingTarget("已保存新副本，复用前需要重新绑定其版本。");
                    inspected = result; apFile = result.Copy;
                    SetNativeProgrammingSource(apFile, result);
                    Display(result.Snapshot);
                }
                ShowReceipt(result, job);
            }, running: Phase.Validating, outcome: result => result.Compiled && (!add || result.Saved && result.Verified) && result.Error.Length == 0 ? Phase.Completed : Phase.Failed,
                ended: error => { if (task != null) RecordProgrammingError(records, task, error, "检查 Siemens 权限和独立副本，重新确认后再操作。"); });
        }

        private string NativeFingerprint() { return inspected == null ? "" : inspected.Saved ? inspected.SavedCopyHash : inspected.SourceHash; }

        private bool ConfirmInsertion(string summary, string sourceDiff)
        {
            using (var review = new Form { Text = "核对差异并批准离线新增 SCL 块", StartPosition = FormStartPosition.CenterParent, Size = new Size(820, 560),
                MinimumSize = new Size(480, 340), ShowInTaskbar = false })
            {
                var text = TextArea("批准前的 SCL 差异");
                text.Text = summary + "\r\n\r\n" + sourceDiff;
                text.BackColor = Palette.CardBg; text.ForeColor = Palette.Text;
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = TiaWorkbenchStyle.Pixels(this, 48), FlowDirection = FlowDirection.RightToLeft };
                var cancelReview = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.Add(cancelReview);
                buttons.Controls.Add(new Button { Text = "批准并编译", DialogResult = DialogResult.Yes, AutoSize = true });
                review.Controls.Add(text); review.Controls.Add(buttons);
                review.CancelButton = cancelReview;
                review.Shown += delegate { cancelReview.Focus(); };
                return review.ShowDialog(this) == DialogResult.Yes;
            }
        }
        private static Phase ReceiptPhase(TiaResult result) { return result.Error.Length == 0 ? Phase.Completed : Phase.Failed; }
        private void RestoreBackup()
        {
            var root = PlcEngineering.FullLocalPath(Path.Combine(AppPaths.DataDir, "tia-jobs"));
            if (!Directory.Exists(root)) throw new InvalidOperationException("当前资料目录还没有 TIA 作业快照。");
            string path;
            using (var dialog = new OpenFileDialog
            {
                Title = "选择 TIA 作业的完成回执或写入检查点", InitialDirectory = root,
                Filter = "TIA 作业回执 (result.json;write-started.json)|result.json;write-started.json", CheckFileExists = true
            })
            {
                if (savedJob != null) dialog.FileName = RecoveryReceiptPath(savedJob);
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            SetPhase(Phase.WaitingForApproval);
            if (MessageBox.Show(this, "根据以下作业回执验证原始快照并生成新工程副本。\r\n\r\n" + path,
                "恢复原始工程快照", MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK)
            {
                SetPhase(Phase.Cancelled);
                return;
            }
            Work("恢复已验证的工程快照", token =>
            {
                var job = TiaOpenness.ReadRecoveryReceipt(path);
                var project = TiaOpenness.RestoreSnapshot(job, token);
                return "快照清单验证通过\r\nSHA-256 " + job.SourceHash + "\r\n\r\n恢复工程 · " + project;
            }, report =>
            {
                output.Text = report;
                assistant.Text = "已生成恢复副本，并将回执保存在原作业目录。可重新选择该工程读取核对。";
                SelectTab(6);
            });
        }
        private void ShowReceipt(TiaResult result, TiaJob job = null)
        {
            if (result.Error.Length != 0) lastWorkbenchError = result.Error;
            if (result.McpAudit.Length != 0) mcpStatus = "MCP 已响应";
            if (job != null)
            {
                verificationCompile = result.Error.Length == 0 ? "TIA 已返回编译回执" : "失败 · " + result.Error;
                if (result.CompileFinishedUtc.Length != 0) lastNativeValidation = result.CompileFinishedUtc;
            }
            if (result.Saved || result.WriteStarted) savedJob = result;
            output.Text = (result.Error.Length == 0 ? "" : "失败 · " + result.Error + "\r\n") + result.Report + "\r\n\r\n工作副本 · " + result.Copy;
            if (result.OpennessVersion.Length != 0) output.AppendText("\r\nOpenness · " + result.OpennessVersion);
            if (result.McpAudit.Length != 0) output.AppendText("\r\nMCP 审计 · " + result.McpAudit);
            if (result.Copy.Length != 0) output.AppendText("\r\n作业回执 · " + RecoveryReceiptPath(result));
            bool canRepair = job != null && job.Operation == "scl" && result.Diagnostics.Any(issue => MatchesBlock(issue, job.Block));
            bool compiled = job != null && result.CompileStartedUtc.Length != 0;
            SetDiagnostics(result.Diagnostics, canRepair ? job.Source : null, canRepair ? job.Block : null,
                compiled ? "TIA 离线编译 · " + result.CompileState : result.Diagnostics.Count != 0 ? "Openness 工程操作" : null);
            if (compiled && !result.Compiled && currentDiagnostics.Any(issue => issue.Severity == "Error")) diagnosticFilter.SelectedIndex = 1;
            else diagnosticFilter.SelectedIndex = 0;
            OpenOutput(currentDiagnostics.Count == 0 ? 0 : 1);
            assistant.Text = result.Error.Length == 0 ? "回执已保存在本地工程作业目录。\r\n" + (result.Saved ? "副本已保存。" : "请查看诊断和工作副本状态。") : result.Error;
            if (result.Error.Length != 0) Store.Log("warn", "engineering", "TIA · " + result.Error.Split('\n')[0]);
            SelectTab(6);
        }
        private static string RecoveryReceiptPath(TiaResult result) { return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(result.Copy)), "result.json"); }
        private string ProjectId() { var p = projects == null ? null : projects.Current; return p == null ? "" : p.Id; }
        private string ProjectLabel() { var id = ProjectId(); return id.Length == 0 ? "" : " · 项目 " + id; }
        private void AddReviewTask()
        {
            if (snapshot == null) throw new InvalidOperationException("请先打开工程资料。");
            var task = new TaskItem { Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10), Title = "复核 TIA 工程 · " + snapshot.Name, Project = ProjectId(), Notes = Limit(output.Text, 4000), CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            if (MessageBox.Show(this, task.Title, "添加工程待办", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            Store.Tasks.Add(task);
            try { Store.SaveTasksOnly(); }
            catch { Store.Tasks.Remove(task); throw; }
            Store.Log("info", "engineering", "TIA · 已添加工程待办" + ProjectLabel()); assistant.Text = "已添加到 DeskBox 待办。";
        }
        private void ExportScl() { if (mode.SelectedIndex != 0 && editor.TextLength > 0) Save("SCL 源草稿 (*.scl)|*.scl", (templateName ?? "PLC-Draft") + ".scl", editor.Text); }
        private void ExportReport()
        {
            var report = "# 博图 AI 工程师\r\n\r\n" + (snapshot == null ? "" : snapshot.Report()) + "\r\n## AI 分析与草稿记录\r\n" + lastAiAnalysis + "\r\n## 差异\r\n" + diff.Text + "\r\n## 验证\r\n" + output.Text;
            Save("Markdown 报告 (*.md)|*.md", "DeskBox-TIA-Review.md", report);
        }
        private void Save(string filter, string name, string content)
        {
            using (var dialog = new SaveFileDialog { Filter = filter, FileName = name, OverwritePrompt = true })
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    string path = PlcEngineering.FullLocalPath(dialog.FileName);
                    if (File.Exists(path)) throw new IOException("为保留原始资料，请使用新文件名。");
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(content);
                    Store.Log("info", "engineering", "TIA · 已导出 " + Path.GetFileName(path) + ProjectLabel());
                }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (operation != null) operation.Cancel();
                workbenchTips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
