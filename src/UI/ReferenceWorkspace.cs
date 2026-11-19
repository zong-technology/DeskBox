using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Fs;
using DeskBox.Native;
using DeskBox.Sync;

namespace DeskBox.UI
{
    /// <summary>The selected twelve-page native workspace. Services remain shared with the classic workspace.</summary>
    public partial class ReferenceWorkspace : UserControl
    {
        private readonly Store _store;
        private readonly WorkspaceStore _projects;
        private readonly LocalSyncCoordinator _sync;
        private readonly CloudSyncAgent _cloud;
        private readonly ChatService _chat;
        private readonly ProviderStore _providers;
        private readonly Automation.WorkflowEngine _workflows;
        private readonly FileLibrary _library;
        private readonly Func<string,UserControl> _legacyFactory;
        private readonly Dictionary<string,Control> _pages=new Dictionary<string,Control>();
        private readonly Dictionary<string,Action> _reload=new Dictionary<string,Action>();
        private readonly Dictionary<string,Views.ViewBase> _serviceViews=new Dictionary<string,Views.ViewBase>();
        private readonly Dictionary<string,DarkButton> _nav=new Dictionary<string,DarkButton>();
        private readonly List<Font> _fonts=new List<Font>();
        private Palette _p;
        private Panel _content,_rail;
        private Label _heading,_cloudLabel,_status;
        private DarkInput _globalSearch;
        private Image _factory;
        private Image _brand;
        private string _current="home",_selectedProject="";
        public event Action ClassicRequested;
        public event Action<string> FileAiRequested;
        public event Action<string> TiaFileRequested;
        public event Action<string> PageChanged;
        public string CurrentPage{get{return _current;}}
        public static readonly string[] ReferencePages={"home","desktop","projects","engineering","ai","search","tasks","history","automation","incidents","weather","mobile"};

