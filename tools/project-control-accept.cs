// Mission Control project health, weighted progress, pulse, timeline and next-action acceptance.
using System;
using System.IO;
using System.Linq;
using DeskBox;
using DeskBox.Core;
using DeskBox.Engineering;

public class ProjectControlAccept
{
    static int pass, fail;
    static void C(string name, bool ok, string detail) { if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); } else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); } }

    public static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "deskbox-project-control-" + DateTime.Now.Ticks);
        var root = Path.Combine(data, "SureHand"); Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "PLC"));
        File.WriteAllText(Path.Combine(root, "PLC", "Main.scl"), "code");
        File.WriteAllText(Path.Combine(root, "old.txt"), "old"); File.SetLastWriteTime(Path.Combine(root, "old.txt"), DateTime.Now.AddDays(-20));
        Store.DataDir = data; AppPaths.DataDir = data;
        var store = new Store(); store.Init();
        var ws = new WorkspaceStore(); var project = ws.Add("SureHand Project", root, "PLC");
        ws.UpdateProjectDetails(project.Id, "SH-2026-01", "生物反应器", "Arthur", DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd"), DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"), "medium");
        ws.SetStageProgress(project.Id, "Design", 100); ws.SetStageProgress(project.Id, "PLC", 50); ws.SetCurrent(project.Id);

        store.Tasks.Add(new TaskItem { Id="overdue", Title="FAT-021 阻塞问题", Project=project.Id, Priority="high", Due=DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd"), Notes="等待供应商，阻塞" });
        store.Tasks.Add(new TaskItem { Id="done", Title="IO Check", Project=project.Id, Done=true, DoneAt=DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds() });
        store.Tasks.Add(new TaskItem { Id="other", Title="Other Project", Project="other" });
        store.SaveTasksOnly();
        store.Log("info", "ai.chat", "AI 分析 PLC 程序差异");
        store.Log("error", "project", "FAT 测试出现偏差");

        var service = new ProjectControlService(store, ws);
        var s = service.GetSnapshot(true);
        C("当前项目正确", s.ProjectId == project.Id && s.ProjectName == "SureHand Project", s.ProjectName);
        C("阶段权重进度=20%", Math.Abs(s.OverallProgress - 20) < 0.001, s.OverallProgress.ToString("0.###"));
        C("阶段明细存在", s.Stages.Count >= 8 && s.Stages["PLC"] == 50, "stages=" + s.Stages.Count);
        C("只统计当前项目明确任务", s.OpenTasks == 1 && s.OverdueTasks == 1 && s.BlockedTasks == 1 && s.HighRiskTasks == 1, s.OpenTasks + "/" + s.OverdueTasks + "/" + s.BlockedTasks);
        C("7天完成任务", s.TasksCompleted7d == 1, s.TasksCompleted7d.ToString());
        C("7天修改文件", s.FilesModified7d == 1, s.FilesModified7d.ToString());
        C("7天 AI 活动", s.AiActions7d >= 1, s.AiActions7d.ToString());
        C("7天错误", s.Errors7d >= 1, s.Errors7d.ToString());
        C("逾期/阻塞导致 AT RISK", s.Health == "AT RISK", s.Health);
        C("Next Action 优先逾期任务", s.NextAction.Contains("FAT-021") && s.NextAction.Contains("逾期"), s.NextAction);
        C("截止天数真实计算", s.DaysToDeadline == 20, s.DaysToDeadline.ToString());
        C("Timeline 来自真实日志", s.Timeline.Any(x => x.Contains("PLC 程序差异")) && s.Timeline.Any(x => x.Contains("FAT 测试")), string.Join(" | ", s.Timeline.ToArray()));

        var reloaded = new WorkspaceStore();
        C("当前项目选择持久化", reloaded.Current != null && reloaded.Current.Id == project.Id, reloaded.Current == null ? "none" : reloaded.Current.Name);
        reloaded.SetStageProgress(project.Id, "PLC", 150);
        C("阶段进度限制 0–100", reloaded.Current.StageProgress["PLC"] == 100, reloaded.Current.StageProgress["PLC"].ToString());

        store.Tasks.Clear(); store.SaveTasksOnly();
        reloaded.UpdateProjectDetails(project.Id, "SH-2026-01", "生物反应器", "Arthur", "", DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"), "low");
        var healthy = new ProjectControlService(store, reloaded).GetSnapshot(true);
        C("无阻塞且低风险为 ON TRACK", healthy.Health == "ON TRACK", healthy.Health);
        C("Next Action 推进最低阶段", healthy.NextAction.Contains("最低进度阶段"), healthy.NextAction);

        Console.WriteLine("\n==== Project Control 验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
