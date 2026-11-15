using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DeskBox.UI
{
    internal sealed class DesktopActionCard : DarkButton
    {
        private readonly string _glyph, _detail;

        public DesktopActionCard(string name, string glyph, string detail) : base(name)
        {
            _glyph = glyph;
            _detail = detail;
            AccessibleDescription = detail;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool outline = string.IsNullOrEmpty(_detail);
            bool hover = ClientRectangle.Contains(PointToClient(Cursor.Position));
            var color = hover ? Palette.Hover : outline ? Palette.CardBg : Palette.PanelAlt;
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Palette.FillRounded(g, color, bounds, 8);
            Palette.DrawRounded(g, outline ? Palette.Accent : Palette.Line, bounds, 8, 1);
            var textFormat = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
            int iconX = 12;
            TextRenderer.DrawText(g, _glyph, UiKit.CachedFont("Segoe MDL2 Assets", 11, FontStyle.Regular),
                new Rectangle(iconX, 0, 25, Height), Palette.Accent, textFormat);
            int x = 42, width = Math.Max(0, Width - x - 8);
            int titleY = outline ? 0 : Math.Max(3, Height / 2 - 20);
            int titleHeight = outline ? Height : 23;
            TextRenderer.DrawText(g, Text, UiKit.CachedFont("Microsoft YaHei UI", 10, FontStyle.Bold),
                new Rectangle(x, titleY, width, titleHeight), outline ? Palette.Accent : Palette.Text, textFormat);
            if (!outline)
                TextRenderer.DrawText(g, _detail, UiKit.CachedFont("Microsoft YaHei UI", 8.5f, FontStyle.Regular),
                    new Rectangle(x, titleY + 23, width, 19), Palette.TextDim, textFormat);
            if (Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(bounds, -4, -4), Palette.Text, color);
        }
    }
}
