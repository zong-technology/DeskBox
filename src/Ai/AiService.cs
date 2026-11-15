using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using DeskBox.Core;

namespace DeskBox.Ai
{
    public class AiRequest
    {
        public string Kind = "";              // summarize | compare | report | organize | nlsearch
        public string Text = "";
        public List<string> Paths = new List<string>();
        public string Extra = "";
    }

    public class AiAnswer
    {
        public bool Ok = true;
        public string Title = "";
        public string Text = "";
        public List<string> Bullets = new List<string>();
        public List<KeyValuePair<string, string>> Meta = new List<KeyValuePair<string, string>>();
        public string Provider = "";
        public string Error = "";
    }

    public interface IAiProvider
    {
        string Name { get; }
        bool Available { get; }
        AiAnswer Ask(AiRequest r);
    }

    /// <summary>本地规则引擎：所有结论都来自真实读取与统计，绝不联网。</summary>
    public class LocalAiProvider : IAiProvider
    {
        public string Name { get { return "本地"; } }
        public bool Available { get { return true; } }

        private static readonly string[] Stop = { "的", "了", "和", "是", "在", "我", "有", "就", "不", "人", "都", "一", "一个", "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有", "看", "好", "自己", "这", "and", "the", "for", "with", "that", "this", "from" };

        public AiAnswer Ask(AiRequest r)
        {
            var a = new AiAnswer { Provider = Name };
            try
            {
                if (r == null) { a.Ok = false; a.Error = "空请求"; return a; }
                switch ((r.Kind ?? "").ToLowerInvariant())
                {
                    case "summarize": Summarize(r, a); break;
                    case "compare": Compare(r, a); break;
                    case "report": Report(r, a); break;
                    case "organize": Organize(r, a); break;
                    case "nlsearch":
                        var q = NlQuery.Parse(r.Text);
                        a.Title = "已解析搜索条件";
                        a.Text = NlQuery.Explain(q);
                        break;
                    default:
                        a.Ok = false; a.Error = "不支持的请求类型: " + r.Kind;
                        break;
                }
            }
            catch (Exception ex)
            {
                a.Ok = false;
                a.Error = ex.Message;
            }
            return a;
        }

