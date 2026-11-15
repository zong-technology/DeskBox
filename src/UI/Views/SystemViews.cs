using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Sys;
using DeskBox.Fs;

namespace DeskBox.UI.Views
{
    /// <summary>磁盘空间分析：大文件 / 目录占用 / 重复文件候选。</summary>
    public class DiskView : ViewBase
    {
        private readonly DiskAnalyzer _analyzer;
        private DarkInput _root;
        private DarkButton _btnPick, _btnScan, _btnStop, _btnDelete;
        private DarkSelect _tab;
        private DarkList _list;
        private Label _status;
        private System.Threading.CancellationTokenSource _cts;
        private DiskReport _report;
        private Panel _overview;
        private bool _scanning;

        public event Action<string> OpenRequested;
        public event Action<string> RevealRequested;

        public DiskView(Store store) : base(store, "磁盘分析", "统计真实目录大小、找出大文件与重复文件候选（重复检测较慢）")
        {
            _analyzer = new DiskAnalyzer();
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Palette.Bg };
            top.AutoScroll = true;
            _root = new DarkInput { Left = 0, Top = 2, Width = 420, Placeholder = "要分析的目录，如 D:\\ 或 C:\\Users\\arthur\\Documents" };
            _root.Palette = Palette;
            _btnPick = new DarkButton("选择目录") { Left = 428, Top = 2, Width = 90 };
            _btnPick.Click += delegate
            {
                using (var fb = new FolderBrowserDialog())
                {
                    fb.Description = "选择要分析的目录";
                    if (fb.ShowDialog(this) == DialogResult.OK) _root.Text = fb.SelectedPath;
                }
            };
            _btnScan = new DarkButton("开始分析") { Primary = true, Left = 526, Top = 2, Width = 90 };
            _btnScan.Click += delegate { StartScan(_root.Text); };
            _btnStop = new DarkButton("停止") { Left = 624, Top = 2, Width = 70 };
            _btnStop.Click += delegate { if (_cts != null) _cts.Cancel(); };

            _tab = new DarkSelect(new[] { "大文件", "目录占用", "重复文件候选" }) { Left = 702, Top = 2, Width = 140 };
            _tab.Palette = Palette;
            _tab.SelectedIndexChanged += delegate { Render(); };
            _btnDelete = new DarkButton("移到备份（可恢复）") { Left = 850, Top = 2, Width = 150 };
            _btnDelete.Click += delegate { MoveToBackup(); };

            _status = new Label { Left = 0, Top = 42, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg, Text = "尚未分析" };
            top.Controls.AddRange(new Control[] { _root, _btnPick, _btnScan, _btnStop, _tab, _btnDelete, _status });

