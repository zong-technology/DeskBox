using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.UI;
using DeskBox.UI.Views;

public class ValidationProgressAccept
{
    static int pass, fail;
    static void Check(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    [STAThread] public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-validation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        Store.DataDir = dir; AppPaths.DataDir = dir;
        var app = new Store(); app.Init(); app.Settings.TakeoverIcons = false;
        var ws = new WorkspaceStore(); var a = ws.Add("项目 A", dir, "CSV/验证");
        var dirB = Path.Combine(dir, "B"); Directory.CreateDirectory(dirB); var b = ws.Add("项目 B", dirB, "PLC");
        var steps = ValidationProgress.Create();
        Check("27个图示节点", steps.Count == 27 && steps[9].Name == "DQ 通过？" && steps[25].Name.Contains("VSR"));
        foreach (var step in steps) step.Percent = null;
        steps[0].Percent = 100; steps[1].Percent = 50; steps[1].Notes = "等待 QA 签批";
        steps[1].Start = "2026-11-01"; steps[1].End = "2026-11-05";
        ws.SaveValidation(a.Id, steps, "2026-11-01", "2026-12-01", "测试备注", dir);
        Check("N/A不计入平均值", ValidationProgress.Overall(a) == 75);
        Check("节点日期备注更新时间保存", a.ValidationSteps[1].Notes == "等待 QA 签批" && a.ValidationSteps[1].UpdatedAt > 0 && a.ValidationSteps[1].End == "2026-11-05");
        Check("项目日期备注与目录保存", a.Start == "2026-11-01" && a.Deadline == "2026-12-01" && a.Notes == "测试备注" && a.Root == dir && a.UpdatedAt > 0);
        Check("另一项目不受影响", b.ValidationSteps == null && b.Notes == "");
        var reload = new WorkspaceStore(); Check("重新加载保留N/A与百分比", reload.Items.First(x => x.Id == a.Id).ValidationSteps[2].Percent == null && ValidationProgress.Overall(reload.Items.First(x => x.Id == a.Id)) == 75);
        var before = a.UpdatedAt; steps[0].Percent = 101;
        bool rejected = false; try { ws.SaveValidation(a.Id, steps, "", "", "invalid", dir); } catch (ArgumentException) { rejected = true; }
        Check("非法百分比拒绝且不破坏原数据", rejected && a.ValidationSteps[0].Percent == 100 && a.UpdatedAt == before);
        steps[0].Percent = double.NaN; rejected = false; try { ws.SaveValidation(a.Id, steps, "", "", "", dir); } catch (ArgumentException) { rejected = true; } Check("NaN进度被拒绝", rejected);
        steps[0].Percent = null; rejected = false; try { ws.SaveValidation(a.Id, steps, "2026-12-01", "2026-11-01", "", dir); } catch (ArgumentException) { rejected = true; } Check("结束早于开始被拒绝", rejected);
        foreach (var step in steps) step.Percent = null;
        ws.SaveValidation(a.Id, steps, "", "", "", dir); Check("全N/A显示N/A", ValidationProgress.Overall(a) == null && ValidationProgress.Display(a) == "N/A");
        ws.SetCurrent(a.Id); var health = new ProjectControlService(app, ws); Check("项目A快照", health.GetSnapshot(false).ProjectId == a.Id);
        ws.SetCurrent(b.Id); Check("切换项目缓存立即更新", health.GetSnapshot(false).ProjectId == b.Id);
        using (var form = new ProjectProgressForm(ws, a, Palette.For("cyber", "#22D3EE")))
        {
            form.Show(); Application.DoEvents();
            Check("编辑窗能够真实显示", form.Visible && form.Controls.Count >= 3);
            var grid = form.Controls.OfType<SplitContainer>().First().Panel2.Controls.OfType<DataGridView>().First();
            Check("27节点可编辑且更新时间只读", grid.Rows.Count == 27 && !grid.Columns[1].ReadOnly && grid.Columns[5].ReadOnly);
            var output = Path.Combine(Environment.CurrentDirectory, "shots", "validation-review"); Directory.CreateDirectory(output);
            using (var bmp = new System.Drawing.Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height)); bmp.Save(Path.Combine(output, "progress-editor.png")); }
            grid.Rows[1].Cells[1].Value = "65%";
            form.Controls.OfType<FlowLayoutPanel>().First().Controls.OfType<Button>().First(x => x.Text == "保存进度").PerformClick();
            Check("真实编辑窗口保存百分号输入", a.ValidationSteps[1].Percent == 65);
        }
        using (var host = new Form { Width = 1400, Height = 900 })
        using (var home = new EngineerHomeView(app, ws, null, null, null))
        {
            host.Controls.Add(home); host.Show(); Application.DoEvents();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var picker = (DarkSelect)typeof(EngineerHomeView).GetField("_projectPicker", flags).GetValue(home);
            var choices = ws.Items.ToArray(); picker.SelectedIndex = Array.FindIndex(choices, x => x.Id == a.Id);
            Check("主页项目选择切换当前项目", ws.Current.Id == a.Id);
            var hero = (Label)typeof(EngineerHomeView).GetField("_heroProject", flags).GetValue(home);
            Check("主页显示所选项目实时进度", hero.Text.Contains(a.Name) && hero.Text.Contains("65%"));
            var flow = (ValidationFlowControl)typeof(EngineerHomeView).GetField("_validationFlow", flags).GetValue(home);
            Check("主页显示验证流程图", flow.Visible);
            var output = Path.Combine(Environment.CurrentDirectory, "shots", "validation-review");
            using (var bmp = new System.Drawing.Bitmap(host.Width, host.Height)) { host.DrawToBitmap(bmp, new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height)); bmp.Save(Path.Combine(output, "home-project-progress.png")); }
        }
        var searchA = Path.Combine(dir, "Scope"); var searchB = Path.Combine(dir, "Scope-other"); Directory.CreateDirectory(searchA); Directory.CreateDirectory(searchB);
        File.WriteAllText(Path.Combine(searchA, "validation-evidence.txt"), "A"); File.WriteAllText(Path.Combine(searchB, "validation-evidence.txt"), "B");
        var index = DeskBox.Fs.FileIndex.Instance; index.Configure(new[] { searchA, searchB }.ToList(), new System.Collections.Generic.List<string>()); index.Rebuild();
        var until = DateTime.UtcNow.AddSeconds(15); while (index.IsIndexing && DateTime.UtcNow < until) System.Threading.Thread.Sleep(20);
        using (var search = new SearchView(app, index, null, null))
        {
            SearchOutcome outcome = null; search.Searched += x => outcome = x;
            search.SetProjectScope(searchA, "Scope project");
            var query = new SearchQuery { Text = "validation-evidence", Roots = new System.Collections.Generic.List<string> { searchB } };
            search.DoSearch(query);
            Check("项目搜索排除相似前缀兄弟目录", outcome != null && outcome.Hits.Count == 1 && outcome.Hits[0].Entry.Path == Path.Combine(searchA, "validation-evidence.txt"));
            search.DoSearch();
            Check("再次搜索仍保留项目范围", outcome != null && outcome.Hits.All(x => x.Entry.Path.StartsWith(searchA + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
            search.SetProjectScope(null, null); search.DoSearch(new SearchQuery { Text = "validation-evidence" });
            Check("可返回全局搜索", outcome != null && outcome.Hits.Count == 2);
        }
        Console.WriteLine("Validation progress: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
