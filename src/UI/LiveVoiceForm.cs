using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    internal sealed class LiveVoiceForm : Form
    {
        private readonly TextBox _endpoint = new TextBox { Text = GeminiLiveVoiceProvider.DefaultEndpoint, Width = 530 };
        private readonly ComboBox _provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ComboBox _model = new ComboBox { Width = 385 };
        private readonly Button _fetch = new Button { Text = "获取模型", Width = 95 };
        private readonly string[] _endpoints = { QwenLiveVoiceProvider.DefaultEndpoint, GeminiLiveVoiceProvider.DefaultEndpoint };
        private readonly string[] _models = { QwenLiveVoiceProvider.DefaultModel, "gemini-3.1-flash-live-preview" };
        private readonly string[] _keys = new string[2];
        private int _currentProvider = -1;
        private CancellationTokenSource _catalogRequest;
        private readonly TextBox _key = new TextBox { UseSystemPasswordChar = true, Width = 260 };
        private readonly Button _connect = new Button { Text = "启动实时语音", Width = 120 };
        private readonly Button _mute = new Button { Text = "关闭麦克风", Width = 120, Enabled = false };
        private readonly Label _status = new Label { Text = "待机 · 开始后将音频发送至所填的语音服务。", AutoSize = true };
        private readonly CoreView _core = new CoreView { Dock = DockStyle.Fill };
        private readonly FlowLayoutPanel _setup;
        private bool _embedded;
        private CancellationTokenSource _session;
        private bool _muted, _closeAfterSession, _dropPlayback;

        public LiveVoiceForm() : this(null) { }

        public LiveVoiceForm(Func<EngineerWorkspace> currentProject)
        {
            _core.CurrentProject = currentProject;
            _core.ReturnRequested = Close;
            _core.MuteRequested = delegate { if (_mute.Enabled) ToggleMute(); };
            _core.PlaybackResetRequested = delegate { _dropPlayback = true; };
            Text = "AI&SI · 实时语音";
            ClientSize = new Size(660, 440);
            MinimumSize = new Size(590, 380);
            StartPosition = FormStartPosition.CenterParent;
            _keys[0] = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
            _keys[1] = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            _provider.Items.AddRange(new object[] { "千问百炼 · Omni Realtime", "Google · Gemini Live" });
            _provider.SelectedIndexChanged += delegate { SelectProvider(); };
            _provider.SelectedIndex = 0;
            var controls = _setup = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 204, Padding = new Padding(8), AutoScroll = true };
            Row(controls, "供应商", _provider);
            Row(controls, "WSS 地址", _endpoint);
            controls.Controls.Add(new Label { Text = "语音模型", Width = 72 });
            controls.Controls.Add(_model);
            controls.Controls.Add(_fetch);
            controls.SetFlowBreak(_fetch, true);
            Row(controls, "API Key", _key);
            controls.Controls.AddRange(new Control[] { _connect, _mute, _status });
            controls.SetFlowBreak(_status, true);
            foreach (var entry in new[] { new[]{"core","智能核心"}, new[]{"reactor","反应器"}, new[]{"plc","PLC 拓扑"}, new[]{"progress","项目进度"} })
            {
                var id = entry[0];
                var button = new Button { Text = entry[1], Width = 98 };
                button.Click += delegate { _core.ShowScene(id); };
                controls.Controls.Add(button);
            }
            Controls.Add(_core);
            Controls.Add(controls);
            _fetch.Click += async delegate { await FetchModels(); };
            _connect.Click += async delegate { if (_session != null) _session.Cancel(); else await RunSession(); };
            _mute.Click += delegate { ToggleMute(); };
        }

        public void EmbedIn(Control owner)
        {
            _embedded = true;
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = Size.Empty;
            Dock = DockStyle.Fill;
            var back = new Button { Text = "返回 AI 助手", Width = 120 };
            back.Click += delegate { Close(); };
            _setup.Controls.Add(back);
            _status.Text = "连接后进入沉浸模式。Esc 返回 AI 助手，Ctrl+M 切换麦克风。";
            owner.Controls.Add(this);
            BringToFront();
            Show();
            _connect.Focus();
        }

        private void SetImmersive(bool active)
        {
            if (!_embedded || IsDisposed) return;
            _setup.Visible = !active;
            _core.SetWorkspace(active);
            if (active)
            {
                _core.ShowScene("core");
                _core.BringToFront();
                _core.Focus();
            }
            else
            {
                _setup.BringToFront();
                _connect.Focus();
            }
        }

        private void ToggleMute()
        {
            _muted = !_muted;
            _core.Muted = _muted;
            _mute.Text = _muted ? "开启麦克风" : "关闭麦克风";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_embedded && keyData == Keys.Escape) { Close(); return true; }
            if (_embedded && _session != null && _mute.Enabled && keyData == (Keys.Control | Keys.M))
            {
                ToggleMute();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void CancelForShutdown()
        {
            if (_session != null) _session.Cancel();
            if (_catalogRequest != null) _catalogRequest.Cancel();
        }

        private void SelectProvider()
        {
            if (_currentProvider >= 0)
            {
                _endpoints[_currentProvider] = _endpoint.Text;
                _models[_currentProvider] = _model.Text;
                _keys[_currentProvider] = _key.Text;
            }
            _currentProvider = _provider.SelectedIndex;
            _endpoint.Text = _endpoints[_currentProvider];
            _model.Items.Clear();
            _model.Text = _models[_currentProvider];
            _key.Text = _keys[_currentProvider];
        }

        private async Task FetchModels()
        {
            using (var request = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                _catalogRequest = request;
                _fetch.Enabled = _provider.Enabled = _connect.Enabled = _endpoint.Enabled = _key.Enabled = false;
                _status.Text = "正在获取实时语音模型…";
                try
                {
                    string current = _model.Text;
                    var models = await (_currentProvider == 0
                        ? QwenLiveVoiceProvider.ListModelsAsync(_endpoint.Text, _key.Text, request.Token)
                        : VoiceModelCatalog.ListGeminiAsync(_endpoint.Text, _key.Text, request.Token));
                    if (IsDisposed) return;
                    _model.Items.Clear();
                    _model.Items.AddRange(models);
                    _model.Text = current;
                    if (models.Length != 0) _model.DroppedDown = true;
                    _status.Text = models.Length == 0 ? "目录未列出可用的实时模型，可手动填写模型 ID。" : "获取到 " + models.Length + " 个实时模型。";
                }
                catch (ArgumentException error) { if (!IsDisposed) _status.Text = error.Message; }
                catch (OperationCanceledException) { if (!IsDisposed) _status.Text = "获取模型超时，可直接使用已填写的模型 ID。"; }
                catch (IOException error) { if (!IsDisposed) _status.Text = error.Message + " 可直接使用已填写的模型 ID。"; }
                catch (Exception) { if (!IsDisposed) _status.Text = "无法读取模型目录，可直接使用已填写的模型 ID。"; }
                finally
                {
                    _catalogRequest = null;
                    if (!IsDisposed) _fetch.Enabled = _provider.Enabled = _connect.Enabled = _endpoint.Enabled = _key.Enabled = true;
                }
            }
        }

        private IRealtimeVoiceProvider CreateProvider()
        {
            if (_currentProvider == 0) return new QwenLiveVoiceProvider(_endpoint.Text, _model.Text, _key.Text, !_embedded);
            return new GeminiLiveVoiceProvider(_endpoint.Text, _model.Text, _key.Text);
        }

        private static void Row(FlowLayoutPanel panel, string label, Control field)
        {
            panel.Controls.Add(new Label { Text = label, Width = 72, TextAlign = ContentAlignment.MiddleLeft });
            panel.Controls.Add(field);
            panel.SetFlowBreak(field, true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_session != null)
            {
                _closeAfterSession = true;
                _session.Cancel();
                e.Cancel = true;
            }
            if (_catalogRequest != null) _catalogRequest.Cancel();
            base.OnFormClosing(e);
        }

        private async Task RunSession()
        {
            var session = new CancellationTokenSource();
            _session = session;
            _connect.Text = "结束会话";
            _provider.Enabled = _fetch.Enabled = _endpoint.Enabled = _model.Enabled = _key.Enabled = false;
            _muted = false;
            _core.Muted = false;
            _mute.Text = "关闭麦克风";
            Task<VoicePacket> receive = null;
            try
            {
                _status.Text = "连接中…";
                using (IRealtimeVoiceProvider provider = CreateProvider())
                {
                    try
                    {
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                        {
                            timeout.CancelAfter(TimeSpan.FromSeconds(20));
                            await provider.ConnectAsync(timeout.Token);
                        }
                        using (var speaker = new VoiceSpeaker(provider.OutputSampleRate))
                        {
                            VoiceMicrophone mic = null;
                            var samples = new short[VoiceMicrophone.SamplesPerBlock];
                            var token = session.Token;
                            bool muteApplied = false;
                            double inputLevel = 0;
                            _mute.Enabled = true;
                            SetImmersive(true);
                            _status.Text = "聆听 · 麦克风开启";
                            receive = provider.ReceiveAsync(token);
                            try
                            {
                                while (true)
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (_dropPlayback) { speaker.Flush(); _dropPlayback = false; }
                                    if (receive.IsCompleted)
                                    {
                                        var packet = await receive;
                                        if (packet.ReconnectRequested) throw new IOException("服务端将关闭此连接，请重新启动会话。");
                                        if (packet.Interrupted) speaker.Flush();
                                        if (!_core.Paused && !_core.OutputMuted)
                                            foreach (var audio in packet.Audio) speaker.Play(audio);
                                        _core.AcceptCaption(packet);
                                        var qwen = provider as QwenLiveVoiceProvider;
                                        if (qwen != null && packet.SceneCalls.Count > 0)
                                        {
                                            foreach (var call in packet.SceneCalls)
                                                if (call.Scene != null) _core.ShowScene(call.Scene);
                                            await qwen.CompleteScenesAsync(packet.SceneCalls, token);
                                        }
                                        receive = provider.ReceiveAsync(token);
                                    }
                                    if (_muted || _core.Paused)
                                    {
                                        if (mic != null) { mic.Dispose(); mic = null; }
                                        if (!muteApplied) await provider.EndInputAsync(token);
                                        muteApplied = true;
                                        inputLevel = 0;
                                    }
                                    else
                                    {
                                        if (mic == null) mic = new VoiceMicrophone(provider.InputSampleRate);
                                        muteApplied = false;
                                        for (int i = 0; i < 4; i++)
                                        {
                                            int count = mic.Read(samples);
                                            if (count == 0) break;
                                            inputLevel = VoiceMicrophone.Rms(samples, count);
                                            var bytes = new byte[count * 2];
                                            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                                            await provider.SendAudioAsync(bytes, token);
                                        }
                                    }
                                    var playback = speaker.PlaybackLevel();
                                    _core.UpdateSignal(playback ?? inputLevel, playback.HasValue);
                                    _status.Text = (playback.HasValue ? "AI 正在讲话" : "聆听") + (_muted ? " · 麦克风已关闭" : " · 麦克风开启");
                                    await Task.Delay(15, token);
                                }
                            }
                            finally { if (mic != null) mic.Dispose(); }
                        }
                    }
                    finally
                    {
                        session.Cancel();
                        if (receive != null)
                            ObserveFailure(receive);
                    }
                }
            }
            catch (OperationCanceledException) { _status.Text = "会话已结束，或连接超时。"; }
            catch (ArgumentException error) { _status.Text = error.Message; }
            catch (InvalidOperationException error) { _status.Text = error.Message; }
            catch (IOException error) { _status.Text = error.Message; }
            catch (Exception) { _status.Text = "语音连接或音频设备异常，请检查配置后重试。"; }
            finally
            {
                _session = null;
                session.Dispose();
                if (!IsDisposed)
                {
                    _core.UpdateSignal(0, false);
                    _connect.Text = "启动实时语音";
                    _mute.Enabled = false;
                    _provider.Enabled = _fetch.Enabled = _endpoint.Enabled = _model.Enabled = _key.Enabled = true;
                    SetImmersive(false);
                    if (_closeAfterSession) Close();
                }
            }
        }

        private static void ObserveFailure(Task task)
        {
            task.ContinueWith(failed => GC.KeepAlive(failed.Exception), CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private sealed class CoreView : Control
        {
            private double _level;
            private bool _speaking, _workspace;
            public bool Muted, Paused, OutputMuted;
            public Action ReturnRequested, MuteRequested, PlaybackResetRequested;
            private bool _captions = true, _captionFinished;
            private bool _probeCue, _tipCue, _probePlayed, _reduced;
            private float _probeReveal, _tipReveal;
            private DateTime _signalAt;
            private readonly ContextMenuStrip _sceneMenu = new ContextMenuStrip();
            private string _transcript = "";
            private readonly Dictionary<string, Rectangle> _actions = new Dictionary<string, Rectangle>();
            private DateTime _started;

            public void SetWorkspace(bool active)
            {
                _workspace = active;
                Paused = OutputMuted = false;
                _transcript = "";
                _captionFinished = true;
                _started = _signalAt = DateTime.UtcNow;
                ClearProjection();
                Invalidate();
            }
            private string _scene = "core";
            public Func<EngineerWorkspace> CurrentProject;
            private double _yaw = 0.48, _pitch = 0.30, _zoom = 1;
            private Point _lastDrag;

            public void ShowScene(string scene)
            {
                Capture = false;
                _scene = scene;
                Cursor = scene == "reactor" ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (_scene != "reactor" || e.Button != MouseButtons.Left) return;
                Focus();
                _lastDrag = e.Location;
                Capture = true;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (_scene != "reactor" || !Capture) return;
                _yaw += (e.X - _lastDrag.X) * .014;
                _pitch = Math.Max(-1.2, Math.Min(1.2, _pitch + (e.Y - _lastDrag.Y) * .012));
                _lastDrag = e.Location;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left) return;
                Capture = false;
                if (!_workspace) return;
                foreach (var action in _actions)
                {
                    if (!action.Value.Contains(e.Location)) continue;
                    switch (action.Key)
                    {
                        case "back":
                        case "end": if (ReturnRequested != null) ReturnRequested(); return;
                        case "mic": if (MuteRequested != null) MuteRequested(); break;
                        case "speaker": OutputMuted = !OutputMuted; ResetPlayback(); break;
                        case "pause": Paused = !Paused; ResetPlayback(); break;
                        case "caption": _captions = !_captions; break;
                        case "visual": _sceneMenu.Show(this, e.Location); break;
                        case "reduced": _reduced = !_reduced; break;
                        default: ShowScene(action.Key); break;
                    }
                    Invalidate();
                    return;
                }
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                if (_scene != "reactor") return;
                _zoom = Math.Max(.55, Math.Min(2.2, _zoom * Math.Pow(1.12, e.Delta / 120.0)));
                Invalidate();
            }

            protected override void OnMouseDoubleClick(MouseEventArgs e)
            {
                base.OnMouseDoubleClick(e);
                if (_scene != "reactor" || e.Button != MouseButtons.Left) return;
                _yaw = .48;
                _pitch = .30;
                _zoom = 1;
                Invalidate();
            }

            public CoreView()
            {
                DoubleBuffered = true;
                TabStop = true;
                AccessibleName = "智能中枢";
                BackColor = Color.FromArgb(9, 21, 36);
                _sceneMenu.Items.Add("AI 本体", null, delegate { ClearProjection(); ShowScene("core"); });
                _sceneMenu.Items.Add("反应器", null, delegate { ClearProjection(); ShowScene("reactor"); });
                _sceneMenu.Items.Add("PLC 拓扑", null, delegate { ClearProjection(); ShowScene("plc"); });
                _sceneMenu.Items.Add("项目进度", null, delegate { ClearProjection(); ShowScene("progress"); });
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _sceneMenu.Dispose();
                base.Dispose(disposing);
            }

            private void ResetPlayback()
            {
                ClearProjection();
                ShowScene("core");
                if (PlaybackResetRequested != null) PlaybackResetRequested();
            }

            private void ClearProjection()
            {
                _probeCue = _tipCue = _probePlayed = false;
                _probeReveal = _tipReveal = 0;
            }
            public void AcceptCaption(VoicePacket packet)
            {
                if (packet.Interrupted)
                {
                    _transcript = "";
                    _captionFinished = true;
                    ClearProjection();
                    ShowScene("core");
                    return;
                }
                if (!string.IsNullOrEmpty(packet.Caption))
                {
                    _transcript = (_captionFinished ? "" : _transcript) + packet.Caption;
                    if (!Paused && !OutputMuted)
                    {
                        if (_transcript.Contains("温度探头")) _probeCue = true;
                        if (_probeCue && (_transcript.Contains("感温端") || _transcript.Contains("感温位置") ||
                            _transcript.Contains("探头尖端"))) _tipCue = true;
                    }
                    if (_transcript.Length > 90) _transcript = _transcript.Substring(_transcript.Length - 90);
                    _captionFinished = false;
                }
                if (packet.TurnComplete) _captionFinished = true;
            }

            public void UpdateSignal(double level, bool speaking)
            {
                _level = Math.Min(1, level * 4);
                _speaking = speaking;
                if (_workspace)
                {
                    var now = DateTime.UtcNow;
                    float step = _reduced ? 1 : Math.Min(1, (float)(now - _signalAt).TotalMilliseconds / 480);
                    _signalAt = now;
                    if (speaking && _probeCue)
                    {
                        _probePlayed = true;
                        if (_scene != "core") ShowScene("core");
                    }
                    if (!speaking && _captionFinished && _probePlayed)
                        _probeCue = _tipCue = _probePlayed = false;
                    float target = _probePlayed ? 1 : 0;
                    _probeReveal += Math.Max(-step, Math.Min(step, target - _probeReveal));
                    float focus = _tipCue && _probeReveal >= .85f ? 1 : 0;
                    _tipReveal += Math.Max(-step, Math.Min(step, focus - _tipReveal));
                }
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (_workspace)
                {
                    var elapsed = DateTime.UtcNow - _started;
                    VoiceWorkspaceArt.Draw(g, ClientSize, _speaking, Muted, _level, elapsed.ToString(@"mm\:ss"), _scene,
                        _scene == "progress" && CurrentProject != null ? CurrentProject() : null,
                        Paused, OutputMuted, _captions, _transcript, _yaw, _pitch, _zoom,
                        _probeReveal, _tipReveal, _reduced, _actions);
                    return;
                }
                float cx = ClientSize.Width / 2f, cy = ClientSize.Height / 2f;
                float radius = Math.Min(cx, cy) * 0.62f;
                if (_scene != "core")
                {
                    var area = new Rectangle(16, 10, Math.Max(0, ClientSize.Width - 115), Math.Max(0, ClientSize.Height - 20));
                    VoiceSceneView.Draw(g, area, _scene, _scene == "progress" && CurrentProject != null ? CurrentProject() : null, Font, _yaw, _pitch, _zoom);
                    cx = ClientSize.Width - 50;
                    cy = 48;
                    radius = 36;
                }
                var color = _speaking ? Color.Orange : Color.Turquoise;
                for (int i = 0; i < 4; i++)
                {
                    float r = radius * (0.45f + i * 0.18f + (float)_level * i * 0.08f);
                    using (var pen = new Pen(Color.FromArgb(90 + (int)(_level * 150), color), 2))
                        g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);
                }
                using (var brush = new SolidBrush(Color.FromArgb(50 + (int)(_level * 190), color)))
                    g.FillEllipse(brush, cx - radius / 4, cy - radius / 4, radius / 2, radius / 2);
            }
        }
    }
}
