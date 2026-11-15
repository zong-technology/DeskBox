using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;

namespace DeskBox
{
    public class BackupResult
    {
        public bool Ok;
        public string Message = "";
        public string Directory = "";
        public int FileCount;
    }

    internal class PendingRestore
    {
        public string BackupDirectory = "";
        public long RequestedAt;
    }

    /// <summary>数据备份与恢复。恢复在下次 Store.Init 前执行，并先做自动安全快照。</summary>
    public static class DataBackupService
    {
        private const string PendingFileName = "restore-pending.json";

        public static BackupResult CreateBackup(string label)
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var safeLabel = string.IsNullOrWhiteSpace(label) ? "manual" : Sanitize(label);
            var dest = Path.Combine(Store.DataDir, "backup", safeLabel + "_" + stamp);
            return CreateSnapshot(Store.DataDir, dest);
        }

        public static BackupResult ScheduleRestore(string backupDirectory)
        {
            var check = ValidateBackup(backupDirectory);
            if (!check.Ok) return check;
            try
            {
                Json.Save(Path.Combine(Store.DataDir, PendingFileName), new PendingRestore
                {
                    BackupDirectory = Path.GetFullPath(backupDirectory),
                    RequestedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                return new BackupResult { Ok = true, Directory = Path.GetFullPath(backupDirectory), FileCount = check.FileCount, Message = "已安排在下次启动前恢复" };
            }
            catch (Exception ex) { return new BackupResult { Ok = false, Message = "无法安排恢复：" + ex.Message }; }
        }

        public static bool ApplyPendingRestore(out string message)
        {
            message = "没有待恢复的数据";
            var marker = Path.Combine(Store.DataDir, PendingFileName);
            if (!File.Exists(marker)) return false;
            PendingRestore pending = null;
            try { pending = Json.Load<PendingRestore>(marker, null); }
            catch { }
            if (pending == null || string.IsNullOrWhiteSpace(pending.BackupDirectory))
            {
                TryDelete(marker);
                message = "恢复请求无效，已取消";
                return false;
            }

            var check = ValidateBackup(pending.BackupDirectory);
            if (!check.Ok)
            {
                TryDelete(marker);
                message = check.Message;
                return false;
            }

            try
            {
                var safety = Path.Combine(Store.DataDir, "backup", "pre_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var snapshot = CreateSnapshot(Store.DataDir, safety);
                if (!snapshot.Ok) throw new IOException("创建恢复前安全快照失败：" + snapshot.Message);

                int restored = CopyTree(pending.BackupDirectory, Store.DataDir, true, null);
                TryDelete(marker);
                message = "已恢复 " + restored + " 个文件；恢复前快照：" + safety;
                return true;
            }
            catch (Exception ex)
            {
                TryDelete(marker);
                message = "恢复失败：" + ex.Message;
                return false;
            }
        }

        public static BackupResult ValidateBackup(string backupDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(backupDirectory)) return new BackupResult { Ok = false, Message = "未选择备份目录" };
                var full = Path.GetFullPath(backupDirectory);
                if (!System.IO.Directory.Exists(full)) return new BackupResult { Ok = false, Message = "备份目录不存在：" + full };
                int count = System.IO.Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Count();
                if (count == 0) return new BackupResult { Ok = false, Message = "所选目录不包含任何备份文件" };
                bool recognizable = File.Exists(Path.Combine(full, "state.json")) || File.Exists(Path.Combine(full, "tasks.json")) || File.Exists(Path.Combine(full, "ai-providers.json"));
                if (!recognizable) return new BackupResult { Ok = false, Message = "所选目录不是可识别的 DeskBox 数据备份" };
                return new BackupResult { Ok = true, Directory = full, FileCount = count, Message = "备份有效" };
            }
            catch (Exception ex) { return new BackupResult { Ok = false, Message = "检查备份失败：" + ex.Message }; }
        }

        private static BackupResult CreateSnapshot(string source, string destination)
        {
            try
            {
                System.IO.Directory.CreateDirectory(destination);
                string excluded = Path.GetFullPath(Path.Combine(source, "backup"));
                int count = CopyTree(source, destination, false, excluded);
                return new BackupResult { Ok = true, Directory = destination, FileCount = count, Message = "备份完成，共 " + count + " 个文件" };
            }
            catch (Exception ex) { return new BackupResult { Ok = false, Directory = destination, Message = "备份失败：" + ex.Message }; }
        }

        private static int CopyTree(string source, string destination, bool atomic, string excludedRoot)
        {
            int count = 0;
            string sourceFull = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in System.IO.Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                if (!string.IsNullOrEmpty(excludedRoot) && IsWithin(full, excludedRoot)) continue;
                if (string.Equals(Path.GetFileName(full), PendingFileName, StringComparison.OrdinalIgnoreCase)) continue;
                var relative = full.Substring(sourceFull.Length);
                var target = Path.Combine(destination, relative);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) System.IO.Directory.CreateDirectory(parent);
                if (atomic) CopyAtomic(full, target); else File.Copy(full, target, true);
                count++;
            }
            return count;
        }

        private static void CopyAtomic(string source, string target)
        {
            var tmp = target + ".restore.tmp";
            File.Copy(source, tmp, true);
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        private static bool IsWithin(string path, string root)
        {
            var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var p = Path.GetFullPath(path);
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sanitize(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Trim();
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
