using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Core;

namespace DeskBox.Sys
{
    /// <summary>
    /// 剪贴板历史：事件驱动（AddClipboardFormatListener + 隐藏 NativeWindow 处理 WM_CLIPBOARDUPDATE），不轮询。
    /// 支持文本与文件列表；敏感内容（密码/令牌/卡号/私钥等）只留「已屏蔽」占位，不写磁盘。
    /// 线程安全：History 由 lock 保护；Changed 在创建隐藏窗口的线程（通常是 UI 线程）触发，
    /// 若订阅方在其他线程，请自行 BeginInvoke —— 这里不引用任何 Form。
    /// Start()/Stop() 必须在同一线程（默认 UI 线程）调用，且该线程要有消息循环。
    /// </summary>
    public class ClipboardWatcher
    {
        public const int DefaultMaxEntries = 100;
        public const string MaskedPlaceholder = "已屏蔽（敏感内容未记录）";

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int ClipboardRetries = 3;
        private const int ClipboardRetryDelayMs = 60;
        private const int MaxTextLength = 100000;          // 单条文本上限，防止 json 膨胀
        private const int DebounceMs = 120;                // 合并一次复制产生的多条通知，等 OLE 写完再读

        private static readonly string[] SensitiveKeywords =
        {
            "password", "passwd", "密码", "token", "secret", "私钥", "银行卡", "身份证"
        };

        private static readonly Regex PureCardNumber = new Regex("^[0-9]{12,19}$", RegexOptions.Compiled);

        private readonly Store _store;
        private readonly int _maxEntries;
        private readonly object _lock = new object();
        private readonly List<ClipEntry> _history = new List<ClipEntry>();
        // id -> 内容签名：一次复制可能触发两条 WM_CLIPBOARDUPDATE，靠签名与最近一条比较去重；
        // 敏感条目只存哈希，不保留明文。
        private readonly Dictionary<string, string> _signatures = new Dictionary<string, string>();

        private ClipWindow _window;
        private System.Windows.Forms.Timer _debounceTimer;
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _pendingUpdate;

        private bool _hasSuppress;
        private uint _suppressSeq;

        /// <summary>历史变化通知（新增/删除/清空）。触发线程 = 隐藏窗口线程，UI 可能需要 BeginInvoke。</summary>
        public event Action Changed;

        public ClipboardWatcher(Store store, int maxEntries = DefaultMaxEntries)
        {
            _store = store;
            _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
        }

        /// <summary>按时间倒序（最新在前）的快照，调用方拿到的是一份副本。</summary>
        public List<ClipEntry> History
        {
            get { lock (_lock) { return new List<ClipEntry>(_history); } }
        }

        public int Count
        {
            get { lock (_lock) { return _history.Count; } }
        }

        public int MaxEntries { get { return _maxEntries; } }

        public bool Paused { get { return _paused; } }

        public bool Running { get { return _running; } }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        public void Start()
        {
            if (_running) return;
            try
            {
                var w = new ClipWindow(this);
                var cp = new CreateParams();
                cp.Caption = "DeskBox.ClipboardWatcher";
                cp.Style = unchecked((int)0x80000000);                  // WS_POPUP，永不显示
                cp.ExStyle = 0x00000080 | 0x08000000;                   // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                cp.X = -32000;
                cp.Y = -32000;
                cp.Width = 1;
                cp.Height = 1;
                w.CreateHandle(cp);

                if (!AddClipboardFormatListener(w.Handle))
                {
                    int err = Marshal.GetLastWin32Error();
                    DeskBox.Core.AppLog.Write("warn", "clipboard", "AddClipboardFormatListener 失败: " + err);
                    try { w.DestroyHandle(); } catch { }
                    return;
                }

                _window = w;
                _running = true;

                // 去抖计时器：一次复制往往触发两条通知，且第一条可能在 OLE 写完前到达
                _pendingUpdate = false;
                _debounceTimer = new System.Windows.Forms.Timer();
                _debounceTimer.Interval = DebounceMs;
                _debounceTimer.Tick += OnDebounceTick;

                DeskBox.Core.AppLog.Write("info", "clipboard", "剪贴板监听已启动");
            }
            catch (Exception ex)
            {
                _running = false;
                DeskBox.Core.AppLog.Write("error", "clipboard", "启动剪贴板监听失败: " + ex.Message);
            }
        }

