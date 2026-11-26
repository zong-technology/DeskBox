using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Fs
{
    /// <summary>
    /// 轻量的本地文件索引。只扫描显式配置的根目录，默认不扫全盘。
    /// Rebuild 在后台线程完成；建立完成后一次性替换内存快照并触发 Changed。
    /// </summary>
    public class FileIndex
    {
        public const int MaxEntryCount = 200000;
        private const long MaxIndexFileBytes = 50L * 1024 * 1024;

        private static readonly FileIndex _instance = new FileIndex();
        public static FileIndex Instance { get { return _instance; } }

        private readonly object _lock = new object();
        private List<FileEntry> _entries = new List<FileEntry>();
        private List<string> _roots = new List<string>();
        private List<string> _excludes = new List<string>();
        private string _state = "idle";
        private string _message = "";
        private bool _indexing = false;
        private int _skipped = 0;
        private int _configVersion = 0;
        private long _lastIndexedAt = 0;

        private FileIndex()
        {
            List<string> roots = DefaultRoots();
            List<string> excludes = DefaultExcludes();

            try
            {
                SearchRootsConfig cfg = Json.Load<SearchRootsConfig>(AppPaths.File("search-roots.json"), null);
                if (cfg != null)
                {
                    if (cfg.Roots != null) roots = NormalizeRoots(cfg.Roots);
                    if (cfg.Excludes != null) excludes = NormalizeExcludes(cfg.Excludes);
                }
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("warn", "fs.index", "读取 search-roots.json 失败: " + ex.Message);
            }

            _roots = roots;
            _excludes = excludes;
            TryLoadIndex();
        }

        public bool IsIndexing { get { lock (_lock) { return _indexing; } } }
        public string State { get { lock (_lock) { return _state; } } }
        public int Count { get { lock (_lock) { return _entries.Count; } } }
        public string Message { get { lock (_lock) { return _message; } } }
        public int SkippedCount { get { lock (_lock) { return _skipped; } } }
        public long LastIndexedAt { get { lock (_lock) { return _lastIndexedAt; } } }

        public List<string> Roots
        {
            get { lock (_lock) { return new List<string>(_roots); } }
        }

        public List<string> Excludes
        {
            get { lock (_lock) { return new List<string>(_excludes); } }
        }

        public event Action Changed;

        /// <summary>更新索引目录并持久化到 data\search-roots.json，不自动开始扫描。</summary>
        public void Configure(List<string> roots, List<string> excludes)
        {
            List<string> nr = roots == null ? DefaultRoots() : NormalizeRoots(roots);
            List<string> ne = excludes == null ? DefaultExcludes() : NormalizeExcludes(excludes);

            bool changed = false;
            lock (_lock)
            {
                changed = !SameList(_roots, nr) || !SameList(_excludes, ne);
                _roots = nr;
                _excludes = ne;
                if (changed)
                {
                    _configVersion++;
                    _entries = new List<FileEntry>();
                    _skipped = 0;
                    _lastIndexedAt = 0;
                    if (!_indexing)
                    {
                        _state = "idle";
                        _message = "索引配置已更新，请重建索引";
                    }
                    else
                    {
                        _message = "索引配置已更新，当前扫描结果将被丢弃";
                    }
                }
            }

            SaveConfig(nr, ne);
            if (changed) RaiseChanged();
        }

        /// <summary>后台重建索引；不阻塞调用线程。重复调用在构建期间会被忽略。</summary>
        public void Rebuild()
        {
            int version;
            lock (_lock)
            {
                if (_indexing) return;
                _indexing = true;
                _state = "indexing";
                _message = "正在建立索引…";
                _skipped = 0;
                version = _configVersion;
            }

            Thread thread = new Thread(delegate() { RebuildWorker(version); });
            thread.IsBackground = true;
            thread.Name = "DeskBox.FileIndex";
            try
            {
                thread.Start();
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _indexing = false;
                    _state = "idle";
                    _message = "无法启动索引线程: " + ex.Message;
                }
                DeskBox.Core.AppLog.Write("error", "fs.index", "无法启动索引线程: " + ex.Message);
                RaiseChanged();
            }
        }

        /// <summary>在内存快照中过滤、打分并排序。</summary>
        public SearchOutcome Search(SearchQuery q) { return Search(q, CancellationToken.None); }

        internal SearchOutcome Search(SearchQuery q, CancellationToken cancellation)
        {
            if (q == null) q = new SearchQuery();
            Stopwatch sw = Stopwatch.StartNew();

            List<FileEntry> snapshot;
            string state;
            string indexMessage;
            lock (_lock)
            {
                snapshot = new List<FileEntry>(_entries);
                state = _state;
                indexMessage = _message;
            }

            List<string> tokens = SplitTokens(q.Text);
            string fullText = (q.Text ?? "").Trim();
            HashSet<string> extSet = NormalizeExtSet(q.Exts);
            List<string> queryRoots = NormalizeRoots(q.Roots);
            string pathContains = (q.PathContains ?? "").Trim();
            string kind = (q.Kind ?? "").Trim().ToLowerInvariant();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            List<SearchHit> hits = new List<SearchHit>();
            int totalMatched = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                FileEntry e = snapshot[i];
                if (e == null || string.IsNullOrEmpty(e.Path)) continue;

                if (kind == "file" && e.IsDir) continue;
                if ((kind == "dir" || kind == "directory") && !e.IsDir) continue;
                if (extSet.Count > 0)
                {
                    if (e.IsDir || string.IsNullOrEmpty(e.Ext) || !extSet.Contains(e.Ext)) continue;
                }
                if (q.MinSize > 0 && e.Size < q.MinSize) continue;
                if (q.MaxSize > 0 && e.Size > q.MaxSize) continue;
                if (q.ModifiedAfter > 0 && e.Mtime < q.ModifiedAfter) continue;
                if (q.ModifiedBefore > 0 && e.Mtime > q.ModifiedBefore) continue;

                if (queryRoots.Count > 0 && !MatchesAnyRoot(e.Path, queryRoots)) continue;
                if (pathContains.Length > 0 &&
                    e.Path.IndexOf(pathContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (tokens.Count > 0)
                {
                    bool all = true;
                    for (int t = 0; t < tokens.Count; t++)
                    {
                        if (!ContainsIgnoreCase(e.Name, tokens[t]) &&
                            !ContainsIgnoreCase(e.Path, tokens[t]))
                        {
                            all = false;
                            break;
                        }
                    }
                    if (!all) continue;
                }

                if (q.PathFilter != null && !q.PathFilter(e.Path)) continue;

                int score;
                string highlight;
                ScoreEntry(e, fullText, tokens, extSet, q, now, out score, out highlight);
                SearchHit hit = new SearchHit();
                hit.Entry = e;
                hit.Score = score;
                hit.Highlight = highlight;
                hits.Add(hit);
                totalMatched++;
            }

            SortHits(hits, q);

            int limit = q.Limit <= 0 ? int.MaxValue : q.Limit;
            int shown = hits.Count;
            if (hits.Count > limit)
            {
                hits.RemoveRange(limit, hits.Count - limit);
                shown = limit;
            }

            SearchOutcome outcome = new SearchOutcome();
            outcome.Hits = hits;
            outcome.TotalMatched = totalMatched;
            outcome.IndexState = state;
            outcome.ElapsedMs = sw.ElapsedMilliseconds;

            string message = indexMessage;
            if (state == "indexing")
                message = "索引正在建立，当前显示的是上一次索引快照";
            else if (state == "idle" && snapshot.Count == 0)
                message = "索引尚未建立，请先重建索引";

            if (totalMatched > shown)
            {
                if (!string.IsNullOrEmpty(message)) message += "；";
                message += "共匹配 " + totalMatched + " 条，当前显示前 " + shown + " 条";
            }
            outcome.Message = message;

            return outcome;
        }

        /// <summary>直接读取目录内容，用于列表懒加载（不依赖索引）。</summary>
        public List<FileEntry> Children(string dir, int limit)
        {
            List<FileEntry> list = new List<FileEntry>();
            if (string.IsNullOrWhiteSpace(dir)) return list;

            string full;
            try { full = Path.GetFullPath(dir.Trim()); }
            catch { return list; }

            List<string> roots;
            lock (_lock) { roots = new List<string>(_roots); }

            string[] paths;
            try { paths = Directory.GetFileSystemEntries(full); }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("warn", "fs.index", "读取目录失败 " + full + ": " + ex.Message);
                return list;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                try
                {
                    FileAttributes attr = File.GetAttributes(paths[i]);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    bool isDir = (attr & FileAttributes.Directory) != 0;
                    string name = Path.GetFileName(paths[i]);
                    FileEntry e = BuildEntry(paths[i], name, isDir, PickRoot(paths[i], roots));
                    list.Add(e);
                }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("warn", "fs.index", "读取条目失败 " + paths[i] + ": " + ex.Message);
                }
            }

            list.Sort(delegate(FileEntry a, FileEntry b)
            {
                if (a.IsDir != b.IsDir) return a.IsDir ? -1 : 1;
                int c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });

            if (limit > 0 && list.Count > limit) list.RemoveRange(limit, list.Count - limit);
            return list;
        }

        /// <summary>启动器名称补全：前缀命中优先，其次名称包含。</summary>
        public List<string> Suggest(string prefix, int max)
        {
            List<string> result = new List<string>();
            if (max <= 0 || string.IsNullOrEmpty(prefix)) return result;
            string p = prefix.Trim();
            if (p.Length == 0) return result;

            List<FileEntry> snapshot;
            lock (_lock) { snapshot = new List<FileEntry>(_entries); }

            List<FileEntry> starts = new List<FileEntry>();
            List<FileEntry> contains = new List<FileEntry>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                FileEntry e = snapshot[i];
                if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                if (e.Name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) starts.Add(e);
                else if (e.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) contains.Add(e);
            }

            Comparison<FileEntry> cmp = delegate(FileEntry a, FileEntry b)
            {
                int c = a.Name.Length.CompareTo(b.Name.Length);
                if (c != 0) return c;
                c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            };
            starts.Sort(cmp);
            contains.Sort(cmp);

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddSuggestions(starts, result, seen, max);
            AddSuggestions(contains, result, seen, max);
            return result;
        }

        // -----------------------------------------------------------------
        // 索引构建
        // -----------------------------------------------------------------

        private void RebuildWorker(int version)
        {
            Stopwatch sw = Stopwatch.StartNew();
            List<string> roots;
            List<string> excludes;
            lock (_lock)
            {
                roots = new List<string>(_roots);
                excludes = new List<string>(_excludes);
            }

            List<FileEntry> built = new List<FileEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;
            bool truncated = false;
            string fatal = "";

            try
            {
                for (int i = 0; i < roots.Count; i++)
                {
                    if (built.Count >= MaxEntryCount) { truncated = true; break; }
                    string root = roots[i];
                    if (string.IsNullOrEmpty(root)) continue;
                    try
                    {
                        if (!Directory.Exists(root))
                        {
                            skipped++;
                            continue;
                        }
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }
                    ScanRoot(root, built, seen, excludes, ref skipped, ref truncated);
                }
            }
            catch (Exception ex)
            {
                fatal = ex.Message;
                DeskBox.Core.AppLog.Write("error", "fs.index", "建立索引失败: " + ex.Message);
            }

            sw.Stop();
            bool stale = false;
            string finalMessage;

            lock (_lock)
            {
                stale = _configVersion != version;
                if (!stale)
                {
                    if (fatal.Length > 0)
                    {
                        if (built.Count > 0) _entries = built;
                        finalMessage = "索引未完成: " + fatal;
                    }
                    else
                    {
                        _entries = built;
                        finalMessage = truncated
                            ? "索引已达到 " + MaxEntryCount + " 条上限，扫描提前停止"
                            : "已索引 " + built.Count + " 个条目";
                        if (skipped > 0) finalMessage += "，跳过 " + skipped + " 个无权限/不可访问项";
                        finalMessage += "，耗时 " + sw.ElapsedMilliseconds + " ms";
                        _lastIndexedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    }
                    _skipped = skipped;
                    _message = finalMessage;
                    _state = "ready";
                }
                else
                {
                    _indexing = false;
                    _state = "idle";
                    _message = "索引配置在扫描期间发生变化，本次结果已丢弃，请重新建立索引";
                    _skipped = skipped;
                }
                _indexing = false;
            }

            if (!stale)
            {
                bool tooLarge = SaveIndex(built, roots, excludes);
                if (tooLarge)
                {
                    lock (_lock)
                    {
                        _message = _message + "；索引文件超过 50MB，未写入磁盘";
                    }
                }
            }

            RaiseChanged();
        }

        private void ScanRoot(string root, List<FileEntry> output, HashSet<string> seen,
                              List<string> excludes, ref int skipped, ref bool truncated)
        {
            Stack<string> stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(dir))
                    {
                        if (output.Count >= MaxEntryCount)
                        {
                            truncated = true;
                            return;
                        }

                        string name;
                        try { name = Path.GetFileName(path); }
                        catch { name = ""; }

                        if (IsExcluded(path, name, excludes)) continue;
                        if (!seen.Add(path)) continue;

                        FileAttributes attr;
                        try { attr = File.GetAttributes(path); }
                        catch { skipped++; continue; }

                        if ((attr & FileAttributes.ReparsePoint) != 0) continue;

                        bool isDir = (attr & FileAttributes.Directory) != 0;
                        FileEntry entry;
                        try { entry = BuildEntry(path, name, isDir, root); }
                        catch { skipped++; continue; }

                        output.Add(entry);
                        if (output.Count >= MaxEntryCount)
                        {
                            truncated = true;
                            return;
                        }
                        if (isDir) stack.Push(path);
                    }
                }
                catch (UnauthorizedAccessException) { skipped++; }
                catch (DirectoryNotFoundException) { skipped++; }
                catch (PathTooLongException) { skipped++; }
                catch (IOException) { skipped++; }
                catch (Exception ex)
                {
                    skipped++;
                    DeskBox.Core.AppLog.Write("warn", "fs.index", "目录遍历失败 " + dir + ": " + ex.Message);
                }
            }
        }

        private static FileEntry BuildEntry(string path, string name, bool isDir, string root)
        {
            FileEntry e = new FileEntry();
            e.Path = path;
            e.Name = name;
            e.IsDir = isDir;
            e.Root = root ?? "";
            if (isDir)
            {
                e.Size = 0;
                e.Mtime = ToUnixMs(Directory.GetLastWriteTimeUtc(path));
                e.Ext = "";
            }
            else
            {
                FileInfo fi = new FileInfo(path);
                e.Size = fi.Length;
                e.Mtime = ToUnixMs(fi.LastWriteTimeUtc);
                e.Ext = Path.GetExtension(name) ?? "";
                e.Ext = e.Ext.ToLowerInvariant();
            }
            return e;
        }

        private static long ToUnixMs(DateTime utc)
        {
            try
            {
                if (utc.Kind == DateTimeKind.Local) utc = utc.ToUniversalTime();
                else if (utc.Kind == DateTimeKind.Unspecified) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
                return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
            }
            catch { return 0; }
        }

        // -----------------------------------------------------------------
        // 搜索辅助
        // -----------------------------------------------------------------

        private static void SortHits(List<SearchHit> hits, SearchQuery q)
        {
            string sortBy = (q.SortBy ?? "").Trim().ToLowerInvariant();
            Comparison<SearchHit> primary;

            if (sortBy == "size")
            {
                primary = delegate(SearchHit a, SearchHit b) { return a.Entry.Size.CompareTo(b.Entry.Size); };
            }
            else if (sortBy == "mtime" || sortBy == "modified")
            {
                primary = delegate(SearchHit a, SearchHit b) { return a.Entry.Mtime.CompareTo(b.Entry.Mtime); };
            }
            else if (sortBy == "path")
            {
                primary = delegate(SearchHit a, SearchHit b)
                {
                    return string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase);
                };
            }
            else if (sortBy == "score" || sortBy == "relevance")
            {
                primary = delegate(SearchHit a, SearchHit b) { return b.Score.CompareTo(a.Score); };
            }
            else
            {
                primary = delegate(SearchHit a, SearchHit b)
                {
                    return string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
                };
            }

            hits.Sort(delegate(SearchHit a, SearchHit b)
            {
                int c = primary(a, b);
                if (c == 0)
                    c = string.Compare(a.Entry.Name, b.Entry.Name, StringComparison.OrdinalIgnoreCase);
                if (c == 0)
                    c = string.Compare(a.Entry.Path, b.Entry.Path, StringComparison.OrdinalIgnoreCase);
                if (q.SortDesc) c = -c;
                return c;
            });
        }

        private static void ScoreEntry(FileEntry e, string fullText, List<string> tokens,
                                       HashSet<string> extSet, SearchQuery q, long now,
                                       out int score, out string highlight)
        {
            string name = e.Name ?? "";
            string path = e.Path ?? "";

            if (tokens.Count == 0)
            {
                score = 40;
                highlight = FilterHighlight(e, q);
            }
            else if (string.Equals(name, fullText, StringComparison.OrdinalIgnoreCase))
            {
                score = 100;
                highlight = "名称完全匹配 “" + fullText + "”";
            }
            else if (name.StartsWith(fullText, StringComparison.OrdinalIgnoreCase))
            {
                score = 90;
                highlight = "名称以 “" + fullText + "” 开头";
            }
            else if (name.IndexOf(fullText, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score = 70;
                highlight = "名称包含 “" + fullText + "”";
            }
            else
            {
                bool allInName = true;
                string firstNameToken = "";
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (name.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (firstNameToken.Length == 0) firstNameToken = tokens[i];
                    }
                    else
                    {
                        allInName = false;
                        break;
                    }
                }

                if (allInName)
                {
                    score = 70;
                    highlight = "名称包含 “" + firstNameToken + "”";
                }
                else
                {
                    string pathToken = "";
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        if (path.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            pathToken = tokens[i];
                            break;
                        }
                    }
                    score = 40;
                    highlight = "路径包含 “" + pathToken + "”";
                }
            }

            if (e.IsDir) score += 1;
            else
            {
                score += 2;
                if (extSet.Count > 0) score += 2;
            }

            if (e.Mtime > 0)
            {
                long age = now - e.Mtime;
                if (age < 0) age = 0;
                const long day = 24L * 60 * 60 * 1000;
                if (age <= 7 * day) score += 3;
                else if (age <= 30 * day) score += 2;
                else if (age <= 365 * day) score += 1;
            }
        }

        private static string FilterHighlight(FileEntry e, SearchQuery q)
        {
            if (q.Exts != null && q.Exts.Count > 0)
            {
                List<string> parts = new List<string>();
                for (int i = 0; i < q.Exts.Count; i++)
                {
                    string x = (q.Exts[i] ?? "").Trim();
                    if (x.Length > 0) parts.Add(x);
                }
                if (parts.Count > 0) return "扩展名 " + string.Join(" / ", parts.ToArray());
            }
            if (q.MinSize > 0 || q.MaxSize > 0) return "大小符合筛选条件";
            if (q.ModifiedAfter > 0 || q.ModifiedBefore > 0) return "修改时间符合筛选条件";
            if (!string.IsNullOrEmpty(q.PathContains)) return "路径包含 “" + q.PathContains.Trim() + "”";
            if (!string.IsNullOrEmpty(q.Kind)) return e.IsDir ? "类型：文件夹" : "类型：文件";
            return "匹配筛选条件";
        }

        private static List<string> SplitTokens(string text)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                    if (start >= 0)
                    {
                        list.Add(text.Substring(start, i - start));
                        start = -1;
                    }
                }
                else if (start < 0) start = i;
            }
            if (start >= 0) list.Add(text.Substring(start));
            return list;
        }

        private static HashSet<string> NormalizeExtSet(List<string> exts)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (exts == null) return set;
            for (int i = 0; i < exts.Count; i++)
            {
                string x = (exts[i] ?? "").Trim().ToLowerInvariant();
                if (x.Length == 0) continue;
                if (x[0] != '.') x = "." + x;
                set.Add(x);
            }
            return set;
        }

        private static bool ContainsIgnoreCase(string value, string token)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) return false;
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddSuggestions(List<FileEntry> source, List<string> result,
                                           HashSet<string> seen, int max)
        {
            for (int i = 0; i < source.Count && result.Count < max; i++)
            {
                string path = source[i].Path;
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                result.Add(path);
            }
        }

        // -----------------------------------------------------------------
        // 配置
        // -----------------------------------------------------------------

        private void SaveConfig(List<string> roots, List<string> excludes)
        {
            try
            {
                SearchRootsConfig cfg = new SearchRootsConfig();
                cfg.Roots = new List<string>(roots);
                cfg.Excludes = new List<string>(excludes);
                Json.Save(AppPaths.File("search-roots.json"), cfg);
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("error", "fs.index", "保存 search-roots.json 失败: " + ex.Message);
            }
        }

        private static List<string> DefaultRoots()
        {
            List<string> list = new List<string>();
            AddExisting(list, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddExisting(list, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) AddExisting(list, Path.Combine(profile, "Downloads"));
            AddExisting(list, @"D:\桌面");

            return NormalizeRoots(list);
        }

        private static List<string> DefaultExcludes()
        {
            List<string> list = new List<string>();
            list.Add("$RECYCLE.BIN");
            list.Add("System Volume Information");
            list.Add("node_modules");
            list.Add(".git");
            list.Add(".svn");
            list.Add(".hg");
            list.Add(@"AppData\Local\Temp");
            list.Add(@"Windows\Temp");
            list.Add("$WinREAgent");
            list.Add("Recovery");
            return NormalizeExcludes(list);
        }

        private static void AddExisting(List<string> list, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (Directory.Exists(path)) list.Add(path);
            }
            catch { }
        }

        private static List<string> NormalizeRoots(List<string> roots)
        {
            List<string> list = new List<string>();
            if (roots == null) return list;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < roots.Count; i++)
            {
                string p = NormalizeRoot(roots[i]);
                if (p.Length == 0 || !seen.Add(p)) continue;
                list.Add(p);
            }
            return list;
        }

        private static string NormalizeRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "";
            string p = root.Trim().Trim('"');
            if (p.Length == 0) return "";
            try { p = Path.GetFullPath(p); }
            catch { return ""; }

            string rootPart = Path.GetPathRoot(p) ?? "";
            while (p.Length > rootPart.Length &&
                   (p[p.Length - 1] == Path.DirectorySeparatorChar ||
                    p[p.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                p = p.Substring(0, p.Length - 1);
            }
            return p;
        }

        private static List<string> NormalizeExcludes(List<string> excludes)
        {
            List<string> list = new List<string>();
            if (excludes == null) return list;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < excludes.Count; i++)
            {
                string x = (excludes[i] ?? "").Trim().Trim('"');
                if (x.Length == 0) continue;
                x = x.Replace('/', '\\').TrimEnd('\\');
                if (x.Length == 0 || !seen.Add(x)) continue;
                list.Add(x);
            }
            return list;
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        // -----------------------------------------------------------------
        // 路径与排除
        // -----------------------------------------------------------------

        internal static bool IsExcluded(string fullPath, string name, List<string> excludes)
        {
            if (excludes == null || excludes.Count == 0) return false;
            for (int i = 0; i < excludes.Count; i++)
            {
                string ex = excludes[i];
                if (ex.IndexOf('\\') < 0)
                {
                    if (string.Equals(name, ex, StringComparison.OrdinalIgnoreCase)) return true;
                    continue;
                }

                if (Path.IsPathRooted(ex))
                {
                    if (IsUnder(fullPath, ex)) return true;
                }
                else
                {
                    string probe = "\\" + (fullPath ?? "").TrimEnd('\\') + "\\";
                    string needle = "\\" + ex.Trim('\\') + "\\";
                    if (probe.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return true;
            string prefix = root.TrimEnd('\\');
            if (prefix.Length == 0) return false;
            prefix += "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesAnyRoot(string path, List<string> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                if (IsUnder(path, roots[i])) return true;
            }
            return false;
        }

        private static string PickRoot(string path, List<string> roots)
        {
            string best = "";
            if (roots == null) return best;
            for (int i = 0; i < roots.Count; i++)
            {
                if (IsUnder(path, roots[i]) && roots[i].Length > best.Length) best = roots[i];
            }
            return best;
        }

        // -----------------------------------------------------------------
        // 索引落盘
        // -----------------------------------------------------------------

        private bool TryLoadIndex()
        {
            try
            {
                string path = AppPaths.File("index.json");
                if (!File.Exists(path)) return false;
                FileInfo fi = new FileInfo(path);
                if (fi.Length > MaxIndexFileBytes) return false;

                SearchIndexFile disk = Json.Load<SearchIndexFile>(path, null);
                if (disk == null || disk.Version != 1 || disk.Entries == null) return false;
                if (!SameList(disk.Roots, _roots) || !SameList(disk.Excludes, _excludes)) return false;

                List<FileEntry> loaded = new List<FileEntry>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < disk.Entries.Count && loaded.Count < MaxEntryCount; i++)
                {
                    SearchIndexRow row = disk.Entries[i];
                    if (row == null || string.IsNullOrEmpty(row.P)) continue;
                    if (!seen.Add(row.P)) continue;

                    FileEntry e = new FileEntry();
                    e.Path = row.P;
                    e.Name = Path.GetFileName(row.P);
                    e.IsDir = row.D != 0;
                    e.Size = row.S < 0 ? 0 : row.S;
                    e.Mtime = row.T;
                    e.Ext = e.IsDir ? "" : (Path.GetExtension(e.Name) ?? "").ToLowerInvariant();
                    if (row.R >= 0 && row.R < _roots.Count) e.Root = _roots[row.R];
                    else e.Root = PickRoot(e.Path, _roots);
                    loaded.Add(e);
                }

                lock (_lock)
                {
                    _entries = loaded;
                    _state = "ready";
                    _message = "已从磁盘载入 " + loaded.Count + " 个条目";
                }
                return true;
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("warn", "fs.index", "读取 index.json 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>写索引文件。返回 true 表示因超过 50MB 而跳过。</summary>
        private bool SaveIndex(List<FileEntry> entries, List<string> roots, List<string> excludes)
        {
            try
            {
                SearchIndexFile disk = new SearchIndexFile();
                disk.Version = 1;
                disk.SavedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                disk.Roots = new List<string>(roots);
                disk.Excludes = new List<string>(excludes);
                disk.Entries = new List<SearchIndexRow>(entries.Count);

                Dictionary<string, int> rootIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < roots.Count; i++) rootIndex[roots[i]] = i;

                for (int i = 0; i < entries.Count; i++)
                {
                    FileEntry e = entries[i];
                    SearchIndexRow row = new SearchIndexRow();
                    row.P = e.Path;
                    row.S = e.Size;
                    row.T = e.Mtime;
                    row.D = e.IsDir ? 1 : 0;
                    int ri;
                    row.R = (!string.IsNullOrEmpty(e.Root) && rootIndex.TryGetValue(e.Root, out ri)) ? ri : -1;
                    disk.Entries.Add(row);
                }

                string json = Json.ToJson(disk);
                if (Encoding.UTF8.GetByteCount(json) > MaxIndexFileBytes) return true;

                string path = AppPaths.File("index.json");
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                return false;
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("error", "fs.index", "保存 index.json 失败: " + ex.Message);
                return false;
            }
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null) return;
            Delegate[] list = handler.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try { ((Action)list[i])(); }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("error", "fs.index", "Changed 订阅者异常: " + ex.Message);
                }
            }
        }
    }

    /// <summary>search-roots.json 的结构。</summary>
    public class SearchRootsConfig
    {
        public List<string> Roots = new List<string>();
        public List<string> Excludes = new List<string>();
    }

    /// <summary>index.json 的紧凑结构，只保存重建 FileEntry 所需字段。</summary>
    public class SearchIndexFile
    {
        public int Version = 1;
        public long SavedAt = 0;
        public List<string> Roots = new List<string>();
        public List<string> Excludes = new List<string>();
        public List<SearchIndexRow> Entries = new List<SearchIndexRow>();
    }

    public class SearchIndexRow
    {
        public string P = "";
        public long S = 0;
        public long T = 0;
        public int D = 0;
        public int R = -1;
    }
}
