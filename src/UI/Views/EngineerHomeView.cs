using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;
using DeskBox.Fs;
using DeskBox.Sys;

namespace DeskBox.UI.Views
{
    /// <summary>默认工程驾驶舱：在 DeskBox 内完成项目、AI、工具、任务与系统状态切换。</summary>
    public class EngineerHomeView : ViewBase
    {
        private readonly WorkspaceStore _workspaces;
        private readonly FileLibrary _library;
        private readonly SystemMonitor _monitor;
        private readonly NotificationStore _notifications;
        private readonly ProjectControlService _projectControl;
        private readonly Timer _timer;
        private Label _clock, _system, _tasks, _recent, _alerts, _heroProject, _pulse, _timeline, _nextAction;
        private Panel _hero, _systemCard, _taskCard, _alertCard, _projectCard, _recentCard, _pulseCard, _timelineCard, _nextCard;
        private ProjectHealthControl _projectHealth;
        private ProjectHealthControl _dependencyHealth, _focusHealth;
        private Panel _dependencyCard, _focusCard;
        private Panel _moduleCard;
        private readonly System.Collections.Generic.List<FeatureTile> _featureTiles = new System.Collections.Generic.List<FeatureTile>();
        private readonly DarkButton[] _layoutButtons = new DarkButton[3];
        private string _layoutMode;
        private int[] _activityValues = new int[7];
        private DarkSelect _projectPicker;
        private EngineerWorkspace[] _projectChoices = new EngineerWorkspace[0];
        private bool _selectingProject, _forceProjectRefresh;
        private ValidationFlowControl _validationFlow;
        public event Action<EngineerWorkspace> ProjectSearchRequested;
        public event Action<string> NavigateRequested;
        public event Action ContextToggleRequested;

        public EngineerHomeView(Store store, WorkspaceStore workspaces, FileLibrary library, SystemMonitor monitor, NotificationStore notifications)
            : base(store, "工程驾驶舱", "Automation · DCS · Siemens PLC · CSV / GxP · Pharmaceutical Equipment")
        {
            _workspaces = workspaces; _library = library; _monitor = monitor; _notifications = notifications;
            _projectControl = new ProjectControlService(store, workspaces);
            _layoutMode = store.Settings.HomeLayout == "focus" || store.Settings.HomeLayout == "system" ? store.Settings.HomeLayout : "mission";
            Build();
            if (_workspaces != null) _workspaces.Changed += OnProjectChanged;
            _timer = new Timer { Interval = 2000 };
            _timer.Tick += delegate { if (Visible) RefreshData(); };
            _timer.Start();
            RefreshData();
        }

