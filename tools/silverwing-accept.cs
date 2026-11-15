using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
            var settings=new RealtimeVoiceSettings {ApiKey="fixture-key-never-send",Appearance="quantum"};
            storage.Save(settings);var restored=new RealtimeVoiceSettingsStore().Load();
            Check(restored.ApiKey==settings.ApiKey && restored.Model==settings.Model,"Voice profile survives reopening");
            Check(!File.ReadAllText(Path.Combine(profileDir,"realtime-voice.json")).Contains(settings.ApiKey),"No plaintext secret is persisted");
            Check(restored.Ready,"Persisted profile can be used without reopening setup");
            var setup=new JavaScriptSerializer().Serialize(QwenLiveVoiceProvider.Setup(QwenLiveVoiceProvider.DefaultModel));
            Check(!setup.Contains("tools") && !setup.Contains("deskbox_show_scene"),"Voice model has no scene tools");
            Check(typeof(HolographicCallView).BaseType==typeof(UserControl),"Hologram is an application surface, not a form or plugin");

            var fake=new FakeProvider();int inputs=0;FakeInput lastInput=null;FakeOutput speaker=null;
            using(var host=new Form {ClientSize=new Size(1280,800)})
            using(var view=new HolographicCallView(settings,p=>fake,r=>{inputs++;return lastInput=new FakeInput();},r=>speaker=new FakeOutput())){
                Freeze(view);host.Controls.Add(view);host.Show();Application.DoEvents();
                int forms=Application.OpenForms.Count;view.Start();Pump(()=>inputs==1);
                Check(fake.Connected==1 && view.Mode==HologramMode.Listening,"Entering starts exactly one connection automatically");
                view.Start();Check(fake.Connected==1,"Repeated entry does not duplicate the session");
                Check(view.Controls.Count==0 && Application.OpenForms.Count==forms,"Call surface contains no setup controls or secondary windows");
                fake.Emit(new VoicePacket {Caption="你好，",Audio={new byte[32]}});Pump(()=>view.Caption=="你好，");
                fake.Emit(new VoicePacket {Caption="我在这里。",TurnComplete=true});Pump(()=>view.Caption=="你好，我在这里。");
                Check(speaker.Played==1,"Real transport packets drive audio and captions");
                Check((float)typeof(HolographicCallView).GetField("targetLevel",Hidden).GetValue(view)>.5f,"Actual playback level drives the light field");
                Key(view,Keys.Control|Keys.M);Pump(()=>lastInput.Disposed);
                Check(view.Muted && fake.Ended==1,"Microphone is released on mute");
                Key(view,Keys.Control|Keys.M);Pump(()=>inputs==2);Check(!view.Muted,"Unmute reopens input without reconnecting");
                fake.Emit(new VoicePacket {Interrupted=true,UserCaption="先听我说"});Pump(()=>view.Caption=="你：先听我说");
                Check(speaker.Flushed==1,"Interruption flushes queued playback");
                fake.Emit(new VoicePacket {Caption="我在听，请继续。",Audio={new byte[32]}});Pump(()=>view.Mode==HologramMode.Speaking);
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
                host.Close();
            }
            var pending=new FakeProvider {HoldConnect=true};int unopened=0;
            using(var view=new HolographicCallView(settings,p=>pending,r=>{unopened++;return new FakeInput();},r=>new FakeOutput())){
                Freeze(view);view.Start();var ending=view.StopAsync();Pump(()=>ending.IsCompleted);
                Check(unopened==0 && pending.Disposed,"Cancelling connection never opens a late microphone");
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
            var store=new Store();store.Init();store.Settings.TakeoverIcons=false;store.Settings.AutoStart=false;store.Settings.ReferenceUi=true;
            store.Settings.DesktopDir=Path.Combine(profileDir,"desktop");Directory.CreateDirectory(store.Settings.DesktopDir);
            using(var main=new MainForm(store)){
                main.Show();Application.DoEvents();int forms=Application.OpenForms.Count;
                main.ShowLiveVoice();Application.DoEvents();
                var view=(HolographicCallView)typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main);Freeze(view);
                Check(view.Parent==main && main.Controls.GetChildIndex(view)==0,"One click replaces the software surface directly");
                Check(Application.OpenForms.Count==forms,"Entering hologram opens no additional form");
                Key(view,Keys.Escape);Pump(()=>typeof(MainForm).GetField("_liveVoice",Hidden).GetValue(main)==null);
                Check(!main.IsDisposed,"Escape returns to the existing software");main.Close();
            }
            using(var settingsView=new SettingsCenterView(store,new ProviderStore())){
                var page=(Control)typeof(SettingsCenterView).GetMethod("PageVoice",Hidden).Invoke(settingsView,null);
                var key=(DarkInput)page.Controls.Find("hologram-key",false)[0];
                Check(key.Inner.UseSystemPasswordChar && key.Text=="","One-time settings keep the saved secret masked");
                Check(page.Controls.Find("hologram-save",false).Length==1,"Voice setup lives in the normal settings center");page.Dispose();
            }
            Console.WriteLine("PASS "+checks+" native checks (fake voice transport; no live service or microphone used)");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }

    private sealed class FakeInput : IVoiceInput { public bool Disposed;public int Read(short[] samples){return 0;}public void Dispose(){Disposed=true;} }
    private sealed class FakeOutput : IVoiceOutput {public int Played,Flushed;public bool Disposed;public void Play(byte[] pcm){Played++;}public void Flush(){Flushed++;Played=0;}public double? PlaybackLevel(){return Played>0 ? (double?).16:null;}public void Dispose(){Disposed=true;} }
    private sealed class FakeProvider : IRealtimeVoiceProvider
    {
        public int Connected,Ended;public bool Disposed,HoldConnect,FailConnect;private TaskCompletionSource<VoicePacket> waiting;
        public int InputSampleRate{get{return 16000;}}public int OutputSampleRate{get{return 24000;}}
        public Task ConnectAsync(CancellationToken token){Connected++;if(FailConnect)throw new IOException("fixture failure");if(!HoldConnect)return Task.FromResult(0);var t=new TaskCompletionSource<bool>();token.Register(()=>t.TrySetCanceled());return t.Task;}
        public Task SendAudioAsync(byte[] pcm,CancellationToken token){return Task.FromResult(0);}public Task EndInputAsync(CancellationToken token){Ended++;return Task.FromResult(0);}
        public Task<VoicePacket> ReceiveAsync(CancellationToken token){waiting=new TaskCompletionSource<VoicePacket>();token.Register(()=>waiting.TrySetCanceled());return waiting.Task;}
        public void Emit(VoicePacket packet){if(waiting==null || !waiting.TrySetResult(packet))throw new Exception("No pending receiver");}
        public void Dispose(){Disposed=true;}
    }
}