        public void Stop()
        {
            ClipWindow w = _window;
            _window = null;
            _running = false;
            _pendingUpdate = false;

            System.Windows.Forms.Timer timer = _debounceTimer;
            _debounceTimer = null;
            if (timer != null)
            {
                try { timer.Stop(); timer.Dispose(); }
                catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "释放去抖计时器失败: " + ex.Message); }
            }

            if (w == null) return;
            try { RemoveClipboardFormatListener(w.Handle); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "移除监听失败: " + ex.Message); }
            try { w.DestroyHandle(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "销毁隐藏窗口失败: " + ex.Message); }
        }

        public void Pause(bool paused)
        {
            _paused = paused;
            if (paused)
            {
                _pendingUpdate = false;
                _hasSuppress = false;
                System.Windows.Forms.Timer timer = _debounceTimer;
                if (timer != null)
                {
                    try { timer.Stop(); }
                    catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // 采集
        // ------------------------------------------------------------------

        private void OnClipboardUpdate()
        {
            if (!_running || _paused) return;
            if (ConsumeSuppression()) return;      // 自己 CopyToClipboard 触发的通知，不重复记录

            // 去抖：一次复制可能连发多条通知，且首条可能早于 OLE 写入完成，
            // 立即读会拿到「部分数据」。合并到 120ms 后再读，保证读到最终状态。
            _pendingUpdate = true;
            System.Windows.Forms.Timer timer = _debounceTimer;
            if (timer != null)
            {
                try { timer.Stop(); timer.Start(); }
                catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "去抖计时器异常: " + ex.Message); }
                return;
            }

            // 计时器不可用（极端情况）：退化为立即读取
            _pendingUpdate = false;
            try { Capture(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "clipboard", "读取剪贴板失败: " + ex.Message); }
        }

        private void OnDebounceTick(object sender, EventArgs e)
        {
            System.Windows.Forms.Timer timer = _debounceTimer;
            if (timer != null)
            {
                try { timer.Stop(); }
                catch { }
            }
            if (!_running || _paused)
            {
                _pendingUpdate = false;
                return;
            }
            if (!_pendingUpdate) return;
            _pendingUpdate = false;
            try { Capture(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "clipboard", "读取剪贴板失败: " + ex.Message); }
        }

        private void Capture()
        {
            string kind = "text";
            string text = null;

            // 1) 文件列表优先（资源管理器复制文件时同时带有文本，按 files 记录更准确）
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    StringCollection files = RetryGetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        var sb = new StringBuilder();
                        int added = 0;
                        for (int i = 0; i < files.Count; i++)
                        {
                            if (string.IsNullOrEmpty(files[i])) continue;   // 防御过渡态里的空路径
                            if (added > 0) sb.Append(';');
                            sb.Append(files[i]);
                            added++;
                        }
                        if (added > 0)
                        {
                            text = sb.ToString();
                            kind = "files";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DeskBox.Core.AppLog.Write("warn", "clipboard", "读取文件列表失败: " + ex.Message);
            }

            // 2) 纯文本
            if (text == null)
            {
                text = RetryGetText();
                kind = "text";
            }

            if (text == null) return;
            text = text.Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength);

            bool sensitive = kind == "text" && LooksSensitive(text);

            var entry = new ClipEntry();
            entry.Id = NewId();
            entry.Ts = NowMs();
            entry.Kind = kind;
            entry.Sensitive = sensitive;
            entry.Text = sensitive ? "" : text;      // 敏感内容不留在内存/磁盘
            entry.Source = SafeForegroundTitle();

            string signature = sensitive
                ? kind + "|sha256:" + Sha256Hex(text)
                : BuildSignature(kind, text);

            lock (_lock)
            {
                // 与最近一条完全相同则忽略（避免重复复制/重复通知刷屏）
                if (_history.Count > 0 && string.Equals(SignatureOf(_history[0]), signature, StringComparison.Ordinal))
                    return;

                _history.Insert(0, entry);
                _signatures[entry.Id] = signature;
                while (_history.Count > _maxEntries)
                {
                    ClipEntry dropped = _history[_history.Count - 1];
                    _history.RemoveAt(_history.Count - 1);
                    _signatures.Remove(dropped.Id);
                }
            }

            Save();
            RaiseChanged();
        }

        private static string RetryGetText()
        {
            for (int attempt = 0; attempt < ClipboardRetries; attempt++)
            {
                try { return Clipboard.GetText(); }
                catch (ExternalException) { Thread.Sleep(ClipboardRetryDelayMs); }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("warn", "clipboard", "GetText 失败: " + ex.Message);
                    return null;
                }
            }
            DeskBox.Core.AppLog.Write("warn", "clipboard", "剪贴板被占用，GetText 重试 " + ClipboardRetries + " 次后放弃");
            return null;
        }

        private static StringCollection RetryGetFileDropList()
        {
            for (int attempt = 0; attempt < ClipboardRetries; attempt++)
            {
                try { return Clipboard.GetFileDropList(); }
                catch (ExternalException) { Thread.Sleep(ClipboardRetryDelayMs); }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("warn", "clipboard", "GetFileDropList 失败: " + ex.Message);
                    return null;
                }
            }
            DeskBox.Core.AppLog.Write("warn", "clipboard", "剪贴板被占用，GetFileDropList 重试 " + ClipboardRetries + " 次后放弃");
            return null;
        }

        // ------------------------------------------------------------------
        // 操作
        // ------------------------------------------------------------------

        /// <summary>把历史条目重新写回剪贴板；成功返回 true。敏感条目不保留原文，返回 false。</summary>
        public bool CopyToClipboard(string id)
        {
            ClipEntry entry = null;
            lock (_lock)
            {
                for (int i = 0; i < _history.Count; i++)
                {
                    if (string.Equals(_history[i].Id, id, StringComparison.Ordinal))
                    {
                        entry = _history[i];
                        break;
                    }
                }
            }
            if (entry == null || entry.Sensitive) return false;

            for (int attempt = 0; attempt < ClipboardRetries; attempt++)
            {
                try
                {
                    if (entry.Kind == "files")
                    {
                        var list = new StringCollection();
                        string[] parts = entry.Text.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        for (int i = 0; i < parts.Length; i++) list.Add(parts[i]);
                        Clipboard.SetFileDropList(list);
                    }
                    else
                    {
                        Clipboard.SetText(entry.Text);
                    }
                    ArmSuppression();   // 自己写回产生的剪贴板通知不重复入库
                    return true;
                }
                catch (ExternalException) { Thread.Sleep(ClipboardRetryDelayMs); }
                catch (Exception ex)
                {
                    DeskBox.Core.AppLog.Write("warn", "clipboard", "写回剪贴板失败: " + ex.Message);
                    break;
                }
            }
            return false;
        }

        public void Remove(string id)
        {
            bool removed = false;
            lock (_lock)
            {
                for (int i = _history.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_history[i].Id, id, StringComparison.Ordinal))
                    {
                        _history.RemoveAt(i);
                        _signatures.Remove(id);
                        removed = true;
                    }
                }
            }
            if (!removed) return;
            Save();
            RaiseChanged();
        }

        public void Clear()
        {
            lock (_lock)
            {
                _history.Clear();
                _signatures.Clear();
            }
            Save();
            RaiseChanged();
        }

        // ------------------------------------------------------------------
        // 持久化（敏感条目永不落盘）
        // ------------------------------------------------------------------

        public void Save()
        {
            var safe = new List<ClipEntry>();
            lock (_lock)
            {
                for (int i = 0; i < _history.Count; i++)
                {
                    ClipEntry e = _history[i];
                    if (e == null || e.Sensitive) continue;
                    if (string.IsNullOrEmpty(e.Text)) continue;
                    safe.Add(e);
                }
            }
            try { Json.Save(HistoryPath(), safe); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "保存剪贴板历史失败: " + ex.Message); }
        }

        public void Load()
        {
            string path = HistoryPath();
            List<ClipEntry> loaded = Json.Load<List<ClipEntry>>(path, new List<ClipEntry>());
            if (loaded == null) loaded = new List<ClipEntry>();

            var list = new List<ClipEntry>();
            for (int i = 0; i < loaded.Count && list.Count < _maxEntries; i++)
            {
                ClipEntry e = loaded[i];
                if (e == null || e.Sensitive) continue;      // 旧文件里即使有敏感条目也丢弃
                if (string.IsNullOrEmpty(e.Text)) continue;
                if (string.IsNullOrEmpty(e.Id)) e.Id = NewId();
                if (e.Kind != "files") e.Kind = "text";
                list.Add(e);
            }

            lock (_lock)
            {
                _history.Clear();
                _signatures.Clear();
                _history.AddRange(list);
                for (int i = 0; i < list.Count; i++)
                    _signatures[list[i].Id] = BuildSignature(list[i].Kind, list[i].Text ?? "");
            }
        }

        private string HistoryPath()
        {
            string dir = null;
            try { if (_store != null) dir = Store.DataDir; }
            catch { }
            if (string.IsNullOrEmpty(dir)) dir = AppPaths.DataDir;
            return Path.Combine(dir, "clipboard.json");
        }

        // ------------------------------------------------------------------
        // 敏感判定 / 展示
        // ------------------------------------------------------------------

        /// <summary>敏感内容判定：关键词 / 纯数字 12-19 位（疑似卡号）/ sk-、ghp_ 开头的密钥。</summary>
        public static bool LooksSensitive(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim();
            if (t.Length == 0) return false;

            if (t.StartsWith("sk-", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase)) return true;
            if (PureCardNumber.IsMatch(t)) return true;

            for (int i = 0; i < SensitiveKeywords.Length; i++)
            {
                if (t.IndexOf(SensitiveKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>UI 展示用文本：敏感条目只显示占位，文件列表显示数量。</summary>
        public static string DisplayText(ClipEntry entry)
        {
            if (entry == null) return "";
            if (entry.Sensitive) return MaskedPlaceholder;
            if (entry.Kind == "files")
            {
                string[] parts = (entry.Text ?? "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "";
                if (parts.Length == 1) return parts[0];
                return "[" + parts.Length + " 个文件] " + parts[0];
            }
            return entry.Text ?? "";
        }

        // ------------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------------

        private void ArmSuppression()
        {
            uint seq = 0;
            try { seq = GetClipboardSequenceNumber(); }
            catch { }
            _suppressSeq = seq;
            _hasSuppress = seq != 0;
        }

        /// <summary>剪贴板还是自己那次写入（同一序列号）就忽略；序列号变了说明是外部复制，正常处理。</summary>
        private bool ConsumeSuppression()
        {
            if (!_hasSuppress) return false;
            uint seq = 0;
            try { seq = GetClipboardSequenceNumber(); }
            catch { }
            if (seq != 0 && seq == _suppressSeq) return true;
            _hasSuppress = false;
            return false;
        }

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception ex) { DeskBox.Core.AppLog.Write("warn", "clipboard", "Changed 订阅者异常: " + ex.Message); }
        }

        private static string BuildSignature(string kind, string text)
        {
            return kind + "|" + text;
        }

        /// <summary>最近一条的签名；敏感条目原文已清空，只能用记录下来的哈希。</summary>
        private string SignatureOf(ClipEntry head)
        {
            if (head == null) return "";
            string sig;
            if (_signatures.TryGetValue(head.Id, out sig)) return sig;
            if (head.Sensitive) return "sensitive#" + head.Id;   // 原文未知：不做抑制
            return BuildSignature(head.Kind, head.Text ?? "");
        }

        private static string Sha256Hex(string text)
        {
            try
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                    var sb = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
                    return sb.ToString();
                }
            }
            catch { return ""; }
        }

        private static string NewId()
        {
            return "c_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private static long NowMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string SafeForegroundTitle()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "";
                var sb = new StringBuilder(256);
                int n = GetWindowText(h, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------
        // Win32
        // ------------------------------------------------------------------

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        /// <summary>隐藏的消息接收窗口（只在创建它的线程上收消息）。</summary>
        private sealed class ClipWindow : NativeWindow
        {
            private readonly ClipboardWatcher _owner;

            public ClipWindow(ClipboardWatcher owner)
            {
                _owner = owner;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE)
                {
                    try { _owner.OnClipboardUpdate(); }
                    catch (Exception ex) { DeskBox.Core.AppLog.Write("error", "clipboard", "处理剪贴板通知失败: " + ex.Message); }
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
