using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.UI;
using DeskBox.UI.Views;

// Runs only with fake transports/devices and synthetic profiles. No paid service or microphone is opened.
internal static class SilverwingAccept
{
    private static int checks;
    private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
    private static void Pump(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(5);while(!done() && DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(10);}Check(done(),"Asynchronous state completed");}
    private static void Key(Control control,Keys key){control.GetType().GetMethod("ProcessCmdKey",Hidden).Invoke(control,new object[]{new Message(),key});}
    private static void Freeze(HolographicCallView view){((System.Windows.Forms.Timer)typeof(HolographicCallView).GetField("timer",Hidden).GetValue(view)).Stop();typeof(HolographicCallView).GetField("seconds",Hidden).SetValue(view,4.0);}
    private static Bitmap Shot(Control view,Size size){view.Size=size;var image=new Bitmap(size.Width,size.Height);view.DrawToBitmap(image,new Rectangle(Point.Empty,size));return image;}
    private static int Changed(Bitmap a,Bitmap b){int result=0;for(int y=0;y<a.Height;y+=4)for(int x=0;x<a.Width;x+=4)if(a.GetPixel(x,y)!=b.GetPixel(x,y))result++;return result;}
    private static int Luminous(Bitmap a){int n=0;for(int y=0;y<a.Height*.86;y+=3)for(int x=0;x<a.Width;x+=3){var p=a.GetPixel(x,y);if(p.B>70 && p.G>40)n++;}return n;}

