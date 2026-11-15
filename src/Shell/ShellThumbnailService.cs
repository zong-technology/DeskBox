using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using DeskBox.Native;

namespace DeskBox.Shell
{
    /// <summary>缩略图来源：真实 Shell 缩略图 / SHGetFileInfo 图标回退 / 无结果（负缓存）。</summary>
    public enum ThumbnailSource
    {
        None = 0,
        Shell = 1,
        Icon = 2
    }

    /// <summary>一次缩略图查询结果（值快照，调用方可长期持有）。</summary>
    public sealed class ThumbnailInfo
    {
        public string Path = "";
        public int RequestedSize;
        public int Width;
        public int Height;
        public ThumbnailSource Source = ThumbnailSource.None;
        public bool IsFallback { get { return Source == ThumbnailSource.Icon; } }
        public bool HasImage { get { return Source != ThumbnailSource.None; } }
    }

    /// <summary>服务计数器（快照读取，线程安全）。</summary>
    public sealed class ThumbnailStats
    {
        internal long RequestedCount;
        internal long DeduplicatedCount;
        internal long DroppedCount;
        internal long ExtractedCount;
        internal long FallbackCount;
        internal long FailedCount;
        internal long CacheHitCount;
        internal long EvictionCount;
        internal long TimeoutCount;
        internal long SkippedUnsafeCount;
        internal int PendingCount;
        internal int CachedCount;
        internal int ActiveExtractorCount;
        internal int PeakExtractorCount;

        /// <summary>真正入队的请求数（去重/命中/丢弃的不算）。</summary>
        public long Requested { get { return Interlocked.Read(ref RequestedCount); } }
        /// <summary>同一路径+尺寸在排队/提取期间被重复请求的次数。</summary>
        public long Deduplicated { get { return Interlocked.Read(ref DeduplicatedCount); } }
        /// <summary>队列满后丢弃的请求数。</summary>
        public long Dropped { get { return Interlocked.Read(ref DroppedCount); } }
        /// <summary>成功拿到真实 Shell 缩略图的次数。</summary>
        public long Extracted { get { return Interlocked.Read(ref ExtractedCount); } }
        /// <summary>回退到 SHGetFileInfo 图标的次数。</summary>
        public long Fallback { get { return Interlocked.Read(ref FallbackCount); } }
        /// <summary>真实缩略图失败的次数（含随后回退成功）。</summary>
        public long Failed { get { return Interlocked.Read(ref FailedCount); } }
        /// <summary>缓存命中次数（含绘制命中）。</summary>
        public long CacheHits { get { return Interlocked.Read(ref CacheHitCount); } }
        /// <summary>LRU 淘汰次数。</summary>
        public long Evictions { get { return Interlocked.Read(ref EvictionCount); } }
        /// <summary>超时放弃的提取次数。</summary>
        public long Timeouts { get { return Interlocked.Read(ref TimeoutCount); } }
        /// <summary>当前待处理（含正在提取）条目数。</summary>
        public int Pending { get { return Interlocked.CompareExchange(ref PendingCount, 0, 0); } }
        /// <summary>当前缓存条目数。</summary>
        public int Cached { get { return Interlocked.CompareExchange(ref CachedCount, 0, 0); } }
        public long SkippedUnsafe { get { return Interlocked.Read(ref SkippedUnsafeCount); } }
        public int ActiveExtractors { get { return Interlocked.CompareExchange(ref ActiveExtractorCount, 0, 0); } }
        public int PeakActiveExtractors { get { return Interlocked.CompareExchange(ref PeakExtractorCount, 0, 0); } }
    }