        private void Build()
        {
            Body.AutoScroll = true;
            BuildLayoutSwitcher();
            Header.Height = 86;
            HintLabel.Top = 32; HintLabel.Height = 16;
            _projectPicker = new DarkSelect(new string[0]) { Left = 6, Top = 50, Width = 330, Palette = Palette };
            _projectPicker.SelectedIndexChanged += delegate { if (!_selectingProject && _projectPicker.SelectedIndex >= 0 && _projectPicker.SelectedIndex < _projectChoices.Length) _workspaces.SetCurrent(_projectChoices[_projectPicker.SelectedIndex].Id); };
            Header.Controls.Add(_projectPicker);
            var openFolder = new DarkButton("打开项目文件夹") { Left = 344, Top = 50, Width = 130 };
            openFolder.Click += delegate { var project = _workspaces.Current; if (project == null) return; try { if (!System.IO.Directory.Exists(project.Root)) throw new System.IO.DirectoryNotFoundException("项目文件夹不存在：" + project.Root); DeskBox.Native.Win32.OpenPath(project.Root); _workspaces.Touch(project.Id); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开项目文件夹"); } };
            var searchProject = new DarkButton("项目内搜索") { Left = 482, Top = 50, Width = 110 };
            searchProject.Click += delegate { if (ProjectSearchRequested != null) ProjectSearchRequested(_workspaces.Current); };
            var editProgress = new DarkButton("编辑验证进度") { Left = 600, Top = 50, Width = 120 };
            editProgress.Click += delegate { var project = _workspaces.Current; if (project != null) using (var form = new ProjectProgressForm(_workspaces, project, Palette)) form.ShowDialog(this); };
            Header.Controls.AddRange(new Control[] { openFolder, searchProject, editProgress });
            var contextToggle = new DarkButton("信息侧栏") { Left = 728, Top = 50, Width = 90 };
            contextToggle.Click += delegate { if (ContextToggleRequested != null) ContextToggleRequested(); };
            Header.Controls.Add(contextToggle);
            _hero = new Panel { Left = 6, Top = 8, Width = 1220, Height = 118, BackColor = Palette.CardBg };
            _hero.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (_hero.BackgroundImage != null)
                {
                    var image = _hero.BackgroundImage; float ratio = Math.Max((float)_hero.Width / image.Width, (float)_hero.Height / image.Height);
                    float sw = _hero.Width / ratio, sh = _hero.Height / ratio;
                    e.Graphics.DrawImage(image, _hero.ClientRectangle, new RectangleF((image.Width - sw) / 2, (image.Height - sh) / 2, sw, sh), GraphicsUnit.Pixel);
                }
                Palette.DrawRounded(e.Graphics, Color.FromArgb(150, ModeAccent), new Rectangle(0, 0, _hero.Width - 1, _hero.Height - 1), 14, 1.2f);
            };
            using (var stream = typeof(EngineerHomeView).Assembly.GetManifestResourceStream("DeskBox.EngineeringFactory"))
                if (stream != null) using (var source = Image.FromStream(stream)) _hero.BackgroundImage = new Bitmap(source);
            _hero.Controls.Add(new Label { Text = "DESKBOX  //  ENGINEERING MISSION CONTROL", Left = 22, Top = 18, AutoSize = true, ForeColor = Palette.Accent, BackColor = Color.Transparent, Font = new Font("Consolas", 13F, FontStyle.Bold) });
            _heroProject = new Label { Text = "未设置当前项目", Left = 22, Top = 50, AutoSize = true, ForeColor = Palette.Text, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold) };
            _hero.Controls.Add(_heroProject);
            _hero.Controls.Add(new Label { Text = "从项目、文件、诊断、AI 到验证证据，尽量不离开当前工作台。", Left = 24, Top = 84, AutoSize = true, ForeColor = Palette.TextDim, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 9F) });
            _clock = new Label { Left = 930, Top = 24, Width = 260, Height = 68, TextAlign = ContentAlignment.MiddleRight, ForeColor = Palette.Text, BackColor = Color.Transparent, Font = new Font("Consolas", 22F, FontStyle.Bold) };
            _hero.Controls.Add(_clock);
            Body.Controls.Add(_hero);

            _systemCard = Card("系统遥测", "真实本机指标 · 2 秒刷新", 6, 266, 390, 210);
            _system = ValueLabel(_systemCard);
            _taskCard = Card("任务与验证", "未完成、逾期与高优先级", 410, 266, 390, 210);
            _tasks = ValueLabel(_taskCard);
            _alertCard = Card("通知 / 风险", "未读提醒与最近错误", 814, 266, 412, 210);
            _alerts = ValueLabel(_alertCard);

