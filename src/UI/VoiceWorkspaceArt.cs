using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    internal static class VoiceWorkspaceArt
    {
        private static readonly Color Page = Color.FromArgb(2, 7, 11);
        private static readonly Color Border = Color.FromArgb(42, 63, 73);
        private static readonly Color Text = Color.FromArgb(235, 247, 252);
        private static readonly Color Muted = Color.FromArgb(154, 178, 190);
        private static readonly Color Cyan = Color.FromArgb(104, 228, 247);
        private static readonly Color Amber = Color.FromArgb(255, 192, 112);
        private static readonly Color Red = Color.FromArgb(249, 116, 120);
        private static readonly Bitmap Presence = Load("DeskBox.VoicePresence");
        private static readonly Bitmap Probe = Load("DeskBox.VoiceProbe");

        private static Bitmap Load(string resource)
        {
            using (var stream = typeof(VoiceWorkspaceArt).Assembly.GetManifestResourceStream(resource))
                return stream == null ? null : CopyBitmap(stream);
        }

        private static Bitmap CopyBitmap(System.IO.Stream stream)
        {
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);
        }

        public static void Draw(Graphics g, Size size, bool speaking, bool muted, double level,
            string elapsed, string scene, EngineerWorkspace workspace, bool paused, bool outputMuted,
            bool captions, string transcript, double yaw, double pitch, double zoom,
            float reveal, float focus, bool reduced, Dictionary<string, Rectangle> actions)
        {
            g.Clear(Page);
            actions.Clear();
            Func<int, int, int, int, Rectangle> bounds = (x, y, w, h) =>
                new Rectangle(x * size.Width / 1920, y * size.Height / 1080,
                    Math.Max(1, w * size.Width / 1920), Math.Max(1, h * size.Height / 1080));
            float scale = Math.Max(.65f, Math.Min(1.2f, size.Width / 1920f));
            string status = paused ? "已暂停" : speaking ? "AI 正在回答" : muted ? "麦克风已关闭" : "正在聆听";
            using (var small = new Font("Microsoft YaHei UI", 12 * scale))
            using (var title = new Font(small.FontFamily, 15 * scale, FontStyle.Bold))
            using (var caption = new Font(small.FontFamily, 21 * scale))
            using (var border = new Pen(Border))
            using (var frame = Palette.RoundedRect(bounds(24, 24, 1872, 1032), 12))
            {
                g.DrawPath(border, frame);
                Label(g, "DESKBOX  LIVE  WORKBENCH", title, Text, bounds(76, 55, 605, 55), false);
                g.DrawLine(border, bounds(670, 81, 1, 1).Location, bounds(1240, 81, 1, 1).Location);
                Label(g, "●  LIVE  /  " + elapsed, small, Cyan, bounds(1260, 55, 235, 55));
                Button(g, actions, "reduced", bounds(1505, 54, 155, 48), reduced ? "标准动效" : "减少动效", small, Muted);
                Button(g, actions, "back", bounds(1690, 54, 150, 48), "返回助手", small, Muted);

                var plot = bounds(160, 135, 1600, 695);
                if (scene != "core" && reveal <= 0)
                    VoiceSceneView.Draw(g, plot, scene, workspace, small, yaw, pitch, zoom);
                else
                {
                    float pulse = reduced || paused ? 0 : (float)level;
                    int width = (int)(1400 - 740 * reveal + 110 * pulse);
                    int center = (int)(960 - 535 * reveal);
                    var body = bounds(center - width / 2, (int)(152 + 262 * reveal), width, (int)(640 - 275 * reveal));
                    if (Presence != null) DrawLayer(g, Presence, body, .84f + .16f * pulse);
                    else Label(g, "AI", title, Cyan, body);
                    if (reveal > 0 && Probe != null)
                    {
                        var item = DrawLayer(g, Probe, bounds(975, 139, 750, 670), reveal);
                        var anchor = new PointF(item.Left + item.Width * .75f, item.Top + item.Height * .30f);
                        var tip = new PointF(item.Left + item.Width * .484f, item.Top + item.Height * .93f);
                        var elbow = bounds(1480, 265, 1, 1).Location;
                        using (var lead = new Pen(Color.FromArgb((int)(235 * reveal), Amber)))
                        {
                            if (!reduced)
                            {
                                var origin = bounds(530, 624, 1, 1).Location;
                                var curve1 = bounds(750, 700, 1, 1).Location;
                                var curve2 = bounds(900, 316, 1, 1).Location;
                                using (var light = new Pen(Color.FromArgb((int)(170 * reveal), Cyan), Math.Max(1, 2 * scale)))
                                    g.DrawBezier(light, origin, curve1, curve2, anchor);
                            }
                            g.DrawLine(lead, anchor, elbow);
                            g.DrawLine(lead, elbow, bounds(1810, 265, 1, 1).Location);
                        }
                        Label(g, "温度探头 · 示意", small, Color.FromArgb((int)(255 * reveal), Text),
                            bounds(1490, 213, 330, 50), false);
                        if (focus > 0)
                        {
                            float radius = 22 * scale;
                            var target = bounds(1465, 728, 1, 1).Location;
                            using (var ring = new Pen(Color.FromArgb((int)(250 * focus), Amber), Math.Max(1, 2 * scale)))
                            {
                                g.DrawEllipse(ring, tip.X - radius, tip.Y - radius, radius * 2, radius * 2);
                                g.DrawLine(ring, tip.X + radius, tip.Y, target.X, target.Y);
                            }
                            Label(g, "感温端", small, Color.FromArgb((int)(255 * focus), Amber), bounds(1485, 700, 250, 48), false);
                        }
                    }
                }

                Label(g, captions && !string.IsNullOrWhiteSpace(transcript) ? transcript : status,
                    caption, Text, bounds(175, 858, 1570, 67));
                g.DrawLine(border, bounds(86, 940, 1, 1).Location, bounds(1834, 940, 1, 1).Location);
                Button(g, actions, "mic", bounds(585, 966, 144, 55), muted ? "开启麦克风" : "麦克风", small, Cyan);
                Button(g, actions, "pause", bounds(737, 966, 144, 55), paused ? "恢复" : "暂停", small, Muted);
                Button(g, actions, "caption", bounds(889, 966, 144, 55), captions ? "字幕 · 开" : "字幕 · 关", small, captions ? Cyan : Muted);
                Button(g, actions, "end", bounds(1041, 966, 144, 55), "结束", small, Red);
                Button(g, actions, "speaker", bounds(1290, 966, 200, 55), outputMuted ? "扬声器 · 关" : "扬声器 · 开", small, Muted);
                Button(g, actions, "visual", bounds(1510, 966, 140, 55), "场景", small, Muted);
            }
        }

        private static Rectangle DrawLayer(Graphics g, Bitmap image, Rectangle area, float opacity)
        {
            float scale = Math.Min(area.Width / (float)image.Width, area.Height / (float)image.Height);
            int w = Math.Max(1, (int)(image.Width * scale)), h = Math.Max(1, (int)(image.Height * scale));
            var target = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
                g.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
            return target;
        }

        private static void Button(Graphics g, Dictionary<string, Rectangle> actions,
            string id, Rectangle area, string text, Font font, Color color)
        {
            actions[id] = area;
            if (area.Width < 4 || area.Height < 4) return;
            using (var outline = Palette.RoundedRect(area, 12))
            using (var pen = new Pen(Color.FromArgb(165, color))) g.DrawPath(pen, outline);
            Label(g, text, font, color, area);
        }

        private static void Label(Graphics g, string text, Font font, Color color, Rectangle area, bool centered = true)
        {
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = centered ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(text ?? "", font, brush, area, format);
        }
    }
}
