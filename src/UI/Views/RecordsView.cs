using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskBox.UI.Views
{
    /// <summary>统一工作记录：AI、自动化、文件、任务、配置、通知与错误。</summary>
    public class RecordsView : ViewBase
    {
        private readonly DarkSelect _type;
        private readonly DarkSelect _level;
        private readonly DarkInput _search;
        private readonly DarkList _list;
        private readonly Label _stat;
        private TextBox _details;
        private List<LogEntry> _visibleLogs=new List<LogEntry>();

        public RecordsView(Store store) : base(store, "工作记录", "统一查看 AI、自动化、文件、任务、配置、通知与系统错误")
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            _type = new DarkSelect(new[] { "全部类型", "AI", "自动化", "工程", "文件", "任务", "配置", "通知", "系统" }) { Left = 0, Top = 2, Width = 110 };
            _level = new DarkSelect(new[] { "全部级别", "信息", "警告", "错误" }) { Left = 118, Top = 2, Width = 100 };
            _search = new DarkInput { Left = 226, Top = 2, Width = 280, Placeholder = "搜索事件名称或错误摘要" };
            var refresh = new DarkButton("刷新") { Left = 514, Top = 2, Width = 70 };
            var clear = new DarkButton("清空记录") { Left = 592, Top = 2, Width = 90 };
            _stat = new Label { Left = 694, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            _type.SelectedIndexChanged += delegate { ReloadData(); };
            _level.SelectedIndexChanged += delegate { ReloadData(); };
            _search.TextChanged += delegate { ReloadData(); };
            refresh.Click += delegate { ReloadData(); };
            clear.Click += delegate
            {
                if (!Prompt.Confirm(this, "清空工作记录", "清空统一工作记录？文件操作历史与撤销批次不会被删除。", Palette)) return;
                Store.ClearLogs();
                ReloadData();
            };
            top.Controls.AddRange(new Control[] { _type, _level, _search, refresh, clear, _stat });
            var export=new DarkButton("导出 CSV"){Left=900,Top=2,Width=100,Palette=Palette};export.Click+=delegate{using(var dialog=new SaveFileDialog{Filter="CSV 文件|*.csv",FileName="DeskBox工作记录-"+DateTime.Now.ToString("yyyyMMdd")+".csv"})if(dialog.ShowDialog(this)==DialogResult.OK){try{System.IO.File.WriteAllText(dialog.FileName,ExportCsv(_visibleLogs),new System.Text.UTF8Encoding(true));_stat.Text="已导出 "+_visibleLogs.Count+" 条";}catch(Exception ex){_stat.Text="导出失败："+ex.Message;}}};top.Controls.Add(export);top.AutoScroll=true;

            _list = new DarkList { Dock = DockStyle.Fill, Palette = Palette };
            _list.SetColumns(new[] { "时间", "级别", "类型", "事件 / 结果" }, new[] { 150, 70, 100, 760 });
            _list.RowActivated += delegate { ShowDetail(); };
            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
            _type.SelectedIndex = 0;
            _level.SelectedIndex = 0;
        }

        public void ReloadData()
        {
            var logs = Store.ReadLogs(2000);
            string type = TypeKey(_type.SelectedIndex);
            string level = LevelKey(_level.SelectedIndex);
            string q = (_search.Text ?? "").Trim();
            if (!string.IsNullOrEmpty(type)) logs = logs.Where(x => Group(x.Action) == type).ToList();
            if (!string.IsNullOrEmpty(level)) logs = logs.Where(x => string.Equals(x.Level, level, StringComparison.OrdinalIgnoreCase)).ToList();
            if (q.Length > 0) logs = logs.Where(x => ((x.Message ?? "") + " " + (x.Action ?? "")).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _visibleLogs=logs;

            var rows = new List<ListRow>();
            foreach (var e in logs.OrderByDescending(entry => entry.Ts))
            {
                rows.Add(new ListRow
                {
                    Icon = Icon(e),
                    Cells = _details == null
                        ? new[] { UiKit.HumanTime(e.Ts), LevelName(e.Level), TypeName(Group(e.Action)), e.Message ?? "" }
                        : new[] { DateTimeOffset.FromUnixTimeMilliseconds(e.Ts).LocalDateTime.ToString("MM-dd HH:mm"), TypeName(Group(e.Action)), e.Message ?? "" },
                    Tag = e,
                    Tip = e.Message ?? "",
                    Accent = string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(248, 113, 113) : (Color?)null
                });
            }
            _list.SetRows(rows);
            if (_details != null && rows.Count > 0) _list.SelectIndex(0);
            _stat.Text = rows.Count + " 条" + (rows.Count >= 2000 ? "（显示最近 2000 条）" : "");
        }

        public override void UseReferenceLayout()
        {
            if (_details == null)
            {
                var filters = (Panel)_type.Parent;
                var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
                foreach (Control control in filters.Controls.Cast<Control>().ToArray())
                    flow.Controls.Add(control);
                _type.Visible = false;
                _search.Width = 225;
                filters.Controls.Add(flow);
                filters.Height = 49;

                var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var categories = new DarkList { Dock = DockStyle.Fill, Palette = Palette, RowHeight = 42 };
                categories.SetColumns(new[] { "日志分类" }, new[] { 140 });
                categories.SetRows(_type.Inner.Items.Cast<object>().Select(item =>
                    new ListRow { Cells = new[] { item.ToString() } }).ToList());
                categories.SelectionChanged += delegate {
                    if (categories.SelectedIndex >= 0) _type.SelectedIndex = categories.SelectedIndex;
                };
                _type.SelectedIndexChanged += delegate {
                    if (categories.SelectedIndex != _type.SelectedIndex) categories.SelectIndex(_type.SelectedIndex);
                };
                categories.SelectIndex(_type.SelectedIndex);

                _list.SetColumns(new[] { "时间", "类型", "活动时间线" }, new[] { 108, 70, 440 });
                _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Palette.CardBg,
                    ForeColor = Palette.Text, Font = _list.Font };
                var inspector = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Palette.CardBg };
                var caption = new Label { Dock = DockStyle.Top, Height = 28, Text = "事件详情", ForeColor = Palette.Text };
                inspector.Controls.Add(_details);
                inspector.Controls.Add(caption);
                grid.Controls.Add(categories, 0, 0);
                grid.Controls.Add(_list, 1, 0);
                grid.Controls.Add(inspector, 2, 0);
                Body.Controls.Add(grid);
                grid.BringToFront();
                _list.SelectionChanged += delegate { UpdateDetails(); };
                ReloadData();
            }
            base.UseReferenceLayout();
        }

        public override void ApplyPalette(Palette palette)
        {
            base.ApplyPalette(palette);
            if (_details == null) return;
            _details.BackColor = _details.Parent.BackColor = palette.CardBg;
            _details.ForeColor = palette.Text;
        }

        private void UpdateDetails()
        {
            var selected = _list.SelectedRow;
            var entry = selected == null ? null : selected.Tag as LogEntry;
            _details.Text = entry == null ? "选择一条记录查看详情。" : DetailText(entry);
        }

        private static string DetailText(LogEntry entry)
        {
            var time = DateTimeOffset.FromUnixTimeMilliseconds(entry.Ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            return string.Join("\r\n", new[] { time, LevelName(entry.Level) + " · " + TypeName(Group(entry.Action)),
                entry.Action ?? "", "", entry.Message ?? "" });
        }

        private void ShowDetail()
        {
            var row = _list.SelectedRow;
            var e = row == null ? null : row.Tag as LogEntry;
            if (e == null) return;
            var time = DateTimeOffset.FromUnixTimeMilliseconds(e.Ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            MessageBox.Show(this, "时间：" + time + "\n级别：" + LevelName(e.Level) + "\n类型：" + TypeName(Group(e.Action)) + "\n事件：" + (e.Action ?? "") + "\n\n" + (e.Message ?? ""), "工作记录详情", MessageBoxButtons.OK, e.Level == "error" ? MessageBoxIcon.Error : MessageBoxIcon.Information);
        }
        public static string ExportCsv(IEnumerable<LogEntry> logs)
        {
            Func<string,string> cell=value=>{value=value??"";if(value.TrimStart().Length>0&&"=+-@".Contains(value.TrimStart()[0]))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";};
            return "时间,级别,事件,内容\r\n"+string.Join("\r\n",logs.Select(item=>string.Join(",",new[]{DateTimeOffset.FromUnixTimeMilliseconds(item.Ts).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),item.Level,item.Action,item.Message}.Select(cell))));
        }

        private static string TypeKey(int i) { return i == 1 ? "ai" : i == 2 ? "automation" : i == 3 ? "engineering" : i == 4 ? "file" : i == 5 ? "task" : i == 6 ? "settings" : i == 7 ? "notification" : i == 8 ? "system" : ""; }
        private static string LevelKey(int i) { return i == 1 ? "info" : i == 2 ? "warn" : i == 3 ? "error" : ""; }
        private static string Group(string a)
        {
            a = (a ?? "").ToLowerInvariant();
            if (a == "ai" || a.StartsWith("ai.", StringComparison.Ordinal) || a == "tool") return "ai";
            if (a == "automation" || a == "workflow") return "automation";
            if (a == "workspace" || a == "validation" || a == "network" || a == "engineering" || a == "project" || a.StartsWith("project.", StringComparison.Ordinal)) return "engineering";
            if (a == "task") return "task";
            if (a == "settings") return "settings";
            if (a == "notification") return "notification";
            if (a.StartsWith("file-", StringComparison.Ordinal) || a.StartsWith("file.", StringComparison.Ordinal)) return "file";
            if (new[] { "open", "move", "copy", "rename", "delete", "backup", "search", "scan", "hide", "show", "assign", "file" }.Contains(a)) return "file";
            return "system";
        }
        private static string TypeName(string s) { return s == "ai" ? "AI" : s == "automation" ? "自动化" : s == "engineering" ? "工程" : s == "file" ? "文件" : s == "task" ? "任务" : s == "settings" ? "配置" : s == "notification" ? "通知" : "系统"; }
        private static string LevelName(string s) { return s == "error" ? "错误" : s == "warn" ? "警告" : "信息"; }
        private static string Icon(LogEntry e) { return e.Level == "error" ? "!" : Group(e.Action) == "ai" ? "◈" : Group(e.Action) == "automation" ? "⚡" : Group(e.Action) == "engineering" ? "⌁" : Group(e.Action) == "task" ? "☑" : Group(e.Action) == "notification" ? "🔔" : "•"; }
        protected override void OnActivated() { ReloadData(); }
    }
}