            _projectCard = Card("PROJECT HEALTH", "真实阶段权重、任务、截止日期与风险", 6, 490, 590, 300);
            _projectHealth = new ProjectHealthControl(Palette) { Left = 18, Top = 66, Width = 540, Height = 190, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            _projectCard.Controls.Add(_projectHealth);
            _validationFlow = new ValidationFlowControl(Palette) { Visible = false };
            _projectCard.Controls.Add(_validationFlow);
            var openProjects = new DarkButton("管理项目工作区") { Primary = true, Left = 18, Top = 260, Width = 150 };
            openProjects.Click += delegate { Go("projects"); }; _projectCard.Controls.Add(openProjects);

            _recentCard = Card("最近工程文件", "从收藏与最近访问继续工作", 610, 490, 616, 300);
            _recent = ValueLabel(_recentCard, 190);
            var openRecent = new DarkButton("打开文件管理") { Left = 18, Top = 260, Width = 140 };
            openRecent.Click += delegate { Go("search"); }; _recentCard.Controls.Add(openRecent);

            _pulseCard = Card("PROJECT PULSE · 7 DAYS", "文件、任务、AI 与异常的真实活动", 6, 804, 390, 210);
            _pulse = ValueLabel(_pulseCard, 120);
            _timelineCard = Card("ENGINEERING TIMELINE", "最近工程活动", 410, 804, 500, 210);
            _timeline = ValueLabel(_timelineCard, 125);
            _timelineCard.Paint += DrawActivity;
            _nextCard = Card("NEXT ACTION", "基于截止、阻塞、风险与最低进度阶段", 924, 804, 302, 210);
            _nextAction = ValueLabel(_nextCard, 125);
            _dependencyCard = Card("ENGINEERING DEPENDENCY MAP", "Phase View    /    工程阶段与交付依赖", 6, 126, 1200, 280);
            _dependencyHealth = new ProjectHealthControl(Palette) { Presentation = "dependency", Left = 18, Top = 66, Height = 200, Width = 1164 };
            _dependencyCard.Controls.Add(_dependencyHealth);
            _focusCard = Card("CURRENT TASK / 当前任务", "根据真实截止日期、阻塞与阶段进度推荐", 6, 200, 1200, 260);
            _focusHealth = new ProjectHealthControl(Palette) { Presentation = "focus", Left = 18, Top = 66, Height = 145, Width = 1164 };
            _focusCard.Controls.Add(_focusHealth);
            AddNav(_focusCard, "继续当前项目 →", "projects", 240);
            AddNav(_focusCard, "打开工程工作区", "desktop", 430);
            AddNav(_focusCard, "AI 分析下一步", "ai", 620);
            foreach (var button in _focusCard.Controls.OfType<DarkButton>()) button.Top = 214;
            _moduleCard = Card("功能工作区", "直接进入工程任务，所有入口连接现有功能", 6, 130, 1200, 300);
            string[,] modules = {
                { "desktop", "智能桌面", "真实文件、工程分区、预览与批量操作" },
                { "projects", "项目与验证", "多项目目录、27 节点流程、N/A 与进度" },
                { "ai", "AI 文档助手", "PDF / Word / Excel 正文读取与内容检索" },
                { "automation", "自动化工作流", "节点链路、预演、执行与结果追踪" },
                { "engineering", "工程工具", "PLC / DCS、4–20mA、仪表与验证工具" },
                { "search", "文件检索", "文件名、类型、目录、时间与大小筛选" },
                { "tasks", "任务中心", "待办、优先级、截止日期与提醒" },
                { "monitor", "运行监测", "CPU、内存、网络与磁盘实时状态" },
                { "mobile", "手机联动", "配对说明、设备信任与本地同步" }
            };
            for (int i = 0; i < modules.GetLength(0); i++)
            {
                string route = modules[i, 0];
                var tile = new FeatureTile(Palette, (i + 1).ToString("00"), modules[i, 1], modules[i, 2]);
                tile.Click += delegate { Go(route); }; _moduleCard.Controls.Add(tile); _featureTiles.Add(tile);
            }
            Body.Resize += delegate { LayoutDashboard(); };
            LayoutDashboard();
        }

