using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildProjectWorkspace()
        {
            var page=Page("项目工程工作区","验证交付流程、项目目录、关联任务与备注在同一工作区查看");var toolbar=Toolbar();toolbar.Controls.Add(Button("返回项目目录",()=>Navigate("projects"),false,135));toolbar.Controls.Add(Button("打开项目文件夹",()=>{var p=CurrentProject();if(p!=null)OpenPath(p.Root);},true,160));toolbar.Controls.Add(Button("编辑进度与备注",()=>{var p=CurrentProject();if(p!=null)using(var dialog=new ProjectProgressForm(_projects,p,_p))dialog.ShowDialog(this);},false,160));toolbar.Controls.Add(Button("项目内搜索",()=>{var p=CurrentProject();if(p==null)return;Navigate("search");var search=_serviceViews["search"] as SearchView;if(search!=null)search.SetProjectScope(p.Root,p.Name);},false,130));
            var body=new Panel{Dock=DockStyle.Fill};var banner=Card();banner.Dock=DockStyle.Top;banner.Height=125;var name=TextLabel("",20,true);name.SetBounds(20,14,1000,40);banner.Controls.Add(name);var info=TextLabel("",10,false,_p.TextDim);info.SetBounds(22,59,1100,25);banner.Controls.Add(info);var path=TextLabel("",9,false,_p.TextDim);path.SetBounds(22,89,1100,22);banner.Controls.Add(path);var percent=TextLabel("",29,true,_p.Accent);percent.TextAlign=ContentAlignment.MiddleRight;banner.Controls.Add(percent);banner.Resize+=delegate{name.Width=Math.Max(200,banner.Width-230);info.Width=Math.Max(200,banner.Width-230);path.Width=Math.Max(200,banner.Width-230);percent.SetBounds(Math.Max(0,banner.Width-200),30,178,54);};
            var columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,14,0,0)};columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));var validation=Card("VALIDATION / 验证交付流程");validation.Dock=DockStyle.Fill;var flow=new ValidationFlowControl(_p){Dock=DockStyle.Fill};validation.Controls.Add(flow);flow.BringToFront();
            var contextHost=new Panel{Dock=DockStyle.Fill,AutoScroll=true};var context=new TableLayoutPanel{Dock=DockStyle.Top,Height=560,ColumnCount=1,RowCount=3};contextHost.Controls.Add(context);contextHost.Resize+=delegate{context.Height=Math.Max(560,contextHost.ClientSize.Height);};context.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));context.RowStyles.Add(new RowStyle(SizeType.Percent,60));context.RowStyles.Add(new RowStyle(SizeType.Percent,40));context.RowStyles.Add(new RowStyle(SizeType.Absolute,150));
            var docs=Card("FILES / 项目目录文件");docs.Dock=DockStyle.Fill;_projectFiles=List(new[]{"文件名称","格式","大小","更新时间"},new[]{330,90,90,160});_projectFiles.RowActivated+=delegate{if(_projectFiles.SelectedRow!=null)OpenPath((string)_projectFiles.SelectedRow.Tag);};docs.Controls.Add(_projectFiles);_projectFiles.BringToFront();
            var tasksCard=Card("TASKS / 项目任务");tasksCard.Dock=DockStyle.Fill;var tasks=List(new[]{"任务","截止","状态"},new[]{340,140,90});tasks.RowActivated+=delegate{Navigate("tasks");};tasksCard.Controls.Add(tasks);tasks.BringToFront();
            var notesCard=Card("NOTES / 工程备注");notesCard.Dock=DockStyle.Fill;var notes=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=_p.CardBg,ForeColor=_p.TextDim,Font=FontOf(10),Dock=DockStyle.Fill};notesCard.Controls.Add(notes);notes.BringToFront();
            context.Controls.Add(docs,0,0);context.Controls.Add(tasksCard,0,1);context.Controls.Add(notesCard,0,2);columns.Controls.Add(validation,0,0);columns.Controls.Add(contextHost,1,0);body.Controls.Add(columns);body.Controls.Add(banner);columns.BringToFront();
            _reload["project-detail"]=delegate{var p=CurrentProject();flow.SetProject(p);name.Text=p==null?"尚未选择项目":p.Name;info.Text=p==null?"":p.Number+"  ·  "+p.Kind+"  ·  "+(p.Owner.Length==0?"负责人未设置":p.Owner)+"  ·  "+(p.Deadline.Length==0?"交付日期未设置":"交付 "+p.Deadline);path.Text=p==null?"":p.Root;percent.Text=ValidationProgress.Display(p);notes.Text=p==null?"":string.IsNullOrWhiteSpace(p.Notes)?"尚无项目备注。可通过“编辑进度与备注”添加交付信息。":p.Notes;tasks.SetRows(_store.Tasks.Where(t=>p!=null&&(t.Project==p.Id||t.Project==p.Name)).Select(t=>new ListRow{Cells=new[]{t.Title,t.Due,t.Done?"已完成":"待办"},Tag=t}).ToList());RefreshProjectFiles();};AddMain(page,body,toolbar);return page;
        }
    }
}
