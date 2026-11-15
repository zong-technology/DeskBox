// 自动化页面验收：v2 模板、复制、启停、后台预演。
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Automation;
using DeskBox.Core;
using DeskBox.UI;
using DeskBox.UI.Views;

public class AutomationUiAccept
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    [STAThread]
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-auto-ui-" + DateTime.Now.Ticks);
        var work = Path.Combine(dir, "work");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "input.txt"), "真实文件内容，用于自动化预演。");
        Store.DataDir = dir; AppPaths.DataDir = dir;
        var store = new Store(); store.Init();
        var engine = new WorkflowEngine(store);
        var view = new AutomationView(store, engine);
        var host = new Form { Width = 1400, Height = 900 }; host.Controls.Add(view); host.Show(); Application.DoEvents();
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        var name = (DarkInput)typeof(AutomationView).GetField("_name", f).GetValue(view);
        var root = (DarkInput)typeof(AutomationView).GetField("_root", f).GetValue(view);
        var pattern = (DarkInput)typeof(AutomationView).GetField("_pattern", f).GetValue(view);
        var target = (DarkInput)typeof(AutomationView).GetField("_target", f).GetValue(view);
        var action = (DarkSelect)typeof(AutomationView).GetField("_action", f).GetValue(view);
        Check("自动化页提供 8 类模板/动作", action.Inner.Items.Count == 8, "items=" + action.Inner.Items.Count);
        name.Text = "验收总结"; root.Text = work; pattern.Text = "*.txt"; target.Text = Path.Combine(work, "summary.md"); action.SelectedIndex = 5;
        typeof(AutomationView).GetMethod("Create", f).Invoke(view, null);
        Check("页面创建 v2 工作流", engine.Workflows.Count == 1 && engine.Workflows[0].UseNodes, "count=" + engine.Workflows.Count);
        var wf = engine.Workflows[0];
        Check("模板含完整节点链", wf.Nodes.Count == 6, string.Join("→", wf.Nodes.ConvertAll(n => n.Kind).ToArray()));
        var graph = (WorkflowGraphControl)typeof(AutomationView).GetField("_graph", f).GetValue(view);
        Check("流程图来自真实工作流节点", graph.NodeCount == wf.Nodes.Count, "nodes=" + graph.NodeCount);
        Check("AI 节点使用 summarize", wf.Nodes.Exists(n => n.Type == "ai" && n.Kind == "summarize"), "ai");
        Check("保存节点目标路径", wf.Nodes.Exists(n => n.Kind == "write" && n.Get("path", "") == target.Text), target.Text);

        var list = (DarkList)typeof(AutomationView).GetField("_wfList", f).GetValue(view);
        list.SelectIndex(0);
        typeof(AutomationView).GetMethod("CopySelected", f).Invoke(view, null);
        Check("页面可复制工作流", engine.Workflows.Count == 2 && engine.Workflows[0].Nodes.Count == 6, "count=" + engine.Workflows.Count);
        view.Reload(); list.SelectIndex(0);
        bool before = engine.Workflows[0].Enabled;
        typeof(AutomationView).GetMethod("ToggleSelected", f).Invoke(view, null);
        Check("页面可启用/停用", engine.Workflows[0].Enabled != before, "enabled=" + engine.Workflows[0].Enabled);
        engine.Workflows[0].Enabled = true; engine.Save(); view.Reload(); list.SelectIndex(0);

        typeof(AutomationView).GetMethod("Run", f).Invoke(view, new object[] { true });
        var until = DateTime.UtcNow.AddSeconds(10);
        while (engine.Runs.Count == 0 && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(50); }
        Check("页面后台预演完成", engine.Runs.Count > 0 && engine.Runs[0].DryRun, engine.Runs.Count == 0 ? "no run" : engine.Runs[0].Message);
        Check("预演不写结果文件", !File.Exists(target.Text), "no side effect");
        Check("节点运行记录完整", engine.Runs.Count > 0 && engine.Runs[0].NodeRecords.Count == 6, "records=" + (engine.Runs.Count == 0 ? 0 : engine.Runs[0].NodeRecords.Count));
        view.Reload(); Application.DoEvents();
        Check("运行后流程图仍对应所选节点", graph.NodeCount == 6, "nodes=" + graph.NodeCount);
        var shots = Path.Combine(Environment.CurrentDirectory, "shots", "workflow-review"); Directory.CreateDirectory(shots);
        using (var image = new System.Drawing.Bitmap(host.Width, host.Height)) { host.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height)); image.Save(Path.Combine(shots, "pipeline-dry-run.png")); }
        view.Dispose(); host.Dispose(); engine.StopScheduler();
        Console.WriteLine("\n==== 自动化页面验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
