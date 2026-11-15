using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace DeskBox.UI
{
    /// <summary>主题色板：cyber / midnight / aurora / daylight + 强调色。</summary>
    public class Palette
    {
        public Color Bg, Bg2, Panel, PanelAlt, Line, Text, TextDim, TextFaint, Hover, Accent, AccentSoft, CardBg, HeaderBg, Shadow;

        /// <summary>是否浅色主题（用于半透明底板/描边的明暗选择）</summary>
        public bool IsLight { get; private set; }

        public static Palette For(string theme, string accentHex)
        {
            var p = new Palette();
            var accent = ParseColor(accentHex, Color.FromArgb(110, 231, 255));
            p.Accent = accent;
            p.AccentSoft = Color.FromArgb(46, accent);

            switch ((theme ?? "midnight").ToLowerInvariant())
            {
                case "cyber":
                    p.Bg = Color.FromArgb(8, 15, 28);
                    p.Bg2 = Color.FromArgb(13, 23, 39);
                    p.Panel = Color.FromArgb(17, 30, 49);
                    p.PanelAlt = Color.FromArgb(22, 36, 58);
                    p.Line = Color.FromArgb(31, 58, 84);
                    p.Text = Color.FromArgb(237, 245, 255);
                    p.TextDim = Color.FromArgb(141, 163, 191);
                    p.TextFaint = Color.FromArgb(99, 122, 153);
                    p.Hover = Color.FromArgb(24, 55, 78);
                    p.CardBg = Color.FromArgb(17, 30, 49);
                    p.HeaderBg = Color.FromArgb(7, 14, 25);
                    p.Shadow = Color.FromArgb(130, 0, 5, 12);
                    break;
                case "aurora":
                    p.Bg = Color.FromArgb(6, 26, 30);
                    p.Bg2 = Color.FromArgb(9, 40, 44);
                    p.Panel = Color.FromArgb(12, 34, 38);
                    p.PanelAlt = Color.FromArgb(16, 44, 48);
                    p.Line = Color.FromArgb(30, 70, 74);
                    p.Text = Color.FromArgb(226, 245, 242);
                    p.TextDim = Color.FromArgb(140, 178, 172);
                    p.TextFaint = Color.FromArgb(96, 130, 126);
                    p.Hover = Color.FromArgb(26, 64, 66);
                    p.CardBg = Color.FromArgb(14, 40, 44);
                    p.HeaderBg = Color.FromArgb(10, 30, 34);
                    p.Shadow = Color.FromArgb(90, 0, 0, 0);
                    break;
                case "daylight":
                    p.IsLight = true;
                    p.Bg = Color.FromArgb(238, 242, 249);
                    p.Bg2 = Color.FromArgb(226, 233, 244);
                    p.Panel = Color.FromArgb(252, 253, 255);
                    p.PanelAlt = Color.FromArgb(244, 247, 252);
                    p.Line = Color.FromArgb(210, 219, 232);
                    p.Text = Color.FromArgb(27, 36, 51);
                    p.TextDim = Color.FromArgb(96, 110, 130);
                    p.TextFaint = Color.FromArgb(140, 152, 170);
                    p.Hover = Color.FromArgb(232, 238, 248);
                    p.CardBg = Color.FromArgb(255, 255, 255);
                    p.HeaderBg = Color.FromArgb(246, 249, 254);
                    p.Shadow = Color.FromArgb(40, 60, 90, 120);
                    break;
                default:
                    p.Bg = Color.FromArgb(9, 13, 22);
                    p.Bg2 = Color.FromArgb(14, 20, 33);
                    p.Panel = Color.FromArgb(17, 23, 37);
                    p.PanelAlt = Color.FromArgb(22, 29, 45);
                    p.Line = Color.FromArgb(38, 48, 68);
                    p.Text = Color.FromArgb(230, 237, 247);
                    p.TextDim = Color.FromArgb(139, 154, 179);
                    p.TextFaint = Color.FromArgb(96, 110, 133);
                    p.Hover = Color.FromArgb(28, 37, 56);
                    p.CardBg = Color.FromArgb(20, 27, 43);
                    p.HeaderBg = Color.FromArgb(14, 20, 33);
                    p.Shadow = Color.FromArgb(110, 0, 0, 0);
                    break;
            }
            return p;
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return fallback;
                var s = hex.Trim().TrimStart('#');
                if (s.Length == 6)
                    return Color.FromArgb(255, Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16));
                if (s.Length == 8)
                    return Color.FromArgb(Convert.ToInt32(s.Substring(6, 2), 16),
                        Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch { }
            return fallback;
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (r.Width <= 0 || r.Height <= 0) { path.AddRectangle(r); return path; }
            d = Math.Min(d, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, Rectangle r, int radius)
        {
            if (r.Width <= 2 || r.Height <= 2) { using (var b0 = new SolidBrush(color)) g.FillRectangle(b0, r); return; }
            using (var path = RoundedRect(r, radius))
            using (var b = new SolidBrush(color))
                g.FillPath(b, path);
        }

        public static void DrawRounded(Graphics g, Color color, Rectangle r, int radius, float width)
        {
            if (r.Width <= 2 || r.Height <= 2) return;
            using (var path = RoundedRect(r, radius))
            using (var p = new Pen(color, width))
                g.DrawPath(p, path);
        }

        /// <summary>按宽度折行 + 省略号</summary>
        public static string Ellipsis(Graphics g, string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (g.MeasureString(text, font).Width <= maxWidth) return text;
            var sb = text;
            while (sb.Length > 1 && g.MeasureString(sb + "…", font).Width > maxWidth) sb = sb.Substring(0, sb.Length - 1);
            return sb + "…";
        }

        /// <summary>折成最多 maxLines 行</summary>
        public static string[] Wrap(Graphics g, string text, Font font, int maxWidth, int maxLines)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (string.IsNullOrEmpty(text)) return lines.ToArray();
            var cur = "";
            foreach (char ch in text)
            {
                if (g.MeasureString(cur + ch, font).Width > maxWidth)
                {
                    lines.Add(cur);
                    cur = ch.ToString();
                    if (lines.Count == maxLines) break;
                }
                else cur += ch;
            }
            if (lines.Count < maxLines && cur.Length > 0) lines.Add(cur);
            if (lines.Count == maxLines)
            {
                var last = lines[lines.Count - 1];
                var full = string.Concat(lines);
                if (full.Length < text.Length)
                {
                    while (last.Length > 1 && g.MeasureString(last + "…", font).Width > maxWidth) last = last.Substring(0, last.Length - 1);
                    lines[lines.Count - 1] = last + "…";
                }
            }
            return lines.ToArray();
        }
    }
}
