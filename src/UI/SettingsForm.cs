using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DeskBox.Native;

namespace DeskBox.UI
{
    /// <summary>设置面板：主题 / 图标尺寸 / 视图 / 隐藏模式 / 天气城市 / 桌面目录 / 开机自启 / 接管桌面。</summary>
    public class SettingsForm : Form
    {
        private readonly Store _store;
        private readonly Palette _p;
        private readonly AppSettings _s;

        public SettingsForm(Store store, Palette palette)
        {
            _store = store;
            _p = palette;
            _s = Clone(store.Settings);
            Build();
        }

        private static AppSettings Clone(AppSettings s)
        {
            return new AppSettings
            {
                Theme = s.Theme, Accent = s.Accent, View = s.View, IconSize = s.IconSize,
                ShowHidden = s.ShowHidden, HideMode = s.HideMode, SortBy = s.SortBy, SortDir = s.SortDir,
                Clock24 = s.Clock24, ShowSeconds = s.ShowSeconds, WeatherCity = s.WeatherCity,
                WeatherLat = s.WeatherLat, WeatherLon = s.WeatherLon, HasCoords = s.HasCoords, DesktopDir = s.DesktopDir,
                TakeoverIcons = s.TakeoverIcons, AutoStart = s.AutoStart, ShowWidgets = s.ShowWidgets,
                ShowSidebar = s.ShowSidebar, WallpaperTint = s.WallpaperTint, TintOpacity = s.TintOpacity
            };
        }

