using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBox.UI.Views
{
    /// <summary>非阻塞通知中心：提醒可回看、标记已读、清理已读。</summary>
    public class NotificationsView : ViewBase
    {
        private readonly NotificationStore _notifications;
        private readonly DarkList _list;
        private readonly Label _stat;
        private readonly DarkButton _openCalibration;
        private readonly Action<string> _showCalibration;

        public NotificationsView(Store store, NotificationStore notifications, Action<string> showCalibration = null)
            : base(store, "通知中心", "任务、仪表校准和自动化提醒")
        {
            _notifications = notifications;
            _showCalibration = showCalibration;
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Palette.Bg };
            var read = new DarkButton("全部标为已读") { Width = 120 };
            var clear = new DarkButton("清理已读") { Width = 90 };
            _openCalibration = new DarkButton("查看校准到期") { Name = "notification-open-calibration", Width = 136, Enabled = false };
            _openCalibration.Click += delegate { OpenCalibration(SelectedNotification); };
            _stat = new Label { AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F),
                BackColor = Palette.Bg, Margin = new Padding(6, 10, 3, 3) };
            read.Click += delegate { if (_notifications != null) Change(_notifications.MarkAllRead); };
            clear.Click += delegate
            {
                if (_notifications == null) return;
                if (!Prompt.Confirm(this, "清理通知", "删除所有已读通知？未读通知会保留。", Palette)) return;
                Change(_notifications.ClearRead);
            };
            top.Controls.AddRange(new Control[] { read, clear, _openCalibration, _stat });

            _list = new DarkList { Name = "notification-list", Dock = DockStyle.Fill, Palette = Palette };
            _list.SetColumns(new[] { "时间", "状态", "标题", "内容" }, new[] { 150, 70, 220, 680 });
            _list.RowActivated += delegate
            {
                var n = SelectedNotification;
                if (n == null) return;
                if (n.Kind == "calibration") OpenCalibration(n);
                else Change(() => _notifications.MarkRead(n.Id, true));
            };
            _list.SelectionChanged += delegate { UpdateSelection(); };
            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
            if (_notifications != null) _notifications.Changed += OnChanged;
        }

        private AppNotification SelectedNotification
        {
            get { return _list.SelectedRow == null ? null : _list.SelectedRow.Tag as AppNotification; }
        }

        private void UpdateSelection()
        {
            var selection = SelectedNotification;
            _openCalibration.Enabled = _showCalibration != null && selection != null && selection.Kind == "calibration";
        }

        private void OpenCalibration(AppNotification notice)
        {
            if (notice == null || notice.Kind != "calibration" || _showCalibration == null) return;
            Change(() => _notifications.MarkRead(notice.Id, true));
            _showCalibration(notice.SourceId);
        }

        private void Change(Action operation)
        {
            try { operation(); }
            catch (System.IO.IOException error) { _stat.Text = "保存通知失败：" + error.Message; }
            catch (UnauthorizedAccessException error) { _stat.Text = "保存通知失败：" + error.Message; }
        }

        private void OnChanged()
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(new Action(ReloadData)); else ReloadData(); } catch { }
        }

        public void ReloadData()
        {
            string selectedId = SelectedNotification == null ? "" : SelectedNotification.Id;
            var rows = new List<ListRow>();
            if (_notifications != null)
            {
                foreach (var n in _notifications.Items)
                {
                    rows.Add(new ListRow { Icon = n.Read ? "○" : "●", Cells = new[] { UiKit.HumanTime(n.Ts), n.Read ? "已读" : "未读", n.Title, n.Message }, Tag = n, Tip = n.Message, Accent = n.Read ? (Color?)null : Palette.Accent });
                }
                _stat.Text = rows.Count + " 条 · 未读 " + _notifications.UnreadCount;
            }
            _list.SetRows(rows);
            int previous = rows.FindIndex(row => ((AppNotification)row.Tag).Id == selectedId);
            if (previous >= 0) _list.SelectIndex(previous);
            UpdateSelection();
        }

        protected override void OnActivated() { ReloadData(); }
        protected override void Dispose(bool disposing)
        {
            if (_notifications != null) _notifications.Changed -= OnChanged;
            base.Dispose(disposing);
        }
    }
}
