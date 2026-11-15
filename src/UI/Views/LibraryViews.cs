using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Fs;

namespace DeskBox.UI.Views
{
    /// <summary>收藏 / 最近访问 / 标签：左侧分组，右侧文件列表。</summary>
    public class FavoritesView : ViewBase
    {
        private readonly FileLibrary _library;
        private DarkList _list;
        private DarkSelect _groupSel;
        private DarkButton _btnOpen, _btnReveal, _btnRemove, _btnAdd;
        private Label _stat;
        private string _mode = "fav";     // fav | recent | tag
        private string _tag = "";

        public event Action<string> OpenRequested;
        public event Action<string> RevealRequested;

        public FavoritesView(Store store, FileLibrary library) : base(store, "收藏与最近", "收藏常用文件、查看最近访问、按标签聚合")
        {
            _library = library;
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            var tabs = new DarkSelect(new[] { "收藏", "最近访问", "标签" }) { Left = 0, Top = 2, Width = 120 };
            tabs.Palette = Palette;
            tabs.SelectedIndexChanged += delegate
            {
                _mode = tabs.SelectedIndex == 0 ? "fav" : tabs.SelectedIndex == 1 ? "recent" : "tag";
                ReloadData();
            };
            tabs.SelectedIndex = 0;

            _groupSel = new DarkSelect(new[] { "默认" }) { Left = 128, Top = 2, Width = 120 };
            _groupSel.Palette = Palette;
            _groupSel.SelectedIndexChanged += delegate { ReloadData(); };

            _btnAdd = new DarkButton("收藏当前文件…") { Left = 256, Top = 2, Width = 130 };
            _btnAdd.Click += delegate { AddByPath(); };
            _btnOpen = new DarkButton("打开") { Primary = true, Left = 392, Top = 2, Width = 70 };
            _btnOpen.Click += delegate { var p = CurrentPath(); if (p != null && OpenRequested != null) OpenRequested(p); };
            _btnReveal = new DarkButton("定位") { Left = 468, Top = 2, Width = 70 };
            _btnReveal.Click += delegate { var p = CurrentPath(); if (p != null && RevealRequested != null) RevealRequested(p); };
            _btnRemove = new DarkButton("移除收藏") { Left = 544, Top = 2, Width = 90 };
            _btnRemove.Click += delegate { RemoveCurrent(); };

            _stat = new Label { Left = 648, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            top.Controls.AddRange(new Control[] { tabs, _groupSel, _btnAdd, _btnOpen, _btnReveal, _btnRemove, _stat });

            _list = new DarkList { Dock = DockStyle.Fill };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "名称", "路径", "信息" }, new[] { 280, 560, 160 });
            _list.RowActivated += delegate { var p = CurrentPath(); if (p != null && OpenRequested != null) OpenRequested(p); };
            _list.SelectionChanged += delegate { };

            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        public void ReloadData()
        {
            if (_library == null || _list == null) return;
            var rows = new List<ListRow>();
            if (_mode == "fav")
            {
                foreach (var f in _library.Favorites.OrderByDescending(x => x.AddedAt))
                    rows.Add(new ListRow { Icon = "★", Cells = new[] { f.Name, f.Path, f.Group + " · " + UiKit.HumanTime(f.AddedAt) }, Tag = f.Path });
                _stat.Text = "共 " + _library.Favorites.Count + " 个收藏";
            }
            else if (_mode == "recent")
            {
                foreach (var r in _library.Recent.OrderByDescending(x => x.LastOpenAt))
                    rows.Add(new ListRow { Icon = "🕘", Cells = new[] { r.Name, r.Path, "打开 " + r.OpenCount + " 次 · " + UiKit.HumanTime(r.LastOpenAt) }, Tag = r.Path });
                _stat.Text = "最近 " + _library.Recent.Count + " 条";
            }
            else
            {
                if (string.IsNullOrEmpty(_tag))
                {
                    var tags = _library.AllTags();
                    foreach (var t in tags)
                    {
                        var paths = _library.PathsWithTag(t);
                        rows.Add(new ListRow { Icon = "#", Cells = new[] { t, paths.Count + " 个文件", "" }, Tag = "tag:" + t });
                    }
                    _stat.Text = "共 " + tags.Count + " 个标签（点击查看）";
                }
                else
                {
                    foreach (var p in _library.PathsWithTag(_tag))
                        rows.Add(new ListRow { Icon = "#", Cells = new[] { Path.GetFileName(p), p, _tag }, Tag = p });
                    _stat.Text = "标签「" + _tag + "」下 " + rows.Count + " 个文件";
                }
            }
            _list.SetRows(rows);
        }

        private string CurrentPath()
        {
            var row = _list.SelectedRow;
            if (row == null) return null;
            var t = row.Tag as string;
            if (t != null && t.StartsWith("tag:")) { _tag = t.Substring(4); ReloadData(); return null; }
            return t;
        }

        private void AddByPath()
        {
            var p = Prompt.Input(this, "加入收藏", "输入要收藏的完整文件路径", "", Palette);
            if (string.IsNullOrWhiteSpace(p)) return;
            if (!File.Exists(p) && !Directory.Exists(p)) { _stat.Text = "路径不存在：" + p; return; }
            try { _library.AddFavorite(p.Trim(), _groupSel.SelectedText); ReloadData(); }
            catch (Exception ex) { _stat.Text = "收藏失败：" + ex.Message; }
        }

