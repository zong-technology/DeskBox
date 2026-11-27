using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using DeskBox.Core;
using DeskBox.Data;
using DeskBox.Domain;

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
                Json.SaveAtomic(Path.Combine(Store.DataDir, PendingFileName), new PendingRestore
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
                foreach (var folder in ValidationDirectories(pending.BackupDirectory))
                {
                    var destination = Path.Combine(Store.DataDir, ValidationStore.FolderName, Path.GetFileName(folder));
                    ValidationStore.WithDirectory(destination, CancellationToken.None, target =>
                    {
                        ValidationStore.RequireRestorableDirectory(target);
                        return true;
                    });
                }
                var safety = Path.Combine(Store.DataDir, "backup", "pre_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var snapshot = CreateSnapshot(Store.DataDir, safety, false);
                safety = snapshot.Directory;
                if (!snapshot.Ok) throw new IOException("创建恢复前安全快照失败：" + snapshot.Message);

                int restored = CopyTree(pending.BackupDirectory, Store.DataDir, true, null, true);
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
                if (Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar)).StartsWith(".snapshot-", StringComparison.Ordinal))
                    return new BackupResult { Ok = false, Message = "此备份尚未完成" };
                if (!System.IO.Directory.Exists(full)) return new BackupResult { Ok = false, Message = "备份目录不存在：" + full };
                int count = System.IO.Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories).Count();
                if (count == 0) return new BackupResult { Ok = false, Message = "所选目录不包含任何备份文件" };
                bool recognizable = File.Exists(Path.Combine(full, "state.json")) || File.Exists(Path.Combine(full, "tasks.json")) || File.Exists(Path.Combine(full, "ai-providers.json"));
                if (!recognizable) return new BackupResult { Ok = false, Message = "所选目录不是可识别的 DeskBox 数据备份" };
                foreach (var directory in ValidationDirectories(full))
                    ValidationStore.WithDirectory(directory, CancellationToken.None, folder => ReadEvidence(folder, true));
                return new BackupResult { Ok = true, Directory = full, FileCount = count, Message = "备份有效" };
            }
            catch (Exception ex) { return new BackupResult { Ok = false, Message = "检查备份失败：" + ex.Message }; }
        }

        private static BackupResult CreateSnapshot(string source, string destination, bool validateEvidence = true)
        {
            var staging = Path.Combine(Path.GetDirectoryName(destination), ".snapshot-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            try
            {
                Directory.CreateDirectory(staging);
                string excluded = Path.GetFullPath(Path.Combine(source, "backup"));
                int count = CopyTree(source, staging, false, excluded, validateEvidence);
                if (Directory.Exists(destination)) destination += "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Directory.Move(staging, destination);
                return new BackupResult { Ok = true, Directory = destination, FileCount = count, Message = "备份完成，共 " + count + " 个文件" };
            }
            catch (Exception ex)
            {
                return new BackupResult { Ok = false, Directory = destination, Message = "备份失败：" + ex.Message };
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static int CopyTree(string source, string destination, bool restore, string excludedRoot, bool validateEvidence)
        {
            int count = 0;
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            var sourcePrefixLength = source.Length + 1;
            string validationRoot = Path.Combine(source, ValidationStore.FolderName);
            foreach (var file in EnumerateFiles(source, validationRoot, excludedRoot))
            {
                if (string.Equals(Path.GetFileName(file), PendingFileName, StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(destination, file.Substring(sourcePrefixLength));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Copy(file, target, restore);
                count++;
            }
            foreach (var folder in ValidationDirectories(source))
            {
                var target = Path.Combine(destination, ValidationStore.FolderName, Path.GetFileName(folder));
                count += ValidationStore.WithDirectory(folder, CancellationToken.None, directory =>
                    restore
                        ? ValidationStore.WithDirectory(target, CancellationToken.None, ignored => CopyValidation(directory, target, true, validateEvidence))
                        : CopyValidation(directory, target, false, validateEvidence));
            }
            return count;
        }

        private static IEnumerable<string> EnumerateFiles(string root, string validationRoot, string excludedRoot)
        {
            foreach (var file in Directory.EnumerateFiles(root)) yield return file;
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                if (string.Equals(child, validationRoot, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(child, excludedRoot, StringComparison.OrdinalIgnoreCase) ||
                    (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var file in EnumerateFiles(child, validationRoot, excludedRoot)) yield return file;
            }
        }

        private static IEnumerable<string> ValidationDirectories(string source)
        {
            var root = Path.Combine(source, ValidationStore.FolderName);
            if (!Directory.Exists(root)) yield break;
            RequireOrdinaryDirectory(root);
            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                string name = Path.GetFileName(folder);
                if (name.Length != 32 || name.Any(c => !Uri.IsHexDigit(c))) continue;
                RequireOrdinaryDirectory(folder);
                yield return folder;
            }
        }

        private static int CopyValidation(string source, string target, bool restore, bool validate)
        {
            var directory = Path.Combine(source, "evidence");
            var destination = Path.Combine(target, "evidence");
            RequireOrdinaryDirectory(Path.GetDirectoryName(target));
            RequireOrdinaryDirectory(target);
            RequireOrdinaryDirectory(destination);
            if (restore) ValidationStore.RequireRestorableDirectory(target);
            int count = 0;
            if (validate)
            {
                foreach (var entry in ReadEvidence(source, false))
                {
                    Directory.CreateDirectory(destination);
                    Copy(Path.Combine(directory, entry.Key), Path.Combine(destination, entry.Key), restore, entry.Value);
                    count++;
                }
            }
            else if (Directory.Exists(directory))
            {
                RequireOrdinaryDirectory(directory);
                foreach (var file in Directory.EnumerateFiles(directory, "*.bin"))
                {
                    Directory.CreateDirectory(destination);
                    Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
                    count++;
                }
            }
            Directory.CreateDirectory(target);
            // The evidence required by both record snapshots is in place before metadata is published.
            string primary = Path.Combine(source, "records.json");
            string previous = Path.Combine(source, "records.json.bak");
            if (restore && File.Exists(primary) && !File.Exists(previous) && File.Exists(Path.Combine(target, "records.json.bak")))
            {
                Copy(primary, Path.Combine(target, "records.json.bak"), true);
                count++;
            }
            foreach (var name in new[] { "records.json.bak", "records.json" })
            {
                var file = Path.Combine(source, name);
                if (!File.Exists(file)) continue;
                Copy(file, Path.Combine(target, name), restore);
                count++;
            }
            if (!restore)
                foreach (var damaged in Directory.EnumerateFiles(source, "records.json.damaged-*"))
                {
                    File.Copy(damaged, Path.Combine(target, Path.GetFileName(damaged)), true);
                    count++;
                }
            if (restore && !File.Exists(primary) && File.Exists(previous))
            {
                Copy(previous, Path.Combine(target, "records.json"), true);
                count++;
            }
            return count;
        }

        private static Dictionary<string, Evidence> ReadEvidence(string directory, bool verifyBytes)
        {
            var evidenceRoot = Path.Combine(directory, "evidence");
            RequireOrdinaryDirectory(evidenceRoot);
            var result = new Dictionary<string, Evidence>(StringComparer.Ordinal);
            foreach (var item in ValidationStore.BackupEvidence(directory))
            {
                if (item.Sha256 == null || item.Sha256.Length != 64 || item.Sha256.Any(c => !Uri.IsHexDigit(c)))
                    throw new InvalidDataException("验证证据哈希格式无效。");
                string name = item.Sha256.ToLowerInvariant() + ".bin";
                Evidence previous;
                if (result.TryGetValue(name, out previous))
                {
                    if (previous.Size != item.Size) throw new InvalidDataException("证据记录的长度冲突。");
                    continue;
                }
                var path = Path.Combine(evidenceRoot, name);
                if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("验证证据缺失或路径无效：" + item.FileName);
                if (verifyBytes) CheckEvidence(path, item);
                result.Add(name, item);
            }
            return result;
        }

        private static void CheckEvidence(string file, Evidence item)
        {
            if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("验证证据缺失或路径无效：" + item.FileName);
            using (var stream = File.OpenRead(file))
            using (var hash = SHA256.Create())
            {
                if (stream.Length != item.Size || !string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""),
                    item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("验证证据文件核验失败：" + item.FileName);
            }
        }

        private static void RequireOrdinaryDirectory(string path)
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("验证数据目录是重解析点。");
        }

        private static void Copy(string source, string target, bool atomic, Evidence expected = null)
        {
            if (!atomic)
            {
                File.Copy(source, target, true);
                if (expected != null) CheckEvidence(target, expected);
                return;
            }
            var tmp = Path.Combine(Path.GetDirectoryName(target), ".restore-" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp");
            try
            {
                using (var input = File.OpenRead(source))
                using (var output = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.CopyTo(output);
                    output.Flush(true);
                }
                if (expected != null) CheckEvidence(tmp, expected);
                if (File.Exists(target)) File.Replace(tmp, target, null);
                else File.Move(tmp, target);
            }
            finally { TryDelete(tmp); }
        }

        private static string Sanitize(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value.Trim();
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
    }
}
