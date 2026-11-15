using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;

namespace DeskBox.Sync
{
    internal static class BridgeHost
    {
        public static int Run()
        {
            var configured = Environment.GetEnvironmentVariable(DataPaths.EnvHome);
            if (string.IsNullOrWhiteSpace(configured) ||
                !configured.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase))
                return 2;

            var data = Path.GetFullPath(configured);
            if (data.Length <= 3) return 2;
            Directory.CreateDirectory(data);
            AppPaths.DataDir = Store.DataDir = data;

            try
            {
                var store = new Store();
                store.Init();
                var projects = new WorkspaceStore();
                using (var local = new LocalSyncCoordinator(store, projects))
                using (var cloud = new CloudSyncAgent(local))
                using (var dispatch = new WindowsFormsSynchronizationContext())
                {
                    if (!cloud.LoggedIn)
                    {
                        store.Log("warn", "bridge", "请先在此虚拟机上用同一 Windows 用户完成 DeskBox 私人云授权。");
                        return 3;
                    }

                    var providers = new ProviderStore();
                    var workflows = new DeskBox.Automation.WorkflowEngine(store);
                    var files = new DeskBox.Fs.FileOps(store);
                    cloud.MobileAi = new MobileAiRunner(providers, delegate
                    {
                        var context = new ToolContext {
                            Store = store, Projects = projects, Sync = local, Cloud = cloud,
                            Workflows = workflows, Files = files,
                            AllowedRoots = DeskBox.UI.TrustedRoots.Load(), FileAccessEnabled = () => store.Settings.AiFileAccess,
                            UiInvoke = action => {
                                ToolResult result = null;
                                dispatch.Send(delegate { result = action(); }, null);
                                return result;
                            }
                        };
                        context.RefreshAuthorization = () => context.AllowedRoots = DeskBox.UI.TrustedRoots.Load();
                        return context;
                    });
                    store.Log("info", "bridge", "后台中继已启动（不打开窗口，不开启局域网监听）。");
                    var previous = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(dispatch);
                    try { Application.Run(new ApplicationContext()); }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                    return 0;
                }
            }
            catch (Exception)
            {
                AppLog.Write("error", "bridge", "后台中继启动或运行失败，请检查 D 盘数据目录与 Windows 账户。");
                return 4;
            }
        }
    }
}
