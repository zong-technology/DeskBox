// 工程驾驶舱、导航、工具箱与 AI 模式 UI 验收。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.UI;
using DeskBox.UI.Views;

public class EngineeringUiAccept
{
    static int pass, fail;
    static void C(string n, bool ok, string d) { if (ok) { pass++; Console.WriteLine("PASS  " + n + "  [" + d + "]"); } else { fail++; Console.WriteLine("FAIL  " + n + "  [" + d + "]"); } }
    static bool OwnVisible(Control c) { return (bool)typeof(Control).GetMethod("GetState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { 2 }); }
    static void Click(Control c) { typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { EventArgs.Empty }); }

    [STAThread]
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-eng-ui-" + DateTime.Now.Ticks); Directory.CreateDirectory(dir);
        Store.DataDir = dir; AppPaths.DataDir = dir;
        var store = new Store(); store.Init(); store.Settings.TakeoverIcons = false; store.Settings.Theme = "cyber"; store.SaveSettingsOnly();
        var p = Palette.For("cyber", "#22D3EE");
        C("Cyber 主题为深色工程色板", !p.IsLight && p.Bg.R < 10 && p.Accent.B > 150, p.Bg + "/" + p.Accent);

        var form = new MainForm(store);
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        string current = (string)typeof(MainForm).GetField("_currentView", f).GetValue(form);
        C("默认进入工程驾驶舱", current == "home", current);
        var aiButton = (Button)typeof(MainForm).GetField("_btnAi", f).GetValue(form);
        var mobileButton = (Button)typeof(MainForm).GetField("_btnMobile", f).GetValue(form);
        var notificationButton = (Button)typeof(MainForm).GetField("_btnNotifications", f).GetValue(form);
        var topContext = (Label)typeof(MainForm).GetField("_topContext", f).GetValue(form);
        C("顶栏提供真实 AI/手机/通知入口", aiButton != null && mobileButton != null && notificationButton != null, "ok");
        C("顶栏显示当前模块与项目上下文", OwnVisible(topContext) && topContext.Text.Contains("驾驶舱") && topContext.Text.Contains("未选择工程项目"), topContext.Text);
        Click(mobileButton);
        current = (string)typeof(MainForm).GetField("_currentView", f).GetValue(form);
        C("顶栏手机入口可导航", current == "mobile", current);
        form.SwitchView("home");
        var widgets = (WidgetsPanel)typeof(MainForm).GetField("_widgets", f).GetValue(form);
        var contextCard = (Control)typeof(WidgetsPanel).GetField("_context", f).GetValue(widgets);
        var clockCard = (Control)typeof(WidgetsPanel).GetField("_clock", f).GetValue(widgets);
        C("首页右栏切换为工程上下文", OwnVisible(widgets) && OwnVisible(contextCard) && !OwnVisible(clockCard), "context=" + OwnVisible(contextCard));
        form.SwitchView("ai");
        var contextTitle = (string)contextCard.GetType().GetField("_title", f).GetValue(contextCard);
        C("AI 页面获得独立执行上下文", OwnVisible(widgets) && contextTitle == "AI EXECUTION CONTEXT", contextTitle);
        form.SwitchView("desktop");
        var preview = (Control)typeof(MainForm).GetField("_preview", f).GetValue(form);
        C("智能桌面保留文件预览上下文", !OwnVisible(widgets) && preview.Width == 330, "preview=" + preview.Width);
        form.SwitchView("home");
        var railItems = (IEnumerable)typeof(MainForm).GetField("_railItems", f).GetValue(form);
        var ids = new List<string>();
        foreach (var item in railItems) ids.Add((string)item.GetType().GetField("Id").GetValue(item));
        C("导航包含驾驶舱/桌面/项目/工具/手机", new[] { "home", "desktop", "projects", "engineering", "mobile" }.All(ids.Contains), string.Join(",", ids.ToArray()));
        var rail = (Panel)typeof(MainForm).GetField("_rail", f).GetValue(form);
        C("长导航支持滚动", rail.AutoScroll, "AutoScroll=" + rail.AutoScroll);

        var views = (Dictionary<string, UserControl>)typeof(MainForm).GetField("_views", f).GetValue(form);
        C("驾驶舱视图已创建", views.ContainsKey("home") && views["home"] is EngineerHomeView, "views=" + views.Count);
        var home = (EngineerHomeView)views["home"];
        var bodyField = typeof(ViewBase).GetField("Body", BindingFlags.NonPublic | BindingFlags.Instance);
        var body = (Panel)bodyField.GetValue(home);
        C("驾驶舱包含高科技卡片布局", body.Controls.Count >= 7, "cards=" + body.Controls.Count);
        C("驾驶舱无控件负尺寸", body.Controls.Cast<Control>().All(x => x.Width > 0 && x.Height > 0), "ok");
        var modeButtons = (DarkButton[])typeof(EngineerHomeView).GetField("_layoutButtons", f).GetValue(home);
        var setLayout = typeof(EngineerHomeView).GetMethod("SetLayout", f);
        C("首页提供三种真实布局模式", modeButtons.Length == 3 && modeButtons.All(x => x != null), "mission/focus/system");
        setLayout.Invoke(home, new object[] { "focus" }); Application.DoEvents();
        var systemCard = (Panel)typeof(EngineerHomeView).GetField("_systemCard", f).GetValue(home);
        var taskCard = (Panel)typeof(EngineerHomeView).GetField("_taskCard", f).GetValue(home);
        var focusCard = (Panel)typeof(EngineerHomeView).GetField("_focusCard", f).GetValue(home);
        var modules = (Panel)typeof(EngineerHomeView).GetField("_moduleCard", f).GetValue(home);
        C("FOCUS 突出当前任务并展示功能入口", store.Settings.HomeLayout == "focus" && !OwnVisible(systemCard) && OwnVisible(focusCard) && OwnVisible(modules), focusCard.Bounds.ToString());
        setLayout.Invoke(home, new object[] { "system" }); Application.DoEvents();
        var projectCard = (Panel)typeof(EngineerHomeView).GetField("_projectCard", f).GetValue(home);
        var dependencyCard = (Panel)typeof(EngineerHomeView).GetField("_dependencyCard", f).GetValue(home);
        C("SYSTEM 将工程依赖置为主视图", store.Settings.HomeLayout == "system" && OwnVisible(dependencyCard) && dependencyCard.Width >= body.ClientSize.Width - 30 && dependencyCard.Top < projectCard.Top, dependencyCard.Bounds.ToString());
        setLayout.Invoke(home, new object[] { "mission" }); Application.DoEvents();
        C("MISSION 项目进度与主要功能并排展示", store.Settings.HomeLayout == "mission" && !OwnVisible(systemCard) && modules.Left > projectCard.Right && projectCard.Top == modules.Top, projectCard.Bounds.ToString());
        var tiles = ((IEnumerable)typeof(EngineerHomeView).GetField("_featureTiles", f).GetValue(home)).Cast<Control>().ToArray();
        C("九个主要功能均有详细说明", tiles.Length == 9 && tiles.All(x => !string.IsNullOrEmpty(x.AccessibleDescription)), "count=" + tiles.Length);
        foreach (var modeName in new[] { "mission", "focus", "system" })
        {
            setLayout.Invoke(home, new object[] { modeName });
            C(modeName + " 功能卡无重叠且位于面板内部", tiles.All(x => modules.ClientRectangle.Contains(x.Bounds)) && !tiles.SelectMany((x, i) => tiles.Skip(i + 1).Select(y => x.Bounds.IntersectsWith(y.Bounds))).Any(x => x), modules.Bounds.ToString());
        }
        Click(tiles[2]);
        C("AI 功能卡进入真实对话页", (string)typeof(MainForm).GetField("_currentView", f).GetValue(form) == "ai", "ai");
        form.SwitchView("home"); setLayout.Invoke(home, new object[] { "mission" });

        form.SwitchView("engineering");
        C("工程工具视图可导航", views.ContainsKey("engineering") && views["engineering"] is EngineeringToolkitView, "ok");
        var eng = (EngineeringToolkitView)views["engineering"];
        var mode = (DarkSelect)typeof(EngineeringToolkitView).GetField("_mode", f).GetValue(eng);
        C("工具箱包含四大专业模块", mode.Inner.Items.Count == 4, "count=" + mode.Inner.Items.Count);
        var page = (Panel)typeof(EngineeringToolkitView).GetField("_page", f).GetValue(eng);
        C("信号仪表页含真实控件", page.Controls.Count >= 3, "controls=" + page.Controls.Count);
        mode.SelectedIndex = 1; Application.DoEvents();
        C("PLC/Modbus 页可切换", page.Controls.Count >= 3, "controls=" + page.Controls.Count);
        mode.SelectedIndex = 3; Application.DoEvents();
        C("验证页可切换", page.Controls.Count >= 2, "controls=" + page.Controls.Count);

        form.SwitchView("projects");
        C("项目舱可导航", views.ContainsKey("projects") && views["projects"] is WorkspaceView, "ok");
        form.SwitchView("mobile");
        C("手机联动页可导航", views.ContainsKey("mobile") && views["mobile"] is MobileSyncView, "ok");
        form.SwitchView("ai");
        var chatView = views["ai"] as ChatView;
        C("AI&SI 对话视图可导航", chatView != null, "ok");
        var modeSel = (DarkSelect)typeof(ChatView).GetField("_modeSel", f).GetValue(chatView);
        C("AI 提供六种工程模式", modeSel != null && modeSel.Inner.Items.Count == 6, "count=" + (modeSel == null ? 0 : modeSel.Inner.Items.Count));

        var cmd = new CommandPaletteForm(new[] { new CommandItem { Name = "工程工具", Hint = "测试" } }, p);
        C("全局命令面板可创建", cmd.Width >= 700 && cmd.Height >= 500, cmd.Size.ToString());
        cmd.Dispose();

        typeof(MainForm).GetMethod("OnFormClosing", f).Invoke(form, new object[] { new FormClosingEventArgs(CloseReason.ApplicationExitCall, false) });
        form.Dispose();
        Console.WriteLine("\n==== 工程 UI 验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
