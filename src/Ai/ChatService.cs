using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Ai
{
    public class ChatMessage
    {
        public string Id = "";
        public string Role = "user";          // user | assistant | system | tool
        public string Content = "";
        public long Ts = 0;
        public bool Streaming = false;
        public string Error = "";
        public string ErrorKind = "";
        public string ToolName = "";
        public string ToolResult = "";
        public bool Pending;                  // 等待用户确认的工具调用
        public string PendingToolArgs = "";
        public string SpeakerName = "";
        public bool Interrupted;
    }

    public class ChatSession
    {
        public string Id = "";
        public string Title = "新对话";
        public long CreatedAt = 0;
        public long UpdatedAt = 0;
        public string ProviderId = "";
        public string Model = "";
        public string DomainMode = "automation";   // automation|plc|dcs|validation|troubleshoot|general
        public double Temperature = 0.3;
        public int MaxTokens = 0;
        public string Channel = "text", AssistantName = "", Persona = "", VoiceModel = "";
        public List<ChatMessage> Messages = new List<ChatMessage>();

        public void Touch()
        {
            UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (Messages.Count > 0 && (Title == "新对话" || string.IsNullOrEmpty(Title)))
            {
                var first = Messages.FirstOrDefault(m => m.Role == "user");
                if (first != null)
                {
                    var t = first.Content.Replace("\r", " ").Replace("\n", " ").Trim();
                    Title = t.Length > 24 ? t.Substring(0, 24) + "…" : (t.Length == 0 ? "新对话" : t);
                }
            }
        }
    }

    /// <summary>对话服务：多轮上下文、流式、取消、重生成、持久化。所有网络调用在后台线程。</summary>
    public class ChatService
    {
        private readonly Store _store;
        private ProviderStore _providers;
        private readonly List<ChatSession> _sessions = new List<ChatSession>();
        private ChatSession _current;
        private CancellationTokenSource _cts;
        private readonly object _lock = new object();

        public event Action Changed;
        public event Action<ChatMessage> MessageDelta;
        public event Action<string> StatusChanged;   // "思考中" / "已取消" / "失败: xxx"

        public bool Busy { get; private set; }
        public string ActiveSessionId { get; private set; }
        public List<ChatSession> Sessions { get { lock (_lock) return _sessions.ToList(); } }
        public ChatSession Current { get { return _current; } }
        public string Status { get; private set; }

        private string environment = AiEnvironment.Mode;
        private string SessionsFile { get { return Path.Combine(AppPaths.DataDir, environment == "local" ? "chats-local.json" : "chats.json"); } }
        public void ChangeEnvironment()
        {
            if(Busy)throw new InvalidOperationException("请先停止当前对话，再切换 AI 环境。");
            if(environment==AiEnvironment.Mode)return;
            Save();environment=AiEnvironment.Mode;AllowedFiles.Clear();AllowedRoots.Clear();
            lock(_lock)_sessions.Clear();Load();_current=Sessions.FirstOrDefault();if(_current==null)NewSession();Raise();
        }

        public ChatService(Store store, ProviderStore providers)
        {
            _store = store;
            _providers = providers;
            Load();
            if (_sessions.Count == 0) NewSession();
            else { _current = _sessions.OrderByDescending(s => s.UpdatedAt).First(); }
        }

        public void SetProviders(ProviderStore providers) { _providers = providers; }

        internal ChatSession BeginVoiceSession(RealtimeVoiceSettings profile)
        {
            var now=DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var value=new ChatSession {Id="v_"+Guid.NewGuid().ToString("N"),CreatedAt=now,UpdatedAt=now,
                Title="全息 · "+profile.AssistantName+" · "+DateTime.Now.ToString("MM-dd HH:mm"),
                Channel="hologram",AssistantName=profile.AssistantName,Persona=profile.Persona,VoiceModel=profile.Model,DomainMode="general"};
            var provider=_providers==null ? null : _providers.DefaultProvider;
            if(provider!=null){value.ProviderId=provider.Id;value.Model=provider.Model;}
            lock(_lock)_sessions.Insert(0,value);
            return value;
        }

        internal void RecordVoiceMessage(ChatSession session, ChatMessage message)
        {
            lock(_lock){
                int index=session.Messages.FindIndex(m=>m.Id==message.Id);
                if(index<0){
                    int next=session.Messages.FindIndex(m=>m.Ts>message.Ts);
                    if(next<0)session.Messages.Add(message);else session.Messages.Insert(next,message);
                }else session.Messages[index]=message;
                session.Touch();
            }
        }

        internal void SaveVoiceHistory() { Save(); Raise(); }

        // ---------- 会话管理 ----------

        public ChatSession NewSession()
        {
            var s = new ChatSession
            {
                Id = "c_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CreatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                UpdatedAt = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
            var def = _providers != null ? _providers.DefaultProvider : null;
            if (def != null) { s.ProviderId = def.Id; s.Model = def.Model; }
            lock (_lock) { _sessions.Insert(0, s); }
            _current = s;
            Save();
            Raise();
            return s;
        }

        public void SwitchSession(string id)
        {
            var s = _sessions.FirstOrDefault(x => x.Id == id);
            if (s == null) return;
            _current = s;
            Raise();
        }

        public void DeleteSession(string id)
        {
            lock (_lock) _sessions.RemoveAll(x => x.Id == id);
            if (_current != null && _current.Id == id)
                _current = _sessions.FirstOrDefault() ?? NewSession();
            Save();
            Raise();
        }

        public void RenameSession(string id, string title)
        {
            var s = _sessions.FirstOrDefault(x => x.Id == id);
            if (s == null || string.IsNullOrWhiteSpace(title)) return;
            s.Title = title.Trim();
            Save();
            Raise();
        }

        public void SetProvider(string providerId, string model)
        {
            if (_current == null) return;
            if (!string.IsNullOrEmpty(providerId)) _current.ProviderId = providerId;
            if (!string.IsNullOrEmpty(model)) _current.Model = model;
            Save();
            Raise();
        }

        public void SetGenerationOptions(string domainMode, double temperature, int maxTokens)
        {
            if (_current == null) return;
            _current.DomainMode = string.IsNullOrWhiteSpace(domainMode) ? "automation" : domainMode;
            _current.Temperature = Math.Max(0, Math.Min(1.5, temperature));
            _current.MaxTokens = Math.Max(0, maxTokens);
            Save();
        }

        public void ClearCurrent()
        {
            if (_current == null) return;
            _current.Messages.Clear();
            _current.Title = "新对话";
            Save();
            Raise();
        }

        // ---------- 发送 / 流式 ----------

        public void Send(string text)
        {
            if (_current == null || Busy) return;
            if (string.IsNullOrWhiteSpace(text)) return;

            var userMsg = new ChatMessage
            {
                Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Role = "user", Content = text.Trim(),
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
            _current.Messages.Add(userMsg);
            _current.Touch();
            Save();
            Raise();

            RunCompletion(false);
        }

        public void Regenerate()
        {
            if (_current == null || Busy) return;
            var lastAssistant = _current.Messages.LastOrDefault(m => m.Role == "assistant");
            if (lastAssistant != null) _current.Messages.Remove(lastAssistant);
            if (!_current.Messages.Any(m => m.Role == "user")) return;
            Save();
            Raise();
            RunCompletion(true);
        }

        public void Cancel()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            SetStatus("已取消");
        }

        private void SetStatus(string s)
        {
            Status = s;
            var h = StatusChanged;
            if (h != null) h(s);
        }

        /// <summary>工具调用确认回调（UI 注入）：(工具名, 说明) => 是否允许执行。</summary>
        public Func<string, string, bool> ConfirmTool;
        /// <summary>AI 工具允许访问的目录白名单（来自 data\trusted-roots.json）。</summary>
        public List<string> AllowedRoots = new List<string>();
        public List<string> AllowedFiles = new List<string>();
        public Func<List<string>> PermissionRoots;
        public Func<bool> FileAccessEnabled;
        private List<string> _savedRoots = new List<string>();
        private bool _permissionsLoaded, _lastFileAccess;
        public void RefreshPermissions()
        {
            if (PermissionRoots == null) return;
            var roots = PermissionRoots() ?? new List<string>();
            bool enabled = FileAccessEnabled == null || FileAccessEnabled();
            bool changed = _permissionsLoaded && (enabled != _lastFileAccess || !roots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).SequenceEqual(_savedRoots.OrderBy(x => x, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));
            if (changed)
            {
                var removed = _savedRoots.Where(x => !roots.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
                AllowedFiles.RemoveAll(x => removed.Any(r => Under(x, r)));
                AllowedRoots.RemoveAll(x => removed.Any(r => Under(x, r)));
                if (!enabled) { AllowedFiles.Clear(); AllowedRoots.Clear(); }
                if (Busy) Cancel();
                SetStatusSafe("文件授权已更新 · " + (enabled ? roots.Count + " 个目录" : "访问已关闭"));
            }
            _savedRoots = roots.ToList(); _lastFileAccess = enabled; _permissionsLoaded = true;
        }
        private static bool Under(string path, string root)
        {
            try { var full = Path.GetFullPath(path).TrimEnd('\\'); var prefix = Path.GetFullPath(root).TrimEnd('\\'); return full.Equals(prefix, StringComparison.OrdinalIgnoreCase) || full.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase); } catch { return false; }
        }
        private List<string> EffectiveRoots() { RefreshPermissions(); return _savedRoots.Concat(AllowedRoots ?? new List<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        /// <summary>自动化引擎（供 workflow.run 工具使用）。</summary>
        public DeskBox.Automation.WorkflowEngine Workflows;
        public Engineering.WorkspaceStore Projects;
        public Sync.LocalSyncCoordinator Sync;
        public Sync.CloudSyncAgent Cloud;
        public Fs.FileOps Files;
        public Action<string> Navigate;
        public Action RefreshApp;
        public Func<Func<ToolResult>,ToolResult> UiInvoke;
        /// <summary>最多连续工具轮次，避免无限循环。</summary>
        public int MaxToolRounds = 2;

        private ChatMessage _activeAssistant;

        private ChatMessage NewAssistant(ChatSession session)
        {
            var assistant = new ChatMessage
            {
                Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Role = "assistant", Content = "", Streaming = true,
                SpeakerName = session.Channel=="hologram" ? session.AssistantName : "",
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
            session.Messages.Add(assistant);
            _activeAssistant = assistant;
            Raise();
            return assistant;
        }

        private void RunCompletion(bool isRegenerate)
        {
            RefreshPermissions();
            var session = _current;
            var provider = ResolveProvider(session);
            if (provider == null)
            {
                AppendError(session, "未配置 AI 服务商，请到「设置 → AI」添加。");
                return;
            }

            var requestCts = CancellationTokenSource.CreateLinkedTokenSource(AiEnvironment.Token); _cts = requestCts;
            var ct = requestCts.Token;
            var model = string.IsNullOrWhiteSpace(session.Model) ? provider.Model : session.Model;

            ActiveSessionId = session.Id;
            Busy = true;
            SetStatus("请求已发送 · 等待服务商响应");
            Raise();

            // Only idle network activity times out; a healthy long stream must not be cancelled.
            long lastActivity = Environment.TickCount;
            int idleTimeout = Math.Max(120000, provider.TimeoutMs);
            bool idleExpired = false;
            bool executingTool = false;
            var watchdog = new System.Threading.Timer(delegate
            {
                try
                {
                    if (!Busy || executingTool || ct.IsCancellationRequested) return;
                    long elapsed = unchecked((uint)(Environment.TickCount - (int)Interlocked.Read(ref lastActivity)));
                    if (elapsed < idleTimeout) return;
                    idleExpired = true;
                    AppLog.Write("warn", "ai.chat", "网络响应长时间无活动，取消当前请求");
                    requestCts.Cancel();
                    SetStatusSafe("等待响应超时 · 正在结束当前请求");
                }
                catch { }
            }, null, 1000, 1000);

            var worker = new Thread(delegate()
            {
                try
                {
                    var client = new AiClient(provider);
                    for (int round = 0; round <= MaxToolRounds; round++)
                    {
                        if (ct.IsCancellationRequested) break;

                        var assistant = NewAssistant(session);
                        var msgs = BuildContext(session);
                        var req = new AiChatRequest { Messages = msgs, Model = model, Temperature = session.Temperature, MaxTokens = session.MaxTokens };
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var answer = new System.Text.StringBuilder();
                        int lastPublish = Environment.TickCount;
                        AppLog.Write("info", "ai.chat", "阶段=开始 round=" + round + " provider=" + provider.Name + " model=" + model);

                        client.ChatStream(req, ct, delegate(AiStreamChunk chunk)
                        {
                            Interlocked.Exchange(ref lastActivity, (long)Environment.TickCount);
                            if (!string.IsNullOrEmpty(chunk.Stage) && Status != chunk.Stage) SetStatusSafe(chunk.Stage);
                            // 错误优先：服务端可能在同一块里同时带 Done 与 Error
                            if (!string.IsNullOrEmpty(chunk.Error))
                            {
                                assistant.Error = AiClient.DescribeError(chunk.ErrorKind, chunk.Error);
                                assistant.ErrorKind = chunk.ErrorKind;
                                return;
                            }
                            if (!string.IsNullOrEmpty(chunk.Delta))
                            {
                                if (answer.Length == 0) SetStatusSafe("正在接收回复 · 流式输出");
                                answer.Append(chunk.Delta);
                                if (assistant.Content.Length == 0 || unchecked((uint)(Environment.TickCount - lastPublish)) >= 50)
                                {
                                    assistant.Content = answer.ToString(); lastPublish = Environment.TickCount;
                                    var h = MessageDelta; if (h != null) h(assistant);
                                }
                            }
                        });

                        assistant.Content = answer.ToString();
                        sw.Stop();
                        assistant.Streaming = false;
                        AppLog.Write("info", "ai.chat", "阶段=ChatStream返回 round=" + round + " content=" + assistant.Content.Length + " err=" + (assistant.Error ?? ""));

                        if (ct.IsCancellationRequested) { assistant.Error = idleExpired ? "等待响应超时，已保留收到的内容" : "已取消，已保留收到的内容"; assistant.ErrorKind = idleExpired ? "timeout" : "cancelled"; SetStatusSafe(assistant.Error); break; }
                        if (!string.IsNullOrEmpty(assistant.Error)) { RecordFailure(provider, model, assistant, sw.ElapsedMilliseconds); break; }
                        if (string.IsNullOrEmpty(assistant.Content))
                        {
                            assistant.Error = "接口未返回内容（协议可能不兼容）";
                            assistant.ErrorKind = "protocol";
                            RecordFailure(provider, model, assistant, sw.ElapsedMilliseconds);
                            break;
                        }

                        SafeLog("info", "对话完成 · " + provider.Name + " / " + model + " · " + sw.ElapsedMilliseconds + "ms");
                        SetStatusSafe("完成 · " + sw.ElapsedMilliseconds + " ms");
                        Save();
                        Raise();

                        // ---- 工具调用：解析 → 白名单 → 确认 → 执行 → 结果回灌 ----
                        var calls = ToolCallParser.Parse(assistant.Content);
                        if (calls.Count == 0) break;
                        if (round == MaxToolRounds)
                        {
                            assistant.ToolName = "round-limit";
                            SafeLog("warn", "工具调用达到轮次上限（" + MaxToolRounds + "），已停止继续调用");
                            break;
                        }
                        executingTool = true;
                        bool any;
                        try { any = ExecuteToolCalls(session, calls, ct); }
                        finally { Interlocked.Exchange(ref lastActivity, (long)Environment.TickCount); executingTool = false; }
                        if (!any) break;
                    }
                }
                catch (Exception ex)
                {
                    var cur = _activeAssistant;
                    if (cur != null)
                    {
                        cur.Streaming = false;
                        cur.Error = Shorten(ex.Message, 300);
                        cur.ErrorKind = "unknown";
                    }
                    SetStatusSafe("失败：" + Shorten(ex.Message, 120));
                    SafeLog("error", "对话异常：" + Shorten(ex.Message, 200));
                }
                finally
                {
                    try { watchdog.Dispose(); } catch { }
                    _activeAssistant = null;
                    session.Touch();
                    Save();
                    if (ReferenceEquals(_cts, requestCts)) _cts = null;
                    requestCts.Dispose();
                    Busy = false;
                    Raise();
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void RecordFailure(AiProvider provider, string model, ChatMessage assistant, long ms)
        {
            SetStatusSafe("失败：" + assistant.Error);
            SafeLog("warn", "对话失败[" + assistant.ErrorKind + "]：" + Shorten(assistant.Error, 120));
        }

        private void SetStatusSafe(string s) { try { SetStatus(s); } catch { } }
        private void SafeLog(string level, string message) { try { _store.Log(level, "ai", message); } catch { } }

        /// <summary>执行工具调用，把结果作为 tool 消息回灌上下文。</summary>
        private bool ExecuteToolCalls(ChatSession session, List<ToolCall> calls, CancellationToken ct)
        {
            if (calls == null || calls.Count == 0) return false;
            var ctx = new ToolContext
            {
                Store = _store,
                AllowedRoots = EffectiveRoots(),
                AllowedFiles = AllowedFiles ?? new List<string>(),
                Cancellation = ct,
                FileAccessEnabled = FileAccessEnabled,
                Ai = AiService.Instance,
                Workflows = Workflows,
                Projects=Projects,Sync=Sync,Cloud=Cloud,Files=Files,Navigate=Navigate,RefreshApp=RefreshApp,UiInvoke=UiInvoke,
                Confirm = delegate(string text) { return true; }   // 高危操作的确认在下面单独处理
            };
            ctx.RefreshAuthorization = delegate { ctx.AllowedRoots = EffectiveRoots(); ctx.AllowedFiles = AllowedFiles.ToList(); };
            bool producedResult = false;
            foreach (var call in calls)
            {
                if (ct.IsCancellationRequested) break;
                var tool = ToolRegistry.Instance.Get(call.Name);
                SetStatusSafe("正在执行工具 · " + call.Name);
                ToolResult result;
                var sw = System.Diagnostics.Stopwatch.StartNew();

                if (tool == null)
                {
                    result = ToolResult.Fail("未注册的工具：" + call.Name);
                }
                else if (tool.RequiresConfirm)
                {
                    var confirm = ConfirmTool;
                    bool allowed = false;
                    string text = "AI 请求执行操作：" + call.Name + "\n\n" + call.Describe();
                    if (confirm != null) { try { allowed = confirm(call.Name, text); } catch { allowed = false; } }
                    result = allowed
                        ? ToolRegistry.Instance.Invoke(call.Name, call.Args, ctx)
                        : ToolResult.Fail("用户拒绝执行该操作");
                }
                else
                {
                    result = ToolRegistry.Instance.Invoke(call.Name, call.Args, ctx);
                }

                sw.Stop();

                var msg = new ChatMessage
                {
                    Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Role = "tool",
                    ToolName = call.Name,
                    Content = result.Ok ? ("✅ " + result.Output) : ("❌ " + result.Error),
                    Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                };
                session.Messages.Add(msg);
                producedResult = true;
                SafeLog(result.Ok ? "info" : "warn",
                    "工具调用 " + call.Name + " · " + (result.Ok ? "成功" : "失败") + " · " + sw.ElapsedMilliseconds + "ms"
                    + (result.Ok ? "" : " · " + Shorten(result.Error, 100)));
                Raise();
            }
            Save();
            return producedResult && !ct.IsCancellationRequested;
        }

        private AiProvider ResolveProvider(ChatSession session)
        {
            if (_providers == null) return null;
            if (!string.IsNullOrEmpty(session.ProviderId))
            {
                var p = _providers.Get(session.ProviderId);
                if (p != null && p.Enabled) return p;
            }
            return _providers.DefaultProvider;
        }

        private List<AiMessage> BuildContext(ChatSession session)
        {
            var allowedRoots = EffectiveRoots();
            var list = new List<AiMessage>();
            var sys = "你是 DeskBox 的桌面助手 AI&SI，也是制药设备行业的自动化与计算机化系统工程助手。回答简洁、准确、可执行。"
                    + "用户主要从事自动化、DCS、Siemens PLC、系统工程和制药设备验证。"
                    + DomainInstructions(session == null ? "automation" : session.DomainMode)
                    + "涉及 GxP、GAMP 5、21 CFR Part 11、数据完整性或验证结论时，必须区分法规要求、行业最佳实践与建议，不得编造条款号。"
                    + "涉及 PLC/DCS 在线修改、联锁、报警、配方或生产数据时，先提示备份、差异审查、变更控制、测试与回退方案；不要把未验证代码描述为可直接投产。"
                    + "当需要读写文件或执行操作时，只使用用户授权的目录，并在高风险操作前明确说明。\r\n"
                    + "你不能直接执行系统命令或任意代码；需要操作文件、任务或工作流时，只能通过下面注册的工具。\r\n"
                    + ToolRegistry.Instance.DescribeForPrompt();
            sys += "\r\n" + DocumentSkills.Instructions;
            if(session!=null && session.Channel=="hologram")sys+="\r\n继续全息会话。助手名称："+session.AssistantName+"；角色设定："+session.Persona;
            sys += "\r\n你已连接DeskBox真实应用数据。查询任务先用task.list，项目用project.list，现场事件用incident.list，应用状态用app.context；不要未经调用就声称无法获取软件数据。修改前先读取当前ID和版本校验值，使用对应工具并报告真实执行结果。不要把文件权限开关与软件内部任务/项目读取混为一谈。";
            sys += "\r\n授权以本轮系统消息为准；历史对话中的旧授权状态不代表当前权限。可调用工具验证当前授权，不要未经工具就声称权限未更新。";
            if (AllowedFiles != null && AllowedFiles.Count > 0) sys += "\r\n用户本次明确选择可读取的文件（不授权其同目录其他文件）：\r\n" + string.Join("\r\n", AllowedFiles.ToArray());
            if (FileAccessEnabled != null && !FileAccessEnabled())
            {
                sys += "\r\n当前 AI 文件访问开关已关闭，所有文件工具都会拒绝访问。请按最新权限说明回答，不要依据旧对话里的授权信息。";
            }
            else if (allowedRoots.Count > 0)
            {
                sys += "\r\n当前已授权目录（只能在这些目录内操作）：";
                foreach (var r in allowedRoots) sys += "\r\n- " + r;
            }
            else if (AllowedFiles != null && AllowedFiles.Count > 0)
            {
                sys += "\r\n当前仅有上述单文件授权：可用 doc.read 读取所选文件，或把 doc.search 的 root 设置为该文件以搜索正文；未授权任何目录，不能搜索同目录其他文件或创建新路径。";
            }
            else
            {
                sys += "\r\n当前没有授权目录：涉及文件的工具调用会被拒绝，请提示用户先在设置中授权目录。";
            }
            list.Add(new AiMessage { Role = "system", Content = sys });

            // 最近 20 条，避免上下文过长；工具结果以 user 角色回灌，模型才能看到
            var recent = session.Messages.Where(m => m.Role == "user"
                                                   || m.Role == "tool"
                                                   || (m.Role == "assistant" && !string.IsNullOrEmpty(m.Content)))
                                         .TakeLastCompat(20).ToList();
            foreach (var m in recent)
            {
                if (m.Role == "tool")
                    list.Add(new AiMessage { Role = "user", Content = "[工具 " + m.ToolName + " 的执行结果]\r\n" + m.Content });
                else
                    list.Add(new AiMessage { Role = m.Role, Content = m.Content });
            }
            return list;
        }

        private static string DomainInstructions(string mode)
        {
            switch ((mode ?? "automation").ToLowerInvariant())
            {
                case "plc": return "当前模式：Siemens PLC。优先使用 TIA Portal、S7-1200/1500、SCL/LAD/FBD、PROFINET、诊断缓冲区、数据块和状态机语境；说明版本与 CPU 型号相关假设。";
                case "dcs": return "当前模式：DCS/过程控制。优先关注控制回路、报警管理、联锁因果、批次、冗余、历史数据与故障安全。";
                case "validation": return "当前模式：CSV/验证。输出应便于形成 URS、风险评估、DQ/IQ/OQ/PQ、追溯矩阵、测试步骤、接受标准和证据清单。";
                case "troubleshoot": return "当前模式：故障诊断。先给出事实/症状、可能原因、低风险验证步骤、风险边界和回退点；不要跳过测量与证据。";
                case "general": return "当前模式：通用工程。根据上下文选择合适专业深度。";
                default: return "当前模式：自动化系统工程。综合考虑 PLC、DCS、仪表、网络、数据完整性、验证和全生命周期维护。";
            }
        }

        private void AppendError(ChatSession session, string message)
        {
            session.Messages.Add(new ChatMessage
            {
                Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Role = "assistant", Content = "", Error = message, ErrorKind = "config",
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            });
            Save();
            Raise();
        }

        private static string Shorten(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        private void Raise()
        {
            var h = Changed;
            if (h != null) h();
        }

        // ---------- 持久化 ----------

        public void Save()
        {
            try
            {
                lock (_lock)
                {
                    var trimmed = _sessions.OrderByDescending(s => s.UpdatedAt).Take(50).ToList();
                    foreach (var s in trimmed)
                    {
                        if (s.Messages.Count > 200) s.Messages = s.Messages.Skip(s.Messages.Count - 200).ToList();
                    }
                    Json.Save(SessionsFile, trimmed);
                }
            }
            catch (Exception ex) { AppLog.Write("error", "ai", "保存对话失败: " + ex.Message); }
        }

        public void Load()
        {
            try
            {
                var list = Json.Load<List<ChatSession>>(SessionsFile, null);
                if (list == null) return;
                foreach(var session in list.Where(s=>s!=null && s.Channel=="hologram"))
                    foreach(var message in session.Messages.Where(m=>m.Streaming)){
                        message.Streaming=false;message.Interrupted=message.Role=="assistant";
                    }
                lock (_lock)
                {
                    _sessions.Clear();
                    _sessions.AddRange(list.Where(s => s != null && !string.IsNullOrEmpty(s.Id)));
                }
            }
            catch (Exception ex) { AppLog.Write("warn", "ai", "读取对话失败: " + ex.Message); }
        }
    }

    internal static class LlmExt
    {
        public static IEnumerable<T> TakeLastCompat<T>(this IEnumerable<T> src, int n)
        {
            var list = src.ToList();
            return list.Count <= n ? list : list.Skip(list.Count - n);
        }
    }
}
