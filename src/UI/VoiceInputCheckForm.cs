using System;
using System.Drawing;
using System.Windows.Forms;
using DeskBox.Ai;

namespace DeskBox.UI
{
    internal sealed class VoiceInputCheckForm : Form
    {
        private readonly Timer _timer = new Timer { Interval = 16 };
        private readonly short[] _samples = new short[VoiceMicrophone.SamplesPerBlock];
        private readonly Button _toggle = new Button { Text = "启用麦克风", Dock = DockStyle.Bottom, Height = 38 };
        private readonly Label _status = new Label { Dock = DockStyle.Top, Height = 52 };
        private readonly Panel _wave = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(9, 23, 37) };
        private VoiceMicrophone _mic;
        private int _sampleCount;
        private double _level;

        public VoiceInputCheckForm()
        {
            Text = "AI&SI · P1 麦克风检查";
            ClientSize = new Size(560, 240);
            StartPosition = FormStartPosition.CenterParent;
            Controls.Add(_wave);
            Controls.Add(_status);
            Controls.Add(_toggle);
            _status.Text = "仅检查本地麦克风与实时波形。音频不保存、不上传。";
            _toggle.Click += delegate
            {
                if (_mic != null) { StopInput("麦克风已关闭。"); return; }
                try
                {
                    _mic = new VoiceMicrophone();
                    _status.Text = "正在采集 · 默认输入设备 · PCM 24 kHz";
                    _toggle.Text = "关闭麦克风";
                    _timer.Start();
                }
                catch (InvalidOperationException error) { StopInput(error.Message); }
            };
            _timer.Tick += delegate
            {
                try
                {
                    int count = _mic.Read(_samples);
                    if (count == 0) return;
                    _sampleCount = count;
                    _level = VoiceMicrophone.Rms(_samples, count);
                    _wave.Invalidate();
                }
                catch (InvalidOperationException error) { StopInput(error.Message); }
            };
            _wave.Paint += delegate(object sender, PaintEventArgs args)
            {
                int w = _wave.ClientSize.Width, h = _wave.ClientSize.Height;
                using (var pen = new Pen(Color.FromArgb(100 + (int)Math.Min(155, _level * 310), 53, 220, 245), 2))
                {
                    if (_sampleCount < 2) { args.Graphics.DrawLine(pen, 0, h / 2, w, h / 2); return; }
                    var points = new PointF[_sampleCount];
                    for (int i = 0; i < points.Length; i++)
                        points[i] = new PointF(i * (w - 1f) / (points.Length - 1), h * (0.5f - 0.45f * _samples[i] / 32768f));
                    args.Graphics.DrawLines(pen, points);
                }
            };
        }

        private void StopInput(string message)
        {
            _timer.Stop();
            if (_mic != null) { _mic.Dispose(); _mic = null; }
            _sampleCount = 0;
            _level = 0;
            _status.Text = message;
            _toggle.Text = "启用麦克风";
            _wave.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { StopInput("麦克风已关闭。"); _timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