        private void BuildLayoutSwitcher()
        {
            string[] names = { "指挥中心", "专注工作", "系统全景" }; string[] keys = { "mission", "focus", "system" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i; var b = new DarkButton(names[i]) { Width = 88, Height = 28, Top = 8, Primary = keys[i] == _layoutMode, Anchor = AnchorStyles.Top | AnchorStyles.Right };
                b.Palette = Palette.For(Store.Settings.Theme, i == 1 ? "#A78BFA" : i == 2 ? "#34D399" : Store.Settings.Accent);
                b.Click += delegate { SetLayout(keys[index]); };
                Header.Controls.Add(b); _layoutButtons[i] = b;
            }
            Header.Resize += delegate { PositionLayoutButtons(); };
            PositionLayoutButtons();
        }

        private void PositionLayoutButtons() { for (int i = 0; i < _layoutButtons.Length; i++) _layoutButtons[i].Left = Math.Max(320, Header.Width - 286) + i * 94; }
        private Color ModeAccent { get { return _layoutMode == "focus" ? Color.FromArgb(167, 139, 250) : _layoutMode == "system" ? Color.FromArgb(52, 211, 153) : Palette.Accent; } }

        private void SetLayout(string mode)
        {
            Body.AutoScrollPosition = Point.Empty;
            _layoutMode = mode; Store.Settings.HomeLayout = mode; Store.SaveSettingsOnly();
            for (int i = 0; i < _layoutButtons.Length; i++) { _layoutButtons[i].Primary = (i == 0 ? "mission" : i == 1 ? "focus" : "system") == mode; _layoutButtons[i].Invalidate(); }
            LayoutDashboard(); RefreshData();
        }

