using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI.Views
{
    public class ProjectHealthControl : Control
    {
        private ProjectHealthSnapshot _snapshot;
        public Palette Palette { get; set; }
        public string Presentation { get; set; }

        public ProjectHealthControl(Palette palette)
        {
            Palette = palette;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = palette.CardBg;
        }

        public void SetSnapshot(ProjectHealthSnapshot snapshot) { _snapshot = snapshot; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Palette.CardBg);
            if (_snapshot == null || string.IsNullOrEmpty(_snapshot.ProjectId))
            {
                using (var f = new Font("Microsoft YaHei UI", 9F)) using (var b = new SolidBrush(Palette.TextDim)) g.DrawString("尚未设置当前项目。", f, b, 0, 8);
                return;
            }

            if (Presentation == "dependency") { DrawDependency(g); return; }
            if (Presentation == "focus") { DrawFocus(g); return; }
            if (Presentation == "summary") { DrawSummary(g); return; }
            if (Height >= 240 && Width >= 750) { DrawMission(g); return; }

            var ring = new Rectangle(4, 8, 86, 86);
            using (var p0 = new Pen(Palette.Line, 8F)) g.DrawArc(p0, ring, -90, 360);
            using (var p = new Pen(_snapshot.Health == "ON TRACK" ? Color.FromArgb(43, 217, 164) : Color.FromArgb(255, 195, 92), 8F)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, ring, -90, (float)(360 * Math.Max(0, Math.Min(100, _snapshot.OverallProgress)) / 100.0)); }
            using (var n = new Font("Consolas", 15F, FontStyle.Bold)) using (var b = new SolidBrush(Palette.Text))
            {
                var text = _snapshot.ProgressText; var size = g.MeasureString(text, n); g.DrawString(text, n, b, ring.X + (ring.Width - size.Width) / 2, ring.Y + 28);
            }
            using (var f = new Font("Consolas", 9F, FontStyle.Bold)) using (var b = new SolidBrush(_snapshot.Health == "ON TRACK" ? Color.FromArgb(43, 217, 164) : Color.FromArgb(255, 95, 115)))
                g.DrawString(_snapshot.Health, f, b, 104, 14);
            using (var f = new Font("Microsoft YaHei UI", 8.5F)) using (var b = new SolidBrush(Palette.TextDim))
                g.DrawString("OPEN " + _snapshot.OpenTasks + "   OVERDUE " + _snapshot.OverdueTasks + "   BLOCKED " + _snapshot.BlockedTasks, f, b, 104, 40);

            int startY = 102; int colWidth = Math.Max(180, (Width - 18) / 2);
            var stages = _snapshot.Stages.OrderBy(x => StageOrder(x.Key)).ToArray();
            for (int i = 0; i < stages.Length; i++)
            {
                int col = i % 2, row = i / 2; int x = col * colWidth; int y = startY + row * 22;
                DrawStage(g, stages[i].Key, stages[i].Value, x, y, colWidth - 20);
            }
        }

        private void DrawMission(Graphics g)
        {
            var good = _snapshot.Health == "ON TRACK";
            var status = good ? Color.FromArgb(0, 229, 185) : Color.FromArgb(255, 76, 112);
            int diameter = Math.Min(240, Height - 30);
            var ring = new Rectangle(16, 14, diameter, diameter);
            using (var track = new Pen(Color.FromArgb(17, 49, 72), 18)) g.DrawEllipse(track, ring);
            using (var outer = new Pen(Color.FromArgb(25, 82, 112), 1)) g.DrawEllipse(outer, ring.X - 12, ring.Y - 12, diameter + 24, diameter + 24);
            using (var arc = new Pen(status, 18) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, ring, -90, (float)(Math.Max(0, Math.Min(100, _snapshot.OverallProgress)) * 3.6));
            Text(g, _snapshot.ProgressText, new Rectangle(16, 67, diameter, 60), 34, Palette.Text, true, true);
            Text(g, _snapshot.Health, new Rectangle(16, 133, diameter, 26), 12, status, true, true);
            Text(g, _snapshot.ProjectName, new Rectangle(24, 170, diameter - 16, 38), 9, Palette.TextDim, false, true);
            int left = diameter + 55, available = Width - left - 12;
            var stages = _snapshot.Stages.OrderBy(x => StageOrder(x.Key)).ToArray();
            if (stages.Length == 0) return;
            int cell = available / stages.Length;
            using (var line = new Pen(Color.FromArgb(23, 97, 132), 2)) g.DrawLine(line, left + cell / 2, 38, Width - 12 - cell / 2, 38);
            for (int i = 0; i < stages.Length; i++)
            {
                int x = left + i * cell;
                var color = stages[i].Value > 0 ? Palette.Accent : Color.FromArgb(255, 76, 112);
                Text(g, stages[i].Key.ToUpperInvariant(), new Rectangle(x, 0, cell, 24), 8, Palette.TextDim, false, true);
                using (var brush = new SolidBrush(Palette.CardBg)) g.FillEllipse(brush, x + cell / 2 - 6, 32, 12, 12);
                using (var pen = new Pen(color, 2)) g.DrawEllipse(pen, x + cell / 2 - 6, 32, 12, 12);
                Text(g, stages[i].Key.ToUpperInvariant(), new Rectangle(x + 7, 77, cell - 8, 24), 8, Palette.TextDim, false, false);
                Text(g, stages[i].Value.ToString("0") + "%", new Rectangle(x + 7, 103, cell - 8, 32), 17, color, true, false);
                var bar = new Rectangle(x + 7, 146, Math.Max(8, cell - 20), 10);
                Palette.FillRounded(g, Color.FromArgb(26, 50, 76), bar, 4);
                int fill = (int)(bar.Width * Math.Max(0, Math.Min(100, stages[i].Value)) / 100);
                if (fill > 0) Palette.FillRounded(g, color, new Rectangle(bar.X, bar.Y, fill, bar.Height), 4);
            }
            using (var pen = new Pen(Palette.Line)) g.DrawLine(pen, left, 179, Width - 12, 179);
            Text(g, "NEXT ACTION / 下一步行动", new Rectangle(left, 193, available, 22), 9, Palette.TextDim, false, false);
            Text(g, _snapshot.NextAction, new Rectangle(left, 220, available - 12, 40), 12, Palette.Accent, true, false);
            Text(g, _snapshot.DaysToDeadline == int.MaxValue ? "未设置项目截止日期" : "项目截止  " + _snapshot.DaysToDeadline + " 天", new Rectangle(16, diameter + 24, diameter, 24), 10, Palette.TextDim, false, true);
        }

        private new static void Text(Graphics g, string text, Rectangle bounds, float size, Color color, bool bold, bool center)
        {
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, text, font, bounds, color, TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter | (center ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        }

        private void DrawDependency(Graphics g)
        {
            var stages = _snapshot.Stages.OrderBy(x => StageOrder(x.Key)).ToArray();
            if (stages.Length == 0) return;
            int cell = Width / stages.Length, h = Math.Max(74, Height - 44);
            string lowest = stages.OrderBy(x => x.Value).First().Key;
            for (int i = 0; i < stages.Length; i++)
            {
                int x = i * cell;
                bool bottleneck = stages[i].Key == lowest && stages[i].Value < 100;
                var color = bottleneck ? Color.FromArgb(255, 76, 112) : stages[i].Value > 0 ? Palette.Accent : Palette.TextFaint;
                Point[] polygon = { new Point(x + 2, 8), new Point(x + cell - 22, 8), new Point(x + cell - 3, h / 2), new Point(x + cell - 22, h - 8), new Point(x + 2, h - 8), new Point(x + 18, h / 2) };
                using (var fill = new SolidBrush(Color.FromArgb(18, color))) g.FillPolygon(fill, polygon);
                using (var pen = new Pen(color, bottleneck ? 2 : 1)) g.DrawPolygon(pen, polygon);
                Text(g, stages[i].Key.ToUpperInvariant(), new Rectangle(x + 18, 17, cell - 40, h / 2 - 12), Math.Min(11, cell / 15f), Palette.Text, true, true);
                Text(g, stages[i].Value.ToString("0") + "%", new Rectangle(x + 22, h / 2, cell - 44, 24), 12, color, true, true);
                if (Height > 150)
                {
                    int barWidth = Math.Max(12, cell - 52);
                    Palette.FillRounded(g, Palette.PanelAlt, new Rectangle(x + 26, h - 28, barWidth, 7), 3);
                    int fillWidth = (int)(barWidth * Math.Max(0, Math.Min(100, stages[i].Value)) / 100);
                    if (fillWidth > 0) Palette.FillRounded(g, color, new Rectangle(x + 26, h - 28, fillWidth, 7), 3);
                }
            }
            Text(g, "CONTROL SYSTEM BUILD                       FACTORY ACCEPTANCE TEST                       SITE & VALIDATION", new Rectangle(14, h + 4, Width - 28, 28), 9, Palette.TextDim, false, true);
        }

        private void DrawFocus(Graphics g)
        {
            int diameter = Math.Min(120, Height - 14);
            var ring = new Rectangle(12, 7, diameter, diameter);
            using (var pen = new Pen(Palette.Line, 11)) g.DrawEllipse(pen, ring);
            using (var pen = new Pen(Color.FromArgb(0, 229, 185), 11) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, ring, -90, (float)(Math.Max(0, Math.Min(100, _snapshot.OverallProgress)) * 3.6));
            Text(g, _snapshot.ProgressText, ring, 24, Palette.Text, true, true);
            Text(g, _snapshot.NextAction, new Rectangle(180, 6, Width - 200, 58), 22, Palette.Text, true, false);
            Text(g, "复核项目资料、工程差异与测试文档，继续推进当前项目。", new Rectangle(180, 76, Width - 200, 36), 11, Palette.TextDim, false, false);
        }

        private void DrawSummary(Graphics g)
        {
            int diameter = Math.Min(110, Height - 16);
            var ring = new Rectangle(10, 7, diameter, diameter);
            using (var pen = new Pen(Palette.Line, 9)) g.DrawEllipse(pen, ring);
            using (var pen = new Pen(Color.FromArgb(0, 229, 185), 9) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(pen, ring, -90, (float)(Math.Max(0, Math.Min(100, _snapshot.OverallProgress)) * 3.6));
            Text(g, _snapshot.ProgressText, ring, 23, Palette.Text, true, true);
            Text(g, _snapshot.Health, new Rectangle(150, 15, 200, 35), 16, Palette.Accent, true, false);
            Text(g, "OPEN " + _snapshot.OpenTasks + "    BLOCKED " + _snapshot.BlockedTasks + "    OVERDUE " + _snapshot.OverdueTasks, new Rectangle(150, 58, 400, 28), 10, Palette.TextDim, false, false);
            Text(g, "NEXT MILESTONE / 下一步", new Rectangle(Width / 2, 15, Width / 2, 28), 9, Palette.TextDim, false, false);
            Text(g, _snapshot.NextAction, new Rectangle(Width / 2, 50, Width / 2 - 12, 60), 12, Palette.Accent, true, false);
        }

        private void DrawStage(Graphics g, string name, double value, int x, int y, int width)
        {
            using (var f = new Font("Consolas", 8F)) using (var b = new SolidBrush(Palette.TextDim)) g.DrawString(name.ToUpperInvariant(), f, b, x, y);
            var bar = new Rectangle(x + 80, y + 4, Math.Max(40, width - 120), 7);
            Palette.FillRounded(g, Palette.PanelAlt, bar, 3);
            var fill = new Rectangle(bar.X, bar.Y, (int)(bar.Width * Math.Max(0, Math.Min(100, value)) / 100.0), bar.Height);
            if (fill.Width > 0) Palette.FillRounded(g, Palette.Accent, fill, 3);
            using (var f = new Font("Consolas", 8F)) using (var b = new SolidBrush(Palette.TextFaint)) g.DrawString(value.ToString("0") + "%", f, b, bar.Right + 5, y);
        }

        private static int StageOrder(string name)
        {
            string[] order = { "Design", "PLC", "DCS", "HMI", "FAT", "SAT", "Validation", "Delivery" };
            int i = Array.IndexOf(order, name); return i < 0 ? 99 : i;
        }
    }
}
