using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DeskBox.Shell
{
    /// <summary>Debounced FileSystemWatcher set. Overflow triggers the same safe refresh callback.</summary>
    public class DesktopWatcher : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly object _lock = new object();
        private Timer _debounce;
        private bool _disposed;
        public event Action Changed;

        public void Watch(IEnumerable<string> directories)
        {
            DisposeWatchers();
            foreach (var dir in (directories ?? Enumerable.Empty<string>()).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var w = new FileSystemWatcher(dir)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        InternalBufferSize = 32 * 1024,
                        EnableRaisingEvents = true
                    };
                    w.Created += OnChange; w.Deleted += OnChange; w.Changed += OnChange; w.Renamed += OnRename; w.Error += OnError;
                    _watchers.Add(w);
                }
                catch (Exception ex) { AppLog.Write("warn", "file", "文件监视启动失败 " + dir + "：" + ex.Message); }
            }
        }

        private void OnChange(object sender, FileSystemEventArgs e) { Schedule(); }
        private void OnRename(object sender, RenamedEventArgs e) { Schedule(); }
        private void OnError(object sender, ErrorEventArgs e) { AppLog.Write("warn", "file", "文件监视缓冲区异常，将执行安全刷新"); Schedule(); }

        private void Schedule()
        {
            lock (_lock)
            {
                if (_disposed) return;
                if (_debounce != null) _debounce.Dispose();
                _debounce = new Timer(delegate { var h = Changed; if (h != null) h(); }, null, 350, Timeout.Infinite);
            }
        }

        private void DisposeWatchers()
        {
            foreach (var w in _watchers) try { w.Dispose(); } catch { }
            _watchers.Clear();
        }

        public void Dispose()
        {
            lock (_lock) { _disposed = true; if (_debounce != null) { _debounce.Dispose(); _debounce = null; } }
            DisposeWatchers();
        }
    }
}