    /// <summary>
    /// Shell 缩略图抽象。实现约定：
    /// Request 永不阻塞、永不抛异常；结果通过 Ready 事件通知（后台线程），
    /// TryDraw/TryPeek 在 UI 线程同步读取缓存；实例由创建者 Dispose。
    /// </summary>
    public interface IThumbnailService : IDisposable
    {
        bool IsDisposed { get; }
        ThumbnailStats Stats { get; }
        /// <summary>缩略图或回退图标就绪（后台线程触发）。</summary>
        event Action<string, int> Ready;
        /// <summary>请求缩略图；已缓存/已排队/队列满时返回 false。</summary>
        bool Request(string path, int size, bool isDirectory);
        bool Request(string path, int size, bool isDirectory, long version);
        /// <summary>只查缓存，不触发提取。</summary>
        bool TryPeek(string path, int size, out ThumbnailInfo info);
        bool TryPeek(string path, int size, long version, out ThumbnailInfo info);
        /// <summary>仅当缓存里有图时绘制（调用方持有 Graphics）；绘制失败返回 false。</summary>
        bool TryDraw(Graphics g, string path, int size, Rectangle dest);
        bool TryDraw(Graphics g, string path, int size, long version, Rectangle dest);
        /// <summary>丢弃不再显示的路径，控制内存。</summary>
        void Prune(IEnumerable<string> alivePaths);
        void Invalidate(string path);
        void Clear();
    }

    /// <summary>哪些路径值得走缩略图、尺寸归一化、缓存键生成。</summary>
    public static class ThumbnailPolicy
    {
        /// <summary>Grid 模式下启用缩略图的最小图标尺寸。</summary>
        public const int MinIconSize = 64;
        public const int MinSize = 32;
        public const int MaxSize = 256;
        public const int DefaultSize = 96;

        private static readonly HashSet<string> ThumbnailExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 图片
            ".png", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif",
            // 文档 / 表格 / 演示
            ".pdf", ".doc", ".docx", ".docm", ".dot", ".dotx", ".rtf",
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".csv",
            ".ppt", ".pptx", ".pptm", ".pps", ".ppsx",
            ".vsd", ".vsdx", ".vsdm", ".one", ".msg",
            // 视频
            ".mp4", ".mov", ".avi", ".wmv", ".mkv", ".m4v", ".webm"
        };

        /// <summary>该路径是否值得请求缩略图。不访问磁盘（UI 线程安全）。</summary>
        public static bool ShouldUseThumbnail(string path, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (isDirectory) return false;                   // 文件夹/快捷方式直接使用真实 Shell 图标，避免枚举与慢 handler
            var ext = "";
            try { ext = Path.GetExtension(path) ?? ""; } catch { return false; }
            return ext.Length > 0 && ThumbnailExts.Contains(ext);
        }

