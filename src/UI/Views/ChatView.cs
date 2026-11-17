using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>AI&SI 对话界面：会话管理、流式消息、Markdown 渲染、参数栏。</summary>
    public class ChatView : ViewBase
    {
        private ChatService _chat;
        private ProviderStore _providers;

        private DarkList _sessions;
        private MessageBoard _board;
        private DarkInput _input, _modelInput, _maxTokens;
        private DarkSelect _providerSel, _tempSel, _modeSel;
        private DarkButton _btnNew, _btnSend, _btnStop, _btnRegen, _btnCopy, _btnClear, _btnParams;
        private Panel _params;
        private Label _status, _ctxInfo;
        private bool _paramsOpen = true;
        private bool _busyUi;
        private ChatMessage _pendingDelta;
        private System.Windows.Forms.Timer _streamUi;
        private Label _execution, _referenceContext;
        private Panel _referenceCore, _referenceRight;
        private DarkList _referenceFiles;
        private string _displayedSessionId;
        private DarkButton _coreStop;
        private string _coreTitle = "待命", _coreDetail = "";
        private Color _coreColor;
        private bool _coreMoving;
        private Panel _referenceToolbar, _sessionHost, _composerFooter, _chatCenter;
        private Label _sessionCaption, _conversationCaption;
        private DarkInput _sessionSearch;

        public event EventHandler SettingsRequested;

        public ChatView(Store store) : base(store, "AI&SI AI", "多轮对话、流式输出、可随时停止；未配置服务商时请先到设置中添加")
        {
            Build();
            _streamUi = new System.Windows.Forms.Timer { Interval = 80 };
            _streamUi.Tick += delegate { FlushStreaming(); if (_coreMoving && _referenceCore != null && _referenceCore.Visible) _referenceCore.Invalidate(); };
            _streamUi.Start();
        }

        public void SetServices(ChatService chat, ProviderStore providers)
        {
            if (_chat != null)
            {
                _chat.Changed -= OnChatChanged;
                _chat.MessageDelta -= OnDelta;
                _chat.StatusChanged -= OnStatus;
            }
            _chat = chat;
            _providers = providers;
            if (_chat != null)
            {
                _chat.PermissionRoots = DeskBox.UI.TrustedRoots.Load;
                _chat.FileAccessEnabled = delegate { return Store.Settings.AiFileAccess; };
                _chat.RefreshPermissions();
                _chat.ConfirmTool = delegate(string toolName, string detail)
                {
                    return DeskBox.UI.ToolConfirm.Ask(this, toolName, detail, Palette);
                };
            }
            if (_chat != null)
            {
                _chat.Changed += OnChatChanged;
                _chat.MessageDelta += OnDelta;
                _chat.StatusChanged += OnStatus;
            }
            Reload();
        }

        // ---------------- UI ----------------

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Palette.Bg };
            _referenceToolbar=top;
            _btnNew = new DarkButton("＋ 新对话") { Left = 0, Top = 2, Width = 92 };
            _btnNew.Click += delegate { if (_chat != null) { _chat.NewSession(); ReloadSessionList(); } };
            _btnStop = new DarkButton("停止") { Left = 100, Top = 2, Width = 64 };
            _btnStop.Click += delegate { if (_chat != null) _chat.Cancel(); };
            _btnRegen = new DarkButton("重新生成") { Left = 172, Top = 2, Width = 84 };
            _btnRegen.Click += delegate { if (_chat != null) _chat.Regenerate(); };
            _btnCopy = new DarkButton("复制回复") { Left = 264, Top = 2, Width = 84 };
            _btnCopy.Click += delegate { CopyLast(); };
            _btnClear = new DarkButton("清空会话") { Left = 356, Top = 2, Width = 84 };
            _btnClear.Click += delegate { ClearSession(); };
            _btnParams = new DarkButton("参数栏") { Left = 448, Top = 2, Width = 70 };
            _btnParams.Click += delegate { ToggleParams(); };

            _providerSel = new DarkSelect(new string[0]) { Left = 526, Top = 2, Width = 180 };
            _providerSel.Palette = Palette;
            _providerSel.SelectedIndexChanged += delegate { ApplyProviderSelection(); };

            _modelInput = new DarkInput { Left = 714, Top = 2, Width = 200, Placeholder = "模型名，如 deepseek-chat" };
            _modelInput.Palette = Palette;
            _modelInput.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) ApplyProviderSelection(); };

            _status = new Label
            {
                Left = 926, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg, Text = ""
            };
            top.Controls.AddRange(new Control[] { _btnNew, _btnStop, _btnRegen, _btnCopy, _btnClear, _btnParams, _providerSel, _modelInput, _status });

            // 会话列表
            var left = new Panel { Dock = DockStyle.Left, Width = 220, BackColor = Palette.Bg, Padding = new Padding(0, 4, 6, 0) };
            _sessionHost=left;
            _sessionSearch=new DarkInput{Dock=DockStyle.Top,Height=36,Palette=Palette,Placeholder="搜索历史会话"};_sessionSearch.Inner.TextChanged+=delegate{ReloadSessionList();};
            var lblS = new Label
            {
                Text = "会话", Dock = DockStyle.Top, Height = 22, ForeColor = Palette.TextDim,
                BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 8.5F)
            };
            _sessionCaption = lblS;
            _sessions = new DarkList { Dock = DockStyle.Fill };
            _sessions.Palette = Palette;
            _sessions.SetColumns(new[] { "标题", "时间" }, new[] { 140, 56 });
            _sessions.RowActivated += delegate { SwitchSession(); };
            _sessions.RowContextMenu += delegate(object s, MouseEventArgs e) { ShowSessionMenu(e.Location); };
            left.Controls.Add(_sessionSearch);
            left.Controls.Add(_sessions);
            left.Controls.Add(lblS);
            _sessions.BringToFront();

            // 右侧参数栏
            _params = new Panel { Dock = DockStyle.Right, Width = 240, BackColor = Palette.Bg, Padding = new Padding(6, 4, 0, 0), AutoScroll = true };
            BuildParams(_params);

            // 中间对话区
            var center = _chatCenter = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg };
            _board = new MessageBoard(Palette) { Dock = DockStyle.Fill };
            _board.CopyRequested += delegate(string text) { CopyText(text); };

            var inputWrap = new Panel { Dock = DockStyle.Bottom, Height = 118, BackColor = Palette.Bg, Padding = new Padding(0, 6, 0, 0) };
            _input = new DarkInput { Dock = DockStyle.Fill, Placeholder = "输入消息，Enter 发送，Shift+Enter 换行" };
            _input.Palette = Palette;
            _input.Inner.Multiline = true;
            _input.Inner.ScrollBars = ScrollBars.Vertical;
            _input.Inner.Font = new Font("Microsoft YaHei UI", 9.5F);
            _input.KeyPressed += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    Send();
                }
            };
            var bottom = _composerFooter = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Palette.Bg };
            _ctxInfo = new Label
            {
                Left = 2, Top = 8, AutoSize = true, ForeColor = Palette.TextFaint,
                Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg, Text = ""
            };
            _btnSend = new DarkButton("发送") { Primary = true, Left = 0, Top = 3, Width = 88, Height = 28 };
            _btnSend.Click += delegate { Send(); };
            bottom.Controls.Add(_ctxInfo);
            bottom.Controls.Add(_btnSend);
            bottom.Resize += delegate { _btnSend.Left = Math.Max(120, bottom.Width - 96); };

            inputWrap.Controls.Add(_input);
            inputWrap.Controls.Add(bottom);
            center.Controls.Add(_board);
            center.Controls.Add(inputWrap);

            Body.Controls.Add(center);
            Body.Controls.Add(_params);
            Body.Controls.Add(left);
            _execution = new Label { UseMnemonic = false, Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 7, 0, 0), ForeColor = Palette.Accent, BackColor = Palette.HeaderBg, Text = "AI&SI · 就绪，等待输入" };
            Body.Controls.Add(_execution);
            Body.Controls.Add(top);

            UpdateButtons();
        }

        private void BuildParams(Panel host)
        {
            int y = 4;
            host.Controls.Add(new Label { Text = "工程上下文", Left = 4, Top = y, AutoSize = true, ForeColor = Palette.TextDim, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), BackColor = Palette.Bg });
            y += 28;

            _modeSel = new DarkSelect(new[] { "自动化系统", "Siemens PLC", "DCS / 过程控制", "CSV / 制药验证", "故障诊断", "通用工程" }) { Left = 4, Top = y, Width = 210 };
            _modeSel.Palette = Palette; _modeSel.SelectedIndex = 0; host.Controls.Add(_modeSel);
            y += 42;

            host.Controls.Add(new Label { Text = "生成严谨度 / Temperature", Left = 4, Top = y, AutoSize = true, ForeColor = Palette.TextFaint, BackColor = Palette.Bg });
            y += 20;
            _tempSel = new DarkSelect(new[] { "0.0 严谨", "0.3 保守", "0.7 平衡", "1.0 发散", "1.5 最大" }) { Left = 4, Top = y, Width = 200 };
            _tempSel.Palette = Palette;
            _tempSel.SelectedIndex = 2;
            host.Controls.Add(_tempSel);
            y += 40;

            host.Controls.Add(new Label { Text = "最大 Tokens（0=不限制）", Left = 4, Top = y, AutoSize = true, ForeColor = Palette.TextFaint, BackColor = Palette.Bg });
            y += 20;
            _maxTokens = new DarkInput { Left = 4, Top = y, Width = 200, Placeholder = "0" };
            _maxTokens.Palette = Palette;
            _maxTokens.Text = "0";
            host.Controls.Add(_maxTokens);
            y += 44;

            var btnSettings = new DarkButton("AI 服务商设置…") { Left = 4, Top = y, Width = 200 };
            btnSettings.Click += delegate { var h = SettingsRequested; if (h != null) h(this, EventArgs.Empty); };
            host.Controls.Add(btnSettings);
            y += 44;

            var hologram = new DarkButton("全息 AI 通话") { Left = 4, Top = y, Width = 200 };
            hologram.Click += delegate { var main = FindForm() as MainForm; if (main != null) main.ShowLiveVoice(); };
            host.Controls.Add(hologram);
            y += 44;

            host.Controls.Add(new Label { Text = "工程快捷提示", Left = 4, Top = y, AutoSize = true, ForeColor = Palette.TextDim, Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold), BackColor = Palette.Bg });
            y += 26;
            AddPromptButton(host, "PLC 故障树", "请按“现象→可能原因→低风险检查→修复→回退验证”的结构分析以下 PLC 故障：", 4, y);
            AddPromptButton(host, "OQ 用例", "请为以下功能生成制药设备 OQ 测试用例，包含前置条件、步骤、预期结果、接受标准和证据：", 112, y);
            y += 36;
            AddPromptButton(host, "联锁审查", "请审查以下报警/联锁逻辑，指出失效模式、旁路风险、复位条件和需要验证的边界：", 4, y);
            AddPromptButton(host, "SCL 审查", "请审查以下 Siemens SCL 代码，重点检查状态机、边沿、定时器、越界、故障安全和可测试性：", 112, y);
            y += 44;

            var attach = new DarkButton("添加文档") { Left = 4, Top = y, Width = 100, Palette = Palette };
            attach.Click += delegate { AttachDocument(); }; host.Controls.Add(attach);
            var skills = new DarkButton("文档技能") { Left = 112, Top = y, Width = 100, Palette = Palette };
            skills.Click += delegate { MessageBox.Show(this, DocumentSkills.Instructions + "\r\n运行环境：Python 3.12+；PDF/PyMuPDF，Excel/openpyxl。", "AI&SI 文档技能", MessageBoxButtons.OK, MessageBoxIcon.Information); }; host.Controls.Add(skills);
            y += 40;
            var tip = new Label
            {
                Left = 4, Top = y, AutoSize = true, MaximumSize = new Size(208, 0),
                ForeColor = Palette.TextFaint, BackColor = Palette.Bg,
                Font = new Font("Microsoft YaHei UI", 8F),
                Text = "工程安全：\n· AI 输出不是投产批准\n· 在线修改前先备份、差异审查和回退\n· GxP 结论需由批准流程确认\n· 本地文件操作需先授权目录\n· 4–20mA / S7 / Modbus 可调用确定性工具"
            };
            host.Controls.Add(tip);
        }
        public override void UseReferenceLayout()
        {
            base.UseReferenceLayout();
            if (_referenceContext != null) return;
            _referenceToolbar.Hide();
            _execution.Hide();
            _sessionHost.Show();

            _sessionCaption.Text = "会话历史";
            _sessionCaption.Height = 30;
            _sessionCaption.Font = UiKit.CachedFont("Microsoft YaHei UI", 11, FontStyle.Bold);
            _sessionSearch.UseDrawnPlaceholder = true;
            _sessionHost.Padding = new Padding(8);
            _sessionHost.BackColor = Palette.CardBg;
            _sessions.InsetRows = true;
            _sessions.StackFirstTwoCells = true;
            _sessions.ShowHeader = false;
            _sessions.RowHeight = 44;
            _chatCenter.Padding = new Padding(10, 0, 10, 0);
            _board.UseCardSurface = true;
            _conversationCaption = new Label { Dock = DockStyle.Top, Height = 34, UseMnemonic = false,
                AutoEllipsis = true, Padding = new Padding(10, 8, 0, 0), ForeColor = Palette.Text };
            _chatCenter.Controls.Add(_conversationCaption);

            var composeHint = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Palette.TextDim,
                Text = "输入工程问题 · Enter 发送 · Shift+Enter 换行", UseMnemonic = false };
            _input.Parent.Controls.Add(composeHint);
            var attach = new DarkButton("附件") { Left = 0, Top = 3, Width = 66, Height = 28, Palette = Palette };
            attach.Click += delegate { AttachDocument(); };
            _composerFooter.Controls.Add(attach);
            _ctxInfo.Left = 75;

            _referenceRight = new ReferenceCard(Palette) { Dock = DockStyle.Right, Width = 265, Padding = new Padding(10) };
            var contextTitle = new Label { Dock = DockStyle.Top, Height = 29, Text = "工程上下文",
                ForeColor = Palette.Text, Font = UiKit.CachedFont("Microsoft YaHei UI", 11, FontStyle.Bold) };
            _referenceContext = new Label { Dock = DockStyle.Top, Height = 75,
                AutoEllipsis = true, UseMnemonic = false, ForeColor = Palette.TextDim };
            _referenceFiles = new DarkList { Dock = DockStyle.Top, Height = 147, RowHeight = 42, Palette = Palette,
                CardSurface = true, InsetRows = true, ShowHeader = false, AutoFitColumns = true };
            _referenceFiles.SetColumns(new[] { "所选文件" }, new[] { 180 });
            Body.Controls.Remove(_params);
            _params.Dock = DockStyle.Fill;
            _paramsOpen = false;
            _params.Hide();
            _referenceRight.Controls.Add(_params);
            _referenceRight.Controls.Add(_referenceFiles);
            _referenceRight.Controls.Add(_referenceContext);
            _referenceRight.Controls.Add(contextTitle);
            Body.Controls.Add(_referenceRight);
            Body.Controls.SetChildIndex(_referenceRight, Body.Controls.GetChildIndex(_sessionHost));
            Body.Resize += delegate { SizeReferencePanels(); };

            _referenceCore = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Palette.CardBg,
                AccessibleName = "智能中枢" };
            _referenceCore.Cursor = Cursors.Hand;
            _referenceCore.Click += delegate { FocusInput(); };
            _coreStop = new DarkButton("停止回复") { Dock = DockStyle.Right, Width = 100, Palette = Palette };
            _coreStop.Click += delegate { if (_chat != null && _chat.Busy) _chat.Cancel(); };
            _referenceCore.Controls.Add(_coreStop);
            _referenceCore.Paint += PaintCore;
            _chatCenter.Controls.Add(_referenceCore);
            _chatCenter.Controls.SetChildIndex(_conversationCaption, _chatCenter.Controls.Count - 1);
            SizeReferencePanels();
            RefreshEngineeringContext();
        }

        private void SizeReferencePanels()
        {
            int width = Body.ClientSize.Width;
            _sessionHost.Width = Math.Max(145, Math.Min(265, width * 17 / 100));
            _referenceRight.Width = Math.Max(190, Math.Min(293, width * 23 / 100));
            _referenceFiles.Height = Body.ClientSize.Height < 550 ? 100 : 147;
        }

        private void RefreshCore()
        {
            if (_referenceCore == null) return;
            var session = _chat == null ? null : _chat.Current;
            bool active = session != null && _chat.Busy && session.Id == _chat.ActiveSessionId;
            var latest = session == null ? null : session.Messages.LastOrDefault(m => m.Role == "assistant");
            string status = active ? (_chat.Status ?? "") : "";
            _coreMoving = false;
            _coreColor = Palette.Accent;
            if (active && status.StartsWith("已取消", StringComparison.Ordinal))
            {
                _coreTitle = "正在停止";
                _coreDetail = "保留已收到的内容";
            }
            else if (active)
            {
                _coreMoving = true;
                if (status.StartsWith("正在执行工具", StringComparison.Ordinal))
                {
                    _coreTitle = "正在执行工程工具";
                    _coreDetail = status;
                    _coreColor = Color.Gold;
                }
                else if (latest != null && latest.Streaming && !string.IsNullOrEmpty(latest.Content))
                {
                    _coreTitle = "正在接收回复";
                    _coreDetail = "已收到 " + latest.Content.Length + " 字符";
                }
                else
                {
                    _coreTitle = "正在等待回复";
                    _coreDetail = string.IsNullOrWhiteSpace(status) ? "等待服务商响应" : status;
                }
            }
            else if (latest != null && latest.ErrorKind == "cancelled")
            {
                _coreTitle = "已停止";
                _coreDetail = "点击中枢继续输入";
            }
            else if (latest != null && !string.IsNullOrEmpty(latest.Error))
            {
                _coreTitle = "回复失败";
                _coreDetail = latest.Error;
                _coreColor = Color.IndianRed;
            }
            else if (latest != null && !string.IsNullOrEmpty(latest.Content))
            {
                _coreTitle = "回复完成";
                _coreDetail = "点击中枢继续输入";
            }
            else
            {
                _coreTitle = "待命";
                _coreDetail = "点击中枢输入工程问题";
            }
            _coreStop.Visible = active;
            _coreStop.Enabled = active && _coreMoving;
            _referenceCore.AccessibleDescription = _coreTitle + " · " + _coreDetail;
            _referenceCore.Invalidate();
            if (_conversationCaption != null)
                _conversationCaption.Text = session == null ? "新对话" : session.Title +
                    (string.IsNullOrWhiteSpace(session.Model) ? "" : "  ·  " + session.Model);
        }

        private void PaintCore(object sender, PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var faint = new Pen(Color.FromArgb(75, _coreColor), 2))
            using (var accent = new Pen(_coreColor, 2))
            using (var brush = new SolidBrush(_coreColor))
            {
                var ring = new Rectangle(11, 12, 26, 26);
                graphics.DrawEllipse(faint, ring);
                if (_coreMoving) graphics.DrawArc(accent, ring, (Environment.TickCount & int.MaxValue) % 1200 * .3f, 125);
                else graphics.DrawEllipse(accent, 15, 16, 18, 18);
                graphics.FillEllipse(brush, 21, 22, 6, 6);
            }
            int width = Math.Max(1, _referenceCore.ClientSize.Width - 56 - (_coreStop.Visible ? _coreStop.Width : 0));
            var format = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(graphics, "智能中枢 · " + _coreTitle, Font,
                new Rectangle(47, 6, width, 18), Palette.Text, format);
            TextRenderer.DrawText(graphics, _coreDetail, Font,
                new Rectangle(47, 26, width, 18), Palette.TextDim, format);
        }

        private void RefreshEngineeringContext()
        {
            if (_referenceContext == null) return;
            var project = _chat == null || _chat.Projects == null ? null : _chat.Projects.Current;
            var files = _chat == null ? new List<string>() : _chat.AllowedFiles;
            _referenceContext.Text = (project == null ? "未选择工程项目" :
                project.Name + " · " + DeskBox.Engineering.ValidationProgress.Display(project)) + "\r\n\r\n" +
                (Store.Settings.AiFileAccess ? "当前已选文件 " + files.Count + " 个" : "AI 文件访问已关闭");
            _referenceFiles.SetRows(files.Select(path => new ListRow { Cells = new[] { System.IO.Path.GetFileName(path) }, Tag = path }).ToList());
            _referenceFiles.Visible = files.Count != 0;
            RefreshCore();
        }

        public void ToggleReferenceOptions()
        {
            _paramsOpen = !_paramsOpen;
            _params.Visible = _paramsOpen;
        }

        private void AddPromptButton(Panel host, string title, string prompt, int x, int y)
        {
            var b = new DarkButton(title) { Left = x, Top = y, Width = 100, Height = 28 };
            b.Click += delegate { _input.Text = prompt + "\r\n\r\n"; FocusInput(); };
            host.Controls.Add(b);
        }

        private void AttachDocument()
        {
            if (_chat == null) return;
            using (var dialog = new OpenFileDialog { Title = "选择提供给 AI&SI 的文档（仅授权所选文件）", Multiselect = true, Filter = "支持文档|*.pdf;*.docx;*.xlsx;*.xlsm;*.vsdx;*.txt;*.md;*.markdown;*.csv;*.tsv|所有文件|*.*" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var path in dialog.FileNames) if (!_chat.AllowedFiles.Contains(path, StringComparer.OrdinalIgnoreCase)) _chat.AllowedFiles.Add(path);
                _input.Text = "请使用 doc.read 读取以下文档的真实内容，必要时分段继续读取；回答时引用页码、段落或工作表/行：\r\n" + string.Join("\r\n", dialog.FileNames);
                RefreshEngineeringContext();
                _execution.Text = Store.Settings.AiFileAccess ? "已选择 " + dialog.FileNames.Length + " 个文档，点击发送后开始读取。仅授权所选文件。" : "已选中文档；AI 文件访问总开关仍关闭，请在设置 → 文件与隐私中启用。"; FocusInput();
            }
        }

        private void ToggleParams()
        {
            _paramsOpen = !_paramsOpen;
            _params.Visible = _paramsOpen;
        }

        // ---------------- 数据绑定 ----------------

        public void Reload()
        {
            if (!IsHandleCreated) return;
            ReloadSessionList();
            ReloadProviders();
            ReloadGenerationOptions();
            ReloadMessages();
            UpdateButtons();
        }

        private void ReloadGenerationOptions()
        {
            if (_chat == null || _chat.Current == null || _modeSel == null) return;
            switch ((_chat.Current.DomainMode ?? "automation").ToLowerInvariant())
            {
                case "plc": _modeSel.SelectedIndex = 1; break;
                case "dcs": _modeSel.SelectedIndex = 2; break;
                case "validation": _modeSel.SelectedIndex = 3; break;
                case "troubleshoot": _modeSel.SelectedIndex = 4; break;
                case "general": _modeSel.SelectedIndex = 5; break;
                default: _modeSel.SelectedIndex = 0; break;
            }
            double t = _chat.Current.Temperature;
            _tempSel.SelectedIndex = t <= 0.05 ? 0 : t <= 0.35 ? 1 : t <= 0.8 ? 2 : t <= 1.2 ? 3 : 4;
            _maxTokens.Text = _chat.Current.MaxTokens.ToString();
        }

        private void ReloadProviders()
        {
            try
            {
                var list = _providers == null ? new List<AiProvider>() : _providers.Providers.Where(p => p.Enabled).ToList();
                var names = list.Select(p => p.Name + " · " + (string.IsNullOrEmpty(p.Model) ? "未设模型" : p.Model)).ToArray();
                _providerSel.Inner.Items.Clear();
                _providerSel.Inner.Items.AddRange(names);
                var cur = _chat == null ? null : _chat.Current;
                int idx = 0;
                if (cur != null && _providers != null)
                {
                    var p = _providers.Get(cur.ProviderId);
                    if (p != null) idx = Math.Max(0, list.FindIndex(x => x.Id == p.Id));
                }
                if (names.Length > 0) _providerSel.SelectedIndex = idx;
                if (cur != null && !string.IsNullOrEmpty(cur.Model)) _modelInput.Text = cur.Model;
                else if (list.Count > 0 && string.IsNullOrEmpty(_modelInput.Text)) _modelInput.Text = list[0].Model;
            }
            catch { }
        }

        private void ReloadSessionList()
        {
            var rows = new List<ListRow>();
            if (_chat != null)
            {
                foreach (var s in _chat.Sessions.Where(session=>_sessionSearch==null||_sessionSearch.Text.Length==0||session.Title.IndexOf(_sessionSearch.Text,StringComparison.OrdinalIgnoreCase)>=0))
                {
                    rows.Add(new ListRow
                    {
                        Icon = s.Channel=="hologram" ? "◉" : "💬",
                        Cells = new[] { s.Title, UiKit.HumanTime(s.UpdatedAt) },
                        Tag = s.Id,
                        Accent = _chat.Current != null && _chat.Current.Id == s.Id ? Palette.Accent : (Color?)null
                    });
                }
            }
            _sessions.SetRows(rows);
            if (_referenceContext != null && _chat != null && _chat.Current != null)
                _sessions.SelectIndex(rows.FindIndex(row => (string)row.Tag == _chat.Current.Id));
        }

        private void ReloadMessages()
        {
            if (_chat == null || _chat.Current == null) { _board.SetMessages(new List<ChatMessage>()); return; }
            bool switched = _displayedSessionId != _chat.Current.Id;
            _displayedSessionId = _chat.Current.Id;
            _board.SetMessages(_chat.Current.Messages);
            if (switched) _board.ScrollToEnd();
            _ctxInfo.Text = (_chat.Current.Messages.Count) + " 条消息 · 上下文 " + Math.Min(20, _chat.Current.Messages.Count) + " 条";
        }

        private void UpdateButtons()
        {
            bool busy = _chat != null && _chat.Busy;
            _busyUi = busy;
            _btnStop.Enabled = busy;
            _btnSend.Enabled = !busy;
            _btnRegen.Enabled = !busy && _chat != null && _chat.Current != null && _chat.Current.Messages.Any(m => m.Role == "user");
            _btnSend.Text = busy ? "发送中…" : "发送";
            if (_chat != null && _chat.Current != null)
            {
                var p = _providers == null ? null : _providers.Get(_chat.Current.ProviderId);
                _status.Text = (p != null ? p.Name : "未配置") + " · " + (_chat.Current.Model ?? "");
                if (!string.IsNullOrEmpty(_chat.Status)) _status.Text += " · " + _chat.Status;
            }
            _btnStop.Invalidate(); _btnSend.Invalidate();
            RefreshEngineeringContext();
        }

        // ---------------- 事件 ----------------

        private void OnChatChanged()
        {
            SafeUi(delegate
            {
                ReloadSessionList();
                ReloadMessages();
                UpdateButtons();
            });
        }

        private void OnDelta(ChatMessage m)
        {
            System.Threading.Interlocked.Exchange(ref _pendingDelta, m);
        }

        private void FlushStreaming()
        {
            if (IsDisposed || !Visible) return;
            var message = System.Threading.Interlocked.Exchange(ref _pendingDelta, null);
            if (message == null || _chat == null || _chat.Current == null || !_chat.Current.Messages.Any(x => x.Id == message.Id)) return;
            _board.UpdateStreaming(message);
            RefreshCore();
            _execution.Text = (_chat.Status ?? "接收回复") + " · 已收到 " + (message.Content ?? "").Length + " 字符";
        }

        private void OnStatus(string s)
        {
            SafeUi(delegate { if (_chat != null) { _status.Text = s; _execution.Text = "AI&SI · " + s; RefreshCore(); } });
        }

        private void SafeUi(Action a)
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(a); else a();
            }
            catch { }
        }

        // ---------------- 操作 ----------------

        private void Send()
        {
            if (_chat == null) { _status.Text = "AI 服务未初始化"; return; }
            var text = _input.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_referenceContext == null) ApplyProviderSelection();
            ApplyGenerationOptions();
            _input.Text = "";
            _chat.Send(text);
            _board.ScrollToEnd();
        }

        private void ApplyGenerationOptions()
        {
            if (_chat == null) return;
            string[] keys = { "automation", "plc", "dcs", "validation", "troubleshoot", "general" };
            double[] temps = { 0.0, 0.3, 0.7, 1.0, 1.5 };
            int mi = _modeSel == null ? 0 : Math.Max(0, Math.Min(keys.Length - 1, _modeSel.SelectedIndex));
            int ti = _tempSel == null ? 2 : Math.Max(0, Math.Min(temps.Length - 1, _tempSel.SelectedIndex));
            int max; if (!int.TryParse(_maxTokens == null ? "0" : _maxTokens.Text, out max)) max = 0;
            _chat.SetGenerationOptions(keys[mi], temps[ti], max);
        }

        private void ApplyProviderSelection()
        {
            if (_chat == null || _providers == null) return;
            var list = _providers.Providers.Where(p => p.Enabled).ToList();
            int i = _providerSel.SelectedIndex;
            if (i < 0 || i >= list.Count) return;
            var picked = list[i];
            var model = string.IsNullOrWhiteSpace(_modelInput.Text) ? picked.Model : _modelInput.Text.Trim();
            _chat.SetProvider(picked.Id, model);
        }

        private void SwitchSession()
        {
            if (_chat == null) return;
            var row = _sessions.SelectedRow;
            if (row == null) return;
            _chat.SwitchSession((string)row.Tag);
            ReloadMessages();
            UpdateButtons();
        }

        private void ShowSessionMenu(Point p)
        {
            if (_chat == null) return;
            var menu = MenuBuilder.Create(Palette);
            menu.Items.Add(MenuBuilder.Item("重命名", delegate
            {
                var row = _sessions.SelectedRow;
                if (row == null) return;
                var name = Prompt.Input(this, "重命名会话", "标题", row.Cells[0], Palette);
                if (name != null) { _chat.RenameSession((string)row.Tag, name); ReloadSessionList(); }
            }));
            menu.Items.Add(MenuBuilder.Item("删除", delegate
            {
                var row = _sessions.SelectedRow;
                if (row == null) return;
                if (!Prompt.Confirm(this, "删除会话", "删除「" + row.Cells[0] + "」？", Palette)) return;
                _chat.DeleteSession((string)row.Tag);
                ReloadSessionList();
                ReloadMessages();
            }));
            menu.Show(_sessions, p);
        }

        private void ClearSession()
        {
            if (_chat == null) return;
            if (!Prompt.Confirm(this, "清空会话", "清空当前会话的所有消息？（会话本身保留）", Palette)) return;
            _chat.ClearCurrent();
            ReloadMessages();
        }

        private void CopyLast()
        {
            if (_chat == null || _chat.Current == null) return;
            var msg = _chat.Current.Messages.LastOrDefault(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.Content));
            if (msg == null) { _status.Text = "没有可复制的回复"; return; }
            CopyText(msg.Content);
        }

        private void CopyText(string text)
        {
            try
            {
                Clipboard.SetText(text ?? "");
                _status.Text = "已复制到剪贴板";
            }
            catch (Exception ex) { _status.Text = "复制失败：" + ex.Message; }
        }

        public void FocusInput() { try { _input.Inner.Focus(); } catch { } }

        public void SetPrompt(string text)
        {
            _input.Text = text ?? "";
            RefreshEngineeringContext();
            FocusInput();
        }

        protected override void OnActivated()
        {
            if (_chat != null) _chat.RefreshPermissions();
            Reload();
            FocusInput();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_streamUi != null) { _streamUi.Stop(); _streamUi.Dispose(); }
                if (_chat != null) { _chat.Changed -= OnChatChanged; _chat.MessageDelta -= OnDelta; _chat.StatusChanged -= OnStatus; }
            }
            base.Dispose(disposing);
        }

        // =====================================================================
        // 消息绘制区
        // =====================================================================

        internal class MessageBoard : Control
        {
            private readonly List<ChatMessage> _messages = new List<ChatMessage>();
            private readonly List<Bubble> _bubbles = new List<Bubble>();
            private readonly HashSet<string> _expandedTools = new HashSet<string>();
            private readonly Dictionary<string, Measurement> _measurements = new Dictionary<string, Measurement>();
            private class Measurement { public string Content, Error; public int Height; }
            private int _scroll;
            private int _contentH;
            private Palette _p;
            public bool UseCardSurface;
            private System.Windows.Forms.Timer _cursorTimer;
            private bool _cursorOn = true;
            private string _hoverCopyId;

            public event Action<string> CopyRequested;

            public MessageBoard(Palette p)
            {
                _p = p;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = p.Bg;
                _cursorTimer = new System.Windows.Forms.Timer { Interval = 400 };
                _cursorTimer.Tick += delegate { if (Visible && _messages.Any(x => x.Streaming)) { _cursorOn = !_cursorOn; Invalidate(); } };
                _cursorTimer.Start();
                SetStyle(ControlStyles.Selectable, true);
            }

            public void ApplyPalette(Palette p) { _p = p; BackColor = p.Bg; Invalidate(); }

            public void SetMessages(List<ChatMessage> list)
            {
                bool follow = _scroll >= MaxScroll - 3;
                var ids = new HashSet<string>((list ?? new List<ChatMessage>()).Select(x => x.Id));
                foreach (var id in _measurements.Keys.Where(x => !ids.Contains(x)).ToArray()) _measurements.Remove(id);
                _messages.Clear();
                if (list != null) _messages.AddRange(list);
                Rebuild();
                _scroll = UseCardSurface && follow ? MaxScroll : Math.Min(_scroll, MaxScroll);
                Invalidate();
            }

            public void UpdateStreaming(ChatMessage m)
            {
                if (m == null) return;
                bool follow = !UseCardSurface || _scroll >= MaxScroll - 3;
                bool found = false;
                for (int i = 0; i < _messages.Count; i++)
                {
                    if (_messages[i].Id == m.Id) { _messages[i] = m; found = true; break; }
                }
                if (!found) _messages.Add(m);
                Rebuild();
                _scroll = follow ? MaxScroll : Math.Min(_scroll, MaxScroll);
                Invalidate();
            }

            private void Rebuild()
            {
                _bubbles.Clear();
                _contentH = 12;
                using (var g = CreateGraphics())
                {
                    foreach (var m in _messages)
                        _contentH += Measure(g, m) + 10;
                }
                _contentH += 20;
            }

            private int MaxScroll { get { return Math.Max(0, _contentH - Height); } }

            public void ScrollToEnd()
            {
                _scroll = MaxScroll;
                Invalidate();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                _measurements.Clear();
                Rebuild();
                _scroll = Math.Min(_scroll, MaxScroll);
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                _scroll = Math.Max(0, Math.Min(MaxScroll, _scroll - e.Delta));
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                string id = null;
                foreach (var b in _bubbles) if (b.CopyRect.Contains(e.Location) || (b.Message.Role=="tool" && b.Rect.Contains(e.Location))) { id = b.Message.Id; break; }
                if (id != _hoverCopyId) { _hoverCopyId = id; Cursor = id != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hoverCopyId = null;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                foreach (var b in _bubbles)
                {
                    if (b.Message.Role == "tool" && b.Rect.Contains(e.Location))
                    {
                        if (!_expandedTools.Add(b.Message.Id)) _expandedTools.Remove(b.Message.Id);
                        _measurements.Remove(b.Message.Id); Rebuild(); _scroll=Math.Min(_scroll,MaxScroll); Invalidate();return;
                    }
                    if (b.CopyRect.Contains(e.Location) && !string.IsNullOrEmpty(b.Message.Content))
                    {
                        var h = CopyRequested;
                        if (h != null) h(b.Message.Content);
                        return;
                    }
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(UseCardSurface ? _p.CardBg : _p.Bg);

                if (_messages.Count == 0)
                {
                    DrawEmpty(g);
                    return;
                }

                _bubbles.Clear();
                int y = 12 - _scroll;
                foreach (var m in _messages)
                {
                    int h = Measure(g, m);
                    if (y + h >= 0 && y < Height) DrawMessage(g, m, y);
                    y += h + 10;
                }

                if (MaxScroll > 0)
                {
                    int trackH = Height - 8;
                    int barH = Math.Max(30, (int)(trackH * (double)Height / _contentH));
                    int barY = 4 + (int)((trackH - barH) * (double)_scroll / Math.Max(1, MaxScroll));
                    Palette.FillRounded(g, Color.FromArgb(80, _p.TextFaint), new Rectangle(Width - 7, barY, 4, barH), 2);
                }
            }

            private void DrawEmpty(Graphics g)
            {
                int cx = Width / 2, cy = Height / 2 - 60;
                using (var f = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold))
                using (var fs = new Font("Microsoft YaHei UI", 9F))
                {
                    var t1 = "AI&SI";
                    var s1 = g.MeasureString(t1, f);
                    using (var brush = new SolidBrush(_p.Text)) g.DrawString(t1, f, brush, cx - s1.Width / 2, cy);
                    var tips = new[]
                    {
                        "总结一个文档的要点",
                        "帮我列出今天要做的三件事",
                        "对比两个文件的差异",
                        "把这个文件夹里的表格归类整理"
                    };
                    int y = cy + 40;
                    foreach (var tip in tips)
                    {
                        var s = g.MeasureString("· " + tip, fs);
                        using (var brush = new SolidBrush(_p.TextFaint)) g.DrawString("· " + tip, fs, brush, cx - s.Width / 2, y);
                        y += 22;
                    }
                }
            }

            private int Measure(Graphics g, ChatMessage m)
            {
                Measurement cached;
                if (_measurements.TryGetValue(m.Id, out cached) && ReferenceEquals(cached.Content, m.Content) && cached.Error == m.Error) return cached.Height;
                int height = MeasureCore(g, m);
                _measurements[m.Id] = new Measurement { Content = m.Content, Error = m.Error, Height = height };
                return height;
            }

            private int BubbleWidth(ChatMessage message)
            {
                if (!UseCardSurface) return Math.Max(240, Width - 120);
                int preferred = Width * (message.Role == "user" ? 76 : 88) / 100;
                return Math.Max(34, Math.Min(Width - 40, preferred));
            }

            private int MeasureCore(Graphics g, ChatMessage m)
            {
                if (m.Role == "tool")
                {
                    using (var font = new Font("Microsoft YaHei UI", 9F))
                        return 58 + (_expandedTools.Contains(m.Id) ? Math.Min(140,MarkdownLite.Wrap(g, m.Content ?? "", font, BubbleWidth(m) - 28).Count) * 17 : 0);
                }
                int maxW = BubbleWidth(m);
                using (var f = new Font("Microsoft YaHei UI", 9.5F))
                {
                    var blocks = MarkdownLite.Parse(m.Role=="assistant" ? Regex.Replace(m.Content??"", @"```tool[\s\S]*?(?:```|$)", "操作请求 · 结果见工具卡片",RegexOptions.IgnoreCase) : m.Content??"");
                    int h = 0;
                    foreach (var b in blocks) h += MarkdownLite.MeasureBlock(g, b, f, maxW - 24);
                    if (string.IsNullOrEmpty(m.Content) && string.IsNullOrEmpty(m.Error)) h = 22;
                    if (!string.IsNullOrEmpty(m.Error))
                        using (var errorFont = new Font("Microsoft YaHei UI", 8.5F))
                            h += 4 + MarkdownLite.Wrap(g, m.Error, errorFont, maxW - 24).Count * 17;
                    return Math.Max(34, h + 26);
                }
            }

            private int DrawMessage(Graphics g, ChatMessage m, int y)
            {
                if (m.Role == "tool") return DrawToolCard(g, m, y);
                bool isUser = m.Role == "user";
                int maxW = BubbleWidth(m);
                int x = isUser ? Width - maxW - 20 : 20;
                int w = maxW;

                using (var f = new Font("Microsoft YaHei UI", 9.5F))
                using (var fs = new Font("Microsoft YaHei UI", 8F))
                {
                    var blocks = MarkdownLite.Parse(m.Role=="assistant" ? Regex.Replace(m.Content??"", @"```tool[\s\S]*?(?:```|$)", "操作请求 · 结果见工具卡片",RegexOptions.IgnoreCase) : m.Content??"");
                    int h = Measure(g, m);

                    var bg = isUser ? Color.FromArgb(36, 52, 78) : Color.FromArgb(22, 29, 45);
                    var rect = new Rectangle(x, y, w, h);
                    Palette.FillRounded(g, bg, rect, 12);
                    Palette.DrawRounded(g, isUser ? Color.FromArgb(52, 74, 110) : _p.Line, rect, 12, 1f);
                    if (!isUser)
                    {
                        using (var b = new SolidBrush(_p.Accent))
                            g.FillRectangle(b, rect.X, rect.Y + 10, 3, rect.Height - 20);
                    }

                    int ty = y + 12;
                    foreach (var b in blocks)
                        ty += MarkdownLite.DrawBlock(g, b, f, rect.X + 14, ty, w - 24, _p);

                    if (!string.IsNullOrEmpty(m.Error))
                    {
                        using (var fe = new Font("Microsoft YaHei UI", 8.5F))
                            using (var brush = new SolidBrush(Color.FromArgb(248, 113, 113)))
                            {
                                ty += 2;
                                foreach (var line in MarkdownLite.Wrap(g, m.Error, fe, w - 24))
                                {
                                    g.DrawString(line, fe, brush, rect.X + 14, ty);
                                    ty += 17;
                                }
                            }
                    }

                    if (m.Streaming && _cursorOn)
                    {
                        using (var fc = new Font("Consolas", 11F))
                            using (var brush = new SolidBrush(_p.Accent)) g.DrawString("▍", fc, brush, rect.X + 14, ty - 2);
                    }

                    // 复制按钮（悬停显示）
                    var copyRect = new Rectangle(rect.Right - 46, rect.Bottom - 20, 38, 16);
                    if (_hoverCopyId == m.Id)
                    {
                        Palette.FillRounded(g, Color.FromArgb(60, _p.PanelAlt), copyRect, 6);
                        using (var fc = new Font("Microsoft YaHei UI", 7.5F))
                            using (var brush = new SolidBrush(_p.TextDim)) g.DrawString("复制", fc, brush, copyRect.X + 7, copyRect.Y + 1);
                    }

                    using (var ft = new Font("Microsoft YaHei UI", 7.5F))
                    using (var brush = new SolidBrush(_p.TextFaint))
                        g.DrawString((isUser ? "你" : string.IsNullOrEmpty(m.SpeakerName) ? "AI&SI" : m.SpeakerName)+(m.Interrupted ? " · 已打断" : ""), ft, brush,
                            rect.X + 14, rect.Bottom - 18);

                    _bubbles.Add(new Bubble { Message = m, Rect = rect, CopyRect = copyRect });
                    return h;
                }
            }

            /// <summary>工具执行结果卡片（AI 真实执行的可视化证据）。</summary>
            private int DrawToolCard(Graphics g, ChatMessage m, int y)
            {
                int w = BubbleWidth(m);
                bool ok = (m.Content ?? "").StartsWith("✅");
                var accent = ok ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113);
                using (var f = new Font("Microsoft YaHei UI", 9F))
                using (var ft = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    var lines = MarkdownLite.Wrap(g, (m.Content ?? "").Replace("\r\n", "\n"), f, w - 28);
                    int h = Measure(g,m);
                    var rect = new Rectangle(20, y, w, h);
                    Palette.FillRounded(g, Color.FromArgb(18, 26, 40), rect, 10);
                    Palette.DrawRounded(g, accent, rect, 10, 1f);
                    using (var bar = new SolidBrush(accent))
                        g.FillRectangle(bar, rect.X, rect.Y + 8, 3, rect.Height - 16);
                    using (var brush = new SolidBrush(accent)) g.DrawString((_expandedTools.Contains(m.Id)?"▾ ":"▸ ") + (m.ToolName ?? "工具") + (ok ? " · 已完成" : " · 未完成"), ft, brush, rect.X + 14, rect.Y + 7);
                    string summary=ok?"结果已收起 · 点击查看详情":"执行未完成 · 点击查看原因";
                    using(var brush=new SolidBrush(_p.TextDim))g.DrawString(summary,f,brush,rect.X+14,rect.Y+29);
                    _bubbles.Add(new Bubble{Message=m,Rect=rect});
                    if(!_expandedTools.Contains(m.Id))return h;
                    int ty = rect.Y + 54;
                    foreach (var line in lines.Take(140))
                    {
                        using (var brush = new SolidBrush(_p.TextDim)) g.DrawString(line, f, brush, rect.X + 14, ty);
                        ty += 17;
                    }
                    return h;
                }
            }

            protected override void Dispose(bool disposing)
            {
                try { if (_cursorTimer != null) { _cursorTimer.Stop(); _cursorTimer.Dispose(); } } catch { }
                base.Dispose(disposing);
            }

            internal class Bubble
            {
                public ChatMessage Message;
                public Rectangle Rect;
                public Rectangle CopyRect;
            }
        }

        // =====================================================================
        // 轻量 Markdown 渲染（无第三方库）
        // =====================================================================

        internal static class MarkdownLite
        {
            public class Block
            {
                public string Kind = "p";    // p | h1 | h2 | code | bullet | ordered | quote
                public string Text = "";
                public int Level = 0;
            }

            private static readonly Regex InlineCode = new Regex("`([^`]+)`", RegexOptions.Compiled);
            private static readonly Regex Bold = new Regex(@"\*\*([^*]+)\*\*", RegexOptions.Compiled);
            private static readonly Regex Italic = new Regex(@"(?<!\*)\*([^*]+)\*(?!\*)", RegexOptions.Compiled);
            private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

            public static List<Block> Parse(string text)
            {
                var blocks = new List<Block>();
                if (string.IsNullOrEmpty(text)) return blocks;
                var lines = text.Replace("\r\n", "\n").Split('\n');
                bool inCode = false;
                var codeBuf = new StringBuilder();

                foreach (var raw in lines)
                {
                    var line = raw;
                    if (line.TrimStart().StartsWith("```"))
                    {
                        if (inCode)
                        {
                            blocks.Add(new Block { Kind = "code", Text = codeBuf.ToString() });
                            codeBuf.Length = 0;
                            inCode = false;
                        }
                        else inCode = true;
                        continue;
                    }
                    if (inCode) { codeBuf.AppendLine(line); continue; }

                    if (line.Trim().Length == 0)
                    {
                        if (blocks.Count > 0 && blocks[blocks.Count - 1].Kind != "gap") blocks.Add(new Block { Kind = "gap" });
                        continue;
                    }
                    var t = line.TrimStart();
                    if (t.StartsWith("### ")) blocks.Add(new Block { Kind = "h2", Text = t.Substring(4) });
                    else if (t.StartsWith("## ")) blocks.Add(new Block { Kind = "h2", Text = t.Substring(3) });
                    else if (t.StartsWith("# ")) blocks.Add(new Block { Kind = "h1", Text = t.Substring(2) });
                    else if (t.StartsWith("> ")) blocks.Add(new Block { Kind = "quote", Text = t.Substring(2) });
                    else if (t.StartsWith("- ") || t.StartsWith("* ")) blocks.Add(new Block { Kind = "bullet", Text = t.Substring(2) });
                    else if (Regex.IsMatch(t, @"^\d+\.\s")) blocks.Add(new Block { Kind = "ordered", Text = Regex.Replace(t, @"^\d+\.\s", "") });
                    else
                    {
                        if (blocks.Count > 0 && blocks[blocks.Count - 1].Kind == "p")
                            blocks[blocks.Count - 1].Text += " " + line.Trim();
                        else blocks.Add(new Block { Kind = "p", Text = line.Trim() });
                    }
                }
                if (inCode && codeBuf.Length > 0) blocks.Add(new Block { Kind = "code", Text = codeBuf.ToString() });
                return blocks;
            }

            public static int MeasureBlock(Graphics g, Block b, Font baseFont, int maxW)
            {
                if (b.Kind == "gap") return 8;
                if (b.Kind == "code")
                {
                    using (var f = new Font("Consolas", 9F))
                    {
                        int n = Wrap(g, b.Text.Replace("\r\n", "\n").TrimEnd('\n'), f, maxW - 20).Count;
                        return n * 17 + 22;
                    }
                }
                using (var f = FontFor(b, baseFont))
                {
                    var text = b.Kind == "bullet" ? "• " + b.Text : b.Kind == "quote" ? "│ " + b.Text : b.Text;
                    var lines = Wrap(g, StripInline(text), f, maxW - (Indent(b.Kind)));
                    return lines.Count * (int)(f.Size + 7) + 4;
                }
            }

            public static int DrawBlock(Graphics g, Block b, Font baseFont, int x, int y, int maxW, Palette p)
            {
                if (b.Kind == "gap") return 8;

                if (b.Kind == "code")
                {
                    using (var f = new Font("Consolas", 9F))
                    {
                        var lines = Wrap(g, b.Text.Replace("\r\n", "\n").TrimEnd('\n'), f, maxW - 20);
                        int h = lines.Count * 17 + 16;
                        var rect = new Rectangle(x, y, maxW, h);
                        Palette.FillRounded(g, Color.FromArgb(14, 20, 32), rect, 8);
                        Palette.DrawRounded(g, p.Line, rect, 8, 1f);
                        int ty = y + 8;
                        foreach (var line in lines)
                        {
                            using (var brush = new SolidBrush(Color.FromArgb(190, 220, 240))) g.DrawString(line, f, brush, x + 10, ty);
                            ty += 17;
                        }
                        return h + 6;
                    }
                }

                int indent = Indent(b.Kind);
                int lineH = 0;
                using (var f = FontFor(b, baseFont))
                {
                    lineH = (int)(f.Size + 7);
                    var text = b.Text;
                    if (b.Kind == "bullet") text = "• " + text;
                    else if (b.Kind == "quote") text = "│ " + text;

                    var lines = Wrap(g, StripInline(text), f, maxW - indent);
                    int ty = y;
                    foreach (var line in lines)
                    {
                        var color = b.Kind == "quote" ? p.TextDim : (b.Kind == "h1" || b.Kind == "h2" ? p.Text : p.Text);
                        using (var brush = new SolidBrush(color)) g.DrawString(line, f, brush, x + indent, ty);
                        ty += lineH;
                    }
                    return lines.Count * lineH + 4;
                }
            }

            private static int Indent(string kind)
            {
                if (kind == "bullet" || kind == "quote") return 10;
                if (kind == "ordered") return 14;
                return 0;
            }

            private static Font FontFor(Block b, Font baseFont)
            {
                if (b.Kind == "h1") return new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold);
                if (b.Kind == "h2") return new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
                if (b.Kind == "quote") return new Font("Microsoft YaHei UI", 9F, FontStyle.Italic);
                return new Font("Microsoft YaHei UI", 9.5F);
            }

            private static string StripInline(string text)
            {
                // 绘制阶段先去掉行内标记字符（保持可读），链接显示为文本
                var t = Link.Replace(text, "$1");
                t = Bold.Replace(t, "$1");
                t = Italic.Replace(t, "$1");
                t = InlineCode.Replace(t, "$1");
                return t;
            }

            internal static List<string> Wrap(Graphics g, string text, Font font, int maxW)
            {
                var result = new List<string>();
                if (string.IsNullOrEmpty(text)) { result.Add(""); return result; }
                foreach (var para in text.Split('\n'))
                {
                    var cur = "";
                    foreach (var ch in para)
                    {
                        if (g.MeasureString(cur + ch, font).Width > maxW && cur.Length > 0)
                        {
                            result.Add(cur);
                            cur = ch.ToString();
                        }
                        else cur += ch;
                    }
                    result.Add(cur);
                }
                return result;
            }
        }
    }
}
