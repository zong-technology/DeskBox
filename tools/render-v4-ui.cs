// 无需启动第二个 DeskBox 进程：直接用测试宿主渲染 v4 页面截图。
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.UI;

public class RenderV4Ui
{
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("shcore.dll")] private static extern int GetScaleFactorForMonitor(IntPtr monitor, out int percent);

    [STAThread]
    public static int Main(string[] args) { return Run(args, null); }

    public static int Run(string[] args, Action<MainForm, Action<string>> checkNavigation)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "shots");
        Directory.CreateDirectory(output);
        var data = Path.Combine(output, "deskbox-render-v4-" + DateTime.Now.Ticks); Directory.CreateDirectory(data);
        Store.DataDir = data; AppPaths.DataDir = data;
        var store = new Store(); store.Init(); store.Settings.TakeoverIcons = false; store.Settings.Theme = "cyber"; store.Settings.Accent = "#22D3EE";
        var projectRoot = Path.Combine(data, "SureHand Project"); Directory.CreateDirectory(projectRoot); File.WriteAllText(Path.Combine(projectRoot, "IO-List.csv"), "Tag,Description\r\nPT-101,Pressure\r\n");
        store.Settings.DesktopDir=projectRoot;
        var index=DeskBox.Fs.FileIndex.Instance;index.Configure(new System.Collections.Generic.List<string>{projectRoot},new System.Collections.Generic.List<string>());index.Rebuild();
        while(index.IsIndexing){Application.DoEvents();System.Threading.Thread.Sleep(20);}
        var workspaces = new WorkspaceStore(); var project = workspaces.Add("SureHand Project", projectRoot, "PLC"); workspaces.SetCurrent(project.Id);
        workspaces.UpdateProjectDetails(project.Id, "SH-2026-01", "生物反应器", "Arthur", DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd"), DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"), "medium");
        workspaces.SetStageProgress(project.Id, "Design", 90); workspaces.SetStageProgress(project.Id, "PLC", 86); workspaces.SetStageProgress(project.Id, "DCS", 61); workspaces.SetStageProgress(project.Id, "HMI", 72); workspaces.SetStageProgress(project.Id, "FAT", 72); workspaces.SetStageProgress(project.Id, "Validation", 54);
        store.Tasks.Add(new TaskItem { Id="1", Title="配液系统 OQ：报警与联锁测试", Project=project.Id, Priority="high", Due=DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") });
        store.Tasks.Add(new TaskItem { Id="2", Title="灌装线 PLC 程序差异审查", Project=project.Id, Priority="normal" }); store.Save();
        store.Log("info", "project", "完成 IO 检查"); store.Log("info", "ai.chat", "AI 分析 PLC 程序差异");
        store.Settings.ReferenceUi=args.Contains("--reference");
        var form = new MainForm(store); form.Bounds = new Rectangle(0, 0, 1600, 900); form.Show(); Application.DoEvents();
        if (args.Length > 2) { form.Bounds = new Rectangle(0, 0, int.Parse(args[1]), int.Parse(args[2])); Application.DoEvents(); }
        int scale;
        var scaleResult = GetScaleFactorForMonitor(MonitorFromWindow(form.Handle, 2), out scale);
        Console.WriteLine("window " + form.Size + " DPI " + form.DeviceDpi + " MONITOR SCALE " + (scaleResult == 0 ? scale.ToString() : "unknown"));
        if (checkNavigation != null)
        {
            try { checkNavigation(form, route => ShotReference(form, output, route)); }
            finally { form.Close(); form.Dispose(); }
            return 0;
        }
        if(store.Settings.ReferenceUi)
        {
            foreach(var route in ReferenceWorkspace.ReferencePages)
            {
                form.SwitchView(route);
                ShotReference(form, output, route);
            }
            form.Close();form.Dispose();Console.WriteLine("reference pages rendered to "+output);return 0;
        }
        SetHomeLayout(form, "mission");
        DumpSizes(form, "home");
        Shot(form, Path.Combine(output, "v4-engineering-home.png"));
        Shot(form, Path.Combine(output, "v30-home-mission.png"));
        SetHomeLayout(form, "focus"); Shot(form, Path.Combine(output, "v30-home-focus.png"));
        SetHomeLayout(form, "system"); Shot(form, Path.Combine(output, "v30-home-system.png"));
        form.SwitchView("desktop");
        var thumbUntil = DateTime.UtcNow.AddSeconds(2.5);
        while (DateTime.UtcNow < thumbUntil) { Application.DoEvents(); System.Threading.Thread.Sleep(40); }
        Shot(form, Path.Combine(output, "v21-smart-desktop.png"));
        var workspaceMode = typeof(MainForm).GetMethod("SetDesktopWorkspace", BindingFlags.NonPublic | BindingFlags.Instance);
        workspaceMode.Invoke(form, new object[] { true }); Application.DoEvents(); Shot(form, Path.Combine(output, "engineering-desktop.png"));
        workspaceMode.Invoke(form, new object[] { false });
        form.SwitchView("engineering"); Application.DoEvents(); DumpSizes(form, "engineering"); Shot(form, Path.Combine(output, "v4-engineering-tools.png"));
        form.SwitchView("ai"); Application.DoEvents();
        var viewsField = typeof(MainForm).GetField("_views", BindingFlags.NonPublic | BindingFlags.Instance);
        var views = (System.Collections.Generic.Dictionary<string, UserControl>)viewsField.GetValue(form);
        var chat = views["ai"] as DeskBox.UI.Views.ChatView;
        if (chat != null) chat.SetPrompt("请检查 DB10.DBX2.3 对应的设备联锁逻辑，并生成 OQ 测试步骤。\r\n第二行中文输入应完整可见。");
        Application.DoEvents(); Shot(form, Path.Combine(output, "v4-aisi.png"));
        form.SwitchView("projects"); Application.DoEvents(); Shot(form, Path.Combine(output, "v4-projects.png"));
        form.SwitchView("mobile"); Application.DoEvents(); Shot(form, Path.Combine(output, "mobile-pairing-guide.png"));
        if (args.Length > 3)
            foreach (var route in new[] { "tasks", "search", "library", "monitor", "disk", "clipboard", "history", "notifications", "filehistory", "weather", "automation", "settings" })
            {
                form.SwitchView(route); Application.DoEvents(); Shot(form, Path.Combine(output, "module-" + route + ".png"));
            }
        form.Close(); form.Dispose();
        Console.WriteLine("rendered to " + output);
        return 0;
    }

    private static void ShotReference(Control control, string output, string route)
    {
        var until = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
        Shot(control, Path.Combine(output, route + ".png"));
    }

    private static void Shot(Control control, string path)
    {
        using (var bmp = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb))
        {
            control.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine(path);
    }

    public static void CaptureNative(Control control, string path)
    {
        using (var bitmap = new Bitmap(control.ClientSize.Width, control.ClientSize.Height))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(control.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
            bitmap.Save(path, ImageFormat.Png);
        }
        Console.WriteLine(path);
    }

    private static void DumpSizes(MainForm form, string key)
    {
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        var views = (System.Collections.Generic.Dictionary<string, UserControl>)typeof(MainForm).GetField("_views", f).GetValue(form);
        UserControl view; if (!views.TryGetValue(key, out view)) return;
        var body = (Panel)typeof(DeskBox.UI.Views.ViewBase).GetField("Body", f).GetValue(view);
        Console.WriteLine(key + " view=" + view.ClientSize + " body=" + body.ClientSize);
        if (key == "home")
        {
            var hero = (Panel)view.GetType().GetField("_hero", f).GetValue(view);
            Console.WriteLine("hero=" + hero.Bounds);
        }
        if (key == "engineering")
        {
            var page = (Panel)view.GetType().GetField("_page", f).GetValue(view);
            Console.WriteLine("page=" + page.ClientSize + " cards=" + string.Join(" | ", page.Controls.Cast<Control>().Select(x => x.Bounds.ToString()).ToArray()));
        }
    }

    private static void SetHomeLayout(MainForm form, string mode)
    {
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        var views = (System.Collections.Generic.Dictionary<string, UserControl>)typeof(MainForm).GetField("_views", f).GetValue(form);
        var home = views["home"];
        home.GetType().GetMethod("SetLayout", f).Invoke(home, new object[] { mode });
        if (!string.Equals((string)home.GetType().GetField("_layoutMode", f).GetValue(home), mode, StringComparison.Ordinal))
            throw new InvalidOperationException("Layout did not activate: " + mode);
        Console.WriteLine("LAYOUT " + mode);
        Application.DoEvents();
    }
}
