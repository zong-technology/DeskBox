using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.AppServices;
using DeskBox.Domain;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>
    /// 统一设置中心（规格十一）：外观 / AI 服务商 / 天气 / 自动化 / 文件与隐私 / 通知 / 系统。
    /// 所有可保存项都真实写入 state.json 并立即影响程序行为；
    /// 未实现项显式标注「未实现」，不伪装生效。
    /// </summary>
    public class SettingsCenterView : ViewBase
    {
        private readonly Store _store;
        private ProviderStore _providers;
        private readonly List<NavItem> _nav = new List<NavItem>();
        private Panel _navPanel, _contentHost;
        private int _current = 0;
        private Label _toast;

        public event EventHandler ProvidersChanged;
        public event EventHandler AIEnvironmentChanged;
        public Func<bool> CanSwitchAIEnvironment;
        public event EventHandler FilePermissionsChanged;
        public event EventHandler TemporaryAccessRevoked;
        private void NotifyPermissionsChanged() { if (FilePermissionsChanged != null) FilePermissionsChanged(this, EventArgs.Empty); }
        /// <summary>主题/强调色变化时通知主界面刷新（真实生效）。</summary>
        public event EventHandler AppearanceChanged;

        private class NavItem
        {
            public string Key = "";
            public string Icon = "";
            public string Title = "";
            public DarkButton Btn;
        }

        public SettingsCenterView(Store store, ProviderStore providers)
            : base(store, "设置", "所有设置立即生效并保存到本地；标注「未实现」的项目不会假装生效")
        {
            _store = store;
            _providers = providers;
            BuildShell();
            Switch(0);
        }

        // ---------------- 外壳 ----------------

        public void ShowAiProviders()
        {
            Switch(_nav.FindIndex(x => x.Key == "ai"));
        }

        private void BuildShell()
        {
            _navPanel = new Panel { Dock = DockStyle.Left, Width = 186, BackColor = Palette.Bg, Padding = new Padding(0, 6, 8, 0) };

            var items = new[]
            {
                new NavItem { Key = "appearance", Icon = "🎨", Title = "外观" },
                new NavItem { Key = "ai",         Icon = "◈",  Title = "AI 服务商" },
                new NavItem { Key = "voice",      Icon = "◉",  Title = "全息语音" },
                new NavItem { Key = "weather",    Icon = "☁",  Title = "天气" },
                new NavItem { Key = "automation", Icon = "⚡", Title = "自动化" },
                new NavItem { Key = "privacy",    Icon = "🔒", Title = "文件与隐私" },
                new NavItem { Key = "notify",     Icon = "🔔", Title = "通知" },
                new NavItem { Key = "system",     Icon = "⚙",  Title = "系统" },
            };

            int y = 0;
            foreach (var it in items)
            {
                var item = it;
                var b = new DarkButton(item.Icon + "  " + item.Title)
                {
                    Left = 0, Top = y, Width = 178, Height = 34, Radius = 8
                };
                b.Palette = Palette;
                b.Click += delegate { Switch(_nav.FindIndex(x => x.Key == item.Key)); };
                _navPanel.Controls.Add(b);
                item.Btn = b;
                _nav.Add(item);
                y += 38;
            }

            _contentHost = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg, AutoScroll = true, Padding = new Padding(6, 6, 6, 6) };

            _toast = new Label
            {
                AutoSize = false, Height = 26, Width = 260, Visible = false,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(30, 58, 44),
                ForeColor = Color.FromArgb(110, 231, 183),
                Font = new Font("Microsoft YaHei UI", 9F)
            };

            Body.Controls.Add(_contentHost);
            Body.Controls.Add(_navPanel);
            Body.Controls.Add(_toast);
        }

        private void Toast(string text, bool ok = true)
        {
            try
            {
                _toast.Text = (ok ? "✓ " : "✗ ") + text;
                _toast.BackColor = ok ? Color.FromArgb(30, 58, 44) : Color.FromArgb(60, 30, 34);
                _toast.ForeColor = ok ? Color.FromArgb(110, 231, 183) : Color.FromArgb(248, 150, 150);
                _toast.Left = Math.Max(0, Body.Width - _toast.Width - 16);
                _toast.Top = 4;
                _toast.Visible = true;
                _toast.BringToFront();
                var t = new System.Windows.Forms.Timer { Interval = 2200 };
                t.Tick += delegate { t.Stop(); t.Dispose(); _toast.Visible = false; };
                t.Start();
            }
            catch { }
        }

        private void Switch(int index)
        {
            if (index < 0 || index >= _nav.Count) return;
            _current = index;
            for (int i = 0; i < _nav.Count; i++)
            {
                _nav[i].Btn.Primary = (i == index);
                _nav[i].Btn.Invalidate();
            }
            _contentHost.SuspendLayout();
            foreach (Control old in _contentHost.Controls.Cast<Control>().ToArray()) old.Dispose();
            _contentHost.Controls.Clear();
            var page = BuildPage(_nav[index].Key);
            _contentHost.Controls.Add(page);
            _contentHost.ResumeLayout();
            page.PerformLayout();
        }

        private Control BuildPage(string key)
        {
            switch (key)
            {
                case "appearance": return PageAppearance();
                case "ai": return PageAi();
                case "voice": return PageVoice();
                case "weather": return PageWeather();
                case "automation": return PageAutomation();
                case "privacy": return PagePrivacy();
                case "notify": return PageNotify();
                default: return PageSystem();
            }
        }

        // ---------------- 通用控件工厂 ----------------

        private int _y;
        private Panel NewPage()
        {
            _y = 8;
            return new Panel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Palette.Bg, Width = Math.Max(600, Body.Width - 210) };
        }

        private void Section(Panel host, string title, string hint)
        {
            var t = new Label
            {
                Text = title, Left = 8, Top = _y, AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Palette.Text, BackColor = Palette.Bg
            };
            host.Controls.Add(t);
            _y += 26;
            if (!string.IsNullOrEmpty(hint))
            {
                var h = new Label
                {
                    Text = hint, Left = 8, Top = _y, AutoSize = true, MaximumSize = new Size(760, 0),
                    Font = new Font("Microsoft YaHei UI", 8.5F),
                    ForeColor = Palette.TextFaint, BackColor = Palette.Bg
                };
                host.Controls.Add(h);
                _y += 22;
            }
            _y += 4;
        }

        private void RowLabel(Panel host, string text, string note = "")
        {
            var l = new Label
            {
                Text = text, Left = 8, Top = _y, AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Palette.TextDim, BackColor = Palette.Bg
            };
            host.Controls.Add(l);
            if (!string.IsNullOrEmpty(note))
            {
                var n = new Label
                {
                    Text = note, Left = l.Left + l.PreferredWidth + 10, Top = _y + 2, AutoSize = true,
                    Font = new Font("Microsoft YaHei UI", 8F),
                    ForeColor = Color.FromArgb(180, 140, 90), BackColor = Palette.Bg
                };
                host.Controls.Add(n);
            }
            _y += 24;
        }

        private void Gap() { _y += 10; }

        private DarkSelect AddSelect(Panel host, string[] items, int selected, Action<int> onChange)
        {
            var s = new DarkSelect(items) { Left = 8, Top = _y, Width = 260 };
            s.Palette = Palette;
            if (selected >= 0 && selected < items.Length) s.SelectedIndex = selected;
            s.SelectedIndexChanged += delegate { onChange(s.SelectedIndex); };
            host.Controls.Add(s);
            _y += 38;
            return s;
        }

        private CheckBox AddCheck(Panel host, string text, bool value, Action<bool> onChange)
        {
            var c = new CheckBox
            {
                Text = text, Left = 8, Top = _y, AutoSize = true, Checked = value,
                ForeColor = Palette.Text, BackColor = Palette.Bg,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            c.CheckedChanged += delegate
            {
                onChange(c.Checked);
                _store.SaveSettingsOnly();
                Toast("已保存");
            };
            host.Controls.Add(c);
            _y += 30;
            return c;
        }

        private DarkInput AddInput(Panel host, string placeholder, string value, Action<string> onChange)
        {
            var i = new DarkInput { Left = 8, Top = _y, Width = 340, Placeholder = placeholder };
            i.Palette = Palette;
            i.Text = value ?? "";
            i.KeyPressed += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; onChange(i.Text); _store.SaveSettingsOnly(); Toast("已保存"); }
            };
            host.Controls.Add(i);
            _y += 38;
            return i;
        }

        private DarkButton AddButton(Panel host, string text, int x, int top, int width, Action onClick, bool primary = false)
        {
            var b = new DarkButton(text) { Left = x, Top = top, Width = width, Primary = primary };
            b.Palette = Palette;
            b.Click += delegate { onClick(); };
            host.Controls.Add(b);
            return b;
        }

        // ---------------- 1. 外观 ----------------

        private Control PageVoice()
        {
            var p=NewPage();
            var storage=new RealtimeVoiceSettingsStore();
            var saved=storage.Load();
            Section(p,"全息语音","在这里保存一次。进入全息 AI 通话后自动连接，画面只显示投影和实时字幕。");
            RowLabel(p,"助手名称","角色与名称会应用于下次通话，并随会话记录保存");
            var name=AddInput(p,"给全息助手起一个名字",saved.AssistantName,delegate(string value){});name.Name="hologram-name";name.Inner.MaxLength=24;
            RowLabel(p,"角色设定","可以描述身份、性格、专长与说话方式；最多 2000 字");
            var persona=new TextBox {Left=8,Top=_y,Width=620,Height=100,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,
                Text=saved.Persona,MaxLength=2000,BackColor=Palette.PanelAlt,ForeColor=Palette.Text,Name="hologram-persona",AccessibleName="全息助手角色设定"};
            p.Controls.Add(persona);_y+=112;
            RowLabel(p,"实时语音服务");
            var provider=new ComboBox {Left=8,Top=_y,Width=340,DropDownStyle=ComboBoxStyle.DropDownList,
                BackColor=Palette.PanelAlt,ForeColor=Palette.Text,Name="hologram-service"};
            provider.Items.AddRange(new object[]{"Google · Gemini Live","千问百炼 · Omni Realtime"});
            provider.SelectedIndex=saved.Provider=="qwen" ? 1 : 0;p.Controls.Add(provider);_y+=38;
            RowLabel(p,"实时服务地址","百炼需要填写所属工作空间的 WSS 地址");
            var endpoint=AddInput(p,"wss://…",saved.Endpoint,delegate(string value){});endpoint.Name="hologram-endpoint";
            RowLabel(p,"实时语音模型","普通文本聊天模型不能替代实时语音模型");
            var model=new ComboBox {Left=8,Top=_y,Width=340,Text=saved.Model,BackColor=Palette.PanelAlt,ForeColor=Palette.Text,Name="hologram-model"};
            p.Controls.Add(model);_y+=38;
            RowLabel(p,"API 密钥",string.IsNullOrEmpty(saved.ApiKey) ? "使用 Windows DPAPI 加密保存" : "已保存；留空保留当前密钥");
            var key=AddInput(p,"输入密钥，留空保留已保存的密钥","",delegate(string value){});key.Name="hologram-key";key.Inner.UseSystemPasswordChar=true;
            RowLabel(p,"投影外观");
            int appearance=saved.Appearance=="silverwing" ? 1 : saved.Appearance=="photon" ? 2 : 0;
            var style=AddSelect(p,new[]{"量子光核 · 沉浸光场","银紫龙翼 · 原画全息","星辉光粒子 · 全身投影"},appearance,delegate(int value){});style.Name="hologram-appearance";
            var reduced=new CheckBox {Left=8,Top=_y,AutoSize=true,Text="减少动态效果",Checked=saved.ReducedMotion,ForeColor=Palette.Text,BackColor=Palette.Bg,Name="hologram-motion"};
            p.Controls.Add(reduced);_y+=36;
            var interruption=new CheckBox {Left=8,Top=_y,AutoSize=true,Text="允许免按键打断（建议戴耳机时启用）",Checked=saved.AllowInterruption,ForeColor=Palette.Text,BackColor=Palette.Bg,Name="hologram-interruption"};
            p.Controls.Add(interruption);_y+=36;
            var status=new Label {Left=8,Top=_y+46,AutoSize=true,MaximumSize=new Size(650,0),ForeColor=Palette.TextDim,Text="Esc 返回；Ctrl+M 静音；轻点投影或空格打断。默认稳定播报，减少扬声器回声干扰。",Name="hologram-setting-status"};
            p.Controls.Add(status);
            var save=AddButton(p,"保存全息语音",8,_y,145,delegate {
                try {
                    string service=provider.SelectedIndex==1 ? "qwen" : "gemini";
                    var value=new RealtimeVoiceSettings {Provider=service,Endpoint=endpoint.Text.Trim(),Model=model.Text.Trim(),
                        AssistantName=name.Text.Trim(),Persona=persona.Text.Trim(),
                        ApiKey=string.IsNullOrWhiteSpace(key.Text) && service==saved.Provider ? saved.ApiKey : key.Text.Trim(),
                        Appearance=style.SelectedIndex==1 ? "silverwing" : style.SelectedIndex==2 ? "photon" : "quantum",ReducedMotion=reduced.Checked,AllowInterruption=interruption.Checked};
                    storage.Save(value);saved=value;key.Text="";
                    status.Text=value.Ready ? "已加密保存。下次点击全息 AI 通话会直接进入并连接。" : "投影偏好已保存，语音尚未启用。";
                    Toast("全息语音已保存");
                } catch(ArgumentException error){status.Text=error.Message;Toast("请核对实时语音参数",false);}
                  catch(Exception){status.Text="保存失败，请检查本机存储权限。";Toast("保存失败",false);}
            },true);save.Name="hologram-save";
            var fetch=AddButton(p,"获取实时模型",164,_y,145,delegate{});
            CancellationTokenSource request=null;
            fetch.Click += async delegate {
                if(request!=null)return;
                request=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                fetch.Enabled=save.Enabled=provider.Enabled=false;
                try {
                    string service=provider.SelectedIndex==1 ? "qwen" : "gemini";
                    string secret=string.IsNullOrWhiteSpace(key.Text) && service==saved.Provider ? saved.ApiKey : key.Text.Trim();
                    var values=await (service=="qwen" ? QwenLiveVoiceProvider.ListModelsAsync(endpoint.Text,secret,request.Token) :
                        VoiceModelCatalog.ListGeminiAsync(endpoint.Text,secret,request.Token));
                    if(p.IsDisposed)return;
                    string current=model.Text;model.Items.Clear();model.Items.AddRange(values);model.Text=current;
                    status.Text="已读取 "+values.Length+" 个实时模型，可选择后保存。";
                }catch(Exception){if(!p.IsDisposed)status.Text="暂时无法读取模型目录，可手动填写模型 ID。";}
                finally {request.Dispose();request=null;if(!p.IsDisposed)fetch.Enabled=save.Enabled=provider.Enabled=true;}
            };
            p.Disposed+=delegate{if(request!=null)request.Cancel();};
            provider.SelectedIndexChanged+=delegate {
                bool qwen=provider.SelectedIndex==1;
                endpoint.Text=qwen ? QwenLiveVoiceProvider.DefaultEndpoint : GeminiLiveVoiceProvider.DefaultEndpoint;
                model.Text=qwen ? QwenLiveVoiceProvider.DefaultModel : "gemini-3.1-flash-live-preview";key.Text="";
            };
            _y+=96;
            return p;
        }

        private Control PageAppearance()
        {
            var p = NewPage();
            Section(p, "主题与强调色", "修改后立即生效");

            RowLabel(p, "主题");
            var themes = new[] { "工程矩阵（cyber）", "深空（midnight）", "极光（aurora）", "明亮（daylight）" };
            int ti = _store.Settings.Theme == "midnight" ? 1 : _store.Settings.Theme == "aurora" ? 2 : _store.Settings.Theme == "daylight" ? 3 : 0;
            AddSelect(p, themes, ti, delegate(int i)
            {
                _store.Settings.Theme = i == 1 ? "midnight" : i == 2 ? "aurora" : i == 3 ? "daylight" : "cyber";
                _store.SaveSettingsOnly();
                RaiseAppearance();
                Toast("主题已应用");
            });

            RowLabel(p, "强调色");
            var colors = new[] { "#6EE7FF", "#60A5FA", "#A78BFA", "#34D399", "#FBBF24", "#F87171" };
            int cx = 8;
            foreach (var hex in colors)
            {
                var b = new Panel
                {
                    Left = cx, Top = _y, Width = 30, Height = 30,
                    BackColor = Palette.ParseColor(hex, Color.FromArgb(110, 231, 255)),
                    Cursor = Cursors.Hand
                };
                var cap = hex;
                b.Paint += delegate(object s, PaintEventArgs e)
                {
                    var r = new Rectangle(0, 0, 29, 29);
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    Palette.DrawRounded(e.Graphics,
                        string.Equals(_store.Settings.Accent, cap, StringComparison.OrdinalIgnoreCase) ? Color.White : Color.FromArgb(70, 90, 110),
                        r, 8, 2f);
                };
                b.Click += delegate
                {
                    _store.Settings.Accent = cap;
                    _store.SaveSettingsOnly();
                    RaiseAppearance();
                    p.Invalidate(true);
                    Toast("强调色已应用");
                };
                p.Controls.Add(b);
                cx += 38;
            }
            _y += 42;

            RowLabel(p, "图标尺寸");
            var sizes = new[] { "48", "56", "64", "72", "80", "96" };
            int si = Array.IndexOf(sizes, _store.Settings.IconSize.ToString());
            AddSelect(p, sizes, si < 0 ? 2 : si, delegate(int i)
            {
                _store.Settings.IconSize = int.Parse(sizes[i]);
                _store.SaveSettingsOnly();
                Toast("图标尺寸已保存");
            });

            Gap();
            Section(p, "行为", null);
            AddCheck(p, "启用界面动画", _store.Settings.Animations, delegate(bool v) { _store.Settings.Animations = v; });
            AddCheck(p, "使用12页参考图工作台（可切回经典布局）", _store.Settings.ReferenceUi, delegate(bool v) { _store.Settings.ReferenceUi=v;RaiseAppearance(); });
            AddCheck(p, "显示小部件栏", _store.Settings.ShowWidgets, delegate(bool v) { _store.Settings.ShowWidgets = v; });
            AddCheck(p, "显示侧边栏", _store.Settings.ShowSidebar, delegate(bool v) { _store.Settings.ShowSidebar = v; });
            AddCheck(p, "隐藏系统桌面图标（由 DeskBox 接管桌面）", _store.Settings.TakeoverIcons, delegate(bool v)
            {
                _store.Settings.TakeoverIcons = v;
                try { DeskBox.Native.Win32.SetSystemIconsVisible(!v); } catch { }
            });

            RowLabel(p, "界面缩放", "未实现：当前版本不支持，下一版生效");
            AddSelect(p, new[] { "100%", "110%", "125%" }, 0, delegate(int i) { _store.Settings.Zoom = i == 1 ? 110 : i == 2 ? 125 : 100; });

            AddButton(p, "保存设置", 8, _y, 100, delegate { _store.SaveSettingsOnly(); Toast("设置已保存"); }, true);
            return p;
        }

        private void RaiseAppearance()
        {
            var h = AppearanceChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------------- 2. AI 服务商 ----------------

        private DarkList _provList;
        private DarkInput _fName, _fBase, _fKey, _fModel, _fTimeout, _fRetry, _fHeaders;
        private DarkSelect _fProtocol;
        private CheckBox _fEnabled, _fDefault;
        private Label _testResult;
        private AiProvider _editing;
        private bool _testing;
        private bool _keyUnchanged;

        private Control PageAi()
        {
            var p = NewPage();
            Section(p,"全部 AI 的运行环境","本地与网络分别保存模型配置、密钥和对话；普通 AI、全息、工作流 AI 及手机中继共用执行边界。");
            var environment=new ComboBox {Left=8,Top=_y,Width=280,DropDownStyle=ComboBoxStyle.DropDownList,Name="ai-environment",AccessibleName="AI 运行环境"};
            environment.Items.AddRange(new object[]{"本地隔离 · 仅本机模型","网络开放 · 允许联网模型"});environment.SelectedIndex=AiEnvironment.Local?0:1;p.Controls.Add(environment);_y+=38;
            RowLabel(p,"本地仅允许 127.0.0.1 / ::1，不跟随重定向，不开放网络共享、工作流执行或设备同步。需事先安装本机模型服务；不会自动改用云端。");
            environment.SelectedIndexChanged+=delegate {
                var mode=environment.SelectedIndex==0?"local":"network";if(mode==AiEnvironment.Mode)return;
                if(CanSwitchAIEnvironment!=null&&!CanSwitchAIEnvironment()){Toast("请先结束全息通话或停止文字生成，再切换环境",false);environment.SelectedIndex=AiEnvironment.Local?0:1;return;}
                try {AiEnvironment.Select(mode);_providers.ChangeEnvironment();_editing=null;var changed=AIEnvironmentChanged;if(changed!=null)changed(this,EventArgs.Empty);ReloadProviderList();if(_providers.Providers.Count==0)NewProvider();RaiseProviders();Toast("AI 环境已切换，原配置和记录保留");}
                catch(Exception error){Toast(error.Message,false);environment.SelectedIndex=AiEnvironment.Local?0:1;}
            };
            AddCheck(p, "博图工程需求：Enter 发送（关闭后用 Ctrl+Enter；Shift+Enter 换行）",
                _store.Settings.TiaEnterSends, value => _store.Settings.TiaEnterSends = value).Name = "tia-enter-sends";
            Section(p, "AI 服务商管理", "API Key 使用 Windows DPAPI 加密存储，界面与日志永不显示明文");

            var bar = new Panel { Left = 0, Top = _y, Height = 36, Width = Math.Max(700, Body.Width - 230), BackColor = Palette.Bg };
            int bx = 8;
            AddButton(bar, "＋ 新增", bx, 2, 84, delegate { NewProvider(); }, true); bx += 90;
            AddButton(bar, "保存", bx, 2, 70, delegate { SaveEditing(); }); bx += 76;
            AddButton(bar, "删除", bx, 2, 70, delegate { DeleteProvider(); }); bx += 76;
            AddButton(bar, "设为默认", bx, 2, 84, delegate { SetDefaultProvider(); }); bx += 90;
            AddButton(bar, "测试连接", bx, 2, 84, delegate { TestProvider(false); }); bx += 90;
            AddButton(bar, "检测文本流", bx, 2, 100, delegate { TestProvider(true); }).Name = "ai-stream-probe";
            p.Controls.Add(bar);
            _y += 42;

            _provList = new DarkList { Left = 8, Top = _y, Width = Math.Max(660, Body.Width - 240), Height = 132 };
            _provList.Palette = Palette;
            _provList.SetColumns(new[] { "名称", "协议", "模型", "默认", "状态" }, new[] { 150, 110, 150, 60, 190 });
            _provList.SelectionChanged += delegate { LoadSelected(); };
            p.Controls.Add(_provList);
            _y += 140;

            Section(p, "服务商配置", null);
            RowLabel(p, "名称");
            _fName = AddInput(p, "例如：DeepSeek 官方", "", delegate(string v) { });
            RowLabel(p, "协议");
            _fProtocol = AddSelect(p, new[] { "openai（Chat Completions）", "openai-responses", "custom" }, 0, delegate(int i) { });
            RowLabel(p, "Base URL");
            _fBase = AddInput(p, "例如：https://api.deepseek.com 或 https://api.openai.com/v1", "", delegate(string v) { });
            RowLabel(p, "API Key", "仅显示掩码，明文不落盘");
            _fKey = AddInput(p, "sk-...", "", delegate(string v) { });
            RowLabel(p, "模型");
            _fModel = AddInput(p, "例如：deepseek-chat / gpt-4o-mini", "", delegate(string v) { });
            RowLabel(p, "超时（毫秒）");
            _fTimeout = AddInput(p, "60000", "60000", delegate(string v) { });
            RowLabel(p, "失败重试次数");
            _fRetry = AddInput(p, "1", "1", delegate(string v) { });
            RowLabel(p, "额外请求头", "每行一条，格式 Header: value");
            _fHeaders = AddInput(p, "可选", "", delegate(string v) { });

            _fEnabled = new CheckBox { Text = "启用", Left = 8, Top = _y, AutoSize = true, ForeColor = Palette.Text, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 9F) };
            p.Controls.Add(_fEnabled);
            _fDefault = new CheckBox { Text = "设为默认", Left = 110, Top = _y, AutoSize = true, ForeColor = Palette.Text, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 9F) };
            p.Controls.Add(_fDefault);
            _y += 34;

            _testResult = new Label
            {
                Left = 8, Top = _y, AutoSize = true, MaximumSize = new Size(760, 0),
                Font = new Font("Microsoft YaHei UI", 9F), ForeColor = Palette.TextFaint, BackColor = Palette.Bg,
                Text = "尚未测试"
            };
            p.Controls.Add(_testResult);

            ReloadProviderList();
            return p;
        }

        private void ReloadProviderList()
        {
            if (_provList == null) return;
            var rows = new List<ListRow>();
            if (_providers != null)
            {
                foreach (var pr in _providers.Providers)
                {
                    rows.Add(new ListRow
                    {
                        Icon = pr.Enabled ? "●" : "○",
                        Cells = new[]
                        {
                            pr.Name,
                            pr.Protocol,
                            string.IsNullOrEmpty(pr.Model) ? "—" : pr.Model,
                            pr.IsDefault ? "✓" : "",
                            string.IsNullOrEmpty(pr.LastStatus) ? "未测试" : pr.LastStatus
                        },
                        Tag = pr.Id,
                        Accent = pr.IsDefault ? Palette.Accent : (Color?)null
                    });
                }
            }
            _provList.SetRows(rows);
            if (rows.Count > 0 && _provList.SelectedIndex < 0) _provList.SelectIndex(0);
            LoadSelected();
        }

        private void LoadSelected()
        {
            if (_provList == null || _providers == null) return;
            var row = _provList.SelectedRow;
            var pr = row == null ? null : _providers.Get((string)row.Tag);
            _editing = pr;
            if (pr == null) return;
            _fName.Text = pr.Name;
            _fProtocol.SelectedIndex = pr.Protocol == "openai-responses" ? 1 : pr.Protocol == "custom" ? 2 : 0;
            _fBase.Text = pr.BaseUrl;
            // 规格 4.2：界面绝不回显明文 Key，只显示掩码；用户不改动则保留原 Key
            _fKey.Text = string.IsNullOrEmpty(pr.ApiKey) ? "" : ProviderStore.MaskKey(pr.ApiKey);
            _fKey.Inner.PasswordChar = '\0';          // 掩码本身不是秘密，正常显示
            _keyUnchanged = true;
            _fKey.Inner.TextChanged += delegate { _keyUnchanged = false; };
            _fModel.Text = pr.Model;
            _fTimeout.Text = pr.TimeoutMs.ToString();
            _fRetry.Text = pr.MaxRetries.ToString();
            _fHeaders.Text = pr.ExtraHeaders;
            _fEnabled.Checked = pr.Enabled;
            _fDefault.Checked = pr.IsDefault;
            if (_testResult != null)
                _testResult.Text = string.IsNullOrEmpty(pr.LastStatus) ? "尚未测试" : ("最近状态：" + pr.LastStatus);
        }

        private void NewProvider()
        {
            if (_providers == null) { Toast("AI 服务未初始化", false); return; }
            _fName.Text = AiEnvironment.Local ? "本机模型" : "新服务商";
            _fBase.Text = AiEnvironment.Local ? "http://127.0.0.1:11434/v1" : "https://";
            _fProtocol.SelectedIndex=0;
            _fKey.Text = "";
            _fModel.Text = "";
            _fTimeout.Text = "60000";
            _fRetry.Text = "1";
            _fHeaders.Text = "";
            _fEnabled.Checked = true;
            _fDefault.Checked = _providers.Providers.Count == 0;
            _editing = null;
            _keyUnchanged=false;if(_testResult!=null)_testResult.Text="尚未测试";
            Toast("填写后点「保存」创建");
        }

        private void SaveEditing()
        {
            if (_providers == null) { Toast("AI 服务未初始化", false); return; }
            var name = (_fName.Text ?? "").Trim();
            if (name.Length == 0) { Toast("名称不能为空", false); return; }
            var baseUrl = (_fBase.Text ?? "").Trim();
            if (!baseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)) { Toast("Base URL 必须以 http 开头", false); return; }

            int timeout = 60000, retry = 1;
            int.TryParse((_fTimeout.Text ?? "").Trim(), out timeout);
            int.TryParse((_fRetry.Text ?? "").Trim(), out retry);
            if (timeout < 1000) timeout = 60000;
            if (retry < 0 || retry > 5) retry = 1;

            var keyText = (_fKey.Text ?? "").Trim();
            bool keepOldKey = _keyUnchanged || (keyText.Length > 0 && keyText == (_editing == null ? "" : ProviderStore.MaskKey(_editing.ApiKey ?? "")));
            if (_editing == null)
            {
                var np = new AiProvider
                {
                    Name = name,
                    Protocol = _fProtocol.SelectedIndex == 1 ? "openai-responses" : _fProtocol.SelectedIndex == 2 ? "custom" : "openai",
                    BaseUrl = baseUrl,
                    ApiKey = keyText,
                    Model = (_fModel.Text ?? "").Trim(),
                    TimeoutMs = timeout,
                    MaxRetries = retry,
                    ExtraHeaders = _fHeaders.Text ?? "",
                    Enabled = _fEnabled.Checked,
                    IsDefault = _fDefault.Checked
                };
                _providers.Add(np);
                _store.Log("info", "settings", "新增 AI 服务商：" + name);
            }
            else
            {
                _editing.Name = name;
                _editing.Protocol = _fProtocol.SelectedIndex == 1 ? "openai-responses" : _fProtocol.SelectedIndex == 2 ? "custom" : "openai";
                _editing.BaseUrl = baseUrl;
                if (!keepOldKey) _editing.ApiKey = keyText;
                _editing.Model = (_fModel.Text ?? "").Trim();
                _editing.TimeoutMs = timeout;
                _editing.MaxRetries = retry;
                _editing.ExtraHeaders = _fHeaders.Text ?? "";
                _editing.Enabled = _fEnabled.Checked;
                _providers.Update(_editing);
                if (_fDefault.Checked) _providers.SetDefault(_editing.Id);
                _store.Log("info", "settings", "修改 AI 服务商：" + name);
            }
            _providers.Save();
            ReloadProviderList();
            RaiseProviders();
            Toast("服务商已保存（Key 已加密）");
        }

        private void DeleteProvider()
        {
            if (_providers == null) return;
            var row = _provList.SelectedRow;
            if (row == null) { Toast("先选中一个服务商", false); return; }
            var pr = _providers.Get((string)row.Tag);
            if (pr == null) return;
            if (!Prompt.Confirm(this, "删除服务商", "删除「" + pr.Name + "」？\n\n该服务商的配置与加密 Key 将一并删除，此操作不可撤销。", Palette)) return;
            _providers.Remove(pr.Id);
            _providers.Save();
            _store.Log("info", "settings", "删除 AI 服务商：" + pr.Name);
            ReloadProviderList();
            RaiseProviders();
            Toast("已删除");
        }

        private void SetDefaultProvider()
        {
            if (_providers == null) return;
            var row = _provList.SelectedRow;
            if (row == null) { Toast("先选中一个服务商", false); return; }
            _providers.SetDefault((string)row.Tag);
            _providers.Save();
            ReloadProviderList();
            RaiseProviders();
            Toast("已设为默认");
        }

        private async void TestProvider(bool streaming)
        {
            if (_providers == null || _testing) return;
            var row = _provList.SelectedRow;
            var provider = row == null ? null : _providers.Get((string)row.Tag);
            if (provider == null) { Toast("先选中一个服务商", false); return; }
            if (string.IsNullOrWhiteSpace(provider.BaseUrl)) { Toast("请先填写 Base URL", false); return; }

            _testing = true;
            _testResult.ForeColor = Palette.TextDim;
            _testResult.Text = streaming ? "正在检测模型文本流…" : "正在测试连接…";
            var snapshot = provider.Clone();
            var environment = AiEnvironment.Mode;
            var label = streaming ? "文本流与结束帧验证" : "连接测试";
            try
            {
                var result = await Task.Run(delegate
                {
                    using (var timeout = new CancellationTokenSource(15000))
                    {
                        if (streaming) return AgentRuntime.ProbeModel(snapshot, timeout.Token);
                        var reply = new AiClient(snapshot).TestConnection(timeout.Token);
                        return new ModelProbe { Ok = reply.Ok, HttpStatus = reply.HttpStatus, ElapsedMs = reply.ElapsedMs,
                            Error = reply.Error, ErrorKind = reply.ErrorKind };
                    }
                });
                if (IsDisposed || environment != AiEnvironment.Mode || !ReferenceEquals(_providers.Get(provider.Id), provider) ||
                    provider.UpdatedAt != snapshot.UpdatedAt || !ReferenceEquals(_editing, provider)) return;
                provider.LastStatus = label + (result.Ok ? (" · " + result.ElapsedMs + " ms") : (" · " + result.ErrorKind));
                _providers.Save();
                ReloadProviderList();
                _testResult.ForeColor = result.Ok ? Color.FromArgb(110, 231, 183) : Color.FromArgb(248, 150, 150);
                _testResult.Text = result.Ok
                    ? label + "通过（" + result.ElapsedMs + " ms，HTTP " + result.HttpStatus + "）"
                    : label + "失败 · " + KindText(result.ErrorKind) + " · " + result.Error;
                _store.Log(result.Ok ? "info" : "warn", "ai", label + " " + provider.Name + " · " +
                    (result.Ok ? "成功" : "失败[" + result.ErrorKind + "]"));
            }
            catch (Exception error) { if (!IsDisposed) _testResult.Text = label + "失败 · " + error.Message; }
            finally { _testing = false; }
        }

        private static string KindText(string kind)
        {
            switch (kind)
            {
                case "auth": return "API Key 无效";
                case "model": return "模型不存在";
                case "url": return "Base URL 错误";
                case "network": return "网络连接失败";
                case "timeout": return "请求超时";
                case "ratelimit": return "服务商限流";
                case "server": return "服务端错误";
                case "protocol": return "接口协议不兼容";
                case "output_limit": return "服务商输出受限";
                default: return "未知错误";
            }
        }

        private void RaiseProviders()
        {
            var h = ProvidersChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        // ---------------- 3. 天气 ----------------

        private Control PageWeather()
        {
            var p = NewPage();
            Section(p, "天气数据", "数据源：Open-Meteo（免 Key，真实数据）");

            var cities = new CityStore();
            RowLabel(p, "当前城市：" + (cities.Current == null ? "未设置（使用默认坐标）" : cities.Current.Display));
            RowLabel(p, "常用城市：" + (cities.Favorites.Count == 0 ? "无" : string.Join("、", cities.Favorites.Select(c => c.Name).ToArray())));

            var bar = new Panel { Left = 0, Top = _y, Height = 36, Width = 700, BackColor = Palette.Bg };
            AddButton(bar, "去天气页管理城市", 8, 2, 150, delegate
            {
                MessageBox.Show(this, "请到左侧导航「天气」页面搜索、添加常用城市并切换当前城市。\n切换后坐标会立即保存。", "DeskBox");
            }, true);
            p.Controls.Add(bar);
            _y += 42;

            RowLabel(p, "自动刷新间隔");
            var opts = new[] { "10 分钟", "30 分钟", "60 分钟" };
            int cur = _store.Settings.WeatherRefreshMinutes == 30 ? 1 : _store.Settings.WeatherRefreshMinutes == 60 ? 2 : 0;
            AddSelect(p, opts, cur, delegate(int i)
            {
                _store.Settings.WeatherRefreshMinutes = i == 1 ? 30 : i == 2 ? 60 : 10;
                _store.SaveSettingsOnly();
                Toast("刷新间隔已保存");
            });

            RowLabel(p, "温度单位");
            AddSelect(p, new[] { "摄氏度 °C", "华氏度 °F" }, _store.Settings.TempUnit == "F" ? 1 : 0, delegate(int i)
            {
                _store.Settings.TempUnit = i == 1 ? "F" : "C";
                _store.SaveSettingsOnly();
                Toast("已保存");
            });

            Gap();
            Section(p, "数据源", null);
            RowLabel(p, "当前数据源：Open-Meteo（免 Key）", "当前版本使用单一可靠数据源；网络失败时明确显示缓存或离线状态");
            AddButton(p, "保存设置", 8, _y, 100, delegate { _store.SaveSettingsOnly(); Toast("设置已保存"); }, true);
            return p;
        }

        // ---------------- 4. 自动化 ----------------

        private Control PageAutomation()
        {
            var p = NewPage();
            Section(p, "执行策略", "影响自动化工作流的真实执行行为");

            RowLabel(p, "失败处理");
            AddSelect(p, new[] { "停止后续节点（stop）", "跳过并继续（skip）" }, _store.Settings.WorkflowFailMode == "skip" ? 1 : 0, delegate(int i)
            {
                _store.Settings.WorkflowFailMode = i == 1 ? "skip" : "stop";
                _store.SaveSettingsOnly();
                Toast("已保存");
            });

            RowLabel(p, "并发上限");
            var conc = new[] { "1", "2", "4" };
            int ci = Array.IndexOf(conc, _store.Settings.WorkflowConcurrency.ToString());
            AddSelect(p, conc, ci < 0 ? 0 : ci, delegate(int i)
            {
                _store.Settings.WorkflowConcurrency = int.Parse(conc[i]);
                _store.SaveSettingsOnly();
                Toast("已保存");
            });

            RowLabel(p, "执行历史保留条数");
            var keeps = new[] { "100", "500", "1000" };
            int ki = Array.IndexOf(keeps, _store.Settings.WorkflowHistoryKeep.ToString());
            AddSelect(p, keeps, ki < 0 ? 1 : ki, delegate(int i)
            {
                _store.Settings.WorkflowHistoryKeep = int.Parse(keeps[i]);
                _store.SaveSettingsOnly();
                Toast("已保存");
            });

            Gap();
            AddCheck(p, "高风险操作执行前需确认（删除/覆盖/批量移动）", _store.Settings.WorkflowConfirmDanger, delegate(bool v) { _store.Settings.WorkflowConfirmDanger = v; });

            Gap();
            Section(p, "当前能力说明", null);
            RowLabel(p, "已支持：移动 / 复制 / 备份 / 记录，以及 v2 AI 总结、报告、任务和通知模板", null);
            RowLabel(p, "v2 执行器支持条件、节点间数据传递、取消、超时、重试与失败策略", "可在自动化页创建模板并预演/执行");
            AddButton(p, "保存设置", 8, _y, 100, delegate { _store.SaveSettingsOnly(); Toast("设置已保存"); }, true);
            return p;
        }

        // ---------------- 5. 文件与隐私 ----------------

        private Control PagePrivacy()
        {
            var p = NewPage();
            Section(p, "AI 文件访问授权", "AI 工具只能在你授权的目录内读写；未授权时工具调用一律被拒绝");
            AddCheck(p, "允许 AI 文件访问（读取、内容搜索、经确认写入）", _store.Settings.AiFileAccess, delegate(bool v) { _store.Settings.AiFileAccess = v; NotifyPermissionsChanged(); });
            RowLabel(p, "启用后，读取的内容可能发往当前 AI 服务商；授权变更立即影响下一次工具调用。");

            var roots = TrustedRoots.Load();
            RowLabel(p, "已授权目录：" + (roots.Count == 0 ? "无（仍可能有本次明确选定的单文件授权）" : roots.Count + " 个"));

            int y = _y;
            foreach (var r in roots)
            {
                var label = new TextBox
                {
                    Text = r, Left = 16, Top = y + 4, Width = 440, ReadOnly = true, BorderStyle = BorderStyle.None,
                    Font = new Font("Consolas", 8.5F), ForeColor = Palette.TextDim, BackColor = Palette.Bg
                };
                p.Controls.Add(label);
                var root = r;
                var btn = new DarkButton("移除") { Left = 470, Top = y, Width = 62, Height = 24 };
                btn.Palette = Palette;
                btn.Click += delegate
                {
                    try { TrustedRoots.Remove(root); } catch (Exception ex) { Toast(ex.Message); return; }
                    NotifyPermissionsChanged();
                    _store.Log("info", "settings", "移除授权目录：" + root);
                    Switch(_current);
                    Toast("已移除授权");
                };
                p.Controls.Add(btn);
                y += 30;
            }
            _y = y + 6;

            var bar = new Panel { Left = 0, Top = _y, Height = 36, Width = 700, BackColor = Palette.Bg };
            AddButton(bar, "＋ 添加授权目录", 8, 2, 140, delegate { AddRoot(); }, true);
            AddButton(bar, "撤销临时文件授权", 160, 2, 160, delegate { if (TemporaryAccessRevoked != null) TemporaryAccessRevoked(this, EventArgs.Empty); Toast("已撤销本次选定文件与目录的临时授权"); });
            p.Controls.Add(bar);
            _y += 42;

            Gap();
            Section(p, "文件操作安全", null);
            AddCheck(p, "删除/覆盖文件前需要确认", _store.Settings.FileOpConfirm, delegate(bool v) { _store.Settings.FileOpConfirm = v; });

            Gap();
            Section(p, "数据目录", null);
            RowLabel(p, "位置：" + Store.DataDir);
            var openBar = new Panel { Left = 0, Top = _y, Height = 36, Width = 700, BackColor = Palette.Bg };
            AddButton(openBar, "打开数据目录", 8, 2, 120, delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", Store.DataDir); } catch (Exception ex) { Toast("打开失败：" + ex.Message, false); }
            });
            p.Controls.Add(openBar);
            _y += 42;

            AddButton(p, "保存设置", 8, _y, 100, delegate { _store.SaveSettingsOnly(); Toast("设置已保存"); }, true);
            return p;
        }

        private void AddRoot()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择允许 AI 读写的目录（建议选择具体资料目录，不要选整个磁盘）";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { TrustedRoots.Add(dlg.SelectedPath); } catch (Exception ex) { Toast(ex.Message); return; }
                NotifyPermissionsChanged();
                _store.Log("info", "settings", "添加授权目录：" + dlg.SelectedPath);
                Switch(_current);
                Toast("已添加授权目录");
            }
        }

        // ---------------- 6. 通知 ----------------

        private Control PageNotify()
        {
            var p = NewPage();
            Section(p, "提醒方式", "当前版本仅支持应用内提醒");

            AddCheck(p, "启用应用内提醒", _store.Settings.NotifyInApp, delegate(bool v) { _store.Settings.NotifyInApp = v; });
            AddCheck(p, "任务到点提醒", _store.Settings.NotifyTask, delegate(bool v) { _store.Settings.NotifyTask = v; });

            RowLabel(p, "系统托盘气泡通知", "启用后，任务与自动化通知会同步显示托盘气泡");
            AddCheck(p, "启用托盘气泡", _store.Settings.NotifyTray, delegate(bool v) { _store.Settings.NotifyTray = v; });

            Gap();
            Section(p, "当前行为说明", null);
            RowLabel(p, "任务提醒：写入通知中心并在状态区提示，不阻塞当前操作", null);
            RowLabel(p, "托盘气泡：使用 WinForms NotifyIcon，不依赖 Windows 通知中心", null);
            AddButton(p, "保存设置", 8, _y, 100, delegate { _store.SaveSettingsOnly(); Toast("设置已保存"); }, true);
            return p;
        }

        // ---------------- 7. 系统 ----------------

        private Control PageSystem()
        {
            var p = NewPage();
            Section(p, "启动与运行", null);
            AddCheck(p, "开机自动启动（写入 HKCU Run，需你明确启用）", _store.Settings.AutoStart, delegate(bool v)
            {
                _store.Settings.AutoStart = v;
                try { AutoStart.Apply(v); Toast(v ? "已设置开机启动" : "已取消开机启动"); }
                catch (Exception ex) { Toast("设置失败：" + ex.Message, false); }
            });

            Gap();
            Section(p, "日志", null);
            RowLabel(p, "日志保留天数");
            var days = new[] { "7 天", "30 天", "90 天" };
            int di = _store.Settings.LogKeepDays == 7 ? 0 : _store.Settings.LogKeepDays == 90 ? 2 : 1;
            AddSelect(p, days, di, delegate(int i)
            {
                _store.Settings.LogKeepDays = i == 0 ? 7 : i == 2 ? 90 : 30;
                _store.SaveSettingsOnly();
                Toast("已保存");
            });

            var bar = new Panel { Left = 0, Top = _y, Height = 36, Width = 700, BackColor = Palette.Bg };
            AddButton(bar, "按保留天数清理日志", 8, 2, 160, delegate { CleanLogs(); });
            p.Controls.Add(bar);
            _y += 42;

            Gap();
            Section(p, "数据备份", "备份为纯文件复制，不含任何上传");
            var bar2 = new Panel { Left = 0, Top = _y, Height = 36, Width = 700, BackColor = Palette.Bg };
            AddButton(bar2, "立即备份数据", 8, 2, 130, delegate { Backup(); }, true);
            AddButton(bar2, "从备份恢复", 146, 2, 120, delegate { RestoreBackup(); });
            p.Controls.Add(bar2);
            _y += 42;
            RowLabel(p, "恢复会在下次启动前执行，并自动保留恢复前安全快照", null);

            Gap();
            Section(p, "版本信息", null);
            RowLabel(p, "DeskBox 0.6.2（项目概览 · 任务月历）", null);
            RowLabel(p, "数据目录：" + Store.DataDir, null);
            RowLabel(p, "AI 助手：AI&SI", null);
            RowLabel(p, "全局呼出：Win+F · 备用命令中心：Ctrl+Alt+Space · 应用内命令：Ctrl+K", null);
            RowLabel(p, "工程快捷键：Ctrl+Shift+A AI · Ctrl+Shift+E 工具 · Ctrl+Shift+P 项目", null);
            RowLabel(p, "自动更新：未实现（需手动下载新版覆盖）", null);
            return p;
        }

        private void CleanLogs()
        {
            try
            {
                var file = Path.Combine(Store.DataDir, "logs.jsonl");
                if (!File.Exists(file)) { Toast("没有日志文件", false); return; }
                var cutoff = DateTime.Now.AddDays(-Math.Max(1, _store.Settings.LogKeepDays));
                var lines = File.ReadAllLines(file);
                var kept = new List<string>();
                int removed = 0;
                foreach (var line in lines)
                {
                    bool keep = true;
                    try
                    {
                        var e = Json.FromJson<LogEntry>(line);
                        if (e != null && e.Ts > 0)
                        {
                            var ts = DateTimeOffset.FromUnixTimeMilliseconds(e.Ts).LocalDateTime;
                            if (ts < cutoff) { keep = false; removed++; }
                        }
                    }
                    catch { }
                    if (keep) kept.Add(line);
                }
                File.WriteAllLines(file, kept.ToArray());
                _store.Log("info", "settings", "清理日志：移除 " + removed + " 条，保留 " + kept.Count + " 条");
                Toast("已清理 " + removed + " 条日志");
            }
            catch (Exception ex) { Toast("清理失败：" + ex.Message, false); }
        }

        private bool _backupWork;

        private async void Backup()
        {
            if (_backupWork) return;
            _backupWork = true;
            Toast("正在复制数据并核验证据…");
            try
            {
                var result = await Task.Run(() => DataBackupService.CreateBackup("manual"));
                if (IsDisposed) return;
                if (!result.Ok)
                {
                    Toast(result.Message, false);
                    return;
                }
                _store.Log("info", "backup", result.Message + "：" + result.Directory);
                MessageBox.Show(this,
                    result.Message + "。\n\n位置：\n" + result.Directory +
                    "\n\n服务商密钥使用原 Windows 用户凭据加密。更换用户或电脑后需重新填写。",
                    "DeskBox 备份", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Toast("备份完成");
            }
            finally { _backupWork = false; }
        }

        private async void RestoreBackup()
        {
            if (_backupWork) return;
            string selected;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择 DeskBox 备份目录（例如 manual_20261030_120000）";
                dialog.SelectedPath = Path.Combine(Store.DataDir, "backup");
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                selected = dialog.SelectedPath;
            }
            _backupWork = true;
            Toast("正在核对备份及证据…");
            try
            {
                var check = await Task.Run(() => DataBackupService.ValidateBackup(selected));
                if (IsDisposed) return;
                if (!check.Ok) { Toast(check.Message, false); return; }
                var warning = "将从以下备份恢复 " + check.FileCount + " 个文件：\n\n" + check.Directory +
                    "\n\n恢复会覆盖现有配置、任务和历史。系统会先创建恢复前安全快照，并在下次启动前执行。继续？";
                if (!Prompt.Confirm(this, "安排数据恢复", warning, Palette)) return;
                var result = await Task.Run(() => DataBackupService.ScheduleRestore(check.Directory));
                if (IsDisposed) return;
                if (!result.Ok) { Toast(result.Message, false); return; }
                _store.Log("info", "backup", "已安排下次启动恢复：" + check.Directory);
                MessageBox.Show(this, "恢复已安排。请从托盘退出 DeskBox 后重新启动。\n\n下次启动会先创建恢复前安全快照，再应用所选备份。", "DeskBox 恢复", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Toast("已安排恢复");
            }
            finally { _backupWork = false; }
        }

        protected override void OnActivated()
        {
            if (_providers != null) ReloadProviderList();
        }
    }
}
