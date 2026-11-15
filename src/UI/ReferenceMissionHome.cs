using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Label[] _homeMetrics;
        private void RefreshDashboardCounts()
        {
            if(_homeMetrics==null)return;
            _homeMetrics[0].Text=_store.Tasks.Count(t=>!t.Done).ToString();
            _homeMetrics[1].Text=_store.Tasks.Count(t=>!t.Done&&t.Priority=="high").ToString();
            _homeMetrics[2].Text=_sync.Incidents.Count.ToString();
            _homeMetrics[3].Text=_cloud.Connected?"在线":_cloud.LoggedIn?"连接中":"未连接";
        }
        private Control BuildMissionHome()
        {
            var page=new Panel{Dock=DockStyle.Fill,Padding=new Padding(24,16,10,4),BackColor=_p.Bg};
            var toolbar=Toolbar();var project=new DarkSelect(new string[0]){Palette=_p,Width=330,Height=34};toolbar.Controls.Add(project);
            toolbar.Controls.Add(Button("打开项目目录",()=>{var p=CurrentProject();if(p!=null)OpenPath(p.Root);},true,145));
            toolbar.Controls.Add(Button("编辑验证进度",()=>{var p=CurrentProject();if(p!=null)using(var form=new Views.ProjectProgressForm(_projects,p,_p))form.ShowDialog(this);},false,145));
            toolbar.Controls.Add(Button("进入智能桌面",()=>Navigate("desktop"),false,145));
            var hero=Card();hero.Dock=DockStyle.Top;hero.Height=146;hero.Padding=Padding.Empty;
            hero.Paint+=delegate(object sender,PaintEventArgs e){if(hero.Width<2)return;var saved=e.Graphics.Save();using(var path=Palette.RoundedRect(new Rectangle(0,0,hero.Width-1,hero.Height-1),12))e.Graphics.SetClip(path);Photo(e.Graphics,hero.ClientRectangle,70);using(var tint=new LinearGradientBrush(hero.ClientRectangle,_p.Bg2,Color.FromArgb(15,_p.Bg2),0f))e.Graphics.FillRectangle(tint,hero.ClientRectangle);e.Graphics.Restore(saved);Palette.DrawRounded(e.Graphics,_p.Line,new Rectangle(0,0,hero.Width-1,hero.Height-1),12,1);};
            var eyebrow=TextLabel("ENGINEERING COMMAND CENTER  /  工程指挥台",9,true,_p.Accent);eyebrow.SetBounds(24,18,690,22);hero.Controls.Add(eyebrow);
            var title=TextLabel("",23,true);title.SetBounds(24,50,800,44);hero.Controls.Add(title);
            var meta=TextLabel("",10,false,_p.TextDim);meta.SetBounds(26,105,900,24);hero.Controls.Add(meta);
            var clock=TextLabel("",27,true,_p.Accent);clock.TextAlign=ContentAlignment.MiddleRight;hero.Controls.Add(clock);var date=TextLabel("",9,false,_p.TextDim);date.TextAlign=ContentAlignment.MiddleRight;hero.Controls.Add(date);
            hero.Resize+=delegate{clock.SetBounds(Math.Max(0,hero.Width-310),32,280,50);date.SetBounds(Math.Max(0,hero.Width-310),86,280,30);title.Width=Math.Max(200,hero.Width-360);meta.Width=Math.Max(200,hero.Width-360);};
            Action tick=delegate{clock.Text=DateTime.Now.ToString("HH:mm:ss");date.Text=DateTime.Now.ToString("yyyy-MM-dd  dddd");};tick();var timer=new Timer{Interval=1000};timer.Tick+=delegate{if(hero.Visible)tick();};timer.Start();hero.Disposed+=delegate{timer.Stop();timer.Dispose();};
            var metrics=new TableLayoutPanel{Dock=DockStyle.Top,Height=106,ColumnCount=4,RowCount=1,Padding=new Padding(0,14,0,0)};
            _homeMetrics=new Label[4];string[] captions={"待办任务","高优先级","现场事件","跨网同步"};string[] routes={"tasks","tasks","incidents","mobile"};string[] symbols={"\uE73A","\uE7BA","\uE814","\uE8EA"};
            for(int i=0;i<4;i++)
            {
                metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));var card=Card();card.Dock=DockStyle.Fill;card.Padding=Padding.Empty;var symbol=new Label{Text=symbols[i],Font=new Font("Segoe MDL2 Assets",22),ForeColor=i==1?Color.FromArgb(251,113,133):_p.Accent,BackColor=Color.Transparent,Bounds=new Rectangle(18,24,42,36)};card.Controls.Add(symbol);
                var value=TextLabel("—",23,true,i==1?(Color?)Color.FromArgb(251,113,133):null);value.SetBounds(76,10,180,40);card.Controls.Add(value);_homeMetrics[i]=value;var caption=TextLabel(captions[i],9,false,_p.TextDim);caption.SetBounds(78,53,180,24);card.Controls.Add(caption);string route=routes[i];card.Cursor=Cursors.Hand;card.Click+=delegate{Navigate(route);};foreach(Control child in card.Controls)child.Click+=delegate{Navigate(route);};metrics.Controls.Add(card,i,0);
            }
            var main=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(0,14,0,0)};main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));main.RowStyles.Add(new RowStyle(SizeType.Percent,48));main.RowStyles.Add(new RowStyle(SizeType.Percent,52));
            var healthCard=Card("PROJECT HEALTH / 项目进度与交付阶段");healthCard.Dock=DockStyle.Fill;var health=new ReferenceProjectHealth(_p,CurrentProject){Dock=DockStyle.Fill};healthCard.Controls.Add(health);health.BringToFront();health.Cursor=Cursors.Hand;health.Click+=delegate{var p=CurrentProject();if(p!=null)ShowProject(p);};
            var tasksCard=Card("NEXT ACTIONS / 优先处理");tasksCard.Dock=DockStyle.Fill;var tasks=List(new[]{"任务","截止","优先级"},new[]{300,100,70});tasksCard.Controls.Add(tasks);tasks.BringToFront();tasks.RowActivated+=delegate{Navigate("tasks");};
            var documentsCard=Card("RECENT FILES / 最近打开");documentsCard.Dock=DockStyle.Fill;var files=List(new[]{"文件名称","目录","打开时间"},new[]{330,320,150});documentsCard.Controls.Add(files);files.BringToFront();files.RowActivated+=delegate{if(files.SelectedRow!=null)OpenPath((string)files.SelectedRow.Tag);};
            var nextCard=Card("WORKSPACE / 项目下一步与常用入口");nextCard.Dock=DockStyle.Fill;var next=TextLabel("",10,false,_p.TextDim);next.Dock=DockStyle.Top;next.Height=62;nextCard.Controls.Add(next);var shortcuts=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true,Padding=new Padding(0,10,0,0)};nextCard.Controls.Add(shortcuts);shortcuts.BringToFront();nextCard.Controls.SetChildIndex(next,1);
            foreach(var entry in new[]{new[]{"AI 文档分析","ai"},new[]{"工程工具","engineering"},new[]{"项目内搜索","search"},new[]{"现场事件","incidents"}}){string id=entry[1];var button=Button(entry[0],()=>{Navigate(id);if(id=="search"){var p=CurrentProject();var search=_serviceViews["search"] as Views.SearchView;if(p!=null&&search!=null)search.SetProjectScope(p.Root,p.Name);}},false,145);button.Height=36;button.Margin=new Padding(0,0,10,10);shortcuts.Controls.Add(button);}
            main.Controls.Add(healthCard,0,0);main.Controls.Add(tasksCard,1,0);main.Controls.Add(documentsCard,0,1);main.Controls.Add(nextCard,1,1);page.Controls.Add(main);page.Controls.Add(metrics);page.Controls.Add(hero);page.Controls.Add(toolbar);
            bool loading=false;project.SelectedIndexChanged+=delegate{if(loading||project.SelectedIndex<0)return;var values=_projects.Items;if(project.SelectedIndex<values.Count){_selectedProject=values[project.SelectedIndex].Id;_projects.SetCurrent(_selectedProject);}};
            _reload["home"]=delegate{
                loading=true;var values=_projects.Items;project.Inner.Items.Clear();project.Inner.Items.AddRange(values.Select(p=>p.Name).ToArray());var current=CurrentProject();project.SelectedIndex=current==null?-1:values.FindIndex(p=>p.Id==current.Id);loading=false;
                title.Text=current==null?"连接项目，开始今天的工程工作":current.Name;meta.Text=current==null?"Automation · DCS · Siemens PLC · 制药设备验证":string.Join("   ·   ",new[]{current.Number,current.Kind,current.Owner.Length==0?"负责人未设置":current.Owner,current.Deadline.Length==0?"交付日期未设置":"交付 "+current.Deadline}.Where(part=>!string.IsNullOrWhiteSpace(part)));
                var step=current==null||current.ValidationSteps==null?null:current.ValidationSteps.FirstOrDefault(s=>s.Percent.HasValue&&s.Percent.Value<100);next.Text=step==null?"选择项目，查看节点与项目资料。":"待推进 · "+step.Name+"  "+step.Percent.Value.ToString("0.#")+"%\r\n"+(string.IsNullOrWhiteSpace(step.Notes)?"点击项目进度可查看完整验证流程。":step.Notes);
                tasks.SetRows(_store.Tasks.Where(t=>!t.Done).OrderByDescending(t=>t.Priority=="high").ThenBy(t=>t.Due.Length==0?"9999":t.Due).Take(8).Select(t=>new ListRow{Cells=new[]{t.Title,t.Due,t.Priority=="high"?"高":"普通"},Tag=t}).ToList());files.SetRows(_library.Recent.Take(10).Select(f=>new ListRow{Cells=new[]{Path.GetFileName(f.Path),Path.GetDirectoryName(f.Path),TimeText(f.LastOpenAt)},Tag=f.Path}).ToList());RefreshDashboardCounts();health.Invalidate();
            };return page;
        }
    }
    internal sealed class ReferenceProjectHealth:Control
    {
        private readonly Func<EngineerWorkspace> _project;
        private Palette _palette;
        public ReferenceProjectHealth(Palette palette,Func<EngineerWorkspace> project){_palette=palette;_project=project;BackColor=palette.CardBg;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        public void ApplyPalette(Palette palette){_palette=palette;BackColor=palette.CardBg;Invalidate();}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;var p=_palette;var project=_project();var value=ValidationProgress.Overall(project);int diameter=Math.Min(142,Math.Max(50,Height-18));var ring=new Rectangle(10,8,diameter,diameter);
            using(var pen=new Pen(p.Line,10))g.DrawArc(pen,ring,-90,360);if(value.HasValue)using(var pen=new Pen(p.Accent,10){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawArc(pen,ring,-90,(float)(value.Value*3.6));
            using(var font=new Font("Microsoft YaHei UI",22,FontStyle.Bold))TextRenderer.DrawText(g,value.HasValue?value.Value.ToString("0.#")+"%":"N/A",font,ring,p.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            int left=diameter+36,available=Math.Max(100,Width-left-6),cell=available/4;
            string[] names={"需求","设计","DQ","FAT","SAT","IQ","OQ","交付"};int[][] groups={new[]{1,2,3,4},new[]{5},new[]{6,7,8,9},new[]{10,11,12},new[]{13,14,15},new[]{16,17,18},new[]{19,20,21},new[]{22,23,24,25,26}};
            using(var font=new Font("Microsoft YaHei UI",9))using(var number=new Font("Consolas",13,FontStyle.Bold))for(int i=0;i<names.Length;i++)
            {
                int x=left+i%4*cell,y=4+i/4*Math.Max(1,(Height-8)/2);double? progress=null;
                if(project!=null&&project.ValidationSteps!=null){var steps=groups[i].Where(index=>index<project.ValidationSteps.Count).Select(index=>project.ValidationSteps[index]).Where(s=>s.Percent.HasValue&&s.Weight>0).ToArray();if(steps.Length>0)progress=steps.Sum(s=>s.Percent.Value*s.Weight)/steps.Sum(s=>s.Weight);}
                TextRenderer.DrawText(g,names[i],font,new Point(x,y),p.TextDim);TextRenderer.DrawText(g,progress.HasValue?progress.Value.ToString("0.#")+"%":"N/A",number,new Point(x,y+22),progress.HasValue?p.Accent:p.TextFaint);int bar=Math.Max(20,cell-20);using(var brush=new SolidBrush(p.Line))g.FillRectangle(brush,x,y+Math.Min(45,(Height-8)/2-5),bar,4);if(progress.HasValue)using(var brush=new SolidBrush(p.Accent))g.FillRectangle(brush,x,y+Math.Min(45,(Height-8)/2-5),(float)(bar*progress.Value/100),4);
            }
        }
    }
}
