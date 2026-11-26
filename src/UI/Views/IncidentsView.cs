using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Sync;

namespace DeskBox.UI.Views
{
    public class IncidentsView : ViewBase
    {
        private readonly LocalSyncCoordinator _sync;
        private readonly WorkspaceStore _projects;
        private ProjectContext _project;
        public void SetProjectContext(ProjectContext project)
        {
            bool changed = (_project == null ? "" : _project.Id) != (project == null ? "" : project.Id);
            _project = project;
            if (changed) _search.Text = "";
            ReloadData();
        }
        private DarkList _list;
        private DarkInput _search;
        private DarkSelect _filter;
        private Label _summary;
        private TextBox _detailText;
        private Label _metrics;
        public override void UseReferenceLayout()
        {
            base.UseReferenceLayout();if(_detailText!=null)return;
            var filters=_search.Parent as FlowLayoutPanel;if(filters!=null){filters.WrapContents=true;filters.AutoSize=true;filters.AutoSizeMode=AutoSizeMode.GrowAndShrink;}
            var detail=new Panel{Dock=DockStyle.Right,Width=320,Padding=new Padding(18),BackColor=Palette.CardBg};
            _detailText=new TextBox{Dock=DockStyle.Fill,ForeColor=Palette.Text,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",10),Text="选择事件，查看现象、调查与处理措施",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None};
            var edit=new DarkButton("调查 / 更新处理结果"){Palette=Palette,Primary=true,Dock=DockStyle.Bottom,Height=38};edit.Click+=delegate{if(_list.SelectedRow!=null)Edit(_list.SelectedRow.Tag as SyncRecord);};detail.Controls.Add(_detailText);detail.Controls.Add(edit);
            _metrics=new Label{Dock=DockStyle.Top,Height=78,Padding=new Padding(14,16,0,0),ForeColor=Palette.Accent,BackColor=Palette.CardBg,Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold)};
            Body.Controls.Add(detail);Body.Controls.Add(_metrics);_list.BringToFront();_list.SelectionChanged+=delegate{var record=_list.SelectedRow==null?null:_list.SelectedRow.Tag as SyncRecord;if(record==null){_detailText.Text="选择事件，查看现象、调查与处理措施";return;}var item=Json.FromJson<IncidentDto>(record.Json);if(item==null)return;_detailText.Text="事件详情   /   "+item.Severity+"\r\n\r\n"+ProjectName(item.ProjectId)+"\r\n设备："+item.Equipment+"\r\n负责人："+item.Owner+"\r\n状态："+StateText(item.Status)+"\r\n\r\n现象描述\r\n"+item.Description+"\r\n\r\n调查 / 根因\r\n"+item.Cause+"\r\n\r\n处理措施\r\n"+item.Solution+"\r\n\r\n更新时间："+DateTimeOffset.FromUnixTimeMilliseconds(item.UpdatedAt).LocalDateTime.ToString("MM-dd HH:mm")+"  ·  版本 "+record.Version;};ReloadData();
        }
        public static readonly string[] States = { "open", "investigating", "resolved", "closed" };
        public static string StateText(string state) { int index=Array.IndexOf(States,state); return index<0?state:new[]{"待处理","调查中","已解决","已关闭"}[index]; }
        public IncidentsView(Store store, LocalSyncCoordinator sync, WorkspaceStore projects) : base(store,"FIELD OPERATIONS / 现场事件","设备问题 · 项目关联 · 调查与措施 · 手机和电脑同步")
        {
            _sync=sync;_projects=projects;
            var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=52,AutoSize=false,WrapContents=false,AutoScroll=true};
            var add=new DarkButton("新建现场事件"){Width=140,Palette=Palette,Primary=true}; add.Click+=delegate{Edit(null);};
            var edit=new DarkButton("查看 / 处理"){Width=120,Palette=Palette}; edit.Click+=delegate{if(_list.SelectedRow!=null)Edit(_list.SelectedRow.Tag as SyncRecord);else _summary.Text="先选择一项现场事件";};
            _search=new DarkInput{Width=250,Palette=Palette,Placeholder="搜索项目、设备、现象或负责人"};_search.Inner.TextChanged+=delegate{ReloadData();};
            _filter=new DarkSelect(new[]{"全部状态","待处理","调查中","已解决","已关闭"}){Width=120,Palette=Palette};_filter.SelectedIndex=0;_filter.SelectedIndexChanged+=delegate{ReloadData();};
            toolbar.Controls.AddRange(new Control[]{add,edit,_search,_filter});
            _summary=new Label{Dock=DockStyle.Bottom,Height=30,ForeColor=Palette.TextDim};
            _list=new DarkList{Dock=DockStyle.Fill,Palette=Palette,RowHeight=44};
            _list.SetColumns(new[]{"状态","级别","项目 / 设备","事件描述","负责人","更新时间"},new[]{90,60,210,340,100,150});
            _list.DoubleClick+=delegate{if(_list.SelectedRow!=null)Edit(_list.SelectedRow.Tag as SyncRecord);};
            Body.Controls.Add(_list);Body.Controls.Add(toolbar);Body.Controls.Add(_summary);
            _sync.Changed+=Changed;ReloadData();
        }
        private string ProjectName(string id) { var project=_projects.Items.FirstOrDefault(p=>p.Id==id);return project==null?(string.IsNullOrEmpty(id)?"未关联项目":id):project.Name; }
        private void ReloadData()
        {
            if(_list==null)return;
            var rows=_sync.Sync.Records.Where(r=>r.EntityType=="incident"&&!r.Deleted).Select(r=>new{Record=r,Item=Json.FromJson<IncidentDto>(r.Json)}).Where(x=>x.Item!=null&&(_project==null||x.Item.ProjectId==_project.Id)).OrderByDescending(x=>x.Item.UpdatedAt).ToList();
            var query=(_search.Text??"").Trim();int state=_filter.SelectedIndex;
            if(_metrics!=null)_metrics.Text="待处理  "+rows.Count(x=>x.Item.Status=="open")+"     调查中  "+rows.Count(x=>x.Item.Status=="investigating")+"     已解决  "+rows.Count(x=>x.Item.Status=="resolved")+"     已关闭  "+rows.Count(x=>x.Item.Status=="closed");
            _list.SetRows(rows.Where(x=>(state<=0||x.Item.Status==States[state-1])&&(query.Length==0||(ProjectName(x.Item.ProjectId)+" "+x.Item.Equipment+" "+x.Item.Description+" "+x.Item.Owner+" "+x.Item.Cause+" "+x.Item.Solution).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0))
                .Select(x=>new ListRow{Icon=x.Item.Status=="closed"?"✓":"!",Cells=new[]{StateText(x.Item.Status),x.Item.Severity,ProjectName(x.Item.ProjectId)+" / "+x.Item.Equipment,x.Item.Description,x.Item.Owner,x.Item.UpdatedAt<=0?"—":DateTimeOffset.FromUnixTimeMilliseconds(x.Item.UpdatedAt).LocalDateTime.ToString("MM-dd HH:mm")},Tag=x.Record,Tip=x.Item.Description,Accent=x.Item.Severity=="S1"?Color.FromArgb(255,95,115):(Color?)null}).ToList());
            _summary.Text="共 "+rows.Count+" 项 · 待处理 "+rows.Count(x=>x.Item.Status=="open")+" · 调查中 "+rows.Count(x=>x.Item.Status=="investigating")+" · 已关闭 "+rows.Count(x=>x.Item.Status=="closed")+" · 修改后由现有同步服务回传手机";
            if(_detailText!=null){if(_list.RowCount>0)_list.SelectIndex(0);else _detailText.Text="当前范围没有匹配的现场事件";}
        }
        private void Edit(SyncRecord record)
        {
            var item=record==null?new IncidentDto{Id=Guid.NewGuid().ToString("N"),ProjectId=_projects.Current==null?"":_projects.Current.Id}:Json.FromJson<IncidentDto>(record.Json);
            if(item==null || (_project != null && item.ProjectId != _project.Id))return;
            using(var dialog=new Form{Text="现场事件 · 调查与处理",ClientSize=new Size(740,650),MinimumSize=new Size(660,620),StartPosition=FormStartPosition.CenterParent,BackColor=Palette.Bg,ForeColor=Palette.Text,Font=new Font("Microsoft YaHei UI",9F),AutoScaleMode=AutoScaleMode.Dpi})
            {
                var fields=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=2,RowCount=9,AutoScroll=true};
                fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                string[] ids=new[]{""}.Concat(_projects.Items.Select(p=>p.Id)).ToArray();
                string[] names=new[]{"未关联项目"}.Concat(_projects.Items.Select(p=>p.Name)).ToArray();
                if(!ids.Contains(item.ProjectId)){ids=ids.Concat(new[]{item.ProjectId}).ToArray();names=names.Concat(new[]{"原项目 · "+item.ProjectId}).ToArray();}
                if(_project!=null){ids=new[]{_project.Id};names=new[]{_project.Name};}
                var project=new DarkSelect(names){Palette=Palette,Enabled=_project==null};project.SelectedIndex=Math.Max(0,Array.IndexOf(ids,item.ProjectId));
                var equipment=Input(item.Equipment,false);var owner=Input(item.Owner,false);
                var severity=new DarkSelect(new[]{"S1","S2","S3","S4"}){Palette=Palette};severity.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{"S1","S2","S3","S4"},item.Severity));
                var state=new DarkSelect(States.Select(StateText).ToArray()){Palette=Palette};state.SelectedIndex=Math.Max(0,Array.IndexOf(States,item.Status));
                var description=Input(item.Description,true);var cause=Input(item.Cause,true);var solution=Input(item.Solution,true);
                Control[] controls={project,equipment,severity,state,owner,description,cause,solution};string[] labels={"关联项目","设备 / 位号","严重程度","处理状态","负责人","现象描述","调查 / 根因","处理措施"};
                for(int i=0;i<controls.Length;i++){fields.RowStyles.Add(new RowStyle(SizeType.Absolute,i<5?42:100));fields.Controls.Add(new Label{Text=labels[i],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,i);controls[i].Dock=DockStyle.Fill;fields.Controls.Add(controls[i],1,i);}
                var footer=new Panel{Dock=DockStyle.Bottom,Height=65};var status=new Label{Left=18,Top=8,Width=520,Height=45,ForeColor=Palette.TextDim,Text="解决 / 关闭前填写处理措施。保存时检查版本，避免覆盖另一端的新修改。"};
                var save=new DarkButton("保存处理结果"){Dock=DockStyle.Right,Width=150,Palette=Palette,Primary=true};
                save.Click+=delegate{
                    item.ProjectId=ids[Math.Max(0,project.SelectedIndex)];item.Equipment=equipment.Text.Trim();item.Owner=owner.Text.Trim();item.Severity=new[]{"S1","S2","S3","S4"}[severity.SelectedIndex];item.Status=States[state.SelectedIndex];item.Description=description.Text.Trim();item.Cause=cause.Text.Trim();item.Solution=solution.Text.Trim();
                    try{_sync.SaveIncident(item,record==null?0:record.Version);Store.Log("info","incident","保存现场事件："+item.Description);dialog.Close();}catch(Exception ex){status.Text=ex.Message;}
                };
                footer.Controls.Add(status);footer.Controls.Add(save);dialog.Controls.Add(fields);dialog.Controls.Add(footer);dialog.ShowDialog(this);
            }
            ReloadData();
        }
        private DarkInput Input(string text,bool multiline){var input=new DarkInput{Text=text??"",Palette=Palette};input.Inner.Multiline=multiline;if(multiline)input.Inner.ScrollBars=ScrollBars.Vertical;return input;}
        private void Changed(){if(IsDisposed)return;if(InvokeRequired){try{BeginInvoke(new Action(ReloadData));}catch{}}else ReloadData();}
        protected override void OnActivated(){ReloadData();}
        protected override void Dispose(bool disposing){if(disposing)_sync.Changed-=Changed;base.Dispose(disposing);}
    }
}
