using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>任务与提醒：完整 CRUD、优先级、截止、提醒、筛选排序、应用内通知。</summary>
    public class TasksView : ViewBase
    {
        private readonly Store _store;
        private DarkList _list;
        private DarkInput _quick;
        private DarkSelect _filter, _sort;
        private DarkButton _btnAdd, _btnEdit, _btnDone, _btnDelete, _btnRemind;
        private Label _stat;
        private Panel _overview;
        private bool _referenceStyle;
        public override void UseReferenceLayout(){base.UseReferenceLayout();_referenceStyle=true;_overview.Height=108;_overview.Invalidate();BuildDetails();BuildReferenceToolbar();BuildCalendar();}
        private Panel _calendarHost;
        private TaskMonthCalendar _calendar;
        private DarkList _dayTasks;
        private void BuildCalendar()
        {
            if(_calendarHost!=null)return;_calendarHost=new Panel{Dock=DockStyle.Fill,Visible=false};_calendar=new TaskMonthCalendar(_store,Palette){Dock=DockStyle.Fill};_dayTasks=new DarkList{Dock=DockStyle.Bottom,Height=150,Palette=Palette,AutoFitColumns=true,CardSurface=true,RowHeight=38};_dayTasks.SetColumns(new[]{"当天任务","状态","优先级"},new[]{420,100,90});_calendarHost.Controls.Add(_calendar);_calendarHost.Controls.Add(_dayTasks);Body.Controls.Add(_calendarHost);_calendarHost.BringToFront();_list.BringToFront();
            _calendar.DateSelected+=date=>ReloadDayTasks();_dayTasks.RowActivated+=delegate{if(_dayTasks.SelectedRow==null)return;string id=(string)_dayTasks.SelectedRow.Tag;_filter.SelectedIndex=0;ReloadData();int index=_list.Rows.FindIndex(row=>(string)row.Tag==id);if(index>=0)_list.SelectIndex(index);};
            var toggle=new DarkButton("月历 / 列表"){Palette=Palette,Width=115,Height=34,Margin=new Padding(0,0,8,10)};toggle.Click+=delegate{_calendarHost.Visible=!_calendarHost.Visible;_list.Visible=!_calendarHost.Visible;if(_calendarHost.Visible){_calendarHost.BringToFront();ReloadDayTasks();}else _list.BringToFront();};_referenceFilters.SetFlowBreak(_sort,false);_referenceFilters.Controls.Add(toggle);_referenceFilters.Controls.SetChildIndex(_stat,_referenceFilters.Controls.Count-1);_referenceFilters.SetFlowBreak(toggle,true);
        }
        private void ReloadDayTasks(){if(_calendar==null)return;_calendar.Invalidate();_dayTasks.SetRows(_store.Tasks.Where(t=>t.Due==_calendar.SelectedDate.ToString("yyyy-MM-dd")).Select(t=>new ListRow{Cells=new[]{t.Title,t.Done?"已完成":"待办",PriorityText(t.Priority)},Tag=t.Id}).ToList());}
        private FlowLayoutPanel _referenceFilters;
        private void BuildReferenceToolbar()
        {
            if(_referenceFilters!=null)return;var oldToolbar=_quick.Parent;_referenceFilters=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,BackColor=Palette.Bg};
            foreach(var control in new Control[]{_quick,_btnAdd,_btnEdit,_btnDone,_btnRemind,_btnDelete,_filter,_sort}){control.Height=34;control.Margin=new Padding(0,0,8,10);_referenceFilters.Controls.Add(control);}
            _stat.Margin=new Padding(0,2,8,10);_referenceFilters.Controls.Add(_stat);_referenceFilters.SetFlowBreak(_sort,true);_quick.Width=300;Body.Controls.Add(_referenceFilters);oldToolbar.Dispose();_list.BringToFront();
        }
        private Panel _details;
        private DarkInput _detailTitle,_detailDue,_detailReminder,_detailProject,_detailNotes;
        private DarkSelect _detailPriority;
        private Label _detailStatus;
        private string _detailId="",_detailOriginal="";
        private void BuildDetails()
        {
            if(_details!=null)return;
            _details=new Panel{Dock=DockStyle.Right,Width=310,BackColor=Palette.CardBg,Padding=new Padding(14)};
            var heading=new Label{Dock=DockStyle.Top,Height=34,Text="TASK DETAILS  /  任务详情",ForeColor=Palette.Accent,Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold)};
            var fields=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,AutoScroll=true};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            _detailTitle=new DarkInput{Palette=Palette};_detailDue=new DarkInput{Palette=Palette,Placeholder="yyyy-MM-dd"};_detailReminder=new DarkInput{Palette=Palette,Placeholder="yyyy-MM-dd HH:mm"};_detailProject=new DarkInput{Palette=Palette};_detailNotes=new DarkInput{Palette=Palette};_detailNotes.Inner.Multiline=true;
            _detailPriority=new DarkSelect(new[]{"普通","高","低"}){Palette=Palette};
            string[] names={"标题","优先级","截止日期","提醒时间","关联项目","备注"};Control[] inputs={_detailTitle,_detailPriority,_detailDue,_detailReminder,_detailProject,_detailNotes};
            for(int i=0;i<inputs.Length;i++){fields.RowStyles.Add(new RowStyle(SizeType.Absolute,25));fields.RowStyles.Add(new RowStyle(SizeType.Absolute,i==5?110:42));fields.Controls.Add(new Label{Text=names[i],ForeColor=Palette.TextDim,Dock=DockStyle.Fill},0,i*2);inputs[i].Dock=DockStyle.Fill;inputs[i].Margin=new Padding(0,0,0,8);fields.Controls.Add(inputs[i],0,i*2+1);}
            var save=new DarkButton("保存详情"){Palette=Palette,Primary=true,Dock=DockStyle.Bottom,Height=38};save.Click+=delegate{try{var item=_store.Tasks.FirstOrDefault(t=>t.Id==_detailId);if(item==null)throw new InvalidOperationException("先选择任务");if(Json.ToJson(item)!=_detailOriginal)throw new InvalidOperationException("任务已在另一端更新，请重新选择后编辑");DateTime due,remind;if(_detailDue.Text.Length>0&&!DateTime.TryParseExact(_detailDue.Text,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out due))throw new ArgumentException("截止日期应为 yyyy-MM-dd");if(_detailReminder.Text.Length>0&&!DateTime.TryParseExact(_detailReminder.Text,"yyyy-MM-dd HH:mm",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out remind))throw new ArgumentException("提醒应为 yyyy-MM-dd HH:mm");if(string.IsNullOrWhiteSpace(_detailTitle.Text))throw new ArgumentException("标题不能为空");item.Title=_detailTitle.Text.Trim();item.Due=_detailDue.Text;item.RemindAt=_detailReminder.Text;item.Reminded=false;item.Priority=_detailPriority.SelectedIndex==1?"high":_detailPriority.SelectedIndex==2?"low":"normal";item.Project=_detailProject.Text.Trim();item.Notes=_detailNotes.Text;_store.SaveTasksOnly();_detailOriginal=Json.ToJson(item);_store.Log("info","task","更新任务详情："+item.Title);ReloadData();_detailStatus.Text="已保存，等待云同步";}catch(Exception ex){_detailStatus.Text=ex.Message;}};
            _detailStatus=new Label{Dock=DockStyle.Bottom,Height=48,ForeColor=Palette.TextDim,Text="选择任务查看详情；保存前检查同步版本"};_details.Controls.Add(fields);_details.Controls.Add(save);_details.Controls.Add(_detailStatus);_details.Controls.Add(heading);fields.BringToFront();Body.Controls.Add(_details);_list.BringToFront();
            _list.SelectionChanged+=delegate{var item=Selected();if(item==null)return;_detailId=item.Id;_detailOriginal=Json.ToJson(item);_detailTitle.Text=item.Title;_detailDue.Text=item.Due;_detailReminder.Text=item.RemindAt;_detailProject.Text=item.Project;_detailNotes.Text=item.Notes;_detailPriority.SelectedIndex=item.Priority=="high"?1:item.Priority=="low"?2:0;_detailStatus.Text=item.Done?"已完成":"待办 · 支持编辑提醒与备注";};
        }
        private string _currentFilter = "all";   // all | todo | done | overdue

        public TasksView(Store store, NotificationStore notifications = null) : base(store, "任务与提醒", "创建、编辑、完成；提醒由通知中心在全应用范围内处理")
        {
            _store = store;
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Palette.Bg };

            _quick = new DarkInput { Left = 0, Top = 2, Width = 380, Placeholder = "输入任务标题，回车快速添加" };
            _quick.Palette = Palette;
            _quick.KeyPressed += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; QuickAdd(); }
            };

            _btnAdd = new DarkButton("＋ 添加") { Primary = true, Left = 388, Top = 2, Width = 84 };
            _btnAdd.Click += delegate { QuickAdd(); };
            _btnEdit = new DarkButton("编辑") { Left = 480, Top = 2, Width = 70 };
            _btnEdit.Click += delegate { EditSelected(); };
            _btnDone = new DarkButton("完成/取消") { Left = 558, Top = 2, Width = 90 };
            _btnDone.Click += delegate { ToggleSelected(); };
            _btnRemind = new DarkButton("提醒") { Left = 656, Top = 2, Width = 70 };
            _btnRemind.Click += delegate { SetReminder(); };
            _btnDelete = new DarkButton("删除") { Left = 734, Top = 2, Width = 70 };
            _btnDelete.Click += delegate { DeleteSelected(); };

            _filter = new DarkSelect(new[] { "全部", "未完成", "已完成", "已逾期" }) { Left = 812, Top = 2, Width = 100 };
            _filter.Palette = Palette;
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += delegate
            {
                _currentFilter = _filter.SelectedIndex == 1 ? "todo" : _filter.SelectedIndex == 2 ? "done" : _filter.SelectedIndex == 3 ? "overdue" : "all";
                ReloadData();
            };

            _sort = new DarkSelect(new[] { "按创建时间", "按截止日期", "按优先级" }) { Left = 920, Top = 2, Width = 110 };
            _sort.Palette = Palette;
            _sort.SelectedIndexChanged += delegate { ReloadData(); };

            _stat = new Label
            {
                Left = 0, Top = 44, AutoSize = true, ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg, Text = ""
            };
            top.Controls.AddRange(new Control[] { _quick, _btnAdd, _btnEdit, _btnDone, _btnRemind, _btnDelete, _filter, _sort, _stat });
            top.AutoScroll = true;
            _overview = new Panel { Dock = DockStyle.Top, Height = 126, BackColor = Palette.Bg };
            _overview.Paint += PaintOverview;

            _list = new DarkList { Dock = DockStyle.Fill, RowHeight = 34 };
            _list.Palette = Palette;
            _list.SetColumns(new[] { "状态", "标题", "优先级", "截止", "提醒", "项目" }, new[] { 70, 520, 90, 120, 140, 180 });
            _list.Resize += delegate {
                if(!_referenceStyle)return;
                _list.SetColumns(new[]{"状态","标题","优先级","截止","提醒","项目"},_list.ClientSize.Width<760 ? new[]{56,520,72,100,0,0}:new[]{70,520,90,120,140,180});
            };
            _list.RowActivated += delegate { ToggleSelected(); };
            _list.RowContextMenu += delegate(object s, MouseEventArgs e)
            {
                var menu = MenuBuilder.Create(Palette);
                menu.Items.Add(MenuBuilder.Item("编辑", EditSelected));
                menu.Items.Add(MenuBuilder.Item("标记完成/取消", ToggleSelected));
                menu.Items.Add(MenuBuilder.Item("设置提醒…", SetReminder));
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(MenuBuilder.Item("删除", DeleteSelected));
                menu.Show(_list, e.Location);
            };

            Body.Controls.Add(_list);
            Body.Controls.Add(_overview);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        // ---------------- 数据 ----------------

        public void ReloadData()
        {
            if (_list == null || _store == null) return;
            _overview.Invalidate();
            var all = _store.Tasks ?? new List<TaskItem>();
            IEnumerable<TaskItem> q = all;
            switch (_currentFilter)
            {
                case "todo": q = q.Where(t => !t.Done); break;
                case "done": q = q.Where(t => t.Done); break;
                case "overdue": q = q.Where(t => !t.Done && IsOverdue(t)); break;
            }
            var list = q.ToList();
            if (_sort.SelectedIndex == 1) list = list.OrderBy(t => ParseDue(t.Due) ?? DateTime.MaxValue).ToList();
            else if (_sort.SelectedIndex == 2) list = list.OrderByDescending(t => PriorityRank(t.Priority)).ToList();
            else list = list.OrderByDescending(t => t.CreatedAt).ToList();

            var rows = new List<ListRow>();
            foreach (var t in list)
            {
                rows.Add(new ListRow
                {
                    Icon = t.Done ? "✅" : (IsOverdue(t) ? "⚠" : "○"),
                    Cells = new[]
                    {
                        t.Done ? "已完成" : (IsOverdue(t) ? "已逾期" : "待办"),
                        t.Title,
                        PriorityText(t.Priority),
                        string.IsNullOrEmpty(t.Due) ? "—" : t.Due,
                        string.IsNullOrEmpty(t.RemindAt) ? "—" : t.RemindAt,
                        string.IsNullOrEmpty(t.Project) ? "—" : t.Project
                    },
                    Tag = t.Id,
                    Accent = t.Done ? (Color?)Palette.TextFaint : (IsOverdue(t) ? (Color?)Color.FromArgb(248, 113, 113) : (Color?)null)
                });
            }
            _list.SetRows(rows);
            ReloadDayTasks();
            if(_referenceStyle&&_detailId.Length==0&&rows.Count>0)_list.SelectIndex(0);
            int todo = all.Count(t => !t.Done);
            _stat.Text = "共 " + all.Count + " 项 · 未完成 " + todo + " · 已逾期 " + all.Count(t => !t.Done && IsOverdue(t)) + " · 已提醒 " + all.Count(t => !string.IsNullOrEmpty(t.RemindAt));
        }

        private void PaintOverview(object sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var tasks = _store.Tasks ?? new List<TaskItem>();
            if(_referenceStyle)
            {
                int[] counts={tasks.Count(t=>!t.Done),tasks.Count(t=>!t.Done&&IsOverdue(t)),tasks.Count(t=>!t.Done&&t.Priority=="high"),tasks.Count(t=>t.Done)};string[] captions={"进行中","已逾期","高优先级","已完成"};int width=Math.Max(1,(_overview.Width-36)/4);
                using(var number=new Font("Segoe UI",width<125?20:25,FontStyle.Bold))using(var label=new Font("Microsoft YaHei UI",9))for(int i=0;i<4;i++){var rect=new Rectangle(i*(width+12),8,width,100);Palette.FillRounded(g,Palette.CardBg,rect,10);Palette.DrawRounded(g,Palette.Line,rect,10,1);using(var text=new SolidBrush(i==1&&counts[i]>0?Color.FromArgb(255,105,120):Palette.Accent))g.DrawString(counts[i].ToString(),number,text,rect.X+12,rect.Y+11);using(var text=new SolidBrush(Palette.TextDim))g.DrawString(captions[i],label,text,rect.X+12,rect.Y+64);}return;
            }
            int done = tasks.Count(x => x.Done), open = tasks.Count(x => !x.Done), overdue = tasks.Count(x => !x.Done && IsOverdue(x)), high = tasks.Count(x => !x.Done && x.Priority == "high");
            float completed = tasks.Count == 0 ? 0 : (float)done / tasks.Count;
            var bounds = new Rectangle(6, 8, Math.Max(1, _overview.Width - 14), 104);
            Palette.FillRounded(g, Palette.CardBg, bounds, 10);
            using (var track = new Pen(Palette.Line, 8)) g.DrawEllipse(track, 24, 25, 68, 68);
            using (var progress = new Pen(Palette.Accent, 8) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(progress, 24, 25, 68, 68, -90, completed * 360);
            using (var number = new Font("Consolas", 20, FontStyle.Bold))
            using (var caption = new Font("Microsoft YaHei UI", 9))
            using (var text = new SolidBrush(Palette.Text))
            using (var dim = new SolidBrush(Palette.TextDim))
            {
                g.DrawString((completed * 100).ToString("0") + "%", caption, text, 41, 50);
                string[] labels = { "OPEN / 待办", "OVERDUE / 逾期", "HIGH PRIORITY / 高优先级", "COMPLETED / 已完成" };
                int[] values = { open, overdue, high, done };
                int cell = Math.Max(120, (_overview.Width - 150) / 4);
                for (int i = 0; i < 4; i++)
                {
                    int x = 120 + cell * i;
                    g.DrawString(labels[i], caption, dim, x, 28);
                    using (var value = new SolidBrush(i == 1 && overdue > 0 ? Color.FromArgb(255, 95, 115) : Palette.Accent)) g.DrawString(values[i].ToString(), number, value, x, 53);
                }
            }
        }

        private static DateTime? ParseDue(string due)
        {
            if (string.IsNullOrWhiteSpace(due)) return null;
            DateTime d;
            return DateTime.TryParse(due, out d) ? (DateTime?)d : null;
        }

        private static bool IsOverdue(TaskItem t)
        {
            var d = ParseDue(t.Due);
            return d.HasValue && d.Value.Date < DateTime.Today;
        }

        private static int PriorityRank(string p)
        {
            if (p == "high") return 3;
            if (p == "low") return 1;
            return 2;
        }

        private static string PriorityText(string p)
        {
            if (p == "high") return "高";
            if (p == "low") return "低";
            return "普通";
        }

        private TaskItem Selected()
        {
            var row = _list.SelectedRow;
            if (row == null) return null;
            return _store.Tasks.FirstOrDefault(t => t.Id == (string)row.Tag);
        }

        // ---------------- 操作 ----------------

        private void QuickAdd()
        {
            var title = (_quick.Text ?? "").Trim();
            if (title.Length == 0) return;
            var t = new TaskItem
            {
                Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                Title = title,
                Priority = DetectPriority(title),
                Due = DetectDue(title),
                CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
            _store.Tasks.Add(t);
            _store.SaveTasksOnly();
            _store.Log("info", "task", "新建任务：" + t.Title);
            _quick.Text = "";
            ReloadData();
        }

        private static string DetectPriority(string title)
        {
            if (title.Contains("紧急") || title.Contains("!！")) return "high";
            if (title.Contains("有空") || title.Contains("低优先")) return "low";
            return "normal";
        }

        private static string DetectDue(string title)
        {
            // "今天/明天/后天" 自动带出截止日期
            var today = DateTime.Today;
            if (title.Contains("今天")) return today.ToString("yyyy-MM-dd");
            if (title.Contains("明天")) return today.AddDays(1).ToString("yyyy-MM-dd");
            if (title.Contains("后天")) return today.AddDays(2).ToString("yyyy-MM-dd");
            if (title.Contains("本周")) return today.AddDays(7 - (int)today.DayOfWeek).ToString("yyyy-MM-dd");
            return "";
        }

        private void EditSelected()
        {
            var t = Selected();
            if (t == null) { _stat.Text = "先选中一条任务"; return; }
            var title = Prompt.Input(this, "编辑任务", "标题", t.Title, Palette);
            if (title == null) return;
            if (title.Trim().Length == 0) return;
            t.Title = title.Trim();
            _store.SaveTasksOnly();
            _store.Log("info", "task", "修改任务：" + t.Title);
            ReloadData();
        }

        private void ToggleSelected()
        {
            var t = Selected();
            if (t == null) return;
            t.Done = !t.Done;
            t.DoneAt = t.Done ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : 0;
            _store.SaveTasksOnly();
            _store.Log("info", "task", (t.Done ? "完成任务：" : "恢复任务：") + t.Title);
            ReloadData();
        }

        private void DeleteSelected()
        {
            var t = Selected();
            if (t == null) return;
            if (!Prompt.Confirm(this, "删除任务", "删除「" + t.Title + "」？此操作不可撤销。", Palette)) return;
            _store.Tasks.Remove(t);
            _store.SaveTasksOnly();
            _store.Log("info", "task", "删除任务：" + t.Title);
            ReloadData();
        }

        private void SetReminder()
        {
            var t = Selected();
            if (t == null) { _stat.Text = "先选中一条任务"; return; }
            var text = Prompt.Input(this, "设置提醒", "提醒时间（格式 2026-10-30 09:00，留空取消提醒）",
                string.IsNullOrEmpty(t.RemindAt) ? DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm") : t.RemindAt, Palette);
            if (text == null) return;
            if (string.IsNullOrWhiteSpace(text))
            {
                t.RemindAt = "";
                t.Reminded = false;
                _store.SaveTasksOnly();
                ReloadData();
                return;
            }
            DateTime when;
            if (!DateTime.TryParse(text, out when))
            {
                MessageBox.Show(this, "时间格式无法识别，请用 2026-10-30 09:00 这样的格式。", "DeskBox");
                return;
            }
            t.RemindAt = when.ToString("yyyy-MM-dd HH:mm");
            t.Reminded = false;
            _store.SaveTasksOnly();
            _store.Log("info", "task", "设置提醒：" + t.Title + " @ " + t.RemindAt);
            ReloadData();
        }

        protected override void OnActivated() { ReloadData(); }
    }
}
