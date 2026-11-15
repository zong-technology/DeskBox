using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBox.UI
{
    public class ListRow
    {
        public string[] Cells = new string[0];
        public string Icon = "";
        public object Tag;
        public string Tip = "";
        public Color? Accent;
    }

    /// <summary>自绘通用列表：多列、行内图标、选中、悬停、滚轮、双击、右键。</summary>
    public class DarkList : Control
    {
        private readonly List<ListRow> _rows = new List<ListRow>();
        private int[] _colWidths = new int[] { 300 };
        private int[] _baseWidths = new int[] { 300 };
        private bool _autoFitColumns;
        public bool AutoFitColumns { get{return _autoFitColumns;} set{_autoFitColumns=value;FitColumns();} }
        private string[] _headers = new string[] { "名称" };
        private int _rowH = 26;
        private int _selected = -1;
        private int _hover = -1;
        private int _scroll;

        public Palette Palette { get; set; }
        public int RowHeight { get { return _rowH; } set { _rowH = Math.Max(18, value); Invalidate(); } }
        public bool ShowHeader = true;
        public bool CardSurface;
        internal bool InsetRows, StackFirstTwoCells;
        public event EventHandler SelectionChanged;
        public event EventHandler RowActivated;
        public event MouseEventHandler RowContextMenu;

        public DarkList()
        {
            Palette = Palette.For("midnight", "#6EE7FF");
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(9, 13, 22);
            Font = new Font("Microsoft YaHei UI", 8.8F);
        }

        public void ApplyPalette(Palette p) { Palette = p; BackColor = p.Bg; Invalidate(); }

        public void SetColumns(string[] headers, int[] widths)
        {
            _headers = headers ?? new string[0];
            _baseWidths = widths ?? new int[] { 300 };FitColumns();
            Invalidate();
        }
        protected override void OnResize(EventArgs e){base.OnResize(e);FitColumns();}
        private void FitColumns()
        {
            if(_baseWidths==null)return;_colWidths=(int[])_baseWidths.Clone();
            if(_autoFitColumns&&Width>40&&_colWidths.Length>0){double total=0;foreach(var width in _baseWidths)total+=width;int available=Width-24,minimum=Math.Min(72,available/_colWidths.Length),remaining=available-minimum*_colWidths.Length;if(total>0)for(int i=0;i<_colWidths.Length;i++)_colWidths[i]=minimum+(int)(remaining*_baseWidths[i]/total);}Invalidate();
        }

        public void SetRows(List<ListRow> rows)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            _selected = -1;
            _scroll = 0;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public ListRow SelectedRow { get { return _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null; } }
        public int SelectedIndex { get { return _selected; } }
        public int RowCount { get { return _rows.Count; } }
        public List<ListRow> Rows { get { return _rows; } }

        public void SelectIndex(int i)
        {
            if (i < -1 || i >= _rows.Count) return;
            _selected = i;
            EnsureVisible();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        private int HeaderH { get { return ShowHeader ? 26 : 0; } }

        private void EnsureVisible()
        {
            if (_selected < 0 || Height - HeaderH < _rowH) return;
            int top = _selected * _rowH - _scroll;
            if (top < 0) _scroll = _selected * _rowH;
            else if (top + _rowH > Height - HeaderH) _scroll = _selected * _rowH - (Height - HeaderH) + _rowH;
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll));
        }

        private int MaxScroll
        {
            get { return Math.Max(0, _rows.Count * _rowH - (Height - HeaderH)); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(CardSurface?Palette.CardBg:Palette.Bg);

            int x0 = 8;
            if (ShowHeader)
            {
                using (var b = new SolidBrush(Palette.HeaderBg))
                    g.FillRectangle(b, 0, 0, Width, HeaderH);
                using (var f = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Bold))
                {
                    int x = x0;
                    for (int c = 0; c < _headers.Length && c < _colWidths.Length; c++)
                    {
                        using (var brush = new SolidBrush(Palette.TextDim)) g.DrawString(_headers[c], f, brush, x, 5);
                        x += _colWidths[c];
                    }
                }
                using (var p = new Pen(Palette.Line))
                    g.DrawLine(p, 0, HeaderH - 1, Width, HeaderH - 1);
            }

            if (_rows.Count == 0)
            {
                using (var f = new Font("Microsoft YaHei UI", 9.5F))
                {
                    var s = "暂无数据";
                    var sz = g.MeasureString(s, f);
                    g.DrawString(s, f, new SolidBrush(Palette.TextFaint), (Width - sz.Width) / 2, Height / 2 - 10);
                }
                return;
            }

            int first = Math.Max(0, _scroll / _rowH);
            int last = Math.Min(_rows.Count - 1, ( _scroll + Height - HeaderH) / _rowH + 1);
            for (int i = first; i <= last; i++)
            {
                var row = _rows[i];
                int y = HeaderH + i * _rowH - _scroll;
                if (y > Height) break;
                var rect = new Rectangle(4, y, Width - 8, _rowH - 1);
                if (InsetRows)
                {
                    var card = new Rectangle(rect.X, rect.Y + 2, rect.Width, Math.Max(1, rect.Height - 4));
                    Palette.FillRounded(g, i == _selected || i == _hover ? Palette.Hover : Palette.PanelAlt, card, 7);
                }
                else if (i == _selected) Palette.FillRounded(g, Color.FromArgb(52, Palette.Accent), rect, 6);
                else if (i == _hover) Palette.FillRounded(g, Palette.Hover, rect, 6);
                if(CardSurface&&!InsetRows)using(var divider=new Pen(Color.FromArgb(65,Palette.Line)))g.DrawLine(divider,8,y+_rowH-1,Width-14,y+_rowH-1);

                if (StackFirstTwoCells && row.Cells.Length >= 2)
                {
                    var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
                    int width = Math.Max(1, Width - 26);
                    TextRenderer.DrawText(g, row.Cells[0], Font, new Rectangle(12, y + 3, width, 20),
                        row.Accent ?? Palette.Text, flags);
                    TextRenderer.DrawText(g, row.Cells[1],
                        UiKit.CachedFont("Microsoft YaHei UI", 8, FontStyle.Regular),
                        new Rectangle(12, y + 23, width, 15), Palette.TextDim, flags);
                    continue;
                }

                int x = x0;
                if (!string.IsNullOrEmpty(row.Icon))
                {
                    using (var fi = new Font("Segoe UI Emoji", 9F))
                        using(var iconBrush=new SolidBrush(Palette.TextDim))g.DrawString(row.Icon, fi, iconBrush, x, y + (_rowH-fi.Height)/2f);
                    x += 20;
                }
                Color fg = Palette.Text;
                if (row.Accent.HasValue) fg = row.Accent.Value;
                {
                    for (int c = 0; c < row.Cells.Length && c < _colWidths.Length; c++)
                    {
                        int w = _colWidths[c] - (c == 0 && !string.IsNullOrEmpty(row.Icon) ? 20 : 0);
                        if (w <= 4) { x += _colWidths[c]; continue; }
                        var txt = Palette.Ellipsis(g, row.Cells[c] ?? "", Font, w - 8);
                        using (var brush = new SolidBrush(c == 0 || i == _selected ? fg : Palette.TextDim)) g.DrawString(txt, Font, brush, x, y + (_rowH-Font.Height)/2f);
                        x += _colWidths[c];
                        if (x > Width) break;
                    }
                }
            }

            // 滚动条
            if (MaxScroll > 0)
            {
                int trackH = Height - HeaderH - 8;
                int barH = Math.Max(30, (int)(trackH * (double)(Height - HeaderH) / (_rows.Count * _rowH)));
                int barY = HeaderH + 4 + (int)((trackH - barH) * (double)_scroll / Math.Max(1, MaxScroll));
                Palette.FillRounded(g, Color.FromArgb(90, Palette.TextFaint), new Rectangle(Width - 7, barY, 4, barH), 2);
            }
        }

        private int RowIndexAt(Point p)
        {
            if (p.Y < HeaderH) return -1;
            int idx = (p.Y - HeaderH + _scroll) / _rowH;
            return idx >= 0 && idx < _rows.Count ? idx : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = RowIndexAt(e.Location);
            if (idx != _hover) { _hover = idx; Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            int idx = RowIndexAt(e.Location);
            if (e.Button == MouseButtons.Left)
            {
                _selected = idx;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            else if (e.Button == MouseButtons.Right && idx >= 0)
            {
                _selected = idx;
                Invalidate();
                if (RowContextMenu != null) RowContextMenu(this, e);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int idx = RowIndexAt(e.Location);
            if (idx >= 0)
            {
                _selected = idx;
                if (RowActivated != null) RowActivated(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta));
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down && _selected < _rows.Count - 1) { SelectIndex(_selected + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up && _selected > 0) { SelectIndex(_selected - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && SelectedRow != null) { if (RowActivated != null) RowActivated(this, EventArgs.Empty); e.Handled = true; }
        }
    }
}
