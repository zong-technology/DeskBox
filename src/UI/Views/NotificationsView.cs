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

        public NotificationsView(Store store, NotificationStore notifications) : base(store, "通知中心", "任务与自动化提醒不会再用阻塞对话框；所有通知可回看")
        {
            _notifications = notifications;
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            var read = new DarkButton("全部标为已读") { Left = 0, Top = 2, Width = 120 };
            var clear = new DarkButton("清理已读") { Left = 128, Top = 2, Width = 90 };
            _stat = new Label { Left = 230, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            read.Click += delegate { if (_notifications != null) _notifications.MarkAllRead(); };
            clear.Click += delegate
            {
                if (_notifications == null) return;
                if (!Prompt.Confirm(this, "清理通知", "删除所有已读通知？未读通知会保留。", Palette)) return;
                _notifications.ClearRead();
            };
            top.Controls.AddRange(new Control[] { read, clear, _stat });

            _list = new DarkList { Dock = DockStyle.Fill, Palette = Palette };
            _list.SetColumns(new[] { "时间", "状态", "标题", "内容" }, new[] { 150, 70, 220, 680 });
            _list.RowActivated += delegate
            {
                var row = _list.SelectedRow;
                var n = row == null ? null : row.Tag as AppNotification;
                if (n != null) _notifications.MarkRead(n.Id, true);
            };
            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
            if (_notifications != null) _notifications.Changed += OnChanged;
        }

        private void OnChanged()
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(new Action(ReloadData)); else ReloadData(); } catch { }
        }

        public void ReloadData()
        {
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
        }

        protected override void OnActivated() { ReloadData(); }
        protected override void Dispose(bool disposing)
        {
            if (_notifications != null) _notifications.Changed -= OnChanged;
            base.Dispose(disposing);
        }
    }
}
