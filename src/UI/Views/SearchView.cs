using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Fs;

namespace DeskBox.UI.Views
{
    /// <summary>文件搜索：条件式筛选 + 结果列表 + 预览联动。</summary>
    public class SearchView : ViewBase
    {
        private readonly FileIndex _index;
        private readonly FileLibrary _library;
        private readonly FileHistory _history;
        private DarkInput _query;
        private DarkSelect _ext, _size, _time, _sort;
        private DarkButton _btnSearch, _btnRebuild, _btnFavorite, _btnNl;
        private DarkList _list;
        private Label _status;
        private SearchOutcome _last;
        private string _projectRoot;
        private ProjectContext _project;
        private Timer _indexTimer;
        public void SetProjectContext(ProjectContext project)
        {
            bool same = _project == null ? project == null : _project.SameScope(project);
            _project = project;
            if (same) return;
            _query.Text = "";
            SetProjectScope(project == null ? null : project.Root, project == null ? null : project.Name);
        }

        public string FolderScope { get { return _projectRoot; } }
        public event Action<string> FolderScopeChanged;
        private DarkButton _clearProjectScope;
        public void SearchText(string text){_query.Text=text??"";DoSearch();}
        public void FilterType(int index){_ext.SelectedIndex=index;DoSearch();}
        private FlowLayoutPanel _referenceFilters;
        public override void UseReferenceLayout()
        {
            base.UseReferenceLayout();if(_referenceFilters!=null)return;
            var oldToolbar=_query.Parent;_referenceFilters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,BackColor=Palette.Bg,Padding=new Padding(0,0,0,6)};
            foreach(var control in new Control[]{_query,_btnSearch,_ext,_size,_time,_sort,_btnNl,_btnRebuild,_btnFavorite}){control.Margin=new Padding(0,0,8,10);control.Height=34;_referenceFilters.Controls.Add(control);}
            _status.AutoSize=true;_status.Margin=new Padding(0,8,8,4);_referenceFilters.Controls.Add(_status);_referenceFilters.SetFlowBreak(_btnFavorite,true);
            _referenceFilters.Resize+=delegate{_query.Width=Math.Max(180,Math.Min(440,_referenceFilters.Width-100));};Body.Controls.Add(_referenceFilters);oldToolbar.Dispose();_list.BringToFront();
        }
        public void SetProjectScope(string root, string name)
        {
            if (_project != null && !_project.CanAccessPath(root))
            {
                root = _project.Root;
                name = _project.Name;
            }
            _projectRoot = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
            if (_clearProjectScope != null) _clearProjectScope.Visible = _project == null && _projectRoot != null;
            HintLabel.Text = _projectRoot == null ? "搜索全部已索引文件" : "搜索范围：" + name + " · " + _projectRoot;
            if (FolderScopeChanged != null) FolderScopeChanged(_projectRoot);
            DoSearch();
        }
        private List<FileEntry> _current = new List<FileEntry>();

        public event Action<FileEntry> EntrySelected;
        public event Action<FileEntry> EntryActivated;
        public event Action<SearchOutcome> Searched;

        public SearchView(Store store, FileIndex index, FileLibrary library, FileHistory history)
            : base(store, "文件搜索", "支持文件名 / 类型 / 路径 / 时间 / 大小组合筛选，回车执行")
        {
            _index = index;
            _library = library;
            _history = history;
            Build();
            UpdateStatus();
            _indexTimer = new Timer { Interval = 1500 };
            _indexTimer.Tick += delegate { UpdateStatus(); if (_index != null && !_index.IsIndexing) { _indexTimer.Stop(); DoSearch(); } };
            _indexTimer.Start();
            if (_index != null) _index.Changed += IndexChanged;
        }

