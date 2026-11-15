using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace DeskBox.UI
{
    /// <summary>
    /// 启动画面：显示品牌 + 真实初始化步骤。
    /// 进度只反映真正完成的步骤（由 Program 逐步回调），不做固定延时假进度。
    /// </summary>
    public class SplashForm : Form
    {
        private readonly string _title = "桌匣 DeskBox";
        private readonly string _subtitle = "AI&SI · 桌面工作台";
        private readonly string[] _steps;
        private int _done;
        private string _current = "";
        private string _error = "";
        private float _phase;
        private readonly System.Windows.Forms.Timer _anim;
        private readonly DateTime _startAt = DateTime.Now;

        public SplashForm(string[] steps)
        {
            _steps = steps ?? new string[0];
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(9, 13, 22);
            ClientSize = new Size(560, 320);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            _anim = new System.Windows.Forms.Timer { Interval = 33 };
            _anim.Tick += delegate { _phase += 0.045f; if (_phase > (float)(Math.PI * 2)) _phase -= (float)(Math.PI * 2); Invalidate(); };
            _anim.Start();
        }

        /// <summary>由初始化逻辑在后台线程调用：标记某一步完成。</summary>
        public void Advance(string stepName)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action<string>(Advance), stepName); return; }
                _current = stepName ?? "";
                _done = Math.Min(_steps.Length, _done + 1);
                Invalidate();
            }
            catch { }
        }

        public void Fail(string message)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) { BeginInvoke(new Action<string>(Fail), message); return; }
                _error = message ?? "未知错误";
                Invalidate();
            }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(9, 13, 22), Color.FromArgb(17, 26, 44), 45f))
                g.FillRectangle(bg, ClientRectangle);

            // 柔和光晕（克制，不闪烁）
            int cx = ClientSize.Width / 2;
            for (int i = 5; i >= 1; i--)
            {
                int r = 90 + i * 26;
                int alpha = 5 + (int)(4 * Math.Sin(_phase + i));
                using (var b = new SolidBrush(Color.FromArgb(Math.Max(2, alpha), 110, 231, 255)))
                    g.FillEllipse(b, cx - r, 84 - r, r * 2, r * 2);
            }

            // 标识
            var logo = new Rectangle(cx - 28, 56, 56, 56);
            using (var path = Rounded(logo, 16))
            using (var b = new LinearGradientBrush(logo, Color.FromArgb(110, 231, 255), Color.FromArgb(124, 92, 255), 45f))
                g.FillPath(b, path);
            using (var f = new Font("Segoe UI", 24F, FontStyle.Bold))
            {
                var sz = g.MeasureString("D", f);
                g.DrawString("D", f, new SolidBrush(Color.FromArgb(10, 16, 26)), cx - sz.Width / 2, 56 + (56 - sz.Height) / 2);
            }

            using (var f = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold))
            {
                var sz = g.MeasureString(_title, f);
                g.DrawString(_title, f, new SolidBrush(Color.FromArgb(230, 237, 247)), cx - sz.Width / 2, 128);
            }
            using (var f = new Font("Microsoft YaHei UI", 9.5F))
            {
                var sz = g.MeasureString(_subtitle, f);
                g.DrawString(_subtitle, f, new SolidBrush(Color.FromArgb(120, 140, 170)), cx - sz.Width / 2, 160);
            }

            // 进度条（真实步骤比例）
            int total = Math.Max(1, _steps.Length);
            int barW = 360, barX = cx - barW / 2, barY = 214;
            Palette.FillRounded(g, Color.FromArgb(28, 38, 58), new Rectangle(barX, barY, barW, 6), 3);
            int fill = (int)(barW * Math.Min(1.0, (double)_done / total));
            if (fill > 0)
                Palette.FillRounded(g, Color.FromArgb(110, 231, 255), new Rectangle(barX, barY, fill, 6), 3);

            using (var f = new Font("Microsoft YaHei UI", 8.5F))
            {
                var msg = string.IsNullOrEmpty(_error)
                    ? (_current.Length > 0 ? "正在：" + _current : "准备中…")
                    : "初始化失败：" + _error;
                var color = string.IsNullOrEmpty(_error) ? Color.FromArgb(139, 154, 179) : Color.FromArgb(248, 113, 113);
                var sz = g.MeasureString(msg, f);
                g.DrawString(msg, f, new SolidBrush(color), cx - sz.Width / 2, barY + 16);
            }
            using (var f = new Font("Microsoft YaHei UI", 8F))
            {
                var ver = "v3  ·  " + Math.Max(0, (int)(DateTime.Now - _startAt).TotalMilliseconds) + " ms";
                g.DrawString(ver, f, new SolidBrush(Color.FromArgb(90, 104, 128)), 16, ClientSize.Height - 24);
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            try { if (_anim != null) { _anim.Stop(); _anim.Dispose(); } } catch { }
            base.Dispose(disposing);
        }
    }
}
