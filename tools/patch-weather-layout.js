const fs = require('fs');
const p = 'src/UI/Views/WeatherView.cs';
let s = fs.readFileSync(p, 'utf8');

// 用纯 Dock 布局替代 SplitContainer（SplitterDistance 在创建时会被钳制，导致右侧被压缩）
const startMarker = '            var split = new SplitContainer';
const endMarker = '            Body.Controls.Add(split);';
const start = s.indexOf(startMarker);
const end = s.indexOf(endMarker);
if (start < 0 || end < 0) { console.log('MARKERS NOT FOUND'); process.exit(1); }
const after = s.indexOf('\n', s.indexOf('split.BringToFront();', end));

const newLayout = `            var leftWrap = new Panel { Dock = DockStyle.Left, Width = 320, BackColor = Palette.Bg, Padding = new Padding(0, 4, 8, 0) };
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
            leftWrap.BringToFront();
`;

s = s.slice(0, start) + newLayout + s.slice(after);

// 移除后面重复的 leftWrap/rightWrap/分栏代码残留（若有）
s = s.replace(/\n\s*var leftWrap = new Panel \{ Dock = DockStyle\.Fill, BackColor = Palette\.Bg \};\n(?:.*\n)*?\s*rightWrap\.Controls\.Add\(_nowCard\);\n\s*split\.Panel2\.Controls\.Add\(rightWrap\);\n/g, '\n');

fs.writeFileSync(p, s, 'utf8');
console.log('layout replaced:', !s.includes('SplitContainer'), '| has leftWrap:', s.includes('Dock = DockStyle.Left'));
