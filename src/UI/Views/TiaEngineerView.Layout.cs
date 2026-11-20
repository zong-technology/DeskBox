using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed partial class TiaEngineerView
    {
        private Panel navigationPane, assistantPane, outputPane, outputPages, codeInspector;
        private Splitter navigationDivider, assistantDivider, outputDivider;
        private DarkButton toggleAssistant, toggleTree, toggleOutput;
        private DarkButton[] outputTabs;
        private RichTextBox history, validationSummary, blockDetails;
        private Label projectHeading, connectionStatus, contextDetails, editorStatus, diagnosisCount;
        private Control diagnosticPage;
        private readonly ToolTip workbenchTips = new ToolTip { ShowAlways = true };
        private bool compact, compactAssistant = true, v20Checked, v20Discovered;

        private void BuildWorkbenchLayout(FlowLayoutPanel toolbar, Action configureModel)
        {
            Header.Hide();
            navigationPane = BuildProjectNavigation();
            assistantPane = BuildAssistant(configureModel);
            outputPane = BuildOutputPane();
            navigationDivider = new Splitter { Name = "tia-tree-divider", Dock = DockStyle.Left, Width = TiaWorkbenchStyle.RailGap };
            assistantDivider = new Splitter { Name = "tia-assistant-divider", Dock = DockStyle.Right, Width = TiaWorkbenchStyle.RailGap };
            outputDivider = new Splitter { Name = "tia-log-divider", Dock = DockStyle.Bottom, Height = TiaWorkbenchStyle.RailGap, MinExtra = 205 };
            var workArea = new Panel { Dock = DockStyle.Fill, Name = "tia-work-area" };
            workArea.Controls.AddRange(new Control[] { tabs, outputDivider, outputPane });
            var shell = new Panel { Dock = DockStyle.Fill, Name = "tia-workbench-zones" };
            shell.Controls.AddRange(new Control[] { workArea, assistantDivider, assistantPane, navigationDivider, navigationPane });

            toggleAssistant = Button("收起助手", ToggleAssistant, false);
            toggleAssistant.Name = "tia-toggle-assistant";
            var reset = Button("恢复默认布局", ResetLayout, false);
            reset.Name = "tia-reset-layout";
            toolbar.Controls.AddRange(new Control[] { toggleAssistant, reset });
            FormatCombo(mode);
            FormatCombo(cpu);
            Body.Controls.AddRange(new Control[] { shell, toolbar, BuildContextBar() });
            Body.SizeChanged += delegate { LayoutWorkbench(); };
            navigationDivider.SplitterMoved += delegate
            {
                Store.Settings.TiaTreeWidth = Clamp(LayoutUnits(navigationPane.Width), 154, 420);
                SaveLayout();
            };
            assistantDivider.SplitterMoved += delegate
            {
                if (compact) Store.Settings.TiaAssistantHeight = Clamp(LayoutUnits(assistantPane.Height), 178, 350);
                else Store.Settings.TiaAssistantWidth = Clamp(LayoutUnits(assistantPane.Width), 300, 520);
                SaveLayout();
            };
            outputDivider.SplitterMoved += delegate
            {
                Store.Settings.TiaLogHeight = Clamp(LayoutUnits(outputPane.Height), TiaWorkbenchStyle.MinimumOutputHeight, 300);
                SaveLayout();
            };
            LayoutWorkbench();
        }

        private Control BuildContextBar()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Dpi.D(this, TiaWorkbenchStyle.ContextHeight), Name = "tia-context-bar" };
            var title = Heading("TIA AI 工程师");
            title.Dock = DockStyle.Left;
            title.Width = Dpi.D(this, 132);
            projectHeading = Caption("");
            projectHeading.Name = "tia-project-heading";
            projectHeading.Dock = DockStyle.Fill;
            connectionStatus = Caption("");
            connectionStatus.Name = "tia-connection-status";
            connectionStatus.Dock = DockStyle.Right;
            connectionStatus.Width = Dpi.D(this, 174);
            connectionStatus.TextAlign = ContentAlignment.MiddleRight;
            toggleTree = Button("收起导航", () => { Store.Settings.TiaTreeCollapsed = !Store.Settings.TiaTreeCollapsed; SaveLayout(); }, false);
            toggleTree.Name = "tia-toggle-tree";
            toggleOutput = Button("收起输出", () => { Store.Settings.TiaLogCollapsed = !Store.Settings.TiaLogCollapsed; SaveLayout(); }, false);
            toggleOutput.Name = "tia-toggle-output";
            foreach (var button in new[] { toggleTree, toggleOutput })
            {
                button.Dock = DockStyle.Right;
                button.Width = Dpi.D(this, 82);
            }
            header.Padding = new Padding(Dpi.D(this, DesignTokens.Space8), Dpi.D(this, 5), Dpi.D(this, DesignTokens.Space8), Dpi.D(this, 5));
            header.Controls.AddRange(new Control[] { projectHeading, title, connectionStatus, toggleOutput, toggleTree });
            return header;
        }

        private Panel BuildProjectNavigation()
        {
            var pane = new Panel { Name = "tia-tree-pane", Dock = DockStyle.Left, Width = 260, Padding = new Padding(6, 0, 6, 4) };
            pane.Controls.AddRange(new Control[] { tree, treeQuery, Heading("工程导航") });
            tree.Font = UiKit.CachedFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            tree.ShowNodeToolTips = true;
            tree.BorderStyle = BorderStyle.None;
            treeQuery.Height = Dpi.D(this, TiaWorkbenchStyle.ToolbarHeight);
            tree.BeforeExpand += delegate(object sender, TreeViewCancelEventArgs e)
            {
                var items = e.Node.Tag as System.Collections.Generic.List<PlcObject>;
                if (items == null) return;
                e.Node.Tag = null;
                e.Node.Nodes.Clear();
                PopulateTree(e.Node, items);
            };
            treeQuery.Inner.AccessibleName = "搜索工程对象";
            treeQuery.Inner.TextChanged += delegate { RefreshTree(); };
            treeQuery.KeyPressed += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter || tree.Nodes.Count == 0) return;
                e.SuppressKeyPress = true;
                tree.Focus();
            };
            return pane;
        }

        private Panel BuildAssistant(Action configureModel)
        {
            var pane = new Panel { Name = "tia-assistant-pane", Dock = DockStyle.Right, Width = 350, Padding = new Padding(8, 0, 8, 5) };
            contextDetails = Caption("");
            contextDetails.Name = "tia-context-details";
            contextDetails.Height = Dpi.D(this, 58);
            contextDetails.TextAlign = ContentAlignment.TopLeft;
            contextDetails.Padding = new Padding(6, 5, 4, 4);
            assistant.TabStop = false;
            assistant.Margin = Padding.Empty;
            modelStatus.Height = Dpi.D(this, 24);
            var tools = Bar();
            tools.Dock = DockStyle.Bottom;
            tools.Controls.AddRange(new Control[] { generate, Button("添加工程资料", delegate { var files = SelectExports(); if (files != null) LoadExports(files, true); }),
                Button("保存需求草稿", SaveRequirement), Button("填入示例", FillExample), regenerate });
            if (configureModel != null) tools.Controls.Add(Button("配置 AI 服务商", configureModel));
            var choices = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            choices.Controls.AddRange(new Control[] { useProjectContext, voiceAccess });
            voiceAccess.CheckedChanged += delegate { if (voiceAccess.Checked) voiceScope = DeskBox.Ai.AiEnvironment.Token; };
            var composer = new Panel { Dock = DockStyle.Bottom, Name = "tia-composer" };
            composer.Controls.AddRange(new Control[] { requirement, choices, tools, Heading("工程需求") });
            Action fitComposer = delegate
            {
                int target = Dpi.D(this, compact ? 82 : 108) + tools.Height + choices.Height;
                if (composer.Height != target) composer.Height = target;
            };
            tools.SizeChanged += delegate { fitComposer(); };
            choices.SizeChanged += delegate { fitComposer(); };
            pane.SizeChanged += delegate { fitComposer(); };
            fitComposer();
            pane.Controls.AddRange(new Control[] { assistant, composer, contextDetails, modelStatus, Heading("AI 工程师") });
            requirement.Enter += delegate { requirement.BackColor = UiKit.Mix(Palette.PanelAlt, Palette.Accent, 0.15); };
            requirement.Leave += delegate { requirement.BackColor = Palette.PanelAlt; };
            return pane;
        }

        private void BuildProgrammingPage()
        {
            var page = new TabPage("程序编辑") { Padding = new Padding(5, 4, 5, 4) };
            var toolbar = Bar();
            toolbar.Controls.AddRange(new Control[] { Button("保存 SCL", ExportScl), Button("静态检查草稿", CheckDraft), Button("查找 SCL", FindCode),
                Button("撤销", UndoCode), Button("替换 SCL", ReplaceCode), Button("匹配括号", MatchBracket), Button("高亮", Highlight),
                scaleName, Button("载入缩放模板", Scale) });
            blockDetails = TextArea("程序信息");
            blockDetails.TabStop = false;
            blockDetails.Margin = Padding.Empty;
            codeInspector = new Panel { Dock = DockStyle.Right, Width = Dpi.D(this, 205), Padding = new Padding(6, 0, 0, 0), Name = "tia-block-inspector" };
            codeInspector.Controls.AddRange(new Control[] { blockDetails, Heading("程序信息") });
            var editorBody = new Panel { Dock = DockStyle.Fill };
            editorBody.Controls.AddRange(new[] { BuildCodeEditor(), codeInspector });
            editorStatus = Caption("");
            editorStatus.Dock = DockStyle.Bottom;
            editorStatus.Name = "tia-editor-status";
            editorStatus.Height = Dpi.D(this, 24);
            page.Controls.AddRange(new Control[] { editorBody, editorStatus, toolbar });
            page.SizeChanged += delegate { codeInspector.Visible = page.ClientSize.Width >= Dpi.D(this, 750); };
            codeInspector.Visible = false;
            tabs.TabPages.Add(page);
            editor.SelectionChanged += delegate { UpdateEditorStatus(); };
            editor.TextChanged += delegate { UpdateEditorStatus(); };
            editor.ReadOnlyChanged += delegate { UpdateEditorStatus(); };
            editor.Enter += delegate
            {
                if (!compact || !compactAssistant) return;
                compactAssistant = false;
                LayoutWorkbench();
            };
        }

        private Panel BuildOutputPane()
        {
            var pane = new Panel { Name = "tia-log-pane", Dock = DockStyle.Bottom, Height = 152 };
            var toolbar = Bar();
            toolbar.WrapContents = false;
            toolbar.Padding = new Padding(0, 1, 0, 1);
            toolbar.AutoSize = false;
            toolbar.Height = Dpi.D(this, TiaWorkbenchStyle.ToolbarHeight);
            outputTabs = new[] { "编译结果", "诊断", "操作日志" }.Select((name, index) => Button(name, () => OpenOutput(index), false)).ToArray();
            foreach (var button in outputTabs) { button.Height = Dpi.D(this, 27); toolbar.Controls.Add(button); }
            diagnosisCount = Caption("无诊断记录");
            diagnosisCount.Name = "tia-diagnosis-count";
            diagnosisCount.AutoSize = true;
            diagnosisCount.Margin = new Padding(8, 6, 0, 0);
            toolbar.Controls.Add(diagnosisCount);
            history = TextArea("工程操作日志");
            diagnosticPage = BuildValidationPanel();
            outputPages = new Panel { Dock = DockStyle.Fill };
            outputPages.Controls.AddRange(new[] { output, diagnosticPage, history });
            pane.Controls.AddRange(new Control[] { outputPages, toolbar });
            foreach (var list in new[] { diagnosticList, referenceGraph, hardwareInventory, networkLinks }) StyleDetails(list);
            SelectOutput(0);
            return pane;
        }

        private Control BuildValidationSummary()
        {
            validationSummary = TextArea("工程验证状态");
            return validationSummary;
        }

        private Label Heading(string title)
        {
            var header = Caption(title);
            header.Height = Dpi.D(this, 30);
            header.Font = UiKit.CachedFont("Microsoft YaHei UI", 10, FontStyle.Bold);
            header.TextAlign = ContentAlignment.MiddleLeft;
            return header;
        }

        private int LayoutUnits(int pixels) { return (int)Math.Round(pixels * 96.0 / DeviceDpi); }
        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }

        private void LayoutWorkbench()
        {
            var settings = Store.Settings;
            int width = LayoutUnits(Body.DisplayRectangle.Width), height = LayoutUnits(Body.DisplayRectangle.Height);
            compact = width < TiaWorkbenchStyle.CompactWidth;
            bool assistantOpen = !settings.TiaAssistantCollapsed && (!compact || compactAssistant);
            bool treeOpen = !settings.TiaTreeCollapsed;
            navigationPane.Width = Dpi.D(this, Clamp(settings.TiaTreeWidth, compact ? 154 : 220, compact ? 185 : Math.Min(420, width - (assistantOpen ? 740 : 435))));
            navigationDivider.MinSize = Dpi.D(this, compact ? 154 : 220);
            navigationDivider.MinExtra = Dpi.D(this, TiaWorkbenchStyle.MinimumCodeWidth + (!compact && assistantOpen ? 306 : 0));
            navigationPane.Visible = navigationDivider.Visible = treeOpen;
            assistantPane.Dock = assistantDivider.Dock = compact ? DockStyle.Bottom : DockStyle.Right;
            if (compact)
            {
                assistantDivider.Height = Dpi.D(this, TiaWorkbenchStyle.RailGap);
                assistantDivider.MinSize = Dpi.D(this, 178);
                assistantDivider.MinExtra = Dpi.D(this, 235);
                assistantPane.Height = Dpi.D(this, Clamp(settings.TiaAssistantHeight, 178, Math.Min(350, height - 240)));
            }
            else
            {
                assistantDivider.Width = Dpi.D(this, TiaWorkbenchStyle.RailGap);
                assistantDivider.MinSize = Dpi.D(this, 300);
                assistantDivider.MinExtra = Dpi.D(this, TiaWorkbenchStyle.MinimumCodeWidth) + (treeOpen ? navigationPane.Width : 0);
                assistantPane.Width = Dpi.D(this, Clamp(settings.TiaAssistantWidth, 300, Math.Min(520, width - (treeOpen ? LayoutUnits(navigationPane.Width) : 0) - TiaWorkbenchStyle.MinimumCodeWidth)));
            }
            assistantPane.Visible = assistantDivider.Visible = assistantOpen;
            contextDetails.Visible = !compact;
            outputPages.Visible = outputDivider.Visible = !settings.TiaLogCollapsed;
            outputDivider.MinSize = Dpi.D(this, TiaWorkbenchStyle.MinimumOutputHeight);
            int outputLimit = compact && assistantOpen ? Math.Min(130, Math.Max(92, (height - LayoutUnits(assistantPane.Height)) / 4)) : Math.Min(300, Math.Max(92, height / 3));
            outputPane.Height = Dpi.D(this, settings.TiaLogCollapsed ? TiaWorkbenchStyle.ToolbarHeight : Clamp(settings.TiaLogHeight, TiaWorkbenchStyle.MinimumOutputHeight, outputLimit));
            toggleTree.Text = treeOpen ? "收起导航" : "展开导航";
            toggleAssistant.Text = assistantOpen ? "收起助手" : "展开助手";
            toggleOutput.Text = settings.TiaLogCollapsed ? "展开输出" : "收起输出";
            foreach (var button in new[] { toggleTree, toggleAssistant, toggleOutput }) button.AccessibleName = button.Text;
        }

        private void SaveLayout() { LayoutWorkbench(); Store.SaveSettingsOnly(); }

        private void ToggleAssistant()
        {
            Store.Settings.TiaAssistantCollapsed = assistantPane.Visible;
            compactAssistant = !Store.Settings.TiaAssistantCollapsed;
            SaveLayout();
        }

        private void RevealAssistant()
        {
            compactAssistant = true;
            if (!Store.Settings.TiaAssistantCollapsed && assistantPane.Visible) return;
            Store.Settings.TiaAssistantCollapsed = false;
            LayoutWorkbench();
        }

        private void ResetLayout()
        {
            var defaults = new AppSettings();
            var settings = Store.Settings;
            settings.TiaTreeWidth = defaults.TiaTreeWidth;
            settings.TiaAssistantWidth = defaults.TiaAssistantWidth;
            settings.TiaLogHeight = defaults.TiaLogHeight;
            settings.TiaAssistantHeight = defaults.TiaAssistantHeight;
            settings.TiaTreeCollapsed = settings.TiaAssistantCollapsed = settings.TiaLogCollapsed = false;
            compactAssistant = true;
            SaveLayout();
        }

        private void SelectOutput(int index)
        {
            output.Visible = index == 0;
            diagnosticPage.Visible = index == 1;
            history.Visible = index == 2;
            for (int i = 0; i < outputTabs.Length; i++) { outputTabs[i].Primary = i == index; outputTabs[i].Invalidate(); }
        }

        private void OpenOutput(int index)
        {
            SelectOutput(index);
            if (!Store.Settings.TiaLogCollapsed) return;
            Store.Settings.TiaLogCollapsed = false;
            SaveLayout();
        }

        private void UpdateEditorStatus()
        {
            if (editorStatus == null) return;
            int position = editor.SelectionStart, line = editor.GetLineFromCharIndex(position);
            int column = position - Math.Max(0, editor.GetFirstCharIndexFromLine(line)) + 1;
            editorStatus.Text = "SCL   ·   行 " + (line + 1) + "   列 " + column + "   ·   " + (editor.ReadOnly ? "只读" : "可编辑") + "   ·   " + editor.TextLength + " 字符";
            if (editor.SelectionLength > 0) editorStatus.Text += "   ·   已选 " + editor.SelectionLength;
        }

        private void UndoCode()
        {
            if (!editor.ReadOnly && editor.CanUndo) editor.Undo();
            editor.Focus();
        }

        internal void FocusWorkbenchSearch()
        {
            if (editor.Focused) { RunAction(FindCode); return; }
            if (Store.Settings.TiaTreeCollapsed)
            {
                Store.Settings.TiaTreeCollapsed = false;
                SaveLayout();
            }
            treeQuery.Inner.Focus();
            treeQuery.Inner.SelectAll();
        }

        private void UpdateWorkbenchContext()
        {
            if (projectHeading == null) return;
            var cpuName = cpu.SelectedIndex > 0 ? cpu.SelectedItem.ToString() : "未选 PLC";
            projectHeading.Text = (snapshot == null ? "未加载工程" : snapshot.Name) + "   /   " + cpuName + "   /   " + PhaseLabel(phase);
            workbenchTips.SetToolTip(projectHeading, projectHeading.Text);
            connectionStatus.Text = inspected != null ? "V20 · 副本已读取" : !v20Checked ? "V20 · 待检测" : v20Discovered ? "V20 · 接口已发现" : "V20 · 原生接口未就绪";
            connectionStatus.ForeColor = inspected != null ? TiaWorkbenchStyle.Success(Palette) : v20Checked && !v20Discovered ? TiaWorkbenchStyle.Warning(Palette) : Palette.TextDim;
            var objectName = selected == null ? "未选中对象" : selected.Name;
            var origin = snapshot == null ? "未绑定工程资料" : snapshot.Origin;
            contextDetails.Text = "当前工程上下文\r\n" + origin + (snapshot == null ? "" : " · " + snapshot.Objects.Count + " 个对象") + "\r\n" + objectName;
            var detail = selected == null ? "在工程树中选择程序块或变量。" : selected.Name + "\r\n\r\n" + selected.Kind + "   " + selected.Language +
                "\r\n" + selected.Source + "\r\n" + selected.DataType + " " + selected.Address + "\r\n\r\n" + selected.Interface;
            if (blockDetails.Text != detail) blockDetails.Text = detail;
            validationSummary.Text = "工程验证\r\n\r\n" + origin + "\r\n" + cpuName + "\r\n" + connectionStatus.Text +
                "\r\n\r\n离线编译与审批入口位于上方；回执、诊断和操作日志位于下方。\r\n仿真、工程师审核与现场验证独立记录。";
        }

        private void AppendOperation(string action)
        {
            if (history.TextLength > 21000) history.Text = history.Text.Substring(history.TextLength - 16000);
            history.AppendText(DateTime.Now.ToString("HH:mm:ss") + "   " + action + Environment.NewLine);
        }

        private void StyleDetails(ListView list)
        {
            var finalColumn = list.Columns[list.Columns.Count - 1];
            int minimumWidth = finalColumn.Width;
            list.ClientSizeChanged += delegate
            {
                int otherColumns = list.Columns.Cast<ColumnHeader>().Take(list.Columns.Count - 1).Sum(column => column.Width);
                int width = Math.Max(minimumWidth, list.ClientSize.Width - otherColumns - SystemInformation.VerticalScrollBarWidth);
                if (finalColumn.Width != width) finalColumn.Width = width;
            };
            list.OwnerDraw = true;
            list.DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs e)
            {
                using (var fill = new SolidBrush(Palette.HeaderBg)) e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, Rectangle.Inflate(e.Bounds, -4, 0), Palette.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = list.View != System.Windows.Forms.View.Details; };
            list.DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs e)
            {
                var background = e.Item.Selected ? Palette.Hover : e.ItemIndex % 2 == 0 ? Palette.CardBg : Palette.Panel;
                using (var fill = new SolidBrush(background)) e.Graphics.FillRectangle(fill, e.Bounds);
                var text = e.SubItem.Text;
                var color = list == diagnosticList && e.ColumnIndex == 0 ? (text == "错误" ? TiaWorkbenchStyle.Error(Palette) : text == "警告" ? TiaWorkbenchStyle.Warning(Palette) : Palette.TextDim) : Palette.Text;
                TextRenderer.DrawText(e.Graphics, text, list.Font, Rectangle.Inflate(e.Bounds, -4, 0), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
        }

        private void ApplyWorkbenchPalette(Palette palette)
        {
            if (projectHeading == null) return;
            foreach (Control surface in new Control[] { projectHeading.Parent, navigationPane, assistantPane, outputPane, codeInspector })
                surface.BackColor = palette.HeaderBg;
            contextDetails.BackColor = palette.Panel;
            editor.BackColor = TiaWorkbenchStyle.EditorBackground(palette);
            foreach (var box in new[] { blockDetails, history, validationSummary }) { box.BackColor = palette.CardBg; box.ForeColor = palette.Text; }
            foreach (var divider in new[] { navigationDivider, assistantDivider, outputDivider }) divider.BackColor = palette.Bg;
            tabs.Colors = palette;
            tabs.Invalidate();
            UpdateWorkbenchContext();
        }
    }
}
