using System;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Sync;

namespace DeskBox.UI.Views
{
    public class SyncConflictsForm : Form
    {
        private readonly LocalSyncCoordinator _sync;
        private readonly Palette _palette;
        private readonly DarkList _list;
        private readonly TextBox _local, _remote;
        private readonly Label _status;
        private SyncConflict _selected;
        private long _version;
        public SyncConflictsForm(LocalSyncCoordinator sync, Palette palette)
        {
            _sync = sync; _palette = palette; Text = "同步冲突 · 比较与处理"; Width = 1200; Height = 720; MinimumSize = new Size(900, 500); StartPosition = FormStartPosition.CenterParent; BackColor = palette.Bg;
            _list = new DarkList { Dock = DockStyle.Left, Width = 280, Palette = palette }; _list.SetColumns(new[] { "类型", "记录" }, new[] { 80, 190 });
            var panels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(12) };
            panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); panels.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); panels.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panels.Controls.Add(new Label { Text = "当前电脑数据", Dock = DockStyle.Fill, ForeColor = palette.Accent }, 0, 0);
            panels.Controls.Add(new Label { Text = "设备提交的数据 / 删除标记", Dock = DockStyle.Fill, ForeColor = palette.Accent }, 1, 0);
            _local = CreateTextBox(palette); _remote = CreateTextBox(palette); panels.Controls.Add(_local, 0, 1); panels.Controls.Add(_remote, 1, 1);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 74, Padding = new Padding(12) };
            var keep = new DarkButton("保留电脑版本") { Width = 150, Palette = palette }; var remote = new DarkButton("采用设备版本") { Width = 150, Palette = palette };
            var refresh = new DarkButton("刷新差异") { Width = 110, Palette = palette };
            keep.Click += delegate { Resolve(false); }; remote.Click += delegate { Resolve(true); }; refresh.Click += delegate { Reload(); };
            _status = new Label { AutoSize = true, ForeColor = palette.TextDim, Text = "选择一条冲突；处理只修改同步数据与项目/任务配置，不删除磁盘文件。" };
            footer.Controls.AddRange(new Control[] { keep, remote, refresh, _status });
            Controls.Add(panels); Controls.Add(_list); Controls.Add(footer);
            _list.SelectionChanged += delegate
            {
                _selected = _list.SelectedRow == null ? null : _list.SelectedRow.Tag as SyncConflict;
                if (_selected == null) { _local.Clear(); _remote.Clear(); return; }
                var current = _sync.Sync.Records.FirstOrDefault(x => x.Id == _selected.Local.Id && x.EntityType == _selected.Local.EntityType) ?? _selected.Local;
                _version = current.Version; _local.Text = Describe(current); _remote.Text = Describe(_selected.Remote);
            };
            Reload();
        }
        private static TextBox CreateTextBox(Palette p) { return new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BackColor = p.PanelAlt, ForeColor = p.Text, Font = new Font("Consolas", 10), BorderStyle = BorderStyle.None }; }
        private static string Describe(SyncRecord record)
        {
            var text = new StringBuilder(); text.AppendLine("版本：" + record.Version); text.AppendLine("设备：" + record.DeviceId); text.AppendLine("状态：" + (record.Deleted ? "删除此数据记录（不删除文件夹）" : "保留 / 更新记录"));
            if (record.UpdatedAt > 0 && record.UpdatedAt <= 253402300799999L) text.AppendLine("更新时间：" + DateTimeOffset.FromUnixTimeMilliseconds(record.UpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine(); var fields = Json.FromJson<System.Collections.Generic.Dictionary<string, object>>(record.Json);
            if (fields == null) text.AppendLine(record.Json); else foreach (var field in fields) text.AppendLine(field.Key + ": " + Convert.ToString(field.Value));
            return text.ToString();
        }
        private void Reload()
        {
            _sync.PublishWindowsSnapshot();
            var rows = _sync.Sync.Conflicts.Where(x => x.Status == "open").Select(x => new ListRow { Cells = new[] { x.Local.EntityType, x.Local.Id }, Tag = x }).ToList();
            _list.SetRows(rows); if (rows.Count > 0) _list.SelectIndex(0); _status.Text = rows.Count + " 条待处理冲突";
        }
        private void Resolve(bool useRemote)
        {
            if (_selected == null) { _status.Text = "请先选择冲突"; return; }
            if (!Prompt.Confirm(this, "确认处理同步冲突", (useRemote ? "采用设备提交的数据" : "保留当前电脑数据") + "？\r\n" + _selected.Local.EntityType + " / " + _selected.Local.Id + "\r\n选择删除标记会移除项目/任务配置，不会删除磁盘文件。", _palette)) return;
            try
            {
                var result = _sync.ResolveConflict(_selected.Id, useRemote, _version); Reload();
                _status.Text = result == SyncApplyResult.Applied ? "已处理并回写；手机下次同步获取结果" : "数据已变化或冲突已处理，请刷新比较后重试";
            }
            catch (Exception ex) { _status.Text = "处理失败：" + ex.Message; }
        }
    }
}
