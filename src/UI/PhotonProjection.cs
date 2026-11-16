using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DeskBox.UI
{
    // The same reference silhouette is rebuilt from discrete light samples.
    // Geometry sampling is cached; only a small premultiplied particle canvas changes per frame.
    internal static class PhotonProjection
    {
        private struct Photon
        {
            public float X, Y, Phase, Depth;
            public byte Light;
            public bool Edge;
        }

        private static Photon[] photons;
        private static Bitmap frame;
        private static byte[] pixels;
        private static int width, height;
        public static int PointCount { get { return photons == null ? 0 : photons.Length; } }

        private static void Sample(Bitmap source)
        {
            var values = new List<Photon>();
            var bits = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var data = new byte[bits.Stride * source.Height];
                Marshal.Copy(bits.Scan0, data, 0, data.Length);
                for (int y = 3; y < source.Height - 4; y += 5)
                    for (int x = 3; x < source.Width - 4; x += 5)
                    {
                        int offset = y * bits.Stride + x * 4;
                        if (data[offset + 3] < 65) continue;
                        uint seed = unchecked((uint)(x * 73856093) ^ (uint)(y * 19349663));
                        float phase = (seed % 65521) / 65521f;
                        float luminance = (data[offset] * .072f + data[offset + 1] * .715f + data[offset + 2] * .213f) / 255;
                        bool edge = data[offset - 12 + 3] < 80 || data[offset + 12 + 3] < 80 ||
                            data[offset - bits.Stride * 3 + 3] < 80 || data[offset + bits.Stride * 3 + 3] < 80;
                        values.Add(new Photon {
                            X = (x + (phase - .5f) * 2) / source.Width,
                            Y = y / (float)source.Height,
                            Phase = phase,
                            Depth = (float)Math.Cos((x / (float)source.Width - .5f) * Math.PI) * .6f + phase * .4f,
                            Light = (byte)(15 + Math.Pow(luminance, 2.4) * 240), Edge = edge,
                        });
                    }
            }
            finally { source.UnlockBits(bits); }
            photons = values.ToArray();
        }

        public static void Draw(Graphics graphics, RectangleF body, Bitmap source, double seconds,
            float signal, float formation, float strength, bool reduced, bool paused, bool speaking, bool muted,HologramDynamics response = null)
        {
            if (photons == null) Sample(source);
            int padding = Math.Max(12, (int)(body.Width * .12f));
            int nextWidth = Math.Max(1, (int)body.Width + padding * 2);
            int nextHeight = Math.Max(1, (int)(body.Height * 1.12f) + padding * 2);
            if (frame == null || width != nextWidth || height != nextHeight)
            {
                if (frame != null) frame.Dispose();
                width = nextWidth; height = nextHeight;
                frame = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                pixels = new byte[width * height * 4];
            }
            Array.Clear(pixels, 0, pixels.Length);
            float time = reduced || paused ? 0 : (float)seconds;
            float motion = reduced || paused ? 0 : 1;
            float flow=motion==0 ? 0 : response==null ? time*.3f : response.Flow;
            float energy = motion==0 ? 0 : Math.Max(0, Math.Min(1, signal));
            float talking=response==null ? (speaking ? 1 : 0) : response.Speaking;
            float amount = reduced ? 1 : Math.Max(0, Math.Min(1, formation));
            float assembling = 1 - amount;
            float scan = time * .14f % 1;
            for (int i = 0; i < photons.Length; i++)
            {
                var point = photons[i];
                float reveal = Math.Max(0, Math.Min(1, (amount - (1 - point.Y) * .72f) * 4));
                if (reveal <= 0) continue;
                float wave = (float)Math.Sin(flow * 2.1f + point.Phase * 6.28f);
                float x = padding + point.X * body.Width;
                float y = padding + point.Y * body.Height;
                bool face = point.Y < .24f && point.Y > .07f && Math.Abs(point.X - .5f) < .15f;
                x += (float)Math.Sin(flow*1.8f + point.Y * 5) * point.Depth * body.Width * (face ? .001f : .004f+energy*.006f) * motion;
                x += (point.Phase - .5f) * body.Width * assembling * .22f;
                y += (1 - point.Y) * body.Height * assembling * .7f;
                // The central face stays stable; motion is shallow and never rotates the illustration.
                float outer = Math.Abs(point.X - .5f) * 2;
                x += outer * wave * energy * body.Width*.009f * motion;
                float sweep = reduced ? 0 : Math.Max(0, 1 - Math.Abs(point.Y - scan) * 24);
                float shimmer = reduced ? 1 : .88f + .12f * (float)Math.Sin(time * 1.1f + point.Phase * 24);
                float intensity = (point.Light / 255f) * shimmer * (.65f + strength * .45f + energy * .42f) + sweep * strength * .24f;
                int alpha = (int)(Math.Min(1, intensity) * (point.Edge ? 255 : 240) * reveal);
                int hue=face ? 34 : (int)(point.Y*48+point.Depth*46+point.Phase*27+talking*18);
                if(point.Edge && i%5==0)hue=175+(int)(point.Y*38);
                var tint=HologramPalette.At(hue);
                int red = muted ? 135 : tint.R;
                int green = muted ? 169 : tint.G;
                int blue = muted ? 200 : tint.B;
                Dot(x, y, red, green, blue, (int)(alpha * 1.85f));
                {
                    int glow = (int)(alpha * (.16f + strength * .20f));
                    Add((int)x - 1, (int)y, red, green, blue, glow);
                    Add((int)x + 1, (int)y, red, green, blue, glow);
                    Add((int)x, (int)y - 1, red, green, blue, glow);
                    Add((int)x, (int)y + 1, red, green, blue, glow);
                }
                if (i % 67 == 0)
                {
                    Dot(x, y, 225, 245, 255, alpha);
                    Add((int)x - 2, (int)y, red, green, blue, alpha / 5);
                    Add((int)x + 2, (int)y, red, green, blue, alpha / 5);
                    Add((int)x, (int)y - 2, red, green, blue, alpha / 5);
                    Add((int)x, (int)y + 2, red, green, blue, alpha / 5);
                }
                if (i % 7 == 0)
                    Dot(padding + point.X * body.Width, padding + body.Height + (1 - point.Y) * body.Height * .09f,
                        red, green, blue, (int)(alpha * .10f * point.Y));
            }
            if (!reduced)
                for (int i = 0; i < 85; i++)
                {
                    float phase = (i * .618034f + time*.012f+flow * .075f) % 1;
                    float angle = i * 2.39996f + flow * .28f;
                    float radius = body.Width * (.34f + phase * .16f);
                    float x = padding + body.Width / 2 + (float)Math.Cos(angle) * radius;
                    float y = padding + body.Height * (1 - phase);
                    int alpha = (int)(Math.Sin(phase * Math.PI) * 95);
                    var tint=HologramPalette.At(i%4==0 ? 205 : i%2==0 ? 17 : 131);
                    Dot(x, y, tint.R, tint.G, tint.B, alpha);
                }
            var bits = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length); }
            finally { frame.UnlockBits(bits); }
            graphics.DrawImageUnscaled(frame, (int)body.Left - padding, (int)body.Top - padding);
        }

        private static void Dot(float x, float y, int red, int green, int blue, int alpha)
        {
            int left = (int)x, top = (int)y;
            float dx = x - left, dy = y - top;
            Add(left, top, red, green, blue, (int)(alpha * (1 - dx) * (1 - dy)));
            Add(left + 1, top, red, green, blue, (int)(alpha * dx * (1 - dy)));
            Add(left, top + 1, red, green, blue, (int)(alpha * (1 - dx) * dy));
            Add(left + 1, top + 1, red, green, blue, (int)(alpha * dx * dy));
        }

        private static void Add(int x, int y, int red, int green, int blue, int alpha)
        {
            if (x < 0 || y < 0 || x >= width || y >= height || alpha <= 0) return;
            int offset = (y * width + x) * 4;
            pixels[offset] = (byte)Math.Min(255, pixels[offset] + (blue * alpha >> 8));
            pixels[offset + 1] = (byte)Math.Min(255, pixels[offset + 1] + (green * alpha >> 8));
            pixels[offset + 2] = (byte)Math.Min(255, pixels[offset + 2] + (red * alpha >> 8));
            pixels[offset + 3] = (byte)Math.Min(255, pixels[offset + 3] + alpha);
        }
    }
}
