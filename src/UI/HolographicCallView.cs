using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;

namespace DeskBox.UI
{
    internal enum HologramMode { Idle, Connecting, Listening, Thinking, Speaking, Muted, Unavailable }

    // A surface in the existing application window, with no setup controls or secondary window.
    internal sealed class HolographicCallView : UserControl
    {
        private readonly RealtimeVoiceSettings profile;
        private readonly Func<RealtimeVoiceSettings,IRealtimeVoiceProvider> providerFactory;
        private readonly Func<int,IVoiceInput> inputFactory;
        private readonly Func<int,IVoiceOutput> outputFactory;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval=33 };
        private readonly QuantumPresence presence = new QuantumPresence();
        private CancellationTokenSource session;
        private Task completion = Task.FromResult(0);
        private bool exiting, muted, thinking, captionFinished = true;
        private string caption = "", assistantCaption = "", userCaption = "";
        private double seconds;
        private float level, targetLevel;
        private HologramMode mode;
        public event EventHandler ExitRequested;
        internal HologramMode Mode { get { return mode; } }
        internal string Caption { get { return caption; } }
        internal bool Muted { get { return muted; } }

        public HolographicCallView(RealtimeVoiceSettings settings) : this(settings,
            p => p.Provider == "qwen" ? (IRealtimeVoiceProvider)new QwenLiveVoiceProvider(p.Endpoint,p.Model,p.ApiKey) :
                new GeminiLiveVoiceProvider(p.Endpoint,p.Model,p.ApiKey),
            rate => new VoiceMicrophone(rate), rate => new VoiceSpeaker(rate)) { }

        internal HolographicCallView(RealtimeVoiceSettings settings, Func<RealtimeVoiceSettings,IRealtimeVoiceProvider> providers,
            Func<int,IVoiceInput> inputs, Func<int,IVoiceOutput> outputs)
        {
            profile=settings; providerFactory=providers; inputFactory=inputs; outputFactory=outputs;
            Dock=DockStyle.Fill; BackColor=Color.FromArgb(2,5,11); DoubleBuffered=true; TabStop=true;
            AccessibleName="全息 AI 对话"; AccessibleDescription="Esc 返回软件；Ctrl+M 静音；连接中断时点击投影重试。";
            SetStyle(ControlStyles.ResizeRedraw,true);
            timer.Tick += delegate {
                if (!Visible || IsDisposed) return;
                timer.Interval=profile.ReducedMotion ? 150 : 33;
                if (!profile.ReducedMotion) seconds += .033;
                level += (targetLevel-level)*.22f;
                Invalidate();
            };
            timer.Start();
        }

        public void Start()
        {
            if (session != null || exiting || IsDisposed) return;
            if (!profile.Ready) { mode=HologramMode.Idle; caption="语音尚未连接。"; Invalidate(); return; }
            completion=RunAsync();
        }

        public async Task StopAsync()
        {
            exiting=true;
            if (session != null) session.Cancel();
            await completion;
        }

        public void CancelForShutdown() { exiting=true; if(session!=null)session.Cancel(); }

