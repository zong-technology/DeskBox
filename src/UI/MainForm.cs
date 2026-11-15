using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Native;
using DeskBox.UI.Views;
using DeskBox.Shell;
using DeskBox.Engineering;
using DeskBox.Ai;
using DeskBox.Sync;
using System.IO;

namespace DeskBox.UI
{
    /// <summary>DeskBox 主窗口：桌面层全屏面板（顶部工具栏 + 分类栏 + 图标板 + 小部件栏 + 状态栏）。</summary>
    public class MainForm : Form
    {
        private const int GlobalCommandHotkeyId = 0xDB01;
        private const int GlobalWinFHotkeyId = 0xDB02;
        private WinFShortcut _winFShortcut;
        private readonly Store _store;
        private List<DesktopItem> _items = new List<DesktopItem>();
        private string _desktopPath = "";

        private Panel _topbar, _statusbar, _desktopSearchHost, _folderNav;
        private Panel _rail, _contentHost, _desktopHost;
        private Label _desktopMeta;
        private DarkButton _desktopMode, _engineeringMode, _previewToggle;
        private Sidebar _sidebar;
        private IconBoard _board;
        private WidgetsPanel _widgets;
        private PreviewPanel _preview;
        private TextBox _search;
        private Button _btnRefresh, _btnSettings, _btnView, _btnSort, _btnHiddenToggle, _btnCommand, _btnAi, _btnMobile, _btnNotifications, _btnBack, _btnForward, _btnUp, _btnDesktopHome, _btnNew, _btnNavToggle;
        private Label _lblPath, _lblCount, _lblConn, _breadcrumb, _topContext;
        private ToolTip _toolTip;
        private Timer _clockTimer, _scanTimer, _connTimer, _reminderTimer, _noticeStatusTimer;

        // 新模块服务
        private DeskBox.Fs.FileIndex _index;
        private DeskBox.Fs.FileLibrary _library;
        private DeskBox.Fs.FileOps _ops;
        private DeskBox.Fs.FileHistory _history;
        private DeskBox.Sys.SystemMonitor _monitor;
        private DeskBox.Sys.ClipboardWatcher _clipboard;
        private DeskBox.Automation.WorkflowEngine _workflows;
        private DeskBox.Ai.ProviderStore _providers;
        private DeskBox.Ai.ChatService _chat;
        private NotificationStore _notifications;
        private DeskBox.Engineering.WorkspaceStore _workspaces;
        private FolderSession _folderSession;
        private DesktopWatcher _desktopWatcher;
        private ShellFileOperationService _shellFiles;
        private ShellThumbnailService _thumbnails;
        private LocalSyncCoordinator _localSync;
        private CloudSyncAgent _cloudSync;
        private ReferenceWorkspace _reference;
        private bool _referenceActive;
        private bool _closing;
        private readonly Dictionary<string, UserControl> _views = new Dictionary<string, UserControl>();
        private string _currentView = "home";
        private readonly List<RailItem> _railItems = new List<RailItem>();

        public Palette Palette { get; private set; }
        public bool SystemIconsHidden { get; private set; }
        public bool IsOnDesktopLayer { get; private set; }

        public MainForm(Store store)
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            _store = store;
            Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            BuildUi();
            ApplyTheme();
            EnsureServices();
            if(_store.Settings.ReferenceUi)SetReferenceWorkspace(true);
            UpdateGlobalControls();
            LoadItems(false);
            SwitchView("home");
        }

