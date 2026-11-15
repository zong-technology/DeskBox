using System;
using System.Drawing;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    internal static class VoiceSceneView
    {
        public static void Draw(Graphics g, Rectangle area, string scene, EngineerWorkspace project, Font font, double yaw, double pitch, double zoom)
        {
            var title = scene == "reactor" ? "生物反应器 · 三维示意" :
                scene == "plc" ? "PLC 通信拓扑 · 示意链路" : "当前项目进度";
            TextRenderer.DrawText(g, title, font, new Rectangle(area.X, area.Y, area.Width, 26), Color.White);
            var body = new Rectangle(area.X, area.Y + 30, area.Width, Math.Max(0, area.Height - 30));
            if (body.Height < 50) return;
            using (var line = new Pen(Color.Turquoise, 2))
            using (var fill = new SolidBrush(Color.FromArgb(23, 60, 76)))
            {
                if (scene == "reactor") Reactor(g, body, font, yaw, pitch, zoom);
                else if (scene == "plc") Plc(g, body, line, fill, font);
                else Progress(g, body, fill, project, font);
            }
        }

        private static void Reactor(Graphics g, Rectangle area, Font font, double yaw, double pitch, double zoom)
        {
            double scale = Math.Min(area.Width / 4.0, (area.Height - 20) / 5.4) * zoom;
            if (scale <= 0) return;
            double cosYaw = Math.Cos(yaw), sinYaw = Math.Sin(yaw);
            double cosPitch = Math.Cos(pitch), sinPitch = Math.Sin(pitch);
            Func<double, double, double, PointF> project = (x, y, z) =>
            {
                double horizontal = x * cosYaw - z * sinYaw;
                double depth = x * sinYaw + z * cosYaw;
                double vertical = y * cosPitch - depth * sinPitch;
                double perspective = 8 / (8 + y * sinPitch + depth * cosPitch);
                return new PointF((float)(area.X + area.Width / 2.0 + horizontal * scale * perspective),
                    (float)(area.Y + (area.Height - 20) / 2.0 - vertical * scale * perspective));
            };

            var clip = g.Save();
            g.SetClip(new Rectangle(area.X, area.Y, area.Width, area.Height - 20));
            using (var shell = new Pen(Color.FromArgb(170, 64, 221, 222), 1.6f))
            using (var seam = new Pen(Color.FromArgb(80, 64, 221, 222)))
            using (var internals = new Pen(Color.Gold, 2))
            {
                Ring(g, shell, project, 1.0, -1.12);
                Ring(g, shell, project, 1.0, 1.12);
                Ring(g, shell, project, .75, -1.45);
                Ring(g, shell, project, .75, 1.45);
                Ring(g, shell, project, .26, 1.56);
                Ring(g, shell, project, .26, 2.05);
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * Math.PI / 6;
                    double x = Math.Cos(angle), z = Math.Sin(angle);
                    g.DrawLines(seam, new[] { project(.75*x, -1.45, .75*z), project(x, -1.12, z),
                        project(x, 1.12, z), project(.75*x, 1.45, .75*z) });
                }
                for (int i = 0; i < 4; i++)
                {
                    double angle = i * Math.PI / 2, x = Math.Cos(angle), z = Math.Sin(angle);
                    g.DrawLine(shell, project(.26*x, 1.56, .26*z), project(.26*x, 2.05, .26*z));
                    g.DrawLine(shell, project(.70*x, -1.43, .70*z), project(.70*x, -1.95, .70*z));
                }
                g.DrawLine(internals, project(0, 1.56, 0), project(0, -.98, 0));
                foreach (double y in new[] { -.88, -.15 })
                {
                    g.DrawLine(internals, project(-.70, y, 0), project(.70, y, 0));
                    g.DrawLine(internals, project(0, y, -.70), project(0, y, .70));
                }
                // Side nozzle gives the rotation a fixed reference point.
                g.DrawLine(shell, project(.98, .65, 0), project(1.48, .65, 0));
                g.DrawLine(shell, project(1.48, .65, 0), project(1.48, 1.05, 0));
            }
            g.Restore(clip);
            TextRenderer.DrawText(g, "拖动旋转 · 滚轮缩放 · 双击复位 · 黄色为搅拌机构", font,
                new Rectangle(area.X, area.Bottom - 20, area.Width, 20), Color.LightGray,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void Ring(Graphics g, Pen pen, Func<double, double, double, PointF> project, double radius, double y)
        {
            var points = new PointF[37];
            for (int i = 0; i < points.Length; i++)
            {
                double angle = i * Math.PI / 18;
                points[i] = project(radius * Math.Cos(angle), y, radius * Math.Sin(angle));
            }
            g.DrawLines(pen, points);
        }

        private static void Plc(Graphics g, Rectangle a, Pen line, Brush fill, Font font)
        {
            var names = new[] { "PLC", "交换机", "远程 I/O" };
            int gap = 12, w = (a.Width - 2 * gap) / 3, y = a.Y + a.Height / 3;
            if (w < 20) return;
            for (int i = 0; i < names.Length; i++)
            {
                var node = new Rectangle(a.X + i * (w + gap), y, w, 46);
                g.FillRectangle(fill, node);
                g.DrawRectangle(line, node);
                TextRenderer.DrawText(g, names[i], font, node, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (i < names.Length - 1) g.DrawLine(line, node.Right, y + 23, node.Right + gap, y + 23);
            }
            TextRenderer.DrawText(g, "示例通信链路 · 未连接现场设备", font, new Point(a.X, y + 60), Color.LightGray);
        }

        private static void Progress(Graphics g, Rectangle a, Brush fill, EngineerWorkspace project, Font font)
        {
            if (project == null)
            {
                TextRenderer.DrawText(g, "未找到当前项目，请先在项目管理中选择。", font, a.Location, Color.LightGray);
                return;
            }
            TextRenderer.DrawText(g, project.Name + "  ·  " + ValidationProgress.Display(project), font,
                new Rectangle(a.X, a.Y, a.Width, 26), Color.White, TextFormatFlags.EndEllipsis);
            if (project.StageProgress == null) return;
            int y = a.Y + 30, row = Math.Max(12, Math.Min(24, (a.Height - 30) / Math.Max(1, project.StageProgress.Count)));
            int barWidth = Math.Max(1, a.Width - 158);
            foreach (var stage in project.StageProgress)
            {
                if (y + row > a.Bottom) break;
                double value = Math.Max(0, Math.Min(100, stage.Value));
                TextRenderer.DrawText(g, stage.Key, font, new Rectangle(a.X, y, 90, row), Color.LightGray, TextFormatFlags.EndEllipsis);
                g.FillRectangle(fill, a.X + 90, y + 3, barWidth, Math.Max(1, row - 7));
                if (!double.IsNaN(value))
                    g.FillRectangle(Brushes.Turquoise, a.X + 90, y + 3, (int)(barWidth * value / 100), Math.Max(1, row - 7));
                TextRenderer.DrawText(g, value.ToString("0") + "%", font, new Point(a.X + 95 + barWidth, y), Color.White);
                y += row;
            }
        }
    }
}
