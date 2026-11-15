const fs = require('fs');
const p = 'src/Ai/ChatService.cs';
let s = fs.readFileSync(p, 'utf8');

const startMarker = '        private void RunCompletion(bool isRegenerate)';
const endMarker = '        private AiProvider ResolveProvider(ChatSession session)';
const start = s.indexOf(startMarker);
const end = s.indexOf(endMarker);
if (start < 0 || end < 0) { console.log('MARKER NOT FOUND'); process.exit(1); }

const newBlock = `        /// <summary>工具调用确认回调（UI 注入）：(工具名, 说明) => 是否允许执行。</summary>
        public Func<string, string, bool> ConfirmTool;
        /// <summary>AI 工具允许访问的目录白名单（来自 data\\trusted-roots.json）。</summary>
        public List<string> AllowedRoots = new List<string>();
        /// <summary>自动化引擎（供 workflow.run 工具使用）。</summary>
        public DeskBox.Automation.WorkflowEngine Workflows;
        /// <summary>最多连续工具轮次，避免无限循环。</summary>
        public int MaxToolRounds = 2;

        private ChatMessage _activeAssistant;

        private ChatMessage NewAssistant(ChatSession session)
        {
            var assistant = new ChatMessage
            {
                Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Role = "assistant", Content = "", Streaming = true,
                Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
            };
            session.Messages.Add(assistant);
            _activeAssistant = assistant;
            Raise();
            return assistant;
        }

        private void RunCompletion(bool isRegenerate)
        {
            var session = _current;
            var provider = ResolveProvider(session);
            if (provider == null)
            {
                AppendError(session, "未配置 AI 服务商，请到「设置 → AI」添加。");
                return;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var model = string.IsNullOrWhiteSpace(session.Model) ? provider.Model : session.Model;

            Busy = true;
            SetStatus("思考中…");
            Raise();

            // 看门狗：任何情况下都不会让 UI 永久停留在“发送中”（规格十三）
            var watchdog = new System.Threading.Timer(delegate
            {
                try
                {
                    if (!Busy) return;
                    AppLog.Write("warn", "ai.chat", "看门狗触发：请求 30 秒未结束，强制解除忙碌状态");
                    try { if (_cts != null) _cts.Cancel(); } catch { }
                    var cur = _activeAssistant;
                    if (cur != null && string.IsNullOrEmpty(cur.Error))
                    {
                        cur.Error = "请求超时（超过 30 秒未返回）";
                        cur.ErrorKind = "timeout";
                        cur.Streaming = false;
                    }
                    Busy = false;
                    SetStatus("已超时取消");
                    Save();
                    Raise();
                }
                catch { }
            }, null, 30000, Timeout.Infinite);

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
                        var req = new AiChatRequest { Messages = msgs, Model = model, Temperature = 0.7 };
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        AppLog.Write("info", "ai.chat", "阶段=开始 round=" + round + " provider=" + provider.Name + " model=" + model);

                        client.ChatStream(req, ct, delegate(AiStreamChunk chunk)
                        {
                            // 错误优先：服务端可能在同一块里同时带 Done 与 Error
                            if (!string.IsNullOrEmpty(chunk.Error))
                            {
                                assistant.Error = AiClient.DescribeError(chunk.ErrorKind, chunk.Error);
                                assistant.ErrorKind = chunk.ErrorKind;
                                return;
                            }
                            if (!string.IsNullOrEmpty(chunk.Delta))
                            {
                                assistant.Content += chunk.Delta;
                                var h = MessageDelta;
                                if (h != null) h(assistant);
                            }
                        });

                        sw.Stop();
                        assistant.Streaming = false;
                        AppLog.Write("info", "ai.chat", "阶段=ChatStream返回 round=" + round + " content=" + assistant.Content.Length + " err=" + (assistant.Error ?? ""));

                        if (ct.IsCancellationRequested) { assistant.Error = "已取消"; break; }
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
                        bool any = ExecuteToolCalls(session, calls, ct);
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
                    Busy = false;
                    _activeAssistant = null;
                    session.Touch();
                    Save();
                    Raise();
                    try { if (_cts != null) _cts.Dispose(); } catch { }
                    _cts = null;
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
                AllowedRoots = AllowedRoots ?? new List<string>(),
                Ai = AiService.Instance,
                Workflows = Workflows,
                Confirm = delegate(string text) { return true; }   // 高危操作的确认在下面单独处理
            };
            bool any = false;
            foreach (var call in calls)
            {
                if (ct.IsCancellationRequested) break;
                var tool = ToolRegistry.Instance.Get(call.Name);
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
                    string text = "AI 请求执行操作：" + call.Name + "\\n\\n" + call.Describe();
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
                any = any || result.Ok;

                var msg = new ChatMessage
                {
                    Id = "m_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Role = "tool",
                    ToolName = call.Name,
                    Content = result.Ok ? ("✅ " + result.Output) : ("❌ " + result.Error),
                    Ts = DateTimeOffset.Now.ToUnixTimeMilliseconds()
                };
                session.Messages.Add(msg);
                SafeLog(result.Ok ? "info" : "warn",
                    "工具调用 " + call.Name + " · " + (result.Ok ? "成功" : "失败") + " · " + sw.ElapsedMilliseconds + "ms"
                    + (result.Ok ? "" : " · " + Shorten(result.Error, 100)));
                Raise();
            }
            Save();
            return any;
        }

`;

s = s.slice(0, start) + newBlock + s.slice(end);
fs.writeFileSync(p, s, 'utf8');
console.log('RunCompletion replaced, tool loop:', s.includes('ExecuteToolCalls'), '| parser used:', s.includes('ToolCallParser'));
