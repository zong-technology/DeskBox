using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskBox.Engineering
{
    public class ProjectHealthSnapshot
    {
        public string ProjectId = "";
        public string ProjectName = "";
        public double OverallProgress;
        public bool ProgressNotApplicable;
        public string ProgressText { get { return ProgressNotApplicable ? "N/A" : OverallProgress.ToString("0.#") + "%"; } }
        public string Health = "NO PROJECT";
        public int OpenTasks;
        public int OverdueTasks;
        public int BlockedTasks;
        public int HighRiskTasks;
        public int FilesModified7d;
        public int TasksCompleted7d;
        public int AiActions7d;
        public int Errors7d;
        public int DaysToDeadline = int.MaxValue;
        public string NextAction = "先在项目舱设置当前项目。";
        public List<string> Timeline = new List<string>();
        public Dictionary<string, double> Stages = new Dictionary<string, double>();
    }

    /// <summary>Computes Mission Control metrics only from persisted project/task/file/log facts.</summary>
    public class ProjectControlService
    {
        private readonly Store _store;
        private readonly WorkspaceStore _workspaces;
        private readonly object _lock = new object();
        private ProjectHealthSnapshot _cache;
        private long _cacheAt;

        public ProjectControlService(Store store, WorkspaceStore workspaces) { _store = store; _workspaces = workspaces; }

        public ProjectHealthSnapshot GetSnapshot(bool force)
        {
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var current = _workspaces == null ? null : _workspaces.Current;
                if (!force && _cache != null && _cache.ProjectId == (current == null ? "" : current.Id) && now - _cacheAt < 30000) return _cache;
                _cache = Build(); _cacheAt = now; return _cache;
            }
        }

        private ProjectHealthSnapshot Build()
        {
            var project = _workspaces == null ? null : _workspaces.Current;
            var s = new ProjectHealthSnapshot();
            if (project == null) return s;
            s.ProjectId = project.Id; s.ProjectName = project.Name; s.Stages = new Dictionary<string, double>(project.StageProgress ?? EngineerWorkspace.DefaultProgress());

            s.OverallProgress = ValidationProgress.Overall(project) ?? 0;
            s.ProgressNotApplicable = !ValidationProgress.Overall(project).HasValue;

            var tasks = _store.Tasks.Where(t => string.IsNullOrEmpty(t.Project) || t.Project == project.Id || string.Equals(t.Project, project.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            s.OpenTasks = tasks.Count(t => !t.Done);
            s.OverdueTasks = tasks.Count(t => !t.Done && IsOverdue(t.Due));
            s.BlockedTasks = tasks.Count(t => !t.Done && ((t.Notes ?? "").IndexOf("阻塞", StringComparison.OrdinalIgnoreCase) >= 0 || (t.Title ?? "").IndexOf("BLOCK", StringComparison.OrdinalIgnoreCase) >= 0));
            s.HighRiskTasks = tasks.Count(t => !t.Done && t.Priority == "high");
            var sevenDaysAgo = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
            s.TasksCompleted7d = tasks.Count(t => t.Done && t.DoneAt >= sevenDaysAgo);
            s.FilesModified7d = CountRecentFiles(project.Root, DateTime.Now.AddDays(-7), 20000);

            var logs = _store.ReadLogs(2000).Where(x => x.Ts >= sevenDaysAgo).ToList();
            s.AiActions7d = logs.Count(x => (x.Action ?? "").StartsWith("ai", StringComparison.OrdinalIgnoreCase) || x.Action == "tool");
            s.Errors7d = logs.Count(x => x.Level == "error");
            foreach (var log in logs.OrderByDescending(x => x.Ts).Take(8))
                s.Timeline.Add(DateTimeOffset.FromUnixTimeMilliseconds(log.Ts).LocalDateTime.ToString("MM-dd HH:mm") + "  " + log.Message);

            DateTime deadline;
            if (DateTime.TryParse(project.Deadline, out deadline)) s.DaysToDeadline = (deadline.Date - DateTime.Today).Days;
            string risk = (project.Risk ?? "medium").ToLowerInvariant();
            s.Health = s.OverdueTasks > 0 || s.BlockedTasks > 0 || risk == "high" || risk == "critical" || s.DaysToDeadline < 0 ? "AT RISK" : "ON TRACK";
            s.NextAction = NextAction(project, s, tasks);
            return s;
        }

        private static string NextAction(EngineerWorkspace project, ProjectHealthSnapshot s, List<TaskItem> tasks)
        {
            var overdue = tasks.Where(t => !t.Done && IsOverdue(t.Due)).OrderBy(t => t.Due).FirstOrDefault();
            if (overdue != null) return "优先处理逾期任务：" + overdue.Title + "（截止 " + overdue.Due + "）";
            var blocked = tasks.FirstOrDefault(t => !t.Done && ((t.Notes ?? "").Contains("阻塞") || (t.Title ?? "").IndexOf("BLOCK", StringComparison.OrdinalIgnoreCase) >= 0));
            if (blocked != null) return "解除阻塞：" + blocked.Title;
            if (s.DaysToDeadline <= 7) return "项目距离截止日期还有 " + s.DaysToDeadline + " 天，请复核交付物与未完成测试。";
            if (project.ValidationSteps != null)
            {
                var next = project.ValidationSteps.FirstOrDefault(x => x.Percent.HasValue && x.Percent < 100);
                return next == null ? "适用验证节点已完成或均为 N/A，请复核交付记录。" : "推进验证节点：" + next.Name + "（" + next.Percent.Value.ToString("0.#") + "%）";
            }
            var low = s.Stages.OrderBy(x => x.Value).FirstOrDefault();
            if (!string.IsNullOrEmpty(low.Key) && low.Value < 100) return "推进最低进度阶段：" + low.Key + "（当前 " + low.Value.ToString("0") + "%）";
            return "项目状态稳定，建议复核风险、变更记录和下一里程碑。";
        }

        private static bool IsOverdue(string due) { DateTime d; return !string.IsNullOrEmpty(due) && DateTime.TryParse(due, out d) && d.Date < DateTime.Today; }

        private static int CountRecentFiles(string root, DateTime cutoff, int limit)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;
            int count = 0, seen = 0; var pending = new Stack<string>(); pending.Push(root);
            while (pending.Count > 0 && seen < limit)
            {
                var dir = pending.Pop();
                try
                {
                    foreach (var file in Directory.GetFiles(dir)) { seen++; try { if (File.GetLastWriteTime(file) >= cutoff) count++; } catch { } if (seen >= limit) break; }
                    foreach (var child in Directory.GetDirectories(dir)) { try { if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child); } catch { } }
                }
                catch { }
            }
            return count;
        }
    }
}
