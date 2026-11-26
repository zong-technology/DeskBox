using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DeskBox
{
    /// <summary>本地持久化：state.json / tasks.json / logs.jsonl，原子写入，零外部依赖。</summary>
    public class Store
    {
        public static string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        public AppSettings Settings = new AppSettings();
        public List<Category> Categories = new List<Category>();
        public Dictionary<string, ItemOverride> Overrides = new Dictionary<string, ItemOverride>();
        public List<TaskItem> Tasks = new List<TaskItem>();

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        private readonly object _lock = new object();

        public static readonly string[,] BuiltinCategories = new string[,]
        {
            { "docs",      "文档",     "📄", "#60A5FA" },
            { "images",    "图片",     "🖼", "#F472B6" },
            { "videos",    "视频",     "🎬", "#A78BFA" },
            { "audio",     "音频",     "🎵", "#34D399" },
            { "apps",      "程序",     "🚀", "#FBBF24" },
            { "archives",  "压缩包",   "🗜", "#F97316" },
            { "shortcuts", "快捷方式", "🔗", "#38BDF8" },
            { "folders",   "文件夹",   "📁", "#FACC15" },
            { "others",    "其他",     "📦", "#94A3B8" },
        };

        private static readonly Dictionary<string, string> ExtCategory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {".pdf","docs"},{".doc","docs"},{".docx","docs"},{".xls","docs"},{".xlsx","docs"},{".ppt","docs"},{".pptx","docs"},
            {".txt","docs"},{".md","docs"},{".csv","docs"},{".json","docs"},{".xml","docs"},{".rtf","docs"},
            {".png","images"},{".jpg","images"},{".jpeg","images"},{".gif","images"},{".bmp","images"},{".webp","images"},{".svg","images"},{".ico","images"},
            {".mp4","videos"},{".mkv","videos"},{".avi","videos"},{".mov","videos"},{".wmv","videos"},{".flv","videos"},
            {".mp3","audio"},{".wav","audio"},{".flac","audio"},{".m4a","audio"},{".aac","audio"},
            {".exe","apps"},{".msi","apps"},{".bat","apps"},{".cmd","apps"},{".ps1","apps"},
            {".zip","archives"},{".7z","archives"},{".rar","archives"},{".gz","archives"},{".tar","archives"},
            {".lnk","shortcuts"},{".url","shortcuts"},
        };

        public Store() { }

        public void Init()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(DataDir);
                Load();
            }
        }

        public void Load()
        {
            var file = Path.Combine(DataDir, "state.json");
            PersistedState st = null;
            if (File.Exists(file))
            {
                try { st = Json.Deserialize<PersistedState>(File.ReadAllText(file, Encoding.UTF8)); }
                catch (Exception ex) { AppLog.Write("warn", "system", "state.json 解析失败，使用默认值: " + ex.Message); }
            }
            if (st == null) st = new PersistedState();
            Settings = st.Settings ?? new AppSettings();
            Categories = st.Categories ?? new List<Category>();
            Overrides = st.Items ?? new Dictionary<string, ItemOverride>();
            if (Categories.Count == 0) Categories = DefaultCategories();
            else
            {
                // 补齐缺失的内置分类
                foreach (var def in DefaultCategories())
                {
                    if (!Categories.Any(c => c.Id == def.Id)) Categories.Add(def);
                }
            }
            Categories = Categories.OrderBy(c => c.Order).ToList();
            // 兼容旧版本写出的 NaN 坐标
            if (double.IsNaN(Settings.WeatherLat)) { Settings.WeatherLat = 0; Settings.HasCoords = false; }
            if (double.IsNaN(Settings.WeatherLon)) { Settings.WeatherLon = 0; Settings.HasCoords = false; }

            var tf = Path.Combine(DataDir, "tasks.json");
            Tasks = new List<TaskItem>();
            if (File.Exists(tf))
            {
                try { Tasks = Json.Deserialize<List<TaskItem>>(File.ReadAllText(tf, Encoding.UTF8)) ?? new List<TaskItem>(); }
                catch { Tasks = new List<TaskItem>(); }
            }
        }

        public static List<Category> DefaultCategories()
        {
            var list = new List<Category>();
            for (int i = 0; i < BuiltinCategories.GetLength(0); i++)
            {
                list.Add(new Category
                {
                    Id = BuiltinCategories[i, 0],
                    Name = BuiltinCategories[i, 1],
                    Icon = BuiltinCategories[i, 2],
                    Color = BuiltinCategories[i, 3],
                    Order = i + 1,
                    Builtin = true
                });
            }
            return list;
        }

        public static string CategoryForExt(string ext, string kind)
        {
            if (kind == "dir") return "folders";
            if (string.IsNullOrEmpty(ext)) return "others";
            string cat;
            return ExtCategory.TryGetValue(ext, out cat) ? cat : "others";
        }

        public void Save()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(DataDir);
                var st = new PersistedState { Categories = Categories, Items = Overrides, Settings = Settings };
                WriteAtomic(Path.Combine(DataDir, "state.json"), Json.Serialize(st));
                WriteAtomic(Path.Combine(DataDir, "tasks.json"), Json.Serialize(Tasks));
            }
        }

        public void SaveSettingsOnly()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(DataDir);
                var st = new PersistedState { Categories = Categories, Items = Overrides, Settings = Settings };
                WriteAtomic(Path.Combine(DataDir, "state.json"), Json.Serialize(st));
            }
        }

        public void SaveTasksOnly()
        {
            lock (_lock)
            {
                Directory.CreateDirectory(DataDir);
                WriteAtomic(Path.Combine(DataDir, "tasks.json"), Json.Serialize(Tasks));
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        // ---------- 日志 ----------
        public void Log(string level, string action, string message)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var e = new LogEntry
                {
                    Id = "l_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Level = level,
                    Action = action,
                    Message = message
                };
                File.AppendAllText(Path.Combine(DataDir, "logs.jsonl"), Json.Serialize(e) + "\n", new UTF8Encoding(false));
            }
            catch { /* 日志失败不影响主流程 */ }
        }

        public List<LogEntry> ReadLogs(int limit)
        {
            var list = new List<LogEntry>();
            try
            {
                var file = Path.Combine(DataDir, "logs.jsonl");
                if (!File.Exists(file)) return list;
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                int start = Math.Max(0, lines.Length - Math.Max(limit * 3, limit));
                for (int i = start; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    try { list.Add(Json.Deserialize<LogEntry>(lines[i])); } catch { }
                }
                list = list.OrderByDescending(x => x.Ts).Take(limit).ToList();
            }
            catch { }
            return list;
        }

        public void ClearLogs()
        {
            try
            {
                var file = Path.Combine(DataDir, "logs.jsonl");
                if (File.Exists(file)) File.WriteAllText(file, "", new UTF8Encoding(false));
            }
            catch { }
        }

        public void CompactLogs(int keep)
        {
            try
            {
                var file = Path.Combine(DataDir, "logs.jsonl");
                if (!File.Exists(file)) return;
                var lines = File.ReadAllLines(file, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count <= keep) return;
                File.WriteAllLines(file, lines.Skip(lines.Count - keep), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>兜底日志：data 目录不可写时写到 %TEMP%</summary>
    public static class AppLog
    {
        public static void Write(string level, string action, string message)
        {
            try
            {
                var dir = Store.DataDir;
                Directory.CreateDirectory(dir);
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + level + "/" + action + "] " + message + Environment.NewLine;
                File.AppendAllText(Path.Combine(dir, "deskbox.log"), line, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
