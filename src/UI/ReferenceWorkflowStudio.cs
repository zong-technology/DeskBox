using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Automation;
using DeskBox.Core;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildWorkflowStudio()
        {
            var page=Page("自动化工作流","选择流程 → 检查节点与参数 → 预演验证 → 执行并查看结果");var toolbar=Toolbar();var query=Input("搜索工作流",270);toolbar.Controls.Add(query);toolbar.Controls.Add(Button("新建 / 编排流程",()=>{ShowLegacy("automation");_reload["automation"]();},false,160));
            var columns=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,30));
            var catalog=Card("WORKFLOWS / 流程目录");catalog.Dock=DockStyle.Fill;var list=List(new[]{"工作流","状态"},new[]{280,80});catalog.Controls.Add(list);list.BringToFront();
            var canvas=Card("PIPELINE / 节点与执行结果");canvas.Dock=DockStyle.Fill;var graph=new WorkflowGraphControl(_p){Dock=DockStyle.Top,Height=215};graph.UseCardSurface();var records=List(new[]{"节点","结果","耗时 ms"},new[]{220,90,90});
            var runStatus=TextLabel("选择流程后可预演，点击节点查看实际参数。",9,false,_p.TextDim);runStatus.Dock=DockStyle.Bottom;runStatus.Height=70;
            canvas.Controls.Add(records);canvas.Controls.Add(graph);canvas.Controls.Add(runStatus);records.BringToFront();canvas.Controls.SetChildIndex(graph,1);
            var inspector=Card("INSPECTOR / 节点参数");inspector.Dock=DockStyle.Fill;var inspectorHeader=inspector.Controls[0];var selected=TextLabel("选择执行链路中的节点",11,true,_p.Accent);selected.Dock=DockStyle.Top;selected.Height=64;
            var parameters=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=_p.CardBg,BorderStyle=BorderStyle.None,EnableHeadersVisualStyles=false,GridColor=_p.Line,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,ColumnHeadersHeight=36,Font=FontOf(9.5f),RowTemplate={Height=38}};
            parameters.ColumnHeadersDefaultCellStyle.BackColor=_p.HeaderBg;parameters.ColumnHeadersDefaultCellStyle.ForeColor=_p.TextDim;parameters.DefaultCellStyle.BackColor=_p.CardBg;parameters.DefaultCellStyle.ForeColor=_p.Text;parameters.DefaultCellStyle.SelectionBackColor=_p.Hover;parameters.DefaultCellStyle.SelectionForeColor=_p.Text;parameters.Columns.Add("Key","参数");parameters.Columns.Add("Value","值");parameters.Columns[0].ReadOnly=true;parameters.Columns[0].FillWeight=42;parameters.Columns[1].FillWeight=58;
            var inspection=TextLabel("参数与执行记录来自实际流程。\r\n变更后先预演，确认结果再执行。",9,false,_p.TextDim);inspection.Dock=DockStyle.Bottom;inspection.Height=115;
            var save=Button("保存节点参数",()=>{},false,180);save.Dock=DockStyle.Bottom;save.Height=38;save.Enabled=false;
            inspector.Controls.Add(parameters);inspector.Controls.Add(selected);inspector.Controls.Add(save);inspector.Controls.Add(inspection);parameters.BringToFront();inspector.Controls.SetChildIndex(inspectorHeader,inspector.Controls.Count-1);
            Workflow current=null;WorkflowNode node=null;string baseline="";bool busy=false;
            Action show=delegate{
                current=list.SelectedRow==null?null:list.SelectedRow.Tag as Workflow;node=null;save.Enabled=false;parameters.Rows.Clear();selected.Text=current==null?"选择工作流":"点击节点查看参数";
                var run=current==null?null:_workflows.Runs.FirstOrDefault(r=>r.WorkflowId==current.Id);graph.SetWorkflow(current,run);
                records.SetRows(run==null?new List<ListRow>():run.NodeRecords.Select(r=>new ListRow{Cells=new[]{r.Name,r.Skipped?"跳过":r.Ok?"成功":"失败",r.ElapsedMs.ToString()},Tag=r}).ToList());
                if(run!=null&&run.NodeRecords.Count==0)records.SetRows(run.Steps.Select(step=>new ListRow{Cells=new[]{step,"记录","—"}}).ToList());graph.SelectFirstNode();runStatus.Text=run==null?"尚无执行记录。先预演检查节点输入与输出。":(run.DryRun?"预演":"执行")+" · "+(run.Ok?"成功":"未完成")+"\r\n"+run.Message;
            };
            graph.InspectRequested+=delegate(WorkflowNode selectedNode,NodeRecord record){node=selectedNode;parameters.Rows.Clear();foreach(var entry in node.Params??new Dictionary<string,string>())parameters.Rows.Add(entry.Key,entry.Value);baseline=current==null?"":Json.ToJson(current);selected.Text=node.Label+"\r\n"+node.Type+" · "+(node.Enabled?"启用":"停用");save.Enabled=current!=null&&parameters.Rows.Count>0&&!busy;inspection.Text=record==null?"修改参数后先保存，再进行预演。":record.Describe()+"\r\n输入："+record.InputSummary+"\r\n输出："+record.OutputSummary;};
            save.Click+=delegate{try{if(node==null||current==null)return;if(busy||(_workflows.Runner!=null&&_workflows.Runner.ActiveCount>0))throw new InvalidOperationException("执行期间不能修改参数，请等待运行完成");if(Json.ToJson(current)!=baseline)throw new InvalidOperationException("流程已更新，请重新选择节点后编辑");parameters.EndEdit();var updated=new Dictionary<string,string>();foreach(DataGridViewRow row in parameters.Rows)updated[Convert.ToString(row.Cells[0].Value)]=Convert.ToString(row.Cells[1].Value);if(node.Params==null)node.Params=new Dictionary<string,string>();node.Params.Clear();foreach(var entry in updated)node.Params[entry.Key]=entry.Value;_workflows.Save();baseline=Json.ToJson(current);inspection.Text="参数已保存，请先预演验证。";}catch(Exception ex){inspection.Text=ex.Message;}};
            var dry=Button("预演流程",()=>{},false,115);var execute=Button("运行流程",()=>{},true,115);toolbar.Controls.Add(dry);toolbar.Controls.Add(execute);
            Action<bool> runWorkflow=async preview=>{if(current==null||busy)return;if(!preview&&!Prompt.Confirm(this,"运行工作流","将执行「"+current.Name+"」。请先检查预演结果。",_p))return;busy=true;save.Enabled=false;dry.Enabled=false;execute.Enabled=false;runStatus.Text=preview?"正在预演…":"正在执行…";string id=current.Id;try{var result=await Task.Run(()=>_workflows.RunV2(id,preview));if(IsDisposed)return;show();runStatus.Text=(preview?"预演":"执行")+" · "+(result.Ok?"成功":"未完成")+"\r\n"+result.Message;}catch(Exception ex){if(!IsDisposed)runStatus.Text="运行失败："+ex.Message;}finally{busy=false;if(!IsDisposed){dry.Enabled=true;execute.Enabled=true;save.Enabled=node!=null&&parameters.Rows.Count>0;}}};dry.Click+=delegate{runWorkflow(true);};execute.Click+=delegate{runWorkflow(false);};
            list.SelectionChanged+=delegate{show();};_reload["automation"]=delegate{if(busy)return;string id=current==null?"":current.Id;var rows=_workflows.Workflows.Where(w=>query.Text.Length==0||w.Name.IndexOf(query.Text,StringComparison.OrdinalIgnoreCase)>=0).Select(w=>new ListRow{Cells=new[]{w.Name,w.Enabled?"启用":"停用"},Tag=w}).ToList();list.SetRows(rows);if(rows.Count>0)list.SelectIndex(Math.Max(0,rows.FindIndex(row=>((Workflow)row.Tag).Id==id)));else show();};query.Inner.TextChanged+=delegate{_reload["automation"]();};
            columns.Controls.Add(catalog,0,0);columns.Controls.Add(canvas,1,0);columns.Controls.Add(inspector,2,0);AddMain(page,columns,toolbar);return page;
        }
    }
}
