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
        private FlowLayoutPanel _subnav;
        private Label _status;
        private DarkSelect _projectPicker, _mode;
        private string[] _projectIds = new string[0];
        private bool _updatingPicker;
        private bool _layingOutRail;
        private readonly ToolTip _railTips = new ToolTip { ShowAlways = true };
        private DarkInput _globalSearch;
        private Image _factory;
        private string _current="home";
        public event Action ClassicRequested;
        public event Action<string> FileAiRequested;
        public event Action<string> TiaFileRequested;
        public event Action<string> PageChanged;
        public string CurrentPage{get{return _current;}}
        public static readonly string[] ReferencePages = EngineeringNavigation.Routes;

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
        private int WorkbenchPixels(int logical) { return Views.TiaWorkbenchStyle.Pixels(this, logical); }

        private Panel Page(string title, string subtitle)
        {
            var page = new Panel { Dock = DockStyle.Fill, BackColor = _p.Bg, Padding = new Padding(24, 18, 24, 18) };
            var heading = new Panel { Dock = DockStyle.Top, Height = WorkbenchPixels(62) };
            var label = TextLabel(title, 18, true);
            label.Name = "engineering-page-title";
            label.SetBounds(0, 0, 0, Math.Max(WorkbenchPixels(32), label.Font.Height + 4));
            var hint = TextLabel(subtitle, 9.5f, false, _p.TextDim);
            hint.SetBounds(0, label.Bottom + 2, 0, Math.Max(WorkbenchPixels(21), hint.Font.Height + 3));
            heading.Controls.AddRange(new Control[] { label, hint });
            heading.SizeChanged += delegate { label.Width = hint.Width = heading.ClientSize.Width; };
            page.Controls.Add(heading);
            return page;
        }

        private FlowLayoutPanel Toolbar()
        {
            var host = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = WorkbenchPixels(44), WrapContents = false,
                AutoScroll = true, BackColor = _p.Bg, Padding = new Padding(0, 4, 0, 4)
            };
            host.ControlAdded += delegate(object sender, ControlEventArgs args)
            {
                var button = args.Control as DarkButton;
                if (button == null) return;
                button.Height = Math.Max(button.Height, WorkbenchPixels(34));
                int measured = TextRenderer.MeasureText(button.Text, button.Font).Width;
                button.Width = Math.Max(button.Width, measured + WorkbenchPixels(14));
            };
            return host;
        }
        private Panel Card(string title="")
        {
            var card=new ReferenceCard(_p){Margin=new Padding(0,0,14,14),Padding=new Padding(18)};
            if(title.Length>0){int separator=title.IndexOf('/');string primary=separator>=0?title.Substring(separator+1).Trim():title;string secondary=separator>=0?title.Substring(0,separator).Trim():"";var heading=new Panel{Dock=DockStyle.Top,Height=36,BackColor=Color.Transparent};var label=TextLabel(primary,11,true);label.TextAlign=ContentAlignment.MiddleLeft;label.Dock=DockStyle.Fill;heading.Controls.Add(label);if(secondary.Length>0){var code=TextLabel(secondary,8,false,_p.TextFaint);code.Dock=DockStyle.Right;code.Width=Math.Min(180,secondary.Length*10+20);code.TextAlign=ContentAlignment.MiddleRight;heading.Controls.Add(code);label.BringToFront();}card.Controls.Add(heading);}return card;
        }
        private DarkList List(string[] columns,int[] widths){var list=new DarkList{Dock=DockStyle.Fill,Palette=_p,CardSurface=true,AutoFitColumns=true,RowHeight=43,Font=FontOf(9.5f)};list.SetColumns(columns,widths);return list;}
        private void AddMain(Panel page,Control body,Control toolbar=null){var heading=page.Controls[0];page.Controls.Add(body);body.Dock=DockStyle.Fill;if(toolbar!=null)page.Controls.Add(toolbar);page.Controls.SetChildIndex(heading,page.Controls.Count-1);if(toolbar!=null)page.Controls.SetChildIndex(toolbar,1);page.Controls.SetChildIndex(body,0);}
        private void BuildShell()
        {
            var header = new Panel { Name = "engineering-header", Dock = DockStyle.Top, BackColor = _p.HeaderBg };
            var brand = TextLabel("DeskBox", 13, true, _p.Accent);
            brand.TextAlign = ContentAlignment.MiddleLeft;
            var expand = Button("☰", delegate
            {
                _store.Settings.EngineeringNavExpanded = !_store.Settings.EngineeringNavExpanded;
                _store.SaveSettingsOnly();
                UpdateNavigationRail();
            }, false, 38);
            expand.AccessibleName = "展开或收起主导航";
            _railTips.SetToolTip(expand, expand.AccessibleName);

            _projectPicker = new DarkSelect(null) { Name = "active-project", Palette = _p, AccessibleName = "当前工程项目" };
            _projectPicker.SelectedIndexChanged += delegate
            {
                if (_updatingPicker || _projectPicker.SelectedIndex < 0 || _projectPicker.SelectedIndex >= _projectIds.Length) return;
                try { _projects.SetCurrent(_projectIds[_projectPicker.SelectedIndex]); }
                catch (Exception error) { RefreshProjectPicker(); _status.Text = "无法切换项目：" + error.Message; }
            };

            _mode = new DarkSelect(new[] { "工程设计模式", "现场调试模式", "验证测试模式", "项目管理模式" })
                { Name = "engineering-mode", Palette = _p, AccessibleName = "工程工作模式" };
            _mode.SelectedIndex = Math.Max(0, Math.Min(3, _store.Settings.EngineeringMode));
            _mode.SelectedIndexChanged += delegate
            {
                _store.Settings.EngineeringMode = _mode.SelectedIndex;
                _store.SaveSettingsOnly();
                Navigate(new[] { "tia", "incidents", "validation", "projects" }[_mode.SelectedIndex]);
            };

            _globalSearch = Input("搜索工程资料", 290);
            _globalSearch.Name = "engineering-global-search";
            _globalSearch.KeyPressed += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                Navigate("search");
                Views.ViewBase service;
                if (_serviceViews.TryGetValue("search", out service))
                    ((Views.SearchView)service).SearchText(_globalSearch.Text);
            };
            var commands = Button("命令  Ctrl+K", delegate
            {
                var main = FindForm() as MainForm;
                if (main != null) main.ShowCommandPalette();
            }, false, 116);
            commands.AccessibleName = "全局命令 Ctrl+K";
            DarkButton more = null;
            more = Button("工具", () => ShowMore(more), false, 68);
            more.AccessibleName = "常用工具";
            header.Controls.AddRange(new Control[] { expand, brand, _projectPicker, _mode, _globalSearch, commands, more });
            Action layout = delegate
            {
                int dpi = Views.TiaWorkbenchStyle.LogicalDpi(header);
                Func<int, int> px = value => value * dpi / 96;
                header.Height = px(54);
                int top = px(9), height = px(36), gap = px(8);
                int right = header.ClientSize.Width - px(12);
                bool narrow = header.Width < px(760);
                more.SetBounds(right - px(66), top, px(66), height);
                commands.Text = narrow ? "命令" : "命令  Ctrl+K";
                int commandWidth = px(narrow ? 64 : 116);
                commands.SetBounds(more.Left - gap - commandWidth, top, commandWidth, height);
                int modeWidth = px(narrow ? 116 : 151);
                _mode.SetBounds(commands.Left - gap - modeWidth, top, modeWidth, height);
                expand.SetBounds(px(10), top, px(40), height);
                brand.Visible = !narrow;
                brand.SetBounds(expand.Right + gap, top, px(108), height);
                int left = (narrow ? expand.Right : brand.Right) + gap;
                int remaining = Math.Max(0, _mode.Left - gap - left);
                _globalSearch.Visible = remaining >= px(415);
                int pickerWidth = _globalSearch.Visible ? Math.Min(px(410), remaining * 5 / 9) : remaining;
                _projectPicker.SetBounds(left, top, pickerWidth, height);
                _globalSearch.SetBounds(_projectPicker.Right + gap, top, Math.Max(0, remaining - pickerWidth - gap), height);
            };
            header.SizeChanged += delegate { layout(); };

            _rail = new Panel { Name = "engineering-navigation", Dock = DockStyle.Left, BackColor = _p.HeaderBg, AutoScroll = true };
            foreach (int i in Enumerable.Range(0, EngineeringNavigation.Modules.GetLength(0)))
            {
                string id = EngineeringNavigation.Modules[i, 0];
                string title = EngineeringNavigation.Modules[i, 2];
                var button = Button(title, () => Navigate(id));
                button.Font = FontOf(10.5f);
                button.Name = "nav-" + id;
                button.AccessibleName = title;
                button.Navigation = true;
                button.Symbol = EngineeringNavigation.Modules[i, 1];
                _rail.Controls.Add(button);
                _nav[id] = button;
            }
            _rail.SizeChanged += delegate { UpdateNavigationRail(); };
            _subnav = Toolbar();
            _subnav.Height = WorkbenchPixels(45);
            _subnav.Padding = new Padding(12, 5, 8, 0);
            _subnav.Name = "engineering-secondary-nav";
            var outer = new Panel { Dock = DockStyle.Fill, BackColor = _p.Bg };
            _content = new Panel { Dock = DockStyle.Fill, BackColor = _p.Bg };
            outer.Controls.Add(_content);
            outer.Controls.Add(_subnav);
            _status = TextLabel("", 9, false, _p.TextDim);
            _status.Dock = DockStyle.Bottom;
            _status.Height = WorkbenchPixels(26);
            _status.Padding = new Padding(WorkbenchPixels(12), WorkbenchPixels(3), 0, 0);
            Controls.Add(outer);
            Controls.Add(_rail);
            Controls.Add(_status);
            Controls.Add(header);
            HandleCreated += delegate { layout(); UpdateNavigationRail(); };
            UpdateNavigationRail();
            layout();
            RefreshStatus();
        }

        private void RefreshProjectPicker()
        {
            if (_projectPicker == null) return;
            var projects = _projects.Items;
            var current = CurrentProject();
            var ids = new[] { "" }.Concat(projects.Select(project => project.Id)).ToArray();
            var names = new[] { "未绑定工程 · 全局工具" }.Concat(projects.Select(project =>
                string.IsNullOrWhiteSpace(project.Number) ? project.Name : project.Name + " · " + project.Number)).ToArray();
            _updatingPicker = true;
            try
            {
                if (!_projectIds.SequenceEqual(ids) || !_projectPicker.Inner.Items.Cast<string>().SequenceEqual(names))
                {
                    _projectIds = ids;
                    _projectPicker.Inner.Items.Clear();
                    _projectPicker.Inner.Items.AddRange(names);
                }
                _projectPicker.SelectedIndex = current == null ? 0 : Array.IndexOf(ids, current.Id);
                _railTips.SetToolTip(_projectPicker, current == null ? "选择工程项目，或在项目中心新增。" : current.Name + " · " + current.Root);
            }
            finally { _updatingPicker = false; }
        }

        private void BuildSecondaryNavigation(string page)
        {
            var group = EngineeringNavigation.Secondary.FirstOrDefault(section => section[0] == EngineeringNavigation.ParentOf(page));
            _subnav.SuspendLayout();
            while (_subnav.Controls.Count > 0) _subnav.Controls[0].Dispose();
            _subnav.Visible = group != null;
            if (group != null)
            {
                for (int i = 0; i < group.Length; i += 2)
                {
                    string id = group[i];
                    var button = Button(group[i + 1], () => Navigate(id), id == page, 124);
                    button.Name = "section-" + id;
                    _subnav.Controls.Add(button);
                }
            }
            _subnav.ResumeLayout();
        }

        public void Navigate(string page)
        {
            if (!ReferencePages.Contains(page) && !EngineeringNavigation.Secondary.Any(group => group.Contains(page)) &&
                !new[] { "operations", "mission", "history", "weather" }.Contains(page))
            { ShowLegacy(page); return; }
            if (!_pages.ContainsKey(page))
            {
                Control control;
                switch (page)
                {
                    case "home": control = BuildEngineeringDashboard(); break;
                    case "operations": control = BuildOperationsDesk(); break;
                    case "mission": control = BuildMissionHome(); break;
                    case "desktop": control = BuildDesktopPage(); break;
                    case "history": control = BuildHistoryPage(); break;
                    case "projects": control = BuildPortfolio(); break;
                    case "project-detail": control = BuildProjectWorkspace(); break;
                    case "project-context": control = BuildProjectContext(); break;
                    case "validation": control = BuildValidationCenter(); break;
                    case "dcs": control = BuildDcsStudio(); break;
                    case "engineering": control = BuildToolStudio(); break;
                    case "tia": case "settings": case "library": case "filehistory": case "mcp": control = _legacyFactory(page); break;
                    case "tool-workspace": control = BuildToolWorkspace(); break;
                    case "calculator": control = BuildCalculator(); break;
                    case "tasks": control = BuildTasks(); break;
                    case "incidents": control = BuildIncidents(); break;
                    case "weather": control = BuildWeather(); break;
                    case "mobile": control = BuildMobile(); break;
                    default: control = BuildServicePage(page); break;
                }
                if (control == null) { _status.Text = "无法打开 " + EngineeringNavigation.Title(page); return; }
                control.Name = "page-" + page;
                control.Dock = DockStyle.Fill;
                _pages[page] = control;
                _content.Controls.Add(control);
            }
            if (_current != page || _subnav.Controls.Count == 0) BuildSecondaryNavigation(page);
            _current = page;
            foreach (var item in _pages) item.Value.Visible = item.Key == page;
            _pages[page].BringToFront();
            foreach (var item in _nav) { item.Value.Primary = item.Key == EngineeringNavigation.ParentOf(page); item.Value.Invalidate(); }
            Action reload;
            if (_reload.TryGetValue(page, out reload)) reload();
            var view = _pages[page] as Views.ViewBase;
            if (view != null) view.Activate();
            RefreshStatus();
            if (PageChanged != null) PageChanged(page);
        }

        private void UpdateNavigationRail()
        {
            if (_layingOutRail) return;
            _layingOutRail = true;
            _rail.SuspendLayout();
            try
            {
                bool compact = !_store.Settings.EngineeringNavExpanded;
                _rail.Width = WorkbenchPixels(compact ? 62 : 194);
                int logicalHeight = 8 + 43 * _nav.Count;
                int scrollSpace = _rail.Height < WorkbenchPixels(logicalHeight)
                    ? Math.Max(SystemInformation.VerticalScrollBarWidth, WorkbenchPixels(18)) : 0;
                int left = WorkbenchPixels(5);
                int buttonWidth = Math.Max(1, Math.Min(WorkbenchPixels(compact ? 48 : 174), _rail.Width - scrollSpace - 2 * left));
                int scrollOffset = _rail.AutoScrollPosition.Y;
                int i = 0;
                foreach (var button in _nav.Values)
                {
                    button.CompactNavigation = compact;
                    button.SetBounds(left, WorkbenchPixels(8 + 43 * i++) + scrollOffset, buttonWidth, WorkbenchPixels(38));
                    _railTips.SetToolTip(button, button.Text);
                    button.Invalidate();
                }
            }
            finally
            {
                _rail.ResumeLayout();
                _layingOutRail = false;
            }
        }

        internal bool InvalidateProjectTia()
        {
            Control view;
            if (!_pages.TryGetValue("tia", out view)) return false;
            _pages.Remove("tia");
            view.Dispose();
            return _current == "tia" && Visible;
        }

        internal void SetProjectContext(ProjectContext context)
        {
            Views.ViewBase service;
            if (_serviceViews.TryGetValue("search", out service)) ((Views.SearchView)service).SetProjectContext(context);
            _globalSearch.Text = "";
            if (_current == "search")
            {
                Action searchReload;
                if (_reload.TryGetValue("search", out searchReload)) searchReload();
            }
            Control view;
            if (_pages.TryGetValue("mcp", out view)) ((Views.McpCenterView)view).ProjectChanged();
            DataChanged();
        }

        public void RefreshStatus()
        {
            if (_status == null) return;
            RefreshProjectPicker();
            var scope = _projects.Context;
            int open = _store.Tasks.Count(task => !task.Done && (scope == null || scope.IncludesTask(task)));
            int issues = _sync.Incidents.Count(item => item.Status != "closed" && (scope == null || item.ProjectId == scope.Id));
            if (!string.IsNullOrWhiteSpace(_projects.RecoveryNotice))
                _railTips.SetToolTip(_status, _projects.RecoveryNotice);
            _status.Text = (_projects.RecoveryNotice == null ? "" : "工程配置恢复提示 · ") +
                (scope == null ? "全局工具" : scope.Name) + "   ·   " + open + " 项待办   ·   " + issues + " 项未关闭事件   ·   "
                + (_cloud.Connected ? "云同步在线" : _sync.Running ? "局域网同步在线" : "同步未连接") + "   ·   Ctrl+K";
        }

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
        private void DataChanged(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action(DataChanged));}catch{}return;}RefreshStatus();Action reload;if(new[]{"home","operations","mission","projects","project-detail","project-context","tasks","incidents","validation","dcs"}.Contains(_current)&&_reload.TryGetValue(_current,out reload))reload();}
        private void CloudChanged(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action(CloudChanged));}catch{}return;}RefreshStatus();Action reload;if(_current=="mobile"&&_reload.TryGetValue("mobile",out reload))reload();}
        private void ShowMore(Control anchor)
        {
            var menu = MenuBuilder.Create(_p);
            foreach (var entry in new[] { new[] { "工程软件启动台", "operations" }, new[] { "经典项目总览", "mission" }, new[] { "工程计算", "engineering" },
                new[] { "任务", "tasks" }, new[] { "工作记录", "history" }, new[] { "天气与时间", "weather" } })
            {
                var id = entry[1];
                menu.Items.Add(MenuBuilder.Item(entry[0], () => Navigate(id)));
            }
            foreach (var entry in new[] { new[] { "系统监控", "monitor" }, new[] { "剪贴板", "clipboard" },
                new[] { "通知中心", "notifications" }, new[] { "磁盘分析", "disk" } })
            {
                var id = entry[1];
                menu.Items.Add(MenuBuilder.Item(entry[0], () => ShowLegacy(id)));
            }
            menu.Items.Add(MenuBuilder.Item("切换经典模式", () => { if (ClassicRequested != null) ClassicRequested(); }));
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        internal void ShowLegacy(string name, Action<Control> configure = null)
        {
            using(var dialog=new Form{Text="DeskBox · "+name,StartPosition=FormStartPosition.CenterParent,Size=new Size(1120,760),BackColor=_p.Bg})
            {var view=_legacyFactory(name);if(view==null)return;view.Dock=DockStyle.Fill;dialog.Controls.Add(view);var baseView=view as Views.ViewBase;if(baseView!=null){baseView.ApplyPalette(_p);baseView.Activate();}if(configure!=null)configure(view);dialog.ShowDialog(this);}DataChanged();
        }
        private void OpenPath(string path){if(!File.Exists(path)&&!Directory.Exists(path)){_status.Text="路径已不存在："+path;return;}Win32.OpenPath(path);if(File.Exists(path))_library.Touch(path);}
        private EngineerWorkspace CurrentProject(){return _projects.Current;}
        private void ShowProject(EngineerWorkspace project){_projects.SetCurrent(project.Id);Navigate("project-detail");}
        protected override void Dispose(bool disposing){if(disposing){_projects.Changed-=DataChanged;_sync.Changed-=DataChanged;_cloud.Changed-=CloudChanged;_railTips.Dispose();}base.Dispose(disposing);if(disposing){if(_factory!=null)_factory.Dispose();foreach(var font in _fonts)font.Dispose();}}
    }

    internal class ReferenceCard : Panel
    {
        private Palette _palette;
        public void ApplyPalette(Palette palette){_palette=palette;BackColor=palette.CardBg;Invalidate();}
        public ReferenceCard(Palette palette){_palette=palette;BackColor=palette.CardBg;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
        protected override void OnPaint(PaintEventArgs e){if(Width<2||Height<2)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;var r=new Rectangle(0,0,Width-1,Height-1);using(var path=Palette.RoundedRect(r,12))using(var brush=new SolidBrush(_palette.CardBg))e.Graphics.FillPath(brush,path);Palette.DrawRounded(e.Graphics,_palette.Line,r,12,1);base.OnPaint(e);}
    }
}
