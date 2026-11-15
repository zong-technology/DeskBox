using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Fs
{
    /// <summary>
    /// 文件操作历史：追加到 data\history.jsonl（UTF-8 无 BOM，一行一条 JSON），同时写结构化日志。
    /// 只做追加 + 尾部查询，不重写整个文件，避免大文件抖动。
    /// </summary>
    public class FileHistory
    {
        private readonly Store _store;

        public FileHistory(Store store)
        {
            _store = store;
        }

        private static string HistoryPath
        {
            get { return Path.Combine(Store.DataDir, "history.jsonl"); }
        }

        /// <summary>追加一条记录。字段缺失时自动补 Id / Ts。</summary>
        public void Record(FileOpRecord r)
        {
            if (r == null) return;
            if (string.IsNullOrEmpty(r.Id)) r.Id = "h_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            if (r.Ts <= 0) r.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.IsNullOrEmpty(r.Op)) r.Op = "file";

            try { Json.AppendLine(HistoryPath, r); }
            catch { /* 历史失败不影响主流程 */ }

            string msg = (r.Ok ? "" : "失败 ") + r.Op;
            if (!string.IsNullOrEmpty(r.Source)) msg += " | " + r.Source;
            if (!string.IsNullOrEmpty(r.Target)) msg += " → " + r.Target;
            if (!string.IsNullOrEmpty(r.Detail)) msg += " | " + r.Detail;
            Log(r.Ok ? "info" : "warn", "file-" + r.Op, msg);
        }

        /// <summary>按条件查询（新的在前）。limit&lt;=0 时按 100 处理。</summary>
        public List<FileOpRecord> Query(int limit, string op, string keyword)
        {
            if (limit <= 0) limit = 100;
            int scan = Math.Max(limit * 25, 1000);
            if (scan > 50000) scan = 50000;

            var result = new List<FileOpRecord>();
            var all = Json.ReadLines<FileOpRecord>(HistoryPath, scan);
            string opFilter = op == null ? "" : op.Trim();
            string kw = keyword == null ? "" : keyword.Trim();

            foreach (var r in all)
            {
                if (r == null) continue;
                if (opFilter.Length > 0 && !string.Equals(r.Op, opFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (kw.Length > 0 && !Matches(r, kw)) continue;
                result.Add(r);
            }

            result.Sort((a, b) => b.Ts.CompareTo(a.Ts));
            if (result.Count > limit) result.RemoveRange(limit, result.Count - limit);
            return result;
        }

        private static bool Matches(FileOpRecord r, string keyword)
        {
            return Contains(r.Source, keyword)
                || Contains(r.Target, keyword)
                || Contains(r.Detail, keyword)
                || Contains(r.Op, keyword);
        }

        private static bool Contains(string value, string keyword)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>清空历史（只清 history.jsonl，不动日志）。</summary>
        public void Clear()
        {
            try
            {
                Directory.CreateDirectory(Store.DataDir);
                File.WriteAllText(HistoryPath, "", new UTF8Encoding(false));
                Log("info", "file-history", "已清空文件操作历史");
            }
            catch (Exception ex)
            {
                Log("error", "file-history", "清空历史失败：" + ex.Message);
            }
        }

        private void Log(string level, string action, string message)
        {
            try
            {
                if (_store != null) _store.Log(level, action, message);
                else DeskBox.Core.AppLog.Write(level, action, message);
            }
            catch { }
        }
    }
}
