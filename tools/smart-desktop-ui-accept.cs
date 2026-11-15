// Smart Desktop integration: MainForm navigation, context panel and real Command Center providers.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Fs;
using DeskBox.Shell;
using DeskBox.UI;

public class SmartDesktopUiAccept
{
    static int pass, fail;
    static void C(string name, bool ok, string detail) { if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); } else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); } }

    [STAThread]
    public static int Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "deskbox-smart-ui-" + DateTime.Now.Ticks);
        var desktop = Path.Combine(root, "Desktop"); var folder = Path.Combine(desktop, "PLC");
        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(desktop, "IO List.xlsx"), "xlsx"); File.WriteAllText(Path.Combine(folder, "Program.scl"), "FUNCTION_BLOCK FB1");
        Store.DataDir = Path.Combine(root, "data"); AppPaths.DataDir = Store.DataDir; Directory.CreateDirectory(Store.DataDir);
        var store = new Store(); store.Init(); store.Settings.TakeoverIcons = false; store.Settings.DesktopDir = desktop; store.Tasks.Add(new TaskItem { Id="fat", Title="FAT-021 阻塞问题", Priority="high" }); store.Save();
        var form = new MainForm(store);
        form.Show(); Application.DoEvents();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;

        form.SwitchView("desktop"); Application.DoEvents();
        var session = (FolderSession)typeof(MainForm).GetField("_folderSession", flags).GetValue(form);
        var board = (IconBoard)typeof(MainForm).GetField("_board", flags).GetValue(form);
        var widgets = (Control)typeof(MainForm).GetField("_widgets", flags).GetValue(form);
        var preview = (Control)typeof(MainForm).GetField("_preview", flags).GetValue(form);
        var folderNav = (Control)typeof(MainForm).GetField("_folderNav", flags).GetValue(form);
        var rail = (Panel)typeof(MainForm).GetField("_rail", flags).GetValue(form);
        var items = (List<DesktopItem>)typeof(MainForm).GetField("_items", flags).GetValue(form);
        C("桌面聚合会话已初始化", session.Current == FolderSession.DesktopHome, session.Current);
        C("真实桌面文件已加载", items.Any(x => x.Name == "IO List.xlsx") && items.Any(x => x.Name == "PLC"), "count=" + items.Count);
        C("桌面支持 Explorer 文件拖入", board.AllowDrop, "AllowDrop=" + board.AllowDrop);
        C("details 设置可解析", IconBoard.ParseView("details") == BoardView.Details, IconBoard.ParseView("details").ToString());
        var cycleView = typeof(MainForm).GetMethod("CycleView", flags);
        store.Settings.View = "grid"; board.RefreshTheme();
        cycleView.Invoke(form, null); bool cycleList = board.View == BoardView.List;
        cycleView.Invoke(form, null); bool cycleDetails = board.View == BoardView.Details && store.Settings.View == "details";
        cycleView.Invoke(form, null); bool cycleCompact = board.View == BoardView.Compact;
        cycleView.Invoke(form, null); bool cycleGrid = board.View == BoardView.Grid;
        C("视图循环包含 Details", cycleList && cycleDetails && cycleCompact && cycleGrid, "list/details/compact/grid");
        store.Settings.View = "details"; board.RefreshTheme();
        bool detailsPainted = true;
        try { using (var image = new System.Drawing.Bitmap(Math.Max(1, board.Width), Math.Max(1, board.Height))) board.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, image.Width, image.Height)); } catch { detailsPainted = false; }
        C("Details 视图可真实绘制", detailsPainted && board.View == BoardView.Details, "view=" + board.View);
        C("Desktop 使用文件上下文栏", !widgets.Visible && preview.Width == 330, "widgets=" + widgets.Visible + " preview=" + preview.Width);
        C("文件夹导航栏仅桌面显示", folderNav.Visible, "visible");
        int collapsedWidth = rail.Width;
        C("导航默认折叠", collapsedWidth < 120 && !store.Settings.NavExpanded, "width=" + rail.Width);
        typeof(MainForm).GetMethod("ToggleNavigation", flags).Invoke(form, null); Application.DoEvents();
        C("导航可展开并保存状态", rail.Width > collapsedWidth && store.Settings.NavExpanded, "width=" + rail.Width);

        var plc = items.First(x => x.Name == "PLC");
        typeof(MainForm).GetMethod("OpenItem", flags).Invoke(form, new object[] { plc }); Application.DoEvents();
        items = (List<DesktopItem>)typeof(MainForm).GetField("_items", flags).GetValue(form);
        C("双击文件夹留在 DeskBox 内", session.Current == Path.GetFullPath(folder) && items.Any(x => x.Name == "Program.scl"), session.Current);
        session.Back(); Application.DoEvents();
        C("Back 返回 Desktop Home", session.Current == FolderSession.DesktopHome, session.Current);

        form.SwitchView("home"); Application.DoEvents();
        C("HOME 恢复 Today/Risk/Weather 侧栏", widgets.Visible && preview.Width == 0, "widgets=" + widgets.Visible);
        var statusLabel = (Label)typeof(MainForm).GetField("_lblConn", flags).GetValue(form);
        typeof(MainForm).GetMethod("UpdateConnLabel", flags).Invoke(form, null);
        C("状态栏区分局域网与电脑云账号", statusLabel.Text.Contains("PROJECT") && statusLabel.Text.Contains("LAN") && statusLabel.Text.Contains("CLOUD 未登录电脑") && statusLabel.Text.Contains("AI"), statusLabel.Text);

        var workspaces = (WorkspaceStore)typeof(MainForm).GetField("_workspaces", flags).GetValue(form);
        workspaces.Add("SureHand Project", desktop, "PLC");
        var index = FileIndex.Instance;
        var waitUntil = DateTime.UtcNow.AddSeconds(15); while (index.IsIndexing && DateTime.UtcNow < waitUntil) Thread.Sleep(50);
        index.Configure(new List<string> { desktop }, new List<string>()); index.Rebuild();
        waitUntil = DateTime.UtcNow.AddSeconds(15); while (index.IsIndexing && DateTime.UtcNow < waitUntil) Thread.Sleep(50);
        var dynamicMethod = typeof(MainForm).GetMethod("BuildDynamicCommands", flags);
        Func<string, List<CommandItem>> commands = delegate(string q) { return (List<CommandItem>)dynamicMethod.Invoke(form, new object[] { q }); };
        var s7 = commands("解析 DB10.DBX2.3");
        C("Command Center 本地解析 S7", s7.Any(x => x.Name.Contains("S7 地址") && x.Hint.Contains("字节偏移 2")), "count=" + s7.Count);
        var scale = commands("计算 12mA 0-100");
        C("Command Center 本地计算 4-20mA", scale.Any(x => x.Name.Contains("50")), "count=" + scale.Count);
        var projectCommands = commands("SureHand");
        C("Command Center 搜索真实项目", projectCommands.Any(x => x.Name.Contains("SureHand Project")), "count=" + projectCommands.Count);
        var taskCommands = commands("FAT-021");
        C("Command Center 搜索真实任务", taskCommands.Any(x => x.Name.Contains("FAT-021")), "count=" + taskCommands.Count);
        var fileCommands = commands("IO List");
        C("Command Center 搜索真实索引文件", fileCommands.Any(x => x.Name.Contains("IO List.xlsx")), "count=" + fileCommands.Count);
        C("Command Center 保留 AI 入口", fileCommands.Any(x => x.Name == "询问 AI&SI"), "ok");

        typeof(MainForm).GetMethod("OnFormClosing", flags).Invoke(form, new object[] { new FormClosingEventArgs(CloseReason.ApplicationExitCall, false) });
        form.Dispose();
        Console.WriteLine("\n==== Smart Desktop UI 验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
