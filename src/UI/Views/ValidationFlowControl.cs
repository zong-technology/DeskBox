using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI.Views
{
    public class ValidationFlowControl : UserControl
    {
        private readonly FlowCanvas _canvas;
        public ValidationFlowControl(Palette palette)
        {
            AutoScroll = true; BackColor = palette.CardBg;
            _canvas = new FlowCanvas(palette) { Width = 620, Height = 2110 };
            Controls.Add(_canvas);
            Resize += delegate { _canvas.Width=Math.Min(620,Math.Max(360,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-4));_canvas.Left = Math.Max(0, (ClientSize.Width - _canvas.Width - SystemInformation.VerticalScrollBarWidth) / 2);_canvas.Invalidate(); };
        }
        public void SetProject(EngineerWorkspace project) { _canvas.Project = project; _canvas.Invalidate(); }
        public void ApplyPalette(Palette palette){BackColor=palette.CardBg;_canvas.ApplyPalette(palette);}

        private class FlowCanvas : Control
        {
            private Palette _palette;
            public void ApplyPalette(Palette palette){_palette=palette;BackColor=palette.CardBg;Invalidate();}
            public EngineerWorkspace Project;
            private int X(int value){return (int)Math.Round(value*Width/620d);}
            public FlowCanvas(Palette p) { _palette = p; BackColor = p.CardBg; DoubleBuffered = true; }
            private Rectangle Node(int i)
            {
                if (i == 0) return new Rectangle(X(230), 20, X(160), 48);
                if (i <= 4) return new Rectangle(X(8 + (i - 1) * 154), 110, X(144), 62);
                return new Rectangle(X(i == 9 ? 240 : 190), 290 + (i - 5) * 80, X(i == 9 ? 140 : 240), 62);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                var steps = Project != null && Project.ValidationSteps != null ? Project.ValidationSteps : ValidationProgress.Create();
                using (var pen = new Pen(_palette.TextFaint, 1.4f))
                using (var cap = new AdjustableArrowCap(4, 5))
                {
                    pen.CustomEndCap = cap;
                    for (int i = 1; i <= 4; i++)
                    {
                        var branch = Node(i); g.DrawLine(pen, X(310), 68, branch.X + branch.Width / 2, 110);
                        g.DrawLine(pen, branch.X + branch.Width / 2, branch.Bottom, X(310), 230);
                    }
                    g.DrawLine(pen, X(310), 255, X(310), 290);
                    for (int i = 5; i < 26; i++) { var a = Node(i); var b = Node(i + 1); g.DrawLine(pen, X(310), a.Bottom, X(310), b.Top); }
                    var decision = Node(9); var design = Node(5);
                    g.DrawLines(pen, new[] { new Point(decision.Right, decision.Top + 31), new Point(X(520), decision.Top + 31), new Point(X(520), design.Top + 31), new Point(design.Right, design.Top + 31) });
                    TextRenderer.DrawText(g, "否：返回设计复核", Font, new Rectangle(X(410), 420, X(200), 25), _palette.TextDim);
                    TextRenderer.DrawText(g, "是", Font, new Point(X(320), decision.Bottom + 4), _palette.TextDim);
                }
                using (var fill = new SolidBrush(_palette.PanelAlt)) g.FillEllipse(fill, X(280), 215, X(60), 40);
                TextRenderer.DrawText(g, "汇合", Font, new Rectangle(X(280), 215, X(60), 40), _palette.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                for (int i = 0; i < steps.Count && i < 27; i++)
                {
                    var r = Node(i); var step = steps[i];
                    Color color = !step.Percent.HasValue ? _palette.TextFaint : step.Percent >= 100 ? Color.FromArgb(0, 220, 175) : _palette.Accent;
                    using (var brush = new SolidBrush(_palette.PanelAlt))
                    using (var border = new Pen(color, 1.3f))
                    {
                        if (i == 9) { var points = new[] { new Point(r.X + r.Width / 2, r.Top), new Point(r.Right, r.Y + 31), new Point(r.X + r.Width / 2, r.Bottom), new Point(r.X, r.Y + 31) }; g.FillPolygon(brush, points); g.DrawPolygon(border, points); }
                        else if (i == 0 || i == 26) { g.FillEllipse(brush, r); g.DrawEllipse(border, r); }
                        else { g.FillRectangle(brush, r); g.DrawRectangle(border, r); }
                    }
                    string value = step.Percent.HasValue ? step.Percent.Value.ToString("0.#") + "%" : "N/A";
                    TextRenderer.DrawText(g, step.Name + "\r\n" + value, Font, r, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
            }
        }
    }
}
