using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>左侧模块导航 + 中间内容切换。桌面视图保留原有 IconBoard/Sidebar 组合。</summary>
    public class RailButton : Control
    {
        public string Icon = "";
        public string Label = "";
        public bool Active;
        public bool Expanded;
        public Palette Palette;
        private bool _hover;

        public RailButton(string icon, string label, Palette p)
        {
            Icon = icon; Label = label; Palette = p;
            Width = 64; Height = 52;
            AccessibleName = label;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        // 自绘控件不依赖 Control.Click 的捕获语义：鼠标抬起即视为点击
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.HeaderBg);
            if (Active) Palette.FillRounded(g, Color.FromArgb(52, Palette.Accent), new Rectangle(6, 4, Width - 12, Height - 8), 10);
            else if (_hover) Palette.FillRounded(g, Palette.Hover, new Rectangle(6, 4, Width - 12, Height - 8), 10);
            using (var fi = new Font("Segoe UI Emoji", 13F))
            using (var ib = new SolidBrush(Active ? Palette.Text : Palette.TextDim))
            {
                var iw = g.MeasureString(Icon, fi).Width;
                g.DrawString(Icon, fi, ib, Expanded ? 15 : (Width - iw) / 2f, Expanded ? 14 : 6);
            }
            using (var f = new Font("Microsoft YaHei UI", 7.5F))
            {
                var sz = g.MeasureString(Label, f);
                g.DrawString(Label, f, new SolidBrush(Active ? Palette.Text : Palette.TextFaint), Expanded ? 48 : (Width - sz.Width) / 2, Expanded ? 19 : Height - 17);
            }
        }
    }

    /// <summary>模块宿主：管理导航栏与各视图的创建/切换。</summary>
    public class ViewHost : Panel
    {
        private readonly Store _store;
        private readonly Dictionary<string, UserControl> _views = new Dictionary<string, UserControl>();
        private readonly List<RailButton> _buttons = new List<RailButton>();
        private readonly Panel _rail, _content;
        private readonly Func<string, UserControl> _factory;
        private string _current = "desktop";
        public event Action<string> ViewChanged;

        public ViewHost(Store store, Palette palette, Func<string, UserControl> factory)
        {
            _store = store;
            _factory = factory;
            BackColor = palette.Bg;
            _rail = new Panel { Dock = DockStyle.Left, Width = 64, BackColor = palette.HeaderBg };
            _content = new Panel { Dock = DockStyle.Fill, BackColor = palette.Bg };
            Controls.Add(_content);
            Controls.Add(_rail);
            BuildRail(palette);
        }

        private void BuildRail(Palette p)
        {
            string[,] items = new string[,]
            {
                { "desktop", "🗂", "桌面" },
                { "search", "🔍", "搜索" },
                { "library", "★", "收藏" },
                { "disk", "💽", "磁盘" },
                { "history", "🕘", "历史" },
                { "ai", "✦", "AI" },
                { "automation", "⚡", "自动化" },
                { "monitor", "📈", "监控" },
                { "clipboard", "📋", "剪贴板" },
                { "settings", "⚙", "设置" },
            };
            int y = 10;
            for (int i = 0; i < items.GetLength(0); i++)
            {
                var b = new RailButton(items[i, 1], items[i, 2], p) { Top = y, Left = 4 };
                var id = items[i, 0];
                b.Click += delegate { SwitchTo(id, true); };
                _rail.Controls.Add(b);
                _buttons.Add(b);
                y += 56;
            }
        }

        public PalettedView CurrentView { get { return null; } }

        public string Current { get { return _current; } }

        public void ApplyPalette(Palette p)
        {
            BackColor = p.Bg;
            _rail.BackColor = p.HeaderBg;
            _content.BackColor = p.Bg;
            foreach (var b in _buttons) { b.Palette = p; b.Invalidate(); }
            foreach (var v in _views.Values)
            {
                var vb = v as ViewBase;
                if (vb != null) vb.ApplyPalette(p);
            }
        }

        public UserControl SwitchTo(string id, bool animated)
        {
            if (id == null) return null;
            UserControl view;
            if (!_views.TryGetValue(id, out view))
            {
                view = _factory(id);
                if (view == null) return null;
                view.Dock = DockStyle.Fill;
                _views[id] = view;
                _content.Controls.Add(view);
            }
            foreach (var v in _views.Values) v.Visible = (v == view);
            view.Visible = true;
            view.BringToFront();
            _current = id;
            for (int i = 0; i < _buttons.Count; i++) _buttons[i].Active = false;
            // 按 id 顺序对应按钮
            string[] order = { "desktop", "search", "library", "disk", "history", "ai", "automation", "monitor", "clipboard", "settings" };
            for (int i = 0; i < order.Length && i < _buttons.Count; i++) _buttons[i].Active = order[i] == id;
            foreach (var b in _buttons) b.Invalidate();
            var vb2 = view as ViewBase;
            if (vb2 != null) vb2.Activate();
            if (ViewChanged != null) ViewChanged(id);
            return view;
        }

        public UserControl Get(string id)
        {
            UserControl v;
            return _views.TryGetValue(id, out v) ? v : null;
        }
    }

    /// <summary>占位类型，避免外部引用出错。</summary>
    public class PalettedView { }
}
