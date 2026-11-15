using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>天气页：城市搜索（唯一 id + 经纬度）、常用城市、切换、逐小时与多日预报。</summary>
    public class WeatherView : ViewBase
    {
        private readonly Store _store;
        private readonly CityStore _cities;
        private DarkInput _search;
        private DarkButton _btnSearch, _btnRefresh, _btnAddFav, _btnRemoveFav;
        private DarkList _cityList, _hourList;
        private Panel _nowCard;
        private Label _status;
        private WeatherSnapshot _now;

        public WeatherView(Store store) : base(store, "天气", "真实数据源 Open-Meteo；城市用唯一 ID + 经纬度，切换后重新请求")
        {
            _store = store;
            _cities = new CityStore();
            Build();
            LoadCurrent(false);
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Palette.Bg };
            _search = new DarkInput { Left = 0, Top = 2, Width = 300, Placeholder = "搜索城市，如：上海 / 广州 / Tokyo" };
            _search.Palette = Palette;
            _search.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoSearch(); } };
            _btnSearch = new DarkButton("搜索") { Primary = true, Left = 308, Top = 2, Width = 74 };
            _btnSearch.Click += delegate { DoSearch(); };
            _btnRefresh = new DarkButton("刷新天气") { Left = 390, Top = 2, Width = 90 };
            _btnRefresh.Click += delegate { LoadCurrent(true); };
            _btnAddFav = new DarkButton("设为常用") { Left = 488, Top = 2, Width = 90 };
            _btnAddFav.Click += delegate { AddFavorite(); };
            _btnRemoveFav = new DarkButton("移除常用") { Left = 586, Top = 2, Width = 90 };
            _btnRemoveFav.Click += delegate { RemoveFavorite(); };

            _status = new Label
            {
                Left = 0, Top = 44, AutoSize = true, ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg, Text = ""
            };
            top.Controls.AddRange(new Control[] { _search, _btnSearch, _btnRefresh, _btnAddFav, _btnRemoveFav, _status });

            var leftWrap = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = Palette.Bg, Padding = new Padding(0, 4, 8, 0) };
            var lblCities = new Label
            {
                Text = "常用城市 / 搜索结果", Dock = DockStyle.Top, Height = 24,
                ForeColor = Palette.TextDim, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _cityList = new DarkList { Dock = DockStyle.Fill };
            _cityList.Palette = Palette;
            _cityList.SetColumns(new[] { "城市", "信息" }, new[] { 130, 160 });
            _cityList.RowActivated += delegate { SwitchToSelectedCity(); };
            leftWrap.Controls.Add(_cityList);
            leftWrap.Controls.Add(lblCities);

            var rightWrap = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
            _nowCard = new Panel { Dock = DockStyle.Top, Height = 232, BackColor = Palette.Bg };
            _nowCard.Paint += delegate(object s2, PaintEventArgs e) { PaintNow(e.Graphics); };
            _nowCard.Resize += delegate { _nowCard.Invalidate(); };
            var lblHour = new Label
            {
                Text = "未来 24 小时", Dock = DockStyle.Top, Height = 24,
                ForeColor = Palette.TextDim, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
            };
            _hourList = new DarkList { Dock = DockStyle.Fill };
            _hourList.Palette = Palette;
            _hourList.SetColumns(new[] { "时间", "天气", "温度", "降水概率" }, new[] { 120, 200, 120, 120 });
            // Fill 必须先加，Top 后加，否则 Fill 会盖住 Top 区域
            rightWrap.Controls.Add(_hourList);
            rightWrap.Controls.Add(lblHour);
            rightWrap.Controls.Add(_nowCard);

            Body.Controls.Add(rightWrap);
            Body.Controls.Add(leftWrap);
            Body.Controls.Add(top);


            _cityList.SetRows(BuildCityRows());
        }

        private List<ListRow> BuildCityRows()
        {
            var rows = new List<ListRow>();
            foreach (var c in _cities.Favorites)
                rows.Add(new ListRow { Icon = "★", Cells = new[] { c.Name, c.Admin1 + " " + c.Country }, Tag = c, Accent = Palette.Accent });
            return rows;
        }

        private void DoSearch()
        {
            var kw = (_search.Text ?? "").Trim();
            if (kw.Length == 0) { _status.Text = "请输入城市名"; return; }
            _status.Text = "搜索中…";
            var list = CitySearch.Search(kw, 8);
            var rows = new List<ListRow>();
            foreach (var c in list)
                rows.Add(new ListRow { Icon = "🔎", Cells = new[] { c.Name, c.Display }, Tag = c });
            foreach (var c in _cities.Favorites)
                rows.Insert(0, new ListRow { Icon = "★", Cells = new[] { c.Name, c.Display }, Tag = c, Accent = Palette.Accent });
            _cityList.SetRows(rows);
            _status.Text = list.Count == 0 ? "没有找到匹配城市（检查网络）" : "找到 " + list.Count + " 个城市，双击切换";
        }

        private void SwitchToSelectedCity()
        {
            var row = _cityList.SelectedRow;
            var city = row == null ? null : row.Tag as CityInfo;
            if (city == null) { _status.Text = "先选中一个城市"; return; }
            _cities.SetCurrent(city);
            _status.Text = "已切换到 " + city.Display + "（" + city.Lat.ToString("0.##") + ", " + city.Lon.ToString("0.##") + "）";
            LoadCurrent(true);
        }

        private void AddFavorite()
        {
            var row = _cityList.SelectedRow;
            var city = row == null ? null : row.Tag as CityInfo;
            if (city == null) { _status.Text = "先选中一个城市"; return; }
            _cities.AddFavorite(city);
            _cityList.SetRows(BuildCityRows());
            _status.Text = "已加入常用：" + city.Display;
        }

        private void RemoveFavorite()
        {
            var row = _cityList.SelectedRow;
            var city = row == null ? null : row.Tag as CityInfo;
            if (city == null) { _status.Text = "先选中一个城市"; return; }
            _cities.RemoveFavorite(city.Key);
            _cityList.SetRows(BuildCityRows());
            _status.Text = "已移除常用：" + city.Display;
        }

        private void LoadCurrent(bool force = false)
        {
            var city = _cities.Current;
            try
            {
                var settings = _store.Settings;
                if (city != null)
                {
                    settings.WeatherLat = city.Lat;
                    settings.WeatherLon = city.Lon;
                    settings.HasCoords = true;
                    settings.WeatherCity = city.Name;
                }
                _status.Text = "请求中：" + (city != null ? city.Display : "自动定位") + " …";
                // 城市坐标变更必须落盘，否则重启后又回到旧城市（规格 12.1）
                try { _store.SaveSettingsOnly(); } catch { }
                _now = WeatherService.Get(settings, force);
                if (_now != null && (_now.Hours == null || _now.Hours.Count == 0))
                    _now = WeatherService.Get(settings, true);   // 旧缓存缺逐小时数据，强制刷新
                PaintStatus();
                FillHours();
                _nowCard.Invalidate();
            }
            catch (Exception ex)
            {
                _status.Text = "天气请求失败：" + ex.Message;
            }
        }

        private void PaintStatus()
        {
            if (_now == null) return;
            var src = _now.Source == "online" ? "实时" : _now.Source == "cache" ? "缓存（可能过期）" : "离线示意";
            _status.Text = _now.City + (_now.Region.Length > 0 ? " · " + _now.Region : "") + " · 来源 " + src + " · " + _now.UpdatedText;
        }

        private void FillHours()
        {
            var rows = new List<ListRow>();
            if (_now != null && _now.Hours != null)
            {
                foreach (var h in _now.Hours.Take(24))
                    rows.Add(new ListRow { Cells = new[] { h.Time, h.Text, Temperature(h.Temp), h.PrecipProb + "%" } });
            }
            _hourList.SetRows(rows);
        }

        private void PaintNow(Graphics g)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.Bg);
            var card = new Rectangle(0, 0, Math.Max(200, _nowCard.Width - 12), Math.Max(150, _nowCard.Height - 14));
            UiKit.DrawCard(g, Palette, card, 12);
            if (_now == null)
            {
                using (var f = new Font("Microsoft YaHei UI", 10F))
                    g.DrawString("正在加载天气…", f, new SolidBrush(Palette.TextFaint), 20, 20);
                return;
            }
            using (var fb = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold))
            using (var ft = new Font("Segoe UI", 34F, FontStyle.Bold))
            using (var fs = new Font("Microsoft YaHei UI", 9F))
            {
                g.DrawString(_now.City + (_now.Region.Length > 0 ? " · " + _now.Region : ""), fb, new SolidBrush(Palette.Text), 18, 14);
                g.DrawString(Temperature(_now.Temp), ft, new SolidBrush(Palette.Accent), 16, 40);
                using (var fe = new Font("Segoe UI Emoji", 22F))
                    g.DrawString(_now.Icon, fe, new SolidBrush(Palette.Text), 160, 48);
                g.DrawString(_now.Text, fb, new SolidBrush(Palette.Text), 218, 56);
                g.DrawString("体感 " + Temperature(_now.Feels) + "  ·  湿度 " + _now.Humidity + "%  ·  " + _now.WindDir + "风 " + _now.WindSpeed.ToString("0.#") + " m/s  ·  降水 " + _now.PrecipProb + "%",
                    fs, new SolidBrush(Palette.TextDim), 18, 128);
                var src = _now.Source == "online" ? "实时" : _now.Source == "cache" ? "缓存 · 可能过期" : "离线示意";
                g.DrawString("数据来源：" + src + " · " + _now.UpdatedText, fs, new SolidBrush(Palette.TextFaint), 18, 152);
                if (_now.Days != null && _now.Days.Count > 0)
                {
                    int x = 18;
                    foreach (var d in _now.Days.Take(5))
                    {
                        g.DrawString(d.Weekday, fs, new SolidBrush(Palette.TextDim), x, 172);
                        g.DrawString(d.Icon + " " + Temperature(d.TMax) + "/" + Temperature(d.TMin), fs, new SolidBrush(Palette.TextFaint), x, 186);
                        x += Math.Max(110, (_nowCard.Width - 40) / Math.Max(1, _now.Days.Count));
                    }
                }
            }
        }

        private string Temperature(double celsius)
        {
            if (string.Equals(_store.Settings.TempUnit, "F", StringComparison.OrdinalIgnoreCase))
                return (celsius * 9.0 / 5.0 + 32.0).ToString("0") + "°F";
            return celsius.ToString("0") + "°C";
        }

        protected override void OnActivated() { LoadCurrent(false); }
    }
}