        private void BuildUi()
        {
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.WorkingArea;
            DoubleBuffered = true;
            Text = "DeskBox";
            AllowDrop = false;
            TopMost = false;

            _topbar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent };
            _statusbar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.Transparent };
            _sidebar = new Sidebar(_store) { Dock = DockStyle.Left, Width = 208 };
            _widgets = new WidgetsPanel(_store) { Dock = DockStyle.Right, Width = 330 };
            _board = new IconBoard(_store) { Dock = DockStyle.Fill };
            _board.BackdropPainter = (g, _) => PaintBackdrop(g, _board.Location, _board.Size);

            // ---- 顶栏 ----
            var title = new Label
            {
                Text = "DeskBox",
                Left = 46,
                AutoSize = false,
                Width = 150,
                Height = 52,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ForeColor = Palette.Text,
                BackColor = Color.Transparent
            };

            _btnNavToggle = MakeMiniButton("☰", 8);
            _btnNavToggle.Top = 12;
            _btnNavToggle.Click += delegate { ToggleNavigation(); };

            _search = new TextBox
            {
                Width = 240,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                BackColor = Palette.PanelAlt,
                ForeColor = Palette.Text,
                Top = 16,
                Left = 170
            };
            _desktopSearchHost = new Panel { Width = 250, Height = 30, Top = 11, Left = 680, BackColor = Palette.PanelAlt };
            _desktopSearchHost.Controls.Add(_search);
            _search.Width = 240;
            _search.Left = 8;
            _search.Top = 6;

            var ph = new Label
            {
                Text = "搜索桌面条目…",
                Left = 10,
                Top = 7,
                AutoSize = true,
                ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                BackColor = Palette.PanelAlt,
                Cursor = Cursors.IBeam
            };
            ph.Click += (a, b) => _search.Focus();
            _desktopSearchHost.Controls.Add(ph);
            ph.BringToFront();
            _search.TextChanged += (a, b) => { ph.Visible = _search.Text.Length == 0; };
            _search.GotFocus += (a, b) => { ph.Visible = false; };
            _search.LostFocus += (a, b) => { ph.Visible = _search.Text.Length == 0; };

            _folderNav = new Panel { Width = 460, Height = 32, Top = 10, Left = 210, BackColor = Color.Transparent };
            _btnBack = MakeMiniButton("‹", 0);
            _btnForward = MakeMiniButton("›", 30);
            _btnUp = MakeMiniButton("↑", 60);
            _btnDesktopHome = MakeMiniButton("⌂", 90);
            _btnNew = MakeMiniButton("＋", 120);
            _breadcrumb = new Label { Left = 154, Top = 6, Width = 300, Height = 22, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Palette.TextDim, BackColor = Palette.PanelAlt, Font = new Font("Microsoft YaHei UI", 8.5F), Padding = new Padding(8, 0, 4, 0) };
            _folderNav.Controls.AddRange(new Control[] { _btnBack, _btnForward, _btnUp, _btnDesktopHome, _btnNew, _breadcrumb });

            _btnView = MakeToolButton("▦ 网格", 0);
            _btnSort = MakeToolButton("名称 ↑", 0);
            _btnHiddenToggle = MakeToolButton("显示隐藏项", 0);
            _btnRefresh = MakeToolButton("⟳ 刷新", 0);
            _btnSettings = MakeToolButton("⚙ 设置", 0);
            _btnCommand = MakeToolButton("⌁ 命令 Ctrl+K", 0);
            _btnView.Width = 72; _btnSort.Width = 76; _btnHiddenToggle.Width = 76; _btnRefresh.Width = 66;
            _btnCommand.Width = 112; _btnSettings.Width = 42; _btnSettings.Text = "⚙";
            _btnAi = MakeToolButton("AI", 0); _btnAi.Width = 42;
            _btnMobile = MakeToolButton("⇄", 0); _btnMobile.Width = 42;
            _btnNotifications = MakeToolButton("N", 0); _btnNotifications.Width = 42;
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_btnAi, "AI&SI 工程助手");
            _toolTip.SetToolTip(_btnMobile, "手机联动与本地同步");
            _toolTip.SetToolTip(_btnNotifications, "通知中心");
            _toolTip.SetToolTip(_btnSettings, "设置中心");

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 650,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 12, 10, 0)
            };
            flow.Controls.Add(_btnView);
            flow.Controls.Add(_btnSort);
            flow.Controls.Add(_btnHiddenToggle);
            flow.Controls.Add(_btnRefresh);
            flow.Controls.Add(_btnAi);
            flow.Controls.Add(_btnMobile);
            flow.Controls.Add(_btnNotifications);
            flow.Controls.Add(_btnCommand);
            flow.Controls.Add(_btnSettings);

            _topContext = new Label { Left = 170, Top = 0, Width = 420, Height = 52, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = Palette.TextDim, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 9F) };
            _topbar.Resize += delegate { _topContext.Width = Math.Max(180, Math.Min(620, _topbar.Width - 850)); };

            _topbar.Controls.Add(_btnNavToggle);
            _topbar.Controls.Add(title);
            _topbar.Controls.Add(_topContext);
            _topbar.Controls.Add(_folderNav);
            _topbar.Controls.Add(_desktopSearchHost);
            _topbar.Controls.Add(flow);

            // ---- 状态栏 ----
            _lblPath = new Label
            {
                Text = "桌面：—", AutoSize = true, ForeColor = Palette.TextDim,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Color.Transparent,
                Location = new Point(14, 6)
            };
            _lblCount = new Label
            {
                Text = "0 个条目", AutoSize = true, ForeColor = Palette.TextDim,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Color.Transparent,
                Location = new Point(420, 6)
            };
            _lblConn = new Label
            {
                Text = "PROJECT —  |  SYNC LOCAL ONLY  |  AI —  |  DB JSON LOCAL  |  JOBS 0  |  MOBILE OFF", AutoSize = false, Width = 900, Height = 24, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(52, 211, 153),
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Color.Transparent,
                Anchor = AnchorStyles.Right
            };
            _statusbar.Controls.Add(_lblPath);
            _statusbar.Controls.Add(_lblCount);
            _statusbar.Controls.Add(_lblConn);
            _statusbar.Resize += (s, e) => { _lblConn.Width = Math.Max(500, _statusbar.Width - 700); _lblConn.Left = _statusbar.Width - _lblConn.Width - 16; };

            // ---- 模块导航 + 内容区（桌面视图保留 sidebar + board）----
            _rail = new Panel { Dock = DockStyle.Left, Width = _store.Settings.NavExpanded ? 196 : 86, BackColor = Palette.HeaderBg, AutoScroll = true };
            BuildRail();

            _desktopHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
            _sidebar.Dock = DockStyle.Left;
            _board.Dock = DockStyle.Fill;
            _desktopHost.Controls.Add(_board);
            _desktopHost.Controls.Add(_sidebar);
            _desktopHost.Controls.Add(_desktopShelf = BuildDesktopShelf());

            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
            _contentHost.Controls.Add(_desktopHost);

            _preview = new PreviewPanel(_store) { Dock = DockStyle.Right, Width = 0 };
            _preview.OpenRequested += delegate(string p) { OpenPathExternal(p); };
            _preview.RevealRequested += delegate(string p) { try { Win32.RevealPath(p); } catch { } };
            _preview.FavoriteRequested += delegate(string p) { try { _library.AddFavorite(p, "默认"); } catch { } };
            _preview.AiRequested += delegate(string p) { AskAiForPaths(new[] { p }); };

            Controls.Add(_contentHost);
            Controls.Add(_preview);
            Controls.Add(_widgets);
            Controls.Add(_rail);
            Controls.Add(_statusbar);
            Controls.Add(_topbar);
            ResumeLayout(false);

            // ---- 事件 ----
            _search.TextChanged += (s, e) => _board.SetQuery(_search.Text);
            _btnRefresh.Click += (s, e) => LoadItems(true);
            _btnSettings.Click += (s, e) => OpenSettings();
            _btnCommand.Click += (s, e) => ShowCommandPalette();
            _btnAi.Click += delegate { SwitchView("ai"); if (_chatView != null) BeginInvoke(new Action(_chatView.FocusInput)); };
            _btnMobile.Click += delegate { SwitchView("mobile"); };
            _btnNotifications.Click += delegate { SwitchView("notifications"); };
            _btnView.Click += (s, e) => CycleView();
            _btnSort.Click += (s, e) => CycleSort();
            _btnHiddenToggle.Click += (s, e) => ToggleShowHidden();
            _btnBack.Click += delegate { if (_folderSession != null) _folderSession.Back(); };
            _btnForward.Click += delegate { if (_folderSession != null) _folderSession.Forward(); };
            _btnUp.Click += delegate { if (_folderSession != null) _folderSession.Up(); };
            _btnDesktopHome.Click += delegate { if (_folderSession != null) _folderSession.Home(); };
            _btnNew.Click += delegate { ShowNewMenu(_btnNew, new Point(0, _btnNew.Height)); };
            _breadcrumb.Click += delegate { ShowLocationPrompt(); };

            _sidebar.FilterChanged += id => { _board.SetFilter(id); _board.ClearSelection(); };
            _sidebar.NewCategoryRequested += NewCategory;
            _sidebar.CategoryContextRequested += CategoryMenu;

            _board.ItemOpened += OpenItem;
            _board.SelectionChanged += delegate(DesktopItem it)
            {
                if (_referenceActive && _currentView == "desktop" && _referenceDesktop != null)
                {
                    ShowReferenceDesktopSelection(it);
                    return;
                }
                if (_currentView == "desktop" && it != null && _store.Settings.DesktopPreviewEnabled)
                {
                    _preview.Width = 330;
                    _preview.ShowPreview(it.Path);
                }
                else if (_currentView == "desktop") _preview.ClosePreview();
            };
            _board.ItemDropped += OnItemDropped;
            _board.MouseUp += BoardMouseUp;
            _board.ContextRequested += ShowDesktopContextMenu;
            _board.CommandRequested += ExecuteDesktopCommand;
            _board.ExternalFilesDropped += OnExternalFilesDropped;

            _widgets.SettingsRequested += (s, e) => OpenSettings();

            _clockTimer = new Timer { Interval = 1000 };
            _clockTimer.Tick += (s, e) => { UpdateConnLabel(); UpdateGlobalControls(); };
            _clockTimer.Start();

            _scanTimer = new Timer { Interval = 30000 };
            _scanTimer.Tick += (s, e) => LoadItems(false);
            _scanTimer.Start();

            _connTimer = new Timer { Interval = 60000 };
            _connTimer.Tick += (s, e) => UpdateConnLabel();
            _connTimer.Start();

            KeyPreview = true;
            KeyDown += OnFormKeyDown;
        }

        private Panel BuildDesktopShelf()
        {
            var shelf = new Panel { Dock = DockStyle.Top, Height = 102, BackColor = Palette.HeaderBg };
            var title = new Label { Left = 18, Top = 12, Width = 440, Height = 30, Text = "SMART DESKTOP / 工程文件工作台", ForeColor = Palette.Text, Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold) };
            _desktopMeta = new Label { Left = 18, Top = 55, Width = 700, Height = 34, AutoEllipsis = true, ForeColor = Palette.TextDim, Font = new Font("Microsoft YaHei UI", 9) };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 480, Padding = new Padding(6, 12, 6, 4), WrapContents = true };
            _desktopMode = new DarkButton("桌面模式") { Width = 140, Height = 30, Palette = Palette, Primary = !_store.Settings.EngineeringDesktop };
            _engineeringMode = new DarkButton("工程工作区") { Width = 140, Height = 30, Palette = Palette, Primary = _store.Settings.EngineeringDesktop };
            var projectFolder = new DarkButton("当前项目目录") { Width = 140, Height = 30, Palette = Palette };
            var categories = new DarkButton("显示/隐藏分类") { Width = 140, Height = 30, Palette = Palette };
            _previewToggle = new DarkButton("显示/隐藏预览") { Width = 140, Height = 30, Palette = Palette, Primary = _store.Settings.DesktopPreviewEnabled };
            var command = new DarkButton("文件命令 Ctrl+K") { Width = 140, Height = 30, Palette = Palette };
            _desktopMode.Click += delegate { SetDesktopWorkspace(false); };
            _engineeringMode.Click += delegate { SetDesktopWorkspace(true); };
            projectFolder.Click += delegate
            {
                var project = _workspaces == null ? null : _workspaces.Current;
                if (project == null || !Directory.Exists(project.Root)) { _lblConn.Text = "请先在项目舱配置有效项目目录"; return; }
                _folderSession.Navigate(project.Root);
            };
            categories.Click += delegate { _store.Settings.ShowSidebar = !_store.Settings.ShowSidebar; _store.SaveSettingsOnly(); _sidebar.Visible = _store.Settings.ShowSidebar; };
            _previewToggle.Click += delegate
            {
                _store.Settings.DesktopPreviewEnabled = !_store.Settings.DesktopPreviewEnabled; _store.SaveSettingsOnly();
                _previewToggle.Primary = _store.Settings.DesktopPreviewEnabled; _previewToggle.Invalidate();
                _preview.Width = _store.Settings.DesktopPreviewEnabled ? 330 : 0;
            };
            command.Click += delegate { ShowCommandPalette(); };
            actions.Controls.AddRange(new Control[] { _desktopMode, _engineeringMode, projectFolder, categories, _previewToggle, command });
            shelf.Controls.Add(title); shelf.Controls.Add(_desktopMeta); shelf.Controls.Add(actions);
            shelf.Resize += delegate { int textWidth = Math.Max(120, shelf.Width - actions.Width - 42); title.Width = textWidth; _desktopMeta.Width = textWidth; };
            shelf.Paint += delegate(object sender, PaintEventArgs e) { using (var pen = new Pen(Palette.Line)) e.Graphics.DrawLine(pen, 12, shelf.Height - 1, shelf.Width - 12, shelf.Height - 1); };
            return shelf;
        }

        private void SetDesktopWorkspace(bool engineering)
        {
            if (_referenceActive && _currentView == "desktop" && _referenceDesktop != null)
            {
                _referenceEngineering = engineering;
                _board.UseReferenceDesktop(engineering);
                RefreshReferenceDesktop();
                return;
            }
            _board.SetEngineeringMode(engineering);
            _desktopMode.Primary = !engineering; _engineeringMode.Primary = engineering;
            _desktopMode.Invalidate(); _engineeringMode.Invalidate();
        }

        private Button MakeToolButton(string text, int left)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = false,
                Width = 96,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Palette.TextDim,
                BackColor = Palette.PanelAlt,
                Margin = new Padding(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Palette.Line;
            b.FlatAppearance.MouseOverBackColor = Palette.Hover;
            return b;
        }

        private Button MakeMiniButton(string text, int left)
        {
            var b = new Button { Text = text, Left = left, Top = 1, Width = 28, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Palette.PanelAlt, ForeColor = Palette.TextDim, Font = new Font("Segoe UI Symbol", 9F), Cursor = Cursors.Hand, TabStop = false };
            b.FlatAppearance.BorderColor = Palette.Line;
            b.FlatAppearance.MouseOverBackColor = Palette.Hover;
            return b;
        }

        // ---------- 数据 ----------
        public void LoadItems(bool rescan)
        {
            var ids = _store.Categories.Select(c => c.Id);
            var atHome = _folderSession == null || _folderSession.Current == FolderSession.DesktopHome;
            var watchDirectories = atHome ? DesktopScanner.ResolveDesktopDirs(_store.Settings) : new List<string> { _folderSession.Current };
            _items = atHome
                ? DesktopScanner.Scan(_store.Settings, _store.Overrides, ids)
                : DesktopScanner.ScanDirectory(_folderSession.Current, _store.Overrides, ids);
            _desktopPath = atHome ? DesktopScanner.ResolveDesktopDir(_store.Settings) : _folderSession.Current;
            _board.SetData(_items);
            if (_desktopMeta != null) _desktopMeta.Text = _items.Count + " 个真实文件与目录\r\n" + _desktopPath;
            _sidebar.SetData(_items);
            _widgets.Bind(_items, _desktopPath);
            _lblPath.Text = (atHome ? "桌面：" : "位置：") + _desktopPath;
            _lblCount.Text = _items.Count + " 个条目 · 已隐藏 " + _items.Count(i => i.Hidden);
            if (_desktopWatcher != null) _desktopWatcher.Watch(watchDirectories);
            UpdateFolderNavigation();
            if (_referenceActive && _currentView == "desktop") RefreshReferenceDesktop();
            if (rescan) _store.Log("info", "scan", "刷新 " + (atHome ? "桌面" : _desktopPath) + "，共 " + _items.Count + " 个条目");
            Invalidate();
        }

        private void OnFolderLocationChanged()
        {
            if (_closing) return;
            try { if (InvokeRequired) BeginInvoke(new Action(delegate { LoadItems(false); })); else LoadItems(false); } catch { }
        }

        private void SafeUiRefreshDesktop()
        {
            if (_closing || IsDisposed) return;
            try { BeginInvoke(new Action(delegate { if (_currentView == "desktop") LoadItems(false); })); } catch { }
        }

        private void UpdateFolderNavigation()
        {
            if (_folderSession == null || _breadcrumb == null) return;
            bool home = _folderSession.Current == FolderSession.DesktopHome;
            _breadcrumb.Text = home ? "Desktop · 用户 + Public" : _folderSession.Current;
            _btnBack.Enabled = _folderSession.CanBack;
            _btnForward.Enabled = _folderSession.CanForward;
            _btnUp.Enabled = _folderSession.CanUp;
            _btnDesktopHome.Enabled = !home;
        }

        // ---------------- 新模块：导航、服务、视图 ----------------

        private class RailItem
        {
            public string Id = "";
            public string Icon = "";
            public string Title = "";
            public Control Button;
        }

        private void BuildRail()
        {
            string[,] items = new string[,]
            {
                { "home", "◫", "驾驶舱" },
                { "desktop", "▦", "智能桌面" },
                { "projects", "◆", "项目舱" },
                { "engineering", "⌁", "工程工具" },
                { "ai", "◈", "AI&SI" },
                { "automation", "⚡", "自动化" },
                { "search", "🔍", "文件管理" },
                { "tasks", "☑", "任务" },
                { "incidents", "!", "现场事件" },
                { "weather", "☁", "天气" },
                { "monitor", "📈", "监控" },
                { "mobile", "▣", "手机联动" },
                { "history", "🕘", "工作记录" },
                { "notifications", "🔔", "通知" },
                { "filehistory", "↶", "文件历史" },
                { "library", "★", "收藏" },
                { "disk", "💽", "磁盘" },
                { "clipboard", "📋", "剪贴板" },
                { "settings", "⚙", "设置" },
            };
            int y = 8;
            for (int i = 0; i < items.GetLength(0); i++)
            {
                var item = new RailItem { Id = items[i, 0], Icon = items[i, 1], Title = items[i, 2] };
                var b = new RailButton(item.Icon, item.Title, Palette) { Top = y, Left = 4, Tag = item.Id, Expanded = _store.Settings.NavExpanded, Width = _store.Settings.NavExpanded ? 172 : 64 };
                b.Click += delegate(object s, EventArgs e) { SwitchView((string)((Control)s).Tag); };
                _rail.Controls.Add(b);
                item.Button = b;
                _railItems.Add(item);
                y += 56;
            }
        }

        private void ToggleNavigation()
        {
            _store.Settings.NavExpanded = !_store.Settings.NavExpanded;
            _store.SaveSettingsOnly();
            _rail.Width = _store.Settings.NavExpanded ? 196 : 86;
            foreach (var item in _railItems)
            {
                var button = item.Button as RailButton;
                if (button == null) continue;
                button.Expanded = _store.Settings.NavExpanded;
                button.Width = _store.Settings.NavExpanded ? 172 : 64;
                button.Invalidate();
            }
            _btnNavToggle.Text = _store.Settings.NavExpanded ? "«" : "☰";
        }

        private void EnsureServices()
        {
            if (_index != null) return;
            try
            {
                _folderSession = new FolderSession();
                _folderSession.Changed += OnFolderLocationChanged;
                _desktopWatcher = new DesktopWatcher();
                _desktopWatcher.Changed += delegate { SafeUiRefreshDesktop(); };
                _shellFiles = new ShellFileOperationService();
                _shellFiles.Progress += delegate(int done, int total, string item)
                {
                    try { BeginInvoke(new Action(delegate { _lblConn.Text = "FILE " + done + "/" + total + " · " + item; })); } catch { }
                };
            }
            catch (Exception ex) { AppLog.Write("error", "system", "智能桌面服务初始化失败: " + ex.Message); }
            try
            {
                // 真实 Shell 缩略图：有界后台队列 + 有界 LRU 缓存，失败回退 SHGetFileInfo 图标
                _thumbnails = new ShellThumbnailService();
                if (_board != null) _board.Thumbnails = _thumbnails;
            }
            catch (Exception ex) { AppLog.Write("warn", "thumb", "缩略图服务初始化失败，回退系统图标: " + ex.Message); }
            try
            {
                _notifications = new NotificationStore(_store);
                _notifications.Published += OnNotificationPublished;
                _notifications.Changed += OnGlobalContextChanged;
            }
            catch (Exception ex) { AppLog.Write("error", "system", "通知中心初始化失败: " + ex.Message); }
            try
            {
                _workspaces = new DeskBox.Engineering.WorkspaceStore();
                _workspaces.Changed += OnGlobalContextChanged;
            }
            catch (Exception ex) { AppLog.Write("error", "system", "工程工作区初始化失败: " + ex.Message); }
            try
            {
                _localSync = new LocalSyncCoordinator(_store, _workspaces);
                _cloudSync = new CloudSyncAgent(_localSync);
                _cloudSync.Changed += OnGlobalContextChanged;
                _localSync.Changed += OnGlobalContextChanged;
                if (_store.Settings.LocalSyncEnabled) _localSync.Start();
            }
            catch (Exception ex) { AppLog.Write("error", "sync", "Local Sync 初始化失败: " + ex.Message); }
            try
            {
                _library = new DeskBox.Fs.FileLibrary(_store);
                _history = new DeskBox.Fs.FileHistory(_store);
                _ops = new DeskBox.Fs.FileOps(_store);
                _index = DeskBox.Fs.FileIndex.Instance;
                AutoConfigureIndex();
            }
            catch (Exception ex) { AppLog.Write("error", "system", "服务初始化失败: " + ex.Message); }
            try
            {
                _monitor = new DeskBox.Sys.SystemMonitor(2000);
                _monitor.Start();
            }
            catch (Exception ex) { AppLog.Write("error", "system", "监控初始化失败: " + ex.Message); }
            try
            {
                _clipboard = new DeskBox.Sys.ClipboardWatcher(_store, 100);
            }
            catch (Exception ex) { AppLog.Write("error", "system", "剪贴板初始化失败: " + ex.Message); }
            try
            {
                _workflows = new DeskBox.Automation.WorkflowEngine(_store);
                _workflows.Notify = delegate(string title, string message)
                {
                    if (_notifications != null && _store.Settings.NotifyInApp)
                        _notifications.Publish(title, message, "automation", "");
                };
                _workflows.ConfirmDanger = ConfirmDangerousWorkflow;
                _workflows.StartScheduler();
            }
            catch (Exception ex) { AppLog.Write("error", "system", "自动化初始化失败: " + ex.Message); }
            DeskBox.Ai.AiService.Instance.LoadConfig();
            try
            {
                _providers = new DeskBox.Ai.ProviderStore();
                _chat = new DeskBox.Ai.ChatService(_store, _providers);
                _chat.Workflows = _workflows;
                _chat.Projects=_workspaces;_chat.Sync=_localSync;_chat.Cloud=_cloudSync;_chat.Files=_ops;_chat.Navigate=SwitchView;
                _chat.UiInvoke=delegate(Func<DeskBox.Ai.ToolResult> action){if(IsDisposed||_closing)return DeskBox.Ai.ToolResult.Fail("界面已关闭");return InvokeRequired?(DeskBox.Ai.ToolResult)Invoke(action):action();};
                _chat.RefreshApp=delegate{OnGlobalContextChanged();if(_referenceActive)_reference.RefreshData();else{var view=_views.ContainsKey(_currentView)?_views[_currentView] as DeskBox.UI.Views.ViewBase:null;if(view!=null&&_currentView!="ai")view.Activate();}};
                _cloudSync.MobileAi=new DeskBox.Ai.MobileAiRunner(_providers,delegate{var context=new DeskBox.Ai.ToolContext{Store=_store,Projects=_workspaces,Sync=_localSync,Cloud=_cloudSync,Workflows=_workflows,Files=_ops,AllowedRoots=TrustedRoots.Load(),FileAccessEnabled=()=>_store.Settings.AiFileAccess,UiInvoke=_chat.UiInvoke};context.RefreshAuthorization=delegate{context.AllowedRoots=TrustedRoots.Load();};return context;});
            }
            catch (Exception ex) { AppLog.Write("error", "system", "AI 服务初始化失败: " + ex.Message); }
            StartReminderLoop();
        }


        /// <summary>默认索引范围：桌面 + 文档 + 下载 + D:\桌面（存在才加），后台重建。</summary>
        private void AutoConfigureIndex()
        {
            try
            {
                var roots = new List<string>();
                var desktop = DeskBox.DesktopScanner.ResolveDesktopDir(_store.Settings);
                if (!string.IsNullOrEmpty(desktop) && Directory.Exists(desktop)) roots.Add(desktop);
                if (Directory.Exists(@"D:\桌面")) roots.Add(@"D:\桌面");
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrEmpty(docs) && Directory.Exists(docs)) roots.Add(docs);
                var down = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (Directory.Exists(down)) roots.Add(down);

                var excludes = new List<string>();
                excludes.Add(@"C:\Windows");
                excludes.Add(@"C:\Program Files");
                excludes.Add(@"C:\Program Files (x86)");
                excludes.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

                if (_index.Roots.Count == 0) _index.Configure(roots, excludes);
                if (_index.Count == 0 && !_index.IsIndexing) _index.Rebuild();
            }
            catch (Exception ex) { AppLog.Write("warn", "system", "索引配置失败: " + ex.Message); }
        }

        public void SwitchView(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureServices();
            _currentView = id;
            if(_referenceActive){_reference.Navigate(id);return;}

            if (id == "desktop")
            {
                foreach (var v in _views.Values) v.Visible = false;
                _desktopHost.Visible = true;
                _desktopHost.BringToFront();
                _sidebar.Visible = _store.Settings.ShowSidebar;
                ApplyContextPanel(id);
                SetDesktopToolsVisible(true);
                UpdateRailActive();
                _lblConn.Text = "● 桌面模式";
                return;
            }

            _desktopHost.Visible = false;
            SetDesktopToolsVisible(false);
            ApplyContextPanel(id);
            UserControl view;
            if (!_views.TryGetValue(id, out view))
            {
                view = CreateView(id);
                if (view == null) { _desktopHost.Visible = true; return; }
                view.Dock = DockStyle.Fill;
                _contentHost.Controls.Add(view);
                _views[id] = view;
            }
            foreach (var v in _views.Values) v.Visible = (v == view);
            view.Visible = true;
            view.BringToFront();
            var vb = view as DeskBox.UI.Views.ViewBase;
            if (vb != null) vb.Activate();
            UpdateRailActive();
        }

        private void ApplyContextPanel(string viewId)
        {
            if (viewId == "desktop")
            {
                _widgets.Visible = false;
                _preview.Width = _store.Settings.DesktopPreviewEnabled ? 330 : 0;
            }
            else
            {
                _preview.Width = 0;
                bool contextual = viewId == "home" || viewId == "projects" || viewId == "ai" || viewId == "engineering" || viewId == "automation" || viewId == "tasks" || viewId == "mobile";
                bool show = contextual && _store.Settings.ShowWidgets;
                _widgets.Visible = show;
                if (show) UpdateContextPanel(viewId);
            }
        }

        private UserControl _referenceDesktop;
        private Panel _referenceFilePanel, _referenceRight, _referenceContext, _desktopShelf;
        private Label _referencePath, _referenceProject, _referenceProjectDetail;
        private readonly Label[] _referenceTaskNames = new Label[3], _referenceTaskDetails = new Label[3];
        private readonly Panel[] _referenceTaskRows = new Panel[3];
        private DarkList _referenceRecent;
        private DarkButton _referenceDesktopMode, _referenceEngineeringMode, _referenceBack, _referenceUp;
        private bool _referenceEngineering = true, _referenceCategories;
        private string _referencePreviewPath;
        private ContextMenuStrip _desktopMenu;

        private UserControl BuildReferenceDesktop()
        {
            if (_referenceDesktop == null)
            {
                _referenceDesktop = new UserControl { Dock = DockStyle.Fill, BackColor = Palette.Bg };
                var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
                grid.Resize += delegate { grid.RowStyles[1].Height = grid.Width >= 1400 ? 220 : 132; };

                _referenceFilePanel = new ReferenceCard(Palette) { Dock = DockStyle.Fill, BackColor = Palette.CardBg,
                    Padding = new Padding(10), Margin = new Padding(0, 0, 12, 12) };
                var fileTitle = DesktopText("工程桌面", 12, true);
                fileTitle.Height = 27;
                _referencePath = DesktopText("", 9, false);
                _referencePath.Height = 26;
                _referencePath.Cursor = Cursors.Hand;
                _referencePath.Click += delegate { ShowLocationPrompt(); };
                var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, 3) };
                _referenceDesktopMode = DesktopAction("桌面模式", () => SetDesktopWorkspace(false), 78);
                _referenceEngineeringMode = DesktopAction("工程模式", () => SetDesktopWorkspace(true), 78);
                _referenceBack = DesktopAction("返回", () => _folderSession.Back(), 51);
                _referenceUp = DesktopAction("上一级", () => _folderSession.Up(), 59);
                var create = DesktopAction("新建", () => { }, 51);
                create.Click += delegate { ShowNewMenu(create, new Point(0, create.Height)); };
                toolbar.Controls.AddRange(new Control[] {
                    _referenceEngineeringMode, _referenceDesktopMode, _referenceBack, _referenceUp,
                    DesktopAction("桌面", () => _folderSession.Home(), 51),
                    create,
                    DesktopAction("视图", CycleView, 51),
                    DesktopAction("排序", CycleSort, 51),
                    DesktopAction("隐藏项", ToggleShowHidden, 65),
                    DesktopAction("刷新", () => LoadItems(true), 51),
                    DesktopAction("分类", () => { _referenceCategories = !_referenceCategories; _sidebar.Visible = _referenceCategories; }, 52)
                });
                var filter = new DarkInput { Palette = Palette, Placeholder = "筛选当前目录", Width = 142, Height = 30,
                    Margin = new Padding(0, 2, 4, 2) };
                filter.Text = _search.Text;
                filter.Inner.TextChanged += delegate { _search.Text = filter.Text; };
                _search.TextChanged += delegate { if (filter.Text != _search.Text) filter.Text = _search.Text; };
                filter.UseDrawnPlaceholder = true;
                filter.Dock = DockStyle.Right;
                filter.Width = 225;
                _referencePath.Dock = DockStyle.Fill;
                _referencePath.TextAlign = ContentAlignment.MiddleLeft;
                var pathAndFilter = new Panel { Dock = DockStyle.Top, Height = 32, Padding = new Padding(0, 0, 0, 3) };
                pathAndFilter.Controls.Add(_referencePath);
                pathAndFilter.Controls.Add(filter);
                _referenceFilePanel.Controls.Add(toolbar);
                _referenceFilePanel.Controls.Add(pathAndFilter);
                _referenceFilePanel.Controls.Add(fileTitle);

                _referenceRight = new ReferenceCard(Palette) { Dock = DockStyle.Fill, BackColor = Palette.CardBg,
                    Margin = new Padding(0, 0, 0, 12) };
                _referenceContext = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true };
                var contextTitle = DesktopText("当前工作", 12, true);
                var projectRow = DesktopContextRow(out _referenceProject, out _referenceProjectDetail);
                var taskHeading = DesktopText("待办", 10, true);
                taskHeading.Padding = new Padding(0, 8, 0, 0);
                var aiBar = new Panel { Dock = DockStyle.Top, Height = 49, Padding = new Padding(0, 8, 0, 0) };
                var ai = new DesktopActionCard("打开 AI 助手", "\uE99A", "") { Dock = DockStyle.Left, Width = 148, Palette = Palette };
                ai.Click += delegate { SwitchView("ai"); };
                aiBar.Controls.Add(ai);
                _referenceContext.Controls.Add(aiBar);
                for (int i = _referenceTaskRows.Length - 1; i >= 0; i--)
                {
                    _referenceTaskRows[i] = DesktopContextRow(out _referenceTaskNames[i], out _referenceTaskDetails[i]);
                    _referenceContext.Controls.Add(_referenceTaskRows[i]);
                }
                _referenceContext.Controls.Add(taskHeading);
                _referenceContext.Controls.Add(projectRow);
                _referenceContext.Controls.Add(contextTitle);
                _referenceRight.Controls.Add(_referenceContext);

                var lower = new ReferenceCard(Palette) { Dock = DockStyle.Fill, BackColor = Palette.CardBg, Padding = new Padding(12), Margin = Padding.Empty };
                var recentTitle = DesktopText("最近文件与工具", 11, true);
                _referenceRecent = new DarkList { Dock = DockStyle.Fill, Palette = Palette, AutoFitColumns = true, CardSurface = true, InsetRows = true, ShowHeader = false, RowHeight = 42 };
                _referenceRecent.SetColumns(new[] { "最近文件", "打开时间" }, new[] { 340, 116 });
                _referenceRecent.RowActivated += delegate
                {
                    var selected = _referenceRecent.SelectedRow;
                    if (selected != null) OpenPathExternal((string)selected.Tag);
                };
                var shortcuts = new TableLayoutPanel { Dock = DockStyle.Right, Width = 198, ColumnCount = 2, RowCount = 1,
                    Padding = new Padding(8, 2, 0, 10) };
                shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                var calculator = new DesktopActionCard("仪表计算", "\uE9D9", "4–20 mA · 仪表换算") { Palette = Palette };
                calculator.Click += delegate { SwitchView("calculator"); };
                var searchFiles = new DesktopActionCard("文件中心", "\uE8B7", "工程索引 · 文件搜索") { Palette = Palette };
                searchFiles.Click += delegate { SwitchView("search"); };
                calculator.Dock = searchFiles.Dock = DockStyle.Top;
                calculator.Margin = searchFiles.Margin = new Padding(0, 0, 8, 0);
                shortcuts.Controls.Add(calculator, 0, 0);
                shortcuts.Controls.Add(searchFiles, 1, 0);
                lower.Resize += delegate
                {
                    shortcuts.Width = Math.Max(175, lower.ClientSize.Width * 37 / 100);
                    _referenceRecent.RowHeight = lower.Width >= 1400 ? 48 : 35;
                };
                shortcuts.Resize += delegate
                {
                    foreach (Control shortcut in shortcuts.Controls)
                    {
                        shortcut.Height = Math.Min(76, Math.Max(42, shortcuts.ClientSize.Height - 16));
                    }
                };
                lower.Controls.Add(_referenceRecent);
                lower.Controls.Add(shortcuts);
                lower.Controls.Add(recentTitle);
                grid.Controls.Add(_referenceFilePanel, 0, 0);
                grid.Controls.Add(_referenceRight, 1, 0);
                grid.Controls.Add(lower, 0, 1);
                grid.SetColumnSpan(lower, 2);
                _referenceDesktop.Controls.Add(grid);
            }
            if (_desktopHost.Parent != _referenceFilePanel)
            {
                _referenceFilePanel.Controls.Add(_desktopHost);
                _desktopHost.BringToFront();
                _referenceRight.Controls.Add(_preview);
                _preview.Dock = DockStyle.Fill;
                _preview.BringToFront();
            }
            _desktopShelf.Hide();
            _sidebar.Visible = _referenceCategories;
            _desktopHost.Show();
            _board.UseReferenceDesktop(_referenceEngineering);
            RefreshReferenceDesktop();
            return _referenceDesktop;
        }

        private Panel DesktopContextRow(out Label title, out Label detail)
        {
            var row = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 4, 8, 4),
                BackColor = Palette.PanelAlt };
            title = DesktopText("", 10, true);
            title.Height = 23;
            detail = DesktopText("", 8.5f, false);
            detail.Height = 20;
            row.Controls.Add(detail);
            row.Controls.Add(title);
            return row;
        }

        private Label DesktopText(string text, float size, bool heading)
        {
            return new Label { Text = text, Dock = DockStyle.Top, Height = 30, AutoEllipsis = true,
                UseMnemonic = false, ForeColor = heading ? Palette.Text : Palette.TextDim,
                Font = new Font("Microsoft YaHei UI", size, heading ? FontStyle.Bold : FontStyle.Regular) };
        }

        private DarkButton DesktopAction(string label, Action action, int width)
        {
            var button = new DarkButton(label) { Width = width, Height = 30, Palette = Palette,
                Margin = new Padding(0, 2, 4, 2) };
            button.Click += delegate { action(); };
            return button;
        }

        private void RefreshReferenceDesktop()
        {
            if (_referenceDesktop == null) return;
            var folder = new DirectoryInfo(_desktopPath);
            _referencePath.Text = folder.Parent == null ? folder.FullName :
                folder.Root.Name + " … / " + (folder.Parent.Parent == null ? "" : folder.Parent.Name + " / ") + folder.Name;
            if (_toolTip != null) _toolTip.SetToolTip(_referencePath, _desktopPath);
            _referenceBack.Enabled = _btnBack.Enabled;
            _referenceUp.Enabled = _btnUp.Enabled;
            _referenceEngineeringMode.Primary = _referenceEngineering;
            _referenceDesktopMode.Primary = !_referenceEngineering;
            _referenceEngineeringMode.Invalidate();
            _referenceDesktopMode.Invalidate();
            var project = _workspaces == null ? null : _workspaces.Current;
            _referenceProject.Text = project == null ? "未选择工程项目" : project.Name;
            _referenceProjectDetail.Text = project == null ? "可从项目管理选择项目" :
                (string.IsNullOrWhiteSpace(project.Number) ? "" : project.Number + "  ·  ") + "验证进度 " + ValidationProgress.Display(project);
            var tasks = _store.Tasks.Where(task => !task.Done && (project == null || task.Project == project.Id)).Take(3).ToArray();
            for (int i = 0; i < _referenceTaskRows.Length; i++)
            {
                _referenceTaskRows[i].Visible = i < tasks.Length || i == 0;
                _referenceTaskNames[i].Text = i < tasks.Length ? tasks[i].Title : "暂无待办";
                _referenceTaskDetails[i].Text = i >= tasks.Length ? "" :
                    (string.IsNullOrWhiteSpace(tasks[i].Due) ? "未设置截止日期" : "截止 " + tasks[i].Due) +
                    (tasks[i].Priority == "high" ? "  ·  高优先级" : "");
            }
            if (_library != null)
                _referenceRecent.SetRows(_library.Recent.Take(4).Select(file => new ListRow {
                    Cells = new[] { Path.GetFileName(file.Path), DateTimeOffset.FromUnixTimeMilliseconds(file.LastOpenAt).ToLocalTime().ToString("MM-dd HH:mm") }, Tag = file.Path
                }).ToList());
            ShowReferenceDesktopSelection(_board.SelectedItems.FirstOrDefault());
        }

        private void ShowReferenceDesktopSelection(DesktopItem item, bool forcePreview = false)
        {
            var path = item == null || (!forcePreview && !_store.Settings.DesktopPreviewEnabled) ? null : item.Path;
            if (path == null)
            {
                _referencePreviewPath = null;
                if (_preview.IsOpen) _preview.ClosePreview();
                _preview.Hide();
                _referenceContext.Show();
                return;
            }
            if (_referencePreviewPath != path || !_preview.IsOpen)
            {
                _preview.ShowPreview(path);
                _referencePreviewPath = path;
            }
            _referenceContext.Hide();
            _preview.Show();
        }

        public void SetReferenceWorkspace(bool enabled)
        {
            if(enabled&&_reference==null)
            {
                _reference=new ReferenceWorkspace(_store,_workspaces,_localSync,_cloudSync,_chat,_providers,_workflows,_library,CreateView);
                _reference.FileAiRequested += path => AskAiForPaths(new[] { path });
                _reference.PageChanged += id => { if (id == "desktop") RefreshReferenceDesktop(); };
                _reference.ClassicRequested+=delegate{SetReferenceWorkspace(false);};_reference.PageChanged+=id=>{_currentView=id;};Controls.Add(_reference);
            }
            _store.Settings.ReferenceUi=enabled;
            _referenceActive=enabled;if(enabled)_currentView=_reference.CurrentPage;
            foreach(Control control in new Control[]{_topbar,_statusbar,_rail,_contentHost,_widgets,_preview})control.Visible=!enabled;
            if(_reference!=null){_reference.Visible=enabled;if(enabled)_reference.BringToFront();}
            if(!enabled){_board.UseReferenceDesktop(null);_desktopShelf.Show();_contentHost.Controls.Add(_desktopHost);_preview.Dock=DockStyle.Right;Controls.Add(_preview);SwitchView("home");}else if(_referenceDesktop!=null)BuildReferenceDesktop();_store.SaveSettingsOnly();
        }

        private void UpdateContextPanel(string viewId)
        {
            if (_widgets == null) return;
            var lines = new List<string>(); var project = _workspaces == null ? null : _workspaces.Current;
            string title, subtitle; bool tasks = false, logs = false;
            if (viewId == "home" || viewId == "projects")
            {
                title = viewId == "home" ? "MISSION CONTEXT" : "PROJECT CONTEXT"; subtitle = project == null ? "尚未选择工程项目" : project.Name + (string.IsNullOrEmpty(project.Number) ? "" : " · " + project.Number);
                lines.Add("PROGRESS  " + ValidationProgress.Display(project));
                lines.Add("RISK      " + (project == null ? "—" : (project.Risk ?? "medium").ToUpperInvariant()));
                lines.Add("OPEN      " + ProjectTaskCount(project, false));
                lines.Add("OVERDUE   " + ProjectTaskCount(project, true));
                tasks = true; logs = viewId == "home";
            }
            else if (viewId == "ai")
            {
                var provider = _providers == null ? null : _providers.DefaultProvider;
                title = "AI EXECUTION CONTEXT"; subtitle = provider == null ? "尚未配置 AI 服务商" : provider.Name;
                lines.Add("MODEL     " + (provider == null ? "—" : provider.Model));
                lines.Add("STATE     " + (_chat != null && _chat.Busy ? "EXECUTING" : "IDLE"));
                lines.Add("MESSAGES  " + (_chat == null || _chat.Current == null ? 0 : _chat.Current.Messages.Count)); logs = true;
            }
            else if (viewId == "automation")
            {
                int active = _workflows != null && _workflows.Runner != null ? _workflows.Runner.ActiveCount : 0;
                title = "WORKFLOW CONTEXT"; subtitle = "真实运行状态与最近执行";
                lines.Add("ENABLED   " + (_workflows == null ? 0 : _workflows.Workflows.Count(x => x.Enabled)));
                lines.Add("ACTIVE    " + active);
                lines.Add("RUNS      " + (_workflows == null ? 0 : _workflows.Runs.Count)); logs = true;
            }
            else if (viewId == "mobile")
            {
                title = "MOBILE SYNC CONTEXT"; subtitle = "本地优先 · TLS 设备信任";
                lines.Add("SERVICE   " + (_localSync != null && _localSync.Running ? "ONLINE" : "OFF"));
                lines.Add("DEVICES   " + (_localSync == null ? 0 : _localSync.Devices.Devices.Count));
                lines.Add("QUEUE     " + (_localSync == null ? 0 : _localSync.Sync.Queue.Count));
                lines.Add("CONFLICTS " + (_localSync == null ? 0 : _localSync.Sync.Conflicts.Count)); logs = true;
            }
            else if (viewId == "tasks")
            {
                title = "TASK CONTEXT"; subtitle = project == null ? "全部任务" : project.Name;
                lines.Add("OPEN      " + _store.Tasks.Count(x => !x.Done));
                lines.Add("OVERDUE   " + _store.Tasks.Count(x => !x.Done && TaskDuePast(x.Due)));
                lines.Add("HIGH      " + _store.Tasks.Count(x => !x.Done && x.Priority == "high")); tasks = true;
            }
            else
            {
                title = "ENGINEERING CONTEXT"; subtitle = project == null ? "PLC / DCS / Instrumentation / Validation" : project.Name;
                lines.Add("PROJECT   " + (project == null ? "—" : project.Kind));
                lines.Add("PROGRESS  " + ValidationProgress.Display(project));
                lines.Add("LOGS      " + _store.ReadLogs(100).Count); logs = true;
            }
            _widgets.SetContext(title, subtitle, lines.ToArray(), tasks, logs);
        }

        private int ProjectTaskCount(EngineerWorkspace project, bool overdue)
        {
            return _store.Tasks.Count(x => !x.Done && (project == null || string.IsNullOrEmpty(x.Project) || x.Project == project.Id || string.Equals(x.Project, project.Name, StringComparison.OrdinalIgnoreCase)) && (!overdue || TaskDuePast(x.Due)));
        }

        private static bool TaskDuePast(string due) { DateTime value; return !string.IsNullOrEmpty(due) && DateTime.TryParse(due, out value) && value.Date < DateTime.Today; }

        private void SetDesktopToolsVisible(bool visible)
        {
            if (_desktopSearchHost != null) _desktopSearchHost.Visible = visible;
            if (_folderNav != null) _folderNav.Visible = visible;
            if (_btnView != null) _btnView.Visible = visible;
            if (_btnSort != null) _btnSort.Visible = visible;
            if (_btnHiddenToggle != null) _btnHiddenToggle.Visible = visible;
            if (_btnRefresh != null) _btnRefresh.Visible = visible;
            if (_topContext != null) _topContext.Visible = !visible;
            if (!visible)
            {
                _lblPath.Text = "DeskBox 工程工作台 · Ctrl+K 全局命令";
                _lblCount.Text = "当前模块：" + _railItems.Where(x => x.Id == _currentView).Select(x => x.Title).FirstOrDefault();
            }
            else
            {
                _lblPath.Text = "桌面：" + _desktopPath;
                _lblCount.Text = _items.Count + " 个条目 · 已隐藏 " + _items.Count(i => i.Hidden);
            }
            UpdateGlobalControls();
        }

        private UserControl CreateView(string id)
        {
            try
            {
                switch (id)
                {
                    case "desktop": return BuildReferenceDesktop();
                    case "home": return BuildEngineerHomeView();
                    case "projects":
                        var workspaceView = new DeskBox.UI.Views.WorkspaceView(_store, _workspaces);
                        workspaceView.SearchRequested += SearchProject;
                        return workspaceView;
                    case "engineering": return new DeskBox.UI.Views.EngineeringToolkitView(_store);
                    case "search": return BuildSearchView();
                    case "library": return BuildLibraryView();
                    case "disk": return BuildDiskView();
                    case "history": return BuildHistoryView();
                    case "filehistory": return BuildFileHistoryView();
                    case "notifications": return new DeskBox.UI.Views.NotificationsView(_store, _notifications);
                    case "ai": return BuildAiView();
                    case "automation": return BuildAutomationView();
                    case "monitor": return BuildMonitorView();
                    case "mobile": return new DeskBox.UI.Views.MobileSyncView(_store, _localSync, _cloudSync);
                    case "clipboard": return BuildClipboardView();
                    case "tasks": return new DeskBox.UI.Views.TasksView(_store, _notifications);
                    case "incidents": return new DeskBox.UI.Views.IncidentsView(_store, _localSync, _workspaces);
                    case "weather": return BuildWeatherView();
                case "settings": return BuildSettingsCenter();
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "创建视图失败 " + id + ": " + ex.ToString());
            }
            return null;
        }

        private DeskBox.UI.Views.SearchView _searchView;
        private void SearchProject(EngineerWorkspace project)
        {
            if (project == null || !Directory.Exists(project.Root)) return;
            var roots = _index.Roots.ToList();
            if (!roots.Any(x => string.Equals(x, project.Root, StringComparison.OrdinalIgnoreCase))) { roots.Add(project.Root); _index.Configure(roots, _index.Excludes.ToList()); _index.Rebuild(); }
            SwitchView("search"); _searchView.SetProjectScope(project.Root, project.Name); _searchView.FocusQuery();
        }
        private UserControl BuildSearchView()
        {
            _searchView = new DeskBox.UI.Views.SearchView(_store, _index, _library, _history);
            _searchView.EntrySelected += delegate(DeskBox.Core.FileEntry e)
            {
                if(_referenceActive)return;
                _preview.Width = 360;
                _preview.ShowPreview(e.Path);
                var ai = _views.ContainsKey("ai") ? _views["ai"] as DeskBox.UI.Views.AiView : null;
                if (ai != null) ai.SetPaths(new List<string> { e.Path });
            };
            _searchView.EntryActivated += delegate(DeskBox.Core.FileEntry e) { OpenPathExternal(e.Path); };
            _searchView.Searched += delegate(DeskBox.Core.SearchOutcome o) { };
            return _searchView;
        }

        private UserControl BuildLibraryView()
        {
            var v = new DeskBox.UI.Views.FavoritesView(_store, _library);
            v.OpenRequested += delegate(string p) { OpenPathExternal(p); };
            v.RevealRequested += delegate(string p) { try { Win32.RevealPath(p); } catch { } };
            return v;
        }

        private UserControl BuildDiskView()
        {
            var v = new DeskBox.UI.Views.DiskView(_store);
            v.OpenRequested += delegate(string p) { OpenPathExternal(p); };
            v.RevealRequested += delegate(string p) { try { Win32.RevealPath(p); } catch { } };
            return v;
        }

        private UserControl BuildHistoryView()
        {
            return new DeskBox.UI.Views.RecordsView(_store);
        }

        private UserControl BuildFileHistoryView()
        {
            var v = new DeskBox.UI.Views.HistoryView(_store, _history, _ops);
            v.OpenRequested += delegate(string p) { OpenPathExternal(p); };
            return v;
        }

        private DeskBox.UI.Views.ChatView _chatView;
        private UserControl BuildAiView()
        {
            // AI&SI 对话界面；若 ChatView 尚未就绪则退回本地 AI 视图
            try
            {
                _chatView = new DeskBox.UI.Views.ChatView(_store);
                _chatView.SetServices(_chat, _providers);
                _chatView.SettingsRequested += delegate { OpenSettings(); };
                return _chatView;
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "ChatView 创建失败，回退本地 AI: " + ex.Message);
                return new DeskBox.UI.Views.AiView(_store);
            }
        }

        private DeskBox.UI.Views.SettingsCenterView _settingsCenter;

        private UserControl BuildSettingsCenter()
        {
            try
            {
                _settingsCenter = new DeskBox.UI.Views.SettingsCenterView(_store, _providers);
                _settingsCenter.FilePermissionsChanged += delegate { if (_chat != null) _chat.RefreshPermissions(); };
                _settingsCenter.TemporaryAccessRevoked += delegate { if (_chat != null) { if (_chat.Busy) _chat.Cancel(); _chat.AllowedFiles.Clear(); _chat.AllowedRoots.Clear(); } };
                _settingsCenter.ProvidersChanged += delegate
                {
                    try { if (_chat != null && _providers != null) _chat.SetProviders(_providers); } catch { }
                    UpdateGlobalControls();
                };
                _settingsCenter.AppearanceChanged += delegate
                {
                    try
                    {
                        var p2 = Palette.For(_store.Settings.Theme, _store.Settings.Accent);
                        ApplyPaletteEverywhere(p2);
                        if(_reference!=null)_reference.ApplyWorkspacePalette(p2);
                        if(_store.Settings.ReferenceUi!=(_referenceActive))SetReferenceWorkspace(_store.Settings.ReferenceUi);
                    }
                    catch (Exception ex) { AppLog.Write("warn", "system", "应用外观失败: " + ex.Message); }
                };
                return _settingsCenter;
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "设置中心创建失败，回退旧设置: " + ex.Message);
                return new DeskBox.UI.SettingsFormHost(_store);
            }
        }

        private void ApplyPaletteEverywhere(Palette p2)
        {
            try
            {
                this.Palette = p2;
                BackColor = p2.Bg;
                foreach (Control c in Controls) ApplyPaletteRecursive(c, p2);
                Invalidate(true);
            }
            catch { }
        }

        private void ApplyPaletteRecursive(Control c, Palette p2)
        {
            try
            {
                var v = c as DeskBox.UI.Views.ViewBase;
                if (v != null) { v.ApplyPalette(p2); return; }
                var m = c.GetType().GetMethod("ApplyPalette", new Type[] { typeof(Palette) });
                if (m != null) { m.Invoke(c, new object[] { p2 }); return; }
                if (c.BackColor != p2.Bg && c.BackColor != p2.Panel && c.BackColor != p2.PanelAlt) c.BackColor = p2.Bg;
                foreach (Control cc in c.Controls) ApplyPaletteRecursive(cc, p2);
            }
            catch { }
        }

        private UserControl BuildWeatherView()
        {
            return new DeskBox.UI.Views.WeatherView(_store);
        }

        private UserControl BuildAutomationView() { return new DeskBox.UI.Views.AutomationView(_store, _workflows); }
        private UserControl BuildMonitorView() { return new DeskBox.UI.Views.MonitorView(_store, _monitor); }
        private UserControl BuildClipboardView() { return new DeskBox.UI.Views.ClipboardView(_store, _clipboard); }

        private UserControl BuildEngineerHomeView()
        {
            var v = new DeskBox.UI.Views.EngineerHomeView(_store, _workspaces, _library, _monitor, _notifications);
            v.NavigateRequested += delegate(string id) { SwitchView(id); };
            v.ProjectSearchRequested += SearchProject;
            v.ContextToggleRequested += delegate { _store.Settings.ShowWidgets = !_store.Settings.ShowWidgets; _store.SaveSettingsOnly(); ApplyContextPanel("home"); };
            return v;
        }

        private void ShowCommandPalette()
        {
            var commands = new List<CommandItem>
            {
                new CommandItem { Icon="◫", Name="工程驾驶舱", Hint="系统、任务、通知与项目总览", Execute=delegate { SwitchView("home"); } },
                new CommandItem { Icon="▦", Name="智能桌面", Hint="桌面文件、分类与快速启动", Execute=delegate { SwitchView("desktop"); } },
                new CommandItem { Icon="◆", Name="工程项目舱", Hint="PLC / DCS / CSV 项目根目录", Execute=delegate { SwitchView("projects"); } },
                new CommandItem { Icon="⌁", Name="工程工具矩阵", Hint="4–20mA、Pt100、S7、Modbus、网络与验证", Execute=delegate { SwitchView("engineering"); } },
                new CommandItem { Icon="◈", Name="AI&SI 工程助手", Hint="PLC、DCS、GAMP 5、故障分析与文档审查", Execute=delegate { SwitchView("ai"); if (_chatView != null) BeginInvoke(new Action(_chatView.FocusInput)); } },
                new CommandItem { Icon="⚡", Name="自动化工作流", Hint="预演、执行、取消与运行记录", Execute=delegate { SwitchView("automation"); } },
                new CommandItem { Icon="⌕", Name="全局文件搜索", Hint="索引搜索、预览和筛选", Execute=delegate { SwitchView("search"); } },
                new CommandItem { Icon="☑", Name="任务与验证", Hint="截止、提醒、项目与优先级", Execute=delegate { SwitchView("tasks"); } },
                new CommandItem { Icon="📈", Name="系统监控", Hint="CPU、内存、磁盘与网络", Execute=delegate { SwitchView("monitor"); } },
                new CommandItem { Icon="▣", Name="手机联动", Hint="Local Sync、配对码与信任设备", Execute=delegate { SwitchView("mobile"); } },
                new CommandItem { Icon="🔔", Name="通知中心", Hint="任务与自动化提醒", Execute=delegate { SwitchView("notifications"); } },
                new CommandItem { Icon="⚙", Name="设置中心", Hint="AI、天气、隐私、通知与系统", Execute=delegate { SwitchView("settings"); } }
            };
            using (var f = new CommandPaletteForm(commands, Palette, BuildDynamicCommands)) f.ShowDialog(this);
        }

        private List<CommandItem> BuildDynamicCommands(string query)
        {
            var result = new List<CommandItem>();
            var q = (query ?? "").Trim();
            if (q.Length == 0) return result;

            // Deterministic engineering calculations are evaluated locally, never by model guesswork.
            var addressMatch = System.Text.RegularExpressions.Regex.Match(q.ToUpperInvariant(), @"(DB\d+\.DB[XBWD]\d+(?:\.\d+)?|[IQM](?:[BWD]?\d+)(?:\.\d+)?)");
            if (addressMatch.Success)
            {
                var parsed = EngineeringTools.ParseS7Address(addressMatch.Value);
                var hint = parsed.Ok ? parsed.Description : parsed.Error;
                result.Add(new CommandItem { Icon = "⌁", Name = "S7 地址 · " + addressMatch.Value, Hint = hint, Execute = delegate { SwitchView("engineering"); } });
            }

            var scaleMatch = System.Text.RegularExpressions.Regex.Match(q, @"(?<ma>-?\d+(?:\.\d+)?)\s*mA.*?(?<low>-?\d+(?:\.\d+)?)\s*(?:-|~|到)\s*(?<high>-?\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (scaleMatch.Success)
            {
                double ma, low, high;
                if (double.TryParse(scaleMatch.Groups["ma"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ma) &&
                    double.TryParse(scaleMatch.Groups["low"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out low) &&
                    double.TryParse(scaleMatch.Groups["high"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out high))
                {
                    var scaled = EngineeringTools.FromCurrent(ma, low, high);
                    if (scaled.Ok)
                    {
                        var text = scaled.Value.ToString("0.######") + " · " + scaled.Percent.ToString("0.###") + "%";
                        result.Add(new CommandItem { Icon = "⌁", Name = "4–20mA 计算结果 · " + text, Hint = ma + "mA / " + low + "–" + high, Execute = delegate { SwitchView("engineering"); } });
                    }
                }
            }

            if (_workspaces != null)
            {
                foreach (var workspace in _workspaces.Items.Where(x => (x.Name + " " + x.Customer + " " + x.System + " " + x.Root).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(6))
                {
                    var selectedWorkspace = workspace;
                    result.Add(new CommandItem { Icon = "◆", Name = "打开项目 · " + selectedWorkspace.Name, Hint = selectedWorkspace.Kind + " · " + selectedWorkspace.Root, Execute = delegate { if (Directory.Exists(selectedWorkspace.Root)) { _workspaces.Touch(selectedWorkspace.Id); _folderSession.Navigate(selectedWorkspace.Root); SwitchView("desktop"); } } });
                }
            }

            foreach (var task in _store.Tasks.Where(x => (x.Title + " " + x.Project + " " + x.Notes).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).Take(6))
            {
                var selectedTask = task;
                result.Add(new CommandItem { Icon = "☑", Name = (selectedTask.Done ? "已完成 · " : "任务 · ") + selectedTask.Title, Hint = (selectedTask.Priority ?? "normal") + (string.IsNullOrEmpty(selectedTask.Due) ? "" : " · 截止 " + selectedTask.Due), Execute = delegate { SwitchView("tasks"); } });
            }

            if (q.IndexOf("创建", StringComparison.OrdinalIgnoreCase) >= 0 && (q.IndexOf("FAT", StringComparison.OrdinalIgnoreCase) >= 0 || q.IndexOf("问题", StringComparison.OrdinalIgnoreCase) >= 0 || q.IndexOf("任务", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                result.Add(new CommandItem { Icon = "＋", Name = "创建工程任务", Hint = q, Execute = delegate { CreateTaskFromCommand(q); } });
            }

            if (_index != null && _index.Count > 0 && q.Length >= 2)
            {
                var searchText = q.Replace("打开", " ").Replace("查找", " ").Replace("搜索", " ").Replace("最新", " ").Trim();
                var search = NlQuery.Parse(searchText);
                search.Limit = 12;
                if (q.Contains("最新")) { search.SortBy = "mtime"; search.SortDesc = true; }
                var outcome = _index.Search(search);
                foreach (var hit in outcome.Hits.Take(12))
                {
                    var entry = hit.Entry;
                    result.Add(new CommandItem { Icon = entry.IsDir ? "📁" : "⌕", Name = "文件 · " + entry.Name, Hint = entry.Path + " · " + entry.SizeText, Execute = delegate { if (entry.IsDir) { _folderSession.Navigate(entry.Path); SwitchView("desktop"); } else OpenPathExternal(entry.Path); } });
                }
            }

            if (q == "开始今天工作" || q == "继续昨天")
                result.Insert(0, new CommandItem { Icon = "◫", Name = q + " · Today's Mission", Hint = "打开驾驶舱、最近项目和未完成任务", Execute = delegate { SwitchView("home"); } });

            result.Add(new CommandItem { Icon = "◈", Name = "询问 AI&SI", Hint = q, Execute = delegate { SwitchView("ai"); if (_chatView != null) _chatView.SetPrompt(q); } });
            return result;
        }

        private void CreateTaskFromCommand(string command)
        {
            var title = Prompt.Input(this, "创建工程任务", "任务标题", command.Replace("创建", "").Trim(), Palette);
            if (string.IsNullOrWhiteSpace(title)) return;
            var task = new TaskItem { Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10), Title = title.Trim(), Priority = "high", CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            _store.Tasks.Add(task); _store.SaveTasksOnly(); _store.Log("info", "task", "Command Center 创建任务：" + task.Title);
            SwitchView("tasks");
        }

        public void ShowAndOpenCommandPalette()
        {
            if (_closing || IsDisposed) return;
            ShowWorkspace();
            ShowCommandPalette();
        }
        public void ShowWorkspace()
        {
            if(_closing||IsDisposed)return;
            if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
            Show();BringToFront();Activate();Win32.SetForegroundWindow(Handle);
        }

        private bool ConfirmDangerousWorkflow(string detail)
        {
            if (_closing || IsDisposed || !IsHandleCreated) return false;
            try
            {
                if (InvokeRequired)
                    return (bool)Invoke(new Func<string, bool>(ConfirmDangerousWorkflow), detail);
                return Prompt.Confirm(this, "自动化操作确认", detail, Palette);
            }
            catch { return false; }
        }

        private void StartReminderLoop()
        {
            if (_reminderTimer != null) return;
            _reminderTimer = new Timer { Interval = 20000 };
            _reminderTimer.Tick += delegate { CheckTaskReminders(); };
            _reminderTimer.Start();
            CheckTaskReminders();
        }

        private void CheckTaskReminders()
        {
            if (!_store.Settings.NotifyTask) return;
            try
            {
                bool changed = false;
                var now = DateTime.Now;
                foreach (var task in _store.Tasks)
                {
                    if (task.Done || task.Reminded || string.IsNullOrEmpty(task.RemindAt)) continue;
                    DateTime when;
                    if (!DateTime.TryParse(task.RemindAt, out when) || when > now) continue;
                    task.Reminded = true;
                    changed = true;
                    var text = task.Title + (string.IsNullOrEmpty(task.Due) ? "" : "（截止 " + task.Due + "）");
                    if (_notifications != null && _store.Settings.NotifyInApp)
                        _notifications.Publish("任务提醒", text, "task", task.Id);
                    else
                        _store.Log("info", "task", "触发提醒：" + text);
                }
                if (changed) _store.SaveTasksOnly();
            }
            catch (Exception ex) { AppLog.Write("warn", "task", "提醒检查失败: " + ex.Message); }
        }

        private void OnNotificationPublished(AppNotification item)
        {
            try
            {
                if (_closing || IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action<AppNotification>(OnNotificationPublished), item); return; }
                _lblConn.Text = "🔔 " + item.Title + "：" + item.Message;
                _lblConn.ForeColor = item.Kind == "error" ? Color.FromArgb(248, 113, 113) : Palette.Accent;
                if (_noticeStatusTimer != null) { _noticeStatusTimer.Stop(); _noticeStatusTimer.Dispose(); }
                _noticeStatusTimer = new Timer { Interval = 6000 };
                _noticeStatusTimer.Tick += delegate
                {
                    _noticeStatusTimer.Stop();
                    _noticeStatusTimer.Dispose();
                    _noticeStatusTimer = null;
                    UpdateConnLabel();
                };
                _noticeStatusTimer.Start();
                if (_store.Settings.NotifyTray) Program.ShowTrayNotification(item.Title, item.Message);
            }
            catch { }
        }

        private void UpdateRailActive()
        {
            foreach (var it in _railItems)
            {
                var b = it.Button as RailButton;
                if (b != null) { b.Active = it.Id == _currentView; b.Invalidate(); }
            }
        }

        private void OpenPathExternal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
                {
                    Win32.OpenPath(path);
                    if (_library != null) _library.Touch(path);
                    if (_history != null)
                        _history.Record(new DeskBox.Core.FileOpRecord
                        {
                            Id = "o" + DateTime.Now.Ticks, Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                            Op = "open", Source = path, Ok = true, Detail = "由 DeskBox 打开"
                        });
                }
            }
            catch (Exception ex) { AppLog.Write("error", "open", "打开失败 " + path + ": " + ex.Message); }
        }

        private void UpdateConnLabel()
        {
            var project = _workspaces == null ? null : _workspaces.Current;
            var ai = _providers != null && _providers.DefaultProvider != null ? "READY" : "NOT CONFIGURED";
            int jobs = _workflows != null && _workflows.Runner != null ? _workflows.Runner.ActiveCount : 0;
            var index = _index == null ? "—" : _index.State.ToUpperInvariant();
            _lblConn.Text = "PROJECT " + (project == null ? "—" : project.Name) + "  |  LAN " + (_localSync != null && _localSync.Running ? "ONLINE" : "OFF") + "  |  CLOUD " + (_cloudSync==null||!_cloudSync.LoggedIn?"未登录电脑":_cloudSync.Connected?"ONLINE":"连接中 / 重试") + "  |  AI " + ai + "  |  JOBS " + jobs + "  |  INDEX " + index;
            _lblConn.ForeColor = Color.FromArgb(52, 211, 153);
        }

        private void OnGlobalContextChanged()
        {
            if (_closing || IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(OnGlobalContextChanged)); } catch { } return; }
            UpdateGlobalControls();
            if (_widgets != null && _widgets.Visible) UpdateContextPanel(_currentView);
        }

        private void UpdateGlobalControls()
        {
            if(_referenceActive){_reference.RefreshStatus();return;}
            if (_btnAi == null) return;
            var project = _workspaces == null ? null : _workspaces.Current;
            var provider = _providers == null ? null : _providers.DefaultProvider;
            int unread = _notifications == null ? 0 : _notifications.UnreadCount;
            bool syncOnline = _localSync != null && _localSync.Running;
            var aiColor = provider == null ? Palette.TextDim : Palette.Accent;
            var syncColor = syncOnline ? Color.FromArgb(52, 211, 153) : Palette.TextDim;
            var notificationColor = unread > 0 ? Palette.Accent : Palette.TextDim;
            var notificationText = unread > 0 ? "N" + Math.Min(99, unread) : "N";
            if (_btnAi.ForeColor != aiColor) _btnAi.ForeColor = aiColor;
            if (_btnMobile.ForeColor != syncColor) _btnMobile.ForeColor = syncColor;
            if (_btnNotifications.Text != notificationText) _btnNotifications.Text = notificationText;
            if (_btnNotifications.ForeColor != notificationColor) _btnNotifications.ForeColor = notificationColor;
            if (_topContext != null)
            {
                var module = _railItems.Where(x => x.Id == _currentView).Select(x => x.Title).FirstOrDefault() ?? "工程工作台";
                var text = module + "  /  " + (project == null ? "未选择工程项目" : project.Name + (string.IsNullOrEmpty(project.Number) ? "" : " · " + project.Number));
                if (_topContext.Text != text)
                {
                    _topContext.Text = text;
                    _toolTip.SetToolTip(_topContext, project == null ? "在工程项目舱中添加或选择项目" : project.Root);
                }
            }
        }

        private void OpenItem(DesktopItem it)
        {
            if (it == null) return;
            if (it.Kind == "dir" && Directory.Exists(it.Path) && _folderSession != null)
            {
                _folderSession.Navigate(it.Path);
                _store.Log("info", "open", "进入文件夹 " + it.Path);
                return;
            }
            _store.Log("info", "open", "打开 " + it.Name);
            OpenPathExternal(it.Path);
            _widgets.RefreshLogs();
        }

        private string CurrentFolder()
        {
            if (_folderSession == null || _folderSession.Current == FolderSession.DesktopHome)
                return DesktopScanner.ResolveDesktopDir(_store.Settings);
            return _folderSession.Current;
        }

        private List<string> SelectedPaths() { return _board.SelectedItems.Select(x => x.Path).Where(x => File.Exists(x) || Directory.Exists(x)).ToList(); }

        private void ShowLocationPrompt()
        {
            var current = CurrentFolder();
            var path = Prompt.Input(this, "转到位置", "输入本地或 UNC 文件夹路径", current, Palette);
            if (path == null) return;
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Directory.Exists(path)) { _lblConn.Text = "目录不存在：" + path; return; }
            _folderSession.Navigate(path);
        }

        private void ShowNewMenu(Control owner, Point location)
        {
            var menu = MenuBuilder.Create(Palette);
            menu.Items.Add(MenuBuilder.Item("新建文件夹", delegate
            {
                var name = Prompt.Input(this, "新建文件夹", "文件夹名称", "新建文件夹", Palette);
                if (name != null) RunShellOperation(delegate { return _shellFiles.CreateFolder(CurrentFolder(), name); }, "create-folder");
            }));
            menu.Items.Add(MenuBuilder.Item("新建文本文档", delegate
            {
                var name = Prompt.Input(this, "新建文本文档", "文件名称", "新建文本文档.txt", Palette);
                if (name != null) RunShellOperation(delegate { return _shellFiles.CreateTextFile(CurrentFolder(), name); }, "create-file");
            }));
            menu.Show(owner, location);
        }

        private void ShowDesktopContextMenu(DesktopItem item, Point location)
        {
            if (_desktopMenu != null) _desktopMenu.Dispose();
            var menu = _desktopMenu = MenuBuilder.Create(Palette);
            if (item == null)
            {
                menu.Items.Add(MenuBuilder.Item("新建文件夹", delegate { var n = Prompt.Input(this, "新建文件夹", "文件夹名称", "新建文件夹", Palette); if (n != null) RunShellOperation(delegate { return _shellFiles.CreateFolder(CurrentFolder(), n); }, "create-folder"); }));
                menu.Items.Add(MenuBuilder.Item("新建文本文档", delegate { var n = Prompt.Input(this, "新建文本文档", "文件名称", "新建文本文档.txt", Palette); if (n != null) RunShellOperation(delegate { return _shellFiles.CreateTextFile(CurrentFolder(), n); }, "create-file"); }));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuBuilder.Item("粘贴", PasteFiles));
                menu.Items.Add(MenuBuilder.Item("刷新", delegate { LoadItems(true); }));
            }
            else
            {
                menu.Items.Add(MenuBuilder.Item(item.Kind == "dir" ? "进入文件夹" : "打开", delegate { OpenItem(item); }));
                menu.Items.Add(MenuBuilder.Item("在 Explorer 中定位", delegate { Win32.RevealPath(item.Path); }));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuBuilder.Item("复制    Ctrl+C", delegate { CopySelection(false); }));
                menu.Items.Add(MenuBuilder.Item("剪切    Ctrl+X", delegate { CopySelection(true); }));
                menu.Items.Add(MenuBuilder.Item("重命名  F2", RenameSelection));
                menu.Items.Add(MenuBuilder.Item("移入回收站  Delete", delegate { DeleteSelection(false); }));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuBuilder.Item("Ask AI&SI", AskAiForSelection));
                menu.Items.Add(MenuBuilder.Item("预览", delegate { if (_referenceActive && _referenceDesktop != null) ShowReferenceDesktopSelection(item, true); else { _preview.Width = 360; _preview.ShowPreview(item.Path); } }));
                menu.Items.Add(MenuBuilder.Item("属性    Alt+Enter", delegate { Win32.ShowProperties(item.Path); }));
                menu.Items.Add(MenuBuilder.Item("仅从 DeskBox 隐藏", delegate { ToggleHidden(item, true); }));
            }
            menu.Show(_board, location);
        }

        private void ExecuteDesktopCommand(string command)
        {
            switch (command)
            {
                case "delete": DeleteSelection(false); break;
                case "permanent-delete": DeleteSelection(true); break;
            }
        }

        private void CopySelection(bool cut)
        {
            var paths = SelectedPaths();
            if (paths.Count == 0) return;
            try
            {
                ShellFileOperationService.SetClipboardFiles(paths, cut);
                _lblConn.Text = (cut ? "已剪切 " : "已复制 ") + paths.Count + " 项";
            }
            catch (Exception ex) { _lblConn.Text = "剪贴板操作失败：" + ex.Message; }
        }

        private void PasteFiles()
        {
            var paths = ShellFileOperationService.GetClipboardFiles();
            if (paths.Count == 0) { _lblConn.Text = "剪贴板中没有文件"; return; }
            bool move = ShellFileOperationService.ClipboardIsCut();
            RunShellOperation(delegate { return move ? _shellFiles.Move(paths, CurrentFolder()) : _shellFiles.Copy(paths, CurrentFolder()); }, move ? "move" : "copy");
        }

        private void RenameSelection()
        {
            var selected = _board.SelectedItems;
            if (selected.Count != 1) { _lblConn.Text = "重命名需要选中一个对象"; return; }
            var item = selected[0];
            var name = Prompt.Input(this, "重命名", "新名称", item.Name, Palette);
            if (name == null || name == item.Name) return;
            RunShellOperation(delegate { return _shellFiles.Rename(item.Path, name); }, "rename");
        }

        private void DeleteSelection(bool permanent)
        {
            var paths = SelectedPaths();
            if (paths.Count == 0) return;
            var text = permanent
                ? "永久删除选中的 " + paths.Count + " 项？此操作不会进入回收站，且无法由 DeskBox 撤销。"
                : "把选中的 " + paths.Count + " 项移入 Windows 回收站？";
            if (!Prompt.Confirm(this, permanent ? "永久删除" : "移入回收站", text, Palette)) return;
            RunShellOperation(delegate { return _shellFiles.Delete(paths, permanent); }, permanent ? "permanent-delete" : "recycle");
        }

        private void UndoShellOperation()
        {
            if (_shellFiles == null || !_shellFiles.CanUndo) { _lblConn.Text = "没有可撤销的桌面文件操作"; return; }
            RunShellOperation(delegate { return _shellFiles.Undo(); }, "undo");
        }

        private void AskAiForSelection()
        {
            var selected = _board.SelectedItems;
            if (selected.Count == 0) return;
            AskAiForPaths(selected.Select(x => x.Path));
        }

        private void AskAiForPaths(IEnumerable<string> paths)
        {
            var selectedPaths = (paths ?? Enumerable.Empty<string>()).ToArray();
            var lines = string.Join("\r\n", selectedPaths.Select(x => "- " + x).ToArray());
            if (string.IsNullOrWhiteSpace(lines)) return;
            SwitchView("ai");
            if (_chat != null) foreach (var path in selectedPaths)
            {
                if (File.Exists(path) && !_chat.AllowedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _chat.AllowedFiles.Add(path);
                if (Directory.Exists(path) && !_chat.AllowedRoots.Contains(path, StringComparer.OrdinalIgnoreCase)) _chat.AllowedRoots.Add(path);
            }
            if (_chatView != null)
                _chatView.SetPrompt("请使用 doc.read 读取以下已选工程文档的真实正文；若为目录，先用 fs.search 找到文档。根据实际内容分析，引用页码、段落或工作表/行，不能只按文件名推断。内容未读完时明确说明：\r\n" + lines);
        }

        private void OnExternalFilesDropped(List<string> paths, bool move)
        {
            if (paths == null || paths.Count == 0) return;
            var action = move ? "移动" : "复制";
            if (!Prompt.Confirm(this, action + "文件", action + " " + paths.Count + " 项到：\r\n" + CurrentFolder() + "？", Palette)) return;
            RunShellOperation(delegate { return move ? _shellFiles.Move(paths, CurrentFolder()) : _shellFiles.Copy(paths, CurrentFolder()); }, move ? "move" : "copy");
        }

        private void RunShellOperation(Func<ShellOperationResult> operation, string action)
        {
            if (operation == null || _shellFiles == null) return;
            _lblConn.Text = "FILE · 正在执行 " + action;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                ShellOperationResult result;
                try { result = operation(); } catch (Exception ex) { result = new ShellOperationResult { Message = ex.Message }; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _lblConn.Text = (result.Ok ? "● " : "● ERROR · ") + result.Message;
                        _lblConn.ForeColor = result.Ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                        _store.Log(result.Ok ? "info" : "error", "file", action + "： " + result.Message);
                        LoadItems(false);
                    }));
                }
                catch { }
            });
        }

        private void ToggleHidden(DesktopItem it, bool hidden)
        {
            if (it == null) return;
            it.Hidden = hidden;
            ItemOverride ov;
            if (!_store.Overrides.TryGetValue(it.Id, out ov) || ov == null) { ov = new ItemOverride(); _store.Overrides[it.Id] = ov; }
            ov.Hidden = hidden;
            ov.HideMode = _store.Settings.HideMode;
            if (!string.IsNullOrEmpty(it.CategoryId)) ov.CategoryId = it.CategoryId;
            if (_store.Settings.HideMode == "attribute") Win32.SetHiddenAttribute(it.Path, hidden);
            _store.Save();
            _store.Log("info", hidden ? "hide" : "show", (hidden ? "隐藏 " : "显示 ") + it.Name);
            LoadItems(false);
            _widgets.RefreshLogs();
        }

        private void Assign(DesktopItem it, string catId)
        {
            if (it == null) return;
            var cat = _store.Categories.FirstOrDefault(c => c.Id == catId);
            it.CategoryId = catId;
            ItemOverride ov;
            if (!_store.Overrides.TryGetValue(it.Id, out ov) || ov == null) { ov = new ItemOverride(); _store.Overrides[it.Id] = ov; }
            ov.CategoryId = catId;
            ov.Hidden = it.Hidden;
            _store.Save();
            _store.Log("info", "assign", "将 " + it.Name + " 归入「" + (cat != null ? cat.Name : catId) + "」");
            LoadItems(false);
            _widgets.RefreshLogs();
        }

        // ---------- 拖放 ----------
        private void OnItemDropped(string id, Point screenPt)
        {
            var it = _items.FirstOrDefault(i => i.Id == id);
            if (it == null) return;
            var client = _sidebar.PointToClient(screenPt);
            if (_sidebar.Visible && _sidebar.ClientRectangle.Contains(client))
            {
                var cat = _sidebar.HitCategory(client);
                if (cat != null && cat != "__add__" && cat != "all" && cat != "hidden" && cat != "uncategorized")
                    Assign(it, cat);
            }
            _sidebar.SetDropTarget(null);
        }

        private void BoardMouseUp(object sender, MouseEventArgs e) { _sidebar.SetDropTarget(null); }

        // ---------- 视图 / 排序 ----------
        private void CycleView()
        {
            var v = _board.View;
            var next = v == BoardView.Grid ? BoardView.List : v == BoardView.List ? BoardView.Details : v == BoardView.Details ? BoardView.Compact : BoardView.Grid;
            _store.Settings.View = next == BoardView.Grid ? "grid" : next == BoardView.List ? "list" : next == BoardView.Details ? "details" : "compact";
            _store.SaveSettingsOnly();
            _board.RefreshTheme();
            UpdateToolButtons();
        }

        private void CycleSort()
        {
            string[] bys = { "name", "mtime", "size", "kind" };
            int i = Array.IndexOf(bys, _store.Settings.SortBy);
            _store.Settings.SortBy = bys[(i + 1) % bys.Length];
            _store.SaveSettingsOnly();
            _board.Invalidate();
            UpdateToolButtons();
        }

        private void ToggleShowHidden()
        {
            _store.Settings.ShowHidden = !_store.Settings.ShowHidden;
            _store.SaveSettingsOnly();
            LoadItems(false);
            UpdateToolButtons();
        }

        private void UpdateToolButtons()
        {
            _btnView.Text = _board.View == BoardView.Grid ? "▦ 网格" : _board.View == BoardView.List ? "☰ 列表" : _board.View == BoardView.Details ? "☷ 详细" : "≡ 紧凑";
            string sortName = _store.Settings.SortBy == "name" ? "名称" : _store.Settings.SortBy == "mtime" ? "时间"
                : _store.Settings.SortBy == "size" ? "大小" : "类型";
            _btnSort.Text = sortName + (_store.Settings.SortDir == "desc" ? " ↓" : " ↑");
            _btnHiddenToggle.Text = _store.Settings.ShowHidden ? "◉ 隐藏项" : "○ 隐藏项";
        }

        // ---------- 分类 ----------
        private void NewCategory()
        {
            var name = Prompt.Input(this, "新建分类", "分类名称（如：项目资料）", "", Palette);
            if (string.IsNullOrWhiteSpace(name)) return;
            var id = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            _store.Categories.Add(new Category
            {
                Id = id, Name = name.Trim(), Icon = "📦",
                Color = "#F472B6", Order = _store.Categories.Count + 1, Builtin = false
            });
            _store.Save();
            _store.Log("info", "settings", "新建分类「" + name.Trim() + "」");
            _sidebar.Invalidate();
            _widgets.RefreshLogs();
        }

        private void CategoryMenu(string catId)
        {
            var cat = _store.Categories.FirstOrDefault(c => c.Id == catId);
            if (cat == null) return;
            var menu = MenuBuilder.Create(Palette);
            menu.Items.Add(MenuBuilder.Item("重命名", () => RenameCategory(cat)));
            menu.Items.Add(MenuBuilder.Item("更换颜色", () => PickColor(cat)));
            if (!cat.Builtin) menu.Items.Add(MenuBuilder.Item("删除分类", () => DeleteCategory(cat)));
            menu.Show(_sidebar, _sidebar.PointToClient(Cursor.Position));
        }

        private void RenameCategory(Category cat)
        {
            var name = Prompt.Input(this, "重命名分类", "新名称", cat.Name, Palette);
            if (string.IsNullOrWhiteSpace(name)) return;
            cat.Name = name.Trim();
            _store.Save();
            _sidebar.Invalidate();
            LoadItems(false);
        }

        private void PickColor(Category cat)
        {
            var colors = new[] { "#60A5FA", "#F472B6", "#A78BFA", "#34D399", "#FBBF24", "#F97316", "#38BDF8", "#FACC15" };
            var menu = MenuBuilder.Create(Palette);
            foreach (var c in colors)
            {
                var cc = c;
                var item = MenuBuilder.Item(c, () =>
                {
                    cat.Color = cc;
                    _store.Save();
                    _sidebar.Invalidate();
                    _board.Invalidate();
                });
                menu.Items.Add(item);
            }
            menu.Show(_sidebar, _sidebar.PointToClient(Cursor.Position));
        }

        private void DeleteCategory(Category cat)
        {
            var ok = Prompt.Confirm(this, "删除分类", "删除「" + cat.Name + "」？其中条目会回到「其他」。", Palette);
            if (!ok) return;
            _store.Categories.Remove(cat);
            foreach (var ov in _store.Overrides.Values)
                if (ov.CategoryId == cat.Id) ov.CategoryId = "others";
            _store.Save();
            _store.Log("info", "settings", "删除分类「" + cat.Name + "」");
            _sidebar.Invalidate();
            LoadItems(false);
        }

        // ---------- 设置 / 主题 ----------
        public void OpenSettings()
        {
            // 规格十一：统一设置中心（7 页），不再是弹窗
            try
            {
                SwitchView("settings");
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "打开设置中心失败，回退旧对话框: " + ex.Message);
                using (var f = new SettingsForm(_store, Palette))
                {
                    if (f.ShowDialog(this) == DialogResult.OK)
                    {
                        _store.Save();
                        ApplyTheme();
                        LoadItems(false);
                        _widgets.RefreshAll();
                    }
                }
            }
        }

        public void ApplyTheme()
        {
            Palette = Palette.For(_store.Settings.Theme, _store.Settings.Accent);
            BackColor = Palette.Bg;
            _topbar.BackColor = Palette.HeaderBg;
            _statusbar.BackColor = Palette.HeaderBg;
            _sidebar.RefreshTheme();
            _board.RefreshTheme();
            _widgets.RefreshAll();
            foreach (var b in new[] { _btnView, _btnSort, _btnHiddenToggle, _btnRefresh, _btnAi, _btnMobile, _btnNotifications, _btnCommand, _btnSettings })
            {
                b.BackColor = Palette.PanelAlt;
                b.ForeColor = Palette.TextDim;
                b.FlatAppearance.BorderColor = Palette.Line;
                b.FlatAppearance.MouseOverBackColor = Palette.Hover;
            }
            _search.BackColor = Palette.PanelAlt;
            _search.ForeColor = Palette.Text;
            _lblPath.ForeColor = Palette.TextDim;
            _lblCount.ForeColor = Palette.TextDim;
            if (_topContext != null) _topContext.ForeColor = Palette.TextDim;
            UpdateToolButtons();
            UpdateGlobalControls();
            Invalidate(true);
        }

        public void ApplyIconTakeover(bool hide)
        {
            if (Win32.SetSystemIconsVisible(!hide))
            {
                SystemIconsHidden = hide;
                _store.Settings.TakeoverIcons = hide;
                _store.SaveSettingsOnly();
                _store.Log("info", "settings", hide ? "接管桌面：隐藏系统桌面图标" : "恢复系统桌面图标");
            }
        }

        // ---------- 桌面层 ----------
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int currentStyle = Win32.GetWindowLong(Handle, Win32.GWL_EXSTYLE);
            Win32.SetWindowLong(Handle, Win32.GWL_EXSTYLE, currentStyle | Win32.WS_EX_TOOLWINDOW);
            // 常驻桌面层：置底 + 不抢焦点；WorkerW 挂载为可选（部分系统/多屏下不稳定）
            Win32.SetWindowPos(Handle, Win32.HWND_BOTTOM, 0, 0, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOACTIVATE);
            if (_store.Settings.TakeoverIcons) ApplyIconTakeover(true);
            try
            {
                bool ok = Win32.RegisterHotKey(Handle, GlobalCommandHotkeyId, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, (uint)Keys.Space);
                if (!ok) AppLog.Write("warn", "system", "全局快捷键 Ctrl+Alt+Space 注册失败，可能已被其他软件占用");
                if(!Win32.RegisterHotKey(Handle,GlobalWinFHotkeyId,Win32.MOD_WIN|Win32.MOD_NOREPEAT,(uint)Keys.F))
                {
                    _winFShortcut=new WinFShortcut(delegate{if(!IsDisposed)BeginInvoke(new Action(ShowWorkspace));});
                    if(!_winFShortcut.Start())AppLog.Write("warn","system","Win+F呼出未能启用，请使用Ctrl+Alt+Space");
                }
            }
            catch (Exception ex) { AppLog.Write("warn", "system", "全局快捷键注册失败：" + ex.Message); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            PushToBottom();
        }

        public void PushToBottom()
        {
            if (!Visible) return;
            Win32.SetWindowPos(Handle, Win32.HWND_BOTTOM, 0, 0, 0, 0,
                Win32.SWP_NOSIZE | Win32.SWP_NOMOVE | Win32.SWP_NOACTIVATE);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = Screen.PrimaryScreen.WorkingArea;
            PushToBottom();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { Win32.UnregisterHotKey(Handle, GlobalCommandHotkeyId); } catch { }
            try { Win32.UnregisterHotKey(Handle, GlobalWinFHotkeyId);if(_winFShortcut!=null){_winFShortcut.Dispose();_winFShortcut=null;} } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Win32.WM_HOTKEY && (m.WParam.ToInt32() == GlobalCommandHotkeyId || m.WParam.ToInt32() == GlobalWinFHotkeyId))
            {
                try
                {
                    if(m.WParam.ToInt32()==GlobalWinFHotkeyId)ShowWorkspace();else ShowAndOpenCommandPalette();
                }
                catch { }
                return;
            }
            base.WndProc(ref m);
        }

        private Image _wallpaper;
        private System.IO.MemoryStream _wallpaperStream;
        private string _wallpaperPath;

        private void EnsureWallpaper()
        {
            var path = Win32.GetWallpaperPath();
            if (path == _wallpaperPath && _wallpaper != null) return;
            _wallpaperPath = path;
            try { if (_wallpaper != null) { _wallpaper.Dispose(); _wallpaper = null; } } catch { }
            try { if (_wallpaperStream != null) { _wallpaperStream.Dispose(); _wallpaperStream = null; } } catch { }
            try
            {
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    // GDI+ 要求 Image 的生命周期内底层流保持打开：这里用 byte[] + MemoryStream 并持有引用
                    var bytes = System.IO.File.ReadAllBytes(path);
                    _wallpaperStream = new System.IO.MemoryStream(bytes);
                    _wallpaper = Image.FromStream(_wallpaperStream);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "system", "壁纸加载失败: " + ex.Message);
                _wallpaper = null;
            }
        }


        /// <summary>绘制壁纸 + 主题遮罩；offsetInForm 为控件在主窗体客户区中的位置。</summary>
        public void PaintBackdrop(Graphics g, Point offsetInForm, Size size)
        {
            EnsureWallpaper();
            var clip = g.Clip;
            g.SetClip(new Rectangle(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)));
            if (_wallpaper != null)
            {
                float scale = Math.Max((float)ClientRectangle.Width / _wallpaper.Width,
                                       (float)ClientRectangle.Height / _wallpaper.Height);
                int w = (int)(_wallpaper.Width * scale), h = (int)(_wallpaper.Height * scale);
                int x = (ClientRectangle.Width - w) / 2 - offsetInForm.X;
                int y = (ClientRectangle.Height - h) / 2 - offsetInForm.Y;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(_wallpaper, new Rectangle(x, y, w, h));
                using (var b = new SolidBrush(Color.FromArgb((int)(Math.Max(0, Math.Min(1, _store.Settings.TintOpacity)) * 255), Palette.Bg)))
                    g.FillRectangle(b, 0, 0, size.Width, size.Height);
            }
            else
            {
                using (var brush = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height)), Palette.Bg, Palette.Bg2, 35f))
                    g.FillRectangle(brush, 0, 0, Math.Max(1, size.Width), Math.Max(1, size.Height));
            }
            g.Clip = clip;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            EnsureWallpaper();
            if (_wallpaper != null)
            {
                // 铺满窗口（等比裁切），保留用户壁纸
                float scale = Math.Max((float)ClientRectangle.Width / _wallpaper.Width,
                                       (float)ClientRectangle.Height / _wallpaper.Height);
                int w = (int)(_wallpaper.Width * scale), h = (int)(_wallpaper.Height * scale);
                int x = (ClientRectangle.Width - w) / 2, y = (ClientRectangle.Height - h) / 2;
                g.DrawImage(_wallpaper, new Rectangle(x, y, w, h));
                using (var b = new SolidBrush(Color.FromArgb((int)(Math.Max(0, Math.Min(1, _store.Settings.TintOpacity)) * 255), Palette.Bg)))
                    g.FillRectangle(b, ClientRectangle);
            }
            else
            {
                using (var brush = new LinearGradientBrush(ClientRectangle, Palette.Bg, Palette.Bg2, 35f))
                    g.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _closing = true;
            if (_desktopMenu != null) { _desktopMenu.Dispose(); _desktopMenu = null; }
            if (_liveVoice != null) _liveVoice.CancelForShutdown();
            try { if (_chat != null) _chat.Cancel(); } catch { }
            try { if (_workflows != null) { _workflows.CancelAll(); _workflows.StopScheduler(); } } catch { }
            try { if (_clipboard != null) _clipboard.Stop(); } catch { }
            try { if (_monitor != null) _monitor.Stop(); } catch { }
            try { if (_desktopWatcher != null) _desktopWatcher.Dispose(); } catch { }
            try { if (_cloudSync != null) {_cloudSync.Changed-=OnGlobalContextChanged;_cloudSync.Dispose();} } catch { }
            try { if (_localSync != null) { _localSync.Changed -= OnGlobalContextChanged; _localSync.Dispose(); } } catch { }
            try { if (_board != null) _board.Thumbnails = null; } catch { }
            try { if (_thumbnails != null) { _thumbnails.Dispose(); _thumbnails = null; } } catch { }
            if (_folderSession != null) _folderSession.Changed -= OnFolderLocationChanged;
            if (_notifications != null) { _notifications.Published -= OnNotificationPublished; _notifications.Changed -= OnGlobalContextChanged; }
            if (_workspaces != null) _workspaces.Changed -= OnGlobalContextChanged;
            if (_toolTip != null) { _toolTip.Dispose(); _toolTip = null; }
            StopAndDisposeTimer(ref _clockTimer);
            StopAndDisposeTimer(ref _scanTimer);
            StopAndDisposeTimer(ref _connTimer);
            StopAndDisposeTimer(ref _reminderTimer);
            StopAndDisposeTimer(ref _noticeStatusTimer);
            if (SystemIconsHidden) Win32.SetSystemIconsVisible(true);
            base.OnFormClosing(e);
        }

        private static void StopAndDisposeTimer(ref Timer timer)
        {
            if (timer == null) return;
            try { timer.Stop(); timer.Dispose(); } catch { }
            timer = null;
        }

        private HolographicCallView _liveVoice;

        internal void ShowLiveVoice()
        {
            if (_liveVoice != null) return;
            var voice = new HolographicCallView(new DeskBox.Ai.RealtimeVoiceSettingsStore().Load());
            _liveVoice = voice;
            if (_reference != null) _reference.Hide();
            voice.ExitRequested += async delegate {
                await voice.StopAsync();
                if (IsDisposed || _closing) return;
                Controls.Remove(voice);
                voice.Dispose();
            };
            voice.Disposed += delegate {
                if (_liveVoice == voice) _liveVoice = null;
                if (_closing || IsDisposed) return;
                if (_referenceActive && _reference != null) _reference.Show();
                if (_chatView != null) _chatView.FocusInput();
            };
            Controls.Add(voice);
            voice.BringToFront();
            voice.Focus();
            BeginInvoke(new Action(voice.Start));
        }

        private void OnFormKeyDown(object sender, KeyEventArgs e)
        {
            if (_liveVoice != null) return;
            if (e.Control && e.KeyCode == Keys.K) { ShowCommandPalette(); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.A) { SwitchView("ai"); if (_chatView != null) BeginInvoke(new Action(_chatView.FocusInput)); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.E) { SwitchView("engineering"); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.D1) { SwitchView("home"); e.Handled = true; }
            else if (_currentView == "desktop" && HandleDesktopShortcut(e)) { e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { if (_currentView == "desktop") LoadItems(true);else if(_referenceActive)_reference.RefreshCurrentPage(); else { var v = _views.ContainsKey(_currentView) ? _views[_currentView] as DeskBox.UI.Views.ViewBase : null; if (v != null) v.Activate(); } e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.F) { if (_currentView == "desktop") { _search.Focus(); _search.SelectAll(); } else SwitchView("search"); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.P) { SwitchView("projects"); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.T) { SwitchView("tasks"); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { _board.ClearSelection(); _search.Text = ""; }
        }

        private bool HandleDesktopShortcut(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A) { _board.SelectAllVisible(); return true; }
            if (e.Control && e.KeyCode == Keys.C) { CopySelection(false); return true; }
            if (e.Control && e.KeyCode == Keys.X) { CopySelection(true); return true; }
            if (e.Control && e.KeyCode == Keys.V) { PasteFiles(); return true; }
            if (e.Control && e.KeyCode == Keys.Z) { UndoShellOperation(); return true; }
            if (e.KeyCode == Keys.F2) { RenameSelection(); return true; }
            if (e.KeyCode == Keys.Delete) { DeleteSelection(e.Shift); return true; }
            if (e.Alt && e.KeyCode == Keys.Enter)
            {
                var item = _board.SelectedItems.FirstOrDefault(); if (item != null) Win32.ShowProperties(item.Path); return true;
            }
            if (e.KeyCode == Keys.Back || (e.Alt && e.KeyCode == Keys.Left)) { if (_folderSession != null) _folderSession.Back(); return true; }
            if (e.Alt && e.KeyCode == Keys.Right) { if (_folderSession != null) _folderSession.Forward(); return true; }
            return false;
        }
    }
}
