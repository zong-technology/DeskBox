const fs = require('fs');
const p = 'src/UI/UiKit.cs';
let s = fs.readFileSync(p, 'utf8');

// 用自绘下拉替换原生 ComboBox 的可见外观（原生 ComboBox 在暗色下会显示白色块）
const start = s.indexOf('    /// <summary>暗色下拉');
const endMarker = '    public static class UiKit';
const end = s.indexOf(endMarker);
if (start < 0 || end < 0) { console.log('MARKER NOT FOUND'); process.exit(1); }

const replacement = `    /// <summary>暗色下拉：自绘外观 + 原生 ComboBox 仅作数据容器（不显示），点击弹出菜单。</summary>
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
            using (var f = new Font("Microsoft YaHei UI", 9F))
            {
                var txt = SelectedText;
                g.DrawString(Palette.Ellipsis(g, txt, f, Width - 30), f, new SolidBrush(Palette.Text), 10, (Height - 16) / 2f);
            }
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

`;

s = s.slice(0, start) + replacement + s.slice(end);
fs.writeFileSync(p, s, 'utf8');
console.log('DarkSelect replaced:', s.includes('自绘外观') ? 'ok' : 'FAILED');
