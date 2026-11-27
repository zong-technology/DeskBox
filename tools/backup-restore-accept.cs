// 数据备份/恢复验收：真实文件复制、恢复前快照、下次启动前应用。
using System;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.AppServices;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Engineering;

public class BackupRestoreAccept
{
    static int pass, fail;
    static void Check(string name, bool ok, string detail)
    {
        if (ok) { pass++; Console.WriteLine("PASS  " + name + "  [" + detail + "]"); }
        else { fail++; Console.WriteLine("FAIL  " + name + "  [" + detail + "]"); }
    }

    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-backup-" + DateTime.Now.Ticks);
        Directory.CreateDirectory(dir);
        Store.DataDir = dir;
        AppPaths.DataDir = dir;
        var store = new Store(); store.Init();
        store.Settings.Theme = "aurora";
        store.Tasks.Add(new TaskItem { Id = "old", Title = "备份时任务" });
        store.Save();
        Directory.CreateDirectory(Path.Combine(dir, "cache"));
        File.WriteAllText(Path.Combine(dir, "cache", "weather.json"), "old-weather");

        var backup = DataBackupService.CreateBackup("accept");
        Check("真实创建备份", backup.Ok && Directory.Exists(backup.Directory), backup.Message);
        Check("备份包含状态与任务", File.Exists(Path.Combine(backup.Directory, "state.json")) && File.Exists(Path.Combine(backup.Directory, "tasks.json")), "files=" + backup.FileCount);
        Check("备份包含嵌套缓存", File.Exists(Path.Combine(backup.Directory, "cache", "weather.json")), "cache");
        Check("备份不会递归复制 backup", !Directory.Exists(Path.Combine(backup.Directory, "backup")), "no recursion");

        store.Settings.Theme = "daylight";
        store.Tasks.Clear();
        store.Save();
        File.WriteAllText(Path.Combine(dir, "cache", "weather.json"), "new-weather");
        var scheduled = DataBackupService.ScheduleRestore(backup.Directory);
        Check("恢复请求已落盘", scheduled.Ok && File.Exists(Path.Combine(dir, "restore-pending.json")), scheduled.Message);

        string message;
        bool applied = DataBackupService.ApplyPendingRestore(out message);
        Check("恢复成功应用", applied, message);
        var restored = new Store(); restored.Init();
        Check("配置恢复到备份值", restored.Settings.Theme == "aurora", restored.Settings.Theme);
        Check("任务恢复到备份值", restored.Tasks.Count == 1 && restored.Tasks[0].Title == "备份时任务", "tasks=" + restored.Tasks.Count);
        Check("嵌套缓存恢复", File.ReadAllText(Path.Combine(dir, "cache", "weather.json")) == "old-weather", "cache restored");
        var safety = Directory.GetDirectories(Path.Combine(dir, "backup"), "pre_restore_*");
        Check("恢复前安全快照存在", safety.Length == 1 && File.Exists(Path.Combine(safety[0], "state.json")), "snapshots=" + safety.Length);
        Check("恢复请求应用后清除", !File.Exists(Path.Combine(dir, "restore-pending.json")), "cleared");

        var invalid = DataBackupService.ValidateBackup(Path.Combine(dir, "cache"));
        Check("拒绝非 DeskBox 备份", !invalid.Ok, invalid.Message);

        CheckValidationBackup(dir);

        Console.WriteLine("\n==== 备份恢复验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }

    private static void CheckValidationBackup(string root)
    {
        var token = CancellationToken.None;
        var workspaces = new WorkspaceStore();
        var projectFolder = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var workspace = workspaces.Add("Synthetic backup fixture", projectFolder, "PLC");
        workspaces.SetCurrent(workspace.Id);
        var project = workspaces.Context;
        var service = new ValidationService(root);
        var original = Path.Combine(projectFolder, "evidence.txt");
        File.WriteAllText(original, "synthetic evidence for backup verification");
        var evidence = service.ArchiveEvidence(project, new Evidence(), original, token);
        var live = service.EvidencePath(project, evidence, token);
        var good = DataBackupService.CreateBackup("evidence-valid");
        var tampered = DataBackupService.CreateBackup("evidence-tampered");
        Check("验证备份包含经过校验的证据与元数据", good.Ok && tampered.Ok &&
            DataBackupService.ValidateBackup(good.Directory).Ok, good.Message);
        if (!good.Ok || !tampered.Ok) return;

        string blob = Directory.GetFiles(Path.Combine(tampered.Directory, "validation"), "*.bin", SearchOption.AllDirectories).Single();
        var changed = File.ReadAllBytes(blob);
        changed[0] ^= 1;
        File.WriteAllBytes(blob, changed);
        var rejected = DataBackupService.ScheduleRestore(tampered.Directory);
        Check("拒绝恢复长度相同但哈希无效的证据备份", !rejected.Ok &&
            !File.Exists(Path.Combine(root, "restore-pending.json")), rejected.Message);

        File.WriteAllBytes(live, changed);
        var failed = DataBackupService.CreateBackup("corrupt-working-copy");
        Check("证据损坏时不发布不完整的备份", !failed.Ok && !Directory.Exists(failed.Directory), failed.Message);
        string message = "";
        Check("有效备份可恢复损坏原件并保留原有引用", DataBackupService.ScheduleRestore(good.Directory).Ok &&
            DataBackupService.ApplyPendingRestore(out message) && service.VerifyEvidence(project, evidence, token) == "verified" &&
            service.Read(project, token).Find<Evidence>(evidence.Id) != null, message);

        var unpublished = Directory.CreateDirectory(Path.Combine(root, "backup", ".snapshot-fixture")).FullName;
        File.Copy(Path.Combine(root, "state.json"), Path.Combine(unpublished, "state.json"));
        Check("未发布的临时目录不可用作正式恢复源", !DataBackupService.ValidateBackup(unpublished).Ok, unpublished);
    }
}