        public static int NormalizeSize(int size)
        {
            if (size < MinSize) return MinSize;
            if (size > MaxSize) return MaxSize;
            return size;
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var full = Path.GetFullPath(path.Trim());
                return full.Length == 0 ? null : full;
            }
            catch
            {
                return path.Trim();
            }
        }

        internal static string Key(string path, int size)
        {
            return Key(path, size, 0);
        }

        internal static string Key(string path, int size, long version)
        {
            return size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + version.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (NormalizePath(path) ?? "");
        }
    }

    /// <summary>
    /// 真实 Windows Shell 缩略图服务：
    /// - 优先 IShellItemImageFactory.GetImage（图片/PDF/Office/快捷方式都能拿到预览）；
    /// - 失败回退 SHGetFileInfo 图标；
    /// - 有界后台队列 + 单工作线程 + 有界 LRU 缓存（按条数与像素预算双重限制）；
    /// - 每个请求带超时（网络/OneDrive 占位/权限失败不会永久卡死或崩溃）；
    /// - Bitmap/Icon 由服务统一 Dispose，调用方不得释放。
    /// </summary>
    public sealed class ShellThumbnailService : IThumbnailService
    {
        private sealed class WorkItem
        {
            public string Path = "";
            public string Key = "";
            public int Size;
            public bool IsDirectory;
            public long Version;
        }

        private sealed class CacheEntry
        {
            public string Key = "";
            public string Path = "";
            public string NormalizedPath = "";
            public int Size;
            public long Version;
            public Bitmap Thumbnail;
            public Icon FallbackIcon;
            public ThumbnailSource Source = ThumbnailSource.None;
            public int CreatedTick;
            public LinkedListNode<string> Node;
        }

        private sealed class ExtractBox
        {
            public CacheEntry Entry;
            public bool Done;
            public bool Abandoned;
        }

        private readonly int _capacity;
        private readonly long _pixelBudget;
        private readonly int _maxQueue;
        private readonly int _timeoutMs;
        private readonly int _negativeRetryMs;

        private readonly object _cacheLock = new object();
        private readonly Dictionary<string, CacheEntry> _cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _lru = new LinkedList<string>();
        private long _cachedPixels;

        private readonly object _queueLock = new object();
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly HashSet<string> _pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Thread _worker;
        private bool _shutdown;
        private readonly HashSet<string> _alivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _latestVersions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private bool _hasAliveFilter;
        private int _activeExtractors;
        private const int MaxTimedExtractors = 2;

        private int _disposed;
        private readonly ThumbnailStats _stats = new ThumbnailStats();

        public event Action<string, int> Ready;

        public ShellThumbnailService() : this(240, 128, 8000) { }

        public ShellThumbnailService(int capacity, int maxQueue, int timeoutMs)
            : this(capacity, maxQueue, timeoutMs, 64L * 1024 * 1024)
        {
        }

        public ShellThumbnailService(int capacity, int maxQueue, int timeoutMs, long pixelBudget)
        {
            _capacity = capacity < 1 ? 1 : capacity;
            _maxQueue = maxQueue < 1 ? 1 : maxQueue;
            _timeoutMs = timeoutMs < 250 ? 250 : timeoutMs;
            _pixelBudget = pixelBudget < 1024 * 1024 ? 1024 * 1024 : pixelBudget;
            _negativeRetryMs = 30000;
            _worker = new Thread(WorkerLoop);
            _worker.IsBackground = true;
            _worker.Name = "DeskBox-ThumbnailQueue";
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        public int Capacity { get { return _capacity; } }
        public int MaxQueue { get { return _maxQueue; } }
        public int TimeoutMs { get { return _timeoutMs; } }
        public ThumbnailStats Stats { get { return _stats; } }
        public bool IsDisposed { get { return Volatile.Read(ref _disposed) != 0; } }

        // ---------------- 公开 API ----------------

        public bool Request(string path, int size, bool isDirectory)
        {
            return Request(path, size, isDirectory, 0);
        }

        public bool Request(string path, int size, bool isDirectory, long version)
        {
            if (Volatile.Read(ref _disposed) != 0) return false;
            if (!ThumbnailPolicy.ShouldUseThumbnail(path, isDirectory)) return false;
            var normalized = ThumbnailPolicy.NormalizePath(path);
            if (normalized == null) return false;
            var key = ThumbnailPolicy.Key(normalized, ThumbnailPolicy.NormalizeSize(size), version);

            bool stale = false;
            lock (_cacheLock)
            {
                CacheEntry existing;
                if (_cache.TryGetValue(key, out existing))
                {
                    if (existing.Source != ThumbnailSource.None) return false;              // 已缓存
                    if (unchecked((uint)(Environment.TickCount - existing.CreatedTick)) < (uint)_negativeRetryMs) return false; // 负缓存期内不重试
                    stale = true;                                                          // 负缓存过期，重新排队
                }
            }
            if (stale) Remove(key);

            lock (_queueLock)
            {
                if (Volatile.Read(ref _disposed) != 0) return false;
                if (_pending.Contains(key))
                {
                    Interlocked.Increment(ref _stats.DeduplicatedCount);
                    return false;
                }
                if (_queue.Count >= _maxQueue)
                {
                    Interlocked.Increment(ref _stats.DroppedCount);
                    return false;
                }
                _latestVersions[normalized] = version;
                _pending.Add(key);
                _queue.Enqueue(new WorkItem
                {
                    Path = normalized,
                    Key = key,
                    Size = ThumbnailPolicy.NormalizeSize(size),
                    IsDirectory = isDirectory,
                    Version = version
                });
                Interlocked.Exchange(ref _stats.PendingCount, _pending.Count);
                Interlocked.Increment(ref _stats.RequestedCount);
                Monitor.Pulse(_queueLock);
                return true;
            }
        }

        public bool TryPeek(string path, int size, out ThumbnailInfo info)
        {
            return TryPeek(path, size, 0, out info);
        }

        public bool TryPeek(string path, int size, long version, out ThumbnailInfo info)
        {
            info = null;
            if (Volatile.Read(ref _disposed) != 0) return false;
            var key = ThumbnailPolicy.Key(path, ThumbnailPolicy.NormalizeSize(size), version);
            lock (_cacheLock)
            {
                CacheEntry e;
                if (!_cache.TryGetValue(key, out e)) return false;
                TouchLocked(e);
                Interlocked.Increment(ref _stats.CacheHitCount);
                info = new ThumbnailInfo
                {
                    Path = e.Path,
                    RequestedSize = e.Size,
                    Width = e.Thumbnail != null ? e.Thumbnail.Width : (e.FallbackIcon != null ? e.FallbackIcon.Width : 0),
                    Height = e.Thumbnail != null ? e.Thumbnail.Height : (e.FallbackIcon != null ? e.FallbackIcon.Height : 0),
                    Source = e.Source
                };
                return true;
            }
        }

        public bool TryDraw(Graphics g, string path, int size, Rectangle dest)
        {
            return TryDraw(g, path, size, 0, dest);
        }

        public bool TryDraw(Graphics g, string path, int size, long version, Rectangle dest)
        {
            if (g == null || dest.Width <= 0 || dest.Height <= 0) return false;
            if (Volatile.Read(ref _disposed) != 0) return false;
            var key = ThumbnailPolicy.Key(path, ThumbnailPolicy.NormalizeSize(size), version);
            lock (_cacheLock)
            {
                CacheEntry e;
                if (!_cache.TryGetValue(key, out e)) return false;
                TouchLocked(e);
                Interlocked.Increment(ref _stats.CacheHitCount);
                try
                {
                    if (e.Thumbnail != null)
                    {
                        var fit = Fit(e.Thumbnail.Width, e.Thumbnail.Height, dest);
                        var old = g.InterpolationMode;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        try { g.DrawImage(e.Thumbnail, fit); } finally { g.InterpolationMode = old; }
                        return true;
                    }
                    if (e.FallbackIcon != null)
                    {
                        var s = Math.Min(dest.Width, dest.Height);
                        g.DrawIcon(e.FallbackIcon, new Rectangle(dest.X + (dest.Width - s) / 2, dest.Y + (dest.Height - s) / 2, s, s));
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public void Prune(IEnumerable<string> alivePaths)
        {
            if (Volatile.Read(ref _disposed) != 0 || alivePaths == null) return;
            var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in alivePaths)
            {
                var n = ThumbnailPolicy.NormalizePath(p);
                if (n != null) alive.Add(n);
            }
            Dictionary<string, long> versions;
            lock (_queueLock)
            {
                _hasAliveFilter = true;
                _alivePaths.Clear();
                foreach (var path in alive) _alivePaths.Add(path);
                foreach (var path in _latestVersions.Keys.Where(x => !alive.Contains(x)).ToList()) _latestVersions.Remove(path);
                if (_queue.Count > 0)
                {
                    var kept = _queue.Where(x => alive.Contains(x.Path)).ToList();
                    _queue.Clear();
                    _pending.Clear();
                    foreach (var item in kept) { _queue.Enqueue(item); _pending.Add(item.Key); }
                    Interlocked.Exchange(ref _stats.PendingCount, _pending.Count);
                }
                versions = new Dictionary<string, long>(_latestVersions, StringComparer.OrdinalIgnoreCase);
            }
            List<string> dead = null;
            lock (_cacheLock)
            {
                foreach (var kv in _cache)
                    if (!alive.Contains(kv.Value.NormalizedPath) || (versions.ContainsKey(kv.Value.NormalizedPath) && versions[kv.Value.NormalizedPath] != kv.Value.Version)) { if (dead == null) dead = new List<string>(); dead.Add(kv.Key); }
                if (dead == null) return;
                foreach (var k in dead) RemoveLocked(k);
            }
        }

        public void Invalidate(string path)
        {
            var normalized = ThumbnailPolicy.NormalizePath(path);
            if (normalized == null) return;
            lock (_cacheLock)
            {
                var keys = _cache.Where(x => string.Equals(x.Value.NormalizedPath, normalized, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList();
                foreach (var key in keys) RemoveLocked(key);
            }
            lock (_queueLock) _latestVersions[normalized] = long.MinValue;
        }

        public void Clear()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            lock (_cacheLock)
            {
                var keys = new List<string>(_cache.Keys);
                foreach (var k in keys) RemoveLocked(k);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_queueLock)
            {
                _shutdown = true;
                _queue.Clear();
                _pending.Clear();
                Interlocked.Exchange(ref _stats.PendingCount, 0);
                Monitor.PulseAll(_queueLock);
            }
            try { if (_worker != null) _worker.Join(1500); } catch { }
            lock (_cacheLock)
            {
                foreach (var e in _cache.Values) DisposeEntry(e);
                _cache.Clear();
                _lru.Clear();
                _cachedPixels = 0;
                Interlocked.Exchange(ref _stats.CachedCount, 0);
            }
            Ready = null;
        }

        // ---------------- 工作线程 ----------------

        private void WorkerLoop()
        {
            while (true)
            {
                WorkItem item = null;
                lock (_queueLock)
                {
                    while (!_shutdown && _queue.Count == 0) Monitor.Wait(_queueLock, 500);
                    if (_shutdown) return;
                    if (_queue.Count == 0) continue;
                    item = _queue.Dequeue();
                }
                if (item == null) continue;
                try { Process(item); }
                catch (Exception ex) { AppLog.Write("warn", "thumb", "缩略图任务异常 " + item.Path + "：" + ex.Message); }
                finally
                {
                    lock (_queueLock)
                    {
                        _pending.Remove(item.Key);
                        Interlocked.Exchange(ref _stats.PendingCount, _pending.Count);
                    }
                }
            }
        }

        private void Process(WorkItem item)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (!IsAlive(item.Path)) return;
            CacheEntry entry;
            lock (_cacheLock)
            {
                CacheEntry hit;
                if (_cache.TryGetValue(item.Key, out hit) && hit.Source != ThumbnailSource.None) return;
            }

            entry = ExtractWithTimeout(item);
            if (!IsAlive(item.Path) || !IsCurrentVersion(item)) { DisposeEntry(entry); return; }
            if (entry == null)
                entry = new CacheEntry
                {
                    Key = item.Key, Path = item.Path, NormalizedPath = item.Path,
                    Size = item.Size, Version = item.Version, Source = ThumbnailSource.None
                };

            bool published = false;
            lock (_cacheLock)
            {
                if (Volatile.Read(ref _disposed) == 0 && !_cache.ContainsKey(item.Key))
                {
                    entry.CreatedTick = Environment.TickCount;
                    entry.Node = _lru.AddFirst(item.Key);
                    _cache[item.Key] = entry;
                    _cachedPixels += Pixels(entry);
                    TrimLocked();
                    published = true;
                }
            }
            if (!published) { DisposeEntry(entry); return; }

            if (entry.Source != ThumbnailSource.None) RaiseReady(item.Path, item.Size);
        }

        /// <summary>在独立 STA 线程上提取并等待，超时则放弃（不让网络共享/占位文件卡死队列）。</summary>
        private CacheEntry ExtractWithTimeout(WorkItem item)
        {
            int active = Interlocked.Increment(ref _activeExtractors);
            Interlocked.Exchange(ref _stats.ActiveExtractorCount, active);
            UpdatePeak(active);
            if (active > MaxTimedExtractors)
            {
                int remaining = Interlocked.Decrement(ref _activeExtractors);
                Interlocked.Exchange(ref _stats.ActiveExtractorCount, remaining);
                Interlocked.Increment(ref _stats.SkippedUnsafeCount);
                return null;
            }
            var box = new ExtractBox();
            var t = new Thread(delegate ()
            {
                try { box.Entry = Extract(item); }
                catch (Exception ex) { AppLog.Write("warn", "thumb", "缩略图提取失败 " + item.Path + "：" + ex.Message); }
                finally
                {
                    lock (box)
                    {
                        box.Done = true;
                        if (box.Abandoned && box.Entry != null) { DisposeEntry(box.Entry); box.Entry = null; }
                        int remaining = Interlocked.Decrement(ref _activeExtractors);
                        Interlocked.Exchange(ref _stats.ActiveExtractorCount, remaining);
                        Monitor.Pulse(box);
                    }
                }
            });
            t.IsBackground = true;
            t.Name = "DeskBox-ThumbnailExtract";
            try { t.SetApartmentState(ApartmentState.STA); } catch { }
            try { t.Start(); }
            catch { int remaining = Interlocked.Decrement(ref _activeExtractors); Interlocked.Exchange(ref _stats.ActiveExtractorCount, remaining); return null; }

            lock (box)
            {
                if (!box.Done) Monitor.Wait(box, _timeoutMs);
                if (!box.Done)
                {
                    box.Abandoned = true;
                    Interlocked.Increment(ref _stats.TimeoutCount);
                    AppLog.Write("warn", "thumb", "缩略图提取超时（" + _timeoutMs + "ms），已跳过：" + item.Path);
                    return null;   // 放弃等待；后台线程结束时自行释放结果
                }
                return box.Entry;
            }
        }

        private CacheEntry Extract(WorkItem item)
        {
            ThumbnailPathRisk risk = ThumbnailPathSafety.Classify(item.Path);
            bool isDir = item.IsDirectory || (risk == ThumbnailPathRisk.None && SafeDirectoryExists(item.Path));
            if (risk != ThumbnailPathRisk.None)
            {
                Interlocked.Increment(ref _stats.SkippedUnsafeCount);
                Icon safeIcon = TrySafeTypeIcon(item.Path, isDir);
                if (safeIcon != null) Interlocked.Increment(ref _stats.FallbackCount);
                return new CacheEntry { Key = item.Key, Path = item.Path, NormalizedPath = item.Path, Size = item.Size, FallbackIcon = safeIcon, Source = safeIcon != null ? ThumbnailSource.Icon : ThumbnailSource.None };
            }

            Bitmap thumb = null;
            try { thumb = ShellImages.Extract(item.Path, item.Size); }
            catch (Exception ex) { AppLog.Write("warn", "thumb", "Shell 缩略图异常 " + item.Path + "：" + ex.Message); }

            if (thumb != null)
            {
                Interlocked.Increment(ref _stats.ExtractedCount);
                return new CacheEntry
                {
                    Key = item.Key, Path = item.Path, NormalizedPath = item.Path,
                    Size = item.Size, Version = item.Version, Thumbnail = thumb, Source = ThumbnailSource.Shell
                };
            }

            Interlocked.Increment(ref _stats.FailedCount);
            Icon icon = TryFallbackIcon(item.Path, isDir);
            if (icon != null) Interlocked.Increment(ref _stats.FallbackCount);
            return new CacheEntry
            {
                Key = item.Key, Path = item.Path, NormalizedPath = item.Path,
                Size = item.Size, Version = item.Version, FallbackIcon = icon,
                Source = icon != null ? ThumbnailSource.Icon : ThumbnailSource.None
            };
        }

        private Icon TryFallbackIcon(string path, bool isDirectory)
        {
            try
            {
                if (path == null) return null;
                bool exists = isDirectory ? SafeDirectoryExists(path) : SafeFileExists(path);
                if (isDirectory && !exists) return Win32.GetShellIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows), true, false);
                var icon = exists ? Win32.GetShellIcon(path, true, false) : Win32.GetShellIcon(path, true, true);
                if (icon == null && isDirectory) icon = Win32.GetShellIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows), true, false);
                return icon;
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "thumb", "回退图标失败 " + path + "：" + ex.Message);
                return null;
            }
        }

        private static Icon TrySafeTypeIcon(string path, bool isDirectory)
        {
            try
            {
                if (isDirectory) return Win32.GetShellIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows), true, false);
                string extension = "";
                try { extension = Path.GetExtension(path) ?? ""; } catch { }
                return Win32.GetShellIcon("thumbnail-placeholder" + extension, true, true);
            }
            catch { return null; }
        }

        private static bool SafeFileExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try { return Directory.Exists(path); } catch { return false; }
        }

        private bool IsAlive(string path)
        {
            lock (_queueLock) return !_hasAliveFilter || _alivePaths.Contains(path);
        }

        private bool IsCurrentVersion(WorkItem item)
        {
            lock (_queueLock)
            {
                long version;
                return !_latestVersions.TryGetValue(item.Path, out version) || version == item.Version;
            }
        }

        private void UpdatePeak(int value)
        {
            while (true)
            {
                int current = Interlocked.CompareExchange(ref _stats.PeakExtractorCount, 0, 0);
                if (value <= current || Interlocked.CompareExchange(ref _stats.PeakExtractorCount, value, current) == current) return;
            }
        }

        // ---------------- 缓存 ----------------

        private static long Pixels(CacheEntry e)
        {
            return e.Thumbnail == null ? 0 : (long)e.Thumbnail.Width * e.Thumbnail.Height;
        }

        private void TouchLocked(CacheEntry e)
        {
            if (e.Node == null || e.Node.List == null) return;
            try
            {
                _lru.Remove(e.Node);
                _lru.AddFirst(e.Node);
            }
            catch { }
        }

        private void TrimLocked()
        {
            while ((_cache.Count > _capacity || _cachedPixels > _pixelBudget) && _lru.Last != null)
            {
                RemoveLocked(_lru.Last.Value);
                Interlocked.Increment(ref _stats.EvictionCount);
            }
            Interlocked.Exchange(ref _stats.CachedCount, _cache.Count);
        }

        private void Remove(string key)
        {
            lock (_cacheLock) { RemoveLocked(key); }
        }

        private void RemoveLocked(string key)
        {
            CacheEntry e;
            if (!_cache.TryGetValue(key, out e)) return;
            _cache.Remove(key);
            if (e.Node != null && e.Node.List != null)
            {
                try { _lru.Remove(e.Node); } catch { }
                e.Node = null;
            }
            _cachedPixels -= Pixels(e);
            if (_cachedPixels < 0) _cachedPixels = 0;
            DisposeEntry(e);
            Interlocked.Exchange(ref _stats.CachedCount, _cache.Count);
        }

        private static void DisposeEntry(CacheEntry e)
        {
            if (e == null) return;
            if (e.Thumbnail != null) { try { e.Thumbnail.Dispose(); } catch { } e.Thumbnail = null; }
            if (e.FallbackIcon != null) { try { e.FallbackIcon.Dispose(); } catch { } e.FallbackIcon = null; }
        }

        private static Rectangle Fit(int w, int h, Rectangle dest)
        {
            if (w <= 0 || h <= 0) return dest;
            double scale = Math.Min((double)dest.Width / w, (double)dest.Height / h);
            int tw = Math.Max(1, (int)Math.Round(w * scale));
            int th = Math.Max(1, (int)Math.Round(h * scale));
            return new Rectangle(dest.X + (dest.Width - tw) / 2, dest.Y + (dest.Height - th) / 2, tw, th);
        }

        private void RaiseReady(string path, int size)
        {
            var h = Ready;
            if (h == null) return;
            try { h(path, size); }
            catch (Exception ex) { AppLog.Write("warn", "thumb", "缩略图通知处理异常 " + path + "：" + ex.Message); }
        }
    }

    internal enum ThumbnailPathRisk { None, Missing, Network, CloudPlaceholder, Invalid }

    internal static class ThumbnailPathSafety
    {
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;
        private const uint FILE_ATTRIBUTE_OFFLINE = 0x00001000;
        private const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
        private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
        private const uint DRIVE_REMOTE = 4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributes(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string rootPathName);

        internal static ThumbnailPathRisk Classify(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return ThumbnailPathRisk.Invalid;
            try
            {
                if (path.StartsWith(@"\\", StringComparison.Ordinal)) return ThumbnailPathRisk.Network;
                var root = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(root) && GetDriveType(root) == DRIVE_REMOTE) return ThumbnailPathRisk.Network;
                uint attributes = GetFileAttributes(path);
                if (attributes == INVALID_FILE_ATTRIBUTES) return ThumbnailPathRisk.Missing;
                if ((attributes & (FILE_ATTRIBUTE_OFFLINE | FILE_ATTRIBUTE_RECALL_ON_OPEN | FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS)) != 0)
                    return ThumbnailPathRisk.CloudPlaceholder;
                return ThumbnailPathRisk.None;
            }
            catch { return ThumbnailPathRisk.Invalid; }
        }
    }

    /// <summary>IShellItemImageFactory 原生封装（图片/PDF/Office/快捷方式的系统预览）。</summary>
    internal static class ShellImages
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        internal enum SIIGBF
        {
            RESIZETOFIT = 0x00,
            BIGGERSIZEOK = 0x01,
            MEMORYONLY = 0x02,
            ICONONLY = 0x04,
            THUMBNAILONLY = 0x08
        }

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            public int bmiColors;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetObject(IntPtr hObject, int cbSize, ref BITMAP lpObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BITMAPINFO lpbmi, uint usage);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, uint dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        private const uint COINIT_APARTMENTTHREADED = 0x2;

        /// <summary>取真实 Shell 缩略图；没有缩略图/失败返回 null（调用方回退图标）。</summary>
        internal static Bitmap Extract(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            int hrInit = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                IShellItemImageFactory factory;
                int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
                if (hr != 0 || factory == null) return null;
                try
                {
                    var sz = new SIZE { cx = size, cy = size };
                    IntPtr hbm;
                    hr = factory.GetImage(sz, SIIGBF.BIGGERSIZEOK | SIIGBF.THUMBNAILONLY, out hbm);
                    if (hr != 0 || hbm == IntPtr.Zero) return null;
                    try { return BitmapFromHBitmap(hbm); }
                    finally { DeleteObject(hbm); }
                }
                catch (Exception ex)
                {
                    AppLog.Write("warn", "thumb", "GetImage 失败 " + path + "：" + ex.Message);
                    return null;
                }
                finally
                {
                    try { Marshal.ReleaseComObject(factory); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "thumb", "Shell 解析失败 " + path + "：" + ex.Message);
                return null;
            }
            finally
            {
                if (hrInit >= 0) { try { CoUninitialize(); } catch { } }
            }
        }

        /// <summary>HBITMAP → 32bpp ARGB Bitmap（保留 alpha；全透明则补不透明）。</summary>
        internal static Bitmap BitmapFromHBitmap(IntPtr hbitmap)
        {
            if (hbitmap == IntPtr.Zero) return null;
            var bm = new BITMAP();
            if (GetObject(hbitmap, Marshal.SizeOf(typeof(BITMAP)), ref bm) == 0) return null;
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0 || (long)w * h > 64L * 1024 * 1024) return null;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w;
            bmi.bmiHeader.biHeight = -h;   // 负数 = 自上而下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            var buffer = new byte[w * h * 4];
            IntPtr hdc = GetDC(IntPtr.Zero);
            int lines;
            try { lines = GetDIBits(hdc, hbitmap, 0, (uint)h, buffer, ref bmi, 0); }
            finally { if (hdc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, hdc); }
            if (lines == 0) return FallbackFromHBitmap(hbitmap);

            bool anyAlpha = false;
            for (int i = 3; i < buffer.Length; i += 4) { if (buffer[i] != 0) { anyAlpha = true; break; } }

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            try
            {
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    if (anyAlpha)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            IntPtr row = (IntPtr)((long)data.Scan0 + (long)y * data.Stride);
                            Marshal.Copy(buffer, y * w * 4, row, w * 4);
                        }
                    }
                    else
                    {
                        var row = new byte[w * 4];
                        for (int y = 0; y < h; y++)
                        {
                            int src = y * w * 4;
                            for (int x = 0; x < w; x++)
                            {
                                row[x * 4] = buffer[src + x * 4];
                                row[x * 4 + 1] = buffer[src + x * 4 + 1];
                                row[x * 4 + 2] = buffer[src + x * 4 + 2];
                                row[x * 4 + 3] = 255;
                            }
                            Marshal.Copy(row, 0, (IntPtr)((long)data.Scan0 + (long)y * data.Stride), w * 4);
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                return FallbackFromHBitmap(hbitmap);
            }
        }

        private static Bitmap FallbackFromHBitmap(IntPtr hbitmap)
        {
            try
            {
                using (var raw = Image.FromHbitmap(hbitmap))
                    return new Bitmap(raw);
            }
            catch { return null; }
        }
    }
}