        private async Task RunAsync()
        {
            var active=new CancellationTokenSource();
            session=active; mode=HologramMode.Connecting; caption="正在与你连接…";
            muted=false; thinking=false; assistantCaption=userCaption=""; captionFinished=true;
            Task<VoicePacket> receive=null;
            try {
                using (var provider=providerFactory(profile)) {
                    try {
                        using (var timeout=CancellationTokenSource.CreateLinkedTokenSource(active.Token)) {
                            timeout.CancelAfter(TimeSpan.FromSeconds(20));
                            await provider.ConnectAsync(timeout.Token);
                        }
                        active.Token.ThrowIfCancellationRequested();
                        using (var speaker=outputFactory(provider.OutputSampleRate)) {
                            IVoiceInput microphone=null;
                            var samples=new short[VoiceMicrophone.SamplesPerBlock];
                            bool inputEnded=false;
                            mode=HologramMode.Listening; caption="我在听。";
                            receive=provider.ReceiveAsync(active.Token);
                            try {
                                while (true) {
                                    active.Token.ThrowIfCancellationRequested();
                                    if (receive.IsCompleted) {
                                        var packet=await receive;
                                        if(packet.ReconnectRequested)throw new System.IO.IOException();
                                        if(packet.Interrupted){speaker.Flush();assistantCaption=userCaption="";captionFinished=true;thinking=false;caption="我在听。";}
                                        if(packet.InputEnded)thinking=true;
                                        if(!string.IsNullOrWhiteSpace(packet.UserCaption)){
                                            userCaption=Tail(packet.UserCaptionDelta ? userCaption+packet.UserCaption : packet.UserCaption);
                                            caption="你："+userCaption;
                                        }
                                        if(!string.IsNullOrEmpty(packet.Caption)) {
                                            if(captionFinished)userCaption="";
                                            assistantCaption=(captionFinished ? "" : assistantCaption)+packet.Caption;
                                            assistantCaption=Tail(assistantCaption);caption=assistantCaption;captionFinished=false;
                                        }
                                        if(packet.TurnComplete)captionFinished=true;
                                        foreach(var audio in packet.Audio)speaker.Play(audio);
                                        receive=provider.ReceiveAsync(active.Token);
                                    }
                                    double input=0;
                                    if(muted) {
                                        if(microphone!=null){microphone.Dispose();microphone=null;}
                                        if(!inputEnded)await provider.EndInputAsync(active.Token);
                                        inputEnded=true;
                                    } else {
                                        if(microphone==null)microphone=inputFactory(provider.InputSampleRate);
                                        inputEnded=false;
                                        for(int i=0;i<4;i++){
                                            int count=microphone.Read(samples);if(count==0)break;
                                            input=VoiceMicrophone.Rms(samples,count);
                                            var bytes=new byte[count*2];Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);
                                            await provider.SendAudioAsync(bytes,active.Token);
                                        }
                                    }
                                    var playback=speaker.PlaybackLevel();
                                    if(playback.HasValue)thinking=false;
                                    mode=playback.HasValue ? HologramMode.Speaking : muted ? HologramMode.Muted : thinking ? HologramMode.Thinking : HologramMode.Listening;
                                    targetLevel=(float)Math.Min(1,(playback ?? input)*6);
                                    await Task.Delay(15,active.Token);
                                }
                            } finally { if(microphone!=null)microphone.Dispose(); }
                        }
                    } finally { active.Cancel(); if(receive!=null)ObserveFailure(receive); }
                }
            } catch(OperationCanceledException) { if(!exiting){mode=HologramMode.Unavailable;caption="暂时没有连上，请稍后重试。";} }
              catch(Exception) { if(!exiting){mode=HologramMode.Unavailable;caption="连接暂时中断，点击投影可重试。";} }
            finally {
                if(session==active)session=null;
                active.Dispose();targetLevel=0;
                if(!IsDisposed)Invalidate();
            }
        }

        private static string Tail(string text)
        {
            if(text.Length<=150)return text;
            int offset=text.Length-150;
            if(char.IsLowSurrogate(text[offset]))offset++;
            return text.Substring(offset);
        }
        private static void ObserveFailure(Task task) { task.ContinueWith(t=>GC.KeepAlive(t.Exception),TaskContinuationOptions.OnlyOnFaulted); }

        protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if(mode==HologramMode.Unavailable)Start(); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if(keyData==Keys.Escape){if(!exiting && ExitRequested!=null)ExitRequested(this,EventArgs.Empty);return true;}
            if(keyData==(Keys.Control|Keys.M)){
                if(session!=null && mode!=HologramMode.Connecting){muted=!muted;caption=muted ? "麦克风已静音。" : "我在听。";}
                return true;
            }
            return base.ProcessCmdKey(ref msg,keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.Clear(BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;
            int subtitleHeight=Math.Max(115,Height/6);
            var stage=new Rectangle(0,0,Width,Math.Max(1,Height-subtitleHeight));
            if(profile.Appearance=="quantum")presence.Draw(g,stage,seconds,level,mode,profile.ReducedMotion);
            else SilverwingProjection.Draw(g,stage,level,mode==HologramMode.Speaking,muted,false,profile.ReducedMotion,seconds,1,.75f,profile.Appearance=="photon");
            if(!string.IsNullOrWhiteSpace(caption))
                using(var font=new Font("Microsoft YaHei UI",Math.Max(14,Math.Min(23,Width/65f)),FontStyle.Regular))
                using(var brush=new SolidBrush(Color.FromArgb(225,231,242,250)))
                using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisWord})
                    g.DrawString(caption,font,brush,new RectangleF(Width*.16f,Height-subtitleHeight+12,Width*.68f,subtitleHeight-24),format);
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing){CancelForShutdown();timer.Stop();timer.Dispose();presence.Dispose();}
            base.Dispose(disposing);
        }
    }
}
