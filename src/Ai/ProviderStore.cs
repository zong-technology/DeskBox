using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using DeskBox.Core;

namespace DeskBox.Ai
{
    /// <summary>
    /// AI 服务商配置。ApiKey 只在内存中为明文；落盘由 ProviderStore 用 DPAPI 加密成 apiKeyEnc。
    /// </summary>
    public class AiProvider
    {
        public string Id = "";
        public string Name = "";
        public string Protocol = "openai";      // openai | openai-responses | custom
        public string BaseUrl = "";
        public string Model = "";
        public string ApiKey = "";              // 仅内存明文，绝不写 json / 日志
        public int TimeoutMs = 60000;
        public int MaxRetries = 1;
        public bool Enabled = true;
        public bool IsDefault = false;
        public string ExtraHeaders = "";        // 每行一条 "Header: value"
        public Dictionary<string, string> ExtraParams = new Dictionary<string, string>();
        public long CreatedAt = 0;
        public long UpdatedAt = 0;
        public string LastStatus = "";          // "ok: ..." / "err: ..."

        public AiProvider Clone()
        {
            var c = new AiProvider();
            c.Id = Id;
            c.Name = Name;
            c.Protocol = Protocol;
            c.BaseUrl = BaseUrl;
            c.Model = Model;
            c.ApiKey = ApiKey;
            c.TimeoutMs = TimeoutMs;
            c.MaxRetries = MaxRetries;
            c.Enabled = Enabled;
            c.IsDefault = IsDefault;
            c.ExtraHeaders = ExtraHeaders;
            c.ExtraParams = ExtraParams == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(ExtraParams);
            c.CreatedAt = CreatedAt;
            c.UpdatedAt = UpdatedAt;
            c.LastStatus = LastStatus;
            return c;
        }
    }

    /// <summary>
    /// 多服务商配置存储：DPAPI 加密密钥、原子写入、旧版 data\ai.json 自动迁移。
    /// 配置文件：data\ai-providers.json。
    /// </summary>
    public class ProviderStore
    {
        private const int FileVersion = 1;
        private const string EntropyText = "DeskBox.Ai.ProviderStore.v3";

        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _lock = new object();
        private readonly List<AiProvider> _providers = new List<AiProvider>();

        public ProviderStore()
        {
            Load();
        }

        /// <summary>服务商快照（新增/删除/修改请走 Add/Update/Remove；改完需 Save）。</summary>
        public List<AiProvider> Providers
        {
            get { lock (_lock) return new List<AiProvider>(_providers); }
        }

        /// <summary>当前默认服务商；未配置时返回 null。</summary>
        public AiProvider DefaultProvider
        {
            get { lock (_lock) return FindDefaultLocked(); }
        }

        public static string DataFile
        {
            get { return Path.Combine(AppPaths.DataDir, "ai-providers.json"); }
        }

