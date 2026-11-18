using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed class TiaEngineerView : ViewBase
    {
        private readonly ProviderStore providers;
        private readonly WorkspaceStore projects;
        private readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, AccessibleName = "TIA 工程对象" };
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = "操作模式" };
        private readonly ComboBox cpu = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 275, AccessibleName = "目标 PLC" };
        private readonly RichTextBox requirement, editor, analysis, hardware, topology, tags, diff, output, assistant;
        private readonly TextBox scaleName = new TextBox { Text = "FB_AnalogScale", Width = 128, AccessibleName = "缩放块名称" };
        private readonly TextBox catalogQuery = new TextBox { Width = 140, AccessibleName = "硬件目录型号或订货号" };
        private readonly NumericUpDown inputStart = new NumericUpDown { Maximum = 65535, Width = 70, AccessibleName = "输入起始字节" };
        private readonly NumericUpDown outputStart = new NumericUpDown { Maximum = 65535, Width = 70, AccessibleName = "输出起始字节" };
        private readonly CheckBox voiceAccess = new CheckBox { Text = "允许语音查询所选对象", AutoSize = true };
        private readonly List<DarkButton> actions = new List<DarkButton>();
        private readonly DarkButton cancel;
        private readonly Panel side;
        private PlcSnapshot snapshot, baseline;
        private PlcObject selected;
        private TiaResult inspected;
        private string apFile, template, templateName;
        private CancellationTokenSource operation;
        private CancellationToken voiceScope;

        public TiaEngineerView(Store store, ProviderStore providers, WorkspaceStore projects) : base(store, "博图 AI 工程师", "只读模式 · TIA Portal、SCL 与离线工程验证")
        {
            this.providers = providers; this.projects = projects;
            Name = "tia-engineer";
            mode.Items.AddRange(new object[] { "只读分析", "辅助设计", "自动离线工程" }); mode.SelectedIndex = 0;
            mode.SelectedIndexChanged += delegate { RefreshHeading(); UpdateButtons(); };
            cpu.Items.Add("选择工程 PLC"); cpu.SelectedIndex = 0;
            var top = Bar();
            top.Controls.AddRange(new Control[] { mode, Button("检测环境", Inspect), Button("打开导出资料", ChooseExports), Button("读取 .ap20 副本", ChooseProject), Button("导出报告", ExportReport) });
            cancel = new DarkButton("取消当前任务") { Width = 116, Height = 32, Enabled = false, Palette = Palette };
            cancel.Click += delegate { if (operation != null) operation.Cancel(); }; top.Controls.Add(cancel);

            requirement = TextArea("工程需求", false); requirement.Text = "";
            editor = TextArea("SCL 草稿", false); editor.WordWrap = false; editor.TextChanged += delegate { UpdateButtons(); };
            analysis = TextArea("程序分析"); hardware = TextArea("硬件目录及建议"); topology = TextArea("网络与设备");
            tags = TextArea("变量与 IO"); diff = TextArea("工程差异"); output = TextArea("编译和操作日志"); assistant = TextArea("AI 工程助手");
            var programming = Page("AI 编程", editor, Button("AI 任务计划", Generate), scaleName, Button("缩放模板", Scale), Button("高亮", Highlight), Button("保存 SCL", ExportScl));
            var entry = new Panel { Dock = DockStyle.Top, Height = 98, Padding = new Padding(0, 4, 0, 4) };
            entry.Controls.Add(requirement); var entryTitle = Caption("工程需求、引用资料及待确认条件"); entry.Controls.Add(entryTitle);
            programming.Controls.Add(entry); programming.Controls.SetChildIndex(entry, 1);
            Page("程序分析", analysis, Button("解释所选对象", Explain), Button("检查工程", Check));
            Page("硬件组态", hardware, catalogQuery, Button("查本机型号", Catalog), Button("AI 硬件建议", ExplainHardware));
            Page("网络拓扑", topology);
            Page("变量管理", tags, Button("检查 IO", Check), new Label { Text = "输入 I 起点", AutoSize = true }, inputStart, new Label { Text = "输出 Q 起点", AutoSize = true }, outputStart, Button("生成地址建议", PlanIo));
            Page("工程对比", diff, Button("设为基线", Remember), Button("对比另一批文件", Compare));
            Page("测试验证", output, cpu, Button("编译副本", delegate { Compile(false); }), Button("批准新增模板并编译", delegate { Compile(true); }), Button("加入待办", AddReviewTask), Button("解释编译诊断", ExplainCompile));
            var left = new Panel { Dock = DockStyle.Left, Width = 240, Padding = new Padding(0, 0, 8, 0) };
            left.Controls.Add(tree); left.Controls.Add(Caption("工程浏览器"));
            side = new Panel { Dock = DockStyle.Right, Width = 320, Padding = new Padding(8, 0, 0, 0) };
            side.Controls.Add(assistant); side.Controls.Add(Caption("工程助手 · 执行与复核")); side.Controls.Add(voiceAccess);
            voiceAccess.Dock = DockStyle.Bottom;
            voiceAccess.CheckedChanged += delegate { if (voiceAccess.Checked) voiceScope = AiEnvironment.Token; };
            var center = new Panel { Dock = DockStyle.Fill };
            center.Controls.Add(tabs); center.Controls.Add(side); center.Controls.Add(left);
            center.Resize += delegate
            {
                bool wide = center.Width >= Dpi.D(this, 1050);
                left.Width = Dpi.D(this, wide ? 260 : 180);
                side.Dock = wide ? DockStyle.Right : DockStyle.Bottom;
                if (wide) side.Width = Dpi.D(this, 340);
                else side.Height = Dpi.D(this, 132);
            };
            tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabs.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                bool activeTab = e.Index == tabs.SelectedIndex;
                using (var fill = new SolidBrush(activeTab ? Palette.PanelAlt : Palette.HeaderBg)) e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, e.Bounds, activeTab ? Palette.Accent : Palette.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            };
            FormatCombo(mode); FormatCombo(cpu);
            Body.Controls.Add(center); Body.Controls.Add(top);
            tree.AfterSelect += delegate(object sender, TreeViewEventArgs e) { ShowObject(e.Node == null ? null : e.Node.Tag as PlcObject); };
            assistant.Text = "选择已导出的 SimaticML、SCL 或 IO CSV，或读取本机 .ap20 工程副本。\r\n\r\n默认只读。工程写入需要明确审批及本机 V20。";
            output.Text = "当前没有编译回执。";
            ApplyPalette(Palette);
            UpdateButtons();
        }

        private FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 5) }; }
        private Label Caption(string text) { return new Label { Text = text, Dock = DockStyle.Top, Height = 24, ForeColor = Palette.TextDim, Font = Font }; }
        private RichTextBox TextArea(string name, bool readOnly = true)
        {
            return new RichTextBox { Dock = DockStyle.Fill, ReadOnly = readOnly, AccessibleName = name, BorderStyle = BorderStyle.None, DetectUrls = false, Font = new Font(readOnly ? "Microsoft YaHei UI" : "Consolas", 10), MaxLength = 24000 };
        }
        private DarkButton Button(string label, Action action)
        {
            var button = new DarkButton(label) { Height = 32, Palette = Palette, AccessibleName = label };
            button.Width = Math.Max(90, TextRenderer.MeasureText(label, button.Font).Width + 24);
            button.Click += delegate { try { action(); } catch (Exception e) { Failed(e.Message); } };
            actions.Add(button); return button;
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
                TextRenderer.DrawText(e.Graphics, Convert.ToString(box.Items[e.Index]), box.Font, e.Bounds, Palette.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                e.DrawFocusRectangle();
            };
        }
        private TabPage Page(string title, Control target, params Control[] buttons)
        {
            var page = new TabPage(title) { Padding = new Padding(4) };
            page.Controls.Add(target);
            if (buttons.Length > 0) { var bar = Bar(); bar.Controls.AddRange(buttons); page.Controls.Add(bar); }
            tabs.TabPages.Add(page); return page;
        }
        private void SelectTab(int index) { tabs.SelectedIndex = index; }
        private void UpdateButtons()
        {
            if (actions == null || cancel == null || editor == null) return;
            foreach (DarkButton b in actions)
                b.Enabled = operation == null && (mode.SelectedIndex > 0 || !new[] { "AI 任务计划", "缩放模板", "保存 SCL", "AI 硬件建议", "生成地址建议", "编译副本", "批准新增模板并编译" }.Contains(b.Text)) &&
                    (b.Text != "批准新增模板并编译" || mode.SelectedIndex == 2 && template != null && SameSource(template, editor.Text));
            cancel.Enabled = operation != null;
            mode.Enabled = operation == null; cpu.Enabled = operation == null;
            editor.ReadOnly = operation != null || mode.SelectedIndex == 0;
        }
        private void RefreshHeading()
        {
            var provider = providers == null ? null : providers.DefaultProvider;
            HintLabel.Text = Convert.ToString(mode.SelectedItem) + " · " + (snapshot == null ? "未加载工程" : snapshot.Name + " / " + snapshot.Origin) + " · " +
                (provider == null || !provider.Enabled ? "AI 未配置" : "AI " + provider.Name + " / " + provider.Model);
        }
        protected override void OnActivated() { base.OnActivated(); RefreshHeading(); }
        public override void ApplyPalette(Palette palette)
        {
            base.ApplyPalette(palette);
            if (assistant == null) return;
            foreach (var box in new[] { requirement, editor, analysis, hardware, topology, tags, diff, output, assistant })
            { box.BackColor = palette.CardBg; box.ForeColor = palette.Text; }
            tree.BackColor = palette.CardBg; tree.ForeColor = palette.Text; tree.LineColor = palette.Line;
            foreach (TabPage page in tabs.TabPages) page.BackColor = palette.Bg;
            foreach (Control control in new Control[] { mode, cpu, scaleName, catalogQuery, voiceAccess }) { control.BackColor = palette.CardBg; control.ForeColor = palette.Text; }
        }
        private async void Work<T>(string action, Func<CancellationToken, T> work, Action<T> display)
        {
            if (operation != null) return;
            operation = CancellationTokenSource.CreateLinkedTokenSource(AiEnvironment.Token);
            var active = operation;
            UpdateButtons(); assistant.Text = "正在执行 · " + action;
            Store.Log("info", "engineering", "TIA · 开始 " + action + ProjectLabel());
            try
            {
                var value = await Task.Run(() => work(active.Token), active.Token);
                active.Token.ThrowIfCancellationRequested();
                if (!IsDisposed) display(value);
                Store.Log("info", "engineering", "TIA · 完成 " + action + ProjectLabel());
            }
            catch (OperationCanceledException) { if (!IsDisposed) Failed("任务已取消。已生成的隔离副本和回执可核对。"); }
            catch (Exception e) { if (!IsDisposed) Failed(e.Message); }
            finally { operation = null; active.Dispose(); if (!IsDisposed) UpdateButtons(); }
        }
        private void Failed(string message)
        {
            assistant.Text = message; output.AppendText(Environment.NewLine + message); SelectTab(6);
            Store.Log("warn", "engineering", "TIA · " + message.Split('\n')[0]);
        }
        private void Inspect() { Work("检测本机 TIA", t => TiaEnvironmentInspector.Inspect(), result => { assistant.Text = result.Describe(); output.Text = assistant.Text; SelectTab(6); }); }
        private string[] SelectExports()
        {
            using (var dialog = new OpenFileDialog { Title = "选择工程导出文件", Multiselect = true, CheckFileExists = true, Filter = "工程导出 (*.xml;*.scl;*.udt;*.db;*.csv)|*.xml;*.scl;*.udt;*.db;*.csv" })
            {
                var project = projects == null ? null : projects.Current;
                if (project != null) dialog.InitialDirectory = project.Root;
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
            }
        }
        private void ChooseExports()
        {
            var files = SelectExports(); if (files == null) return;
            LoadExports(files);
        }
        public void LoadExports(string[] files)
        {
            Work("读取工程导出文件", t => PlcEngineering.ReadExports(files, t), value =>
            {
                inspected = null; apFile = null; ResetCpus(); Display(value);
            });
        }
        private void ChooseProject()
        {
            using (var dialog = new OpenFileDialog { Title = "选择关闭的独立 V20 离线工程文件", Filter = "TIA Portal V20 (*.ap20)|*.ap20", CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = dialog.FileName;
                var job = new TiaJob { ProjectFile = path, ProjectId = ProjectId() };
                Work("生成隔离副本并读取", t => TiaOpenness.Run(job, t), result =>
                {
                    ShowReceipt(result);
                    if (result.Error.Length != 0 || result.Snapshot == null) return;
                    inspected = result; apFile = path; ResetCpus(); cpu.Items.AddRange(result.Cpus.Cast<object>().ToArray());
                    if (cpu.Items.Count == 2) cpu.SelectedIndex = 1;
                    Display(result.Snapshot);
                });
            }
        }
        private void ResetCpus() { cpu.Items.Clear(); cpu.Items.Add("选择工程 PLC"); cpu.SelectedIndex = 0; }
        private void Display(PlcSnapshot value)
        {
            snapshot = value; selected = null; voiceAccess.Checked = false; RefreshHeading();
            tree.BeginUpdate(); tree.Nodes.Clear();
            try
            {
                foreach (var group in value.Objects.GroupBy(o => Category(o.Kind)))
                {
                    var category = tree.Nodes.Add(group.Key + "  ·  " + group.Count());
                    foreach (var scope in group.GroupBy(x => x.Scope))
                    {
                        var branch = scope.Key.Length == 0 ? category : category.Nodes.Add(scope.Key);
                        foreach (var item in scope.Take(2000))
                            branch.Nodes.Add(new TreeNode(item.Name + "  [" + item.Kind + "]") { Tag = item, ToolTipText = item.Source });
                        if (scope.Count() > 2000) branch.Nodes.Add("仅显示前 2000 项，报告仍检查全部对象");
                    }
                    category.Expand();
                }
            }
            finally { tree.EndUpdate(); }
            analysis.Text = value.Report(); assistant.Text = analysis.Text;
            tags.Text = string.Join(Environment.NewLine, value.Objects.Where(x => x.Kind == "PlcTag").Take(1500).Select(x => x.Name + "  " + x.DataType + "  " + x.Address + "  · " + x.Content + "  · " + x.Source));
            var devices = value.Objects.Where(o => o.Kind == "Device" || o.Kind == "DeviceItem").ToList();
            hardware.Text = devices.Count == 0 ? "未取得设备组态。可提供 IO CSV，通过本机硬件目录核对订货号和固件。" : string.Join(Environment.NewLine, devices.Select(x => x.Scope + " / " + x.Name + " · " + x.Content));
            topology.Text = string.Join(Environment.NewLine, value.Objects.Where(x => x.Kind == "Subnet").Select(x => x.Name + " · " + x.Content)) + "\r\n\r\n设备层级\r\n" + hardware.Text + "\r\n\r\n端口连接与网络链路需通过源工程复核。";
            SelectTab(1);
        }
        private static string Category(string kind)
        {
            if (kind == "Device" || kind == "DeviceItem") return "Devices / Hardware";
            if (kind == "Subnet") return "Networks";
            if (kind.StartsWith("PlcTag", StringComparison.Ordinal)) return "PLC Tags";
            if (kind == "PlcStruct" || kind == "UDT" || kind == "PlcType") return "Data Types";
            return "PLC Blocks";
        }
        private void ShowObject(PlcObject item)
        {
            selected = item; if (item == null) return;
            analysis.Text = Describe(item, 16000);
            SelectTab(item.Kind == "PlcTag" ? 4 : 1);
        }
        private static string Describe(PlcObject item, int limit)
        {
            return Limit(item.Identity + "  ·  " + item.Language + "\r\n来源 " + item.Source + "\r\n" + item.Address + " " + item.DataType + "\r\n\r\n" + item.Interface + "\r\n\r\n" + item.Content + "\r\n\r\n引用线索 · " + string.Join(", ", item.References), limit);
        }
        private static string Limit(string text, int limit) { return text.Length <= limit ? text : text.Substring(0, limit) + "\r\n（已截断）"; }
        public string ReadForVoice(string name)
        {
            if (!Store.Settings.AiFileAccess || !voiceAccess.Checked || voiceScope.IsCancellationRequested || snapshot == null) throw new InvalidOperationException("请先在博图工作台加载资料并打开本会话的语音查询授权。");
            if (string.IsNullOrWhiteSpace(name)) return Limit(snapshot.Report() + "\r\n对象 · " + string.Join(", ", snapshot.Objects.Take(50).Select(x => x.Name)), 3600);
            var exact = snapshot.Objects.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Identity, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
            if (exact.Count != 1) throw new InvalidOperationException("对象未找到或名称不唯一，请提供完整工程路径。");
            ShowObject(exact[0]);
            return Describe(exact[0], 4200);
        }
        private void Check() { if (snapshot == null) throw new InvalidOperationException("请先读取工程或导出资料。"); output.Text = snapshot.Report(); SelectTab(6); }
        private AiProvider ApproveModel(string context)
        {
            if (context.Length != 0 && !Store.Settings.AiFileAccess) throw new InvalidOperationException("请先在文件与隐私中启用 AI 文件访问。");
            var model = providers == null ? null : providers.DefaultProvider;
            if (model == null || !model.Enabled) throw new InvalidOperationException("当前环境未配置默认 AI 服务商。");
            if (!AiEnvironment.Local && MessageBox.Show(this, "将本次需求和当前范围的资料发送给 " + model.Name + "。\r\n工程上下文约 " + context.Length + " 字。是否继续？", "发送工程资料", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return null;
            return model.Clone();
        }
        private string Context()
        {
            if (snapshot == null) return "";
            return Limit(snapshot.Report() + "\r\n工程对象（最多 80 项）\r\n" + string.Join("\r\n", snapshot.Objects.Take(80).Select(x => x.Identity + " · " + x.Language + " · " + x.Source)) + "\r\n" + (selected == null ? "" : Describe(selected, 10000)), 16000);
        }
        private void Generate()
        {
            if (mode.SelectedIndex == 0) return;
            string context = Context(), prompt = requirement.Text; var model = ApproveModel(context); if (model == null) return;
            Work("生成结构化工程任务与草稿", t => TiaPlanning.Plan(model, prompt, context, t), draft =>
            {
                template = null; editor.Text = draft.Scl; Highlight(); assistant.Text = draft.Plan(); SelectTab(0);
                output.Text = "AI 草稿 · " + draft.Kind + " " + draft.Name + "\r\n" + draft.Plan() + "\r\nTIA 编译未执行。";
            });
        }
        private void Explain() { if (selected == null) throw new InvalidOperationException("请选择工程对象。"); RunExplanation("解释当前对象、可见条件和引用线索。", Context(), analysis, 1); }
        private void ExplainHardware() { RunExplanation("根据 IO 清单设计模块分组、地址规划及现场核对项。明确建议和待确认，未获取订货号或固件时留空。" + requirement.Text, Limit(Context() + "\r\n" + tags.Text, 16000), hardware, 2); }
        private void ExplainCompile() { RunExplanation("分析以下编译诊断，建议可审查的修复步骤。", Limit(output.Text, 16000), assistant, 6); }
        private void RunExplanation(string prompt, string context, RichTextBox target, int tab)
        {
            var model = ApproveModel(context); if (model == null) return;
            Work("AI 工程分析", t => TiaPlanning.Explain(model, prompt, context, t), text => { target.Text = text; assistant.Text = text; SelectTab(tab); });
        }
        private void Scale()
        {
            if (mode.SelectedIndex == 0) return;
            templateName = scaleName.Text.Trim(); template = PlcEngineering.AnalogScale(templateName);
            editor.Text = template; Highlight(); assistant.Text = "模拟量线性缩放 · FB 草稿\r\n\r\n" + PlcEngineering.ScaleTests;
            output.Text = assistant.Text + "\r\n\r\nTIA 编译未执行。"; SelectTab(0); UpdateButtons();
        }
        private static bool SameSource(string a, string b) { return a.Replace("\r", "") == b.Replace("\r", ""); }
        private void Highlight()
        {
            int start = editor.SelectionStart, length = editor.SelectionLength;
            editor.SuspendLayout();
            try
            {
                editor.SelectAll(); editor.SelectionColor = Palette.Text;
                foreach (Match match in Regex.Matches(editor.Text, @"(?i)\b(?:FUNCTION_BLOCK|FUNCTION|DATA_BLOCK|ORGANIZATION_BLOCK|TYPE|END_[A-Z_]+|VAR(?:_[A-Z_]+)?|BEGIN|IF|THEN|ELSE|TRUE|FALSE|BOOL|REAL|INT|WORD|OR|AND)\b"))
                { editor.Select(match.Index, match.Length); editor.SelectionColor = Palette.Accent; }
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
        private void PlanIo()
        {
            if (snapshot == null) throw new InvalidOperationException("请先读取变量清单。");
            var source = snapshot; int input = (int)inputStart.Value, output = (int)outputStart.Value;
            Work("生成不覆盖已用地址的 IO 建议", t => PlcEngineering.PlanIo(source, input, output), plan => { tags.Text = plan; assistant.Text = "已生成建议地址。请按本机目录和实际槽位复核。"; SelectTab(4); });
        }
        private void Catalog()
        {
            var job = new TiaJob { Operation = "catalog", CatalogQuery = catalogQuery.Text.Trim(), ProjectId = ProjectId() };
            Work("查询 V20 硬件目录", t => TiaOpenness.Run(job, t), result => { ShowReceipt(result); hardware.Text = result.Error.Length == 0 ? result.Report : result.Error; SelectTab(2); });
        }
        private void Compile(bool add)
        {
            if (mode.SelectedIndex == 0 || add && mode.SelectedIndex != 2) return;
            if (inspected == null || cpu.SelectedIndex < 1) throw new InvalidOperationException("请先读取 V20 工程副本并选择目标 PLC。");
            if (add && (template == null || !SameSource(template, editor.Text))) throw new InvalidOperationException("批准写入只接受未经修改的缩放模板。其他 SCL 草稿可先导出进行审核。");
            var job = new TiaJob { Operation = add ? "scale" : "compile", ProjectFile = apFile, Cpu = Convert.ToString(cpu.SelectedItem), Block = add ? templateName : "",
                ApprovedHash = add ? PlcEngineering.Hash(template) : "", ExpectedSource = inspected.SourceHash, ProjectId = ProjectId() };
            string summary = "工程 " + snapshot.Name + "\r\nPLC " + job.Cpu + "\r\n" + (add ? "新增单个 FB " + templateName + "，禁止覆盖同名对象。成功编译和复读后保存副本。\r\n源 SHA-256 " + job.ApprovedHash : "只在新副本离线编译并读取诊断。") + "\r\n\r\n将先保留原文件快照，再操作独立副本。";
            if (MessageBox.Show(this, summary, "批准离线工程操作", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Work(add ? "副本新增与离线编译" : "离线编译", t => TiaOpenness.Run(job, t), result =>
            {
                ShowReceipt(result);
                if (result.Saved && result.Snapshot != null) Display(result.Snapshot);
                SelectTab(6);
            });
        }
        private void ShowReceipt(TiaResult result)
        {
            output.Text = (result.Error.Length == 0 ? "" : "失败 · " + result.Error + "\r\n") + result.Report + "\r\n\r\n工作副本 · " + result.Copy;
            assistant.Text = result.Error.Length == 0 ? "回执已保存在本地工程作业目录。\r\n" + (result.Saved ? "副本已保存。" : "请查看诊断和工作副本状态。") : result.Error;
            if (result.Error.Length != 0) Store.Log("warn", "engineering", "TIA · " + result.Error.Split('\n')[0]);
            SelectTab(6);
        }
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
            var report = "# 博图 AI 工程师\r\n\r\n" + (snapshot == null ? "" : snapshot.Report()) + "\r\n## 差异\r\n" + diff.Text + "\r\n## 验证\r\n" + output.Text;
            Save("Markdown 报告 (*.md)|*.md", "DeskBox-TIA-Review.md", report);
        }
        private void Save(string filter, string name, string content)
        {
            using (var dialog = new SaveFileDialog { Filter = filter, FileName = name, OverwritePrompt = true })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string path = PlcEngineering.FullLocalPath(dialog.FileName);
                    if (File.Exists(path)) throw new IOException("为保留原始资料，请使用新文件名。");
                    using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(content);
                    Store.Log("info", "engineering", "TIA · 已导出 " + Path.GetFileName(path) + ProjectLabel());
                }
        }
        protected override void Dispose(bool disposing) { if (disposing && operation != null) operation.Cancel(); base.Dispose(disposing); }
    }
}