        private static string ReadText(string path, int maxBytes)
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException("文件不存在", path);
            if (new[] { ".pdf", ".docx", ".xlsx", ".xlsm", ".vsdx" }.Contains(fi.Extension.ToLowerInvariant()))
            {
                var document = DocumentSkills.Run(new Dictionary<string, string> { { "op", "read" }, { "path", path }, { "maxChars", Math.Min(200000, maxBytes).ToString() } });
                if (!document.Ok) throw new IOException(document.Error);
                return document.Text + (document.Truncated ? "\n[摘要输入已截断，不能代表完整文档]" : "");
            }
            if (new[] { ".doc", ".xls", ".vsd" }.Contains(fi.Extension.ToLowerInvariant())) throw new IOException("旧格式请先转换为 DOCX/XLSX/VSDX");
            byte[] buf;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int len = (int)Math.Min(maxBytes, fs.Length);
                buf = new byte[len];
                int off = 0;
                while (off < len)
                {
                    int n = fs.Read(buf, off, len - off);
                    if (n <= 0) break;
                    off += n;
                }
                if (off < len) Array.Resize(ref buf, off);
            }
            // BOM / UTF-8 校验 → GBK 回退
            if (buf.Length >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF)
                return Encoding.UTF8.GetString(buf, 3, buf.Length - 3);
            try
            {
                return new UTF8Encoding(false, true).GetString(buf);
            }
            catch
            {
                try { return Encoding.GetEncoding(936).GetString(buf); }
                catch { return Encoding.UTF8.GetString(buf); }
            }
        }

        private void Summarize(AiRequest r, AiAnswer a)
        {
            var path = r.Paths.FirstOrDefault();
            if (string.IsNullOrEmpty(path)) { a.Ok = false; a.Error = "未选择文件"; return; }
            a.Title = "文档摘要 · " + Path.GetFileName(path);
            string text = ReadText(path, 1024 * 1024);
            int chars = text.Length;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            int nonEmpty = lines.Count(l => l.Trim().Length > 0);
            int paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries).Length;

            // 高频词
            var freq = new Dictionary<string, int>();
            var sb = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else
                {
                    var w = sb.ToString();
                    sb.Length = 0;
                    if (w.Length >= 2 && w.Length <= 12 && !Stop.Contains(w))
                    {
                        int c;
                        freq.TryGetValue(w, out c);
                        freq[w] = c + 1;
                    }
                }
            }
            var top = freq.OrderByDescending(kv => kv.Value).Take(8).ToList();
            var sentences = text.Split(new[] { '。', '！', '？', '\n', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim()).Where(s => s.Length >= 8).Take(3).ToList();

            a.Meta.Add(new KeyValuePair<string, string>("字符数", chars.ToString("N0")));
            a.Meta.Add(new KeyValuePair<string, string>("行数", lines.Length.ToString("N0")));
            a.Meta.Add(new KeyValuePair<string, string>("非空行", nonEmpty.ToString("N0")));
            a.Meta.Add(new KeyValuePair<string, string>("段落", paragraphs.ToString("N0")));
            a.Meta.Add(new KeyValuePair<string, string>("大小", new FileInfo(path).Length + " B"));
            if (top.Count > 0)
                a.Bullets.Add("高频词：" + string.Join("、", top.Select(kv => kv.Key + "(" + kv.Value + ")").ToArray()));
            foreach (var s in sentences) a.Bullets.Add("摘录：" + (s.Length > 60 ? s.Substring(0, 60) + "…" : s));
            if (text.Contains(",") || text.Contains("\t"))
            {
                var firstLine = lines.FirstOrDefault(l => l.Trim().Length > 0) ?? "";
                int cols = (firstLine.Count(c => c == ',') + (firstLine.Contains("\t") ? firstLine.Count(c => c == '\t') : 0)) + 1;
                a.Bullets.Add("看起来是表格文本：约 " + cols + " 列 × " + nonEmpty + " 行");
            }
            a.Text = chars == 0 ? "文件为空。" : "共 " + chars.ToString("N0") + " 个字符，" + nonEmpty + " 行有效内容。";
        }

        private void Compare(AiRequest r, AiAnswer a)
        {
            if (r.Paths.Count < 2) { a.Ok = false; a.Error = "需要选择 2 个文件"; return; }
            var p1 = r.Paths[0];
            var p2 = r.Paths[1];
            string t1 = ReadText(p1, 2 * 1024 * 1024);
            string t2 = ReadText(p2, 2 * 1024 * 1024);
            var l1 = t1.Replace("\r\n", "\n").Split('\n');
            var l2 = t2.Replace("\r\n", "\n").Split('\n');
            var set1 = new HashSet<string>(l1.Where(l => l.Trim().Length > 0));
            var set2 = new HashSet<string>(l2.Where(l => l.Trim().Length > 0));
            int same = set1.Count(s => set2.Contains(s));
            int only1 = set1.Count(s => !set2.Contains(s));
            int only2 = set2.Count(s => !set1.Contains(s));
            int total = same + only1 + only2;
            double sim = total == 0 ? 100 : same * 100.0 / total;

            a.Title = "对比 · " + Path.GetFileName(p1) + " ↔ " + Path.GetFileName(p2);
            a.Meta.Add(new KeyValuePair<string, string>(Path.GetFileName(p1), new FileInfo(p1).Length + " B / " + l1.Length + " 行"));
            a.Meta.Add(new KeyValuePair<string, string>(Path.GetFileName(p2), new FileInfo(p2).Length + " B / " + l2.Length + " 行"));
            a.Meta.Add(new KeyValuePair<string, string>("相似度", sim.ToString("0.0") + "%"));
            a.Bullets.Add("相同行：" + same + "，仅文件 A：" + only1 + "，仅文件 B：" + only2);
            foreach (var s in set1.Where(s => !set2.Contains(s)).Take(5)) a.Bullets.Add("仅 A：" + Cut(s, 50));
            foreach (var s in set2.Where(s => !set1.Contains(s)).Take(5)) a.Bullets.Add("仅 B：" + Cut(s, 50));
            a.Text = sim > 95 ? "两份文件内容高度一致。" : sim > 60 ? "两份文件有明显重叠，但也存在差异。" : "两份文件差异较大。";
        }

        private static string Cut(string s, int n) { s = s.Trim(); return s.Length <= n ? s : s.Substring(0, n) + "…"; }

        private void Report(AiRequest r, AiAnswer a)
        {
            var paths = r.Paths ?? new List<string>();
            var infos = new List<FileInfo>();
            foreach (var p in paths)
            {
                try { var fi = new FileInfo(p); if (fi.Exists) infos.Add(fi); } catch { }
            }
            if (infos.Count == 0) { a.Ok = false; a.Error = "没有可用文件（路径为空或不存在）"; return; }
            a.Title = "工作总结 · " + infos.Count + " 个文件";
            var byExt = infos.GroupBy(f => f.Extension.ToLowerInvariant()).OrderByDescending(g => g.Count()).Take(5).ToList();
            long total = infos.Sum(f => f.Length);
            int recent7 = infos.Count(f => f.LastWriteTime > DateTime.Now.AddDays(-7));
            a.Meta.Add(new KeyValuePair<string, string>("文件数", infos.Count.ToString()));
            a.Meta.Add(new KeyValuePair<string, string>("总大小", UiKitShim.HumanSize(total)));
            a.Meta.Add(new KeyValuePair<string, string>("最近 7 天修改", recent7 + " 个"));
            a.Bullets.Add("类型分布：" + string.Join("、", byExt.Select(g => (g.Key == "" ? "无扩展名" : g.Key) + "×" + g.Count()).ToArray()));
            foreach (var f in infos.OrderByDescending(f => f.Length).Take(3))
                a.Bullets.Add("最大文件：" + f.Name + "（" + UiKitShim.HumanSize(f.Length) + "）");
            a.Text = "这批文件共 " + UiKitShim.HumanSize(total) + "，" + recent7 + " 个在最近一周内修改过。";
        }

        private void Organize(AiRequest r, AiAnswer a)
        {
            List<FileEntry> entries = r != null && r.Paths != null
                ? r.Paths.Select(p => new FileInfo(p)).Where(fi => fi.Exists).Select(fi => new FileEntry
                {
                    Path = fi.FullName, Name = fi.Name, Ext = fi.Extension.ToLowerInvariant(),
                    Size = fi.Length, Mtime = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds()
                }).ToList()
                : new List<FileEntry>();
            if (entries.Count == 0) { a.Ok = false; a.Error = "没有可分析的条目"; return; }
            a.Title = "整理建议";
            var groups = entries.GroupBy(e => e.Ext).OrderByDescending(g => g.Count()).Take(5);
            foreach (var g in groups)
            {
                if (g.Count() >= 3)
                    a.Bullets.Add("把 " + g.Count() + " 个 " + (g.Key == "" ? "无扩展名" : g.Key) + " 归入「" + CategoryName(g.Key) + "」分类");
            }
            var big = entries.Where(e => e.Size > 200L * 1024 * 1024).ToList();
            if (big.Count > 0) a.Bullets.Add(big.Count + " 个 200MB+ 文件建议归档或移到备份盘");
            long old = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
            var stale = entries.Where(e => e.Mtime > 0 && e.Mtime < old).ToList();
            if (stale.Count > 0) a.Bullets.Add(stale.Count + " 个文件超过 30 天未修改，可考虑归档");
            var dupNames = entries.GroupBy(e => e.Name.ToLowerInvariant()).Where(g => g.Count() > 1).ToList();
            if (dupNames.Count > 0) a.Bullets.Add(dupNames.Count + " 组同名文件，建议做重复检查");
            if (a.Bullets.Count == 0) a.Bullets.Add("当前条目分布比较均匀，暂无强整理建议");
            a.Text = "基于 " + entries.Count + " 个条目的扩展名/大小/时间统计得出。" ;
        }

        private static string CategoryName(string ext)
        {
            switch (ext)
            {
                case ".xlsx": case ".xls": case ".csv": return "表格";
                case ".doc": case ".docx": return "文档";
                case ".pdf": return "PDF";
                case ".png": case ".jpg": case ".jpeg": case ".gif": case ".bmp": return "图片";
                case ".mp4": case ".mkv": case ".avi": return "视频";
                case ".zip": case ".7z": case ".rar": return "压缩包";
                case ".ps1": case ".bat": case ".cmd": return "脚本";
                default: return "其他";
            }
        }
    }

    /// <summary>避免 UI 依赖 Core：这里只借一个最小格式化函数。</summary>
    internal static class UiKitShim
    {
        public static string HumanSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024L * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.#") + " MB";
            return (bytes / 1024.0 / 1024 / 1024).ToString("0.##") + " GB";
        }
    }

    /// <summary>可选远程 AI（OpenAI 兼容）。默认关闭，必须由用户在设置里显式启用。</summary>
    public class RemoteAiProvider : IAiProvider
    {
        private readonly string _endpoint, _apiKey, _model;
        public RemoteAiProvider(string endpoint, string apiKey, string model)
        {
            _endpoint = endpoint; _apiKey = apiKey; _model = model;
        }

        public string Name { get { return "远程"; } }
        public bool Available { get { return !string.IsNullOrWhiteSpace(_endpoint) && !string.IsNullOrWhiteSpace(_apiKey); } }

        public AiAnswer Ask(AiRequest r)
        {
            var a = new AiAnswer { Provider = Name };
            try
            {
                var prompt = "你是文件整理助手。请用中文简洁回答：" + (r.Text ?? "");
                var payload = "{\"model\":\"" + Escape(_model) + "\",\"messages\":[{\"role\":\"user\",\"content\":\"" + Escape(prompt) + "\"}],\"temperature\":0.3}";
                var req = (HttpWebRequest)WebRequest.Create(_endpoint);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 15000;
                req.Headers.Add("Authorization", "Bearer " + _apiKey);
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var body = sr.ReadToEnd();
                    var obj = Json.FromJson<Dictionary<string, object>>(body);
                    object choicesObj;
                    if (obj != null && obj.TryGetValue("choices", out choicesObj) && choicesObj is object[])
                    {
                        var arr = (object[])choicesObj;
                        if (arr.Length > 0)
                        {
                            var first = arr[0] as Dictionary<string, object>;
                            object msgObj;
                            if (first != null && first.TryGetValue("message", out msgObj))
                            {
                                var msg = msgObj as Dictionary<string, object>;
                                object contentObj;
                                if (msg != null && msg.TryGetValue("content", out contentObj))
                                {
                                    a.Text = Convert.ToString(contentObj);
                                    a.Title = "远程回答";
                                    return a;
                                }
                            }
                        }
                    }
                }
                a.Ok = false;
                a.Error = "远程返回格式无法解析";
            }
            catch (Exception ex)
            {
                a.Ok = false;
                a.Error = ex.Message;
            }
            return a;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    /// <summary>AI 门面：默认本地；远程需显式配置并逐次确认（确认由 UI 负责）。</summary>
    public class AiService
    {
        private static readonly AiService _instance = new AiService();
        public static AiService Instance { get { return _instance; } }

        private readonly LocalAiProvider _local = new LocalAiProvider();
        private RemoteAiProvider _remote;
        private bool _remoteEnabled;

        public string ActiveProvider { get { return _remoteEnabled && _remote != null && _remote.Available ? "远程" : "本地"; } }
        public bool RemoteEnabled { get { return _remoteEnabled; } }

        public List<string> Capabilities
        {
            get
            {
                return new List<string>
                {
                    "文档摘要（本地统计）", "文件对比（行级差异）", "工作总结（批量统计）",
                    "整理建议（规则分析）", "自然语言搜索（本地解析）"
                };
            }
        }

        public void Configure(string endpoint, string apiKey, string model, bool enabled)
        {
            _remote = new RemoteAiProvider(endpoint, apiKey, model);
            _remoteEnabled = enabled;
            try
            {
                var cfg = new Dictionary<string, string>();
                cfg["endpoint"] = endpoint ?? "";
                cfg["apiKey"] = apiKey ?? "";
                cfg["model"] = model ?? "";
                cfg["enabled"] = enabled ? "1" : "0";
                Json.Save(Path.Combine(AppPaths.DataDir, "ai.json"), cfg);
            }
            catch { }
        }

        public void LoadConfig()
        {
            try
            {
                var cfg = Json.Load<Dictionary<string, string>>(Path.Combine(AppPaths.DataDir, "ai.json"), null);
                if (cfg == null) return;
                string ep, key, model, en;
                cfg.TryGetValue("endpoint", out ep);
                cfg.TryGetValue("apiKey", out key);
                cfg.TryGetValue("model", out model);
                cfg.TryGetValue("enabled", out en);
                _remote = new RemoteAiProvider(ep, key, model);
                _remoteEnabled = en == "1";
            }
            catch { }
        }

        public AiAnswer Ask(AiRequest r)
        {
            if (_remoteEnabled && _remote != null && _remote.Available)
            {
                var ans = _remote.Ask(r);
                if (ans.Ok) return ans;
                var local = _local.Ask(r);
                local.Meta.Add(new KeyValuePair<string, string>("提示", "远程不可用（" + ans.Error + "），已使用本地结果"));
                return local;
            }
            return _local.Ask(r);
        }
    }
}
