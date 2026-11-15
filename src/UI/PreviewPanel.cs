using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.UI
{
    /// <summary>右侧预览面板：按类型渲染图片 / 文本 / 代码 / 表格 / PDF 元信息，其余显示文件信息。</summary>
    public class PreviewPanel : Control
    {
        private PreviewResult _result;
        private string _path = "";
        private readonly Store _store;
        private int _scroll;
        private int _contentH;
        private Rectangle _openBtn, _revealBtn, _favBtn, _aiBtn;

        public Palette Palette { get; set; }
        public bool IsOpen { get; private set; }
        public event Action<string> OpenRequested;
        public event Action<string> RevealRequested;
        public event Action<string> FavoriteRequested;
        public event Action<string> AiRequested;

        public PreviewPanel(Store store)
        {
            _store = store;
            Palette = Palette.For(store.Settings.Theme, store.Settings.Accent);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Palette.Bg;
            Width = 360;
        }

        public void ApplyPalette(Palette p) { Palette = p; BackColor = p.Bg; Invalidate(); }

        public void ShowPreview(string path)
        {
            ReleaseImage();
            _path = path ?? "";
            _scroll = 0;
            IsOpen = !string.IsNullOrEmpty(_path);
            if (!IsOpen) { _result = null; Invalidate(); return; }
            try
            {
                _result = DeskBox.Fs.PreviewService.Produce(_path, 200000);
            }
            catch (Exception ex)
            {
                _result = new PreviewResult { Kind = "meta", Title = Path.GetFileName(_path), Error = ex.Message };
            }
            Invalidate();
        }

        public void ClosePreview() { ReleaseImage(); IsOpen = false; _path = ""; _result = null; Invalidate(); }

        private void ReleaseImage() { if (_result != null && _result.Image != null) { _result.Image.Dispose(); _result.Image = null; } }
        protected override void Dispose(bool disposing) { if (disposing) ReleaseImage(); base.Dispose(disposing); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Palette.Bg);
            if (!IsOpen || _result == null) { PaintEmpty(g); return; }

            var card = new Rectangle(8, 8, Width - 16, Height - 16);
            UiKit.DrawCard(g, Palette, card, 12);

            int pad = 16;
            int x = card.X + pad;
            int y = card.Y + 12;
            int w = card.Width - pad * 2;

            using (var f = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
            using (var fs = new Font("Microsoft YaHei UI", 8.5F))
            {
                g.DrawString(Palette.Ellipsis(g, _result.Title, f, w - 20), f, new SolidBrush(Palette.Text), x, y);
                y += 26;
                if (!string.IsNullOrEmpty(_result.Subtitle))
                {
                    g.DrawString(Palette.Ellipsis(g, _result.Subtitle, fs, w - 20), fs, new SolidBrush(Palette.TextFaint), x, y);
                    y += 20;
                }

                if (!string.IsNullOrEmpty(_result.Error))
                {
                    g.DrawString("预览失败：" + _result.Error, fs, new SolidBrush(Color.FromArgb(248, 113, 113)), x, y);
                    y += 22;
                }

                // 图片
                if (_result.Kind == "image" && _result.Image != null)
                {
                    int maxH = Math.Max(120, Height / 2);
                    float scale = Math.Min((float)w / _result.Image.Width, (float)maxH / _result.Image.Height);
                    scale = Math.Min(scale, 1f);
                    int iw = Math.Max(1, (int)(_result.Image.Width * scale)), ih = Math.Max(1, (int)(_result.Image.Height * scale));
                    var dest = new Rectangle(x + (w - iw) / 2, y, iw, ih);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(_result.Image, dest);
                    y += ih + 12;
                }
                // 表格
                else if (_result.Kind == "table" && _result.Rows.Count > 0)
                {
                    int rows = Math.Min(_result.Rows.Count, 12);
                    int cols = Math.Min(_result.Rows[0].Length, 4);
                    int cw = w / Math.Max(1, cols);
                    for (int r = 0; r < rows; r++)
                    {
                        for (int c = 0; c < cols; c++)
                        {
                            var cell = r < _result.Rows.Count && c < _result.Rows[r].Length ? _result.Rows[r][c] : "";
                            var col = r == 0 ? Palette.Accent : Palette.TextDim;
                            g.DrawString(Palette.Ellipsis(g, cell, fs, cw - 6), fs, new SolidBrush(col), x + c * cw, y + r * 20);
                        }
                    }
                    y += rows * 20 + 8;
                    g.DrawString("共 " + _result.Rows.Count + " 行（预览前 12 行）", fs, new SolidBrush(Palette.TextFaint), x, y);
                    y += 22;
                }
                // 文本
                else if ((_result.Kind == "text" || _result.Kind == "code" || _result.Kind == "pdf") && !string.IsNullOrEmpty(_result.Text))
                {
                    var lines = _result.Text.Replace("\r\n", "\n").Split('\n');
                    int maxLines = Math.Max(4, (Height - y - 110) / 17);
                    for (int i = 0; i < Math.Min(lines.Length, maxLines); i++)
                    {
                        g.DrawString(Palette.Ellipsis(g, lines[i], fs, w - 8), fs,
                            new SolidBrush(Palette.TextDim), x, y + i * 17);
                    }
                    y += Math.Min(lines.Length, maxLines) * 17 + 8;
                    if (_result.Truncated) g.DrawString("（已截断，仅显示前 " + _result.Text.Length + " 字符）", fs, new SolidBrush(Palette.TextFaint), x, y);
                }

                // 元信息
                foreach (var m in _result.Meta)
                {
                    g.DrawString(Palette.Ellipsis(g, m, fs, w - 6), fs, new SolidBrush(Palette.TextFaint), x, y);
                    y += 17;
                }

                // 底部按钮
                int bw = Math.Max(90, (w - 8) / 2);
                int by = card.Bottom - 76;
                _openBtn = new Rectangle(x, by, bw, 28);
                _revealBtn = new Rectangle(x + bw + 8, by, bw, 28);
                _aiBtn = new Rectangle(x, by + 34, bw, 28);
                _favBtn = new Rectangle(x + bw + 8, by + 34, bw, 28);
                DrawMiniButton(g, _openBtn, "用默认程序打开");
                DrawMiniButton(g, _revealBtn, "在资源管理器显示");
                DrawMiniButton(g, _aiBtn, "Ask AI&SI");
                DrawMiniButton(g, _favBtn, "加入收藏");
            }
        }

        private void PaintEmpty(Graphics g)
        {
            var card = new Rectangle(8, 8, Width - 16, Height - 16);
            UiKit.DrawCard(g, Palette, card, 12);
            using (var title = new Font("Consolas", 10F, FontStyle.Bold))
            using (var body = new Font("Microsoft YaHei UI", 8.7F))
            using (var accent = new SolidBrush(Palette.Accent))
            using (var dim = new SolidBrush(Palette.TextDim))
            using (var faint = new SolidBrush(Palette.TextFaint))
            {
                g.DrawString("DESKTOP CONTEXT", title, accent, 24, 24);
                g.DrawString("选择文件后显示预览、元数据和 AI 操作。", body, dim, 24, 56);
                g.DrawString("快捷键", body, accent, 24, 102);
                g.DrawString("Enter       打开 / 进入文件夹\r\nF2          重命名\r\nCtrl+C/X/V  复制 / 剪切 / 粘贴\r\nDelete      移入回收站\r\nShift+Delete 永久删除\r\nAlt+Enter   文件属性\r\nBackspace   返回上一级\r\nCtrl+Z      撤销最近操作", body, dim, 24, 130);
                g.DrawString("安全边界\r\n· Explorer 保持运行\r\n· 默认删除进入 Windows 回收站\r\n· AI 文件访问仍受授权目录限制", body, faint, 24, 310);
            }
        }

        private void DrawMiniButton(Graphics g, Rectangle r, string text)
        {
            Palette.FillRounded(g, Palette.PanelAlt, r, 7);
            Palette.DrawRounded(g, Palette.Line, r, 7, 1f);
            using (var f = new Font("Microsoft YaHei UI", 8F))
            {
                var sz = g.MeasureString(text, f);
                if (sz.Width > r.Width - 6)
                {
                    var shortText = text.Length > 6 ? text.Substring(0, 6) : text;
                    sz = g.MeasureString(shortText, f);
                    g.DrawString(shortText, f, new SolidBrush(Palette.TextDim), r.X + (r.Width - sz.Width) / 2, r.Y + 6);
                }
                else g.DrawString(text, f, new SolidBrush(Palette.TextDim), r.X + (r.Width - sz.Width) / 2, r.Y + 6);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!IsOpen) return;
            if (_openBtn.Contains(e.Location) && OpenRequested != null) OpenRequested(_path);
            else if (_revealBtn.Contains(e.Location) && RevealRequested != null) RevealRequested(_path);
            else if (_aiBtn.Contains(e.Location) && AiRequested != null) AiRequested(_path);
            else if (_favBtn.Contains(e.Location) && FavoriteRequested != null) FavoriteRequested(_path);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = _openBtn.Contains(e.Location) || _revealBtn.Contains(e.Location) || _aiBtn.Contains(e.Location) || _favBtn.Contains(e.Location)
                ? Cursors.Hand : Cursors.Default;
        }
    }
}