    [STAThread] private static int Main(string[] args)
    {
        try{
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
            string profileDir=Path.Combine(output,"synthetic-profile");Directory.CreateDirectory(profileDir);
            AppPaths.DataDir=Store.DataDir=profileDir;
            var storage=new RealtimeVoiceSettingsStore();
            var settings=new RealtimeVoiceSettings {ApiKey="fixture-key-never-send",Appearance="quantum",AssistantName="星璃",Persona="你是一位温柔、严谨的项目助手。"};
            storage.Save(settings);var restored=new RealtimeVoiceSettingsStore().Load();
            Check(restored.ApiKey==settings.ApiKey && restored.Model==settings.Model,"Voice profile survives reopening");
            Check(!File.ReadAllText(Path.Combine(profileDir,"realtime-voice.json")).Contains(settings.ApiKey),"No plaintext secret is persisted");
            Check(!restored.AllowInterruption,"Existing profiles default to stable speaker playback");
            settings.AllowInterruption=true;storage.Save(settings);
            Check(new RealtimeVoiceSettingsStore().Load().AllowInterruption,"Hands-free interruption preference survives reopening");
            settings.AllowInterruption=false;storage.Save(settings);
            Check(restored.Ready,"Persisted profile can be used without reopening setup");
            Check(restored.AssistantName==settings.AssistantName && restored.Persona==settings.Persona,"Custom name and persona survive reopening");
            var setup=new JavaScriptSerializer().Serialize(QwenLiveVoiceProvider.Setup(QwenLiveVoiceProvider.DefaultModel,settings));
            var geminiSetup=GeminiLiveVoiceProvider.SetupJson(settings.Model,settings);
            Check(setup.Contains(settings.AssistantName) && setup.Contains(settings.Persona) && geminiSetup.Contains(settings.AssistantName) && geminiSetup.Contains(settings.Persona),"Both live providers receive the selected role and name");
            Check(setup.Contains("hologram_show") && geminiSetup.Contains("deskbox_tasks") && !setup.Contains("deskbox_show_scene"),"Live tools expose temporary information and application queries without old scenes");
            Check(setup.Contains("\"type\":\"function\",\"function\":{") && setup.Contains("qwen3-asr-flash-realtime"),"Qwen session uses nested function definitions and explicitly enables user transcription");
            var legacySetup=new JavaScriptSerializer().Serialize(QwenLiveVoiceProvider.Setup("qwen3-omni-flash-realtime",settings));
            Check(!legacySetup.Contains("\"tools\":") && legacySetup.Contains(settings.AssistantName),"Older Qwen voice models keep persona and transcription without unsupported tool configuration");
            Check(typeof(HolographicCallView).BaseType==typeof(UserControl),"Hologram is an application surface, not a form or plugin");
            var recordStore=new Store();recordStore.Init();recordStore.Tasks.Add(new TaskItem {Id="voice-fixture",Title="隔离测试任务",Done=false});
            var history=new ChatService(recordStore,new ProviderStore());
            CheckVoiceStability(settings);
            CheckVoiceRecords(history,settings);
            CheckVoiceTools(recordStore);
            CheckToolCards(output);
            CheckDynamicProjection(output);

            var fake=new FakeProvider();int inputs=0;FakeInput lastInput=null;FakeOutput speaker=null;
            bool holdQuery=false;
            using(var started=new ManualResetEvent(false))
            using(var release=new ManualResetEvent(false))
            using(var host=new Form {ClientSize=new Size(1280,800)})
            using(var view=new HolographicCallView(settings,p=>fake,r=>{inputs++;return lastInput=new FakeInput();},r=>speaker=new FakeOutput(),history,()=>new ToolContext {Store=recordStore,UiInvoke=action=>{if(holdQuery){started.Set();release.WaitOne();}return action();}})){
                Freeze(view);host.Controls.Add(view);host.Show();Application.DoEvents();
                int forms=Application.OpenForms.Count;view.Start();Pump(()=>inputs==1);
                Check(fake.Connected==1 && view.Mode==HologramMode.Listening,"Entering starts exactly one connection automatically");
                view.Start();Check(fake.Connected==1,"Repeated entry does not duplicate the session");
                Check(view.Controls.Count==0 && Application.OpenForms.Count==forms,"Call surface contains no setup controls or secondary windows");
                fake.Emit(new VoicePacket {Caption="你好，",Audio={new byte[32]}});Pump(()=>view.Caption=="你好，");
                fake.Emit(new VoicePacket {Caption="我在这里。",TurnComplete=true});Pump(()=>view.Caption=="你好，我在这里。");
                Check(speaker.Played==1,"Real transport packets drive audio and captions");
                Check((float)typeof(HolographicCallView).GetField("targetLevel",Hidden).GetValue(view)>.5f,"Actual playback level drives the light field");
                var dynamics=(HologramDynamics)typeof(HolographicCallView).GetField("dynamics",Hidden).GetValue(view);
                speaker.Amplitude=0;Pump(()=>(float)typeof(HolographicCallView).GetField("targetLevel",Hidden).GetValue(view)==0);
                for(int i=0;i<25;i++)view.AdvanceFrame(.033);
                Check(dynamics.Energy<.03f,"A pause in actual output audio returns the particles to a quiet envelope");
                speaker.Amplitude=.16;Pump(()=>(float)typeof(HolographicCallView).GetField("targetLevel",Hidden).GetValue(view)>.5f);
                for(int i=0;i<8;i++)view.AdvanceFrame(.033);
                Check(dynamics.Energy>.7f,"Output audio resumes the particle motion without using subtitle length as a voice signal");
                fake.Emit(new VoicePacket {Caption="批量"});
                for(int i=0;i<16;i++)fake.Emit(new VoicePacket {Caption="信息"});
                fake.Emit(new VoicePacket {Caption="完成",TurnComplete=true});
                Pump(()=>view.Caption=="批量"+string.Concat(Enumerable.Repeat("信息",16))+"完成");
                Check(view.Conversation.Messages.Any(m=>m.Content==view.Caption),"Bursty live packets keep transcript order and reach the persistent conversation");
                Key(view,Keys.Control|Keys.M);Pump(()=>view.Muted && lastInput.Disposed && fake.Ended>0);
                Check(view.Muted && lastInput.Disposed,"Microphone is released on mute");
                Key(view,Keys.Control|Keys.M);Pump(()=>inputs==2);Check(!view.Muted,"Unmute reopens input without reconnecting");
                fake.Emit(new VoicePacket {Interrupted=true,UserCaption="先听"});Pump(()=>view.Caption=="你：先听");
                fake.Emit(new VoicePacket {UserCaption="我说",UserCaptionDelta=true});Pump(()=>view.Caption=="你：先听我说");
                Check(speaker.Flushed==1,"Interruption flushes queued playback");
                Check(dynamics.Energy==0 && dynamics.Speaking==0,"Barge-in immediately stops the previous voice motion");
                fake.Emit(new VoicePacket {InputEnded=true});Pump(()=>view.Mode==HologramMode.Thinking);
                fake.Emit(new VoicePacket {Caption="我在听，请继续。",Audio={new byte[32]}});Pump(()=>view.Mode==HologramMode.Speaking);
                fake.Emit(new VoicePacket {ToolCalls={new VoiceToolCall {Id="show-info",Name="hologram_show",Arguments=new Dictionary<string,object>{{"title","今日任务"},{"content","01  审核项目文档\n02  核对设备清单\n03  确认测试进度"},{"seconds",30}}}}});
                Pump(()=>view.Information!=null && fake.ToolResults.Count==1);
                foreach(var size in new[]{new Size(1600,900),new Size(1280,800),new Size(1024,768)}){
                    view.Dock=DockStyle.None;using(var image=Shot(view,size))image.Save(Path.Combine(output,"information-"+size.Width+".png"),ImageFormat.Png);
                }
                Check(view.Controls.Count==0 && Application.OpenForms.Count==forms,"AI information is painted in the same immersive surface without controls or windows");
                fake.Emit(new VoicePacket {ToolCalls={new VoiceToolCall {Id="clear-info",Name="hologram_clear"}}});Pump(()=>view.Information==null && fake.ToolResults.Count==2);
                holdQuery=true;
                try {
                    fake.Emit(new VoicePacket {ToolCalls={new VoiceToolCall {Id="slow-query",Name="deskbox_tasks"}}});Pump(()=>started.WaitOne(0));
                    fake.Emit(new VoicePacket {Interrupted=true,UserCaption="先停止这项操作"});Pump(()=>view.Caption.Contains("先停止"));
                    fake.Emit(new VoicePacket {ToolCalls={new VoiceToolCall {Id="next-action",Name="hologram_show",Arguments=new Dictionary<string,object>{{"title","新指令"},{"content","等待旧操作收尾"}}}}});
                    Pump(()=>fake.ToolResults.Any(r=>r.Id=="next-action"));
                    Check(view.Mode!=HologramMode.Unavailable && fake.Connected==1,"A new instruction during a cancelling tool cannot disconnect the live call");
                    var busy=fake.ToolResults.First(r=>r.Id=="next-action");
                    Check(new JavaScriptSerializer().Serialize(busy.Response).Contains("\"ok\":false"),"Overlapping operations return honest busy feedback instead of executing twice");
                } finally { holdQuery=false;release.Set(); }
                var tick=typeof(System.Windows.Forms.Timer).GetMethod("OnTick",Hidden);
                var animation=(System.Windows.Forms.Timer)typeof(HolographicCallView).GetField("timer",Hidden).GetValue(view);
                for(int i=0;i<5;i++)tick.Invoke(animation,new object[]{EventArgs.Empty});
                foreach(var size in new[]{new Size(1600,900),new Size(1280,800),new Size(1024,768)}){
                    view.Dock=DockStyle.None;
                    using(var image=Shot(view,size)){
                        image.Save(Path.Combine(output,"immersive-"+size.Width+".png"),ImageFormat.Png);
                        Check(Luminous(image)>500,"Projected light field is visible at "+size);
                    }
                    Check(view.Controls.Count==0,"Only projection and painted subtitles at "+size);
                }
                using(var a=Shot(view,new Size(1280,800)))using(var b=new Bitmap(1280,800)){
                    typeof(HolographicCallView).GetField("seconds",Hidden).SetValue(view,6.0);view.DrawToBitmap(b,new Rectangle(0,0,1280,800));
                    Check(Changed(a,b)>300,"Projection has depth and continuous motion");
                    var watch=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<3;i++)view.DrawToBitmap(b,new Rectangle(0,0,1280,800));
                    Console.WriteLine("Immersive render mean: "+(watch.Elapsed.TotalMilliseconds/3).ToString("0.0")+" ms (offscreen CI host)");
                }
                var stopping=view.StopAsync();Pump(()=>stopping.IsCompleted);stopping.GetAwaiter().GetResult();
                Check(fake.Disposed && lastInput.Disposed && speaker.Disposed,"Exit releases transport and both audio devices");
                Check(view.Conversation!=null && view.Conversation.Messages.Any(m=>m.Content=="先听我说") && view.Conversation.Messages.Any(m=>m.SpeakerName==settings.AssistantName),"Call surface saves matching user and named assistant messages");
                host.Close();
            }
            var pending=new FakeProvider {HoldConnect=true};int unopened=0;
            using(var view=new HolographicCallView(settings,p=>pending,r=>{unopened++;return new FakeInput();},r=>new FakeOutput())){
                Freeze(view);view.Start();var ending=view.StopAsync();Pump(()=>ending.IsCompleted);
                Check(unopened==0 && pending.Disposed,"Cancelling connection never opens a late microphone");
            }
            var voiceTransports = new List<FakeProvider>();
            FakeInput pausedInput = null;
            using (var view = new HolographicCallView(settings,
                p => { var transport = new FakeProvider(); voiceTransports.Add(transport); return transport; },
                r => pausedInput = new FakeInput(), r => new FakeOutput()))
            {
                Freeze(view);
                view.Start();
                Pump(() => view.Mode == HologramMode.Listening && pausedInput != null);
                var pause = view.PauseAsync();
                Pump(() => pause.IsCompleted);
                pause.GetAwaiter().GetResult();
                Check(voiceTransports.Single().Disposed && pausedInput.Disposed && view.Mode == HologramMode.Idle, "Pausing closes the transport and releases the microphone before showing idle");
                view.Start();
                Pump(() => voiceTransports.Count == 2 && view.Mode == HologramMode.Listening);
                var stop = view.StopAsync();
                Pump(() => stop.IsCompleted);
                stop.GetAwaiter().GetResult();
                Check(voiceTransports.All(transport => transport.Disposed), "Resuming opens a new transport and close releases it");
            }
            var failed=new FakeProvider {FailConnect=true};
            using(var view=new HolographicCallView(settings,p=>failed,r=>new FakeInput(),r=>new FakeOutput())){
                Freeze(view);view.Start();Pump(()=>view.Mode==HologramMode.Unavailable);
                Check(view.Controls.Count==0 && !view.Caption.Contains("API") && !view.Caption.Contains(settings.ApiKey),"Errors stay as a subtitle without revealing setup or credentials");
            }
            var noKey=new RealtimeVoiceSettings();storage.Save(noKey);
            using(var view=new HolographicCallView(noKey,p=>{throw new Exception("must not connect");},r=>new FakeInput(),r=>new FakeOutput())){
                Freeze(view);view.Start();Check(view.Mode==HologramMode.Idle && view.Controls.Count==0,"Unconfigured entry still shows only the hologram");
                noKey.ReducedMotion=true;
                using(var a=Shot(view,new Size(1280,800)))using(var b=new Bitmap(1280,800)){
                    typeof(HolographicCallView).GetField("seconds",Hidden).SetValue(view,10.0);view.DrawToBitmap(b,new Rectangle(0,0,1280,800));
                    Check(Changed(a,b)==0,"Reduced motion freezes the full light field");
                }
            }
            // The shell navigation checks need the real window, but no model connection.
            // The earlier call tests use the injectable transport and audio fixtures.
            storage.Save(new RealtimeVoiceSettings());
            Console.WriteLine("Checking the main shell with voice credentials disabled");
            var store=new Store();store.Init();store.Settings.TakeoverIcons=false;store.Settings.AutoStart=false;store.Settings.ReferenceUi=true;
            store.Settings.DesktopDir=Path.Combine(profileDir,"desktop");Directory.CreateDirectory(store.Settings.DesktopDir);
            using(var main=new MainForm(store)){
                main.Show();Application.DoEvents();int forms=Application.OpenForms.Count;
                main.ShowLiveVoice();Application.DoEvents();
                var view=(HolographicCallView)typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main);Freeze(view);
                Check(view.Parent==main && main.Controls.GetChildIndex(view)==0,"One click replaces the software surface directly");
                Check(view.Bounds==main.ClientRectangle,"Hologram covers all software chrome");
                Check(Application.OpenForms.Count==forms,"Entering hologram opens no additional form");
                Key(view,Keys.Escape);Pump(()=>typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main)==null);
                Check(!main.IsDisposed,"Escape returns to the existing software");
                main.SetReferenceWorkspace(false);main.ShowLiveVoice();Application.DoEvents();
                view=(HolographicCallView)typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main);Freeze(view);
                Check(view.Bounds==main.ClientRectangle,"Classic software mode also becomes fully immersive");
                Key(view,Keys.Escape);Pump(()=>typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main)==null);main.Close();
            }
            storage.Save(settings);
            using(var settingsView=new SettingsCenterView(store,new ProviderStore())){
                var page=(Control)typeof(SettingsCenterView).GetMethod("PageVoice",Hidden).Invoke(settingsView,null);
                var key=(DarkInput)page.Controls.Find("hologram-key",false)[0];
                Check(key.Inner.UseSystemPasswordChar && key.Text=="","One-time settings keep the saved secret masked");
                Check(page.Controls.Find("hologram-name",false).Length==1 && page.Controls.Find("hologram-persona",false).Length==1,"Name and persona are edited only in normal settings");
                Check(page.Controls.Find("hologram-save",false).Length==1,"Voice setup lives in the normal settings center");page.Dispose();
            }
            using(var list=new DarkList {Size=new Size(460,240),AutoFitColumns=true}){
                list.SetColumns(new[]{"状态","标题","截止","项目"},new[]{56,520,100,0});
                list.SetRows(new List<ListRow>{new ListRow{Cells=new[]{"待办","完整的工程任务标题与详细验证要求","2026-12-30","原项目"}}});list.SelectIndex(0);
                var widths=(int[])typeof(DarkList).GetField("_colWidths",Hidden).GetValue(list);
                Check(widths[3]==0 && widths.Sum()<=list.Width-24 && widths[1]>widths[0],"Narrow lists preserve title space and hidden columns consume no width");
                Check(list.AccessibleDescription.Contains("完整的工程任务标题与详细验证要求") && list.AccessibleDescription.Contains("原项目"),"Keyboard selection exposes complete row content even when visual columns are compact");
            }
            Console.WriteLine("PASS "+checks+" native checks (fake voice transport; no live service or microphone used)");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }

    private static void CheckVoiceStability(RealtimeVoiceSettings settings)
    {
        var provider=new FakeProvider();FakeInput input=null;FakeOutput output=null;
        using(var view=new HolographicCallView(settings,p=>provider,r=>input=new FakeInput(),r=>output=new FakeOutput())){
            Freeze(view);view.Start();Pump(()=>input!=null);
            provider.Emit(new VoicePacket {Audio={new byte[32]}});Pump(()=>view.Mode==HologramMode.Speaking);
            input.Feeding=true;Pump(()=>provider.Sent.Count>0);
            Check(provider.Sent.All(b=>b.All(x=>x==0)),"Speaker playback suppresses microphone echo instead of triggering a new speech turn");
            Key(view,Keys.Space);Pump(()=>output.Flushed==1);
            provider.Emit(new VoicePacket {Caption="旧回复",Audio={new byte[32]}});
            Pump(()=>provider.Sent.Any(b=>b.Any(x=>x!=0)));
            Check(output.Played==0 && !view.Caption.Contains("旧回复") && provider.Connected==1,"Manual interruption discards late output and resumes input without reconnecting");
            output.Accept=false;
            provider.Emit(new VoicePacket {ResponseStarted=true,Caption="完整长回复文字",Audio={new byte[32]},TurnComplete=true});
            Pump(()=>view.Caption.Contains("完整文字"));
            Check(view.Mode!=HologramMode.Unavailable && provider.Connected==1,"A full audio buffer keeps the connection and transcript alive");
            provider.Emit(new VoicePacket {ResponseFailed=true,TurnComplete=true});Pump(()=>view.Caption.Contains("本轮回复未完成"));
            Check(view.Mode!=HologramMode.Unavailable && provider.Connected==1,"Qwen per-turn failure keeps the same voice connection available for another question");
            output.Accept=true;provider.Emit(new VoicePacket {ResponseStarted=true,Caption="下一轮",Audio={new byte[32]}});Pump(()=>view.Mode==HologramMode.Speaking);
            Key(view,Keys.Control|Keys.M);Pump(()=>view.Muted && input.Disposed);
            Key(view,Keys.Space);Pump(()=>view.Caption.Contains("仍静音"));
            Check(view.Muted && provider.Connected==1,"Interrupting playback preserves an explicitly muted microphone");
            var stop=view.StopAsync();Pump(()=>stop.IsCompleted);
        }
        settings.AllowInterruption=true;provider=new FakeProvider();
        using(var view=new HolographicCallView(settings,p=>provider,r=>new FakeInput {Feeding=true},r=>new FakeOutput())){
            Freeze(view);view.Start();Pump(()=>provider.Sent.Count>0);
            provider.Sent.Clear();provider.Emit(new VoicePacket {Audio={new byte[32]}});Pump(()=>view.Mode==HologramMode.Speaking && provider.Sent.Count>0);
            Check(provider.Sent.Any(b=>b.Any(x=>x!=0)),"Opt-in hands-free mode continues transmitting voice during playback");
            var stop=view.StopAsync();Pump(()=>stop.IsCompleted);
        }
        settings.AllowInterruption=false;
    }

    private static Bitmap ProjectionFrame(QuantumPresence presence,HologramDynamics dynamics,HologramMode mode,double seconds,Size size,bool reduced=false)
    {
        var image=new Bitmap(size.Width,size.Height);
        using(var g=Graphics.FromImage(image)){
            g.Clear(Color.FromArgb(2,5,11));presence.Draw(g,new Rectangle(Point.Empty,size),seconds,dynamics.Energy,mode,reduced,dynamics);
        }
        return image;
    }

    private static void CheckDynamicProjection(string output)
    {
        var quiet=new HologramDynamics();for(int i=0;i<60;i++)quiet.Advance(1.0/30,HologramMode.Listening,0,false);
        Check(quiet.Energy==0 && quiet.PulseAge.All(a=>a>1.25f),"Quiet time does not manufacture voice pulses");
        Check(HologramDynamics.FromRms(.003)==0 && HologramDynamics.FromRms(.035)>.2f,"The audio envelope ignores low-level noise and reacts to ordinary speech RMS");
        var active=new HologramDynamics();for(int i=0;i<20;i++)active.Advance(1.0/30,HologramMode.Speaking,.85f,false);
        Check(active.Energy>.8f && active.Speaking>.95f,"The speech envelope has a responsive attack");
        using(var renderer=new QuantumPresence())
        using(var a=ProjectionFrame(renderer,quiet,HologramMode.Listening,4,new Size(1280,720)))
        using(var b=ProjectionFrame(renderer,active,HologramMode.Speaking,4,new Size(1280,720))){
            a.Save(Path.Combine(output,"motion-listening.png"),ImageFormat.Png);b.Save(Path.Combine(output,"motion-speaking.png"),ImageFormat.Png);
            int cyan=0,violet=0,warm=0;
            for(int y=0;y<b.Height;y+=2)for(int x=0;x<b.Width;x+=2){var c=b.GetPixel(x,y);
                if(c.B>45 && c.G>c.R*1.18 && c.B>c.R*1.15)cyan++;
                if(c.B>45 && c.R>c.G*1.18 && c.B>c.G*1.18)violet++;
                if(c.R>45 && c.R>c.B*1.1 && c.G>c.B*.8)warm++;
            }
            Console.WriteLine("Spectral layers: cyan="+cyan+", violet="+violet+", warm="+warm);
            Check(cyan>150 && violet>80 && warm>30,"Cool, violet and warm particle layers are visible simultaneously");
            Check(Changed(a,b)>500 && Luminous(b)>Luminous(a),"Speaking changes the spatial field and light distribution");
        }
        for(int i=0;i<30;i++)active.Advance(1.0/30,HologramMode.Speaking,0,false);
        Check(active.Energy<.02f,"Silent PCM inside a speaking turn settles smoothly instead of looping a fake pulse");
        active.Interrupt();Check(active.Energy==0 && active.Speaking==0 && active.PulseAge.All(a=>a>1.25f),"Interrupting clears old energy and travelling wavefronts");
        var reduced=new HologramDynamics();for(int i=0;i<30;i++)reduced.Advance(1.0/30,HologramMode.Listening,.6f,true);
        Check(reduced.Flow==0 && reduced.PulseAge.All(a=>a>1.25f),"Reduced motion disables travelling waves and rotation");
        for(int i=0;i<15;i++)active.Advance(1.0/30,HologramMode.Speaking,.85f,false);
        using(var a=new Bitmap(1280,800))using(var b=new Bitmap(1280,800)){
            using(var g=Graphics.FromImage(a)){g.Clear(Color.FromArgb(2,5,11));SilverwingProjection.Draw(g,new Rectangle(0,0,1280,800),0,false,false,false,false,4,1,.75f,true,quiet);}
            using(var g=Graphics.FromImage(b)){g.Clear(Color.FromArgb(2,5,11));SilverwingProjection.Draw(g,new Rectangle(0,0,1280,800),.85,true,false,false,false,4,1,.75f,true,active);}
            b.Save(Path.Combine(output,"motion-full-body-photon.png"),ImageFormat.Png);
            Check(PhotonProjection.PointCount>10000 && Changed(a,b)>200,"The full-body particle character also responds to speech without replacing its reference silhouette");
        }
        var folder=Path.Combine(output,"motion-frames");Directory.CreateDirectory(folder);
        var demo=new HologramDynamics();
        using(var renderer=new QuantumPresence())for(int frame=0;frame<90;frame++){
            double t=frame/15.0;var mode=t<1.7 ? HologramMode.Listening : t<2.4 ? HologramMode.Thinking : t<4.9 ? HologramMode.Speaking : HologramMode.Listening;
            float signal=t>.9 && t<1.7 ? .32f : t>=2.4 && t<4.9 && (int)((t-2.4)*5)%4!=3 ? (float)(.35+.5*Math.Pow(Math.Sin(t*11),2)) : 0;
            if(frame==74)demo.Interrupt();demo.Advance(1.0/15,mode,signal,false);
            using(var image=ProjectionFrame(renderer,demo,mode,t+2,new Size(800,500)))
            using(var g=Graphics.FromImage(image))
            using(var font=new Font("Microsoft YaHei UI",12))
            using(var brush=new SolidBrush(Color.FromArgb(220,229,242,252)))
            using(var format=new StringFormat {Alignment=StringAlignment.Center}){
                string caption=t<.9 ? "安静倾听" : t<1.7 ? "你：展示今天的任务。" : t<2.4 ? "正在整理你的任务。" : t<4.9 ? "我为你整理了今天的任务。" : "已停止播报，继续倾听。";
                g.DrawString(caption,font,brush,new RectangleF(0,460,800,35),format);
                image.Save(Path.Combine(folder,"frame-"+frame.ToString("D3")+".png"),ImageFormat.Png);
            }
        }
    }

    private static void CheckVoiceRecords(ChatService history,RealtimeVoiceSettings settings)
    {
        var log=new VoiceConversation(history,settings);
        log.Accept(new VoicePacket {InputStarted=true,UserTurnId="late-user"});Thread.Sleep(3);
        log.Accept(new VoicePacket {Caption="已经查到"});
        log.Accept(new VoicePacket {UserCaption="请查看任务",UserTurnId="late-user"});
        log.Accept(new VoicePacket {Caption="相关信息。",TurnComplete=true});
        log.Accept(new VoicePacket {UserCaption="请查看任务",UserTurnId="late-user"});
        Check(log.Session.Messages[0].Role=="user" && log.Session.Messages.Count(m=>m.Role=="user")==1,"Late and repeated user transcription keeps its original turn and order");
        string longText=new string('详',340);
        log.Accept(new VoicePacket {Interrupted=true,InputStarted=true,UserTurnId="next-user"});
        log.Accept(new VoicePacket {UserCaption=longText,UserCaptionDelta=true,UserTurnId="next-user"});
        Check(log.LastCompletedUserText=="请查看任务","TIA draft transfer excludes partial transcription segments");
        log.Accept(new VoicePacket {Caption="待完成的回复"});log.Flush(true);
        var recovered=new ChatService(null,new ProviderStore()).Sessions.Single(s=>s.Id==log.Session.Id);
        Check(recovered.Messages.All(m=>!m.Streaming) && recovered.Messages.Last().Interrupted,"Restart recovers an interrupted transcript without an endless streaming cursor");
        log.Close();
        var saved=new ChatService(null,new ProviderStore()).Sessions.Single(s=>s.Id==log.Session.Id);
        Check(saved.Messages.Any(m=>m.Content==longText) && saved.Messages.Any(m=>m.Content=="已经查到相关信息。"),"Saved history keeps full transcripts beyond the subtitle limit and joins deltas once");
        Check(saved.AssistantName==settings.AssistantName && saved.Persona==settings.Persona && saved.Channel=="hologram" && saved.VoiceModel==settings.Model,"History retains the role and live-model identity used in that call");
        Check(!File.ReadAllText(Path.Combine(AppPaths.DataDir,"chats.json")).Contains(settings.ApiKey),"Voice history never stores the provider key");
        log.Accept(new VoicePacket {Caption="未读完的千问回复"});log.Accept(new VoicePacket {ResponseFailed=true,TurnComplete=true});
        Check(log.Session.Messages.Last().Interrupted && !log.Session.Messages.Last().Streaming,"A failed Qwen turn preserves its unfinished transcript without an endless streaming state");
        log.Accept(new VoicePacket {InputStarted=true});
        log.Accept(new VoicePacket {UserCaption="新工程需求",UserCaptionDelta=true,InputEnded=true});
        Check(log.LastCompletedUserText=="新工程需求","Finished Gemini input is available for explicit TIA draft transfer");
    }

    private static void CheckToolCards(string output)
    {
        using(var host=new Form{ClientSize=new Size(900,620)})
        using(var board=new ChatView.MessageBoard(Palette.For("cyber","#22D3EE")){Dock=DockStyle.Fill,UseCardSurface=true}){
            host.Controls.Add(board);host.Show();Application.DoEvents();
            var message=new ChatMessage{Id="tool-card",Role="tool",ToolName="task.list",Content="✅ {\"Tasks\":["+string.Join(",",Enumerable.Repeat("{\"Title\":\"超滤SIP的FS需要供应商更新步骤\",\"Status\":\"open\"}",50).ToArray())+"]}"};
            board.SetMessages(new List<ChatMessage>{message});board.Refresh();Application.DoEvents();
            int collapsed=(int)typeof(ChatView.MessageBoard).GetField("_contentH",Hidden).GetValue(board);
            Check(collapsed<140,"Long tool results are collapsed by default on the desktop");
            using(var image=Shot(board,new Size(900,620)))image.Save(Path.Combine(output,"tools-collapsed.png"),ImageFormat.Png);
            typeof(Control).GetMethod("OnMouseUp",Hidden).Invoke(board,new object[]{new MouseEventArgs(MouseButtons.Left,1,45,30,0)});board.Refresh();
            int expanded=(int)typeof(ChatView.MessageBoard).GetField("_contentH",Hidden).GetValue(board);
            Check(expanded>collapsed,"Clicking a tool card exposes the real result");
            using(var image=Shot(board,new Size(900,620)))image.Save(Path.Combine(output,"tools-expanded.png"),ImageFormat.Png);
            typeof(Control).GetMethod("OnMouseUp",Hidden).Invoke(board,new object[]{new MouseEventArgs(MouseButtons.Left,1,45,30,0)});board.Refresh();
            Check((int)typeof(ChatView.MessageBoard).GetField("_contentH",Hidden).GetValue(board)==collapsed,"The tool result can be folded back without losing its content");
        }
    }

    private static void CheckVoiceTools(Store store)
    {
        var tools=new HologramTools(()=>new ToolContext {Store=store});
        var read=tools.Execute(new VoiceToolCall {Id="tasks",Name="deskbox_tasks",Arguments=new Dictionary<string,object>{{"query","隔离测试任务"}}});
        Check(new JavaScriptSerializer().Serialize(read.Response).Contains("隔离测试任务"),"Live AI reads real synthetic application tasks through the existing tool registry");
        var operation=tools.Execute(new VoiceToolCall {Id="create",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"tool","task.create"},{"arguments","{\"title\":\"全息创建任务\",\"notes\":\"真实保存\"}"}}});
        Check(new JavaScriptSerializer().Serialize(operation.Response).Contains("\"ok\":true") && store.Tasks.Any(t=>t.Title=="全息创建任务"),"Hologram operations really create application tasks using the shared registry");
        int createdCount=store.Tasks.Count;
        var replay=tools.Execute(new VoiceToolCall {Id="create",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"arguments","{ \"notes\": \"真实保存\", \"title\": \"全息创建任务\" }"},{"tool","task.create"}}});
        Check(new JavaScriptSerializer().Serialize(replay.Response)==new JavaScriptSerializer().Serialize(operation.Response) && store.Tasks.Count==createdCount,"Replayed voice writes return the same receipt without creating duplicate tasks");
        var changed=tools.Execute(new VoiceToolCall {Id="create",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"tool","task.create"},{"arguments","{\"title\":\"不得重复创建\"}"}}});
        Check(new JavaScriptSerializer().Serialize(changed.Response).Contains("\"ok\":false") && store.Tasks.Count==createdCount,"Reusing a voice operation ID with different data cannot cause a second write");
        var large=new HologramTools(()=>new ToolContext {Store=store,AllowedRoots=new List<string>{new string('x',22000)}});
        var capped=large.Execute(new VoiceToolCall {Id="bounded",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"tool","app.context"},{"arguments","{}"}}});
        var bounded=new JavaScriptSerializer().Serialize(capped.Response);
        Check(bounded.Length<17000 && bounded.Contains("\"truncated\":true"),"Large operation results are bounded and explicitly marked before voice continuation");
        var denied=tools.Execute(new VoiceToolCall {Id="deny",Name="fs.write",Arguments=new Dictionary<string,object>{{"path","must-not-write"},{"content","x"}}});
        Check(new JavaScriptSerializer().Serialize(denied.Response).Contains("\"ok\":false"),"Unregistered operations cannot execute through realtime tools");
        var tiaTools = new HologramTools(() => new ToolContext {Store=store,TiaRead=name=>"导出证据："+name}, true);
        var tiaRead = tiaTools.Execute(new VoiceToolCall {Id="tia-read",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"tool","tia.read"},{"arguments","{\"name\":\"FB_FAULT\"}"}}});
        Check(new JavaScriptSerializer().Serialize(tiaRead.Response).Contains("导出证据：FB_FAULT"),"TIA voice uses the authorized read route");
        var tiaWrite = tiaTools.Execute(new VoiceToolCall {Id="tia-write",Name="deskbox_execute",Arguments=new Dictionary<string,object>{{"tool","task.create"},{"arguments","{\"title\":\"不允许从博图创建\"}"}}});
        Check(new JavaScriptSerializer().Serialize(tiaWrite.Response).Contains("\"ok\":false") && store.Tasks.Count==createdCount,"TIA voice cannot execute unrelated application mutations");
        var shown=tools.Execute(new VoiceToolCall {Id="show",Name="hologram_show",Arguments=new Dictionary<string,object>{{"title","测试投影"},{"content","来自 AI 的临时要点"},{"seconds",9999}}});
        Check(tools.Information!=null && tools.Information.ExpiresUtc<=DateTime.UtcNow.AddSeconds(121),"Temporary information has a bounded lifetime");
        tools.Expire(DateTime.UtcNow.AddSeconds(122));Check(tools.Information==null,"Information expires independently of animation and reduced motion");
        var invalid=tools.Execute(new VoiceToolCall {Id="invalid",Name="hologram_show",Arguments=new Dictionary<string,object>{{"title","过长内容"},{"content",new string('字',481)}}});
        Check(tools.Information==null && new JavaScriptSerializer().Serialize(invalid.Response).Contains("\"ok\":false"),"Oversized display requests return a tool error without changing the projection");
        var parsed=GeminiLiveVoiceProvider.ParsePacket("{\"toolCall\":{\"functionCalls\":[{\"id\":\"g1\",\"name\":\"hologram_show\",\"args\":{\"title\":\"摘要\",\"content\":\"信息\"}}]}}");
        Check(parsed.ToolCalls.Count==1 && parsed.ToolCalls[0].Arguments["content"].ToString()=="信息","Gemini function-call packets reach the bounded display tool");
        Check(GeminiLiveVoiceProvider.ParsePacket("{\"serverContent\":{\"inputTranscription\":{\"text\":\"说完了\",\"finished\":true}}}").InputEnded,"Gemini finished transcription can enter the thinking state before speech output");
        var response=GeminiLiveVoiceProvider.ToolResponseJson(new[]{shown});
        Check(response.Contains("functionResponses") && response.Contains("\"id\":\"show\"") && response.Contains("\"ok\":true"),"Gemini receives correlated tool execution feedback");
        using(var qwen=new QwenLiveVoiceProvider("wss://fixture.invalid/live","fixture","not-a-key")){
            Check(qwen.ParsePacket("{\"type\":\"response.done\",\"response\":{\"status\":\"failed\"}}").ResponseFailed,"Qwen response failure is a recoverable turn result");
            Check(!qwen.ParsePacket("{\"type\":\"error\",\"error\":{\"code\":\"response_cancel_not_active\"}}").Interrupted,"A harmless Qwen cancel race does not end the session");
            qwen.ParsePacket("{\"type\":\"response.created\"}");
            var packet=qwen.ParsePacket("{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"q1\",\"name\":\"hologram_clear\",\"arguments\":\"{}\"}");
            Check(packet.ToolCalls.Count==0,"Qwen waits for the active response before continuing tool calls");
            packet=qwen.ParsePacket("{\"type\":\"response.done\",\"response\":{\"status\":\"completed\"}}");
            Check(packet.ToolCalls.Count==1 && packet.ToolCalls[0].Id=="q1","Qwen completed function calls keep the call ID");
            qwen.ParsePacket("{\"type\":\"response.created\"}");
            qwen.ParsePacket("{\"type\":\"response.function_call_arguments.done\",\"call_id\":\"cancel\",\"name\":\"hologram_clear\",\"arguments\":\"{}\"}");
            qwen.ParsePacket("{\"type\":\"input_audio_buffer.speech_started\",\"item_id\":\"u1\"}");
            Check(qwen.ParsePacket("{\"type\":\"response.done\",\"response\":{\"status\":\"cancelled\"}}").ToolCalls.Count==0,"Interrupted Qwen tool requests never change the projection");
            string output=new JavaScriptSerializer().Serialize(QwenLiveVoiceProvider.ToolOutput(shown));
            Check(output.Contains("function_call_output") && output.Contains("\"call_id\":\"show\""),"Qwen receives correlated function output before continuing speech");
            var input=qwen.ParsePacket("{\"type\":\"conversation.item.input_audio_transcription.completed\",\"item_id\":\"u1\",\"transcript\":\"用户转写\"}");
            Check(input.UserTurnId=="u1" && !input.UserCaptionDelta,"Qwen complete transcriptions have stable turn identity");
        }
    }

    private sealed class FakeInput : IVoiceInput { public bool Disposed,Feeding;public int Read(short[] samples){if(!Feeding)return 0;for(int i=0;i<160;i++)samples[i]=1000;return 160;}public void Dispose(){Disposed=true;} }
    private sealed class FakeOutput : IVoiceOutput {public int Played,Flushed;public double Amplitude=.16;public bool Disposed,Accept=true;public bool Play(byte[] pcm){if(!Accept)return false;Played++;return true;}public void Flush(){Flushed++;Played=0;}public double? PlaybackLevel(){return Played>0 ? (double?)Amplitude:null;}public void Dispose(){Disposed=true;} }
    private sealed class FakeProvider : IRealtimeVoiceProvider
    {
        public int Connected,Ended;public bool Disposed,HoldConnect,FailConnect;private TaskCompletionSource<VoicePacket> waiting;
        private readonly Queue<VoicePacket> packets=new Queue<VoicePacket>();
        public readonly List<VoiceToolResult> ToolResults=new List<VoiceToolResult>();
        public int InputSampleRate{get{return 16000;}}public int OutputSampleRate{get{return 24000;}}
        public Task ConnectAsync(CancellationToken token){Connected++;if(FailConnect)throw new IOException("fixture failure");if(!HoldConnect)return Task.FromResult(0);var t=new TaskCompletionSource<bool>();token.Register(()=>t.TrySetCanceled());return t.Task;}
        public readonly List<byte[]> Sent=new List<byte[]>();
        public Task SendAudioAsync(byte[] pcm,CancellationToken token){Sent.Add(pcm);return Task.FromResult(0);}public Task EndInputAsync(CancellationToken token){Ended++;return Task.FromResult(0);}
        public Task CompleteToolsAsync(IList<VoiceToolResult> results,CancellationToken token){ToolResults.AddRange(results);return Task.FromResult(0);}
        public Task<VoicePacket> ReceiveAsync(CancellationToken token){if(packets.Count>0)return Task.FromResult(packets.Dequeue());waiting=new TaskCompletionSource<VoicePacket>();var pending=waiting;token.Register(()=>pending.TrySetCanceled());return pending.Task;}
        public void Emit(VoicePacket packet){if(waiting==null || !waiting.TrySetResult(packet))packets.Enqueue(packet);}
        public void Dispose(){Disposed=true;}
    }
}
