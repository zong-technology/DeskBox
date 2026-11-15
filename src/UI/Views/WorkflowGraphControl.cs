using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Automation;

namespace DeskBox.UI.Views
{
    public class WorkflowGraphControl : UserControl
    {
        private readonly GraphCanvas _canvas;
        private bool _cardSurface;
        public void UseCardSurface(){_cardSurface=true;BackColor=_canvas.BackColor=_canvas.CurrentPalette.CardBg;}
        public void ApplyPalette(Palette palette){_canvas.ApplyPalette(palette);BackColor=_canvas.BackColor=_cardSurface?palette.CardBg:palette.Bg;Invalidate(true);}
        public event Action<WorkflowNode, NodeRecord> InspectRequested;
        public int NodeCount { get { return _canvas.Nodes.Count; } }
        public WorkflowGraphControl(Palette palette)
        {
            AutoScroll = true; BackColor = palette.Bg;
            _canvas = new GraphCanvas(palette) { Height = 164, Width = 700 };
            _canvas.InspectRequested += delegate(WorkflowNode node, NodeRecord record) { if (InspectRequested != null) InspectRequested(node, record); };
            Controls.Add(_canvas);
        }
        public void SetWorkflow(Workflow workflow, WorkflowRun run)
        {
            _canvas.Run = run; _canvas.NameText = workflow == null ? "选择工作流以查看节点链路" : workflow.Name;
            _canvas.Nodes = workflow == null ? new List<WorkflowNode>() : workflow.Nodes != null && workflow.Nodes.Count > 0 ? workflow.Nodes.ToList() :
                new[] { new WorkflowNode { Id = "legacy-trigger", Kind = workflow.Trigger, Type = "trigger" } }.Concat(workflow.Conditions.Select((x, i) => new WorkflowNode { Id = "condition-" + i, Kind = x.Type, Type = "condition", Params = x.Params })).Concat(workflow.Actions.Select((x, i) => new WorkflowNode { Id = "action-" + i, Kind = x.Type, Params = x.Params })).ToList();
            _canvas.Width = Math.Max(700, _canvas.Nodes.Count * 214 + 28); _canvas.Invalidate();
        }
        public void SelectFirstNode()
        {
            var node=_canvas.Nodes.FirstOrDefault(n=>n.Params!=null&&n.Params.Count>0)??_canvas.Nodes.FirstOrDefault();
            if(node!=null&&InspectRequested!=null)InspectRequested(node,_canvas.Run==null?null:_canvas.Run.NodeRecords.LastOrDefault(r=>r.NodeId==node.Id));
        }

        private class GraphCanvas : Control
        {
            private Palette _palette;
            public Palette CurrentPalette{get{return _palette;}}
            public void ApplyPalette(Palette palette){_palette=palette;BackColor=palette.Bg;Invalidate();}
            public List<WorkflowNode> Nodes = new List<WorkflowNode>();
            public WorkflowRun Run;
            public string NameText = "选择工作流以查看节点链路";
            public event Action<WorkflowNode, NodeRecord> InspectRequested;
            public GraphCanvas(Palette palette) { _palette = palette; BackColor = palette.Bg; DoubleBuffered = true; Cursor = Cursors.Hand; }
            private NodeRecord Record(WorkflowNode node) { return Run == null || Run.NodeRecords == null ? null : Run.NodeRecords.LastOrDefault(x => x.NodeId == node.Id); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var title = new Font("Microsoft YaHei UI", 10, FontStyle.Bold))
                using (var font = new Font("Microsoft YaHei UI", 9))
                {
                    TextRenderer.DrawText(g, "WORKFLOW PIPELINE · " + NameText + (Run == null ? " · 尚无运行记录" : Run.DryRun ? " · 最近预演" : " · 最近执行"), title, new Point(14, 8), _palette.Text);
                    for (int i = 0; i < Nodes.Count; i++)
                    {
                        var node = Nodes[i]; var record = Record(node); var rect = new Rectangle(14 + i * 214, 40, 194, 105);
                        Color color = !node.Enabled || record != null && record.Skipped ? _palette.TextFaint : record == null || Run.DryRun && record.Ok ? _palette.Accent : record.Ok ? Color.FromArgb(0, 220, 175) : Color.FromArgb(255, 95, 115);
                        if (i > 0) using (var pen = new Pen(_palette.Line, 2)) using (var cap = new AdjustableArrowCap(4, 4)) { pen.CustomEndCap = cap; g.DrawLine(pen, rect.X - 20, 92, rect.X - 3, 92); }
                        Palette.FillRounded(g, _palette.CardBg, rect, 10); Palette.DrawRounded(g, color, rect, 10, 1);
                        TextRenderer.DrawText(g, (i + 1).ToString("00") + "  " + node.Type.ToUpperInvariant(), font, new Point(rect.X + 12, rect.Y + 10), color);
                        TextRenderer.DrawText(g, node.Label, title, new Rectangle(rect.X + 12, rect.Y + 34, rect.Width - 24, 34), _palette.Text, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                        string status = !node.Enabled ? "已停用" : record == null ? "未执行 / 无节点记录" : record.Skipped ? "已跳过" : (record.Ok ? Run.DryRun ? "预演通过（未执行）" : "执行成功" : "失败") + " · " + record.ElapsedMs + " ms";
                        TextRenderer.DrawText(g, status, font, new Point(rect.X + 12, rect.Bottom - 26), color);
                    }
                }
            }
            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left || e.X < 14 || e.Y < 40 || e.Y > 145) return;
                int index = (e.X - 14) / 214;
                if (index < Nodes.Count && (e.X - 14) % 214 < 194 && InspectRequested != null) InspectRequested(Nodes[index], Record(Nodes[index]));
            }
        }
    }
}
