using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace DeskBox.UI
{
    /// <summary>通用暗色控件：按钮、输入框、卡片面板、标题行。</summary>
    public class DarkButton : Control
    {
        private bool _hover, _down;
        public bool Primary;
        public bool Navigation;
        internal bool CompactNavigation;
        public string Symbol="";

        public DarkButton(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = UiKit.CachedFont("Microsoft YaHei UI", DesignTokens.Caption, FontStyle.Regular);
            Height = 28;
            Cursor = Cursors.Hand;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            Palette = Palette.For("midnight", "#6EE7FF");
        }

        public Palette Palette { get; set; }
        public int Radius = DesignTokens.RadiusMedium;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { OnClick(EventArgs.Empty); e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintSelf(e); } catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "ui", "DarkButton paint: " + ex.ToString()); }
        }

        private void PaintSelf(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var p = Palette;
            if(Navigation)
            {
                var bounds=new Rectangle(0,0,Width-1,Height-1);
                var background=Primary?UiKit.Mix(p.HeaderBg,p.Accent,0.12):_hover?p.Hover:p.HeaderBg;
                Palette.FillRounded(g,background,bounds,7);
                if(Primary){using(var stripe=new SolidBrush(p.Accent))g.FillRectangle(stripe,0,8,3,Height-16);}
                var color=Primary?p.Accent:_hover?p.Text:p.TextDim;
                var iconBounds = CompactNavigation ? ClientRectangle : new Rectangle(Dpi.D(this,16),0,Dpi.D(this,24),Height);
                var iconFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | (CompactNavigation ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
                TextRenderer.DrawText(g,Symbol,UiKit.CachedFont("Segoe MDL2 Assets",DesignTokens.Body,FontStyle.Regular),iconBounds,color,iconFlags);
                if (!CompactNavigation)
                    TextRenderer.DrawText(g,Text.Trim(),Font,new Rectangle(51,0,Width-60,Height),color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
                if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,5,Width-10,Height-10),color,background);
                return;
            }
            Color bg = Primary ? p.Accent : p.PanelAlt;
            if (_hover) bg = Primary ? UiKit.Mix(p.Accent, Color.Black, 0.12) : p.Hover;
            if (_down) bg = Primary ? UiKit.Mix(p.Accent, Color.Black, 0.24) : UiKit.Mix(p.Hover, Color.Black, 0.2);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Palette.FillRounded(g, bg, r, Radius);
            if (!Primary) Palette.DrawRounded(g, p.Line, r, Radius, 1f);
            var fg = Primary ? Color.FromArgb(12, 18, 28) : p.Text;
            if(Symbol.Length>0)TextRenderer.DrawText(g,Symbol,UiKit.CachedFont("Segoe MDL2 Assets",DesignTokens.BodySm,FontStyle.Regular),new Rectangle(Dpi.D(this,8),0,Dpi.D(this,24),Height),fg,TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,Text,Font,ClientRectangle,Enabled?fg:p.TextFaint,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if (Focused) ControlPaint.DrawFocusRectangle(g, new Rectangle(4, 4, Math.Max(1, Width - 9), Math.Max(1, Height - 9)), fg, bg);
        }

        public void ApplyPalette(Palette p) { Palette = p; Invalidate(); }
    }

    /// <summary>带占位符的暗色输入框（外层容器 + 原生 TextBox）。</summary>
    public class DarkInput : Control
    {
        private readonly TextBox _tb;
        private string _placeholder = "";
        private bool _useDrawnPlaceholder;
        private bool _focused;
        private Palette _palette;
        private const int EM_SETCUEBANNER = 0x1501;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public DarkInput()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Palette = Palette.For("midnight", "#6EE7FF");
            Height = 30;
            _tb = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                BackColor = Color.FromArgb(18, 24, 38),
                ForeColor = Color.White
            };
            _tb.TextChanged += delegate { SyncDrawnPlaceholder(); Invalidate(); };
            _tb.GotFocus += delegate { _focused = true; SyncDrawnPlaceholder(); Invalidate(); };
            _tb.LostFocus += delegate { _focused = false; SyncDrawnPlaceholder(); Invalidate(); };
            _tb.KeyDown += delegate(object s, KeyEventArgs e) { if (KeyPressed != null) KeyPressed(s, e); };
            _tb.HandleCreated += delegate { UpdateCue(); ApplyInnerPalette(); };
            Controls.Add(_tb);
            _tb.BringToFront();
        }

        public Palette Palette { get { return _palette; } set { _palette = value ?? Palette.For("midnight", "#6EE7FF"); ApplyInnerPalette(); Invalidate(); } }
        public string Placeholder { get { return _placeholder; } set { _placeholder = value ?? ""; UpdateCue(); SyncDrawnPlaceholder(); Invalidate(); } }
        internal bool UseDrawnPlaceholder
        {
            set
            {
                if (_useDrawnPlaceholder == value) return;
                _useDrawnPlaceholder = value;
                UpdateCue();
                SyncDrawnPlaceholder();
                Invalidate();
            }
        }
        public event KeyEventHandler KeyPressed;
        public override string Text { get { return _tb.Text; } set { _tb.Text = value; } }
        public TextBox Inner { get { return _tb; } }

        public void ApplyPalette(Palette p)
        {
            Palette = p;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (_tb == null) return;   // 基类构造期间会先触发一次布局
            SyncDrawnPlaceholder();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintSelf(e); } catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "ui", "DarkButton paint: " + ex.ToString()); }
        }

        private void PaintSelf(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Palette.FillRounded(g, Palette.PanelAlt, r, 8);
            Palette.DrawRounded(g, _focused ? Palette.Accent : Palette.Line, r, 8, 1f);
            if (ShowDrawnPlaceholder)
                TextRenderer.DrawText(g, _placeholder, _tb.Font,
                    new Rectangle(10, 0, DrawnPlaceholderWidth, Height), Palette.TextDim,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private void ApplyInnerPalette() { if (_tb == null || _palette == null) return; _tb.BackColor = _palette.PanelAlt; _tb.ForeColor = _palette.Text; SyncDrawnPlaceholder(); }
        private bool ShowDrawnPlaceholder
        {
            get { return _useDrawnPlaceholder && _tb != null && !_tb.Multiline &&
                _tb.TextLength == 0 && !string.IsNullOrEmpty(_placeholder); }
        }

        private int DrawnPlaceholderWidth
        {
            get { return Math.Max(0, Math.Min(Width - 35,
                TextRenderer.MeasureText(_placeholder, _tb.Font).Width + 6)); }
        }

        private void SyncDrawnPlaceholder()
        {
            if (_tb == null) return;
            int offset = ShowDrawnPlaceholder ? DrawnPlaceholderWidth : 0;
            if (_tb.Multiline)
                _tb.SetBounds(10, 6, Math.Max(10, Width - 20), Math.Max(20, Height - 12));
            else
                _tb.SetBounds(10 + offset, Math.Max(1, (Height - _tb.PreferredHeight) / 2),
                    Math.Max(10, Width - 20 - offset), _tb.PreferredHeight);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_useDrawnPlaceholder && e.Button == MouseButtons.Left) _tb.Focus();
        }

        private void UpdateCue() { try { if (_tb != null && _tb.IsHandleCreated) SendMessage(_tb.Handle, EM_SETCUEBANNER, new IntPtr(1), _useDrawnPlaceholder ? "" : _placeholder); } catch { } }
    }

    /// <summary>暗色下拉：自绘外观 + 原生 ComboBox 仅作数据容器（不显示），点击弹出菜单。</summary>
    public class DarkSelect : Control
    {
        private readonly ComboBox _cb;
        private bool _hover;
        private bool _open;

        public DarkSelect(string[] items)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Palette = Palette.For("midnight", "#6EE7FF");
            Height = 30;
            Cursor = Cursors.Hand;
            _cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
            if (items != null) _cb.Items.AddRange(items);
            if (_cb.Items.Count > 0) _cb.SelectedIndex = 0;
            _cb.SelectedIndexChanged += delegate { Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); };
            Controls.Add(_cb);
        }

        public Palette Palette { get; set; }
        public ComboBox Inner { get { return _cb; } }
        public int SelectedIndex
        {
            get { return _cb.SelectedIndex; }
            set { _cb.SelectedIndex = value; Invalidate(); }
        }
        public string SelectedText
        {
            get { return _cb.SelectedItem == null ? "" : Convert.ToString(_cb.SelectedItem); }
        }
        public event EventHandler SelectedIndexChanged;

        public void ApplyPalette(Palette p) { Palette = p; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            ShowMenu();
        }

        private void ShowMenu()
        {
            var menu = new ContextMenuStrip
            {
                BackColor = Palette.Panel,
                ForeColor = Palette.Text,
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false,
                Renderer = new DarkMenuRenderer(Palette)
            };
            for (int i = 0; i < _cb.Items.Count; i++)
            {
                int idx = i;
                string label = (_cb.SelectedIndex == i ? "✓ " : "   ") + Convert.ToString(_cb.Items[i]);
                var item = new ToolStripMenuItem(label);
                item.Click += delegate { SelectedIndex = idx; };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { _open = false; Invalidate(); };
            _open = true;
            menu.Show(this, new Point(0, Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Palette.FillRounded(g, _hover || _open ? Palette.Hover : Palette.PanelAlt, r, 8);
            Palette.DrawRounded(g, _open ? Palette.Accent : Palette.Line, r, 8, 1f);
            TextRenderer.DrawText(g,SelectedText,UiKit.CachedFont("Microsoft YaHei UI",DesignTokens.Caption,FontStyle.Regular),new Rectangle(Dpi.D(this,8),0,Math.Max(1,Width-Dpi.D(this,32)),Height),Palette.Text,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding);
            // 下拉箭头
            int cx = Width - 15, cy = Height / 2 - 1;
            using (var pen = new Pen(Palette.TextFaint, 1.4f))
            {
                g.DrawLine(pen, cx - 4, cy - 2, cx, cy + 2);
                g.DrawLine(pen, cx, cy + 2, cx + 4, cy - 2);
            }
        }
    }

    /// <summary>下拉菜单暗色渲染器。</summary>
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private readonly Palette _p;
        public DarkMenuRenderer(Palette p) : base(new DarkMenuColors(p)) { _p = p; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? _p.Text : _p.TextDim;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(_p.Panel)) e.Graphics.FillRectangle(b, e.ToolStrip.ClientRectangle);
        }
    }

    internal class DarkMenuColors : ProfessionalColorTable
    {
        private readonly Palette _p;
        public DarkMenuColors(Palette p) { _p = p; }
        public override Color MenuItemSelected { get { return _p.Hover; } }
        public override Color MenuBorder { get { return _p.Line; } }
        public override Color ToolStripDropDownBackground { get { return _p.Panel; } }
        public override Color ImageMarginGradientBegin { get { return _p.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return _p.Panel; } }
        public override Color ImageMarginGradientEnd { get { return _p.Panel; } }
        public override Color SeparatorDark { get { return _p.Line; } }
        public override Color SeparatorLight { get { return _p.Line; } }
    }

    public static class UiKit
    {
        private static readonly Dictionary<string,Font> FontCache=new Dictionary<string,Font>();
        // Borrowed fonts live for the application lifetime; callers must not dispose them.
        public static Font CachedFont(string family,float size,FontStyle style)
        {
            string key=family+"|"+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+(int)style;
            lock(FontCache){Font font;if(!FontCache.TryGetValue(key,out font)){font=new Font(family,size,style);FontCache.Add(key,font);}return font;}
        }
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                Clamp(a.A + (b.A - a.A) * t),
                Clamp(a.R + (b.R - a.R) * t),
                Clamp(a.G + (b.G - a.G) * t),
                Clamp(a.B + (b.B - a.B) * t));
        }

        private static int Clamp(double v)
        {
            if (v < 0) return 0;
            if (v > 255) return 255;
            return (int)Math.Round(v);
        }

        public static string Ellipsis(Graphics g, string text, Font f, int width)
        {
            return Palette.Ellipsis(g, text, f, width);
        }

        public static void DrawCard(Graphics g, Palette p, Rectangle r, int radius)
        {
            Palette.FillRounded(g, Color.FromArgb(p.IsLight ? 240 : 226, p.CardBg), r, radius);
            Palette.DrawRounded(g, p.Line, r, radius, 1f);
        }

        public static void DrawChip(Graphics g, Palette p, string text, int x, int y, Color color, Font f)
        {
            var sz = TextRenderer.MeasureText(text,f,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
            var r = new Rectangle(x, y, (int)sz.Width + 14, (int)sz.Height + 4);
            Palette.FillRounded(g, Color.FromArgb(40, color), r, 8);
            TextRenderer.DrawText(g,text,f,r,color,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix);
        }

        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }

        public static string HumanTime(long ms)
        {
            if (ms <= 0) return "";
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            var span = DateTime.Now - dt;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalMinutes < 60) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalHours < 24) return (int)span.TotalHours + " 小时前";
            if (span.TotalDays < 8) return (int)span.TotalDays + " 天前";
            return dt.ToString("yyyy-MM-dd");
        }
    }
}
