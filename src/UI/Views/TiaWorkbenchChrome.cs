using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DeskBox.UI.Views
{
    internal static class TiaWorkbenchStyle
    {
        internal const int RailGap = 6;
        internal const int ContextHeight = 54;
        internal const int ToolbarHeight = 40;
        internal const int ControlHeight = 36;
        internal const int CompactWidth = 1080;
        internal const int MinimumCodeWidth = 420;
        internal const int MinimumOutputHeight = 84;
        private static readonly int systemDpi = ReadSystemDpi();

        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();

        private static int ReadSystemDpi()
        {
            try { return (int)GetDpiForSystem(); }
            catch (EntryPointNotFoundException) { return 96; }
        }

        internal static int LogicalDpi(Control view) { return Math.Max(systemDpi, view.DeviceDpi); }
        internal static int Pixels(Control view, int value)
        {
            return (int)Math.Round(value * LogicalDpi(view) / 96.0, MidpointRounding.AwayFromZero);
        }

        internal static Color EditorBackground(Palette palette) { return palette.IsLight ? palette.CardBg : UiKit.Mix(palette.Bg, palette.CardBg, 0.28); }
        internal static Color Success(Palette palette) { return palette.IsLight ? Color.FromArgb(0, 109, 77) : Color.FromArgb(83, 216, 172); }
        internal static Color Warning(Palette palette) { return palette.IsLight ? Color.FromArgb(147, 92, 0) : Color.FromArgb(255, 197, 100); }
        internal static Color Error(Palette palette) { return palette.IsLight ? Color.FromArgb(162, 42, 59) : Color.FromArgb(255, 126, 145); }
    }

    internal sealed class TiaWorkbenchTabs : TabControl
    {
        internal Palette Colors = Palette.For("cyber", null);

        internal TiaWorkbenchTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SizeMode = TabSizeMode.Fixed;
            Padding = new Point(0, 0);
            SelectedIndexChanged += delegate { Invalidate(); };
        }

        internal void FitLabels()
        {
            int width = TiaWorkbenchStyle.Pixels(this, 74);
            foreach (TabPage page in TabPages)
            {
                int measured = TextRenderer.MeasureText(page.Text, Font, Size.Empty,
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                width = Math.Max(width, measured + TiaWorkbenchStyle.Pixels(this, 20));
            }
            ItemSize = new Size(width, TiaWorkbenchStyle.Pixels(this, 29));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Colors.HeaderBg);
            for (int index = 0; index < TabCount; index++)
            {
                var box = GetTabRect(index);
                bool active = index == SelectedIndex;
                using (var fill = new SolidBrush(active ? UiKit.Mix(Colors.PanelAlt, Colors.Accent, 0.13) : Colors.HeaderBg))
                    e.Graphics.FillRectangle(fill, box);
                if (active)
                    using (var pen = new Pen(Colors.Accent, TiaWorkbenchStyle.Pixels(this, 2)))
                        e.Graphics.DrawLine(pen, box.Left + 1, box.Bottom - 2, box.Right - 2, box.Bottom - 2);
                TextRenderer.DrawText(e.Graphics, TabPages[index].Text, Font, box, active ? Colors.Text : Colors.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (active && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(box, -3, -3));
            }
        }
    }
}