        private void RemoveCurrent()
        {
            var row = _list.SelectedRow;
            if (row == null) return;
            var p = row.Tag as string;
            if (p == null || p.StartsWith("tag:")) return;
            if (!Prompt.Confirm(this, "移除收藏", "从收藏移除 " + Path.GetFileName(p) + "？（不会删除文件）", Palette)) return;
            _library.RemoveFavorite(p);
            ReloadData();
        }

        protected override void OnActivated() { ReloadData(); }
    }

    /// <summary>文件操作历史：可筛选、可撤销、可清空。</summary>
    public class HistoryView : ViewBase
    {
        private readonly FileHistory _history;
        private readonly FileOps _ops;
        private DarkList _list;
        private DarkSelect _filter;
        private DarkInput _search;
        private Label _stat;
        private DarkButton _btnUndo, _btnClear, _btnOpen;

        public event Action<string> OpenRequested;

        public HistoryView(Store store, FileHistory history, FileOps ops) : base(store, "操作历史", "记录 DeskBox 能追踪的打开/移动/复制/重命名/删除与备份动作")
        {
            _history = history;
            _ops = ops;
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            _filter = new DarkSelect(new[] { "全部动作", "打开", "移动", "复制", "重命名", "删除/备份", "收藏", "搜索" }) { Left = 0, Top = 2, Width = 120 };
            _filter.Palette = Palette;
            _filter.SelectedIndexChanged += delegate { ReloadData(); };
            _search = new DarkInput { Left = 128, Top = 2, Width = 220, Placeholder = "按名称/路径过滤" };
            _search.Palette = Palette;
            _search.KeyPressed += delegate { ReloadData(); };
            _btnOpen = new DarkButton("打开") { Primary = true, Left = 356, Top = 2, Width = 70 };
            _btnOpen.Click += delegate { var p = CurrentPath(); if (p != null && OpenRequested != null) OpenRequested(p); };
            _btnUndo = new DarkButton("撤销最近批次") { Left = 432, Top = 2, Width = 110 };
            _btnUndo.Click += delegate { UndoLast(); };
            _btnClear = new DarkButton("清空历史") { Left = 548, Top = 2, Width = 90 };
            _btnClear.Click += delegate
            {
                if (!Prompt.Confirm(this, "清空历史", "清空操作历史（不会影响文件与撤销批次）。继续？", Palette)) return;
                _history.Clear();
                ReloadData();
            };
            _stat = new Label { Left = 648, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            top.Controls.AddRange(new Control[] { _filter, _search, _btnOpen, _btnUndo, _btnClear, _stat });

            _list = new DarkList { Dock = DockStyle.Fill };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "时间", "动作", "对象", "结果" }, new[] { 150, 90, 520, 200 });
            _list.RowActivated += delegate { var p = CurrentPath(); if (p != null && OpenRequested != null) OpenRequested(p); };

            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        public void ReloadData()
        {
            if (_history == null || _list == null) return;
            string op = null;
            switch (_filter.SelectedIndex)
            {
                case 1: op = "open"; break;
                case 2: op = "move"; break;
                case 3: op = "copy"; break;
                case 4: op = "rename"; break;
                case 5: op = "delete"; break;
                case 6: op = "favorite"; break;
                case 7: op = "search"; break;
            }
            var items = _history.Query(400, op, _search.Text);
            var rows = new List<ListRow>();
            foreach (var r in items)
            {
                string target = string.IsNullOrEmpty(r.Target) ? r.Source : r.Source + " → " + r.Target;
                rows.Add(new ListRow
                {
                    Icon = IconOf(r.Op),
                    Cells = new[] { DateTimeOffset.FromUnixTimeMilliseconds(r.Ts).LocalDateTime.ToString("MM-dd HH:mm:ss"), OpName(r.Op), target, r.Ok ? (string.IsNullOrEmpty(r.Detail) ? "成功" : r.Detail) : "失败：" + r.Detail },
                    Tag = string.IsNullOrEmpty(r.Target) ? r.Source : r.Target,
                    Accent = r.Ok ? (Color?)null : Color.FromArgb(248, 113, 113)
                });
            }
            _list.SetRows(rows);
            var last = _ops == null ? null : _ops.LastUndo;
            _stat.Text = items.Count + " 条" + (last == null ? "" : " · 可撤销：" + last.Title);
        }

        private static string IconOf(string op)
        {
            switch (op)
            {
                case "open": return "↗";
                case "move": return "📦";
                case "copy": return "📄";
                case "rename": return "✏";
                case "delete": return "🗑";
                case "favorite": return "★";
                case "search": return "🔍";
                default: return "•";
            }
        }

        private static string OpName(string op)
        {
            switch (op)
            {
                case "open": return "打开";
                case "move": return "移动";
                case "copy": return "复制";
                case "rename": return "重命名";
                case "delete": return "删除/备份";
                case "favorite": return "收藏";
                case "search": return "搜索";
                case "tag": return "标签";
                case "backup": return "备份";
                default: return op;
            }
        }

        private string CurrentPath()
        {
            var row = _list.SelectedRow;
            return row == null ? null : row.Tag as string;
        }

        private void UndoLast()
        {
            if (_ops == null) return;
            var last = _ops.LastUndo;
            if (last == null) { _stat.Text = "没有可撤销的批次"; return; }
            if (!Prompt.Confirm(this, "撤销", "撤销「" + last.Title + "」（" + last.Actions.Count + " 个动作）？", Palette)) return;
            string msg;
            bool ok = _ops.Undo(last.Id, out msg);
            MessageBox.Show(this, msg, ok ? "已撤销" : "撤销失败", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            ReloadData();
        }

        protected override void OnActivated() { ReloadData(); }
    }
}
