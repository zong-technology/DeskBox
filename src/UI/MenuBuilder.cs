using System;
using System.Drawing;
using System.Windows.Forms;

namespace DeskBox.UI
{
    /// <summary>暗色主题的菜单与弹窗工具。</summary>
    public static class MenuBuilder
    {
        public static ContextMenuStrip Create(Palette p)
        {
            var m = new ContextMenuStrip
            {
                Renderer = new DarkRenderer(p),
                BackColor = p.Panel,
                ForeColor = p.Text,
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false,
                Padding = new Padding(4)
            };
            return m;
        }

        public static ToolStripMenuItem Item(string text, Action onClick)
        {
            var it = new ToolStripMenuItem(text);
            if (onClick != null) it.Click += (s, e) => onClick();
            return it;
        }

        private class DarkRenderer : ToolStripProfessionalRenderer
        {
            private readonly Palette _p;
            public DarkRenderer(Palette p) : base(new DarkColors(p)) { _p = p; }

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

        private class DarkColors : ProfessionalColorTable
        {
            private readonly Palette _p;
            public DarkColors(Palette p) { _p = p; }
            public override Color MenuItemSelected { get { return _p.Hover; } }
            public override Color MenuItemBorder { get { return _p.Accent; } }
            public override Color MenuBorder { get { return _p.Line; } }
            public override Color ToolStripDropDownBackground { get { return _p.Panel; } }
            public override Color ImageMarginGradientBegin { get { return _p.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return _p.Panel; } }
            public override Color ImageMarginGradientEnd { get { return _p.Panel; } }
            public override Color SeparatorDark { get { return _p.Line; } }
            public override Color SeparatorLight { get { return _p.Line; } }
        }
    }

    /// <summary>简易输入 / 确认弹窗（不依赖 Microsoft.VisualBasic）。</summary>
    public static class Prompt
    {
        public static string Input(IWin32Window owner, string title, string label, string def, Palette p)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.ClientSize = new Size(380, 150);
                f.BackColor = p.Panel; f.ForeColor = p.Text;
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label { Text = label, Left = 18, Top = 18, Width = 340, ForeColor = p.TextDim };
                var tb = new TextBox { Left = 18, Top = 44, Width = 344, Text = def, BackColor = p.PanelAlt, ForeColor = p.Text, BorderStyle = BorderStyle.FixedSingle };
                var ok = new Button { Text = "确定", Left = 200, Top = 90, Width = 76, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = p.Accent };
                var cancel = new Button { Text = "取消", Left = 286, Top = 90, Width = 76, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = p.PanelAlt, ForeColor = p.TextDim };
                ok.FlatAppearance.BorderSize = 0;
                cancel.FlatAppearance.BorderColor = p.Line;
                ok.ForeColor = Color.FromArgb(12, 18, 28);

                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                tb.SelectAll();
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }

        public static bool Confirm(IWin32Window owner, string title, string message, Palette p)
        {
            using (var f = new Form())
            {
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = false; f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.ClientSize = new Size(400, 160);
                f.BackColor = p.Panel; f.ForeColor = p.Text;
                f.Font = new Font("Microsoft YaHei UI", 9F);

                var lbl = new Label { Text = message, Left = 18, Top = 20, Width = 360, Height = 60, ForeColor = p.Text };
                var ok = new Button { Text = "确定", Left = 220, Top = 100, Width = 76, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = p.Accent, ForeColor = Color.FromArgb(12, 18, 28) };
                var cancel = new Button { Text = "取消", Left = 306, Top = 100, Width = 76, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = p.PanelAlt, ForeColor = p.TextDim };
                ok.FlatAppearance.BorderSize = 0;
                cancel.FlatAppearance.BorderColor = p.Line;

                f.Controls.AddRange(new Control[] { lbl, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK;
            }
        }
    }
}
