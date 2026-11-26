using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Automation;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Fs;
using DeskBox.Sync;
using DeskBox.UI;
using DeskBox.UI.Views;

public class ReferenceUiAccept
{
    static int pass,fail;
    static void Check(string label,bool ok){if(ok)pass++;else fail++;Console.WriteLine((ok?"PASS ":"FAIL ")+label);}
    static void Pump(int milliseconds){var stop=DateTime.UtcNow.AddMilliseconds(milliseconds);while(DateTime.UtcNow<stop){Application.DoEvents();Thread.Sleep(15);}}
    [STAThread] public static int Main()
    {
        Application.EnableVisualStyles();var root=Path.Combine(Path.GetTempPath(),"deskbox-reference-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);AppPaths.DataDir=Store.DataDir=root;var store=new Store();store.Init();store.Settings.TakeoverIcons=false;
        var projects=new WorkspaceStore();for(int i=0;i<3;i++){var folder=Path.Combine(root,"Project-"+i);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"IO List.csv"),"Tag,Description\nPT-101,Pressure");File.WriteAllText(Path.Combine(folder,"OQ-Test-Plan.md"),"# OQ 验证计划\n压力联锁检查");var p=projects.Add("制药设备工程项目 "+(i+1),folder,i==0?"PLC":i==1?"DCS":"CSV");projects.UpdateProjectDetails(p.Id,"SH-2026-0"+(i+1),new[]{"配液系统","冻干机","纯化水"}[i],"测试工程师","2026-10-20","2026-11-20","medium");var steps=ValidationProgress.Create();for(int j=0;j<steps.Count;j++)steps[j].Percent=j<12?100:(double?)0;projects.SaveValidation(p.Id,steps,"2026-10-20","2026-11-20","测试样本",folder);}
        projects.SetCurrent(projects.Items[0].Id);for(int i=0;i<8;i++)store.Tasks.Add(new TaskItem{Id="TASK"+i,Title=new[]{"校验压力联锁与报警","核对 IO List 位号","整理 FAT / SAT 证据","更新 OQ 测试计划"}[i%4],Priority=i==0?"high":"normal",Done=i>4,Due=DateTime.Today.AddDays(i-1).ToString("yyyy-MM-dd"),Project=projects.Items[i%3].Id,CreatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});store.SaveTasksOnly();
        var library=new FileLibrary(store);foreach(var p in projects.Items)foreach(var file in Directory.GetFiles(p.Root))library.Touch(file);
        var index=FileIndex.Instance;index.Configure(projects.Items.Select(p=>p.Root).ToList(),new List<string>());index.Rebuild();
        for(int attempt=0;attempt<100&&index.IsIndexing;attempt++)Pump(30);Check("搜索样本索引已建立",index.Count>=6);
        var providers=new ProviderStore();providers.Add(new AiProvider{Name="DeepSeek（测试配置）",Model="mock",BaseUrl="http://127.0.0.1:1",ApiKey="test-only"});var chat=new ChatService(store,providers);chat.Current.Messages.Add(new ChatMessage{Role="user",Content="请检查项目的压力联锁验证要求。"});chat.Current.Messages.Add(new ChatMessage{Role="assistant",Content="## 验证建议\n\n1. 核对 IO List 和报警位号。\n2. 记录联锁触发条件与接受标准。\n3. 保留原始证据并执行复核。\n\n可调用应用任务、项目和文档工具继续检查。"});
        var workflows=new WorkflowEngine(store);
        foreach(var name in new[]{"每日文件备份","PLC 程序版本对比","设备 FAT / SAT 检查","IO 清单生成任务"}){var flow=workflows.Add(name,"manual",60);flow.Actions.Add(new WorkflowStep{Type="log",Params=new Dictionary<string,string>{{"message","测试流程"},{"root",root}}});}workflows.Save();
        var weather=new WeatherSnapshot{City="上海（测试缓存）",Source="cache",Temp=24,Feels=25,Humidity=65,WindSpeed=3,Text="多云",UpdatedAt=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()};for(int i=0;i<9;i++)weather.Hours.Add(new HourlyPoint{Time=DateTime.Today.AddHours(i+8).ToString("s"),Temp=24-i*0.5,Text="多云"});for(int i=0;i<7;i++)weather.Days.Add(new WeatherDay{Date=DateTime.Today.AddDays(i).ToString("yyyy-MM-dd"),Text="多云",TMin=20+i,TMax=26+i});typeof(WeatherService).GetField("_cache",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,weather);typeof(WeatherService).GetField("_cacheAt",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,weather.UpdatedAt);
        using(var sync=new LocalSyncCoordinator(store,projects))using(var cloud=new CloudSyncAgent(sync))
        {
            sync.SaveIncident(new IncidentDto{Id="INC1",ProjectId=projects.Items[0].Id,Equipment="PT-101",Description="压力反馈偏移，联锁需复核",Severity="S1",Status="investigating",Owner="测试工程师",Cause="校准偏差"},0);cloud.SetAccountUrl("https://deskbox-sync.arthur98.workers.dev");
            Func<string,UserControl> legacy=id=>{switch(id){case "ai":var view=new ChatView(store);view.SetServices(chat,providers);return view;case "automation":return new AutomationView(store,workflows);case "search":return new SearchView(store,index,library,new FileHistory(store));case "history":return new RecordsView(store);case "desktop":var desktop=new UserControl();var board=new IconBoard(store){Dock=DockStyle.Fill};board.SetData(Directory.GetFiles(projects.Current.Root).Select(path=>new DesktopItem{Id=path,Path=path,Name=Path.GetFileName(path),Ext=Path.GetExtension(path)}).ToList());desktop.Controls.Add(board);return desktop;case "tasks":return new TasksView(store);case "incidents":return new IncidentsView(store,sync,projects);case "weather":return new WeatherView(store);case "engineering":return new EngineeringToolkitView(store);case "mobile":return new MobileSyncView(store,sync,cloud);case "projects":return new WorkspaceView(store,projects);default:return new UserControl();}};
            using(var host=new Form{ClientSize=new Size(1920,1080),StartPosition=FormStartPosition.Manual,Location=new Point(0,0)})using(var workspace=new ReferenceWorkspace(store,projects,sync,cloud,chat,providers,workflows,library,legacy))
            {
                host.Controls.Add(workspace);host.Show();Pump(200);var folder=Path.Combine(Environment.CurrentDirectory,"shots","ui-062-review");Directory.CreateDirectory(folder);
                int ordinal=0;using(var contact=new Bitmap(1800,1440))using(var graphics=Graphics.FromImage(contact))
                {
                    graphics.Clear(Color.FromArgb(3,16,26));
                    foreach(var page in ReferenceWorkspace.ReferencePages)
                    {
                        workspace.Navigate(page);Pump(page=="mobile"?2500:300);Check("页面可达 "+page,workspace.CurrentPage==page);if(page=="mobile"){var qr=Find(workspace,"CloudLoginQr") as PictureBox;Check("真实云地址二维码已生成",qr!=null&&qr.Image!=null&&qr.Image.Width>100);}using(var image=new Bitmap(host.ClientSize.Width,host.ClientSize.Height)){workspace.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(folder,(ordinal+1).ToString("00")+"-"+page+".png"));int x=(ordinal%3)*600,y=(ordinal/3)*360;graphics.DrawImage(image,new Rectangle(x,y+24,600,336));using(var font=new Font("Microsoft YaHei UI",10,FontStyle.Bold))graphics.DrawString((ordinal+1)+". "+page,font,Brushes.White,x+8,y+2);}ordinal++;
                    }
                    contact.Save(Path.Combine(folder,"all-12-pages.png"));
                }
                host.ClientSize=new Size(1280,800);workspace.Navigate("projects");Pump(200);Check("1280宽度保持内容区域",workspace.Width==1280&&workspace.Height==800);
                var projectQuery=Inputs(workspace).First(i=>i.Visible&&i.Placeholder.StartsWith("搜索项目"));projectQuery.Text="SH-2026-02";Pump(80);Check("项目卡片按真实编号筛选",Buttons(workspace).Count(b=>b.Visible&&b.Text=="查看项目")==1);projectQuery.Text="";
                var global=(DarkInput)typeof(ReferenceWorkspace).GetField("_globalSearch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);global.Text="IO";typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(global.Inner,new object[]{new KeyEventArgs(Keys.Enter)});Pump(80);
                var services=(Dictionary<string,ViewBase>)typeof(ReferenceWorkspace).GetField("_serviceViews",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);var searchList=(DarkList)typeof(SearchView).GetField("_list",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(services["search"]);Check("顶栏搜索进入真实文件检索",workspace.CurrentPage=="search"&&searchList.RowCount==1);
                workspace.Navigate("tasks");var taskView=FindType<TasksView>(workspace);var taskList=(DarkList)typeof(TasksView).GetField("_list",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(taskView);taskList.SelectIndex(0);var selectedTask=store.Tasks.First(t=>t.Id==(string)taskList.SelectedRow.Tag);var notes=(DarkInput)typeof(TasksView).GetField("_detailNotes",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(taskView);notes.Text="详情栏保存验收";typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(taskView).First(b=>b.Text=="保存详情"),new object[]{EventArgs.Empty});Check("任务详情栏保存真实备注",selectedTask.Notes=="详情栏保存验收");taskList.SelectIndex(0);selectedTask.Notes="另一端的新内容";notes.Text="旧编辑覆盖";typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(taskView).First(b=>b.Text=="保存详情"),new object[]{EventArgs.Empty});Check("任务详情拒绝覆盖同步的新内容",selectedTask.Notes=="另一端的新内容");
                Check("任务页面提供真实月历",FindType<TaskMonthCalendar>(taskView)!=null);var month=FindType<TaskMonthCalendar>(taskView);var oldMonth=month.Month;typeof(Control).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(month,new object[]{new MouseEventArgs(MouseButtons.Left,1,month.Width-10,10,0)});Check("月历翻月响应点击",month.Month.Month==oldMonth.AddMonths(1).Month);month.Month=oldMonth;typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(taskView).First(b=>b.Text=="月历 / 列表"),new object[]{EventArgs.Empty});Pump(80);using(var calendarImage=new Bitmap(host.ClientSize.Width,host.ClientSize.Height)){workspace.DrawToBitmap(calendarImage,new Rectangle(Point.Empty,calendarImage.Size));calendarImage.Save(Path.Combine(folder,"14-task-calendar.png"));}typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(taskView).First(b=>b.Text=="月历 / 列表"),new object[]{EventArgs.Empty});
                workspace.Navigate("automation");Pump(100);var pages=(Dictionary<string,Control>)typeof(ReferenceWorkspace).GetField("_pages",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace);var workflowPage=pages["automation"];var parameters=FindType<DataGridView>(workflowPage);Check("工作流默认显示真实节点参数",parameters.Rows.Count==2&&Convert.ToString(parameters.Rows[0].Cells[0].Value)=="message");parameters.Rows[0].Cells[1].Value="参数编辑验收";typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(workflowPage).First(b=>b.Text=="保存节点参数"),new object[]{EventArgs.Empty});Check("工作流参数真实保存",workflows.Workflows[0].Actions[0].Params["message"]=="参数编辑验收");typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(workflowPage).First(b=>b.Text=="预演流程"),new object[]{EventArgs.Empty});for(int tries=0;tries<50&&workflows.Runs.Count==0;tries++)Pump(100);Check("三栏工作区调用真实预演",workflows.Runs.Count>0&&workflows.Runs[0].DryRun&&workflows.Runs[0].Ok);
                workspace.Navigate("engineering");Pump(100);var toolView=FindType<EngineeringToolkitView>(pages["engineering"]);typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(toolView).First(b=>b.Text=="计算工程量"),new object[]{EventArgs.Empty});Check("工具工作区计算真实量程",FindText(toolView,"50    /    50%"));var signal=Inputs(toolView).First(input=>input.Placeholder=="输入电流 / mA");signal.Text="30";typeof(Control).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Buttons(toolView).First(b=>b.Text=="计算工程量"),new object[]{EventArgs.Empty});Check("工具工作区显示越界状态",FindText(toolView,"超出量程"));signal.Text="13";host.ClientSize=new Size(1500,900);Pump(100);Check("工具窗口缩放保留已输入数值",Inputs(toolView).First(input=>input.Placeholder=="输入电流 / mA").Text=="13");host.ClientSize=new Size(1280,800);signal.Text="12";
                workspace.Navigate("project-detail");Pump(100);Check("项目详情内嵌真实验证流程",FindType<ValidationFlowControl>(pages["project-detail"])!=null);Check("窄窗口项目备注有可读高度",FindType<TextBox>(pages["project-detail"]).Height>=60);using(var detailImage=new Bitmap(host.ClientSize.Width,host.ClientSize.Height)){workspace.DrawToBitmap(detailImage,new Rectangle(Point.Empty,detailImage.Size));detailImage.Save(Path.Combine(folder,"13-project-detail.png"));}
                workspace.Navigate("calculator");Pump(100);Check("计算器真实初始结果",FindText(workspace,"50"));
                workspace.ApplyWorkspacePalette(Palette.For("aurora","#34D8B7"));Check("工作台支持模式切换",workspace.BackColor==Palette.For("aurora","#34D8B7").Bg);workspace.ApplyWorkspacePalette(Palette.For("cyber","#25D5F5"));
                host.Close();
            }
            store.Settings.ReferenceUi=true;
            using(var main=new MainForm(store))
            {
                main.Show();Pump(150);var reference=(ReferenceWorkspace)typeof(MainForm).GetField("_reference",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);var widgets=(Control)typeof(MainForm).GetField("_widgets",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);
                Check("真实主窗启动参考模式无旧侧栏泄漏",reference!=null&&reference.Visible&&!widgets.Visible&&reference.Width==main.ClientSize.Width);
                main.SwitchView("desktop");Pump(80);var desktopHost=(Control)typeof(MainForm).GetField("_desktopHost",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);Check("真实桌面画板接入参考工作区",desktopHost.Visible&&desktopHost.Parent!=null&&desktopHost.Parent.Visible);main.SwitchView("tasks");Check("应用导航进入参考页面",reference.CurrentPage=="tasks");
                main.SwitchView("search");var mainSearch=(SearchView)typeof(MainForm).GetField("_searchView",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);var mainSearchList=(DarkList)typeof(SearchView).GetField("_list",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mainSearch);mainSearchList.SelectIndex(0);var hiddenPreview=(PreviewPanel)typeof(MainForm).GetField("_preview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(main);Check("参考搜索不重复加载隐藏旧预览",!hiddenPreview.IsOpen);
                main.SetReferenceWorkspace(false);Check("经典模式仍可切换",!store.Settings.ReferenceUi&&!reference.Visible);
                using(var settings=(UserControl)typeof(MainForm).GetMethod("BuildSettingsCenter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(main,null)){var checkbox=Checks(settings).First(c=>c.Text.StartsWith("使用12页"));checkbox.Checked=true;}
                Check("设置页可切回参考模式且保留业务数据",reference.Visible&&store.Tasks.Count==8&&projects.Items.Count==3);main.Close();
            }
        }
        Console.WriteLine("Reference UI: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
    static T FindType<T>(Control root) where T:Control {if(root is T)return (T)root;foreach(Control child in root.Controls){var value=FindType<T>(child);if(value!=null)return value;}return null;}
    static bool FindText(Control root,string needle){return root.Text.Contains(needle)||root.Controls.Cast<Control>().Any(c=>FindText(c,needle));}
    static Control Find(Control root,string name){if(root.Name==name)return root;foreach(Control child in root.Controls){var found=Find(child,name);if(found!=null)return found;}return null;}
    static IEnumerable<DarkInput> Inputs(Control root){foreach(Control child in root.Controls){var input=child as DarkInput;if(input!=null)yield return input;foreach(var item in Inputs(child))yield return item;}}
    static IEnumerable<DarkButton> Buttons(Control root){foreach(Control child in root.Controls){var button=child as DarkButton;if(button!=null)yield return button;foreach(var item in Buttons(child))yield return item;}}
    static IEnumerable<CheckBox> Checks(Control root){foreach(Control child in root.Controls){var checkbox=child as CheckBox;if(checkbox!=null)yield return checkbox;foreach(var item in Checks(child))yield return item;}}
}
