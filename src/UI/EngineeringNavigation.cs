using System;
using System.Linq;

namespace DeskBox.UI
{
    internal static class EngineeringNavigation
    {
        internal static readonly string[,] Modules =
        {
            { "home",       "\uE80F", "工程驾驶舱" },
            { "desktop",    "\uE8A5", "智能桌面" },
            { "projects",   "\uE8B7", "项目中心" },
            { "tia",        "\uE950", "PLC Studio" },
            { "dcs",        "\uE8A1", "DCS Studio" },
            { "validation", "\uE9D9", "验证中心" },
            { "incidents",  "\uE7BA", "现场工程" },
            { "ai",         "\uE99A", "AI 工程中心" },
            { "search",     "\uE721", "工程知识库" },
            { "automation", "\uE945", "自动化中心" },
            { "mobile",     "\uE8EA", "移动协同" },
            { "settings",   "\uE713", "系统设置" }
        };

        internal static readonly string[][] Secondary =
        {
            new[] { "projects", "项目目录", "project-detail", "项目详情", "project-context", "工程上下文", "tasks", "项目任务" },
            new[] { "incidents", "现场事件", "engineering", "工程工具", "calculator", "信号换算", "tool-workspace", "计算与诊断" },
            new[] { "ai", "工程会话", "mcp", "MCP 管理" },
            new[] { "search", "工程资料", "library", "收藏和标签", "filehistory", "文件操作历史" },
        };

        internal static string[] Routes { get { return Enumerable.Range(0, Modules.GetLength(0)).Select(index => Modules[index, 0]).ToArray(); } }

        internal static string ParentOf(string page)
        {
            var group = Secondary.FirstOrDefault(section => Enumerable.Range(0, section.Length / 2).Any(i => section[i * 2] == page));
            if (group != null) return group[0];
            if (page == "operations" || page == "mission") return "home";
            if (page == "history" || page == "notifications") return "home";
            if (new[] { "weather", "monitor", "clipboard", "disk" }.Contains(page)) return "settings";
            return page;
        }

        internal static string Title(string page)
        {
            var routes = Routes;
            int index = Array.IndexOf(routes, ParentOf(page));
            return index < 0 ? page : Modules[index, 2];
        }
    }
}
