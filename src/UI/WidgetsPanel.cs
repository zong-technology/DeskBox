using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeskBox
{
    /// <summary>
    /// 小部件面板：时钟 / 天气 / 待办 / 日志 / 统计。
    /// 深色玻璃拟态，全部自绘（GraphicsPath 圆角），零第三方依赖。
    /// 线程模型：天气在后台线程抓取，BeginInvoke 回 UI；其余全部在 UI 线程。
    /// </summary>
    public class WidgetsPanel : System.Windows.Forms.UserControl
    {
        // =====================================================================
        // 主题
        // =====================================================================

        /// <summary>一套主题配色。</summary>
        internal sealed class Skin
        {
            public bool Light;
            public Color Bg, CardBg, Line, Text, TextDim, TextFaint;
            public Color Accent, AccentText, AccentSoft, Hover, InputBg;
            public Color Danger, Warn, Ok;

            public static Skin For(string theme, string accentHex)
            {
                Skin s = new Skin();
                Color accent = ParseColor(accentHex, Color.FromArgb(110, 231, 255));
                s.Accent = accent;
                string t = (theme ?? "midnight").Trim().ToLowerInvariant();

                if (t == "aurora")
                {
                    s.Light = false;
                    s.Bg = Color.FromArgb(6, 26, 30);
                    s.CardBg = Color.FromArgb(14, 40, 44);
                    s.Line = Color.FromArgb(30, 70, 74);
                    s.Text = Color.FromArgb(226, 245, 242);
                    s.TextDim = Color.FromArgb(140, 178, 172);
                    s.TextFaint = Color.FromArgb(100, 134, 130);
                    s.Hover = Color.FromArgb(24, 60, 64);
                    s.InputBg = Color.FromArgb(10, 32, 36);
                    s.Danger = Color.FromArgb(252, 129, 129);
                    s.Warn = Color.FromArgb(250, 204, 21);
                    s.Ok = Color.FromArgb(52, 211, 153);
                }
                else if (t == "daylight")
                {
                    s.Light = true;
                    s.Bg = Color.FromArgb(245, 247, 251);
                    s.CardBg = Color.FromArgb(255, 255, 255);
                    s.Line = Color.FromArgb(222, 228, 240);
                    s.Text = Color.FromArgb(27, 36, 51);
                    s.TextDim = Color.FromArgb(96, 110, 130);
                    s.TextFaint = Color.FromArgb(140, 152, 170);
                    s.Hover = Color.FromArgb(237, 242, 250);
                    s.InputBg = Color.FromArgb(246, 248, 252);
                    s.Danger = Color.FromArgb(200, 48, 60);
                    s.Warn = Color.FromArgb(176, 112, 8);
                    s.Ok = Color.FromArgb(5, 122, 85);
                }
                else
                {
                    s.Light = false;
                    s.Bg = Color.FromArgb(9, 13, 22);
                    s.CardBg = Color.FromArgb(18, 24, 38);
                    s.Line = Color.FromArgb(38, 48, 68);
                    s.Text = Color.FromArgb(230, 237, 247);
                    s.TextDim = Color.FromArgb(139, 154, 179);
                    s.TextFaint = Color.FromArgb(96, 110, 133);
                    s.Hover = Color.FromArgb(30, 39, 58);
                    s.InputBg = Color.FromArgb(13, 18, 30);
                    s.Danger = Color.FromArgb(248, 113, 113);
                    s.Warn = Color.FromArgb(251, 191, 36);
                    s.Ok = Color.FromArgb(52, 211, 153);
                }

                s.AccentText = s.Light ? Mix(accent, Color.Black, 0.46) : accent;
                s.AccentSoft = Mix(accent, s.CardBg, 0.80);
                return s;
            }
        }

        // =====================================================================
        // 绘制 / 字体工具
        // =====================================================================

        internal const TextFormatFlags TF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        /// <summary>圆角矩形路径（12px 卡片、8px 按钮都走这里）。</summary>
        internal static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (r.Width <= 1 || r.Height <= 1) { path.AddRectangle(r); return path; }
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void FillRounded(Graphics g, Color c, Rectangle r, int radius)
        {
            using (GraphicsPath p = RoundedRect(r, radius))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        internal static void DrawRounded(Graphics g, Color c, Rectangle r, int radius, float width)
        {
            using (GraphicsPath p = RoundedRect(r, radius))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        internal static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        internal static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                string s = hex.Trim().TrimStart('#');
                if (s.Length == 6)
                    return Color.FromArgb(255,
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
                if (s.Length == 8)
                    return Color.FromArgb(
                        Convert.ToInt32(s.Substring(6, 2), 16),
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch (Exception) { }
            return fallback;
        }

        internal static int TextW(string text, Font f)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TF).Width;
        }

        internal static int TextH(string text, Font f)
        {
            if (string.IsNullOrEmpty(text)) text = "0";
            return TextRenderer.MeasureText(text, f, new Size(int.MaxValue, int.MaxValue), TF).Height;
        }

        internal static void TextAt(Graphics g, string text, Font f, int x, int y, Color c)
        {
            if (string.IsNullOrEmpty(text)) return;
            TextRenderer.DrawText(g, text, f, new Point(x, y), c, TF);
        }

        internal static void TextIn(Graphics g, string text, Font f, Rectangle r, Color c, TextFormatFlags extra)
        {
            if (string.IsNullOrEmpty(text) || r.Width <= 2 || r.Height <= 1) return;
            TextRenderer.DrawText(g, text, f, r, c, TF | extra);
        }

        internal static string WeekCn(DayOfWeek d)
        {
            switch (d)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }

        /// <summary>时区显示名截短：取 UTC 偏移 + 第一个地区。</summary>
        internal static string ShortTimeZone(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            int i = name.IndexOf(')');
            if (i > 0 && i + 1 < name.Length)
            {
                string offset = name.Substring(0, i).Trim().Trim('(').Trim();
                string rest = name.Substring(i + 1).Trim();
                int cut = rest.IndexOfAny(new char[] { '，', ',', '；', ';' });
                if (cut > 0) rest = rest.Substring(0, cut).Trim();
                if (rest.Length == 0) return offset;
                return offset + " · " + rest;
            }
            return name;
        }

        internal static string HHmm(long unixMs)
        {
            try
            {
                if (unixMs <= 0) return "";
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime
                    .ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            catch (Exception) { return ""; }
        }

        internal static Color PriorityColor(string p)
        {
            if (string.Equals(p, "high", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(248, 113, 113);
            if (string.Equals(p, "low", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(148, 163, 184);
            return Color.FromArgb(96, 165, 250);
        }

        internal static string PriorityCn(string p)
        {
            if (string.Equals(p, "high", StringComparison.OrdinalIgnoreCase)) return "高";
            if (string.Equals(p, "low", StringComparison.OrdinalIgnoreCase)) return "低";
            return "普通";
        }

        internal static string NextPriority(string p)
        {
            if (string.Equals(p, "low", StringComparison.OrdinalIgnoreCase)) return "normal";
            if (string.Equals(p, "normal", StringComparison.OrdinalIgnoreCase)) return "high";
            return "low";
        }

        internal static string LogIcon(string action)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "scan": return "🔍";
                case "hide": return "🙈";
                case "show": return "👁";
                case "open": return "↗";
                case "assign": return "🗂";
                case "task": return "✅";
                case "settings": return "⚙";
                default: return "ℹ";
            }
        }

        internal static string LogIconFamily(string action)
        {
            switch ((action ?? "").Trim().ToLowerInvariant())
            {
                case "open":
                case "settings":
                case "system": return "symbol";
                default: return "emoji";
            }
        }

        // =====================================================================
        // 字段
        // =====================================================================

        private readonly Store _store;
        private Skin _skin;
        private FlowLayoutPanel _flow;

        private ClockCard _clock;
        private WeatherCard _weather;
        private TaskCard _tasks;
        private LogCard _logs;
        private StatsCard _stats;
        internal Func<DeskBox.Engineering.ProjectContext> CurrentProject;
        private ContextCard _context;

        private readonly List<DesktopItem> _items = new List<DesktopItem>();
        private string _desktopPath = "";

        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>(StringComparer.Ordinal);
        private readonly Queue<Action> _pendingUi = new Queue<Action>();
        private Timer _timer;
        private int _ticks;
        private volatile bool _weatherBusy;
        private bool _fitting;
        private bool _disposed;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        // =====================================================================
        // 公共接口（MainForm 依赖，签名不可改）
        // =====================================================================

        /// <summary>点时钟卡右上角齿轮时触发，MainForm 打开设置。</summary>
        public event EventHandler SettingsRequested;

        /// <summary>点统计卡「总条目」时触发（可选），MainForm 可打开桌面目录。</summary>
        public event EventHandler RevealRequested;

        public WidgetsPanel(Store store)
        {
            _store = store ?? new Store();
            _skin = Skin.For(Cfg.Theme, Cfg.Accent);

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = _skin.Bg;
            Font = FontOf("ui", 9F, FontStyle.Regular);

            BuildUi();
            ApplyTheme();
            RefreshAll();
        }

        /// <summary>桌面条目 / 桌面路径变化时调用。</summary>
        public void Bind(List<DesktopItem> items, string desktopPath)
        {
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _desktopPath = desktopPath ?? "";
            UpdateStats();
        }

        public void SetContext(string title, string subtitle, string[] lines, bool showTasks, bool showLogs)
        {
            if (_context == null) return;
            _context.SetContent(title, subtitle, lines);
            _context.Visible = true;
            _clock.Visible = false; _weather.Visible = false; _stats.Visible = false;
            _tasks.Visible = showTasks; _logs.Visible = showLogs;
            if (showTasks) RefreshTasks();
            if (showLogs) RefreshLogs();
            FitWidths();
        }

        /// <summary>时钟 + 天气 + 任务 + 日志 + 统计全刷（含主题）。</summary>
        public void RefreshAll()
        {
            if (_disposed) return;
            ApplyTheme();
            if (_clock != null) { _clock.UpdateTime(); _clock.UpdateMetrics(); }
            RefreshTasks();
            RefreshLogs();
            UpdateStats();
            RefreshWeather(false);
            FitWidths();
        }

        /// <summary>只刷日志。</summary>
        public void RefreshLogs()
        {
            if (_disposed || _logs == null) return;
            List<LogEntry> list = null;
            try { list = _store.ReadLogs(30); } catch (Exception) { list = new List<LogEntry>(); }
            _logs.SetLogs(list);
            FitWidths();
        }

        /// <summary>只刷任务。</summary>
        public void RefreshTasks()
        {
            if (_disposed || _tasks == null) return;
            _tasks.Reload();
            FitWidths();
        }

        /// <summary>只刷天气（force=true 绕过缓存走网络）。后台线程抓取，失败降级。</summary>
        public void RefreshWeather(bool force)
        {
            if (_disposed || _weather == null || _weatherBusy) return;
            _weatherBusy = true;
            _weather.SetBusy(true);

            AppSettings snapshot = new AppSettings();
            snapshot.WeatherCity = Cfg.WeatherCity;
            snapshot.WeatherLat = Cfg.WeatherLat;
            snapshot.WeatherLon = Cfg.WeatherLon;
            snapshot.WeatherRefreshMinutes = Cfg.WeatherRefreshMinutes;

            try
            {
                Task.Run(delegate
                {
                    WeatherSnapshot snap = null;
                    try { snap = WeatherService.Get(snapshot, force); }
                    catch (Exception) { snap = null; }
                    PostToUi(delegate
                    {
                        _weatherBusy = false;
                        if (_disposed || _weather == null) return;
                        _weather.SetBusy(false);
                        if (snap != null) _weather.SetSnapshot(snap);
                        else _weather.SetError();
                    });
                });
            }
            catch (Exception)
            {
                _weatherBusy = false;
                _weather.SetBusy(false);
                _weather.SetError();
            }
        }

        // =====================================================================
        // 供内部卡片使用
        // =====================================================================

        internal Skin SkinNow { get { return _skin; } }
        internal Store StoreNow { get { return _store; } }
        internal AppSettings Cfg { get { return _store.Settings ?? (_store.Settings = new AppSettings()); } }
        internal List<DesktopItem> ItemsNow { get { return _items; } }

        internal Font FontOf(string key, float size, FontStyle style)
        {
            string k = key + "|" + size.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (int)style;
            Font f;
            if (_fonts.TryGetValue(k, out f)) return f;
            string family = FamilyOf(key);
            try { f = new Font(family, size, style); }
            catch (Exception) { f = new Font(FontFamily.GenericSansSerif, size, style); }
            _fonts[k] = f;
            return f;
        }

        private static string FamilyOf(string key)
        {
            switch (key)
            {
                case "digits": return "Segoe UI";
                case "mono": return "Consolas";
                case "emoji": return "Segoe UI Emoji";
                case "symbol": return "Segoe UI Symbol";
                default: return "Microsoft YaHei UI";
            }
        }

        internal void RaiseSettings()
        {
            EventHandler h = SettingsRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        internal void RaiseReveal()
        {
            EventHandler h = RevealRequested;
            if (h != null) h(this, EventArgs.Empty);
        }

        internal void ScrollBy(int delta)
        {
            if (_flow == null || !_flow.IsHandleCreated) return;
            try
            {
                int cur = -_flow.AutoScrollPosition.Y;
                int max = Math.Max(0, _flow.DisplayRectangle.Height - _flow.ClientSize.Height);
                int next = cur - delta;
                if (next < 0) next = 0;
                if (next > max) next = max;
                if (next != cur) _flow.AutoScrollPosition = new Point(0, next);
            }
            catch (Exception) { }
        }

        // ---------------- 任务操作 ----------------

        internal void AddTask(string title)
        {
            title = (title ?? "").Trim();
            if (title.Length == 0) return;
            TaskItem t = new TaskItem();
            t.Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            t.Title = title;
            t.Priority = "normal";
            t.Due = "";
            t.CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var scope = CurrentProject == null ? null : CurrentProject();
            if (scope != null) t.Project = scope.Id;
            if (_store.Tasks == null) _store.Tasks = new List<TaskItem>();
            _store.Tasks.Add(t);
            try { _store.SaveTasksOnly(); } catch (Exception) { }
            _store.Log("info", "task", "新增任务：" + title);
            RefreshTasks();
            RefreshLogs();
        }

        internal void ToggleTask(TaskItem t)
        {
            if (t == null) return;
            t.Done = !t.Done;
            t.DoneAt = t.Done ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : 0;
            try { _store.SaveTasksOnly(); } catch (Exception) { }
            _store.Log("info", "task", (t.Done ? "完成任务：" : "恢复任务：") + t.Title);
            RefreshTasks();
            RefreshLogs();
        }

        internal void CyclePriority(TaskItem t)
        {
            if (t == null) return;
            t.Priority = NextPriority(t.Priority);
            try { _store.SaveTasksOnly(); } catch (Exception) { }
            _store.Log("info", "task", "优先级 → " + PriorityCn(t.Priority) + "：" + t.Title);
            RefreshTasks();
            RefreshLogs();
        }

        internal void DeleteTask(TaskItem t)
        {
            if (t == null) return;
            if (_store.Tasks != null) _store.Tasks.RemoveAll(delegate(TaskItem x) { return x != null && x.Id == t.Id; });
            try { _store.SaveTasksOnly(); } catch (Exception) { }
            _store.Log("info", "task", "删除任务：" + t.Title);
            RefreshTasks();
            RefreshLogs();
        }

        internal void EditDue(TaskItem t)
        {
            if (t == null) return;
            using (DueDialog dlg = new DueDialog(_skin, t.Due))
            {
                Form owner = FindForm();
                DialogResult dr = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                if (dr != DialogResult.OK) return;
                t.Due = dlg.Cleared ? "" : dlg.DueText;
                try { _store.SaveTasksOnly(); } catch (Exception) { }
                _store.Log("info", "task", (t.Due.Length == 0 ? "清除截止日期：" : "截止 " + t.Due + "：") + t.Title);
                RefreshTasks();
                RefreshLogs();
            }
        }

        internal void EditCity()
        {
            using (CityDialog dlg = new CityDialog(_skin, Cfg.WeatherCity))
            {
                Form owner = FindForm();
                DialogResult dr = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
                if (dr != DialogResult.OK) return;
                Cfg.WeatherCity = dlg.CityName;
                Cfg.WeatherLat = 0; Cfg.HasCoords = false;
                Cfg.WeatherLon = 0;
                try { _store.SaveSettingsOnly(); } catch (Exception) { }
                _store.Log("info", "settings", "天气城市改为 " + (dlg.CityName.Length == 0 ? "（默认上海）" : dlg.CityName));
                RefreshLogs();
                RefreshWeather(true);
            }
        }

        internal void ClearLogs()
        {
            Form owner = FindForm();
            DialogResult dr = owner != null
                ? MessageBox.Show(owner, "确定清空全部日志？", "DeskBox", MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
                : MessageBox.Show("确定清空全部日志？", "DeskBox", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (dr != DialogResult.OK) return;
            try { _store.ClearLogs(); } catch (Exception) { }
            _store.Log("info", "system", "日志已清空");
            RefreshLogs();
        }

        // =====================================================================
        // 组装与布局
        // =====================================================================

        private void BuildUi()
        {
            _flow = new FlowLayoutPanel();
            _flow.FlowDirection = FlowDirection.TopDown;
            _flow.WrapContents = false;
            _flow.AutoScroll = true;
            _flow.AutoScrollMinSize = new Size(0, 0);
            _flow.Dock = DockStyle.Fill;
            _flow.BackColor = _skin.Bg;
            _flow.Padding = new Padding(12, 12, 12, 12);
            _flow.Margin = new Padding(0);
            _flow.TabStop = false;
            EnableDoubleBuffer(_flow);
            _flow.ClientSizeChanged += delegate { FitWidths(); };
            _flow.HandleCreated += delegate { ApplyWindowTheme(true); };
            Controls.Add(_flow);

            _clock = new ClockCard(this);
            _weather = new WeatherCard(this);
            _tasks = new TaskCard(this);
            _logs = new LogCard(this);
            _stats = new StatsCard(this);
            _context = new ContextCard(this) { Visible = false };

            _flow.Controls.Add(_context);
            _flow.Controls.Add(_clock);
            _flow.Controls.Add(_weather);
            _flow.Controls.Add(_tasks);
            _flow.Controls.Add(_logs);
            _flow.Controls.Add(_stats);

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private static void EnableDoubleBuffer(Control c)
        {
            try
            {
                PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (pi != null) pi.SetValue(c, true, null);
            }
            catch (Exception) { }
        }

        private void ApplyTheme()
        {
            _skin = Skin.For(Cfg.Theme, Cfg.Accent);
            BackColor = _skin.Bg;
            if (_flow != null) _flow.BackColor = _skin.Bg;
            ApplyWindowTheme(false);
            if (_clock != null) _clock.ApplySkin();
            if (_weather != null) _weather.ApplySkin();
            if (_tasks != null) _tasks.ApplySkin();
            if (_logs != null) _logs.ApplySkin();
            if (_stats != null) _stats.ApplySkin();
            if (_context != null) _context.ApplySkin();
            Invalidate(true);
        }

        private string _themeMode;

        /// <summary>让原生滚动条跟随主题（深色主题用 DarkMode_Explorer）。</summary>
        private void ApplyWindowTheme(bool force)
        {
            try
            {
                string mode = _skin != null && _skin.Light ? "Explorer" : "DarkMode_Explorer";
                if (!force && _themeMode == mode) return;
                _themeMode = mode;
                if (IsHandleCreated) SetWindowTheme(Handle, mode, null);
                if (_flow != null && _flow.IsHandleCreated)
                {
                    SetWindowTheme(_flow.Handle, mode, null);
                    // 强制非客户区（滚动条）重绘，否则切换主题后仍是旧颜色
                    SetWindowPos(_flow.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                    _flow.Invalidate(true);
                }
            }
            catch (Exception) { }
        }

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        /// <summary>卡片宽度跟随面板宽度（320px 下：卡片 279px）。</summary>
        private void FitWidths()
        {
            if (_disposed || _fitting || _flow == null) return;
            _fitting = true;
            try
            {
                int avail = _flow.ClientSize.Width - _flow.Padding.Horizontal;
                avail = Math.Max(160, avail);
                bool vs = _flow.VerticalScroll.Visible;
                if (vs != _lastVs)
                {
                    _lastVs = vs;
                    ApplyWindowTheme(true);
                }
                Card[] cards = new Card[] { _context, _clock, _weather, _tasks, _logs, _stats };
                for (int i = 0; i < cards.Length; i++)
                {
                    Card c = cards[i];
                    if (c != null && c.Width != avail) c.Width = avail;
                }
                for (int i = 0; i < cards.Length; i++)
                {
                    Card c = cards[i];
                    if (c != null) c.UpdateMetrics();
                }
            }
            finally { _fitting = false; }
        }

        private bool _lastVs;

        private void UpdateStats()
        {
            if (_stats == null) return;
            int total = _items.Count;
            int hidden = 0;
            for (int i = 0; i < _items.Count; i++) if (_items[i] != null && _items[i].Hidden) hidden++;
            int cats = _store.Categories != null ? _store.Categories.Count : 0;
            _stats.SetStats(total, hidden, cats);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_disposed) return;
            _ticks++;
            try { if (_clock != null && _clock.Visible) _clock.UpdateTime(); }
            catch (Exception) { }
            if (_ticks % 15 == 0)
            {
                try { if (_tasks != null && _tasks.Visible) RefreshTasks(); if (_logs != null && _logs.Visible) RefreshLogs(); }
                catch (Exception) { }
            }
            int weatherTicks = Math.Max(60, Math.Max(1, Cfg.WeatherRefreshMinutes) * 60);
            if (_weather != null && _weather.Visible && _ticks % weatherTicks == 0)
            {
                try { RefreshWeather(false); }
                catch (Exception) { }
            }
        }

        private void PostToUi(Action action)
        {
            if (action == null) return;
            try
            {
                if (_disposed || IsDisposed) return;
                if (!IsHandleCreated)
                {
                    lock (_pendingUi) _pendingUi.Enqueue(action);
                    return;
                }
                BeginInvoke(action);
            }
            catch (Exception) { }
        }

        private void DrainPending()
        {
            Action[] list;
            lock (_pendingUi) { list = _pendingUi.ToArray(); _pendingUi.Clear(); }
            for (int i = 0; i < list.Length; i++)
            {
                try { BeginInvoke(list[i]); }
                catch (Exception) { }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowTheme(true);
            if (_timer != null && !_timer.Enabled) _timer.Start();
            DrainPending();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            FitWidths();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                if (_timer != null)
                {
                    try { _timer.Stop(); _timer.Tick -= OnTick; _timer.Dispose(); }
                    catch (Exception) { }
                    _timer = null;
                }
                foreach (Font f in _fonts.Values)
                {
                    try { f.Dispose(); }
                    catch (Exception) { }
                }
                _fonts.Clear();
            }
            base.Dispose(disposing);
        }

        // =====================================================================
        // 卡片基类
        // =====================================================================

        private class Card : Panel
        {
            protected readonly WidgetsPanel Host;
            protected const int Pad = 14;
            protected string HoverKey;

            protected Card(WidgetsPanel host)
            {
                Host = host;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
                TabStop = false;
                Margin = new Padding(0, 0, 0, 10);
                BackColor = host.SkinNow.CardBg;
            }

            protected Skin S { get { return Host.SkinNow; } }
            protected int InnerW { get { return Math.Max(20, Width - Pad * 2); } }
            protected int InnerRight { get { return Width - Pad; } }

            public virtual void ApplySkin()
            {
                BackColor = S.CardBg;
                Invalidate();
            }

            public virtual void UpdateMetrics() { }

            protected virtual void PaintCard(Graphics g) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(S.Bg);

                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width > 8 && r.Height > 8)
                {
                    using (GraphicsPath p = RoundedRect(r, 12))
                    {
                        using (SolidBrush b = new SolidBrush(S.CardBg)) g.FillPath(b, p);
                        using (Pen pen = new Pen(S.Line, 1f)) g.DrawPath(pen, p);
                    }
                    if (!S.Light)
                    {
                        using (Pen hp = new Pen(Color.FromArgb(18, 255, 255, 255), 1f))
                            g.DrawLine(hp, Pad, 1, Width - Pad - 1, 1);
                    }
                }
                PaintCard(g);
            }

            protected void SetHover(string key)
            {
                if (HoverKey == key) return;
                HoverKey = key;
                Cursor = string.IsNullOrEmpty(key) ? Cursors.Default : Cursors.Hand;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                SetHover(null);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                Host.ScrollBy(e.Delta);
            }
        }

        // =====================================================================
        // 场景上下文卡
        // =====================================================================

        private sealed class ContextCard : Card
        {
            private string _title = "WORKSPACE CONTEXT";
            private string _subtitle = "";
            private string[] _lines = new string[0];

            public ContextCard(WidgetsPanel host) : base(host) { }

            public void SetContent(string title, string subtitle, string[] lines)
            {
                _title = string.IsNullOrEmpty(title) ? "WORKSPACE CONTEXT" : title;
                _subtitle = subtitle ?? "";
                _lines = lines ?? new string[0];
                UpdateMetrics(); Invalidate();
            }

            public override void UpdateMetrics()
            {
                int h = 82 + _lines.Length * 24;
                if (Height != h) Height = h;
            }

            protected override void PaintCard(Graphics g)
            {
                TextAt(g, _title, Host.FontOf("ui", 10.5f, FontStyle.Bold), Pad, 14, S.AccentText);
                TextIn(g, _subtitle, Host.FontOf("ui", 8.5f, FontStyle.Regular), new Rectangle(Pad, 39, InnerW, 34), S.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                int y = 78;
                foreach (var line in _lines)
                {
                    TextIn(g, line, Host.FontOf("mono", 9f, FontStyle.Regular), new Rectangle(Pad, y, InnerW, 20), S.Text,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    y += 24;
                }
            }
        }

        // =====================================================================
        // 1. 时钟卡
        // =====================================================================

        private sealed class ClockCard : Card
        {
            private string _digits = "00:00";
            private string _suffix = "";
            private string _dateText = "";
            private string _tzText = "";
            private float _timeSize = 36f;
            private int _timeH = 56;
            private Rectangle _gearRect;
            private const int GearTop = 8;
            private const int GearSize = 18;
            private const int TimeTop = GearTop + GearSize + 2;

            public ClockCard(WidgetsPanel host) : base(host)
            {
                UpdateTime();
            }

            public void UpdateTime()
            {
                DateTime now = DateTime.Now;
                AppSettings cfg = Host.Cfg;
                bool seconds = cfg.ShowSeconds;
                if (cfg.Clock24)
                {
                    _digits = now.ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
                    _suffix = "";
                }
                else
                {
                    _digits = now.ToString(seconds ? "h:mm:ss" : "h:mm", CultureInfo.InvariantCulture);
                    _suffix = now.ToString("tt", CultureInfo.CurrentCulture);
                }
                _dateText = now.Year + "年" + now.Month + "月" + now.Day + "日 " + WeekCn(now.DayOfWeek);
                _tzText = ShortTimeZone(TimeZoneInfo.Local.DisplayName);
                Invalidate();
            }

            private void FitTimeFont()
            {
                string sample = Host.Cfg.ShowSeconds ? "00:00:00" : "00:00";
                // 12 小时制要给 AM/PM 留位；24 小时制留白极小，确保字号落在 36~40pt
                int reserve = Host.Cfg.Clock24 ? 6 : 40;
                int avail = InnerW - reserve;
                float chosen = 22f;
                for (float size = 40f; size >= 22f; size -= 1f)
                {
                    Font f = Host.FontOf("mono", size, FontStyle.Regular);
                    chosen = size;
                    if (TextW(sample, f) <= avail) break;
                }
                _timeSize = chosen;
            }

            public override void UpdateMetrics()
            {
                FitTimeFont();
                _timeH = TextH("0", Host.FontOf("mono", _timeSize, FontStyle.Regular));
                int h = TimeTop + _timeH + 2 + 20 + 18 + 10;
                if (h < 112) h = 112;
                if (Height != h) Height = h;
            }

            protected override void PaintCard(Graphics g)
            {
                int gearX = InnerRight - GearSize;
                _gearRect = new Rectangle(gearX, GearTop, GearSize, GearSize);
                if (HoverKey == "gear") FillRounded(g, S.Hover, _gearRect, 9);
                TextIn(g, "⚙", Host.FontOf("symbol", 10.5f, FontStyle.Regular), _gearRect,
                    HoverKey == "gear" ? S.AccentText : S.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // ---- 时间 ----
                Font tf = Host.FontOf("mono", _timeSize, FontStyle.Regular);
                bool seconds = Host.Cfg.ShowSeconds && _digits.Length > 2;
                string prefix = seconds ? _digits.Substring(0, _digits.Length - 2) : _digits;
                string secPart = seconds ? _digits.Substring(_digits.Length - 2) : "";

                int prefixW = TextW(prefix, tf);
                int secW = secPart.Length > 0 ? TextW(secPart, tf) : 0;

                Font sf = Host.FontOf("ui", 9f, FontStyle.Bold);
                int suffixW = _suffix.Length > 0 ? TextW(_suffix, sf) + 6 : 0;
                int suffixH = _suffix.Length > 0 ? TextH(_suffix, sf) : 0;

                int totalW = prefixW + secW + suffixW;
                int x = Pad + Math.Max(0, (InnerW - totalW) / 2);

                TextAt(g, prefix, tf, x, TimeTop, S.Text);
                if (secW > 0) TextAt(g, secPart, tf, x + prefixW, TimeTop, S.AccentText);
                if (suffixW > 0)
                    TextAt(g, _suffix, sf, x + prefixW + secW + 6, TimeTop + _timeH - suffixH - 9, S.TextDim);

                // ---- 日期 ----
                int dateY = TimeTop + _timeH + 2;
                TextIn(g, _dateText, Host.FontOf("ui", 10.5f, FontStyle.Regular),
                    new Rectangle(Pad, dateY, InnerW, 20), S.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // ---- 时区 ----
                TextIn(g, _tzText, Host.FontOf("ui", 8.5f, FontStyle.Regular),
                    new Rectangle(Pad, dateY + 20, InnerW, 18), S.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            private string HitTest(Point p)
            {
                return _gearRect.Contains(p) ? "gear" : null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(HitTest(e.Location));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (HitTest(e.Location) == "gear") Host.RaiseSettings();
            }
        }

        // =====================================================================
        // 2. 天气卡
        // =====================================================================

        private sealed class WeatherCard : Card
        {
            private WeatherSnapshot _snap;
            private bool _busy;
            private bool _error;
            private Rectangle _refreshRect;
            private Rectangle _locateRect;
            private Rectangle _pillRect;

            private const int HeaderY = 12;
            private const int HeaderH = 22;
            private const int MainY = 40;
            private const int MainH = 46;
            private const int SubY = 86;
            private const int SubH = 18;
            private const int DividerY = 108;
            private const int ForecastY = 114;
            private const int CardH = 172;

            public WeatherCard(WidgetsPanel host) : base(host) { }

            public void SetSnapshot(WeatherSnapshot s)
            {
                _snap = s;
                _error = false;
                Invalidate();
            }

            public void SetBusy(bool busy)
            {
                _busy = busy;
                Invalidate();
            }

            public void SetError()
            {
                _error = true;
                Invalidate();
            }

            public override void UpdateMetrics()
            {
                if (Height != CardH) Height = CardH;
            }

            private string CityText()
            {
                if (_snap == null) return "天气";
                string city = string.IsNullOrEmpty(_snap.City) ? "本地" : _snap.City;
                string region = _snap.Region ?? "";
                if (region.Length > 0 && !string.Equals(region, city, StringComparison.Ordinal))
                    return city + " · " + region;
                return city;
            }

            private string PillText(out Color color)
            {
                if (_busy) { color = S.TextDim; return "更新中"; }
                if (_error) { color = S.Danger; return "获取失败"; }
                if (_snap == null) { color = S.TextDim; return "加载中"; }
                if (_snap.Source == "online") { color = S.Ok; return "实时"; }
                if (_snap.Source == "cache")
                {
                    color = S.Warn;
                    string hm = HHmm(_snap.UpdatedAt);
                    return hm.Length > 0 ? "缓存 · " + hm : "缓存";
                }
                color = S.Warn;
                return "离线示意";
            }

            protected override void PaintCard(Graphics g)
            {
                int right = InnerRight;
                _refreshRect = new Rectangle(right - 22, 11, 22, 22);
                _locateRect = new Rectangle(right - 22 - 4 - 22, 11, 22, 22);

                // ---- 头部按钮 ----
                if (HoverKey == "refresh") FillRounded(g, S.Hover, _refreshRect, 8);
                if (HoverKey == "locate") FillRounded(g, S.Hover, _locateRect, 8);
                TextIn(g, "⟳", Host.FontOf("symbol", 12f, FontStyle.Regular), _refreshRect,
                    _busy ? S.TextFaint : (HoverKey == "refresh" ? S.AccentText : S.TextDim),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextIn(g, "📍", Host.FontOf("emoji", 10f, FontStyle.Regular), _locateRect,
                    HoverKey == "locate" ? S.AccentText : S.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // ---- 来源标签 ----
                Color pillColor;
                string pill = PillText(out pillColor);
                Font pf = Host.FontOf("ui", 8f, FontStyle.Regular);
                int pw = Math.Max(30, TextW(pill, pf) + 14);
                _pillRect = new Rectangle(_locateRect.Left - 8 - pw, 14, pw, 18);
                FillRounded(g, Mix(pillColor, S.CardBg, 0.82), _pillRect, 9);
                TextIn(g, pill, pf, _pillRect, pillColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // ---- 城市 ----
                int cityW = Math.Max(20, _pillRect.Left - 8 - Pad);
                TextIn(g, CityText(), Host.FontOf("ui", 10f, FontStyle.Bold),
                    new Rectangle(Pad, HeaderY, cityW, HeaderH), S.Text,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // ---- 主体：温度 + emoji + 文案 ----
                Font tf = Host.FontOf("digits", 30f, FontStyle.Bold);
                string temp = (_snap == null) ? "--" : Temperature(_snap.Temp);
                int tw = TextW(temp, tf);
                int th = TextH("21°", tf);
                int ty = MainY + (MainH - th) / 2 - 3;
                TextAt(g, temp, tf, Pad, ty, S.Text);

                Font ef = Host.FontOf("emoji", 18f, FontStyle.Regular);
                string icon = (_snap == null || string.IsNullOrEmpty(_snap.Icon)) ? "🌡" : _snap.Icon;
                int ex = Pad + tw + 10;
                int ew = TextW(icon, ef);
                TextAt(g, icon, ef, ex, MainY + 5, S.Text);

                Font wf = Host.FontOf("ui", 11f, FontStyle.Regular);
                string desc = _error ? "暂时无法获取天气" : (_snap == null ? "正在获取…" : (_snap.Text ?? ""));
                int dx = ex + ew + 10;
                TextIn(g, desc, wf, new Rectangle(dx, MainY, Math.Max(10, right - dx), MainH), S.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // ---- 第二行：体感 / 湿度 / 风 ----
                TextIn(g, SubText(), Host.FontOf("ui", 8.5f, FontStyle.Regular),
                    new Rectangle(Pad, SubY, InnerW, SubH), S.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                // ---- 分隔线 ----
                using (Pen pen = new Pen(S.Line, 1f)) g.DrawLine(pen, Pad, DividerY, right, DividerY);

                // ---- 5 日预报 ----
                PaintForecast(g, right);
            }

            private string SubText()
            {
                if (_error) return "请检查网络或稍后重试";
                if (_snap == null) return "体感 — · 湿度 — · 风 —";
                string feels = "体感 " + Temperature(_snap.Feels);
                string hum = "湿度 " + _snap.Humidity.ToString(CultureInfo.InvariantCulture) + "%";
                string wind;
                string dir = _snap.WindDir ?? "";
                string spd = _snap.WindSpeed.ToString("0.0", CultureInfo.InvariantCulture);
                if (dir.Length > 0) wind = dir + "风 " + spd + "m/s";
                else wind = "风速 " + spd + "m/s";
                return feels + " · " + hum + " · " + wind;
            }

            private void PaintForecast(Graphics g, int right)
            {
                int cols = 5;
                int cw = InnerW / cols;
                if (cw < 24) return;
                Font wdF = Host.FontOf("ui", 8f, FontStyle.Regular);
                Font emF = Host.FontOf("emoji", 13f, FontStyle.Regular);
                Font tpF = Host.FontOf("ui", 8.5f, FontStyle.Regular);

                for (int i = 0; i < cols; i++)
                {
                    int x = Pad + i * cw;
                    WeatherDay d = (_snap != null && _snap.Days != null && i < _snap.Days.Count) ? _snap.Days[i] : null;

                    string wd = d != null ? d.Weekday : "—";
                    if (i == 0 && d == null) wd = "—";
                    TextIn(g, wd, wdF, new Rectangle(x, ForecastY, cw, 14), S.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    string em = d != null && !string.IsNullOrEmpty(d.Icon) ? d.Icon : "·";
                    TextIn(g, em, emF, new Rectangle(x, ForecastY + 15, cw, 19), S.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    string hi = d != null ? Temperature(d.TMax) : "—";
                    string lo = d != null ? Temperature(d.TMin) : "";
                    int hiW = TextW(hi, tpF);
                    int loW = lo.Length > 0 ? TextW(lo, tpF) : 0;
                    int gap = lo.Length > 0 ? 3 : 0;
                    int totalW = hiW + gap + loW;
                    int tx = x + Math.Max(0, (cw - totalW) / 2);
                    TextAt(g, hi, tpF, tx, ForecastY + 35, S.Text);
                    if (lo.Length > 0) TextAt(g, lo, tpF, tx + hiW + gap, ForecastY + 35, S.TextFaint);
                }
            }

            private string Temperature(double celsius)
            {
                if (string.Equals(Host._store.Settings.TempUnit, "F", StringComparison.OrdinalIgnoreCase))
                    return ((int)Math.Round(celsius * 9.0 / 5.0 + 32.0)).ToString(CultureInfo.InvariantCulture) + "°F";
                return ((int)Math.Round(celsius)).ToString(CultureInfo.InvariantCulture) + "°C";
            }

            private string HitTest(Point p)
            {
                if (_refreshRect.Contains(p)) return "refresh";
                if (_locateRect.Contains(p)) return "locate";
                return null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(HitTest(e.Location));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                string hit = HitTest(e.Location);
                if (hit == "refresh")
                {
                    if (!_busy) Host.RefreshWeather(true);
                }
                else if (hit == "locate")
                {
                    Host.EditCity();
                }
            }
        }

        // =====================================================================
        // 3. 任务卡
        // =====================================================================

        private sealed class TaskCard : Card
        {
            private readonly InputShell _shell;
            private readonly GlyphButton _add;
            private readonly TaskList _list;
            private readonly FilterBar _filter;
            private string _mode = "todo";
            private int _pending;
            private int _footerY = 140;

            public TaskCard(WidgetsPanel host) : base(host)
            {
                _list = new TaskList(host);
                _list.TaskAction += delegate(TaskItem t, string zone)
                {
                    if (zone == "toggle") Host.ToggleTask(t);
                    else if (zone == "priority") Host.CyclePriority(t);
                    else if (zone == "due") Host.EditDue(t);
                    else if (zone == "delete") Host.DeleteTask(t);
                };
                Controls.Add(_list);

                _shell = new InputShell(host);
                _shell.Submitted += delegate
                {
                    string text = _shell.TextValue;
                    if (text.Trim().Length == 0) return;
                    Host.AddTask(text);
                    _shell.TextValue = "";
                    _shell.FocusBox();
                };
                Controls.Add(_shell);

                _add = new GlyphButton(host);
                _add.Pressed += delegate
                {
                    string text = _shell.TextValue;
                    if (text.Trim().Length == 0) { _shell.FocusBox(); return; }
                    Host.AddTask(text);
                    _shell.TextValue = "";
                    _shell.FocusBox();
                };
                Controls.Add(_add);

                _filter = new FilterBar(host);
                _filter.Selected = _mode;
                _filter.Changed += delegate
                {
                    _mode = _filter.Selected;
                    Reload();
                };
                Controls.Add(_filter);
            }

            public override void ApplySkin()
            {
                base.ApplySkin();
                _shell.ApplySkin();
                _add.ApplySkin();
                _list.Invalidate();
                _filter.Invalidate();
            }

            public void Reload()
            {
                var project = Host.CurrentProject == null ? null : Host.CurrentProject();
                List<TaskItem> all = project == null ? Host.StoreNow.Tasks : Host.StoreNow.Tasks.Where(project.IncludesTask).ToList();
                if (all == null) all = new List<TaskItem>();
                List<TaskItem> shown = new List<TaskItem>();
                _pending = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    TaskItem t = all[i];
                    if (t == null) continue;
                    if (!t.Done) _pending++;
                    if (_mode == "todo" && t.Done) continue;
                    if (_mode == "done" && !t.Done) continue;
                    shown.Add(t);
                }
                shown.Sort(delegate(TaskItem a, TaskItem b) { return a.CreatedAt.CompareTo(b.CreatedAt); });
                _list.SetItems(shown);
                UpdateMetrics();
                LayoutInner();
                Invalidate();
            }

            public override void UpdateMetrics()
            {
                int listH = _list.PreferredHeight;
                int h = 12 + 20 + 6 + 32 + 8 + listH + 8 + 24 + 14;
                if (Height != h) Height = h;
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutInner();
            }

            private void LayoutInner()
            {
                if (_shell == null || Width <= 0) return;
                int pad = Pad;
                int innerW = Width - pad * 2;
                if (innerW < 60) return;

                int y = 12 + 20 + 6;
                _shell.SetBounds(pad, y, innerW - 42, 32);
                _add.SetBounds(pad + innerW - 36, y, 36, 32);

                int listTop = y + 32 + 8;
                int listH = _list.PreferredHeight;
                _list.SetBounds(pad, listTop, innerW, listH);

                _footerY = listTop + listH + 8;
                _filter.SetBounds(pad + innerW - 132, _footerY, 132, 24);
            }

            protected override void PaintCard(Graphics g)
            {
                TextAt(g, "待办", Host.FontOf("ui", 10.5f, FontStyle.Bold), Pad, 12, S.Text);

                using (Pen pen = new Pen(S.Line, 1f))
                    g.DrawLine(pen, Pad, _footerY - 4, InnerRight, _footerY - 4);

                TextIn(g, "未完成 " + _pending.ToString(CultureInfo.InvariantCulture) + " 项",
                    Host.FontOf("ui", 8.5f, FontStyle.Regular),
                    new Rectangle(Pad, _footerY, Math.Max(20, InnerW - 140), 24), S.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }

        // ---------------- 任务输入框 ----------------

        private sealed class InputShell : Panel
        {
            private readonly WidgetsPanel _host;
            private readonly TextBox _box;
            private readonly Label _hint;
            private bool _focused;

            public event EventHandler Submitted;

            public InputShell(WidgetsPanel host)
            {
                _host = host;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = host.SkinNow.InputBg;

                _box = new TextBox();
                _box.BorderStyle = BorderStyle.None;
                _box.Font = host.FontOf("ui", 9.5f, FontStyle.Regular);
                _box.BackColor = host.SkinNow.InputBg;
                _box.ForeColor = host.SkinNow.Text;
                _box.KeyDown += OnBoxKeyDown;
                _box.TextChanged += delegate { UpdateHint(); };
                _box.GotFocus += delegate { _focused = true; UpdateHint(); Invalidate(); };
                _box.LostFocus += delegate { _focused = false; UpdateHint(); Invalidate(); };
                Controls.Add(_box);

                _hint = new Label();
                _hint.AutoSize = false;
                _hint.Text = "添加任务，回车确认…";
                _hint.Font = host.FontOf("ui", 9f, FontStyle.Regular);
                _hint.ForeColor = host.SkinNow.TextFaint;
                _hint.BackColor = host.SkinNow.InputBg;
                _hint.TextAlign = ContentAlignment.MiddleLeft;
                _hint.Cursor = Cursors.IBeam;
                _hint.Click += delegate { FocusBox(); };
                Controls.Add(_hint);
                _hint.BringToFront();
            }

            public string TextValue
            {
                get { return _box.Text; }
                set { _box.Text = value ?? ""; UpdateHint(); }
            }

            public void FocusBox()
            {
                try { _box.Focus(); }
                catch (Exception) { }
            }

            public void ApplySkin()
            {
                Skin s = _host.SkinNow;
                BackColor = s.InputBg;
                _box.BackColor = s.InputBg;
                _box.ForeColor = s.Text;
                _hint.BackColor = s.InputBg;
                _hint.ForeColor = s.TextFaint;
                Invalidate();
            }

            private void UpdateHint()
            {
                bool show = _box.Text.Length == 0 && !_focused;
                if (_hint.Visible != show) _hint.Visible = show;
            }

            private void OnBoxKeyDown(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    EventHandler h = Submitted;
                    if (h != null) h(this, EventArgs.Empty);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _box.Text = "";
                    e.Handled = true;
                }
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (_box == null) return;
                int bh = _box.PreferredHeight;
                int y = Math.Max(2, (Height - bh) / 2);
                _box.SetBounds(12, y, Math.Max(20, Width - 24), bh);
                if (_hint != null) _hint.SetBounds(13, 0, Math.Max(20, Width - 26), Height);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Skin s = _host.SkinNow;
                g.Clear(Parent != null ? Parent.BackColor : s.CardBg);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 2 || r.Height <= 2) return;
                FillRounded(g, s.InputBg, r, 10);
                DrawRounded(g, _focused ? Mix(s.Accent, s.CardBg, 0.45) : s.Line, r, 10, 1f);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                FocusBox();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _host.ScrollBy(e.Delta);
            }
        }

        // ---------------- 加号按钮 ----------------

        private sealed class GlyphButton : Control
        {
            private readonly WidgetsPanel _host;
            private bool _hover;
            private bool _down;

            public event EventHandler Pressed;

            public GlyphButton(WidgetsPanel host)
            {
                _host = host;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            public void ApplySkin() { Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Skin s = _host.SkinNow;
                g.Clear(Parent != null ? Parent.BackColor : s.CardBg);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                if (r.Width <= 2 || r.Height <= 2) return;

                Color bg = s.AccentSoft;
                if (_down) bg = Mix(s.Accent, s.CardBg, 0.55);
                else if (_hover) bg = Mix(s.Accent, s.CardBg, 0.68);
                FillRounded(g, bg, r, 10);

                TextIn(g, "＋", _host.FontOf("ui", 13f, FontStyle.Bold), r,
                    _hover || _down ? s.AccentText : Mix(s.AccentText, s.CardBg, 0.15),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); }
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                bool fire = _down && ClientRectangle.Contains(e.Location);
                _down = false;
                Invalidate();
                if (fire)
                {
                    EventHandler h = Pressed;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _host.ScrollBy(e.Delta);
            }
        }

        // ---------------- 筛选器 ----------------

        private sealed class FilterBar : Control
        {
            private readonly WidgetsPanel _host;
            private static readonly string[] Keys = new string[] { "all", "todo", "done" };
            private static readonly string[] Labels = new string[] { "全部", "未完成", "已完成" };
            private string _selected = "todo";
            private int _hoverIndex = -1;

            public event EventHandler Changed;

            public FilterBar(WidgetsPanel host)
            {
                _host = host;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                TabStop = false;
            }

            public string Selected
            {
                get { return _selected; }
                set
                {
                    if (value == "all" || value == "todo" || value == "done") _selected = value;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Skin s = _host.SkinNow;
                g.Clear(Parent != null ? Parent.BackColor : s.CardBg);
                Rectangle all = new Rectangle(0, 0, Width - 1, Height - 1);
                if (all.Width <= 4 || all.Height <= 4) return;
                FillRounded(g, s.InputBg, all, 8);

                int seg = Width / 3;
                Font f = _host.FontOf("ui", 8f, FontStyle.Regular);
                for (int i = 0; i < 3; i++)
                {
                    Rectangle r = new Rectangle(i * seg + 1, 1, seg - 2, Height - 3);
                    bool sel = Keys[i] == _selected;
                    if (sel) FillRounded(g, s.AccentSoft, r, 7);
                    else if (_hoverIndex == i) FillRounded(g, s.Hover, r, 7);
                    TextIn(g, Labels[i], f, r,
                        sel ? s.AccentText : s.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            private int IndexAt(Point p)
            {
                int seg = Math.Max(1, Width / 3);
                int i = p.X / seg;
                if (i < 0 || i > 2) return -1;
                return i;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int i = IndexAt(e.Location);
                if (i != _hoverIndex) { _hoverIndex = i; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hoverIndex != -1) { _hoverIndex = -1; Invalidate(); }
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                int i = IndexAt(e.Location);
                if (i < 0) return;
                if (Keys[i] == _selected) return;
                _selected = Keys[i];
                Invalidate();
                EventHandler h = Changed;
                if (h != null) h(this, EventArgs.Empty);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _host.ScrollBy(e.Delta);
            }
        }

        // ---------------- 任务列表 ----------------

        private sealed class TaskList : Control
        {
            private readonly WidgetsPanel _host;
            private List<TaskItem> _items = new List<TaskItem>();
            private int _hoverRow = -1;
            private string _hoverZone;

            public event Action<TaskItem, string> TaskAction;

            private const int RowStride = 34;
            private const int RowH = 32;

            public TaskList(WidgetsPanel host)
            {
                _host = host;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }

            public void SetItems(List<TaskItem> items)
            {
                _items = items ?? new List<TaskItem>();
                _hoverRow = -1;
                _hoverZone = null;
                Invalidate();
            }

            public int PreferredHeight
            {
                get { return _items.Count == 0 ? 30 : _items.Count * RowStride - 2; }
            }

            private Rectangle RowRect(int i)
            {
                return new Rectangle(0, i * RowStride, Width, RowH);
            }

            private static Rectangle CircleRect(Rectangle row)
            {
                return new Rectangle(4, row.Y + (row.Height - 18) / 2, 18, 18);
            }

            private static Rectangle DelRect(Rectangle row)
            {
                return new Rectangle(row.Width - 18, row.Y + (row.Height - 16) / 2, 16, 16);
            }

            private static Rectangle DueRect(Rectangle row)
            {
                return new Rectangle(DelRect(row).Left - 6 - 50, row.Y + 8, 50, 16);
            }

            private static Rectangle DotRect(Rectangle row)
            {
                return new Rectangle(DueRect(row).Left - 13, row.Y + (row.Height - 9) / 2, 9, 9);
            }

            private static Rectangle TitleRect(Rectangle row)
            {
                int x = CircleRect(row).Right + 8;
                int w = DotRect(row).Left - x - 4;
                return new Rectangle(x, row.Y + 6, Math.Max(10, w), 20);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Skin s = _host.SkinNow;
                g.Clear(Parent != null ? Parent.BackColor : s.CardBg);

                if (_items.Count == 0)
                {
                    TextIn(g, "暂无任务，先加一个吧", _host.FontOf("ui", 9f, FontStyle.Regular),
                        new Rectangle(4, 0, Math.Max(10, Width - 8), 30), s.TextFaint,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                Font tf = _host.FontOf("ui", 9.5f, FontStyle.Regular);
                Font td = _host.FontOf("ui", 9.5f, FontStyle.Strikeout);
                Font df = _host.FontOf("ui", 8.5f, FontStyle.Regular);

                for (int i = 0; i < _items.Count; i++)
                {
                    TaskItem t = _items[i];
                    if (t == null) continue;
                    Rectangle row = RowRect(i);
                    if (row.Bottom > Height + 2) break;
                    bool hoverRow = _hoverRow == i;

                    if (hoverRow) FillRounded(g, s.Hover, row, 8);

                    // 圆形勾选
                    Rectangle cr = CircleRect(row);
                    if (t.Done)
                    {
                        using (SolidBrush b = new SolidBrush(s.AccentText))
                            g.FillEllipse(b, cr);
                        TextIn(g, "✓", _host.FontOf("symbol", 9f, FontStyle.Bold),
                            new Rectangle(cr.X, cr.Y - 1, cr.Width, cr.Height + 1),
                            s.Light ? Color.White : s.CardBg,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    else
                    {
                        Color ring = (_hoverZone == "toggle" && hoverRow) ? s.AccentText : Mix(s.TextDim, s.CardBg, 0.35);
                        using (Pen pen = new Pen(ring, 1.4f))
                            g.DrawEllipse(pen, cr.X + 1, cr.Y + 1, cr.Width - 2, cr.Height - 2);
                    }

                    // 标题
                    TextIn(g, t.Title, t.Done ? td : tf, TitleRect(row),
                        t.Done ? s.TextFaint : s.Text,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                    // 优先级色点
                    Rectangle dr = DotRect(row);
                    Color pc = PriorityColor(t.Priority);
                    using (SolidBrush b = new SolidBrush(pc))
                        g.FillEllipse(b, dr);
                    if (hoverRow && _hoverZone == "priority")
                        using (Pen pen = new Pen(Mix(pc, Color.White, 0.45), 1f))
                            g.DrawEllipse(pen, dr.X - 2, dr.Y - 2, dr.Width + 4, dr.Height + 4);

                    // 截止日期
                    Rectangle due = DueRect(row);
                    string dueText;
                    Color dueColor;
                    DueInfo(t, out dueText, out dueColor);
                    TextIn(g, dueText, df, due, dueColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    // 删除
                    TextIn(g, "×", _host.FontOf("ui", 12f, FontStyle.Regular),
                        new Rectangle(DelRect(row).X, DelRect(row).Y - 3, DelRect(row).Width, DelRect(row).Height + 4),
                        (hoverRow && _hoverZone == "delete") ? s.Danger : s.TextFaint,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            private void DueInfo(TaskItem t, out string text, out Color color)
            {
                Skin s = _host.SkinNow;
                if (string.IsNullOrEmpty(t.Due))
                {
                    text = "＋日期";
                    color = (_hoverRow >= 0 && _hoverZone == "due") ? s.TextDim : s.TextFaint;
                    return;
                }
                DateTime dt;
                if (!DateTime.TryParse(t.Due, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                {
                    text = t.Due;
                    color = s.TextDim;
                    return;
                }
                DateTime today = DateTime.Today;
                if (dt.Date == today) text = "今天";
                else if (dt.Date == today.AddDays(1)) text = "明天";
                else text = dt.ToString("M-d", CultureInfo.InvariantCulture);

                if (!t.Done && dt.Date < today) color = s.Danger;
                else if (!t.Done && dt.Date == today) color = s.AccentText;
                else color = s.TextDim;
            }

            private string ZoneAt(Point p, out int index)
            {
                index = -1;
                if (_items.Count == 0) return null;
                int i = p.Y / RowStride;
                if (i < 0 || i >= _items.Count) return null;
                Rectangle row = RowRect(i);
                if (!row.Contains(p)) return null;
                index = i;
                if (CircleRect(row).Contains(p)) return "toggle";
                if (DelRect(row).Contains(p)) return "delete";
                if (DotRect(row).Contains(p)) return "priority";
                if (DueRect(row).Contains(p)) return "due";
                return "row";
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int idx;
                string zone = ZoneAt(e.Location, out idx);
                if (idx != _hoverRow || zone != _hoverZone)
                {
                    _hoverRow = idx;
                    _hoverZone = zone;
                    Cursor = (zone == null || zone == "row") ? Cursors.Default : Cursors.Hand;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hoverRow != -1 || _hoverZone != null)
                {
                    _hoverRow = -1;
                    _hoverZone = null;
                    Cursor = Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                int idx;
                string zone = ZoneAt(e.Location, out idx);
                if (idx < 0 || zone == null || zone == "row") return;
                TaskItem t = _items[idx];
                Action<TaskItem, string> h = TaskAction;
                if (h != null) h(t, zone);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _host.ScrollBy(e.Delta);
            }
        }

        // =====================================================================
        // 4. 日志卡
        // =====================================================================

        private sealed class LogCard : Card
        {
            private List<LogEntry> _logs = new List<LogEntry>();
            private Rectangle _clearRect;

            public LogCard(WidgetsPanel host) : base(host) { }

            public void SetLogs(List<LogEntry> logs)
            {
                _logs = logs ?? new List<LogEntry>();
                UpdateMetrics();
                Invalidate();
            }

            public override void UpdateMetrics()
            {
                int rows = Math.Max(1, Math.Min(30, _logs.Count));
                int h = 40 + rows * 19 + 12;
                if (Height != h) Height = h;
            }

            protected override void PaintCard(Graphics g)
            {
                TextAt(g, "日志", Host.FontOf("ui", 10.5f, FontStyle.Bold), Pad, 12, S.Text);

                _clearRect = new Rectangle(InnerRight - 36, 12, 36, 20);
                if (HoverKey == "clear") FillRounded(g, S.Hover, _clearRect, 8);
                TextIn(g, "清空", Host.FontOf("ui", 8.5f, FontStyle.Regular), _clearRect,
                    HoverKey == "clear" ? S.AccentText : S.TextFaint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (_logs.Count == 0)
                {
                    TextIn(g, "暂无日志", Host.FontOf("ui", 8.5f, FontStyle.Regular),
                        new Rectangle(Pad, 40, InnerW, 19), S.TextFaint,
                        TextFormatFlags.VerticalCenter);
                    return;
                }

                Font tmF = Host.FontOf("mono", 8f, FontStyle.Regular);
                Font msgF = Host.FontOf("ui", 8.5f, FontStyle.Regular);
                int y = 40;
                for (int i = 0; i < _logs.Count && i < 30; i++)
                {
                    LogEntry e = _logs[i];
                    if (e == null) { y += 19; continue; }
                    TextAt(g, HHmm(e.Ts), tmF, Pad, y + 2, S.TextFaint);
                    TextIn(g, LogIcon(e.Action), Host.FontOf(LogIconFamily(e.Action), 9f, FontStyle.Regular),
                        new Rectangle(Pad + 36, y, 16, 18), S.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                    Color mc = S.Text;
                    if (string.Equals(e.Level, "error", StringComparison.OrdinalIgnoreCase)) mc = S.Danger;
                    else if (string.Equals(e.Level, "warn", StringComparison.OrdinalIgnoreCase)) mc = S.Warn;
                    TextIn(g, e.Message, msgF, new Rectangle(Pad + 56, y, Math.Max(10, InnerW - 56), 18), mc,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    y += 19;
                }
            }

            private string HitTest(Point p)
            {
                return _clearRect.Contains(p) ? "clear" : null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(HitTest(e.Location));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                if (HitTest(e.Location) == "clear") Host.ClearLogs();
            }
        }

        // =====================================================================
        // 5. 统计卡
        // =====================================================================

        private sealed class StatsCard : Card
        {
            private int _total;
            private int _hidden;
            private int _cats;

            public StatsCard(WidgetsPanel host) : base(host) { }

            public void SetStats(int total, int hidden, int cats)
            {
                _total = total;
                _hidden = hidden;
                _cats = cats;
                Invalidate();
            }

            public override void UpdateMetrics()
            {
                if (Height != 84) Height = 84;
            }

            protected override void PaintCard(Graphics g)
            {
                int cw = InnerW / 3;
                if (cw < 20) return;
                int[] vals = new int[] { _total, _hidden, _cats };
                string[] caps = new string[] { "总条目", "已隐藏", "分类" };
                Font nf = Host.FontOf("digits", 19f, FontStyle.Bold);
                Font cf = Host.FontOf("ui", 8.5f, FontStyle.Regular);

                for (int i = 0; i < 3; i++)
                {
                    int x = Pad + i * cw;
                    if (i > 0)
                        using (Pen pen = new Pen(S.Line, 1f))
                            g.DrawLine(pen, x, 20, x, 58);

                    Color nc = (i == 1 && vals[i] > 0) ? S.AccentText : S.Text;
                    if (i == 0 && HoverKey == "total") nc = S.AccentText;
                    TextIn(g, vals[i].ToString(CultureInfo.InvariantCulture), nf,
                        new Rectangle(x, 14, cw, 28), nc,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    TextIn(g, caps[i], cf, new Rectangle(x, 44, cw, 16), S.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }

            private Rectangle TotalRect()
            {
                return new Rectangle(Pad, 0, Math.Max(10, InnerW / 3), Height);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                SetHover(TotalRect().Contains(e.Location) ? "total" : null);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button == MouseButtons.Left && TotalRect().Contains(e.Location)) Host.RaiseReveal();
            }
        }

        // =====================================================================
        // 弹窗：改城市 / 改截止日期
        // =====================================================================

        private sealed class CityDialog : Form
        {
            private readonly TextBox _box;

            public string CityName { get { return (_box.Text ?? "").Trim(); } }

            public CityDialog(Skin skin, string current)
            {
                Text = "天气城市";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                BackColor = skin.CardBg;
                ForeColor = skin.Text;
                Font = new Font("Microsoft YaHei UI", 9f);
                ClientSize = new Size(300, 132);

                Label lab = new Label();
                lab.Text = "城市名（中文或拼音，留空用默认）";
                lab.AutoSize = false;
                lab.SetBounds(16, 14, 268, 18);
                lab.ForeColor = skin.TextDim;
                Controls.Add(lab);

                _box = new TextBox();
                _box.BorderStyle = BorderStyle.FixedSingle;
                _box.SetBounds(16, 38, 268, 24);
                _box.Text = current ?? "";
                _box.BackColor = skin.Light ? Color.White : skin.InputBg;
                _box.ForeColor = skin.Text;
                Controls.Add(_box);

                Button ok = MakeButton("确定", skin, true);
                ok.SetBounds(146, 84, 72, 30);
                ok.DialogResult = DialogResult.OK;
                Controls.Add(ok);

                Button cancel = MakeButton("取消", skin, false);
                cancel.SetBounds(226, 84, 72, 30);
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);

                AcceptButton = ok;
                CancelButton = cancel;
                _box.SelectAll();
            }

            private static Button MakeButton(string text, Skin skin, bool primary)
            {
                Button b = new Button();
                b.Text = text;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = skin.Line;
                b.BackColor = primary ? Mix(skin.Accent, skin.CardBg, 0.72) : skin.InputBg;
                b.ForeColor = skin.Text;
                b.Font = new Font("Microsoft YaHei UI", 9f);
                b.UseVisualStyleBackColor = false;
                return b;
            }
        }

        private sealed class DueDialog : Form
        {
            private readonly DateTimePicker _picker;
            public bool Cleared;
            public string DueText { get { return _picker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } }

            public DueDialog(Skin skin, string current)
            {
                Text = "截止日期";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterParent;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                BackColor = skin.CardBg;
                ForeColor = skin.Text;
                Font = new Font("Microsoft YaHei UI", 9f);
                ClientSize = new Size(260, 128);

                _picker = new DateTimePicker();
                _picker.Format = DateTimePickerFormat.Custom;
                _picker.CustomFormat = "yyyy-MM-dd";
                _picker.SetBounds(16, 20, 228, 24);
                DateTime parsed;
                if (!string.IsNullOrEmpty(current) &&
                    DateTime.TryParse(current, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    _picker.Value = parsed;
                else
                    _picker.Value = DateTime.Today;
                Controls.Add(_picker);

                Button ok = MakeButton("确定", skin, true);
                ok.SetBounds(32, 76, 64, 30);
                ok.DialogResult = DialogResult.OK;
                Controls.Add(ok);

                Button clear = MakeButton("清除", skin, false);
                clear.SetBounds(104, 76, 64, 30);
                clear.Click += delegate { Cleared = true; DialogResult = DialogResult.OK; Close(); };
                Controls.Add(clear);

                Button cancel = MakeButton("取消", skin, false);
                cancel.SetBounds(176, 76, 64, 30);
                cancel.DialogResult = DialogResult.Cancel;
                Controls.Add(cancel);

                AcceptButton = ok;
                CancelButton = cancel;
            }

            private static Button MakeButton(string text, Skin skin, bool primary)
            {
                Button b = new Button();
                b.Text = text;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 1;
                b.FlatAppearance.BorderColor = skin.Line;
                b.BackColor = primary ? Mix(skin.Accent, skin.CardBg, 0.72) : skin.InputBg;
                b.ForeColor = skin.Text;
                b.Font = new Font("Microsoft YaHei UI", 9f);
                b.UseVisualStyleBackColor = false;
                return b;
            }
        }
    }
}