            _list = new DarkList { Dock = DockStyle.Fill };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "名称", "路径", "大小", "说明" }, new[] { 300, 520, 120, 160 });
            _list.RowActivated += delegate { var p = CurrentPath(); if (p != null && OpenRequested != null) OpenRequested(p); };
            _overview = new Panel { Dock = DockStyle.Top, Height = 182, BackColor = Palette.Bg };
            _overview.Paint += PaintOverview;

            Body.Controls.Add(_list);
            Body.Controls.Add(_overview);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        public void StartScan(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                _status.Text = "目录不存在：" + root;
                return;
            }
            if (_cts != null) { _cts.Cancel(); }
            _root.Text = root;
            _cts = new System.Threading.CancellationTokenSource();
            var cts = _cts;
            _report = null; _scanning = true; _overview.Invalidate();
            _status.Text = "正在分析 " + root + " …（重复检测会较慢）";
            _list.SetRows(new List<ListRow>());
            var sw = System.Diagnostics.Stopwatch.StartNew();

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                DiskReport rep = null;
                try
                {
                    rep = _analyzer.Analyze(root, new List<string>(), 80, 40, true,
                        delegate(string msg) { SafeUi(delegate { if (!cts.IsCancellationRequested) _status.Text = msg; }); },
                        delegate { return cts.IsCancellationRequested; });
                }
                catch (Exception ex)
                {
                    SafeUi(delegate { if (!ReferenceEquals(_cts, cts)) return; _scanning = false; _overview.Invalidate(); _status.Text = "分析失败：" + ex.Message; });
                    return;
                }
                SafeUi(delegate
                {
                    if (!ReferenceEquals(_cts, cts)) return;
                    _scanning = false;
                    _report = rep;
                    sw.Stop();
                    _status.Text = (cts.IsCancellationRequested ? "已停止，部分结果：文件 " : "完成：文件 ") + rep.FileCount + " 个 / 目录 " + rep.DirCount + " 个 / 总计 " + UiKit.HumanSize(rep.TotalBytes)
                        + " · 重复组 " + rep.Duplicates.Count + " · 用时 " + rep.ElapsedMs + " ms"
                        + (string.IsNullOrEmpty(rep.Error) ? "" : " · " + rep.Error);
                    Render();
                });
            });
        }

        private void SafeUi(Action a)
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        private void Render()
        {
            _overview.Invalidate();
            if (_report == null) return;
            var rows = new List<ListRow>();
            if (_tab.SelectedIndex == 0)
            {
                foreach (var f in _report.LargeFiles)
                    rows.Add(new ListRow { Icon = SearchView.IconFor(new FileEntry { Path = f.Path, Name = Path.GetFileName(f.Path), Ext = Path.GetExtension(f.Path) }),
                        Cells = new[] { Path.GetFileName(f.Path), Path.GetDirectoryName(f.Path), UiKit.HumanSize(f.Size), UiKit.HumanTime(f.Mtime) }, Tag = f.Path });
            }
            else if (_tab.SelectedIndex == 1)
            {
                long total = Math.Max(1, _report.TotalBytes);
                foreach (var d in _report.Dirs)
                    rows.Add(new ListRow { Icon = "📁", Cells = new[] { Path.GetFileName(d.Path) + (Path.GetFileName(d.Path) == "" ? d.Path : ""), d.Path,
                        UiKit.HumanSize(d.Size), (d.Size * 100.0 / total).ToString("0.0") + "% / " + d.FileCount + " 文件" }, Tag = d.Path });
            }
            else
            {
                foreach (var g in _report.Duplicates)
                {
                    foreach (var p in g.Paths)
                        rows.Add(new ListRow { Icon = "⧉", Cells = new[] { Path.GetFileName(p), p, UiKit.HumanSize(g.Size), "同组 " + g.Paths.Count + " 份，可省 " + UiKit.HumanSize(g.Wasted) }, Tag = p });
                }
            }
            _list.SetRows(rows);
        }

        private void PaintOverview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Palette.FillRounded(g, Palette.CardBg, new Rectangle(6, 8, Math.Max(1, _overview.Width - 14), 160), 12);
            using (var title = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
            using (var value = new Font("Consolas", 25, FontStyle.Bold))
            using (var text = new Font("Microsoft YaHei UI", 9))
            {
                TextRenderer.DrawText(g, "STORAGE INTELLIGENCE / 扫描概览", title, new Point(24, 22), Palette.Text);
                if (_report == null)
                {
                    TextRenderer.DrawText(g, _scanning ? "正在扫描真实文件，完成或停止后显示统计。" : "选择项目目录并开始分析，查看文件占用与重复候选。", text, new Point(24, 66), Palette.TextDim); return;
                }
                TextRenderer.DrawText(g, UiKit.HumanSize(_report.TotalBytes), value, new Point(24, 52), Palette.Accent);
                TextRenderer.DrawText(g, _report.FileCount + " 文件 / " + _report.DirCount + " 目录", text, new Point(26, 99), Palette.TextDim);
                TextRenderer.DrawText(g, "重复候选可省 " + UiKit.HumanSize(_report.Duplicates.Sum(x => x.Wasted)), text, new Point(26, 126), Palette.TextDim);
                int left = Math.Max(360, _overview.Width * 42 / 100), width = Math.Max(80, _overview.Width - left - 40);
                var dirs = _report.Dirs.OrderByDescending(x => x.Size).Take(4).ToArray();
                for (int i = 0; i < dirs.Length; i++)
                {
                    int y = 27 + i * 32;
                    TextRenderer.DrawText(g, Path.GetFileName(dirs[i].Path) + " · " + UiKit.HumanSize(dirs[i].Size), text, new Rectangle(left, y, width, 20), Palette.TextDim, TextFormatFlags.EndEllipsis);
                    var bar = new Rectangle(left, y + 21, width, 5); Palette.FillRounded(g, Palette.PanelAlt, bar, 2);
                    int fill = (int)(width * Math.Min(1, (double)dirs[i].Size / Math.Max(1, _report.TotalBytes)));
                    if (fill > 0) Palette.FillRounded(g, Palette.Accent, new Rectangle(left, bar.Y, fill, 5), 2);
                }
            }
        }

        private string CurrentPath()
        {
            var row = _list.SelectedRow;
            return row == null ? null : row.Tag as string;
        }

        private void MoveToBackup()
        {
            var row = _list.SelectedRow;
            if (row == null) { _status.Text = "先选中一项"; return; }
            var p = row.Tag as string;
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) { _status.Text = "只能移动文件"; return; }
            if (!Prompt.Confirm(this, "移到备份", "把 " + Path.GetFileName(p) + " 移到 DeskBox 备份目录？\r\n可在操作历史中撤销。", Palette)) return;
            var ops = new FileOps(Store);
            string msg;
            bool ok = ops.DeleteToBackup(new List<string> { p }, out msg);
            _status.Text = (ok ? "已移动：" : "失败：") + msg;
        }

        protected override void OnActivated()
        {
            if (string.IsNullOrWhiteSpace(_root.Text) && _report == null) _root.Text = Store != null ? DesktopScanner.ResolveDesktopDir(Store.Settings) : "D:\\";
        }
        protected override void Dispose(bool disposing) { if (disposing && _cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; } base.Dispose(disposing); }
    }

    /// <summary>系统监控：CPU / 内存 / 磁盘 / 网络（全部来自系统真实接口）。</summary>
    public class MonitorView : ViewBase
    {
        private readonly SystemMonitor _monitor;
        private SystemSample _last;
        private Timer _ui;

        public MonitorView(Store store, SystemMonitor monitor) : base(store, "系统监控", "CPU 来自 GetSystemTimes、内存来自 GlobalMemoryStatusEx、网络来自网卡计数器")
        {
            _monitor = monitor;
            _last = monitor == null ? null : monitor.Last;
            Build();
            _ui = new Timer { Interval = 1000 };
            _ui.Tick += delegate { if (Visible && _last != null) Body.Invalidate(); };
            _ui.Start();
            if (_monitor != null) _monitor.Updated += OnSample;
        }

        private void OnSample(SystemSample sample) { SafeUi(delegate { _last = sample; if (Visible) Body.Invalidate(); }); }
        protected override void OnActivated() { if (_monitor != null) _last = _monitor.Last; Body.Invalidate(); }

        private void SafeUi(Action a)
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        private void Build() { Body.Paint += delegate(object s, PaintEventArgs e) { PaintBody(e.Graphics); }; }

        private void PaintBody(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.Bg);
            if (_last == null)
            {
                using (var f = new Font("Microsoft YaHei UI", 9.5F))
                    g.DrawString("正在采样…", f, new SolidBrush(Palette.TextFaint), 20, 20);
                return;
            }
            int x = 8, y = 8, w = Math.Max(220, (Body.Width - 40) / 3), h = 150;
            DrawGauge(g, new Rectangle(x, y, w, h), "CPU", _last.CpuPercent, "%", Palette.Accent);
            DrawGauge(g, new Rectangle(x + w + 12, y, w, h), "内存", _last.MemUsedPercent, "%",
                Color.FromArgb(167, 139, 250), UiKit.HumanSize(_last.MemUsed) + " / " + UiKit.HumanSize(_last.MemTotal));
            DrawGauge(g, new Rectangle(x + (w + 12) * 2, y, w, h), "网络", -1, "", Color.FromArgb(52, 211, 153),
                "↓ " + UiKit.HumanSize((long)_last.NetRxBps) + "/s   ↑ " + UiKit.HumanSize((long)_last.NetTxBps) + "/s");

            y += h + 20;
            using (var f = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
                g.DrawString("磁盘卷", f, new SolidBrush(Palette.Text), x, y);
            y += 28;
            foreach (var d in _last.Disks)
            {
                var bar = new Rectangle(x, y + 18, Body.Width - 40, 10);
                Palette.FillRounded(g, Palette.PanelAlt, bar, 5);
                int fill = (int)(bar.Width * d.UsedPct / 100.0);
                if (fill > 0) Palette.FillRounded(g, d.UsedPct > 90 ? Color.FromArgb(248, 113, 113) : Palette.Accent, new Rectangle(bar.X, bar.Y, fill, bar.Height), 5);
                using (var f = new Font("Microsoft YaHei UI", 9F))
                using (var fs = new Font("Microsoft YaHei UI", 8.5F))
                {
                    g.DrawString(d.Name + "  " + (string.IsNullOrEmpty(d.Label) ? "" : d.Label), f, new SolidBrush(Palette.Text), bar.X, y - 4);
                    g.DrawString(UiKit.HumanSize(d.Total - d.Free) + " / " + UiKit.HumanSize(d.Total) + "（" + d.UsedPct.ToString("0.0") + "%）", fs,
                        new SolidBrush(Palette.TextFaint), bar.Right - 220, y);
                }
                y += 44;
            }
            if (!string.IsNullOrEmpty(_last.Error))
            {
                using (var f = new Font("Microsoft YaHei UI", 8.5F))
                    g.DrawString("采样错误：" + _last.Error, f, new SolidBrush(Color.FromArgb(248, 113, 113)), x, y);
            }
        }

        private void DrawGauge(Graphics g, Rectangle r, string title, double pct, string unit, Color color, string sub = "")
        {
            UiKit.DrawCard(g, Palette, r, 12);
            using (var f = new Font("Microsoft YaHei UI", 9F))
            using (var fb = new Font("Segoe UI", 26F, FontStyle.Bold))
            using (var fs = new Font("Microsoft YaHei UI", 8.5F))
            {
                g.DrawString(title, f, new SolidBrush(Palette.TextDim), r.X + 16, r.Y + 12);
                if (pct >= 0)
                {
                    var txt = pct.ToString("0") + unit;
                    g.DrawString(txt, fb, new SolidBrush(color), r.X + 14, r.Y + 34);
                }
                else
                {
                    g.DrawString("速率", fb, new SolidBrush(color), r.X + 14, r.Y + 40);
                }
                var bar = new Rectangle(r.X + 16, r.Bottom - 34, r.Width - 32, 8);
                Palette.FillRounded(g, Palette.PanelAlt, bar, 4);
                if (pct >= 0)
                {
                    int fill = (int)(bar.Width * Math.Min(100, pct) / 100.0);
                    if (fill > 0) Palette.FillRounded(g, color, new Rectangle(bar.X, bar.Y, fill, bar.Height), 4);
                }
                g.DrawString(Palette.Ellipsis(g, sub, fs, r.Width - 32), fs, new SolidBrush(Palette.TextFaint), r.X + 16, r.Bottom - 22);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_monitor != null) _monitor.Updated -= OnSample;
            try { if (_ui != null) { _ui.Stop(); _ui.Dispose(); } } catch { }
            base.Dispose(disposing);
        }
    }

    /// <summary>剪贴板历史 + 常用片段（敏感内容自动屏蔽）。</summary>
    public class ClipboardView : ViewBase
    {
        private readonly ClipboardWatcher _watch;
        private DarkList _list;
        private DarkButton _btnCopy, _btnPause, _btnClear;
        private Label _stat;

        public ClipboardView(Store store, ClipboardWatcher watch) : base(store, "剪贴板", "自动记录复制的文本/文件；疑似密码、卡号等敏感内容不落盘")
        {
            _watch = watch;
            Build();
            if (_watch != null)
            {
                _watch.Changed += delegate { SafeUi(ReloadData); };
                _watch.Start();
            }
        }

        

        private void CopySelected()
        {
            var row = _list.SelectedRow;
            if (row == null || _watch == null) return;
            var e = row.Tag as ClipEntry;
            if (e == null) return;
            _stat.Text = _watch.CopyToClipboard(e.Id) ? "已复制到剪贴板" : "复制失败（可能被占用）";
        }

        private void SafeUi(Action a)
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            _btnCopy = new DarkButton("复制回剪贴板") { Primary = true, Left = 0, Top = 2, Width = 120 };
            _btnCopy.Click += delegate { CopySelected(); };
            _btnPause = new DarkButton("暂停监听") { Left = 128, Top = 2, Width = 100 };
            _btnPause.Click += delegate
            {
                if (_watch == null) return;
                _watch.Pause(!_watch.Paused);
                _btnPause.Text = _watch.Paused ? "恢复监听" : "暂停监听";
                _stat.Text = _watch.Paused ? "已暂停" : "监听中";
            };
            _btnClear = new DarkButton("清空") { Left = 236, Top = 2, Width = 70 };
            _btnClear.Click += delegate { if (_watch != null) { _watch.Clear(); ReloadData(); } };
            _stat = new Label { Left = 320, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            top.Controls.AddRange(new Control[] { _btnCopy, _btnPause, _btnClear, _stat });

            _list = new DarkList { Dock = DockStyle.Fill };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "时间", "类型", "内容" }, new[] { 150, 90, 700 });
            _list.RowActivated += delegate { CopySelected(); };

            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        public void ReloadData()
        {
            if (_watch == null) return;
            var rows = new List<ListRow>();
            foreach (var c in _watch.History)
            {
                var text = c.Sensitive ? "（敏感内容已屏蔽）" : (c.Text ?? "").Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 120) text = text.Substring(0, 120) + "…";
                rows.Add(new ListRow
                {
                    Icon = c.Kind == "files" ? "📁" : "📋",
                    Cells = new[] { UiKit.HumanTime(c.Ts), c.Kind == "files" ? "文件" : "文本", text },
                    Tag = c,
                    Accent = c.Sensitive ? Color.FromArgb(248, 113, 113) : (Color?)null
                });
            }
            _list.SetRows(rows);
            _stat.Text = _watch.History.Count + " 条记录" + (_watch.Paused ? "（已暂停）" : "");
        }

        protected override void OnActivated() { ReloadData(); }
    }

}
