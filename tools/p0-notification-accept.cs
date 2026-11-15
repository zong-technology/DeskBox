// P0 回归：通知持久化、全应用任务提醒、服务单例接线与退出清理。
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Automation;
using DeskBox.Core;
using DeskBox.Sys;
using DeskBox.UI;
using DeskBox.UI.Views;

public class P0NotificationAccept
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
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-p0-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        Store.DataDir = dir;
        AppPaths.DataDir = dir;
        var store = new Store();
        store.Init();
        store.Settings.TakeoverIcons = false;
        store.Settings.NotifyInApp = true;
        store.Settings.NotifyTask = true;
        store.SaveSettingsOnly();

        Console.WriteLine("== 通知中心持久化 ==");
        var notices = new NotificationStore(store);
        int published = 0;
        notices.Published += delegate { published++; };
        var n = notices.Publish("测试", "非阻塞消息", "info", "x1");
        Check("发布事件触发", published == 1, "published=" + published);
        Check("未读计数", notices.UnreadCount == 1, "unread=" + notices.UnreadCount);
        var reloaded = new NotificationStore(store);
        Check("通知持久化重载", reloaded.Items.Count == 1 && reloaded.Items[0].Message == "非阻塞消息", "count=" + reloaded.Items.Count);
        reloaded.MarkRead(n.Id, true);
        Check("可标记已读", reloaded.UnreadCount == 0, "unread=" + reloaded.UnreadCount);
        reloaded.ClearRead();
        Check("可清理已读", reloaded.Items.Count == 0, "count=" + reloaded.Items.Count);

        Console.WriteLine("\n== 工作流启动计时器生命周期 ==");
        var engine = new WorkflowEngine(store);
        var wf = engine.Add("启动测试", "startup", 60);
        wf.Enabled = true;
        engine.Save();
        engine.StartScheduler();
        var startupField = typeof(WorkflowEngine).GetField("_startupTimer", BindingFlags.NonPublic | BindingFlags.Instance);
        var periodicField = typeof(WorkflowEngine).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance);
        Check("启动触发器被强引用", startupField.GetValue(engine) != null, "retained");
        engine.StopScheduler();
        Check("停止调度释放全部计时器", startupField.GetValue(engine) == null && periodicField.GetValue(engine) == null, "released");

        Console.WriteLine("\n== 主窗体服务接线与退出清理 ==");
        var due = new TaskItem { Id = "t_due", Title = "到点任务", RemindAt = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-dd HH:mm"), CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds() };
        store.Tasks.Add(due);
        store.SaveTasksOnly();
        var form = new MainForm(store);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var chat = (ChatService)typeof(MainForm).GetField("_chat", flags).GetValue(form);
        var workflows = (WorkflowEngine)typeof(MainForm).GetField("_workflows", flags).GetValue(form);
        Check("AI 工具接入同一工作流引擎", chat != null && object.ReferenceEquals(chat.Workflows, workflows), "wired");
        typeof(MainForm).GetMethod("CheckTaskReminders", flags).Invoke(form, null);
        var center = (NotificationStore)typeof(MainForm).GetField("_notifications", flags).GetValue(form);
        Check("未打开任务页也会提醒", due.Reminded && center.Items.Exists(x => x.SourceId == "t_due"), "notifications=" + center.Items.Count);
        Check("提醒不使用阻塞对话框", Application.OpenForms.Count == 0, "openForms=" + Application.OpenForms.Count);
        var monitor = (SystemMonitor)typeof(MainForm).GetField("_monitor", flags).GetValue(form);
        Check("持续监控仅由主窗体持有", monitor != null && monitor.Running, "running=" + (monitor != null && monitor.Running));
        typeof(MainForm).GetMethod("OnFormClosing", flags).Invoke(form, new object[] { new FormClosingEventArgs(CloseReason.ApplicationExitCall, false) });
        Check("退出停止监控", monitor != null && !monitor.Running, "running=" + (monitor != null && monitor.Running));
        Check("退出停止工作流调度", periodicField.GetValue(workflows) == null, "stopped");
        Check("退出释放提醒计时器", typeof(MainForm).GetField("_reminderTimer", flags).GetValue(form) == null, "released");
        form.Dispose();

        Console.WriteLine("\n==== P0/通知验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
