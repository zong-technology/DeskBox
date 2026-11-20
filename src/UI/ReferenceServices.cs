using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildServicePage(string id)
        {
            if(id=="automation")return BuildWorkflowStudio();
            var names=new Dictionary<string,string>{{"ai","AI 助手"},{"automation","自动化工作流"},{"search","工程文件中心"}};
            var page=Page(names[id],id=="ai"?"结合工程文件和当前项目上下文分析问题":id=="automation"?"预演、执行与工作流状态":"文件名、格式、时间、大小与项目范围检索");
            var view=_legacyFactory(id) as Views.ViewBase;
            if(view==null)return page;view.ApplyPalette(_p);view.UseReferenceLayout();
            _serviceViews[id]=view;
            var toolbar=Toolbar();
            DarkButton allFiles = null, currentProject = null;
            if(id=="ai")
            {
                SetSampleHeading(page, "ENGINEERING COPILOT", "全息 AI 通话", () =>
                {
                    var main = FindForm() as MainForm;
                    if (main != null) main.ShowLiveVoice();
                });
                toolbar.WrapContents = true;
                toolbar.AutoSize = true;
                toolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                if(_providers!=null)foreach(var provider in _providers.Providers.Where(x=>x.Enabled)){var p=provider;toolbar.Controls.Add(Button(p.Name,()=>{if(_chat!=null)_chat.SetProvider(p.Id,p.Model);},_chat!=null&&_chat.Current!=null&&_chat.Current.ProviderId==p.Id,130));}
                toolbar.Controls.Add(Button("博图 AI 工程师",()=>Navigate("tia"),false,140));
                toolbar.Controls.Add(Button("新对话",()=>_chat.NewSession(),false,90));toolbar.Controls.Add(Button("停止",()=>_chat.Cancel(),false,70));toolbar.Controls.Add(Button("参数设置",()=>{var chat=view as Views.ChatView;if(chat!=null)chat.ToggleReferenceOptions();},false,120));toolbar.Controls.Add(Button("服务商设置",()=>ShowLegacy("settings"),false,120));

                toolbar.Controls.Add(Button("4–20 mA 换算", () => Navigate("calculator"), false, 126));
                toolbar.Controls.Add(Button("PLC / Modbus", () =>
                {
                    Navigate("engineering");
                    var mode = _pages["engineering"].Controls.Find("engineering-tool-mode", true).FirstOrDefault() as DarkSelect;
                    if (mode != null) mode.SelectedIndex = 1;
                }, false, 126));
                toolbar.Controls.Add(Button("麦克风检查",()=>{using(var dialog=new VoiceInputCheckForm())dialog.ShowDialog(FindForm());},false,120));
            }
            else if(id=="automation"){toolbar.Controls.Add(Button("工作流列表",()=>view.Activate(),true,120));toolbar.Controls.Add(Button("运行记录",()=>view.Activate(),false,110));}
            else
            {
                var search = (Views.SearchView)view;
                allFiles = Button("全部文件", () => search.SetProjectScope(null, null), true, 110);
                currentProject = Button("当前项目", () => {
                    var project = CurrentProject();
                    if (project != null) search.SetProjectScope(project.Root, project.Name);
                }, false, 110);
                toolbar.Controls.Add(allFiles);
                toolbar.Controls.Add(currentProject);
            }
            Control workspace=view;
            IndexedFolderTree tree = null;
            if(id=="search")
            {
                var shell=new Panel{Dock=DockStyle.Fill};var preview=new PreviewPanel(_store){Dock=DockStyle.Right,Width=320};var categories=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=190,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,8,10,0),AutoScroll=true};
                var search=(Views.SearchView)view;
                foreach(var entry in new[]{new[]{"全部文件","0"},new[]{"Word / 文档","1"},new[]{"Excel / 表格","2"},new[]{"图片","3"},new[]{"PDF","9"}}){int type=int.Parse(entry[1]);categories.Controls.Add(Button(entry[0],()=>search.FilterType(type),false,125));}
                var folders = new Panel { Dock = DockStyle.Left, Width = 168, Padding = new Padding(0, 0, 8, 0) };
                var heading = TextLabel("文件目录", 10, true);
                heading.Dock = DockStyle.Top;
                heading.Height = 27;
                tree = new IndexedFolderTree(_p) { Dock = DockStyle.Fill };
                tree.ScopeSelected += search.SetProjectScope;
                search.FolderScopeChanged += path =>
                {
                    tree.ShowScope(path);
                    allFiles.Primary = path == null;
                    var project = CurrentProject();
                    currentProject.Primary = project != null && string.Equals(project.Root, path, StringComparison.OrdinalIgnoreCase);
                    allFiles.Invalidate();
                    currentProject.Invalidate();
                };
                search.Searched += result => preview.ClosePreview();
                folders.Controls.Add(tree);
                folders.Controls.Add(categories);
                folders.Controls.Add(heading);
                shell.Resize += delegate { preview.Width = Math.Max(180, Math.Min(320, shell.ClientSize.Width / 4)); };
                string tiaFile = null;
                var tia = Button("在博图工作台分析", () => { var handler = TiaFileRequested; if (tiaFile != null && handler != null) handler(tiaFile); }, false, 144);
                tia.Enabled = false; toolbar.Controls.Add(tia);
                search.Searched += result => { tiaFile = null; tia.Enabled = false; };
                search.EntrySelected += file => { preview.ShowPreview(file.Path); tiaFile = !file.IsDir && new[] { ".aml", ".xml", ".scl", ".udt", ".db", ".csv" }.Contains(Path.GetExtension(file.Path).ToLowerInvariant()) ? file.Path : null; tia.Enabled = tiaFile != null; };preview.OpenRequested+=OpenPath;preview.RevealRequested+=path=>DeskBox.Native.Win32.RevealPath(path);preview.FavoriteRequested+=path=>_library.AddFavorite(path,"");preview.AiRequested += path => { var handler = FileAiRequested; if (handler != null) handler(path); };
                shell.Controls.Add(view);shell.Controls.Add(preview);shell.Controls.Add(folders);view.BringToFront();workspace=shell;
            }
            AddMain(page,workspace,toolbar);_reload[id]=()=>{view.Activate();var search=view as Views.SearchView;if(search!=null){tree.RefreshRoots(DeskBox.Fs.FileIndex.Instance.Roots);tree.ShowScope(search.FolderScope);search.DoSearch();}};return page;
        }
        private Control BuildTasks(){return BuildBusinessPage("tasks","任务与提醒","任务概览、截止日期与完成状态");}
        private Control BuildIncidents(){return BuildBusinessPage("incidents","现场事件","项目、设备位号、调查和处理措施");}
        private Control BuildBusinessPage(string id,string title,string subtitle)
        {
            var page=Page(title,subtitle);var view=_legacyFactory(id) as Views.ViewBase;if(view==null)return page;view.ApplyPalette(_p);view.UseReferenceLayout();AddMain(page,view);_reload[id]=()=>view.Activate();return page;
        }
        private WeatherSnapshot _weather;
        private bool _weatherBusy;
        private Control BuildWeather()
        {
            var page=Page("天气","实况、逐小时趋势与未来预报");var toolbar=Toolbar();toolbar.Controls.Add(Button("选择 / 搜索城市",()=>{ShowLegacy("weather");Action refreshOne;if(_reload.TryGetValue("weather",out refreshOne))refreshOne();},false,160));
            var panel=Card();panel.Dock=DockStyle.Fill;
            var city=TextLabel("城市天气",16,true);city.SetBounds(24,20,600,40);panel.Controls.Add(city);
            var icon=new Label{Text="\uE753",Font=new Font("Segoe MDL2 Assets",58),ForeColor=Color.FromArgb(255,204,74),BackColor=Color.Transparent,Bounds=new Rectangle(28,90,100,100)};panel.Controls.Add(icon);
            var temperature=TextLabel("--°C",38,true);temperature.SetBounds(144,92,300,70);panel.Controls.Add(temperature);var condition=TextLabel("正在加载…",11,false,_p.TextDim);condition.SetBounds(150,176,350,32);panel.Controls.Add(condition);
            var details=TextLabel("",10,false,_p.TextDim);details.SetBounds(30,230,570,72);panel.Controls.Add(details);
            var chart=new Panel{Left=600,Top=72,Width=640,Height=240,Anchor=AnchorStyles.Top|AnchorStyles.Right|AnchorStyles.Left,BackColor=Color.Transparent};panel.Controls.Add(chart);
            chart.Paint+=delegate(object s,PaintEventArgs e){var hours=_weather==null?new List<HourlyPoint>():_weather.Hours.Take(24).ToList();var bounds=new Rectangle(24,30,Math.Max(40,chart.Width-48),Math.Max(40,chart.Height-68));if(hours.Count<2){TextRenderer.DrawText(e.Graphics,"暂无逐小时数据",FontOf(10),bounds,_p.TextDim,TextFormatFlags.HorizontalCenter);return;}double min=hours.Min(h=>h.Temp)-2,max=hours.Max(h=>h.Temp)+2;var points=hours.Select((h,i)=>new PointF(bounds.Left+i*bounds.Width/(float)(hours.Count-1),bounds.Bottom-(float)((h.Temp-min)/(max-min)*bounds.Height))).ToArray();using(var pen=new Pen(_p.Line))for(int i=0;i<4;i++)e.Graphics.DrawLine(pen,bounds.Left,bounds.Top+i*bounds.Height/3,bounds.Right,bounds.Top+i*bounds.Height/3);using(var pen=new Pen(_p.Accent,2))e.Graphics.DrawLines(pen,points);for(int i=0;i<points.Length;i++){using(var brush=new SolidBrush(_p.Accent))e.Graphics.FillEllipse(brush,points[i].X-4,points[i].Y-4,8,8);if(i%3!=0&&i!=points.Length-1)continue;TextRenderer.DrawText(e.Graphics,hours[i].Temp.ToString("0.#")+"°",FontOf(9),new Point((int)points[i].X-16,(int)points[i].Y-26),_p.Text);DateTime time;var label=DateTime.TryParse(hours[i].Time,out time)?time.ToString("HH:mm"):hours[i].Time;TextRenderer.DrawText(e.Graphics,label,FontOf(8),new Point((int)points[i].X-20,bounds.Bottom+16),_p.TextDim);}};
            var days=new FlowLayoutPanel{Left=24,Top=355,Width=1100,Height=150,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right,WrapContents=false,AutoScroll=true,BackColor=Color.Transparent};panel.Controls.Add(days);
            var source=TextLabel("",9,false,_p.TextDim);source.SetBounds(24,540,1100,38);panel.Controls.Add(source);
            panel.Resize+=delegate{int left=Math.Max(430,panel.Width*44/100);days.Top=Math.Max(310,panel.Height-170);source.Top=Math.Max(450,panel.Height-42);chart.SetBounds(left,72,Math.Max(200,panel.Width-left-28),Math.Min(240,Math.Max(150,days.Top-105)));days.Width=Math.Max(300,panel.Width-48);source.Width=Math.Max(300,panel.Width-48);};
            Action reload=delegate{if(_weatherBusy)return;_weatherBusy=true;condition.Text="正在更新天气…";Task.Run(()=>WeatherService.Get(_store.Settings,false)).ContinueWith(task=>{if(IsDisposed||!IsHandleCreated)return;BeginInvoke(new Action(delegate{_weatherBusy=false;if(task.IsFaulted){condition.Text="天气更新失败，可重试";return;}_weather=task.Result;bool real=_weather.Source!="fallback";city.Text=_weather.City;temperature.Text=real?_weather.Temp.ToString("0")+"°C":"--°C";condition.Text=real?_weather.Text:"天气暂不可用";details.Text=real?"体感 "+_weather.Feels.ToString("0")+"°C    湿度 "+_weather.Humidity+"%\r\n风速 "+_weather.WindSpeed.ToString("0.#")+" m/s    降水概率 "+_weather.PrecipProb+"%":"请选择城市或检查网络";source.Text="Open-Meteo · "+_weather.Source+" · "+_weather.UpdatedText;foreach(Control c in days.Controls.Cast<Control>().ToArray())c.Dispose();days.Controls.Clear();foreach(var day in _weather.Days.Take(7)){var card=Card();card.Size=new Size(138,122);var label=TextLabel(day.Weekday+"\r\n\r\n"+day.Text+"\r\n"+day.TMin.ToString("0")+"° / "+day.TMax.ToString("0")+"°",10);label.Dock=DockStyle.Fill;card.Controls.Add(label);days.Controls.Add(card);}chart.Invalidate();}));});};
            toolbar.Controls.Add(Button("刷新预报",()=>reload(),true,120));_reload["weather"]=reload;AddMain(page,panel,toolbar);return page;
        }
        private Control BuildMobile()
        {
            var page=Page("移动同步","账号自动同步与局域网服务分别显示，沿用现有设备授权");var toolbar=Toolbar();toolbar.Controls.Add(Button("同步概览",()=>RefreshStatus(),true,110));toolbar.Controls.Add(Button("设备 / 配对设置",()=>ShowLegacy("mobile"),false,160));toolbar.Controls.Add(Button("立即云同步",async()=>await _cloud.SyncNow(),false,140));
            var body=new Panel{Dock=DockStyle.Fill};var upper=new TableLayoutPanel{Dock=DockStyle.Top,Height=320,ColumnCount=2,RowCount=1};upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));upper.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));
            var state=Card("本机同步服务");state.Dock=DockStyle.Fill;var info=TextLabel("",11);info.SetBounds(20,65,660,175);state.Controls.Add(info);var local=Button("启动 / 停止局域网",()=>{if(_sync.Running)_sync.Stop();else _sync.Start();_store.Settings.LocalSyncEnabled=_sync.Running;_store.SaveSettingsOnly();Action refreshOne;if(_reload.TryGetValue("mobile",out refreshOne))refreshOne();},false,185);local.SetBounds(20,260,185,34);state.Controls.Add(local);
            var qrCard=Card("手机扫码登录云账号");qrCard.Dock=DockStyle.Fill;var qr=new PictureBox{Name="CloudLoginQr",Left=20,Top=62,Size=new Size(148,148),SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.White};qrCard.Controls.Add(qr);var qrText=TextLabel("",9,false,_p.TextDim);qrText.SetBounds(184,75,240,100);qrCard.Controls.Add(qrText);var copy=Button("复制云登录地址",()=>{if(!string.IsNullOrEmpty(_cloud.AccountUrl))Clipboard.SetText(_cloud.AccountUrl);},false,154);copy.SetBounds(184,190,154,34);qrCard.Controls.Add(copy);qrCard.Resize+=delegate{qrText.Width=Math.Max(100,qrCard.Width-200);};
            var devices=List(new[]{"设备名称","平台","信任状态","最近活动","有效期"},new[]{300,150,180,230,230});devices.RowActivated+=delegate{ShowLegacy("mobile");};
            var caption=TextLabel("信任设备 · 局域网列表，云端设备在账号网页管理",10,true);caption.Dock=DockStyle.Top;caption.Height=44;
            body.Controls.Add(devices);body.Controls.Add(caption);body.Controls.Add(upper);upper.Controls.Add(state,0,0);upper.Controls.Add(qrCard,1,0);
            string generated="";Action reload=delegate{info.Text="LAN   "+(_sync.Running?"ONLINE · TLS "+_sync.Port:"OFFLINE")+"\r\n\r\nCLOUD   "+_cloud.Status+"\r\n最近云同步  "+(_cloud.LastSyncedAt.HasValue?_cloud.LastSyncedAt.Value.ToString("yyyy-MM-dd HH:mm:ss"):"尚未完成")+"   待上传 "+_cloud.PendingUploads+"\r\n手机 AI  "+(_providers.DefaultProvider!=null&&_providers.DefaultProvider.Enabled?"已准备，需电脑保持运行":"请先配置电脑AI服务商")+"\r\n\r\n现场事件 "+_sync.Incidents.Count+"    未处理冲突 "+_sync.Sync.Conflicts.Count(c=>c.Status=="open");devices.SetRows(_sync.Devices.Devices.Select(d=>new ListRow{Cells=new[]{d.Name,d.Platform,d.Revoked?"已撤销":"已信任",TimeText(d.LastSeenAt),TimeText(d.ExpiresAt)},Tag=d.Id}).ToList());qrText.Text=_cloud.AccountUrl.Length==0?"先在设备设置填写云服务地址。也可继续使用局域网完整链接配对。":"使用iPhone相机扫码，登录同一私人云账号并授权手机。\r\n\r\n"+_cloud.AccountUrl;if(generated!=_cloud.AccountUrl&&_cloud.AccountUrl.Length>0){generated=_cloud.AccountUrl;Task.Run(()=>DocumentSkills.Run(new Dictionary<string,string>{{"op","qrcode"},{"content",generated}})).ContinueWith(task=>{if(qr.IsDisposed||!qr.IsHandleCreated)return;qr.BeginInvoke(new Action(delegate{if(task.IsFaulted||!task.Result.Ok){qrText.Text+="\r\n二维码组件未就绪，可复制地址登录。";return;}try{using(var memory=new MemoryStream(Convert.FromBase64String(task.Result.Text)))using(var image=Image.FromStream(memory)){var old=qr.Image;qr.Image=new Bitmap(image);if(old!=null)old.Dispose();}}catch{qrText.Text+="\r\n二维码未能生成，请复制地址。";}}));});}};
            _reload["mobile"]=reload;AddMain(page,body,toolbar);return page;
        }
        private static string TimeText(long value){return value<=0?"—":DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm");}
    }
}
