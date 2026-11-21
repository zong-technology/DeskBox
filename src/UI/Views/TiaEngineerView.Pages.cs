using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed partial class TiaEngineerView
    {
        private readonly List<Label> pageStates = new List<Label>();
        private string lastWorkbenchError, activeWorkbenchAction;
        private string verificationStatic, verifiedDraft, verificationCompile;
        private TiaEnvironment environmentReport;
        private RichTextBox conversation;
        private TiaWorkbenchTabs assistantTabs;
        private ListView variableGrid, lineChanges;
        private DarkInput variableSearch;
        private Label variableCount, changeCount;
        private int variableSort, changeIndex = -1;
        private bool variableDescending;
        private readonly List<int> changeRows = new List<int>();
        private const string EmptyDiff = "设置工程基线后读取另一份资料，或在编辑器中生成需要审查的 SCL 替换草稿。";

        private void AddWorkbenchPage(TabPage page, string guidance)
        {
            var band = new Panel { Dock = DockStyle.Top, Height = TiaWorkbenchStyle.Pixels(this, 48), Padding = new Padding(8, 4, 8, 4), BackColor = Palette.HeaderBg };
            var title = Caption(page.Text);
            title.Dock = DockStyle.Left;
            title.Width = TiaWorkbenchStyle.Pixels(this, 92);
            title.Font = UiKit.CachedFont("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
            title.TextAlign = ContentAlignment.MiddleLeft;
            var status = Caption("");
            status.Dock = DockStyle.Fill;
            status.Height = TiaWorkbenchStyle.Pixels(this, 21);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.Padding = new Padding(8, 0, 0, 0);
            status.Name = "tia-state-" + pageStates.Count;
            var hint = Caption(guidance);
            hint.Dock = DockStyle.Bottom;
            hint.Height = TiaWorkbenchStyle.Pixels(this, 19);
            hint.Padding = new Padding(8, 0, 0, 0);
            hint.Font = UiKit.CachedFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            workbenchTips.SetToolTip(hint, guidance);
            var text = new Panel { Dock = DockStyle.Fill };
            text.Controls.AddRange(new Control[] { status, hint });
            band.Controls.AddRange(new Control[] { text, title });
            page.Controls.Add(band);
            pageStates.Add(status);
            tabs.TabPages.Add(page);
        }

        private void RefreshPageStates()
        {
            if (pageStates.Count != 7 || navigationEmpty == null) return;
            int tagsFound = variableGrid == null ? 0 : variableGrid.Items.Count;
            var states = new[]
            {
                editor.TextLength == 0 ? "尚无 SCL 草稿" : (editor.ReadOnly ? "只读" : "本机草稿") + "  ·  " + editor.Lines.Length + " 行",
                snapshot == null ? "未加载资料" : selected == null ? snapshot.Objects.Count + " 个工程对象  ·  请选择对象" : selected.Name + "  ·  " + referenceGraph.Items.Count + " 条引用",
                hardwareInventory.Items.Count == 0 ? "未发现设备和模块" : hardwareInventory.Items.Count + " 个设备或模块  ·  可选择槽位",
                networkLinks.Items.Count == 0 ? "未读取到连接关系" : networkLinks.Items.Count + " 条有来源的连接",
                tagsFound == 0 ? "没有匹配的 PLC 变量" : tagsFound + " 条可见 PLC 变量",
                pendingScl != null ? "待确认  ·  SCL 行级差异" : diff.TextLength == 0 || diff.Text == EmptyDiff ? "未生成差异" :
                    diff.Text.StartsWith("待确认的 SCL 草稿替换", StringComparison.Ordinal) ? "上次 SCL 差异" : "已生成工程差异报告",
                verificationCompile ?? StaticCheckStatus() ?? (environmentReport != null ? connectionStatus.Text : "尚未运行工程验证")
            };
            bool loading = phase == Phase.Planning || phase == Phase.Executing || phase == Phase.Validating;
            for (int i = 0; i < pageStates.Count; i++)
            {
                var status = pageStates[i];
                string text = states[i];
                var color = Palette.Text;
                if (loading && tabs.SelectedIndex == i) { text = PhaseLabel(phase) + "  ·  " + text; color = Palette.Accent; }
                if (phase == Phase.Failed && tabs.SelectedIndex == i)
                {
                    text = "操作失败  ·  " + (lastWorkbenchError ?? text).Split('\n')[0].TrimEnd('\r');
                    color = TiaWorkbenchStyle.Error(Palette);
                }
                if (status.Text != text) { status.Text = text; workbenchTips.SetToolTip(status, text); }
                status.ForeColor = color;
            }
            navigationEmpty.Text = snapshot == null
                ? (loading && activeWorkbenchAction != null && activeWorkbenchAction.Contains("读取") ? "正在读取工程资料…" : "未加载工程对象\r\n\r\n使用上方「打开导出资料」或「读取 .ap20 副本」开始。")
                : "当前搜索没有匹配对象。\r\n\r\n清除搜索后可显示完整工程树。";
            bool hasNodes = tree.Nodes.Count != 0;
            tree.Visible = hasNodes;
            navigationEmpty.Visible = !hasNodes;
        }

        private void ApplyPagePalette()
        {
            foreach (var state in pageStates)
            {
                state.Parent.BackColor = Palette.HeaderBg;
                state.Parent.Parent.BackColor = Palette.HeaderBg;
                foreach (Control label in state.Parent.Controls) label.BackColor = Palette.HeaderBg;
                foreach (Control label in state.Parent.Parent.Controls) label.BackColor = Palette.HeaderBg;
            }
            foreach (var box in new[] { conversation, validationSummary })
            {
                box.BackColor = Palette.CardBg;
                box.ForeColor = Palette.Text;
            }
            assistantTabs.Colors = Palette;
            assistantTabs.Font = UiKit.CachedFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
            assistantTabs.FitLabels();
            assistantTabs.Invalidate();
            topologyCanvas.ApplyPalette(Palette);
            RefreshDiffView();
            RefreshPageStates();
        }

        private Control BuildAssistantContent()
        {
            assistantTabs = new TiaWorkbenchTabs { Dock = DockStyle.Fill };
            conversation = TextArea("工程对话记录");
            conversation.Text = "提交工程需求后会在此显示本次对话。";
            var statusPage = new TabPage("任务概览");
            statusPage.Controls.Add(assistant);
            var conversationPage = new TabPage("工程对话");
            conversationPage.Controls.Add(conversation);
            assistantTabs.TabPages.AddRange(new[] { statusPage, conversationPage });
            return assistantTabs;
        }

        private void AddConversation(string speaker, string message)
        {
            const string empty = "提交工程需求后会在此显示本次对话。";
            if (conversation.Text == empty) conversation.Clear();
            if (conversation.TextLength > 14000) conversation.Text = conversation.Text.Substring(conversation.TextLength - 10000);
            conversation.AppendText(speaker + "  ·  " + DateTime.Now.ToString("HH:mm") + "\r\n" + Limit(message, 6000) + "\r\n\r\n");
            conversation.ScrollToCaret();
        }

        private Control BuildVariablesPage()
        {
            variableGrid = new ListView { Name = "tia-variable-table", AccessibleName = "PLC 变量列表", View = View.Details, Dock = DockStyle.Fill,
                FullRowSelect = true, MultiSelect = false, HideSelection = false, ShowItemToolTips = true };
            foreach (var name in new[] { "名称", "数据类型", "地址", "范围", "来源" })
                variableGrid.Columns.Add(name, TiaWorkbenchStyle.Pixels(this, name == "范围" || name == "来源" ? 155 : name == "名称" ? 175 : 112));
            variableGrid.ColumnClick += delegate(object sender, ColumnClickEventArgs e)
            {
                variableDescending = e.Column == variableSort && !variableDescending;
                variableSort = e.Column;
                FillVariableGrid();
            };
            variableGrid.SelectedIndexChanged += delegate
            {
                if (variableGrid.SelectedItems.Count != 1) return;
                var item = (PlcObject)variableGrid.SelectedItems[0].Tag;
                selected = item;
                tags.Text = item.Identity + "\r\n" + item.DataType + "  " + item.Address + "\r\n来源 " + item.Source + "\r\n\r\n" + item.Content;
                UpdateWorkbenchContext();
            };
            variableGrid.ItemActivate += delegate
            {
                if (variableGrid.SelectedItems.Count == 1) ShowObject((PlcObject)variableGrid.SelectedItems[0].Tag, true);
            };
            variableSearch = new DarkInput { Width = TiaWorkbenchStyle.Pixels(this, 265), Height = TiaWorkbenchStyle.Pixels(this, 31), Placeholder = "筛选名称、数据类型、地址或来源", Name = "tia-variable-filter" };
            variableSearch.Inner.AccessibleName = "筛选 PLC 变量";
            variableSearch.Inner.TextChanged += delegate { FillVariableGrid(); };
            variableCount = Caption("等待导入 PLC 变量");
            variableCount.AutoSize = true;
            variableCount.Margin = new Padding(10, 8, 0, 0);
            var filterRow = Bar();
            filterRow.Controls.AddRange(new Control[] { variableSearch, variableCount });
            tags.Text = "载入 IO CSV 或工程副本后，按列排序，或在上方按名称、地址和类型筛选。";
            tags.Height = TiaWorkbenchStyle.Pixels(this, 116);
            tags.Dock = DockStyle.Bottom;
            var page = new Panel { Dock = DockStyle.Fill };
            page.Controls.AddRange(new Control[] { variableGrid, tags, filterRow });
            return page;
        }

        private static string[] VariableColumns(PlcObject item) { return new[] { item.Name, item.DataType, item.Address, item.Scope, item.Source }; }

        private void FillVariableGrid(PlcObject focus = null)
        {
            if (snapshot == null) return;
            if (focus != null && variableSearch.Text.Length != 0) variableSearch.Text = "";
            string filter = variableSearch.Text.Trim();
            var source = snapshot.Objects.Where(item => item.Kind == "PlcTag" && (filter.Length == 0 ||
                VariableColumns(item).Any(value => value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0))).ToArray();
            var sorted = variableDescending
                ? source.OrderByDescending(item => VariableColumns(item)[variableSort], StringComparer.OrdinalIgnoreCase)
                : source.OrderBy(item => VariableColumns(item)[variableSort], StringComparer.OrdinalIgnoreCase);
            var visible = sorted.Take(1500).ToList();
            if (focus != null && !visible.Contains(focus)) visible.Insert(0, focus);
            variableGrid.BeginUpdate();
            try
            {
                variableGrid.Items.Clear();
                foreach (var item in visible)
                {
                    var row = variableGrid.Items.Add(new ListViewItem(VariableColumns(item)) { Tag = item, ToolTipText = item.Identity + "\r\n" + item.Source });
                    if (item == focus) { row.Selected = true; row.EnsureVisible(); }
                }
            }
            finally { variableGrid.EndUpdate(); }
            variableCount.Text = visible.Count + " / " + source.Length + " 条  ·  " + variableGrid.Columns[variableSort].Text + (variableDescending ? " ↓" : " ↑");
            RefreshPageStates();
        }

        private Control BuildDiffPage()
        {
            lineChanges = new ListView { Name = "tia-scl-diff-lines", AccessibleName = "SCL 双栏行差异", Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, MultiSelect = false, ShowItemToolTips = true, HideSelection = false, Visible = false };
            lineChanges.Font = UiKit.CachedFont("Consolas", 10.5f, FontStyle.Regular);
            lineChanges.Columns.Add("当前 SCL 草稿", TiaWorkbenchStyle.Pixels(this, 280));
            lineChanges.Columns.Add("建议 SCL 草稿", TiaWorkbenchStyle.Pixels(this, 280));
            lineChanges.Resize += delegate
            {
                int half = Math.Max(TiaWorkbenchStyle.Pixels(this, 205), (lineChanges.ClientSize.Width - SystemInformation.VerticalScrollBarWidth) / 2);
                foreach (ColumnHeader column in lineChanges.Columns) if (column.Width != half) column.Width = half;
            };
            var navigation = Bar();
            navigation.Name = "tia-diff-navigation";
            changeCount = Caption("未生成差异");
            changeCount.AutoSize = true;
            changeCount.Margin = new Padding(10, 8, 0, 0);
            navigation.Controls.AddRange(new Control[] { Button("上一处", () => NavigateDiff(-1), false), Button("下一处", () => NavigateDiff(1), false), changeCount });
            diff.Text = EmptyDiff;
            var page = new Panel { Dock = DockStyle.Fill };
            page.Controls.AddRange(new Control[] { lineChanges, diff, navigation });
            diff.TextChanged += delegate { RefreshDiffView(); RefreshPageStates(); };
            navigation.Visible = false;
            return page;
        }

        private void RefreshDiffView()
        {
            if (lineChanges == null || lineChanges.Parent == null) return;
            bool review = pendingScl != null && diff.Text.StartsWith("待确认的 SCL 草稿替换", StringComparison.Ordinal);
            diff.Visible = !review;
            lineChanges.Visible = review;
            lineChanges.Parent.Controls["tia-diff-navigation"].Visible = review;
            if (!review) return;
            lineChanges.BeginUpdate();
            try
            {
                lineChanges.Items.Clear();
                changeRows.Clear();
                int left = 1, right = 1;
                foreach (var line in diff.Lines.Skip(2))
                {
                    if (line.Length == 0 || line == "--- 当前草稿" || line == "+++ 建议草稿") continue;
                    if (line.StartsWith("@@"))
                    {
                        var positions = Regex.Match(line, @"^@@ -(\d+),(\d+) \+(\d+),(\d+)");
                        if (!positions.Success) continue;
                        left = int.Parse(positions.Groups[1].Value);
                        right = int.Parse(positions.Groups[3].Value);
                        continue;
                    }
                    char mark = line[0];
                    if (mark != '-' && mark != '+' && mark != ' ') continue;
                    var text = line.Substring(1);
                    var row = new ListViewItem(new[] { mark == '+' ? "" : left++ + "  " + mark + " " + text,
                        mark == '-' ? "" : right++ + "  " + mark + " " + text });
                    if (mark != ' ') changeRows.Add(lineChanges.Items.Count);
                    row.ForeColor = mark == '-' ? TiaWorkbenchStyle.Error(Palette) : mark == '+' ? TiaWorkbenchStyle.Success(Palette) : Palette.Text;
                    row.ToolTipText = text;
                    lineChanges.Items.Add(row);
                }
                changeIndex = -1;
                changeCount.Text = changeRows.Count + " 行变化";
            }
            finally { lineChanges.EndUpdate(); }
        }

        private void NavigateDiff(int step)
        {
            if (changeRows.Count == 0) return;
            changeIndex = changeIndex < 0 ? (step > 0 ? 0 : changeRows.Count - 1) : (changeIndex + step + changeRows.Count) % changeRows.Count;
            var row = lineChanges.Items[changeRows[changeIndex]];
            row.Selected = row.Focused = true;
            row.EnsureVisible();
            changeCount.Text = (changeIndex + 1) + " / " + changeRows.Count + " 行变化";
        }

        private Control BuildValidationSummary()
        {
            validationSummary = TextArea("工程验证状态");
            return validationSummary;
        }

        private void RefreshValidation()
        {
            if (validationSummary == null) return;
            string draftCheck = StaticCheckStatus() ?? "待检查";
            var rows = new[] { "SCL 静态检查     " + draftCheck, "TIA 离线编译     " + (verificationCompile ?? (inspected == null ? "等待授权 V20 项目副本" : "尚未编译")),
                "PLCSIM 仿真       未执行", "现场验证           未执行" };
            string details = environmentReport == null ? "使用「查看能力与环境」检查版本、Openness 和用户组。" : environmentReport.Describe();
            string text = "验证项目\r\n\r\n" + string.Join("\r\n", rows) + "\r\n\r\n本机环境\r\n" + details + "\r\n\r\n编译回执、诊断及操作记录在下方输出区。";
            if (validationSummary.Text != text) validationSummary.Text = text;
        }

        private string StaticCheckStatus()
        {
            return verificationStatic != null && !SameSource(verifiedDraft, editor.Text) ? "草稿已改，需重新检查" : verificationStatic;
        }
    }
}
