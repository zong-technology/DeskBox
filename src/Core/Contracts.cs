using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBox.Core
{
    /// <summary>统一的运行时路径（全部指向安装目录，不在 C 盘落文件）。</summary>
    public static class AppPaths
    {
        public static string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        public static string CacheDir { get { return Path.Combine(DataDir, "cache"); } }
        public static string BackupDir { get { return Path.Combine(DataDir, "backup"); } }
        public static string ThumbDir { get { return Path.Combine(CacheDir, "thumbs"); } }
        public static string PluginDir { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins"); } }

        public static void EnsureAll()
        {
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(CacheDir);
            Directory.CreateDirectory(BackupDir);
            Directory.CreateDirectory(ThumbDir);
            Directory.CreateDirectory(PluginDir);
        }

        public static string File(string name) { return Path.Combine(DataDir, name); }
    }

    /// <summary>JSON 读写（Atomic：先写 .tmp 再替换）。全部 UTF-8 无 BOM。</summary>
    public static class Json
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static T Load<T>(string path, T fallback) where T : class
        {
            try
            {
                if (!File.Exists(path)) return fallback;
                var text = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return fallback;
                var v = Ser.Deserialize<T>(text);
                return v ?? fallback;
            }
            catch { return fallback; }
        }

        public static void Save<T>(string path, T value)
        {
            try { SaveAtomic(path, value); }
            catch (Exception ex) { AppLog.Write("error", "system", "保存失败 " + path + ": " + ex.Message); }
        }

        internal static void SaveAtomic<T>(string path, T value, bool keepPrevious = false)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(Ser.Serialize(value));
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, keepPrevious ? path + ".bak" : null);
                else File.Move(tmp, path);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        public static void AppendLine<T>(string path, T value)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(path, Ser.Serialize(value) + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { AppLog.Write("error", "system", "追加失败 " + path + ": " + ex.Message); }
        }

        public static List<T> ReadLines<T>(string path, int max)
        {
            var list = new List<T>();
            try
            {
                if (!File.Exists(path)) return list;
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - max);
                for (int i = start; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try { list.Add(Ser.Deserialize<T>(lines[i])); } catch { }
                }
            }
            catch { }
            return list;
        }

        public static string ToJson(object o) { return Ser.Serialize(o); }
        public static T FromJson<T>(string s) { try { return Ser.Deserialize<T>(s); } catch { return default(T); } }
    }

    // ---------------------------------------------------------------------
    // 文件系统层的公共契约（各模块只通过这里交换数据）
    // ---------------------------------------------------------------------

    public class FileEntry
    {
        public string Path = "";
        public string Name = "";
        public string Ext = "";
        public long Size = 0;
        public long Mtime = 0;
        public bool IsDir = false;
        public string Root = "";

        public DateTime Modified { get { return DateTimeOffset.FromUnixTimeMilliseconds(Mtime).LocalDateTime; } }
        public string SizeText
        {
            get
            {
                if (IsDir) return "文件夹";
                if (Size < 1024) return Size + " B";
                if (Size < 1024L * 1024) return (Size / 1024.0).ToString("0.#") + " KB";
                if (Size < 1024L * 1024 * 1024) return (Size / 1024.0 / 1024).ToString("0.#") + " MB";
                return (Size / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
            }
        }
    }

    /// <summary>搜索条件：所有字段可空/0 表示不限。</summary>
    public class SearchQuery
    {
        public string Text = "";              // 名称/路径关键词，空格分隔=AND
        public string PathContains = "";      // 路径片段
        public List<string> Exts = new List<string>();  // 例如 [".xlsx",".csv"]
        public List<string> Roots = new List<string>(); // 限定根目录
        internal Func<string, bool> PathFilter;
        public long MinSize = 0;
        public long MaxSize = 0;
        public long ModifiedAfter = 0;
        public long ModifiedBefore = 0;
        public string Kind = "";              // file | dir | ""（不限）
        public string SortBy = "name";        // name | size | mtime | path
        public bool SortDesc = false;
        public int Limit = 500;
    }

    public class SearchHit
    {
        public FileEntry Entry;
        public int Score;                     // 相关度（0..100+）
        public string Highlight = "";         // 命中片段说明
    }

    public class SearchOutcome
    {
        public List<SearchHit> Hits = new List<SearchHit>();
        public int TotalMatched = 0;
        public long ElapsedMs = 0;
        public string IndexState = "";        // idle | indexing | ready
        public string Message = "";
    }

    public class FavoriteEntry
    {
        public string Path = "";
        public string Name = "";
        public string Group = "默认";
        public long AddedAt = 0;
    }

    public class TagEntry
    {
        public string Path = "";
        public List<string> Tags = new List<string>();
    }

    public class RecentEntry
    {
        public string Path = "";
        public string Name = "";
        public long LastOpenAt = 0;
        public int OpenCount = 0;
    }

    // ---------------- 文件操作 ----------------

    public class RenameRule
    {
        public string Find = "";              // 查找（字面量，除非 UseRegex）
        public string Replace = "";           // 替换为
        public bool UseRegex = false;
        public int NumberStart = 1;           // 序号起始
        public int NumberStep = 1;
        public string NumberPattern = "";     // 如 "序号_" → 前缀；空=不编号
        public bool LowerCase = false;
        public bool UpperCase = false;
        public bool TrimSpace = true;
    }

    public class RenamePlanItem
    {
        public string Source = "";
        public string TargetDir = "";
        public string NewName = "";
        public string OldName = "";
        public bool Conflict = false;         // 目标已存在或计划内重名
        public string ConflictReason = "";
        public bool Changed { get { return OldName != NewName; } }
        public string NewPath { get { return System.IO.Path.Combine(TargetDir, NewName); } }
    }

    public class RenamePlan
    {
        public List<RenamePlanItem> Items = new List<RenamePlanItem>();
        public int ChangedCount = 0;
        public int ConflictCount = 0;
        public string Errors = "";
    }

    public class FileOpRecord
    {
        public string Id = "";
        public long Ts = 0;
        public string Op = "";        // open|favorite|move|copy|rename|delete|backup|preview|tag
        public string Source = "";
        public string Target = "";
        public bool Ok = true;
        public string Detail = "";
        public string UndoId = "";    // 关联的撤销批次（可为空）
    }

    /// <summary>撤销动作：按逆序执行即可回滚。</summary>
    public class UndoAction
    {
        public string Kind = "";      // move | rename | copy | trash
        public string From = "";
        public string To = "";
    }

    public class UndoBatch
    {
        public string Id = "";
        public long Ts = 0;
        public string Title = "";
        public List<UndoAction> Actions = new List<UndoAction>();
        public bool Used = false;
    }

    // ---------------- 预览 ----------------

    public class PreviewResult
    {
        public string Kind = "meta";  // image | text | code | table | pdf | shell | meta | unsupported
        public string Title = "";
        public string Subtitle = "";
        public string Text = "";      // 文本/代码内容（截断）
        public bool Truncated = false;
        public System.Drawing.Image Image;   // 图片或缩略图
        public List<string[]> Rows = new List<string[]>();  // 表格
        public List<string> Meta = new List<string>();      // 键值对 "键: 值"
        public string Error = "";     // 非空表示预览失败
    }

    // ---------------- 磁盘分析 ----------------

    public class LargeFile
    {
        public string Path = "";
        public long Size = 0;
        public long Mtime = 0;
    }

    public class DirUsage
    {
        public string Path = "";
        public long Size = 0;
        public int FileCount = 0;
    }

    public class DuplicateGroup
    {
        public long Size = 0;
        public string Hash = "";
        public List<string> Paths = new List<string>();
        public long Wasted { get { return Size * (Paths.Count - 1); } }
    }

    public class DiskReport
    {
        public string Root = "";
        public long TotalBytes = 0;
        public long FileCount = 0;
        public long DirCount = 0;
        public List<LargeFile> LargeFiles = new List<LargeFile>();
        public List<DirUsage> Dirs = new List<DirUsage>();
        public List<DuplicateGroup> Duplicates = new List<DuplicateGroup>();
        public long ElapsedMs = 0;
        public string Error = "";
    }

    // ---------------- 系统监控 ----------------

    public class DiskVolume
    {
        public string Name = "";
        public string Label = "";
        public long Total = 0;
        public long Free = 0;
        public double UsedPct { get { return Total <= 0 ? 0 : (Total - Free) * 100.0 / Total; } }
    }

    public class SystemSample
    {
        public long Ts = 0;
        public double CpuPercent = 0;      // 0..100（GetSystemTimes 采样）
        public double MemUsedPercent = 0;
        public long MemUsed = 0;
        public long MemTotal = 0;
        public double NetRxBps = 0;        // 字节/秒
        public double NetTxBps = 0;
        public List<DiskVolume> Disks = new List<DiskVolume>();
        public string Error = "";
    }

    // ---------------- 剪贴板 ----------------

    public class ClipEntry
    {
        public string Id = "";
        public long Ts = 0;
        public string Text = "";
        public string Kind = "text";   // text | files
        public bool Sensitive = false;
        public string Source = "";
    }

    /// <summary>全局日志入口（写 data/deskbox.log + 结构化 logs.jsonl 由 Store 负责）。</summary>
    public static class AppLog
    {
        public static void Write(string level, string action, string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppPaths.DataDir);
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "/" + action + "] " + message +
                           Environment.NewLine;
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.DataDir, "deskbox.log"), line, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
