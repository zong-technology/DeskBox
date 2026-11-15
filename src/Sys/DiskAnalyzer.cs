using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Sys
{
    /// <summary>
    /// 磁盘占用分析器：一次遍历同时得到总量 / 大文件 / 第一层目录占用 / 重复文件。
    /// 只读，不修改任何文件；所有失败都写进 DiskReport.Error，不向调用方抛异常。
    /// 说明：
    ///  - 查重分两段：先按长度分桶（仅 Size>=4096 且同尺寸的文件才进入候选），
    ///    再对候选做流式 SHA-256，确认内容完全一致才生成 DuplicateGroup（同尺寸但内容不同的不会误报）。
    ///  - Dirs 只统计 root 的第一层子目录，聚合它们各自递归的总大小（深度由遍历自然覆盖，不额外递归）。
    /// </summary>
    public class DiskAnalyzer
    {
        private const long MinDuplicateSize = 4096;   // 小于 4KB 的文件不参与查重
        private const int ProgressDirStep = 200;      // 每 200 个目录至少回调一次
        private const int ProgressMsStep = 200;       // 或每 200ms 一次（先到先报）

        private static readonly string[] AlwaysSkipNames = { "$RECYCLE.BIN", "System Volume Information" };

        private sealed class DirNode
        {
            public readonly string Path;
            public readonly string TopDir;   // root 的第一层子目录（root 自身为 null）
            public readonly int Depth;
            public DirNode(string path, string topDir, int depth) { Path = path; TopDir = topDir; Depth = depth; }
        }

        private sealed class DirAgg
        {
            public readonly string Path;
            public long Size;
            public int FileCount;
            public DirAgg(string path) { Path = path; }
        }

        /// <summary>
        /// 分析 root。cancelled() 返回 true 时尽快返回已收集的部分结果；progress 不会过于频繁（≈200ms）。
        /// </summary>
        public DiskReport Analyze(string root, List<string> excludes, int largeTopN, int dirTopN,
                                  bool findDuplicates, Action<string> progress, Func<bool> cancelled)
        {
            var report = new DiskReport();
            var sw = Stopwatch.StartNew();
            int errorCount = 0;
            string lastError = "";
            bool stopped = false;
            long totalBytes = 0, fileCount = 0, dirCount = 0;

            if (largeTopN <= 0) largeTopN = 50;
            if (dirTopN <= 0) dirTopN = 30;

            try
            {
                if (string.IsNullOrEmpty(root))
                {
                    report.Error = "根目录为空";
                    return report;
                }

                string fullRoot = NormalizePath(root);
                report.Root = fullRoot;
                if (!Directory.Exists(fullRoot))
                {
                    report.Error = "目录不存在: " + fullRoot;
                    return report;
                }

                List<string> excludeList = NormalizeExcludes(excludes);
                var stack = new Stack<DirNode>();
                stack.Push(new DirNode(fullRoot, null, 0));
                var topDirs = new Dictionary<string, DirAgg>(StringComparer.OrdinalIgnoreCase);
                var large = new List<LargeFile>();
                Dictionary<long, object> sizeIndex = findDuplicates ? new Dictionary<long, object>() : null;
                long lastProgressMs = 0;

                while (stack.Count > 0)
                {
                    if (IsCancelled(cancelled)) { stopped = true; break; }
                    DirNode node = stack.Pop();
                    if (node.Depth > 0) dirCount++;

                    var entries = new List<string>();
                    try
                    {
                        foreach (string entry in Directory.EnumerateFileSystemEntries(node.Path)) entries.Add(entry);
                    }
                    catch (Exception ex)
                    {
                        // 无权限 / 路径过长 / 设备异常：计入错误，继续其他目录
                        errorCount++;
                        lastError = ex.Message;
                    }

                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (IsCancelled(cancelled)) { stopped = true; break; }
                        string path = entries[i];

                        FileAttributes attr;
                        try { attr = File.GetAttributes(path); }
                        catch { errorCount++; continue; }

                        if ((attr & FileAttributes.ReparsePoint) != 0) continue;   // 跳过符号链接 / 联接点

                        if ((attr & FileAttributes.Directory) != 0)
                        {
                            if (IsAlwaysSkipped(path)) continue;
                            if (IsExcluded(path, excludeList)) continue;

                            string top = node.TopDir != null ? node.TopDir : path;
                            if (!topDirs.ContainsKey(top)) topDirs[top] = new DirAgg(top);
                            stack.Push(new DirNode(path, top, node.Depth + 1));
                            continue;
                        }

                        if (IsExcluded(path, excludeList)) continue;

                        long size;
                        long mtime;
                        try
                        {
                            var fi = new FileInfo(path);
                            if (!fi.Exists) continue;
                            size = fi.Length;
                            mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                        }
                        catch { errorCount++; continue; }

                        totalBytes += size;
                        fileCount++;

                        if (node.TopDir != null)
                        {
                            DirAgg agg;
                            if (topDirs.TryGetValue(node.TopDir, out agg))
                            {
                                agg.Size += size;
                                agg.FileCount++;
                            }
                        }

                        if (largeTopN > 0) OfferLarge(large, largeTopN, path, size, mtime);

                        if (sizeIndex != null && size >= MinDuplicateSize)
                        {
                            object slot;
                            if (!sizeIndex.TryGetValue(size, out slot)) sizeIndex[size] = path;
                            else
                            {
                                var bucket = slot as List<string>;
                                if (bucket == null)
                                {
                                    bucket = new List<string>();
                                    bucket.Add((string)slot);
                                    sizeIndex[size] = bucket;
                                }
                                bucket.Add(path);
                            }
                        }
                    }

                    if (stopped) break;

                    // 节流：每 200ms 或每 200 个目录回调一次
                    if (progress != null)
                    {
                        long nowMs = sw.ElapsedMilliseconds;
                        if (lastProgressMs == 0 || nowMs - lastProgressMs >= ProgressMsStep || dirCount % ProgressDirStep == 0)
                        {
                            lastProgressMs = nowMs;
                            Report(progress, "扫描中：" + dirCount + " 个目录 / " + fileCount + " 个文件 / " +
                                             FormatSize(totalBytes) + " · " + Shorten(node.Path));
                        }
                    }
                }

                // ---- 大文件：先升序保留 topN，最终降序 ----
                large.Sort(CompareLargeDesc);
                report.LargeFiles = large;

                // ---- 目录占用：第一层子目录降序取 topN ----
                var dirs = new List<DirUsage>();
                foreach (KeyValuePair<string, DirAgg> kv in topDirs)
                {
                    var usage = new DirUsage();
                    usage.Path = kv.Value.Path;
                    usage.Size = kv.Value.Size;
                    usage.FileCount = kv.Value.FileCount;
                    dirs.Add(usage);
                }
                dirs.Sort(delegate(DirUsage a, DirUsage b)
                {
                    int c = b.Size.CompareTo(a.Size);
                    if (c != 0) return c;
                    return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
                });
                if (dirs.Count > dirTopN) dirs = dirs.GetRange(0, dirTopN);
                report.Dirs = dirs;

                report.TotalBytes = totalBytes;
                report.FileCount = fileCount;
                report.DirCount = dirCount;

                // ---- 重复文件（慢，可选） ----
                if (sizeIndex != null && !stopped)
                {
                    Report(progress, "查重中：正在校验 " + sizeIndex.Count + " 组同尺寸文件…");
                    CollectDuplicates(sizeIndex, report, cancelled, ref errorCount, ref stopped, ref lastError);
                }

                Report(progress, stopped
                    ? "已取消：返回已收集的结果"
                    : "完成：" + fileCount + " 个文件 / " + FormatSize(totalBytes));
            }
            catch (Exception ex)
            {
                report.Error = "分析失败: " + ex.Message;
                DeskBox.Core.AppLog.Write("error", "disk", "磁盘分析失败 " + root + ": " + ex.Message);
            }
            finally
            {
                sw.Stop();
                report.ElapsedMs = sw.ElapsedMilliseconds;
                if (errorCount > 0)
                {
                    string note = "跳过 " + errorCount + " 个无权限/不可访问项";
                    if (!string.IsNullOrEmpty(lastError)) note += "（" + lastError + "）";
                    report.Error = string.IsNullOrEmpty(report.Error) ? note : report.Error + "；" + note;
                }
            }
            return report;
        }

        // ------------------------------------------------------------------
        // 重复文件
        // ------------------------------------------------------------------

        private static void CollectDuplicates(Dictionary<long, object> sizeIndex, DiskReport report,
                                              Func<bool> cancelled, ref int errorCount, ref bool stopped, ref string lastError)
        {
            foreach (KeyValuePair<long, object> kv in sizeIndex)
            {
                if (IsCancelled(cancelled)) { stopped = true; break; }

                var bucket = kv.Value as List<string>;
                if (bucket == null || bucket.Count < 2) continue;

                var byHash = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (IsCancelled(cancelled)) { stopped = true; break; }
                    string hash = TryHashFile(bucket[i]);
                    if (hash == null)
                    {
                        errorCount++;
                        lastError = "无法读取 " + bucket[i];
                        continue;
                    }
                    List<string> same;
                    if (!byHash.TryGetValue(hash, out same))
                    {
                        same = new List<string>();
                        byHash[hash] = same;
                    }
                    same.Add(bucket[i]);
                }
                if (stopped) break;

                foreach (KeyValuePair<string, List<string>> g in byHash)
                {
                    if (g.Value.Count < 2) continue;
                    var group = new DuplicateGroup();
                    group.Size = kv.Key;
                    group.Hash = g.Key;
                    group.Paths.AddRange(g.Value);
                    report.Duplicates.Add(group);
                }
            }

            report.Duplicates.Sort(delegate(DuplicateGroup a, DuplicateGroup b)
            {
                return b.Wasted.CompareTo(a.Wasted);
            });
        }

        private static string TryHashFile(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024, FileOptions.SequentialScan))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(fs);
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------

        private static void OfferLarge(List<LargeFile> list, int max, string path, long size, long mtime)
        {
            var lf = new LargeFile();
            lf.Path = path;
            lf.Size = size;
            lf.Mtime = mtime;

            if (list.Count < max)
            {
                list.Add(lf);
                if (list.Count == max) list.Sort(CompareLargeAsc);
                return;
            }
            if (size <= list[0].Size) return;
            list[0] = lf;
            list.Sort(CompareLargeAsc);
        }

        private static int CompareLargeAsc(LargeFile a, LargeFile b) { return a.Size.CompareTo(b.Size); }
        private static int CompareLargeDesc(LargeFile a, LargeFile b) { return b.Size.CompareTo(a.Size); }

        private static string NormalizePath(string path)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch { full = path; }
            if (full.Length > 3)
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            else if (full.Length == 2 && full[1] == ':')
                full += Path.DirectorySeparatorChar;
            return full;
        }

        private static List<string> NormalizeExcludes(List<string> excludes)
        {
            var list = new List<string>();
            if (excludes == null) return list;
            for (int i = 0; i < excludes.Count; i++)
            {
                string e = excludes[i];
                if (string.IsNullOrEmpty(e)) continue;
                string full;
                try { full = Path.GetFullPath(e); }
                catch { full = e; }
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (full.Length == 2 && full[1] == ':') full += Path.DirectorySeparatorChar;
                if (full.Length == 0) continue;
                if (!list.Contains(full)) list.Add(full);
            }
            return list;
        }

        /// <summary>excludes 按路径前缀匹配（忽略大小写），目录边界安全，避免 C:\Temp 误伤 C:\Temporary。</summary>
        private static bool IsExcluded(string path, List<string> excludes)
        {
            if (excludes == null || excludes.Count == 0) return false;
            for (int i = 0; i < excludes.Count; i++)
            {
                string ex = excludes[i];
                if (string.IsNullOrEmpty(ex)) continue;
                if (string.Equals(path, ex, StringComparison.OrdinalIgnoreCase)) return true;
                char last = ex[ex.Length - 1];
                string prefix = (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                    ? ex
                    : ex + Path.DirectorySeparatorChar;
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsAlwaysSkipped(string path)
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < AlwaysSkipNames.Length; i++)
            {
                if (string.Equals(name, AlwaysSkipNames[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsCancelled(Func<bool> cancelled)
        {
            if (cancelled == null) return false;
            try { return cancelled(); }
            catch { return true; }
        }

        /// <summary>progress 是 UI 回调，异常不允许影响分析结果。</summary>
        private static void Report(Action<string> progress, string message)
        {
            if (progress == null) return;
            try { progress(message); }
            catch { }
        }

        private static string Shorten(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            const int max = 70;
            if (path.Length <= max) return path;
            return "…" + path.Substring(path.Length - max);
        }

        private static string FormatSize(long bytes)
        {
            double v = bytes;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? ((long)v).ToString() : v.ToString("0.#")) + " " + units[i];
        }
    }
}
