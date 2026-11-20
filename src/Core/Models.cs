using System;
using System.Collections.Generic;

namespace DeskBox
{
    /// <summary>应用设置（持久化到 data\state.json 的 settings 节点）</summary>
    public class AppSettings
    {
        public string Theme = "cyber";         // cyber | midnight | aurora | daylight
        public string Accent = "#41D9FF";
        public string View = "grid";           // grid | list | compact
        public int IconSize = 64;              // 40..96
        public bool ShowHidden = false;
        public string HideMode = "soft";       // soft | attribute
        public string SortBy = "name";         // name | mtime | size | kind
        public string SortDir = "asc";
        public bool Clock24 = true;
        public bool ShowSeconds = true;
        public string WeatherCity = "";
        public double WeatherLat = 0;
        public double WeatherLon = 0;
        public bool HasCoords = false;
        public string DesktopDir = "";         // 空=自动探测
        public bool TakeoverIcons = true;      // 隐藏系统桌面图标，由 DeskBox 接管
        public bool AutoStart = false;
        public bool ShowWidgets = true;
        public bool ShowSidebar = true;
        public bool NavExpanded = false;
        public bool ReferenceUi = false;
        public string WallpaperTint = "#0B1220";
        public double TintOpacity = 0.55;

        // ---- v3 新增（缺省值兼容旧 state.json，缺失字段自动取默认值，不清空用户配置）----
        public bool Animations = true;              // 界面动画开关
        public int Zoom = 100;                      // 界面缩放（100/110/125），下一版生效
        public int WeatherRefreshMinutes = 10;      // 天气自动刷新间隔
        public string TempUnit = "C";               // C | F
        public string WorkflowFailMode = "stop";    // stop | skip
        public int WorkflowConcurrency = 1;         // 1 | 2 | 4
        public int WorkflowHistoryKeep = 500;       // 执行历史保留条数
        public bool WorkflowConfirmDanger = true;   // 高风险操作需确认
        public bool AiFileAccess = false;           // 允许 AI 读取文件内容
        public bool TiaEnterSends = true;
        public int TiaTreeWidth = 260;
        public int TiaAssistantWidth = 350;
        public int TiaLogHeight = 152;
        public int TiaAssistantHeight = 224;
        public bool TiaTreeCollapsed;
        public bool TiaAssistantCollapsed;
        public bool TiaLogCollapsed;
        public bool FileOpConfirm = true;           // 文件操作需确认
        public bool NotifyInApp = true;             // 应用内提醒
        public bool NotifyTask = true;              // 任务提醒
        public bool NotifyTray = false;             // 托盘气泡通知
        public int LogKeepDays = 30;                // 日志保留天数
        public bool LocalSyncEnabled = false;
        public int LocalSyncPort = 47831;
        public int DiscoveryPort = 47830;
        public string SyncDeviceName = "";
        public string HomeLayout = "mission";
        public bool EngineeringDesktop;
        public bool DesktopPreviewEnabled = true;
    }

    public class Category
    {
        public string Id = "";
        public string Name = "";
        public string Icon = "📁";
        public string Color = "#60A5FA";
        public int Order = 0;
        public bool Builtin = false;
    }

    /// <summary>条目覆盖信息（分类 / 隐藏），按 id 持久化</summary>
    public class ItemOverride
    {
        public string CategoryId = "";
        public bool Hidden = false;
        public string HideMode = "soft";
    }

    /// <summary>桌面条目（运行时扫描结果）</summary>
    public class DesktopItem
    {
        public string Id = "";
        public string Name = "";
        public string Path = "";
        public string Ext = "";
        public string Kind = "file";       // file | dir | shortcut | other
        public long Size = 0;
        public long Mtime = 0;
        public string CategoryId = "";
        public bool Hidden = false;
        public bool SystemHidden = false;
        public bool Available = true;

        public string SizeText
        {
            get
            {
                if (Kind == "dir") return "文件夹";
                if (Size <= 0) return "0 B";
                string[] u = { "B", "KB", "MB", "GB", "TB" };
                double v = Size; int i = 0;
                while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
                return (i == 0 ? ((long)v).ToString() : v.ToString("0.#")) + " " + u[i];
            }
        }

        public string MtimeText
        {
            get
            {
                if (Mtime <= 0) return "";
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(Mtime).LocalDateTime;
                return dt.ToString("yyyy-MM-dd HH:mm");
            }
        }
    }

    public class TaskItem
    {
        public string Id = "";
        public string Title = "";
        public bool Done = false;
        public string Priority = "normal";  // low | normal | high
        public string Due = "";             // yyyy-MM-dd 或空
        public string RemindAt = "";        // yyyy-MM-dd HH:mm 或空
        public string Notes = "";           // 备注
        public string Project = "";         // 关联项目/工作区
        public string Owner = null;         // null: 旧版未保存此字段；空字符串表示明确清空负责人
        public bool Reminded = false;       // 本周期是否已提醒
        public long CreatedAt = 0;
        public long DoneAt = 0;
    }

    public class LogEntry
    {
        public string Id = "";
        public long Ts = 0;
        public string Level = "info";       // info | warn | error
        public string Action = "system";    // scan|hide|show|open|assign|task|settings|system
        public string Message = "";
    }

    /// <summary>应用内通知：持久化、可回看、可标记已读，不阻塞 UI。</summary>
    public class AppNotification
    {
        public string Id = "";
        public long Ts = 0;
        public string Title = "";
        public string Message = "";
        public string Kind = "info";       // info | warning | error | task | automation
        public string SourceId = "";       // 关联任务/工作流等标识
        public bool Read = false;
    }

    /// <summary>state.json 根结构</summary>
    public class PersistedState
    {
        public List<Category> Categories = new List<Category>();
        public Dictionary<string, ItemOverride> Items = new Dictionary<string, ItemOverride>();
        public AppSettings Settings = new AppSettings();
    }
}