        private void Build()
        {
            Text = "DeskBox 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            ClientSize = new Size(560, 560);
            BackColor = _p.Panel;
            ForeColor = _p.Text;
            Font = new Font("Microsoft YaHei UI", 9F);

            int y = 18;
            var tabs = new TabControl
            {
                Left = 14, Top = 14, Width = 532, Height = 470,
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            // ---- 外观 ----
            var t1 = new TabPage("外观") { BackColor = _p.Panel };
            int ty = 16;
            AddLabel(t1, "主题", 18, ty);
            var themeBox = new ComboBox
            {
                Left = 130, Top = ty - 3, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _p.PanelAlt, ForeColor = _p.Text, FlatStyle = FlatStyle.Flat
            };
            themeBox.Items.AddRange(new object[] { "cyber（工程矩阵）", "midnight（深空）", "aurora（极光）", "daylight（明亮）" });
            themeBox.SelectedIndex = _s.Theme == "midnight" ? 1 : _s.Theme == "aurora" ? 2 : _s.Theme == "daylight" ? 3 : 0;
            t1.Controls.Add(themeBox);
            ty += 40;

            AddLabel(t1, "强调色", 18, ty);
            var accents = new[] { "#6EE7FF", "#A78BFA", "#34D399", "#FBBF24", "#F472B6", "#F87171" };
            int ax = 130;
            foreach (var a in accents)
            {
                var c = Palette.ParseColor(a, _p.Accent);
                var btn = new Button
                {
                    Left = ax, Top = ty - 6, Width = 30, Height = 26, BackColor = c,
                    FlatStyle = FlatStyle.Flat, Tag = a, Cursor = Cursors.Hand
                };
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.BorderColor = _p.Line;
                btn.Click += (s, e) => { _s.Accent = (string)((Button)s).Tag; };
                t1.Controls.Add(btn);
                ax += 38;
            }
            ty += 46;

            AddLabel(t1, "图标尺寸", 18, ty);
            var size = new TrackBar { Left = 126, Top = ty - 8, Width = 250, Minimum = 40, Maximum = 96, TickFrequency = 8, Value = Math.Max(40, Math.Min(96, _s.IconSize)) };
            var sizeLbl = new Label { Left = 386, Top = ty, Width = 60, Text = _s.IconSize + " px", ForeColor = _p.TextDim };
            size.ValueChanged += (s, e) => { _s.IconSize = size.Value; sizeLbl.Text = size.Value + " px"; };
            t1.Controls.Add(size); t1.Controls.Add(sizeLbl);
            ty += 52;

            AddLabel(t1, "默认视图", 18, ty);
            var viewBox = new ComboBox { Left = 130, Top = ty - 3, Width = 160, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = _p.PanelAlt, ForeColor = _p.Text, FlatStyle = FlatStyle.Flat };
            viewBox.Items.AddRange(new object[] { "网格", "列表", "详细", "紧凑" });
            viewBox.SelectedIndex = _s.View == "list" ? 1 : _s.View == "details" ? 2 : _s.View == "compact" ? 3 : 0;
            t1.Controls.Add(viewBox);
            ty += 44;

            var chk24 = new CheckBox { Left = 18, Top = ty, Width = 200, Text = "24 小时制", Checked = _s.Clock24, ForeColor = _p.Text };
            var chkSec = new CheckBox { Left = 230, Top = ty, Width = 200, Text = "显示秒", Checked = _s.ShowSeconds, ForeColor = _p.Text };
            t1.Controls.Add(chk24); t1.Controls.Add(chkSec);
            ty += 34;

            var chkWidgets = new CheckBox { Left = 18, Top = ty, Width = 220, Text = "显示右侧小部件栏", Checked = _s.ShowWidgets, ForeColor = _p.Text };
            t1.Controls.Add(chkWidgets);

            // ---- 桌面 ----
            var t2 = new TabPage("桌面") { BackColor = _p.Panel };
            int dy = 16;
            AddLabel(t2, "桌面目录", 18, dy);
            var dirBox = new TextBox { Left = 130, Top = dy - 2, Width = 300, Text = _s.DesktopDir, BackColor = _p.PanelAlt, ForeColor = _p.Text, BorderStyle = BorderStyle.FixedSingle };
            var dirBtn = new Button { Left = 436, Top = dy - 3, Width = 60, Height = 24, Text = "浏览", FlatStyle = FlatStyle.Flat, BackColor = _p.PanelAlt, ForeColor = _p.TextDim };
            dirBtn.FlatAppearance.BorderColor = _p.Line;
            dirBtn.Click += (s, e) =>
            {
                using (var fb = new FolderBrowserDialog())
                {
                    fb.Description = "选择桌面目录";
                    if (fb.ShowDialog(this) == DialogResult.OK) dirBox.Text = fb.SelectedPath;
                }
            };
            t2.Controls.Add(dirBox); t2.Controls.Add(dirBtn);
            dy += 42;

            var chkTakeover = new CheckBox { Left = 18, Top = dy, Width = 480, Text = "接管桌面（隐藏系统桌面图标，由 DeskBox 全权管理）", Checked = _s.TakeoverIcons, ForeColor = _p.Text };
            t2.Controls.Add(chkTakeover);
            dy += 30;
            var tip = new Label
            {
                Left = 40, Top = dy, Width = 460, Height = 40,
                Text = "关闭 DeskBox 或取消勾选时会自动恢复系统桌面图标。",
                ForeColor = _p.TextFaint
            };
            t2.Controls.Add(tip);
            dy += 54;

            var chkAuto = new CheckBox { Left = 18, Top = dy, Width = 300, Text = "开机自动启动 DeskBox", Checked = _s.AutoStart, ForeColor = _p.Text };
            t2.Controls.Add(chkAuto);
            dy += 36;

            AddLabel(t2, "隐藏方式", 18, dy);
            var hideBox = new ComboBox { Left = 130, Top = dy - 3, Width = 240, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = _p.PanelAlt, ForeColor = _p.Text, FlatStyle = FlatStyle.Flat };
            hideBox.Items.AddRange(new object[] { "仅 DeskBox 内隐藏（不改文件）", "同时设置 Windows 隐藏属性" });
            hideBox.SelectedIndex = _s.HideMode == "attribute" ? 1 : 0;
            t2.Controls.Add(hideBox);

            // ---- 天气 ----
            var t3 = new TabPage("天气") { BackColor = _p.Panel };
            int wy = 16;
            AddLabel(t3, "城市", 18, wy);
            var cityBox = new TextBox { Left = 130, Top = wy - 2, Width = 200, Text = _s.WeatherCity, BackColor = _p.PanelAlt, ForeColor = _p.Text, BorderStyle = BorderStyle.FixedSingle };
            var autoBtn = new Button { Left = 340, Top = wy - 3, Width = 110, Height = 24, Text = "自动定位", FlatStyle = FlatStyle.Flat, BackColor = _p.PanelAlt, ForeColor = _p.TextDim };
            autoBtn.FlatAppearance.BorderColor = _p.Line;
            autoBtn.Click += (s, e) => { cityBox.Text = ""; };
            t3.Controls.Add(cityBox); t3.Controls.Add(autoBtn);
            wy += 40;
            var wTip = new Label
            {
                Left = 18, Top = wy, Width = 480, Height = 80,
                Text = "留空 = 自动定位（按系统时区默认城市）。\n天气来自 Open-Meteo（免 Key），断网时显示离线示意。",
                ForeColor = _p.TextFaint
            };
            t3.Controls.Add(wTip);

            tabs.TabPages.Add(t1);
            tabs.TabPages.Add(t2);
            tabs.TabPages.Add(t3);
            Controls.Add(tabs);

            var ok = new Button { Text = "保存设置", Left = 330, Top = 500, Width = 100, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = _p.Accent, ForeColor = Color.FromArgb(12, 18, 28) };
            var cancel = new Button { Text = "取消", Left = 440, Top = 500, Width = 90, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = _p.PanelAlt, ForeColor = _p.TextDim };
            ok.FlatAppearance.BorderSize = 0;
            cancel.FlatAppearance.BorderColor = _p.Line;
            ok.Click += (s, e) =>
            {
                _s.Theme = themeBox.SelectedIndex == 1 ? "midnight" : themeBox.SelectedIndex == 2 ? "aurora" : themeBox.SelectedIndex == 3 ? "daylight" : "cyber";
                _s.View = viewBox.SelectedIndex == 1 ? "list" : viewBox.SelectedIndex == 2 ? "details" : viewBox.SelectedIndex == 3 ? "compact" : "grid";
                _s.IconSize = size.Value;
                _s.Clock24 = chk24.Checked;
                _s.ShowSeconds = chkSec.Checked;
                _s.ShowWidgets = chkWidgets.Checked;
                _s.DesktopDir = dirBox.Text.Trim();
                _s.TakeoverIcons = chkTakeover.Checked;
                _s.AutoStart = chkAuto.Checked;
                _s.HideMode = hideBox.SelectedIndex == 1 ? "attribute" : "soft";
                _s.WeatherCity = cityBox.Text.Trim();
                _s.WeatherLat = double.NaN;
                _s.WeatherLon = double.NaN;

                Copy(_s, _store.Settings);
                AutoStart.Apply(_s.AutoStart);
                DialogResult = DialogResult.OK;
                Close();
            };
            cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static void Copy(AppSettings from, AppSettings to)
        {
            to.Theme = from.Theme; to.Accent = from.Accent; to.View = from.View; to.IconSize = from.IconSize;
            to.ShowHidden = from.ShowHidden; to.HideMode = from.HideMode; to.SortBy = from.SortBy; to.SortDir = from.SortDir;
            to.Clock24 = from.Clock24; to.ShowSeconds = from.ShowSeconds; to.WeatherCity = from.WeatherCity;
            to.WeatherLat = from.WeatherLat; to.WeatherLon = from.WeatherLon; to.HasCoords = from.HasCoords; to.DesktopDir = from.DesktopDir;
            to.TakeoverIcons = from.TakeoverIcons; to.AutoStart = from.AutoStart; to.ShowWidgets = from.ShowWidgets;
            to.ShowSidebar = from.ShowSidebar; to.WallpaperTint = from.WallpaperTint; to.TintOpacity = from.TintOpacity;
        }

        private void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, Left = x, Top = y, Width = 110, ForeColor = _p.TextDim });
        }
    }

    /// <summary>开机自启：HKCU Run 键（不写 C 盘文件）。</summary>
    public static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DeskBox";

        public static void Apply(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        var exe = Application.ExecutablePath;
                        key.SetValue(ValueName, "\"" + exe + "\" --autostart");
                    }
                    else if (key.GetValue(ValueName) != null)
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception ex) { AppLog.Write("warn", "settings", "设置开机自启失败: " + ex.Message); }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                    return key != null && key.GetValue(ValueName) != null;
            }
            catch { return false; }
        }
    }
}
