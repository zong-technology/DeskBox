using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DeskBox.UI
{
    // Native 2.5D illustration projection. No browser, Node runtime or model rig is required.
    internal static class SilverwingProjection
    {
        private static readonly Bitmap Character = Load();
        private static Bitmap scaledCharacter;

        private static Bitmap Load()
        {
            using (var stream = typeof(SilverwingProjection).Assembly.GetManifestResourceStream("DeskBox.Silverwing"))
            {
                if (stream == null) return null;
                using (var image = Image.FromStream(stream)) return new Bitmap(image);
            }
        }

        public static void Draw(Graphics g, Rectangle area, double level, bool speaking, bool muted,
            bool paused, bool reduced, double seconds, float formation, float strength, bool particles = false)
        {
            if (area.Width < 4 || area.Height < 4) return;
            var saved = g.Save();
            g.SetClip(area, CombineMode.Intersect);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float time = reduced || paused ? 0 : (float)seconds;
            float energy = reduced || paused ? 0 : (float)Math.Max(0, Math.Min(1, level));
            Color accent = muted ? Color.FromArgb(130, 150, 184) : speaking
                ? Color.FromArgb(193, 167, 255) : Color.FromArgb(134, 205, 242);
            float h = Math.Min(area.Height * .88f, area.Width * 1.08f);
            float w = h * 2 / 3, cx = area.Left + area.Width / 2f;
            float top = area.Top + area.Height * .015f;
            float floor = top + h;
            float sway = (float)Math.Sin(time * .65) * h * .0015f;
            var body = new RectangleF(cx - w / 2, top + sway, w, h);
            body = Rectangle.Round(body);
            var image = particles ? null : AtSize((int)body.Width, (int)body.Height);

            // Low-contrast ambient light stays behind the face and gown.
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(cx - h * .48f, top, h * .96f, h);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb(28, 99, 79, 154);
                    glow.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(glow, path);
                }
            }
            float halo = h * .70f;
            var circle = new RectangleF(cx - halo / 2, top + h * .14f, halo, halo);
            using (var pen = new Pen(Color.FromArgb(48, accent), Math.Max(1, h / 1000)))
            {
                g.DrawArc(pen, circle, 18 + time * 1.5f, 132);
                g.DrawArc(pen, circle, 196 + time * 1.5f, 123);
                for (int i = 0; i < 60; i++)
                {
                    double a = i * Math.PI / 30;
                    float r = halo / 2 + (i % 5 == 0 ? 7 : 3);
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * (halo / 2 + 1), circle.Top + halo / 2 + (float)Math.Sin(a) * (halo / 2 + 1),
                        cx + (float)Math.Cos(a) * r, circle.Top + halo / 2 + (float)Math.Sin(a) * r);
                }
            }
            // Sparse round motes, confined to the sides of the silhouette.
            using (var mote = new SolidBrush(Color.FromArgb(85, accent)))
                for (int i = 0; i < 34; i++)
                {
                    float x = cx + (i % 2 == 0 ? -1 : 1) * h * (.30f + (i * .173f % .17f));
                    float y = top + h * (i * .137f % .95f) + (float)Math.Sin(time * .5 + i) * 2;
                    g.FillEllipse(mote, x, y, i % 5 == 0 ? 2 : 1, i % 5 == 0 ? 2 : 1);
                }

            float baseW = h * .93f, baseH = h * .135f;
            var emitter = new RectangleF(cx - baseW / 2, floor - baseH * .35f, baseW, baseH);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(emitter);
                using (var glow = new PathGradientBrush(path))
                {
                    glow.CenterColor = Color.FromArgb((int)(28 + energy * 28), accent);
                    glow.SurroundColors = new[] { Color.Transparent };
                    g.FillPath(glow, path);
                }
            }
            if (image != null)
            {
                // Reflection is compressed onto the floor and fades away from the feet.
                for (int i = 0; i < 12; i++)
                {
                    float sourceH = image.Height / 12f;
                    var source = new RectangleF(0, image.Height - sourceH * (i + 1), image.Width, sourceH);
                    var destination = new RectangleF(body.Left, floor + h * .10f * i / 12, w, h * .10f / 12 + 1);
                    using (var opacity = Attributes(.09f * (1 - i / 12f)))
                        g.DrawImage(image, Rectangle.Round(destination), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, opacity);
                }
            }
            for (int i = 0; i < 4; i++)
            {
                var ring = emitter;
                ring.Inflate(-i * baseW * .065f, -i * baseH * .065f);
                using (var pen = new Pen(Color.FromArgb(i == 1 ? 150 : 65, accent), i == 1 ? 1.4f : 1))
                {
                    g.DrawArc(pen, ring, i * 52 + time * (i % 2 == 0 ? 2 : -3), i == 1 ? 290 : 340);
                    if (i == 1)
                    {
                        pen.Color = Color.FromArgb(40, accent);
                        ring.Y += baseH * .18f;
                        g.DrawEllipse(pen, ring);
                    }
                }
            }
            using (var pen = new Pen(Color.FromArgb((int)(80 + energy * 130), accent), Math.Max(1, h * .0018f)))
                for (int i = 0; i < 64; i++)
                {
                    double a = i * Math.PI / 32;
                    float x = cx + (float)Math.Cos(a) * baseW * .435f;
                    float y = emitter.Top + baseH / 2 + (float)Math.Sin(a) * baseH * .435f;
                    float height = h * (.002f + energy * (.012f + .016f * (float)(.5 + .5 * Math.Sin(i * 1.7))));
                    g.DrawLine(pen, x, y, x, y - height);
                }

            if (particles && Character != null)
                PhotonProjection.Draw(g, body, Character, seconds, (float)level, formation, strength, reduced, paused, speaking, muted);
            else if (image != null)
            {
                var revealClip = g.Save();
                float amount = reduced ? 1 : Math.Max(0, Math.Min(1, formation));
                g.SetClip(new RectangleF(body.Left - 4, body.Bottom - body.Height * amount - 2, body.Width + 8, body.Height * amount + 5), CombineMode.Intersect);
                // Tinted offsets trace the original alpha mask. The normal art keeps facial detail intact.
                using (var edge = Attributes(.13f * strength, accent))
                {
                    var outline = body; outline.X -= 1.4f;
                    g.DrawImage(image, Rectangle.Round(outline), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, edge);
                    outline.X += 2.8f;
                    g.DrawImage(image, Rectangle.Round(outline), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, edge);
                }
                using (var opacity = Attributes(1 - .035f * strength))
                    g.DrawImage(image, Rectangle.Round(body), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, opacity);
                if (!reduced && strength > 0)
                {
                    // Only the lower gown catches the passing light, below the protected face.
                    float scan = (time * .055f % 1) * .68f + .27f;
                    var strip = new RectangleF(body.Left, body.Top + h * scan, w, Math.Max(1.3f, h * .003f));
                    using (var light = Attributes(.13f * strength, accent))
                        g.DrawImage(image, Rectangle.Round(strip), 0, image.Height * scan, image.Width,
                            image.Height * strip.Height / h, GraphicsUnit.Pixel, light);
                }
                g.Restore(revealClip);
            }
            else
            {
                using (var pen = new Pen(accent, 2)) g.DrawEllipse(pen, circle);
            }
            g.Restore(saved);
        }

        // Scale once per viewport. Per-frame tinting must not resample the 1536px source repeatedly.
        private static Bitmap AtSize(int width, int height)
        {
            if (Character == null) return null;
            width = Math.Max(1, width); height = Math.Max(1, height);
            if (scaledCharacter != null && scaledCharacter.Width == width && scaledCharacter.Height == height)
                return scaledCharacter;
            var next = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(next)) {
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var attributes = new ImageAttributes()) {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(Character, new Rectangle(0, 0, width, height), 0, 0, Character.Width, Character.Height, GraphicsUnit.Pixel, attributes);
                }
            }
            if (scaledCharacter != null) scaledCharacter.Dispose();
            scaledCharacter = next;
            return scaledCharacter;
        }

        private static ImageAttributes Attributes(float alpha, Color? tint = null)
        {
            var attributes = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = alpha };
            if (tint.HasValue)
            {
                matrix.Matrix00 = matrix.Matrix11 = matrix.Matrix22 = 0;
                matrix.Matrix40 = tint.Value.R / 255f;
                matrix.Matrix41 = tint.Value.G / 255f;
                matrix.Matrix42 = tint.Value.B / 255f;
            }
            attributes.SetColorMatrix(matrix);
            return attributes;
        }
    }
}
