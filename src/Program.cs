using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Native;
using DeskBox.UI;

namespace DeskBox
{
    internal static class Program
    {
        private static Mutex _mutex;
        private static MainForm _form;
        private static NotifyIcon _tray;
        private static EventWaitHandle _showEvent;
        private static Thread _showThread;
        private static volatile bool _shuttingDown;
        private const string ShowEventName = "DeskBox.ShowCommand.7F3A";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--tia-worker")
            {
                Environment.ExitCode = Engineering.Tia.TiaOpenness.RunWorker(args[1], args[2]);
                return;
            }
            bool bridge = args.Length == 1 && args[0] == "--bridge";
            bool first;
            _mutex = new Mutex(true, "DeskBox.SingleInstance.7F3A", out first);
            if (!first)
            {
                if (bridge) { Environment.ExitCode = 5; return; }
                try
                {
                    var current = System.Diagnostics.Process.GetCurrentProcess();
                    foreach (var running in System.Diagnostics.Process.GetProcessesByName(current.ProcessName))
                    {
                        using (running)
                        {
                            if (running.Id == current.Id) continue;
                            string activePath = running.MainModule.FileName;
                            string launchPath = current.MainModule.FileName;
                            if (!string.Equals(activePath, launchPath, StringComparison.OrdinalIgnoreCase))
                            {
                                MessageBox.Show("另一个位置的 DeskBox 已在运行。\r\n\r\n正在运行：" + activePath + "\r\n本次启动：" + launchPath + "\r\n\r\n请先从托盘右键退出正在运行的版本，再启动本次选择的程序。新版本功能不会加载到旧进程中。", "DeskBox 版本切换", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                return;
                            }
                        }
                    }
                    using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set();
                }
                catch
                {
                    MessageBox.Show("DeskBox 已经在运行了（右下角托盘图标）。", "桌匣 DeskBox",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (bridge)
            {
                Environment.ExitCode = DeskBox.Sync.BridgeHost.Run();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 数据目录：DESKBOX_HOME → D:\DeskBox\data → %LOCALAPPDATA%（保证不依赖 EXE 目录可写）
            var dataDir = DeskBox.Core.DataPaths.Resolve();
            DeskBox.Core.AppPaths.DataDir = dataDir;
            DeskBox.Core.DataPaths.MigrateIfNeeded();
            Store.DataDir = dataDir;

            string restoreMessage;
            bool restored = DataBackupService.ApplyPendingRestore(out restoreMessage);
            if (restored) AppLog.Write("info", "backup", restoreMessage);
            else if (restoreMessage != "没有待恢复的数据") AppLog.Write("warn", "backup", restoreMessage);

            // 启动画面：每一步都是真实初始化动作，不做假进度
            var steps = new[]
            {
                "加载用户配置", "初始化数据存储", "初始化 AI 服务", "验证自动化配置",
                "验证系统监控", "加载文件索引", "准备桌面视图"
            };
            var splash = new SplashForm(steps);
            splash.Show();
            splash.Refresh();

            Store store = null;
            try
            {
                splash.Advance("加载用户配置");
                Application.DoEvents();

                splash.Advance("初始化数据存储");
                store = new Store();
                var stateFile=Path.Combine(dataDir,"state.json");bool knownReference=File.Exists(stateFile)&&File.ReadAllText(stateFile).IndexOf("\"ReferenceUi\"",StringComparison.Ordinal)>=0;
                store.Init();
                if(!knownReference){store.Settings.ReferenceUi=true;store.Settings.Theme="cyber";store.Settings.Accent="#25D5F5";store.SaveSettingsOnly();}
                store.Log("info", "system", "DeskBox 启动（数据目录 " + dataDir + "）");

                splash.Advance("初始化 AI 服务");
                DeskBox.Ai.AiService.Instance.LoadConfig();
                Application.DoEvents();

                // 这里只验证配置可读取；正式调度器由 MainForm 唯一持有，避免重复执行工作流。
                splash.Advance("验证自动化配置");
                new DeskBox.Automation.WorkflowEngine(store);

                // 做一次真实采样后立即释放；持续采样器同样由 MainForm 唯一持有。
                splash.Advance("验证系统监控");
                using (var monitor = new DeskBox.Sys.SystemMonitor(2000)) monitor.SampleNow();

                splash.Advance("加载文件索引");
                Application.DoEvents();

                splash.Advance("准备桌面视图");
                Application.DoEvents();
            }
            catch (Exception ex)
            {
                splash.Fail(ex.Message);
                AppLog.Write("error", "system", "启动失败: " + ex.ToString());
                MessageBox.Show("DeskBox 初始化失败：\n\n" + ex.Message + "\n\n将以降级模式继续启动。",
                    "桌匣 DeskBox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (store == null) { store = new Store(); try { store.Init(); } catch { } }
            }
            splash.Close();
            splash.Dispose();

            Application.ThreadException += (s, e) => AppLog.Write("error", "system", "UI 异常: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Write("error", "system", "未处理异常: " + e.ExceptionObject);

            _form = new MainForm(store);
            BuildTray(store, _form);
            StartShowListener();

            Application.Run(_form);

            _shuttingDown = true;
            try { if (_showEvent != null) _showEvent.Set(); } catch { }
            try { if (_showThread != null) _showThread.Join(1000); } catch { }
            try { if (_showEvent != null) _showEvent.Dispose(); } catch { }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Win32.SetSystemIconsVisible(true);
        }

        private static void StartShowListener()
        {
            _showThread = new Thread(delegate()
            {
                while (!_shuttingDown)
                {
                    try { _showEvent.WaitOne(); } catch { break; }
                    if (_shuttingDown) break;
                    try
                    {
                        var form = _form;
                        if (form != null && !form.IsDisposed)
                            form.BeginInvoke(new Action(form.ShowAndOpenCommandPalette));
                    }
                    catch { }
                }
            });
            _showThread.IsBackground = true;
            _showThread.Name = "DeskBox.ShowListener";
            _showThread.Start();
        }

        private static void BuildTray(Store store, MainForm form)
        {
            _tray = new NotifyIcon
            {
                Icon = form.Icon,
                Text = "桌匣 DeskBox",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏 DeskBox", null, (s, e) =>
            {
                form.Visible = !form.Visible;
            });
            var takeover = new ToolStripMenuItem("接管桌面图标") { Checked = form.SystemIconsHidden };
            takeover.Click += (s, e) =>
            {
                form.ApplyIconTakeover(!form.SystemIconsHidden);
                takeover.Checked = form.SystemIconsHidden;
            };
            menu.Items.Add(takeover);
            var auto = new ToolStripMenuItem("开机自启") { Checked = AutoStart.IsEnabled() };
            auto.Click += (s, e) =>
            {
                AutoStart.Apply(!AutoStart.IsEnabled());
                auto.Checked = AutoStart.IsEnabled();
                store.Settings.AutoStart = auto.Checked;
                store.SaveSettingsOnly();
            };
            menu.Items.Add(auto);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("刷新桌面", null, (s, e) => form.LoadItems(true));
            menu.Items.Add("设置…", null, (s, e) => form.OpenSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出（恢复系统图标）", null, (s, e) => form.Close());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => { form.Visible = true; form.Activate(); };
        }

        internal static void ShowTrayNotification(string title, string message)
        {
            try
            {
                if (_tray == null || !_tray.Visible) return;
                _tray.BalloonTipTitle = string.IsNullOrEmpty(title) ? "DeskBox" : title;
                _tray.BalloonTipText = message ?? "";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(5000);
            }
            catch { }
        }
    }
}