        public ReferenceWorkspace(Store store,WorkspaceStore projects,LocalSyncCoordinator sync,CloudSyncAgent cloud,ChatService chat,ProviderStore providers,Automation.WorkflowEngine workflows,FileLibrary library,Func<string,UserControl> legacyFactory)
        {
            _store=store;_projects=projects;_sync=sync;_cloud=cloud;_chat=chat;_providers=providers;_workflows=workflows;_library=library;_legacyFactory=legacyFactory;
            _p=Palette.For(store.Settings.Theme,store.Settings.Accent);
            Dock=DockStyle.Fill;BackColor=_p.Bg;Font=FontOf(9);SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);
            using(var stream=typeof(ReferenceWorkspace).Assembly.GetManifestResourceStream("DeskBox.EngineeringFactory")){if(stream!=null)using(var image=Image.FromStream(stream))_factory=new Bitmap(image);}
            BuildShell();_projects.Changed+=DataChanged;_sync.Changed+=DataChanged;_cloud.Changed+=CloudChanged;Navigate("home");
        }
        private Font FontOf(float size,bool bold=false){var found=_fonts.FirstOrDefault(f=>f.Size==size&&f.Bold==bold);if(found!=null)return found;var font=new Font("Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular);_fonts.Add(font);return font;}
        private Label TextLabel(string text,float size=9.5f,bool bold=false,Color? color=null){return new Label{Text=text,UseMnemonic=false,AutoEllipsis=true,ForeColor=color??_p.Text,BackColor=Color.Transparent,Font=FontOf(size,bold)};}
        private DarkButton Button(string text,Action action,bool primary=false,int width=108){var b=new DarkButton(text){Palette=_p,BackColor=_p.Bg,Primary=primary,Width=width,Height=34,Margin=new Padding(0,0,10,0)};b.Click+=delegate{try{action();}catch(Exception ex){_status.Text=ex.Message;}};return b;}
        private DarkInput Input(string hint,int width=250){return new DarkInput{Placeholder=hint,Palette=_p,Width=width,Height=34,Margin=new Padding(0,0,10,0)};}
        private Panel Page(string title,string subtitle)
        {
            var page=new Panel{Dock=DockStyle.Fill,BackColor=_p.Bg,Padding=new Padding(24,18,24,18)};
            var heading=new Panel{Dock=DockStyle.Top,Height=64};var label=TextLabel(title,18,true);label.SetBounds(0,0,900,34);label.Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right;
            var hint=TextLabel(subtitle,9,false,_p.TextDim);hint.SetBounds(0,39,1000,22);hint.Anchor=label.Anchor;heading.Controls.AddRange(new Control[]{label,hint});page.Controls.Add(heading);return page;
        }
        private FlowLayoutPanel Toolbar(){return new FlowLayoutPanel{Dock=DockStyle.Top,Height=48,WrapContents=false,AutoScroll=true,BackColor=_p.Bg,Padding=new Padding(0,4,0,4)};}
        private Panel Card(string title="")
        {
            var card=new ReferenceCard(_p){Margin=new Padding(0,0,14,14),Padding=new Padding(18)};
            if(title.Length>0){int separator=title.IndexOf('/');string primary=separator>=0?title.Substring(separator+1).Trim():title;string secondary=separator>=0?title.Substring(0,separator).Trim():"";var heading=new Panel{Dock=DockStyle.Top,Height=36,BackColor=Color.Transparent};var label=TextLabel(primary,11,true);label.TextAlign=ContentAlignment.MiddleLeft;label.Dock=DockStyle.Fill;heading.Controls.Add(label);if(secondary.Length>0){var code=TextLabel(secondary,8,false,_p.TextFaint);code.Dock=DockStyle.Right;code.Width=Math.Min(180,secondary.Length*10+20);code.TextAlign=ContentAlignment.MiddleRight;heading.Controls.Add(code);label.BringToFront();}card.Controls.Add(heading);}return card;
        }
        private DarkList List(string[] columns,int[] widths){var list=new DarkList{Dock=DockStyle.Fill,Palette=_p,CardSurface=true,AutoFitColumns=true,RowHeight=43,Font=FontOf(9.5f)};list.SetColumns(columns,widths);return list;}
        private void AddMain(Panel page,Control body,Control toolbar=null){var heading=page.Controls[0];page.Controls.Add(body);body.Dock=DockStyle.Fill;if(toolbar!=null)page.Controls.Add(toolbar);page.Controls.SetChildIndex(heading,page.Controls.Count-1);if(toolbar!=null)page.Controls.SetChildIndex(toolbar,1);page.Controls.SetChildIndex(body,0);}
        private void BuildShell()
        {
            var header=new Panel{Dock=DockStyle.Top,Height=64,BackColor=_p.HeaderBg};header.Paint+=delegate(object s,PaintEventArgs e){using(var pen=new Pen(_p.Line))e.Graphics.DrawLine(pen,0,63,header.Width,63);};
            using(var icon=Icon.ExtractAssociatedIcon(typeof(ReferenceWorkspace).Assembly.Location))if(icon!=null)_brand=icon.ToBitmap();
            var mark=new PictureBox{Image=_brand,SizeMode=PictureBoxSizeMode.Zoom,Bounds=new Rectangle(15,20,26,26),BackColor=Color.Transparent};header.Controls.Add(mark);
            var brand=TextLabel("DeskBox",15,true,_p.Accent);brand.SetBounds(48,17,125,32);header.Controls.Add(brand);
            _heading=TextLabel("工程智能工作台",9,false,_p.TextDim);_heading.SetBounds(184,23,220,24);header.Controls.Add(_heading);
            _globalSearch=Input("搜索工程文件与资料…  Ctrl+K",450);_globalSearch.SetBounds(430,15,450,34);
            _globalSearch.KeyPressed+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Navigate("search");var search=_serviceViews["search"] as Views.SearchView;if(search!=null)search.SearchText(_globalSearch.Text);}};
            header.Controls.Add(_globalSearch);
            _cloudLabel=TextLabel("",9,false,_p.Accent);_cloudLabel.SetBounds(930,22,180,25);_cloudLabel.Cursor=Cursors.Hand;_cloudLabel.Click+=delegate{Navigate("mobile");};header.Controls.Add(_cloudLabel);
            var user=Button(Environment.UserName,()=>ShowLegacy("settings"),false,110);user.SetBounds(1140,15,110,34);header.Controls.Add(user);
            var ai=Button("AI&SI",()=>Navigate("ai"),false,76);ai.SetBounds(1260,15,76,34);header.Controls.Add(ai);
            header.Resize+=delegate{int right=header.ClientSize.Width-16;ai.Left=right-ai.Width;user.Left=ai.Left-10-user.Width;_cloudLabel.Left=user.Left-190;_globalSearch.Width=Math.Max(180,Math.Min(490,_cloudLabel.Left-440));_heading.Visible=header.Width>1200;_globalSearch.Left=header.Width>1200?430:190;_globalSearch.Width=Math.Max(160,_cloudLabel.Left-_globalSearch.Left-24);};
            _rail=new Panel{Dock=DockStyle.Left,Width=200,BackColor=_p.HeaderBg,AutoScroll=true,Padding=new Padding(8,10,8,10)};
            string[,] items={{"home","\uE80F","工程工作区"},{"desktop","\uE8A5","智能桌面"},{"projects","\uE8B7","项目管理"},{"engineering","\uE90F","工程工具"},{"tia","\uE950","博图 AI 工程师"},{"ai","\uE99A","AI&SI 助手"},{"search","\uE721","文件中心"},{"tasks","\uE73A","任务与提醒"},{"history","\uE81C","工作记录"},{"automation","\uE945","自动化"},{"incidents","\uE7BA","现场事件"},{"weather","\uE753","天气"},{"mobile","\uE8EA","移动同步"}};
            for(int i=0;i<items.GetLength(0);i++)
            {
                string id=items[i,0];var button=Button(items[i,2],()=>Navigate(id),false,176);button.SetBounds(4,35+i*44,176,40);button.Font=FontOf(10.5f);button.Name="nav-"+id;button.Navigation=true;button.Symbol=items[i,1];_rail.Controls.Add(button);_nav[id]=button;
            }
            var section=TextLabel("WORKSPACE  /  工作空间",8,false,_p.TextFaint);section.SetBounds(18,10,170,20);_rail.Controls.Add(section);
            var more=Button("功能与设置",()=>ShowMore(),false,176);more.SetBounds(4,635,176,38);more.Navigation=true;more.Symbol="\uE713";_rail.Controls.Add(more);
            var classic=Button("切换经典模式",()=>{if(ClassicRequested!=null)ClassicRequested();},false,176);classic.SetBounds(4,679,176,34);classic.Navigation=true;classic.Symbol="\uE8A9";_rail.Controls.Add(classic);
            _content=new Panel{Dock=DockStyle.Fill,BackColor=_p.Bg};_status=TextLabel("",8,false,_p.TextDim);_status.Dock=DockStyle.Bottom;_status.Height=25;_status.Padding=new Padding(18,3,0,0);
            Controls.Add(_content);Controls.Add(_rail);Controls.Add(_status);Controls.Add(header);RefreshStatus();
        }
        public void Navigate(string page)
        {
            if(!ReferencePages.Contains(page)&&page!="project-detail"&&page!="calculator"&&page!="tool-workspace"&&page!="tia"){ShowLegacy(page);return;}
            _current=page;
            _globalSearch.UseDrawnPlaceholder = page == "desktop" || page == "home" || page == "engineering" || page == "ai";
            if(PageChanged!=null)PageChanged(page);
            if(!_pages.ContainsKey(page))
            {
                Control control;
                switch(page){case "home":control=BuildOperationsDesk();break;case "desktop":control=BuildDesktopPage();break;case "history":control=BuildHistoryPage();break;case "projects":control=BuildPortfolio();break;case "project-detail":control=BuildProjectWorkspace();break;case "engineering":control=BuildToolStudio();break;case "tia":control=_legacyFactory(page);break;case "tool-workspace":control=BuildToolWorkspace();break;case "calculator":control=BuildCalculator();break;case "tasks":control=BuildTasks();break;case "incidents":control=BuildIncidents();break;case "weather":control=BuildWeather();break;case "mobile":control=BuildMobile();break;default:control=BuildServicePage(page);break;}
                control.Name="page-"+page;control.Dock=DockStyle.Fill;_pages[page]=control;_content.Controls.Add(control);
            }
            foreach(var item in _pages)item.Value.Visible=item.Key==page;_pages[page].BringToFront();
            foreach(var item in _nav){item.Value.Primary=item.Key==page||(page=="project-detail"&&item.Key=="projects")||((page=="calculator"||page=="tool-workspace")&&item.Key=="engineering");item.Value.Invalidate();}
            var currentProject=CurrentProject();_heading.Text=currentProject==null?"工程智能工作台":currentProject.Name;
            Action reload;if(_reload.TryGetValue(page,out reload))reload();var view=_pages[page] as Views.ViewBase;if(view!=null)view.Activate();RefreshStatus();
        }
        public void RefreshStatus(){if(_status==null)return;RefreshDashboardCounts();_cloudLabel.Text=_cloud.Connected?"● 云同步在线":_cloud.LoggedIn?"● 云同步连接中":"○ 未登录云账号";_status.Text="DeskBox  /  Win+F 呼出    ·    "+_store.Tasks.Count(x=>!x.Done)+" 项待办    ·    "+_sync.Incidents.Count+" 项现场事件    ·    "+_cloud.Status;}
        public void RefreshData(){DataChanged();}
        public void RefreshCurrentPage(){Action reload;if(_reload.TryGetValue(_current,out reload))reload();RefreshStatus();}
        public void ApplyWorkspacePalette(Palette palette)
        {
            var previous=_p;_p=palette;
            Action<Control> apply=null;apply=control=>{var calendar=control as Views.TaskMonthCalendar;if(calendar!=null)calendar.Palette=palette;var workflowGraph=control as Views.WorkflowGraphControl;if(workflowGraph!=null)workflowGraph.ApplyPalette(palette);var validationFlow=control as Views.ValidationFlowControl;if(validationFlow!=null)validationFlow.ApplyPalette(palette);var dataGrid=control as DataGridView;if(dataGrid!=null){dataGrid.BackgroundColor=palette.CardBg;dataGrid.GridColor=palette.Line;dataGrid.DefaultCellStyle.BackColor=palette.CardBg;dataGrid.DefaultCellStyle.ForeColor=palette.Text;dataGrid.DefaultCellStyle.SelectionBackColor=palette.Hover;dataGrid.DefaultCellStyle.SelectionForeColor=palette.Text;dataGrid.ColumnHeadersDefaultCellStyle.BackColor=palette.HeaderBg;dataGrid.ColumnHeadersDefaultCellStyle.ForeColor=palette.TextDim;}var health=control as ReferenceProjectHealth;if(health!=null)health.ApplyPalette(palette);var preview=control as PreviewPanel;if(preview!=null)preview.ApplyPalette(palette);var card=control as ReferenceCard;if(card!=null)card.ApplyPalette(palette);var view=control as Views.ViewBase;if(view!=null)view.ApplyPalette(palette);var button=control as DarkButton;if(button!=null)button.ApplyPalette(palette);var input=control as DarkInput;if(input!=null)input.ApplyPalette(palette);var list=control as DarkList;if(list!=null)list.ApplyPalette(palette);var select=control as DarkSelect;if(select!=null)select.Palette=palette;
                if(control.BackColor==previous.Bg)control.BackColor=palette.Bg;else if(control.BackColor==previous.HeaderBg)control.BackColor=palette.HeaderBg;else if(control.BackColor==previous.CardBg)control.BackColor=palette.CardBg;else if(control.BackColor==previous.PanelAlt)control.BackColor=palette.PanelAlt;
                if(control.ForeColor==previous.Text)control.ForeColor=palette.Text;else if(control.ForeColor==previous.TextDim)control.ForeColor=palette.TextDim;else if(control.ForeColor==previous.Accent)control.ForeColor=palette.Accent;
                foreach(Control child in control.Controls)apply(child);control.Invalidate();};apply(this);
        }
        private void DataChanged(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action(DataChanged));}catch{}return;}RefreshStatus();Action reload;if(new[]{"home","projects","project-detail","tasks","incidents"}.Contains(_current)&&_reload.TryGetValue(_current,out reload))reload();}
        private void CloudChanged(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action(CloudChanged));}catch{}return;}RefreshStatus();Action reload;if(_current=="mobile"&&_reload.TryGetValue("mobile",out reload))reload();}
        private void ShowMore(){var menu=MenuBuilder.Create(_p);foreach(var item in new[]{new[]{"收藏与标签","library"},new[]{"磁盘分析","disk"},new[]{"系统监控","monitor"},new[]{"剪贴板","clipboard"},new[]{"工作记录","history"},new[]{"通知中心","notifications"},new[]{"文件操作历史","filehistory"},new[]{"设置中心","settings"}}){string id=item[1];menu.Items.Add(MenuBuilder.Item(item[0],()=>ShowLegacy(id)));}menu.Show(_rail,new Point(12,550));}
        internal void ShowLegacy(string name, Action<Control> configure = null)
        {
            using(var dialog=new Form{Text="DeskBox · "+name,StartPosition=FormStartPosition.CenterParent,Size=new Size(1120,760),BackColor=_p.Bg})
            {var view=_legacyFactory(name);if(view==null)return;view.Dock=DockStyle.Fill;dialog.Controls.Add(view);var baseView=view as Views.ViewBase;if(baseView!=null){baseView.ApplyPalette(_p);baseView.Activate();}if(configure!=null)configure(view);dialog.ShowDialog(this);}DataChanged();
        }
        private void OpenPath(string path){if(!File.Exists(path)&&!Directory.Exists(path)){_status.Text="路径已不存在："+path;return;}Win32.OpenPath(path);if(File.Exists(path))_library.Touch(path);}
        private EngineerWorkspace CurrentProject(){return _projects.Current??_projects.Items.FirstOrDefault(p=>p.Id==_selectedProject)??_projects.Items.FirstOrDefault();}
        private void ShowProject(EngineerWorkspace project){_selectedProject=project.Id;_projects.SetCurrent(project.Id);Navigate("project-detail");}
        protected override void Dispose(bool disposing){if(disposing){_projects.Changed-=DataChanged;_sync.Changed-=DataChanged;_cloud.Changed-=CloudChanged;}base.Dispose(disposing);if(disposing){if(_factory!=null)_factory.Dispose();if(_brand!=null)_brand.Dispose();foreach(var font in _fonts)font.Dispose();}}
    }

    internal class ReferenceCard : Panel
    {
        private Palette _palette;
        public void ApplyPalette(Palette palette){_palette=palette;BackColor=palette.CardBg;Invalidate();}
        public ReferenceCard(Palette palette){_palette=palette;BackColor=palette.CardBg;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnPaint(PaintEventArgs e){if(Width<2||Height<2)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(0,0,Width-1,Height-1);using(var path=Palette.RoundedRect(r,12))using(var brush=new SolidBrush(_palette.CardBg))e.Graphics.FillPath(brush,path);Palette.DrawRounded(e.Graphics,_palette.Line,r,12,1);base.OnPaint(e);}
    }
}
