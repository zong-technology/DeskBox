using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private static readonly string[] PortfolioStages = { "Design", "DCS", "PLC", "HMI", "FAT", "SAT", "Validation", "Delivery" };

        private Control BuildPortfolio()
        {
            var page = Page("项目管理", "项目总览、里程碑、风险和工程交付物");
            SetSampleHeading(page, "PROJECT PORTFOLIO", "新建项目", AddProject);
            var toolbar = Toolbar();
            var query = Input("搜索项目、编号、客户或设备", 285);
            query.UseDrawnPlaceholder = true;
            var kindFilter = new DarkSelect(new[] { "全部项目", "PLC", "DCS", "SCADA", "CSV" })
                { Palette = _p, Width = 118, Height = 34 };
            kindFilter.SelectedIndex = 0;
            var view = Button("切换详细表格", () => { }, false, 128);
            toolbar.Controls.AddRange(new Control[] { query, kindFilter, view,
                Button("管理项目", () => ShowLegacy("projects"), false, 105) });

            var metrics = new TableLayoutPanel { Dock = DockStyle.Top, Height = 105, ColumnCount = 3, RowCount = 1 };
            var counts = new Label[3];
            var metricDetails = new Label[3];
            var titles = new[] { "进行中", "临近里程碑", "高风险项目" };
            var colors = new[] { _p.Accent, Color.Goldenrod, Color.Salmon };
            for (int i = 0; i < titles.Length; i++)
            {
                metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                var card = new ReferenceCard(_p) { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 10, 4), Margin = new Padding(0, 0, 10, 10) };
                var heading = PortfolioLabel(titles[i], 10, false, 22);
                var value = PortfolioLabel("", 20, true, 46);
                value.ForeColor = colors[i];
                var description = PortfolioLabel("", 8.5f, false, 18);
                card.Controls.Add(description);
                card.Controls.Add(value);
                card.Controls.Add(heading);
                metrics.Controls.Add(card, i, 0);
                counts[i] = value;
                metricDetails[i] = description;
            }

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            EngineerWorkspace selected = CurrentProject();
            bool detailed = false, loading = false;

            var table = new DarkList { Dock = DockStyle.Top, Palette = _p, CardSurface = true, InsetRows = true,
                AutoFitColumns = true, ShowHeader = false, RowHeight = 53, Font = FontOf(10) };
            var chart = new Panel { Dock = DockStyle.Top, Height = 225, Name = "portfolio-stages", AccessibleName = "八阶段进度" };
            chart.Paint += delegate(object sender, PaintEventArgs args) { PaintPortfolioStages(args.Graphics, chart.ClientRectangle, selected); };
            var catalog = PortfolioPanel("项目组合与工程阶段");
            catalog.Controls.Add(chart);
            catalog.Controls.Add(PortfolioLabel("阶段进度 · 选中项目", 9, true, 22));
            catalog.Controls.Add(table);
            catalog.Controls.Add(PortfolioLabel("项目列表 · 右列为项目总体进度", 9, false, 25));
            catalog.Controls.Add(PortfolioLabel("项目组合与工程阶段", 12, true, 30));
            catalog.Resize += delegate
            {
                bool wide = catalog.Width >= 900;
                table.RowHeight = wide ? 63 : 41;
                table.Height = 3 * table.RowHeight + (table.ShowHeader ? 26 : 0);
                chart.Height = Math.Max(0, Math.Min(wide ? 310 : 170, catalog.ClientSize.Height - table.Height - 103));
                chart.Invalidate();
            };

            var details = PortfolioPanel("项目详情与风险");
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var name = PortfolioLabel("", 12, true, 36);
            var risk = PortfolioLabel("", 11, true, 32);
            var milestone = PortfolioLabel("", 10, false, 58);
            var delivery = PortfolioLabel("", 9, false, 44);
            var notes = new RichTextBox { Dock = DockStyle.Top, Height = 72, ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = _p.CardBg,
                ForeColor = _p.TextDim, Font = FontOf(9) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 76, WrapContents = true };
            var open = new DesktopActionCard("查看项目详情", "\uE8B7", "") { Palette = _p, Width = 149, Height = 32 };
            open.Click += delegate { if (selected != null) ShowProject(selected); };
            var folder = Button("打开项目目录", () => { if (selected != null) OpenPath(selected.Root); }, false, 120);
            actions.Controls.AddRange(new Control[] { open, folder });
            scroll.Controls.Add(notes);
            scroll.Controls.Add(delivery);
            scroll.Controls.Add(milestone);
            scroll.Controls.Add(risk);
            scroll.Controls.Add(name);
            details.Controls.Add(scroll);
            details.Controls.Add(actions);
            details.Controls.Add(PortfolioLabel("项目详情与风险", 12, true, 31));

            Action inspect = () =>
            {
                name.Text = selected == null ? "请选择项目" : selected.Name;
                open.Enabled = folder.Enabled = selected != null;
                risk.Text = selected == null ? "风险 · —" : "风险 · " + PortfolioRisk(selected.Risk);
                risk.ForeColor = IsHighRisk(selected) ? Color.Salmon : _p.Text;
                var next = PortfolioMilestones(selected).OrderBy(item => item.Value).FirstOrDefault();
                milestone.Text = next.Key == null ? "下一里程碑\r\n暂无未完成的有日期节点" :
                    "下一里程碑\r\n" + next.Key + " · " + next.Value.ToString("yyyy-MM-dd") +
                    (next.Value < DateTime.Today ? "（已逾期）" : "");
                delivery.Text = selected == null ? "" : "交付日期 " + (string.IsNullOrWhiteSpace(selected.Deadline) ? "未设置" : selected.Deadline) +
                    "\r\n负责人 " + (string.IsNullOrWhiteSpace(selected.Owner) ? "未设置" : selected.Owner);
                notes.Text = selected == null ? "" : "工程备注\r\n" +
                    (string.IsNullOrWhiteSpace(selected.Notes) ? "暂无备注" : selected.Notes);
                chart.AccessibleDescription = string.Join("，", PortfolioStages.Select(stage =>
                {
                    var value = PortfolioStageValue(selected, stage);
                    return stage + " " + (value.HasValue ? value.Value.ToString("0.#") + "%" : "未记录");
                }));
                chart.Invalidate();
            };

            Action reload = () =>
            {
                var projects = _projects.Items;
                DateTime today = DateTime.Today;
                counts[0].Text = projects.Count(project =>
                {
                    var progress = ValidationProgress.Overall(project);
                    return progress.HasValue && progress.Value > 0 && progress.Value < 100;
                }).ToString("00");
                counts[1].Text = projects.SelectMany(PortfolioMilestones)
                    .Count(item => item.Value >= today && item.Value <= today.AddDays(7)).ToString("00");
                counts[2].Text = projects.Count(IsHighRisk).ToString("00");
                metricDetails[0].Text = "共 " + projects.Count + " 个工程项目";
                metricDetails[1].Text = "未来 7 天的未完成节点";
                metricDetails[2].Text = "高或严重风险";

                string term = query.Text.Trim();
                var visible = projects.Where(project =>
                    (kindFilter.SelectedIndex <= 0 || (project.Kind ?? "").StartsWith(kindFilter.Inner.Text, StringComparison.OrdinalIgnoreCase)) &&
                    (term.Length == 0 || (project.Name + " " + project.Number + " " + project.Customer + " " + project.Equipment)
                        .IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                selected = visible.FirstOrDefault(project => selected != null && project.Id == selected.Id) ?? visible.FirstOrDefault();
                table.ShowHeader = detailed;
                table.Height = 3 * table.RowHeight + (detailed ? 26 : 0);
                chart.Height = Math.Max(0, Math.Min(catalog.Width >= 900 ? 310 : 170, catalog.ClientSize.Height - table.Height - 103));
                table.SetColumns(detailed ? new[] { "项目", "类型", "负责人", "项目总体进度", "交付" } : new[] { "项目", "编号 / 类型", "项目总体进度" },
                    detailed ? new[] { 270, 70, 105, 75, 115 } : new[] { 300, 220, 90 });
                loading = true;
                try
                {
                    table.SetRows(visible.Select(project => new ListRow {
                        Cells = detailed ? new[] { project.Name, project.Kind, project.Owner, ValidationProgress.Display(project), project.Deadline } :
                            new[] { project.Name, string.Join(" · ", new[] { project.Number, project.Kind }.Where(value => !string.IsNullOrWhiteSpace(value))), ValidationProgress.Display(project) },
                        Tag = project, Tip = project.Name
                    }).ToList());
                    if (selected != null) table.SelectIndex(visible.IndexOf(selected));
                }
                finally { loading = false; }
                inspect();
            };
            table.SelectionChanged += delegate
            {
                if (loading) return;
                selected = table.SelectedRow == null ? null : table.SelectedRow.Tag as EngineerWorkspace;
                inspect();
            };
            table.RowActivated += delegate { if (selected != null) ShowProject(selected); };
            query.Inner.TextChanged += delegate { reload(); };
            kindFilter.SelectedIndexChanged += delegate { reload(); };
            view.Click += delegate { detailed = !detailed; view.Text = detailed ? "切换项目列表" : "切换详细表格"; reload(); };

            columns.Controls.Add(catalog, 0, 0);
            columns.Controls.Add(details, 1, 0);
            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(columns);
            body.Controls.Add(metrics);
            _reload["projects"] = reload;
            AddMain(page, body, toolbar);
            return page;
        }

        private ReferenceCard PortfolioPanel(string name)
        {
            return new ReferenceCard(_p) { Name = name, Dock = DockStyle.Fill, Padding = new Padding(13),
                Margin = new Padding(0, 0, 10, 0) };
        }

        private Label PortfolioLabel(string text, float size, bool bold, int height)
        {
            var label = TextLabel(text, size, bold, bold ? _p.Text : _p.TextDim);
            label.Dock = DockStyle.Top;
            label.Height = height;
            return label;
        }

        private static bool IsHighRisk(EngineerWorkspace project)
        {
            return project != null && (string.Equals(project.Risk, "high", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(project.Risk, "critical", StringComparison.OrdinalIgnoreCase));
        }

        private static string PortfolioRisk(string risk)
        {
            switch ((risk ?? "").ToLowerInvariant())
            {
                case "low": return "低";
                case "medium": return "中";
                case "high": return "高";
                case "critical": return "严重";
                default: return "未设置";
            }
        }

        private static IEnumerable<KeyValuePair<string, DateTime>> PortfolioMilestones(EngineerWorkspace project)
        {
            if (project == null) yield break;
            DateTime date;
            if (project.ValidationSteps != null)
                foreach (var step in project.ValidationSteps)
                    if (step.Percent.HasValue && step.Percent.Value < 100 &&
                        DateTime.TryParseExact(step.End, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        yield return new KeyValuePair<string, DateTime>(step.Name, date);
            if (ValidationProgress.Overall(project) != 100 &&
                DateTime.TryParseExact(project.Deadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                yield return new KeyValuePair<string, DateTime>("项目交付", date);
        }

        private static double? PortfolioStageValue(EngineerWorkspace project, string stage)
        {
            double value;
            return project != null && project.StageProgress != null && project.StageProgress.TryGetValue(stage, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value) ? (double?)Math.Max(0, Math.Min(100, value)) : null;
        }

        private void PaintPortfolioStages(Graphics graphics, Rectangle area, EngineerWorkspace project)
        {
            int row = Math.Max(1, area.Height / PortfolioStages.Length);
            int start = Math.Min(96, area.Width / 4), width = Math.Max(0, area.Width - start - 68);
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            using (var track = new SolidBrush(_p.Line))
            using (var fill = new SolidBrush(_p.Accent))
                for (int i = 0; i < PortfolioStages.Length; i++)
                {
                    string stage = PortfolioStages[i];
                    var value = PortfolioStageValue(project, stage);
                    int y = i * row;
                    TextRenderer.DrawText(graphics, stage, FontOf(9), new Rectangle(4, y, Math.Max(1, start - 6), row), _p.TextDim, flags);
                    graphics.FillRectangle(track, start, y + row / 2, width, 5);
                    if (value.HasValue) graphics.FillRectangle(fill, start, y + row / 2, (int)(width * value.Value / 100), 5);
                    TextRenderer.DrawText(graphics, value.HasValue ? value.Value.ToString("0.#") + "%" : "—", FontOf(9),
                        new Rectangle(start + width + 6, y, 60, row), _p.TextDim, flags);
                }
        }
    }
}