        private void SafeUi(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a); else a();
            }
            catch { }
        }

        private void Build()
        {
            _clearProjectScope = new DarkButton("返回全局搜索") { Width = 120, Height = 28, Top = 8, Visible = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            _clearProjectScope.Click += delegate { SetProjectScope(null, null); };
            Header.Controls.Add(_clearProjectScope);
            Header.Resize += delegate { _clearProjectScope.Left = Math.Max(180, Header.Width - 130); };
            _clearProjectScope.Left = Math.Max(180, Header.Width - 130);
            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Palette.Bg };

            _query = new DarkInput { Left = 0, Top = 2, Width = 320, Placeholder = "文件名 / 关键词，如 校准 报告" };
            _query.Palette = Palette;
            _query.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };

            _ext = new DarkSelect(new[] { "全部类型", "文档", "表格", "图片", "视频", "音频", "压缩包", "程序", "代码", "PDF" }) { Left = 330, Top = 2, Width = 110 };
            _size = new DarkSelect(new[] { "任意大小", "> 1 MB", "> 10 MB", "> 100 MB", "> 1 GB", "< 1 MB" }) { Left = 448, Top = 2, Width = 110 };
            _time = new DarkSelect(new[] { "任意时间", "今天", "最近 7 天", "最近 30 天", "今年" }) { Left = 566, Top = 2, Width = 110 };
            _sort = new DarkSelect(new[] { "按名称", "按时间↓", "按大小↓", "按路径" }) { Left = 684, Top = 2, Width = 100 };
            foreach (var c in new Control[] { _ext, _size, _time, _sort })
            {
                var d = c as DarkSelect;
                if (d != null) { d.Palette = Palette; }
            }

            _btnSearch = new DarkButton("搜索") { Primary = true, Left = 794, Top = 2, Width = 74 };
            _btnRebuild = new DarkButton("重建索引") { Left = 876, Top = 2, Width = 86 };
            _btnNl = new DarkButton("自然语言") { Left = 970, Top = 2, Width = 86 };
            _btnFavorite = new DarkButton("加入收藏") { Left = 1062, Top = 2, Width = 86 };

            _btnSearch.Click += delegate { DoSearch(); };
            _btnRebuild.Click += delegate { Rebuild(); };
            _btnNl.Click += delegate { NlSearch(); };
            _btnFavorite.Click += delegate { AddFavorite(); };

            _status = new Label
            {
                Left = 0, Top = 44, AutoSize = true, ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg,
                Text = "索引：" + (Store != null ? "准备中" : "")
            };

            top.Controls.AddRange(new Control[] { _query, _ext, _size, _time, _sort, _btnSearch, _btnRebuild, _btnNl, _btnFavorite, _status });

            _list = new DarkList { Dock = DockStyle.Fill };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "名称", "路径", "大小", "修改时间" }, new[] { 300, 520, 110, 150 });
            _list.SelectionChanged += delegate { NotifySelection(); };
            _list.RowActivated += delegate { if (EntryActivated != null && CurrentEntry() != null) EntryActivated(CurrentEntry()); };

            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();

            top.Resize += delegate
            {
                int w = top.Width;
                _btnFavorite.Left = Math.Max(1062, w - 96);
                _btnNl.Left = _btnFavorite.Left - 100;
                _btnRebuild.Left = _btnNl.Left - 96;
                _btnSearch.Left = _btnRebuild.Left - 84;
                _sort.Left = _btnSearch.Left - 110;
                _time.Left = _sort.Left - 122;
                _size.Left = _time.Left - 122;
                _ext.Left = _size.Left - 124;
                _query.Width = Math.Max(160, _ext.Left - 12);
                _status.Top = 44;
            };
        }

        public FileEntry CurrentEntry()
        {
            var row = _list.SelectedRow;
            return row == null ? null : row.Tag as FileEntry;
        }

        private void NotifySelection()
        {
            var e = CurrentEntry();
            if (e != null && EntrySelected != null) EntrySelected(e);
        }

        private void UpdateStatus()
        {
            if (_index == null) return;
            _status.Text = "索引：" + _index.State + " · " + _index.Count + " 条 · 根目录 " + _index.Roots.Count + " 个"
                + (_index.IsIndexing ? "（后台索引中…）" : "");
        }

        public void Rebuild()
        {
            if (_index == null) return;
            _status.Text = "正在重建索引…";
            _index.Rebuild();
            var t = new Timer { Interval = 700 };
            t.Tick += delegate
            {
                UpdateStatus();
                if (!_index.IsIndexing) { t.Stop(); t.Dispose(); DoSearch(); }
            };
            t.Start();
        }

        public void FocusQuery() { _query.Inner.Focus(); _query.Inner.SelectAll(); }

        public void ApplyQuery(SearchQuery q, bool runNow)
        {
            _query.Text = q.Text;
            _ext.SelectedIndex = 0;
            _size.SelectedIndex = 0;
            _time.SelectedIndex = 0;
            if (runNow) DoSearch(q);
        }

        private SearchQuery BuildQuery()
        {
            var q = new SearchQuery();
            q.Text = _query.Text.Trim();
            switch (_ext.SelectedIndex)
            {
                case 1: q.Exts.AddRange(new[] { ".doc", ".docx", ".txt", ".md", ".rtf" }); break;
                case 2: q.Exts.AddRange(new[] { ".xls", ".xlsx", ".csv" }); break;
                case 3: q.Exts.AddRange(new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" }); break;
                case 4: q.Exts.AddRange(new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv" }); break;
                case 5: q.Exts.AddRange(new[] { ".mp3", ".wav", ".flac", ".m4a" }); break;
                case 6: q.Exts.AddRange(new[] { ".zip", ".7z", ".rar", ".gz" }); break;
                case 7: q.Exts.AddRange(new[] { ".exe", ".msi", ".bat", ".cmd", ".ps1" }); break;
                case 8: q.Exts.AddRange(new[] { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".h", ".html", ".css", ".sql", ".json", ".xml" }); break;
                case 9: q.Exts.Add(".pdf"); break;
            }
            switch (_size.SelectedIndex)
            {
                case 1: q.MinSize = 1024 * 1024; break;
                case 2: q.MinSize = 10L * 1024 * 1024; break;
                case 3: q.MinSize = 100L * 1024 * 1024; break;
                case 4: q.MinSize = 1024L * 1024 * 1024; break;
                case 5: q.MaxSize = 1024 * 1024; break;
            }
            long day = 24L * 3600 * 1000;
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            switch (_time.SelectedIndex)
            {
                case 1: q.ModifiedAfter = now - day; break;
                case 2: q.ModifiedAfter = now - 7 * day; break;
                case 3: q.ModifiedAfter = now - 30 * day; break;
                case 4: q.ModifiedAfter = new DateTime(DateTime.Now.Year, 1, 1).ToUniversalTime().Ticks / 10000 - 62135596800000L; break;
            }
            switch (_sort.SelectedIndex)
            {
                case 1: q.SortBy = "mtime"; q.SortDesc = true; break;
                case 2: q.SortBy = "size"; q.SortDesc = true; break;
                case 3: q.SortBy = "path"; break;
                default: q.SortBy = "name"; break;
            }
            q.Limit = 800;
            return q;
        }

        private void IndexChanged() { SafeUi(delegate { if (!IsDisposed) { UpdateStatus(); DoSearch(); } }); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_index != null) _index.Changed -= IndexChanged;
                if (_indexTimer != null) { _indexTimer.Dispose(); _indexTimer = null; }
            }
            base.Dispose(disposing);
        }

        public void DoSearch() { DoSearch(null); }

        public void DoSearch(SearchQuery forced)
        {
            var q = forced ?? BuildQuery();
            if (_projectRoot != null) q.Roots = new List<string> { _projectRoot };
            var project = _project;
            q.PathFilter = project == null ? (Func<string, bool>)null : project.IncludesPath;
            if (_index == null)
            {
                _status.Text = "索引服务未就绪";
                return;
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _last = _index.Search(q);
            sw.Stop();
            _current = new List<FileEntry>();
            var rows = new List<ListRow>();
            foreach (var hit in _last.Hits)
            {
                var e = hit.Entry;
                if (project != null && !project.CanAccessPath(e.Path)) continue;
                _current.Add(e);
                rows.Add(new ListRow
                {
                    Icon = IconFor(e),
                    Cells = new[] { e.Name, e.Path, e.SizeText, UiKit.HumanTime(e.Mtime) },
                    Tag = e,
                    Tip = hit.Highlight
                });
            }
            _list.SetRows(rows);
            _status.Text = "命中 " + _last.TotalMatched + " 条，显示 " + rows.Count + " 条 · 用时 " + sw.ElapsedMilliseconds + " ms"
                + (_index.IsIndexing ? " · 正在索引，结果尚不完整" : "")
                + (string.IsNullOrEmpty(_last.Message) ? "" : " · " + _last.Message);
            if (Searched != null)
            {
                if (project != null) _last.Hits.RemoveAll(hit => !_current.Contains(hit.Entry));
                Searched(_last);
            }
            try { if (_history != null) _history.Record(new FileOpRecord { Id = "q" + DateTime.Now.Ticks, Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Op = "search", Source = q.Text, Ok = true, Detail = "命中 " + _last.TotalMatched }); } catch { }
        }

        public static string IconFor(FileEntry e)
        {
            if (e.IsDir) return "📁";
            switch ((e.Ext ?? "").ToLowerInvariant())
            {
                case ".pdf": return "📕";
                case ".xls": case ".xlsx": case ".csv": return "📊";
                case ".doc": case ".docx": return "📘";
                case ".ppt": case ".pptx": return "📙";
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": case ".webp": return "🖼";
                case ".mp4": case ".mkv": case ".avi": case ".mov": return "🎬";
                case ".mp3": case ".wav": case ".flac": return "🎵";
                case ".zip": case ".7z": case ".rar": return "🗜";
                case ".exe": case ".msi": return "🚀";
                case ".cs": case ".js": case ".ts": case ".py": case ".java": case ".html": case ".css": case ".json": return "⌨";
                default: return "📄";
            }
        }

        private void AddFavorite()
        {
            var e = CurrentEntry();
            if (e == null) { _status.Text = "先选中一条结果再加入收藏"; return; }
            try
            {
                _library.AddFavorite(e.Path, "默认");
                _status.Text = "已加入收藏：" + e.Name;
            }
            catch (Exception ex) { _status.Text = "收藏失败：" + ex.Message; }
        }

        private void NlSearch()
        {
            var text = Prompt.Input(this, "自然语言搜索", "例如：最近修改的 Excel / 大于100MB的视频 / 包含校准记录 的文档", _query.Text, Palette);
            if (string.IsNullOrWhiteSpace(text)) return;
            var q = DeskBox.Ai.NlQuery.Parse(text);
            _status.Text = DeskBox.Ai.NlQuery.Explain(q);
            DoSearch(q);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
        }
    }
}