        public AiProvider Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock) return _providers.FirstOrDefault(x => x.Id == id);
        }

        // ---------------- CRUD ----------------

        /// <summary>新增；生成 Id，第一个自动设为默认。</summary>
        public AiProvider Add(AiProvider p)
        {
            if (p == null) return null;
            lock (_lock)
            {
                var np = NormalizeForAdd(p);
                while (_providers.Any(x => x.Id == np.Id)) np.Id = NewId();
                if (_providers.Count == 0) np.IsDefault = true;
                if (np.IsDefault) foreach (var x in _providers) x.IsDefault = false;
                _providers.Add(np);
                SaveLocked();
                AiLog.Write("info", "新增 AI 服务商 " + np.Name + "（Key " + MaskKey(np.ApiKey) + "）");
                return np;
            }
        }

        /// <summary>按 Id 更新；ApiKey 为空表示保留原 Key（清空请用 SetApiKey）。</summary>
        public bool Update(AiProvider p)
        {
            if (p == null || string.IsNullOrEmpty(p.Id)) return false;
            lock (_lock)
            {
                var cur = _providers.FirstOrDefault(x => x.Id == p.Id);
                if (cur == null) return false;
                if (!string.IsNullOrWhiteSpace(p.Name)) cur.Name = p.Name.Trim();
                cur.Protocol = NormalizeProtocol(p.Protocol);
                cur.BaseUrl = (p.BaseUrl ?? "").Trim();
                cur.Model = (p.Model ?? "").Trim();
                if (!string.IsNullOrEmpty(p.ApiKey)) cur.ApiKey = p.ApiKey.Trim();
                cur.TimeoutMs = ClampTimeout(p.TimeoutMs);
                cur.MaxRetries = ClampRetries(p.MaxRetries);
                cur.ExtraHeaders = p.ExtraHeaders ?? "";
                cur.ExtraParams = p.ExtraParams == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(p.ExtraParams);
                cur.Enabled = p.Enabled;
                if (!string.IsNullOrEmpty(p.LastStatus)) cur.LastStatus = p.LastStatus;
                cur.UpdatedAt = Now();
                if (p.IsDefault) SetDefaultLocked(cur.Id);
                SaveLocked();
                AiLog.Write("info", "更新 AI 服务商 " + cur.Name + "（Key " + MaskKey(cur.ApiKey) + "）");
                return true;
            }
        }

        /// <summary>删除；若删的是默认，把默认转给第一个可用（启用优先）。</summary>
        public bool Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock)
            {
                var cur = _providers.FirstOrDefault(x => x.Id == id);
                if (cur == null) return false;
                bool wasDefault = cur.IsDefault;
                _providers.Remove(cur);
                if (wasDefault || _providers.Count > 0 && FindDefaultLocked() == null) RebalanceDefaultLocked();
                SaveLocked();
                AiLog.Write("info", "删除 AI 服务商 " + cur.Name + "（" + MaskKey(cur.ApiKey) + "）");
                return true;
            }
        }

        public void SetDefault(string id)
        {
            lock (_lock)
            {
                if (SetDefaultLocked(id)) SaveLocked();
            }
        }

        /// <summary>显式设置/清空某个服务商的 Key（空字符串=清空）。</summary>
        public bool SetApiKey(string id, string key)
        {
            lock (_lock)
            {
                var cur = _providers.FirstOrDefault(x => x.Id == id);
                if (cur == null) return false;
                cur.ApiKey = key == null ? "" : key.Trim();
                cur.UpdatedAt = Now();
                SaveLocked();
                return true;
            }
        }

        // ---------------- 持久化 ----------------

        public void Save()
        {
            lock (_lock) SaveLocked();
        }

        public void Load()
        {
            lock (_lock) LoadLocked();
        }

        /// <summary>脱敏：sk-…abcd（只保留末 4 位）。</summary>
        public static string MaskKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var k = key.Trim();
            if (k.Length <= 4) return "***";
            var tail = k.Substring(k.Length - 4);
            if (k.Length > 8 && k[2] == '-') return k.Substring(0, 3) + "…" + tail;
            if (k.Length > 8) return k.Substring(0, 2) + "…" + tail;
            return "…" + tail;
        }

        private static long Now()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static string NewId()
        {
            return "p_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private static string NormalizeProtocol(string protocol)
        {
            var p = (protocol ?? "").Trim().ToLowerInvariant();
            if (p == "openai" || p == "openai-responses" || p == "custom") return p;
            return "openai";
        }

        private static int ClampTimeout(int ms)
        {
            if (ms <= 0) return 60000;
            return Math.Min(600000, Math.Max(1000, ms));
        }

        private static int ClampRetries(int n)
        {
            return Math.Min(5, Math.Max(0, n));
        }

        private AiProvider NormalizeForAdd(AiProvider p)
        {
            var np = p.Clone();
            np.Id = string.IsNullOrWhiteSpace(np.Id) ? NewId() : np.Id.Trim();
            np.Name = string.IsNullOrWhiteSpace(np.Name) ? "AI 服务" : np.Name.Trim();
            np.Protocol = NormalizeProtocol(np.Protocol);
            np.BaseUrl = (np.BaseUrl ?? "").Trim();
            np.Model = (np.Model ?? "").Trim();
            np.ApiKey = (np.ApiKey ?? "").Trim();
            np.TimeoutMs = ClampTimeout(np.TimeoutMs);
            np.MaxRetries = ClampRetries(np.MaxRetries);
            np.ExtraHeaders = np.ExtraHeaders ?? "";
            if (np.CreatedAt == 0) np.CreatedAt = Now();
            np.UpdatedAt = Now();
            return np;
        }

        private AiProvider FindDefaultLocked()
        {
            var d = _providers.FirstOrDefault(x => x.IsDefault);
            if (d != null) return d;
            var enabled = _providers.FirstOrDefault(x => x.Enabled);
            return enabled ?? _providers.FirstOrDefault();
        }

        private void RebalanceDefaultLocked()
        {
            if (_providers.Count == 0) return;
            var target = _providers.FirstOrDefault(x => x.Enabled) ?? _providers.First();
            foreach (var x in _providers) x.IsDefault = false;
            target.IsDefault = true;
        }

        private bool SetDefaultLocked(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var target = _providers.FirstOrDefault(x => x.Id == id);
            if (target == null) return false;
            foreach (var x in _providers) x.IsDefault = false;
            target.IsDefault = true;
            target.UpdatedAt = Now();
            return true;
        }

        private void LoadLocked()
        {
            _providers.Clear();
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var file = DataFile;
                if (!File.Exists(file))
                {
                    MigrateLegacyLocked();
                    return;
                }
                string text;
                try { text = File.ReadAllText(file, Encoding.UTF8); }
                catch (Exception ex)
                {
                    AiLog.Write("error", "读取 AI 配置失败：" + ex.Message);
                    return;
                }
                if (string.IsNullOrWhiteSpace(text)) return;

                FileDto dto = null;
                try { dto = Ser.Deserialize<FileDto>(text); }
                catch (Exception ex) { BackupBad(file, "JSON 解析失败：" + ex.Message); return; }
                if (dto == null || dto.providers == null) { BackupBad(file, "配置文件结构异常"); return; }

                foreach (var d in dto.providers)
                {
                    var p = FromDto(d);
                    if (p != null) _providers.Add(p);
                }
                NormalizeDefaultsLocked();
            }
            catch (Exception ex)
            {
                AiLog.Write("error", "加载 AI 配置失败：" + ex.Message);
            }
        }

        private void SaveLocked()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var dto = new FileDto();
                dto.version = FileVersion;
                dto.providers = new List<ProviderDto>();
                foreach (var p in _providers) dto.providers.Add(ToDto(p));
                WriteAtomic(DataFile, Ser.Serialize(dto));
            }
            catch (Exception ex)
            {
                AiLog.Write("error", "保存 AI 配置失败：" + ex.Message);
            }
        }

        private void NormalizeDefaultsLocked()
        {
            if (_providers.Count == 0) return;
            var defaults = _providers.Where(x => x.IsDefault).ToList();
            if (defaults.Count == 0) RebalanceDefaultLocked();
            else
            {
                for (int i = 1; i < defaults.Count; i++) defaults[i].IsDefault = false;
            }
        }

        private void BackupBad(string file, string reason)
        {
            _providers.Clear();
            try
            {
                var bad = file + ".bad";
                File.Copy(file, bad, true);
                AiLog.Write("warn", "AI 配置损坏（" + reason + "），已备份为 " + Path.GetFileName(bad) + "，使用空配置");
            }
            catch (Exception ex)
            {
                AiLog.Write("warn", "AI 配置损坏且备份失败：" + ex.Message);
            }
        }

        private static void WriteAtomic(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8NoBom);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch
                {
                    File.Delete(path);
                    File.Move(tmp, path);
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        // ---------------- 旧版 ai.json 迁移 ----------------

        private void MigrateLegacyLocked()
        {
            var legacy = Path.Combine(AppPaths.DataDir, "ai.json");
            if (!File.Exists(legacy)) return;
            try
            {
                var cfg = Ser.Deserialize<Dictionary<string, string>>(File.ReadAllText(legacy, Encoding.UTF8));
                if (cfg != null)
                {
                    string ep = GetCfg(cfg, "endpoint");
                    string key = GetCfg(cfg, "apiKey");
                    string model = GetCfg(cfg, "model");
                    string enabled = GetCfg(cfg, "enabled");
                    if (!string.IsNullOrWhiteSpace(ep) || !string.IsNullOrWhiteSpace(key))
                    {
                        var p = new AiProvider();
                        p.Id = NewId();
                        p.Name = "迁移的 AI 服务";
                        p.Protocol = "openai";
                        p.BaseUrl = ep;
                        p.Model = model;
                        p.ApiKey = key;
                        p.Enabled = enabled == "1";
                        p.IsDefault = true;
                        p.CreatedAt = Now();
                        p.UpdatedAt = Now();
                        p.TimeoutMs = 60000;
                        p.MaxRetries = 1;
                        _providers.Add(p);
                        SaveLocked();
                        AiLog.Write("info", "已迁移旧版 ai.json（Key " + MaskKey(key) + "，模型 " + model + "），密钥已加密");
                    }
                }
            }
            catch (Exception ex)
            {
                AiLog.Write("warn", "旧版 ai.json 迁移失败：" + ex.Message);
            }
            finally
            {
                try
                {
                    var moved = legacy + ".migrated";
                    if (File.Exists(moved)) File.Delete(moved);
                    File.Move(legacy, moved);
                }
                catch (Exception ex)
                {
                    AiLog.Write("warn", "旧版 ai.json 重命名失败：" + ex.Message);
                }
            }
        }

        private static string GetCfg(Dictionary<string, string> cfg, string key)
        {
            string v;
            return cfg.TryGetValue(key, out v) && v != null ? v : "";
        }

        // ---------------- DTO / 加解密 ----------------

        private static byte[] Entropy
        {
            get { return Encoding.UTF8.GetBytes(EntropyText); }
        }

        private static string ProtectKey(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = Encoding.UTF8.GetBytes(plain);
            var enc = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(enc);
        }

        private static string UnprotectKey(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            var bytes = Convert.FromBase64String(enc);
            var dec = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }

        private static ProviderDto ToDto(AiProvider p)
        {
            var d = new ProviderDto();
            d.id = p.Id;
            d.name = p.Name;
            d.protocol = p.Protocol;
            d.baseUrl = p.BaseUrl;
            d.model = p.Model;
            d.apiKeyEnc = "";
            if (!string.IsNullOrEmpty(p.ApiKey))
            {
                try { d.apiKeyEnc = ProtectKey(p.ApiKey); }
                catch (Exception ex)
                {
                    // 加密失败时宁可丢弃密钥，也绝不写明文
                    AiLog.Write("error", "API Key 加密失败（" + p.Name + "），未保存密钥：" + ex.Message);
                }
            }
            d.timeoutMs = p.TimeoutMs;
            d.maxRetries = p.MaxRetries;
            d.enabled = p.Enabled;
            d.isDefault = p.IsDefault;
            d.extraHeaders = p.ExtraHeaders;
            d.extraParams = p.ExtraParams == null ? new Dictionary<string, string>() : p.ExtraParams;
            d.createdAt = p.CreatedAt;
            d.updatedAt = p.UpdatedAt;
            d.lastStatus = p.LastStatus;
            return d;
        }

        private AiProvider FromDto(ProviderDto d)
        {
            if (d == null) return null;
            var p = new AiProvider();
            p.Id = string.IsNullOrWhiteSpace(d.id) ? NewId() : d.id;
            p.Name = string.IsNullOrWhiteSpace(d.name) ? "AI 服务" : d.name;
            p.Protocol = NormalizeProtocol(d.protocol);
            p.BaseUrl = d.baseUrl ?? "";
            p.Model = d.model ?? "";
            p.TimeoutMs = ClampTimeout(d.timeoutMs);
            p.MaxRetries = ClampRetries(d.maxRetries);
            p.Enabled = d.enabled;
            p.IsDefault = d.isDefault;
            p.ExtraHeaders = d.extraHeaders ?? "";
            p.ExtraParams = d.extraParams == null ? new Dictionary<string, string>() : new Dictionary<string, string>(d.extraParams);
            p.CreatedAt = d.createdAt;
            p.UpdatedAt = d.updatedAt;
            p.LastStatus = d.lastStatus ?? "";
            if (!string.IsNullOrEmpty(d.apiKeyEnc))
            {
                try
                {
                    p.ApiKey = UnprotectKey(d.apiKeyEnc);
                }
                catch
                {
                    p.ApiKey = "";
                    p.LastStatus = "密钥无法解密，请重新填写";
                    AiLog.Write("warn", "AI 服务 " + p.Name + " 的密钥无法解密（可能更换了 Windows 用户或电脑），已清空");
                }
            }
            return p;
        }

        private class FileDto
        {
            public int version = FileVersion;
            public List<ProviderDto> providers = new List<ProviderDto>();
        }

        private class ProviderDto
        {
            public string id = "";
            public string name = "";
            public string protocol = "openai";
            public string baseUrl = "";
            public string model = "";
            public string apiKeyEnc = "";
            public int timeoutMs = 60000;
            public int maxRetries = 1;
            public bool enabled = true;
            public bool isDefault = false;
            public string extraHeaders = "";
            public Dictionary<string, string> extraParams = new Dictionary<string, string>();
            public long createdAt = 0;
            public long updatedAt = 0;
            public string lastStatus = "";
        }
    }

    /// <summary>AI 模块统一日志：写入 data/logs.jsonl，action 固定为 "ai"，绝不写明文 Key。</summary>
    internal static class AiLog
    {
        private static readonly object _lock = new object();
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static void Write(string level, string message)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var e = new LogEntry();
                e.Id = "l_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                e.Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                e.Level = string.IsNullOrEmpty(level) ? "info" : level;
                e.Action = "ai";
                e.Message = message ?? "";
                lock (_lock)
                {
                    File.AppendAllText(Path.Combine(AppPaths.DataDir, "logs.jsonl"), Ser.Serialize(e) + "\n", new UTF8Encoding(false));
                }
            }
            catch { /* 日志失败不影响主流程 */ }
        }
    }
}
