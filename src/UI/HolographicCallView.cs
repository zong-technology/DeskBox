using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly HologramDynamics dynamics = new HologramDynamics();
        private readonly Stopwatch animationClock = Stopwatch.StartNew();
        private readonly VoiceConversation conversation;
        private readonly HologramTools tools;
        private CancellationTokenSource session;
        private Task completion = Task.FromResult(0);
        private bool exiting, muted, thinking, captionFinished = true;
        private string caption = "", assistantCaption = "", userCaption = "";
        private double seconds, lastFrame;
        private float level, targetLevel;
        private HologramMode mode;
        public event EventHandler ExitRequested;
        internal HologramMode Mode { get { return mode; } }
        internal string Caption { get { return caption; } }
        internal bool Muted { get { return muted; } }
        internal ChatSession Conversation { get { return conversation.Session; } }
        internal HologramInformation Information { get { tools.Expire(DateTime.UtcNow);return tools.Information; } }

        public HolographicCallView(RealtimeVoiceSettings settings, ChatService history = null, Func<ToolContext> context = null) : this(settings,
            p => p.Provider == "qwen" ? (IRealtimeVoiceProvider)new QwenLiveVoiceProvider(p.Endpoint,p.Model,p.ApiKey,p) :
                new GeminiLiveVoiceProvider(p.Endpoint,p.Model,p.ApiKey,p),
            rate => new VoiceMicrophone(rate), rate => new VoiceSpeaker(rate),history,context) { }

        internal HolographicCallView(RealtimeVoiceSettings settings, Func<RealtimeVoiceSettings,IRealtimeVoiceProvider> providers,
            Func<int,IVoiceInput> inputs, Func<int,IVoiceOutput> outputs, ChatService history = null, Func<ToolContext> context = null)
        {
            profile=settings; providerFactory=providers; inputFactory=inputs; outputFactory=outputs;
            conversation=new VoiceConversation(history,settings);tools=new HologramTools(context);
            Dock=DockStyle.Fill; BackColor=Color.FromArgb(2,5,11); DoubleBuffered=true; TabStop=true;
            AccessibleName="全息 AI 对话"; AccessibleDescription="Esc 返回软件；Ctrl+M 静音；连接中断时点击投影重试。";
            SetStyle(ControlStyles.ResizeRedraw,true);
            timer.Tick += delegate {
                double now=animationClock.Elapsed.TotalSeconds,elapsed=now-lastFrame;lastFrame=now;
                if (!Visible || IsDisposed) return;
                timer.Interval=profile.ReducedMotion ? 150 : 33;
                AdvanceFrame(elapsed);
                tools.Expire(DateTime.UtcNow);conversation.Flush(false);
                Invalidate();
            };
            timer.Start();
        }

        internal void AdvanceFrame(double elapsed)
        {
            double dt=Math.Max(0,Math.Min(.1,elapsed));
            if(!profile.ReducedMotion)seconds+=dt;
            dynamics.Advance(dt,mode,targetLevel,profile.ReducedMotion);level=dynamics.Energy;
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
            conversation.Close();tools.Clear();
        }

        public void CancelForShutdown() { exiting=true; if(session!=null)session.Cancel();conversation.Close();tools.Clear(); }

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
                            double lastInput=0,lastInputAt=-1;
                            mode=HologramMode.Listening; caption="我在听。";
                            receive=provider.ReceiveAsync(active.Token);
                            try {
                                while (true) {
                                    active.Token.ThrowIfCancellationRequested();
                                    if (receive.IsCompleted) {
                                        var packet=await receive;
                                        if(packet.ReconnectRequested)throw new System.IO.IOException();
                                        conversation.Accept(packet);
                                        if(packet.ToolsCancelled)tools.Clear();
                                        if(packet.Interrupted){speaker.Flush();dynamics.Interrupt();targetLevel=level=0;assistantCaption=userCaption="";captionFinished=true;thinking=false;caption="我在听。";}
                                        if(packet.InputEnded)thinking=true;
                                        if(!string.IsNullOrWhiteSpace(packet.UserCaption)){
                                            userCaption=Tail(packet.UserCaptionDelta ? userCaption+packet.UserCaption : packet.UserCaption);
                                            caption="你："+userCaption;
                                        }
                                        if(!string.IsNullOrEmpty(packet.Caption)) {
                                            thinking=true;
                                            if(captionFinished)userCaption="";
                                            assistantCaption=(captionFinished ? "" : assistantCaption)+packet.Caption;
                                            assistantCaption=Tail(assistantCaption);caption=assistantCaption;captionFinished=false;
                                        }
                                        if(packet.TurnComplete){captionFinished=true;thinking=false;}
                                        if(!packet.Interrupted && !packet.ToolsCancelled && packet.ToolCalls.Count>0){
                                            if(packet.ToolCalls.Count>8)throw new System.IO.InvalidDataException();
                                            var results=new List<VoiceToolResult>();
                                            foreach(var call in packet.ToolCalls){
                                                active.Token.ThrowIfCancellationRequested();
                                                if(string.IsNullOrEmpty(call.Id) || string.IsNullOrEmpty(call.Name))continue;
                                                var result=tools.Execute(call);results.Add(result);
                                                if(call.Name=="hologram_show" && tools.Information!=null)dynamics.Information=1;
                                                conversation.Tool(call.Name,new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(result.Response));
                                            }
                                            if(results.Count>0)await provider.CompleteToolsAsync(results,active.Token);
                                            thinking=true;
                                        }
                                        foreach(var audio in packet.Audio)speaker.Play(audio);
                                        receive=provider.ReceiveAsync(active.Token);
                                    }
                                    double input=animationClock.Elapsed.TotalSeconds-lastInputAt<.075 ? lastInput : 0;
                                    if(muted) {
                                        if(microphone!=null){microphone.Dispose();microphone=null;}
                                        input=lastInput=0;lastInputAt=-1;
                                        if(!inputEnded)await provider.EndInputAsync(active.Token);
                                        inputEnded=true;
                                    } else {
                                        if(microphone==null)microphone=inputFactory(provider.InputSampleRate);
                                        inputEnded=false;
                                        for(int i=0;i<4;i++){
                                            int count=microphone.Read(samples);if(count==0)break;
                                            lastInput=VoiceMicrophone.Rms(samples,count);lastInputAt=animationClock.Elapsed.TotalSeconds;
                                            input=Math.Max(input,lastInput);
                                            var bytes=new byte[count*2];Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);
                                            await provider.SendAudioAsync(bytes,active.Token);
                                        }
                                    }
                                    var playback=speaker.PlaybackLevel();
                                    mode=playback.HasValue ? HologramMode.Speaking : muted ? HologramMode.Muted : thinking ? HologramMode.Thinking : HologramMode.Listening;
                                    targetLevel=HologramDynamics.FromRms(playback ?? input);
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
                conversation.Close();tools.Clear();
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
            var info=Information;var infoBounds=RectangleF.Empty;
            if(info!=null){
                if(Width>=1180){
                    infoBounds=new RectangleF(Width*.63f,stage.Height*.2f,Width*.32f,stage.Height*.62f);
                    stage.Width=(int)(Width*.64f);
                }else{
                    infoBounds=new RectangleF(Width*.12f,stage.Height*.60f,Width*.76f,stage.Height*.37f);
                    stage.Height=(int)(stage.Height*.59f);
                }
            }
            if(profile.Appearance=="quantum")presence.Draw(g,stage,seconds,level,mode,profile.ReducedMotion,dynamics);
            else SilverwingProjection.Draw(g,stage,level,mode==HologramMode.Speaking,mode==HologramMode.Muted,false,profile.ReducedMotion,seconds,1,.75f,profile.Appearance=="photon",dynamics);
            if(info!=null)DrawInformation(g,infoBounds,info);
            if(!string.IsNullOrWhiteSpace(caption))
                using(var font=new Font("Microsoft YaHei UI",Math.Max(14,Math.Min(23,Width/65f)),FontStyle.Regular))
                using(var brush=new SolidBrush(Color.FromArgb(225,231,242,250)))
                using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Near,Trimming=StringTrimming.EllipsisWord})
                    g.DrawString(caption,font,brush,new RectangleF(Width*.16f,Height-subtitleHeight+12,Width*.68f,subtitleHeight-24),format);
        }

        private void DrawInformation(Graphics g,RectangleF box,HologramInformation info)
        {
            using(var fill=new SolidBrush(Color.FromArgb(200,5,20,32)))g.FillRectangle(fill,box);
            using(var line=new Pen(Color.FromArgb(100,80,207,243),1)){
                g.DrawLine(line,box.Left,box.Top,box.Right,box.Top);g.DrawLine(line,box.Left,box.Bottom,box.Right,box.Bottom);
                g.DrawLine(line,box.Left,box.Top,box.Left,box.Top+24);g.DrawLine(line,box.Right,box.Bottom-24,box.Right,box.Bottom);
            }
            float padding=Math.Max(14,Math.Min(24,box.Width*.05f));
            using(var accent=new SolidBrush(Color.FromArgb(220,129,222,248)))
            using(var title=new Font("Microsoft YaHei UI",Math.Max(13,Math.Min(19,Width/80f)),FontStyle.Bold))
            using(var format=new StringFormat {Trimming=StringTrimming.EllipsisCharacter})
                g.DrawString(info.Title,title,accent,new RectangleF(box.X+padding,box.Y+12,box.Width-padding*2,32),format);
            var body=new RectangleF(box.X+padding,box.Y+49,box.Width-padding*2,Math.Max(20,box.Height-64));
            float size=Math.Max(12,Math.Min(17,Width/90f));
            while(size>11){using(var candidate=new Font("Microsoft YaHei UI",size))if(g.MeasureString(info.Content,candidate,(int)body.Width).Height<=body.Height)break;size-=.5f;}
            using(var font=new Font("Microsoft YaHei UI",size))
            using(var ink=new SolidBrush(Color.FromArgb(235,220,239,247)))
            using(var format=new StringFormat {Trimming=StringTrimming.EllipsisWord})g.DrawString(info.Content,font,ink,body,format);
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing){CancelForShutdown();timer.Stop();timer.Dispose();presence.Dispose();}
            base.Dispose(disposing);
        }
    }
}
