using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace DeskBox.UI
{
    /// <summary>左侧分类栏：全部 / 已隐藏 / 未分类 + 9 个分类，支持拖放高亮。</summary>
    public class Sidebar : Control
    {
        private readonly Store _store;
        private readonly Dictionary<string, Rectangle> _rects = new Dictionary<string, Rectangle>();
        private string _active = "all";
        private string _hover;
        private string _dropTarget;
        private List<DesktopItem> _items = new List<DesktopItem>();

        public event Action<string> FilterChanged;
        public event Action NewCategoryRequested;
        public event Action<string> CategoryContextRequested;

        public Palette Palette { get; set; }

        public string ActiveFilter { get { return _active; } }

        public Sidebar(Store store)
        {
            _store = store;
            Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
            Width = 200;
        }

        public void SetActive(string id)
        {
            _active = id ?? "all";
            Invalidate();
        }

        public void SetData(List<DesktopItem> items)
        {
            _items = items ?? new List<DesktopItem>();
            Invalidate();
        }

        public void SetDropTarget(string catId)
        {
            if (_dropTarget == catId) return;
            _dropTarget = catId;
            Invalidate();
        }

        public string HitCategory(Point p)
        {
            foreach (var kv in _rects)
                if (kv.Value.Contains(p)) return kv.Key;
            return null;
        }

        public void RefreshTheme()
        {
            Palette = Palette.For(_store.Settings.Theme, _store.Settings.Accent);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.Bg);
            _rects.Clear();

            int y = 12;
            int x = 10;
            int w = Width - 20;

            using (var fl = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                g.DrawString("快速筛选", fl, new SolidBrush(Palette.TextFaint), x + 6, y);
            y += 22;

            bool showHidden = _store.Settings.ShowHidden;
            var hiddenCount = _items.Count(i => i.Hidden);
            var visible = showHidden ? _items : _items.Where(i => !i.Hidden).ToList();

            DrawRow(g, "all", "🗂", "全部条目", visible.Count, null, ref y, x, w);
            DrawRow(g, "hidden", "🙈", "已隐藏", hiddenCount, null, ref y, x, w);
            DrawRow(g, "uncategorized", "📥", "未分类",
                visible.Count(i => string.IsNullOrEmpty(i.CategoryId)), null, ref y, x, w);

            y += 14;
            using (var fl = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                g.DrawString("分类", fl, new SolidBrush(Palette.TextFaint), x + 6, y);
            y += 22;

            foreach (var c in _store.Categories.OrderBy(c => c.Order))
            {
                int cnt = visible.Count(i => i.CategoryId == c.Id);
                DrawRow(g, c.Id, c.Icon, c.Name, cnt, Palette.ParseColor(c.Color, Palette.Accent), ref y, x, w);
            }

            // 新建分类
            var addRect = new Rectangle(x, y + 8, w, 30);
            using (var p = new Pen(Palette.Line))
            using (var path = Palette.RoundedRect(addRect, 8))
                g.DrawPath(p, path);
            using (var f = new Font("Microsoft YaHei UI", 9F))
                g.DrawString("＋ 新建分类", f, new SolidBrush(Palette.TextDim), addRect.X + 12, addRect.Y + 7);
            _rects["__add__"] = addRect;
        }

        private void DrawRow(Graphics g, string id, string icon, string name, int count, Color? dot, ref int y, int x, int w)
        {
            var r = new Rectangle(x, y, w, 30);
            bool active = _active == id;
            bool hover = _hover == id;
            bool drop = _dropTarget == id;

            if (active) Palette.FillRounded(g, Color.FromArgb(40, Palette.Accent), r, 8);
            else if (drop) Palette.FillRounded(g, Color.FromArgb(60, Palette.Accent), r, 8);
            else if (hover) Palette.FillRounded(g, Palette.Hover, r, 8);

            if (dot.HasValue)
            {
                using (var b = new SolidBrush(dot.Value))
                    g.FillRectangle(b, r.X + 2, r.Y + 9, 3, 12);
            }
            using (var fi = new Font("Segoe UI Emoji", 9.5F))
            using (var f = new Font("Microsoft YaHei UI", 9F))
            using (var fc = new Font("Microsoft YaHei UI", 8.5F))
            {
                g.DrawString(icon, fi, new SolidBrush(Palette.Text), r.X + 10, r.Y + 6);
                g.DrawString(Palette.Ellipsis(g, name, f, w - 70), f,
                    new SolidBrush(active ? Palette.Text : Palette.TextDim), r.X + 32, r.Y + 7);
                var cs = count.ToString();
                var cw = g.MeasureString(cs, fc).Width;
                g.DrawString(cs, fc, new SolidBrush(active ? Palette.Text : Palette.TextFaint), r.Right - cw - 10, r.Y + 8);
            }
            _rects[id] = r;
            y += 31;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var id = HitCategory(e.Location);
            if (id != _hover)
            {
                _hover = id;
                Cursor = id != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != null) { _hover = null; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var id = HitCategory(e.Location);
            if (id == null) return;
            if (id == "__add__")
            {
                var h = NewCategoryRequested; if (h != null) h();
                return;
            }
            _active = id;
            Invalidate();
            var h2 = FilterChanged; if (h2 != null) h2(id);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
            {
                var id = HitCategory(e.Location);
                if (id != null && id != "__add__" && id != "all" && id != "hidden" && id != "uncategorized")
                {
                    var h = CategoryContextRequested; if (h != null) h(id);
                }
            }
        }
    }
}
