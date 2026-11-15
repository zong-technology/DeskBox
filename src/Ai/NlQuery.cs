using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DeskBox.Ai
{
    internal static class LocalFmt
    {
        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }
    }
    /// <summary>自然语言 → 结构化搜索条件（纯本地规则，不联网）。</summary>
    public static class NlQuery
    {
        private static readonly Dictionary<string, string[]> ExtMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            {"excel", new[]{".xlsx",".xls",".csv"}},
            {"表格", new[]{".xlsx",".xls",".csv"}},
            {"xlsx", new[]{".xlsx"}},
            {"xls", new[]{".xls"}},
            {"csv", new[]{".csv"}},
            {"word", new[]{".doc",".docx"}},
            {"文档", new[]{".doc",".docx",".pdf",".txt",".md"}},
            {"pdf", new[]{".pdf"}},
            {"ppt", new[]{".ppt",".pptx"}},
            {"演示", new[]{".ppt",".pptx"}},
            {"图片", new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp",".svg"}},
            {"照片", new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp"}},
            {"图片格式", new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp",".svg"}},
            {"视频", new[]{".mp4",".mkv",".avi",".mov",".wmv",".flv"}},
            {"音频", new[]{".mp3",".wav",".flac",".m4a",".aac"}},
            {"压缩包", new[]{".zip",".7z",".rar",".gz",".tar"}},
            {"代码", new[]{".cs",".js",".ts",".py",".java",".cpp",".h",".html",".css",".sql",".ps1",".bat",".json",".xml"}},
            {"文本", new[]{".txt",".md",".log"}},
        };

        private static readonly string[] StopWords = { "的", "文件", "包含", "有关", "关于", "在", "里", "中", "找", "查找", "搜索", "一下", "那个", "这些", "所有", "修改", "最近" };

        public static DeskBox.Core.SearchQuery Parse(string text)
        {
            var q = new DeskBox.Core.SearchQuery();
            if (string.IsNullOrWhiteSpace(text)) return q;
            var raw = text.Trim();
            var work = raw;
            var lower = raw.ToLowerInvariant();
            var words = new List<string>();

            // 时间
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            long day = 24L * 3600 * 1000;
            if (lower.Contains("今天")) { q.ModifiedAfter = now - day; work = work.Replace("今天", " "); }
            else if (lower.Contains("昨天")) { q.ModifiedAfter = now - 2 * day; q.ModifiedBefore = now - day; work = work.Replace("昨天", " "); }
            else if (lower.Contains("本周") || lower.Contains("这周")) { q.ModifiedAfter = now - 7 * day; work = work.Replace("本周", " ").Replace("这周", " "); }
            else if (lower.Contains("上周")) { q.ModifiedAfter = now - 14 * day; q.ModifiedBefore = now - 7 * day; work = work.Replace("上周", " "); }
            else if (lower.Contains("上个月") || lower.Contains("上月")) { q.ModifiedAfter = now - 60 * day; q.ModifiedBefore = now - 30 * day; work = work.Replace("上个月", " ").Replace("上月", " "); }
            else if (lower.Contains("今年")) { q.ModifiedAfter = new DateTime(DateTime.Now.Year, 1, 1).ToUniversalTime().Ticks / 10000 - 62135596800000L; work = work.Replace("今年", " "); }
            else
            {
                var m = System.Text.RegularExpressions.Regex.Match(raw, @"最近\s*(\d+)\s*(天|日|周|月)");
                if (m.Success)
                {
                    int n = int.Parse(m.Groups[1].Value);
                    string unit = m.Groups[2].Value;
                    long span = unit == "月" ? n * 30 * day : unit == "周" ? n * 7 * day : n * day;
                    q.ModifiedAfter = now - span;
                    work = work.Replace(m.Value, " ");
                }
                else
                {
                    var md = System.Text.RegularExpressions.Regex.Match(raw, @"(mtime|修改时间)\s*[><]\s*(\d{4}-\d{1,2}-\d{1,2})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (md.Success)
                    {
                        DateTime dt;
                        if (DateTime.TryParse(md.Groups[2].Value, out dt))
                        {
                            long ts = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
                            if (md.Value.Contains(">")) q.ModifiedAfter = ts; else q.ModifiedBefore = ts;
                        }
                        work = work.Replace(md.Value, " ");
                    }
                }
            }
            if (lower.Contains("最近修改")) { if (q.ModifiedAfter == 0) q.ModifiedAfter = now - 7 * day; work = work.Replace("最近修改", " "); }
            if (lower.Contains("最新")) { if (q.ModifiedAfter == 0) q.ModifiedAfter = now - 7 * day; q.SortBy = "mtime"; q.SortDesc = true; work = work.Replace("最新", " "); }

            // 大小
            var sm = System.Text.RegularExpressions.Regex.Match(raw, @"(大于|超过|>|小于|不到|<|至少)\s*(\d+(?:\.\d+)?)\s*(kb|mb|gb|k|m|g|字节)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sm.Success)
            {
                double v = double.Parse(sm.Groups[2].Value);
                string unit = sm.Groups[3].Value.ToLowerInvariant();
                double mult = unit == "gb" || unit == "g" ? 1024.0 * 1024 * 1024 : unit == "mb" || unit == "m" ? 1024.0 * 1024 : unit == "kb" || unit == "k" ? 1024.0 : 1;
                long bytes = (long)(v * mult);
                string op = sm.Groups[1].Value;
                if (op == "大于" || op == "超过" || op == ">") q.MinSize = bytes;
                else if (op == "小于" || op == "不到" || op == "<") q.MaxSize = bytes;
                else q.MinSize = bytes;
                work = work.Replace(sm.Value, " ");
            }
            if (lower.Contains("大文件")) { q.MinSize = Math.Max(q.MinSize, 100L * 1024 * 1024); work = work.Replace("大文件", " "); }

            // 类型：长词优先，避免"表格"被"格"之类误伤
            foreach (var kv in ExtMap.OrderByDescending(k => k.Key.Length))
            {
                if (lower.Contains(kv.Key))
                {
                    foreach (var e in kv.Value) if (!q.Exts.Contains(e)) q.Exts.Add(e);
                    work = System.Text.RegularExpressions.Regex.Replace(work, System.Text.RegularExpressions.Regex.Escape(kv.Key), " ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                }
            }
            // 直接写的扩展名，如 "*.psd" / ".log"
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(raw, @"\.([a-z0-9]{2,5})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                var e = "." + m.Groups[1].Value.ToLowerInvariant();
                if (!q.Exts.Contains(e)) q.Exts.Add(e);
            }

            // 路径线索：「在 XXX 目录/文件夹下」
            var pm = System.Text.RegularExpressions.Regex.Match(raw, @"(?:在|位于)\s*([^，,。\s]{1,24}?)\s*(?:目录|文件夹|下|里面)");
            if (pm.Success)
            {
                q.PathContains = pm.Groups[1].Value;
                work = work.Replace(pm.Value, " ");
            }
            var pm2 = System.Text.RegularExpressions.Regex.Match(raw, @"(?:路径|path)\s*[:：]?\s*([A-Za-z]:\\[^\s，,。]{1,80})");
            if (pm2.Success)
            {
                q.PathContains = pm2.Groups[1].Value;
                work = work.Replace(pm2.Value, " ");
            }

            // 名称残句：去掉"包含/有关/的"等
            work = work.Replace("包含", " ").Replace("有关", " ").Replace("关于", " ").Replace("文件名", " ").Replace("名字", " ");
            foreach (var sw in StopWords) work = work.Replace(sw, " ");
            work = System.Text.RegularExpressions.Regex.Replace(work, @"[\*""'“”‘’，,。;；]+", " ");
            var tokens = work.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                             .Where(t => t.Length >= 1 && !t.All(char.IsPunctuation)).ToArray();
            if (tokens.Length > 0) q.Text = string.Join(" ", tokens);
            if (q.SortBy == "name" && q.ModifiedAfter > 0) { q.SortBy = "mtime"; q.SortDesc = true; }
            return q;
        }

        public static string Explain(DeskBox.Core.SearchQuery q)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(q.Text)) parts.Add("名称含「" + q.Text + "」");
            if (!string.IsNullOrEmpty(q.PathContains)) parts.Add("路径含「" + q.PathContains + "」");
            if (q.Exts.Count > 0) parts.Add("类型 " + string.Join("/", q.Exts.ToArray()));
            if (q.MinSize > 0) parts.Add("≥ " + LocalFmt.HumanSize(q.MinSize));
            if (q.MaxSize > 0) parts.Add("≤ " + LocalFmt.HumanSize(q.MaxSize));
            if (q.ModifiedAfter > 0) parts.Add("修改于 " + DateTimeOffset.FromUnixTimeMilliseconds(q.ModifiedAfter).LocalDateTime.ToString("yyyy-MM-dd") + " 之后");
            if (q.ModifiedBefore > 0) parts.Add("修改于 " + DateTimeOffset.FromUnixTimeMilliseconds(q.ModifiedBefore).LocalDateTime.ToString("yyyy-MM-dd") + " 之前");
            if (q.SortBy == "mtime") parts.Add("按修改时间" + (q.SortDesc ? "倒序" : "正序"));
            return parts.Count == 0 ? "无有效条件" : "理解为：" + string.Join("，", parts.ToArray());
        }
    }
}