        private Panel Card(string title, string hint, int x, int y, int w, int h)
        {
            var p = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Palette.CardBg };
            p.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; Palette.DrawRounded(e.Graphics, Palette.Line, new Rectangle(0, 0, p.Width - 1, p.Height - 1), 12, 1f); };
            p.Controls.Add(new Label { Text = title, Left = 18, Top = 14, AutoSize = true, ForeColor = Palette.Text, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold) });
            p.Controls.Add(new Label { Text = hint, Left = 18, Top = 40, AutoSize = true, ForeColor = Palette.TextFaint, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 8F) });
            Body.Controls.Add(p); return p;
        }

        private Label ValueLabel(Panel p, int height = 115)
        {
            var l = new Label { Left = 18, Top = 74, Width = p.Width - 36, Height = height, ForeColor = Palette.TextDim, BackColor = Palette.CardBg, Font = new Font("Consolas", 10F), AutoEllipsis = true };
            p.Controls.Add(l); return l;
        }

        private void AddNav(Panel p, string text, string id, int x)
        {
            var b = new DarkButton(text) { Left = x, Top = 64, Width = 174, Height = 34, Primary = id == "ai" || id == "engineering" };
            b.Click += delegate { Go(id); }; p.Controls.Add(b);
        }

        private bool _layingOut;
        private void LayoutDashboard()
        {
            if (_moduleCard == null || _layingOut || Body.ClientSize.Width <= 0) return;
            _layingOut = true; Body.SuspendLayout();
            try
            {
                Body.AutoScrollPosition = Point.Empty;
                int total = Math.Max(850, Body.ClientSize.Width - 18);
                SetVisible(false, _systemCard, _taskCard, _alertCard, _recentCard, _pulseCard, _timelineCard, _nextCard, _dependencyCard, _focusCard);
                SetVisible(true, _hero, _moduleCard, _projectCard);
                _hero.SetBounds(6, 8, total, 112);
                _heroProject.AutoSize = false; _heroProject.AutoEllipsis = true; _heroProject.Width = total - 330; _heroProject.Height = 30;
                _clock.Left = total - _clock.Width - 24;
                var eyebrow = _hero.Controls.OfType<Label>().First();
                eyebrow.Text = _layoutMode == "focus" ? "DESKBOX  //  AI FOCUS WORKSPACE" : _layoutMode == "system" ? "DESKBOX  //  ENGINEERING SYSTEM CONTROL" : "DESKBOX  //  ENGINEERING MISSION CONTROL";
                eyebrow.ForeColor = ModeAccent;
                _moduleCard.Controls.OfType<Label>().First().ForeColor = ModeAccent;
                foreach (var button in _projectCard.Controls.OfType<DarkButton>()) button.Visible = false;
                if (_layoutMode == "focus") LayoutFocus(total);
                else if (_layoutMode == "system") LayoutSystem(total);
                else LayoutMission(total);
                foreach (var label in new[] { _system, _tasks, _alerts, _recent, _pulse, _timeline, _nextAction })
                {
                    label.Width = Math.Max(40, label.Parent.Width - 36);
                    label.Height = Math.Max(24, label.Parent.Height - label.Top - 16);
                }
                _projectHealth.SetBounds(18, 66, _projectCard.Width - 36, 190);
                _projectHealth.Presentation = null;
                _validationFlow.SetBounds(18, 66, _projectCard.Width - 36, Math.Max(100, _projectCard.Height - 80));
                int bottom = Body.Controls.Cast<Control>().Where(x => OwnLayoutVisible(x)).Select(x => x.Bottom).DefaultIfEmpty(0).Max();
                Body.AutoScrollMinSize = new Size(0, bottom + 16);
                Body.Invalidate(true);
            }
            finally { Body.ResumeLayout(true); _layingOut = false; }
        }

        private bool OwnLayoutVisible(Control control)
        {
            // Visibility is inherited from the form; layout also runs before the form is shown.
            if (control == _hero || control == _moduleCard || control == _projectCard) return true;
            if (_layoutMode == "mission") return control == _taskCard || control == _nextCard || control == _timelineCard || control == _pulseCard;
            if (_layoutMode == "focus") return control == _focusCard || control == _nextCard || control == _recentCard || control == _timelineCard;
            return control == _dependencyCard || control == _systemCard || control == _alertCard || control == _timelineCard;
        }

        private void LayoutMission(int total)
        {
            SetVisible(true, _taskCard, _nextCard, _timelineCard, _pulseCard);
            int main = (total - 14) * 61 / 100, right = total - main - 14;
            LayoutModules(20 + main, 134, right, right >= 360 ? 2 : 1, Palette.Accent);
            int y = 134;
            _projectCard.SetBounds(6, y, main, 320);
            int half = (main - 14) / 2;
            _taskCard.SetBounds(6, y + 334, half, 210);
            _nextCard.SetBounds(20 + half, y + 334, main - half - 14, 210);
            _timelineCard.SetBounds(6, y + 558, main, 290); _timeline.Top = 208;
            _pulseCard.SetBounds(20 + main, _moduleCard.Bottom + 14, right, 220);
            SetCardText(_projectCard, "PROJECT HEALTH / 项目进度", "真实验证流程、阶段完成度与项目状态");
            SetCardText(_taskCard, "任务与验证", "当前待办、逾期与高优先级");
        }

        private void LayoutFocus(int total)
        {
            SetVisible(true, _focusCard, _nextCard, _recentCard, _timelineCard);
            int main = (total - 14) * 64 / 100, right = total - main - 14;
            _focusCard.SetBounds(6, 134, main, 270); _focusHealth.Width = main - 36;
            int bw = (main - 52) / 3, i = 0;
            foreach (var button in _focusCard.Controls.OfType<DarkButton>()) button.SetBounds(18 + i++ * (bw + 8), 222, bw, 30);
            LayoutModules(20 + main, 134, right, right >= 360 ? 2 : 1, Color.FromArgb(167, 139, 250));
            _projectCard.SetBounds(6, 418, main, 320);
            _nextCard.SetBounds(20 + main, _moduleCard.Bottom + 14, right, 160);
            _recentCard.SetBounds(6, 752, main, 300);
            _timelineCard.SetBounds(20 + main, _nextCard.Bottom + 14, right, 250); _timeline.Top = 74;
            SetCardText(_projectCard, "CURRENT PROJECT / 当前项目", "围绕当前项目查看进度，编辑入口始终置顶");
        }

        private void LayoutSystem(int total)
        {
            SetVisible(true, _dependencyCard, _systemCard, _alertCard, _timelineCard);
            _dependencyCard.SetBounds(6, 134, total, 230);
            _dependencyHealth.SetBounds(18, 66, total - 36, 150);
            int main = (total - 14) * 61 / 100, right = total - main - 14;
            _projectCard.SetBounds(6, 378, main, 320);
            LayoutModules(20 + main, 378, right, right >= 360 ? 2 : 1, Color.FromArgb(52, 211, 153));
            int half = (main - 14) / 2;
            _systemCard.SetBounds(6, 712, half, 230); _alertCard.SetBounds(20 + half, 712, main - half - 14, 230);
            _timelineCard.SetBounds(6, Math.Max(956, _moduleCard.Bottom + 14), total, 230); _timeline.Top = 74;
            SetCardText(_projectCard, "VALIDATION CONTROL / 验证全景", "项目进度、验证节点及系统状态并排查看");
        }

        private void LayoutModules(int x, int y, int width, int columns, Color accent)
        {
            int tileWidth = (width - 36 - (columns - 1) * 10) / columns;
            int rows = (_featureTiles.Count + columns - 1) / columns;
            _moduleCard.SetBounds(x, y, width, 66 + rows * 86 + 12);
            for (int i = 0; i < _featureTiles.Count; i++)
            {
                _featureTiles[i].Accent = accent;
                _featureTiles[i].SetBounds(18 + i % columns * (tileWidth + 10), 66 + i / columns * 86, tileWidth, 76);
                _featureTiles[i].Invalidate();
            }
        }

        private static void SetVisible(bool visible, params Control[] controls) { foreach (var c in controls) if (c != null) c.Visible = visible; }
        private static void SetCardText(Panel card, string title, string hint) { var labels = card.Controls.OfType<Label>().Take(2).ToArray(); if (labels.Length > 0) labels[0].Text = title; if (labels.Length > 1) labels[1].Text = hint; }

        private class FeatureTile : Control
        {
            private Palette _palette;
            private readonly string _number, _title, _detail;
            private bool _hover;
            public Color Accent;
            public FeatureTile(Palette palette, string number, string title, string detail)
            {
                _palette = palette; _number = number; _title = title; _detail = detail; Accent = palette.Accent;
                DoubleBuffered = true; Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.PushButton; AccessibleName = title; AccessibleDescription = detail;
                SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
            public void ApplyPalette(Palette palette) { _palette = palette; Invalidate(); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; } base.OnKeyDown(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Palette.FillRounded(g, _hover ? _palette.Hover : _palette.PanelAlt, new Rectangle(0, 0, Width - 1, Height - 1), 9);
                Palette.DrawRounded(g, _hover || Focused ? Accent : _palette.Line, new Rectangle(0, 0, Width - 1, Height - 1), 9, 1);
                using (var title = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
                using (var detail = new Font("Microsoft YaHei UI", 8.5F))
                {
                    TextRenderer.DrawText(g, _title, title, new Rectangle(12, 9, Width - 48, 23), _palette.Text, TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, _number, detail, new Point(Width - 30, 12), Accent);
                    TextRenderer.DrawText(g, _detail, detail, new Rectangle(12, 36, Width - 24, 34), _palette.TextDim, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private void Go(string id) { var h = NavigateRequested; if (h != null) h(id); }

        private void DrawActivity(object sender, PaintEventArgs e)
        {
            if (_layoutMode != "mission") return;
            var values = _activityValues;
            float max = Math.Max(1, values.Max());
            int width = _timelineCard.Width - 80;
            var points = Enumerable.Range(0, 7).Select(i => new PointF(44 + width * i / 6f, 166 - 86 * values[i] / max)).ToArray();
            using (var grid = new Pen(Palette.Line))
                for (int i = 0; i < 4; i++) e.Graphics.DrawLine(grid, 44, 80 + i * 29, width + 44, 80 + i * 29);
            using (var fill = new SolidBrush(Color.FromArgb(25, Palette.Accent)))
            {
                var polygon = points.Concat(new[] { new PointF(width + 44, 167), new PointF(44, 167) }).ToArray();
                e.Graphics.FillPolygon(fill, polygon);
            }
            using (var pen = new Pen(Palette.Accent, 2)) e.Graphics.DrawLines(pen, points);
            using (var dot = new SolidBrush(Palette.Accent))
            using (var font = new Font("Consolas", 8))
                for (int i = 0; i < points.Length; i++)
                {
                    e.Graphics.FillEllipse(dot, points[i].X - 3, points[i].Y - 3, 6, 6);
                    TextRenderer.DrawText(e.Graphics, DateTime.Today.AddDays(i - 6).ToString("MM-dd"), font, new Point((int)points[i].X - 17, 177), Palette.TextDim);
                    TextRenderer.DrawText(e.Graphics, values[i].ToString(), font, new Point((int)points[i].X - 4, (int)points[i].Y - 18), Palette.TextDim);
                }
        }

        public void RefreshData()
        {
            if (IsDisposed) return;
            var activity = Store.ReadLogs(2000);
            _activityValues = Enumerable.Range(0, 7).Select(i => activity.Count(x => DateTimeOffset.FromUnixTimeMilliseconds(x.Ts).LocalDateTime.Date == DateTime.Today.AddDays(i - 6))).ToArray();
            _timelineCard.Invalidate();
            _clock.Text = DateTime.Now.ToString("HH:mm:ss\r\nyyyy-MM-dd");
            var s = _monitor == null ? null : _monitor.Last;
            _system.Text = s == null ? "CPU  --\r\nMEM  --\r\nNET  --" :
                "CPU  " + s.CpuPercent.ToString("0.0").PadLeft(5) + "%\r\n" +
                "MEM  " + s.MemUsedPercent.ToString("0.0").PadLeft(5) + "%  " + SizeText(s.MemUsed) + " / " + SizeText(s.MemTotal) + "\r\n" +
                "NET  ↓" + SizeText((long)s.NetRxBps) + "/s  ↑" + SizeText((long)s.NetTxBps) + "/s" + (string.IsNullOrEmpty(s.Error) ? "" : "\r\nERR  " + s.Error);

            int todo = Store.Tasks.Count(x => !x.Done), overdue = Store.Tasks.Count(x => !x.Done && DuePast(x.Due)), high = Store.Tasks.Count(x => !x.Done && x.Priority == "high");
            _tasks.Text = "OPEN      " + todo + "\r\nOVERDUE   " + overdue + "\r\nHIGH      " + high + "\r\n\r\n" + string.Join("\r\n", Store.Tasks.Where(x => !x.Done).Take(3).Select(x => "· " + x.Title).ToArray());
            var errors = Store.ReadLogs(100).Count(x => x.Level == "error");
            _alerts.Text = "UNREAD    " + (_notifications == null ? 0 : _notifications.UnreadCount) + "\r\nERRORS     " + errors + "\r\nSTATUS     " + (errors == 0 ? "NOMINAL" : "ATTENTION") + "\r\n\r\n所有高风险文件/工作流操作保持确认机制";
            _alerts.ForeColor = errors == 0 ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);

            var project = _workspaces == null ? null : _workspaces.Current;
            if (_projectPicker != null)
            {
                _selectingProject = true;
                try
                {
                    _projectChoices = _workspaces == null ? new EngineerWorkspace[0] : _workspaces.Items.ToArray();
                    var names = _projectChoices.Select(x => x.Name + (string.IsNullOrEmpty(x.Number) ? "" : " · " + x.Number)).ToArray();
                    if (!_projectPicker.Inner.Items.Cast<string>().SequenceEqual(names)) { _projectPicker.Inner.Items.Clear(); _projectPicker.Inner.Items.AddRange(names); }
                    _projectPicker.SelectedIndex = Array.FindIndex(_projectChoices, x => project != null && x.Id == project.Id);
                }
                finally { _selectingProject = false; }
            }
            var snapshot = _projectControl.GetSnapshot(_forceProjectRefresh); _forceProjectRefresh = false;
            bool validation = project != null && project.ValidationSteps != null;
            _validationFlow.Visible = validation; _projectHealth.Visible = !validation;
            if (validation) { _validationFlow.SetBounds(18, 66, _projectCard.Width - 36, Math.Max(100, _projectCard.Height - 80)); _validationFlow.SetProject(project); }
            _heroProject.Text = project == null ? "未设置当前项目 · 设备自动化 / DCS / Siemens PLC / CSV" : project.Name + (string.IsNullOrEmpty(project.Number) ? "" : " · " + project.Number) + "  //  " + snapshot.OverallProgress.ToString("0") + "%  " + snapshot.Health;
            if (project != null) _heroProject.Text = project.Name + " · " + project.Number + "  //  " + ValidationProgress.Display(project) + "  " + snapshot.Health;
            TitleLabel.Text = project == null ? "工程驾驶舱" : project.Name + " · 项目进度 " + ValidationProgress.Display(project);
            _projectHealth.SetSnapshot(snapshot);
            _dependencyHealth.SetSnapshot(snapshot); _focusHealth.SetSnapshot(snapshot);
            _pulse.Text = "FILES MODIFIED   " + snapshot.FilesModified7d + "\r\nTASKS COMPLETED  " + snapshot.TasksCompleted7d + "\r\nAI ACTIVITY      " + snapshot.AiActions7d + "\r\nERRORS           " + snapshot.Errors7d;
            _timeline.Text = snapshot.Timeline.Count == 0 ? "暂无可显示的工程活动。" : string.Join("\r\n", snapshot.Timeline.Take(6).ToArray());
            _nextAction.Text = snapshot.NextAction + (snapshot.DaysToDeadline == int.MaxValue ? "" : "\r\n\r\nDEADLINE  " + snapshot.DaysToDeadline + " DAYS");
            _nextAction.ForeColor = snapshot.Health == "AT RISK" ? Color.FromArgb(255, 195, 92) : Palette.Accent;
            var recent = _library == null ? null : _library.Recent.Take(5).ToArray();
            _recent.Text = recent == null || recent.Length == 0 ? "暂无最近文件。\r\n从文件管理打开项目文档后会出现在这里。" : string.Join("\r\n", recent.Select(x => "· " + System.IO.Path.GetFileName(x.Path) + "\r\n  " + x.Path).ToArray());
        }

        private void OnProjectChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(new Action(OnProjectChanged)); } catch { } return; }
            _forceProjectRefresh = true; RefreshData();
            Body.AutoScrollPosition = Point.Empty;
            _validationFlow.AutoScrollPosition = Point.Empty;
        }

        private static bool DuePast(string due) { DateTime d; return !string.IsNullOrEmpty(due) && DateTime.TryParse(due, out d) && d.Date < DateTime.Today; }
        private static string SizeText(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = Math.Max(0, bytes); int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.#") + " " + u[i];
        }
        protected override void OnActivated() { LayoutDashboard(); RefreshData(); }
        public override void ApplyPalette(Palette palette)
        {
            base.ApplyPalette(palette);
            foreach (var tile in _featureTiles) tile.ApplyPalette(palette);
            foreach (var health in new[] { _projectHealth, _dependencyHealth, _focusHealth }) if (health != null) { health.Palette = palette; health.BackColor = palette.CardBg; }
            LayoutDashboard();
        }
        protected override void Dispose(bool disposing) { if (_workspaces != null) _workspaces.Changed -= OnProjectChanged; if (_timer != null) { _timer.Stop(); _timer.Dispose(); } if (disposing && _hero != null && _hero.BackgroundImage != null) { _hero.BackgroundImage.Dispose(); _hero.BackgroundImage = null; } base.Dispose(disposing); }
    }
}
