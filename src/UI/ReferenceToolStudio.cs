using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildToolStudio()
        {
            var page = Page("工程工具", "PLC、DCS、仪表及工程数据转换工具");
            Action run;
            var range = BuildRangeWorkspace(out run);
            var mode = new DarkSelect(new[] { "仪表与信号", "Siemens PLC / Modbus", "网络诊断", "CSV / 制药验证" })
                { Name = "engineering-tool-mode", Palette = _p, Visible = false };
            var tools = new EngineeringToolkitView(_store) { Dock = DockStyle.Fill, Visible = false };
            tools.ApplyPalette(_p);
            tools.UseReferenceLayout();
            var host = new Panel { Dock = DockStyle.Fill };
            host.Controls.Add(range);
            host.Controls.Add(tools);
            host.Controls.Add(mode);
            var menu = new DarkList { Dock = DockStyle.Fill, Palette = _p, InsetRows = true,
                CardSurface = true, ShowHeader = false, AutoFitColumns = true, RowHeight = 52, Font = FontOf(10) };
            menu.SetColumns(new[] { "工具" }, new[] { 200 });
            menu.SetRows(new[] { "4–20 mA", "Pt100", "压力 / 温度", "Modbus", "PLC 地址", "Word / Float", "通信诊断", "工程数据转换", "验证与记录", "软件启动台" }
                .Select(label => new ListRow { Cells = new[] { label } }).ToList());
            menu.SelectIndex(0);

            var categoryModes = new[] { -1, 0, 0, 1, 1, 1, 2, 0, 3 };
            Action showRange = () =>
            {
                mode.SelectedIndex = -1;
                tools.Visible = false;
                range.Visible = true;
                range.BringToFront();
                if (menu.SelectedIndex != 0) menu.SelectIndex(0);
            };
            Action<int> showTools = index =>
            {
                tools.SelectMode(index);
                range.Visible = false;
                tools.Visible = true;
                tools.BringToFront();
            };
            mode.SelectedIndexChanged += delegate
            {
                if (mode.SelectedIndex < 0) return;
                if (menu.SelectedIndex < 0 || menu.SelectedIndex >= categoryModes.Length ||
                    categoryModes[menu.SelectedIndex] != mode.SelectedIndex)
                    menu.SelectIndex(Array.IndexOf(categoryModes, mode.SelectedIndex));
                else showTools(mode.SelectedIndex);
            };
            menu.SelectionChanged += delegate
            {
                int item = menu.SelectedIndex;
                if (item == 0) showRange();
                else if (item == categoryModes.Length)
                {
                    Navigate("home");
                    menu.SelectIndex(0);
                }
                else if (item > 0)
                {
                    int target = categoryModes[item];
                    if (mode.SelectedIndex != target) mode.SelectedIndex = target;
                    else showTools(target);
                }
            };
            SetSampleHeading(page, "INSTRUMENTATION LAB", "运行计算", () => { showRange(); run(); },
                Button("打开工程工作区", () => Navigate("home"), false, 116));
            var categories = InstrumentPanel("工具分类", "INSTRUMENTATION", menu);
            menu.Resize += delegate { menu.RowHeight = Math.Max(35, Math.Min(62, menu.ClientSize.Height / menu.RowCount)); };
            categories.Dock = DockStyle.Left;
            categories.Width = 238;
            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(host);
            body.Controls.Add(categories);
            body.Resize += delegate { categories.Width = body.ClientSize.Width >= 1400 ? 300 : 200; };
            _reload["engineering"] = () => tools.Activate();
            AddMain(page, body);
            return page;
        }

        private Control BuildRangeWorkspace(out Action run)
        {
            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var input = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var ma = InstrumentField("输入电流 / mA", "12.50");
            var low = InstrumentField("工程量下限", "0.00");
            var high = InstrumentField("工程量上限", "100.00");
            var reverse = InstrumentField("工程量（反算电流时使用）", "50");
            var submit = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 69, WrapContents = true, Padding = new Padding(0, 3, 0, 0) };
            var forwardButton = Button("计算工程量", () => { }, true, 119);
            var reverseButton = Button("反算电流", () => { }, false, 112);
            submit.Controls.AddRange(new Control[] { forwardButton, reverseButton });
            input.Controls.Add(submit);
            foreach (var field in new[] { reverse, high, low, ma })
                input.Controls.Add(field.Parent);

            var result = TextLabel("—", 33, true, _p.Accent);
            result.Name = "instrument-result";
            result.Dock = DockStyle.Top;
            result.Height = 74;
            var summary = TextLabel("", 10, false, _p.TextDim);
            summary.Name = "instrument-summary";
            summary.Dock = DockStyle.Top;
            summary.Height = 64;
            var meter = new Panel { Dock = DockStyle.Top, Height = 25 };
            double? percent = null;
            meter.Paint += delegate(object sender, PaintEventArgs e)
            {
                var line = new Rectangle(3, 10, Math.Max(1, meter.Width - 6), 6);
                Palette.FillRounded(e.Graphics, _p.Line, line, 3);
                if (!percent.HasValue) return;
                int width = (int)(line.Width * Math.Max(0, Math.Min(100, percent.Value)) / 100);
                if (width > 0) Palette.FillRounded(e.Graphics, _p.Accent, new Rectangle(line.X, line.Y, width, line.Height), 3);
            };
            var history = new DarkList { Dock = DockStyle.Fill, Palette = _p, CardSurface = true, InsetRows = true,
                ShowHeader = false, AutoFitColumns = true, RowHeight = 42, Font = FontOf(9.5f) };
            history.SetColumns(new[] { "本次计算记录", "结果" }, new[] { 115, 145 });
            var rows = new List<ListRow>();
            var resultBody = new Panel { Dock = DockStyle.Fill };
            resultBody.Controls.Add(history);
            resultBody.Controls.Add(meter);
            resultBody.Controls.Add(summary);
            resultBody.Controls.Add(result);
            Action<bool, bool> calculate = (back, record) =>
            {
                try
                {
                    double a = Finite(low.Text), b = Finite(high.Text);
                    double number = Finite(back ? reverse.Text : ma.Text);
                    var answer = back ? EngineeringTools.ToCurrent(number, a, b) : EngineeringTools.FromCurrent(number, a, b);
                    if (!answer.Ok) throw new ArgumentException(answer.Error);
                    if (double.IsInfinity(answer.Value) || double.IsNaN(answer.Value) ||
                        double.IsInfinity(answer.Percent) || double.IsNaN(answer.Percent))
                        throw new ArgumentException("计算超出有效数值范围。");
                    string display = answer.Value.ToString("0.###", CultureInfo.InvariantCulture);
                    result.Text = display + (back ? " mA" : "");
                    percent = answer.Percent;
                    summary.Text = (back ? "输出电流" : "计算工程量") + "  ·  量程 " +
                        Math.Round(answer.Percent, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture) + "%" +
                        (answer.Percent < 0 || answer.Percent > 100 ? "\r\n超出量程，请核对输入" :
                            back ? "\r\n4 + 16 × (工程量 − 下限) ÷ (上限 − 下限)" :
                            "\r\n下限 + (mA − 4) ÷ 16 × (上限 − 下限)");
                    summary.ForeColor = _p.TextDim;
                    if (record)
                    {
                        rows.Insert(0, new ListRow { Cells = new[] { number.ToString("0.###", CultureInfo.InvariantCulture) +
                            (back ? " 工程量" : " mA"), result.Text } });
                        if (rows.Count > 20) rows.RemoveAt(rows.Count - 1);
                        history.SetRows(rows);
                    }
                }
                catch (ArgumentException error)
                {
                    result.Text = "—";
                    percent = null;
                    summary.Text = error.Message;
                    summary.ForeColor = Color.Salmon;
                }
                meter.Invalidate();
            };
            run = () => calculate(false, true);
            forwardButton.Click += delegate { calculate(false, true); };
            reverseButton.Click += delegate { calculate(true, true); };
            foreach (var field in new[] { ma, low, high, reverse })
            {
                bool back = field == reverse;
                field.KeyPressed += delegate(object sender, KeyEventArgs args)
                {
                    if (args.KeyCode != Keys.Enter) return;
                    args.SuppressKeyPress = true;
                    calculate(back, true);
                };
            }
            columns.Controls.Add(InstrumentPanel("线性量程转换", "mA → 工程量 / 工程量 → mA", input), 0, 0);
            columns.Controls.Add(InstrumentPanel("计算结果与历史", "当前页面的本地计算记录", resultBody), 1, 0);
            calculate(false, false);
            return columns;
        }

        private DarkInput InstrumentField(string label, string value)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(0, 0, 0, 8) };
            var caption = TextLabel(label, 9, false, _p.TextDim);
            caption.Dock = DockStyle.Top;
            caption.Height = 22;
            var field = Input(label);
            field.Text = value;
            field.AccessibleName = label;
            field.Dock = DockStyle.Top;
            field.Height = 35;
            row.Controls.Add(field);
            row.Controls.Add(caption);
            return field;
        }

        private ReferenceCard InstrumentPanel(string title, string subtitle, Control content)
        {
            var card = new ReferenceCard(_p) { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(5, 0, 5, 0) };
            var heading = TextLabel(title, 12, true);
            heading.Dock = DockStyle.Top;
            heading.Height = 27;
            var hint = TextLabel(subtitle, 9, false, _p.TextDim);
            hint.Dock = DockStyle.Top;
            hint.Height = 25;
            card.Controls.Add(content);
            card.Controls.Add(hint);
            card.Controls.Add(heading);
            return card;
        }
    }
}
