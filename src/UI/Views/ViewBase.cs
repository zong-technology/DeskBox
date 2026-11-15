using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>视图基类：统一卡片背景、标题栏、主题刷新。</summary>
    public class ViewBase : UserControl
    {
        protected readonly Store Store;
        protected Panel Header;
        protected Label TitleLabel;
        protected Label HintLabel;
        protected Panel Body;

        public Palette Palette { get; protected set; }

        public ViewBase(Store store, string title, string hint)
        {
            Store = store;
            Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
            Dock = DockStyle.Fill;

            Header = new Panel { Dock = DockStyle.Top, Height = 66, BackColor = Palette.HeaderBg };
            TitleLabel = new Label
            {
                Text = title, UseMnemonic = false, AutoSize = false, AutoEllipsis = true, Left = 18, Top = 9, Height = 27,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Palette.Text, BackColor = Palette.HeaderBg
            };
            HintLabel = new Label
            {
                Text = hint, UseMnemonic = false, AutoSize = false, AutoEllipsis = true, Left = 18, Top = 39, Height = 20,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = Palette.TextDim, BackColor = Palette.HeaderBg
            };
            Header.Controls.Add(TitleLabel);
            Header.Controls.Add(HintLabel);
            Header.Resize += delegate { TitleLabel.Width = Math.Max(150, Header.Width - 340); HintLabel.Width = Math.Max(150, Header.Width - 36); };
            Header.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (var accent = new Pen(Palette.Accent, 2)) e.Graphics.DrawLine(accent, 6, 12, 6, 33);
                using (var border = new Pen(Palette.Line)) e.Graphics.DrawLine(border, 18, Header.Height - 1, Header.Width - 18, Header.Height - 1);
            };
            Body = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg, Padding = new Padding(6, 4, 6, 6) };
            Controls.Add(Body);
            Controls.Add(Header);
        }

        public virtual void ApplyPalette(Palette p)
        {
            Palette = p;
            BackColor = p.Bg;
            Header.BackColor = p.HeaderBg;
            Body.BackColor = p.Bg;
            TitleLabel.ForeColor = p.Text;
            TitleLabel.BackColor = p.HeaderBg;
            HintLabel.ForeColor = p.TextDim;
            HintLabel.BackColor = p.HeaderBg;
            foreach (Control c in Body.Controls) RestyleControl(c, p);
            Invalidate(true);
        }

        protected void RestyleControl(Control c, Palette p)
        {
            var btn = c as DarkButton;
            if (btn != null) { btn.ApplyPalette(p); return; }
            var input = c as DarkInput;
            if (input != null) { input.ApplyPalette(p); return; }
            var sel = c as DarkSelect;
            if (sel != null) { sel.ApplyPalette(p); return; }
            var list = c as DarkList;
            if (list != null) { list.ApplyPalette(p); return; }
            var lbl = c as Label;
            if (lbl != null) { lbl.ForeColor = p.TextDim; lbl.BackColor = c.Parent != null ? c.Parent.BackColor : p.Bg; }
            foreach (Control child in c.Controls) RestyleControl(child, p);
        }

        protected virtual void OnActivated() { }
        public virtual void UseReferenceLayout(){Header.Visible=false;Body.Padding=new Padding(0);FitLists(Body);}
        private void FitLists(Control parent){foreach(Control child in parent.Controls){var list=child as DarkList;if(list!=null){list.CardSurface=true;list.AutoFitColumns=true;list.RowHeight=Math.Max(38,list.RowHeight);}FitLists(child);}}
        public void Activate() { OnActivated(); }
    }
}
