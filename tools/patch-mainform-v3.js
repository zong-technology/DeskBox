const fs = require('fs');
const p = 'src/UI/MainForm.cs';
let s = fs.readFileSync(p, 'utf8');

// 1) 服务字段：加 ChatService / ProviderStore
if (!s.includes('ChatService')) {
  s = s.replace('        private DeskBox.Automation.WorkflowEngine _workflows;',
    '        private DeskBox.Automation.WorkflowEngine _workflows;\n' +
    '        private DeskBox.Ai.ProviderStore _providers;\n' +
    '        private DeskBox.Ai.ChatService _chat;');
}

// 2) EnsureServices 里初始化 ProviderStore / ChatService
if (!s.includes('new DeskBox.Ai.ProviderStore')) {
  s = s.replace('            DeskBox.Ai.AiService.Instance.LoadConfig();',
    '            DeskBox.Ai.AiService.Instance.LoadConfig();\n' +
    '            try\n' +
    '            {\n' +
    '                _providers = new DeskBox.Ai.ProviderStore();\n' +
    '                _chat = new DeskBox.Ai.ChatService(_store, _providers);\n' +
    '            }\n' +
    '            catch (Exception ex) { AppLog.Write("error", "system", "AI 服务初始化失败: " + ex.Message); }');
}

// 3) 导航项：按规格命名
const railOld = `            string[,] items = new string[,]
            {
                { "desktop", "🗂", "桌面" },
                { "search", "🔍", "搜索" },
                { "library", "★", "收藏" },
                { "disk", "💽", "磁盘" },
                { "history", "🕘", "历史" },
                { "ai", "✦", "AI" },
                { "automation", "⚡", "自动化" },
                { "monitor", "📈", "监控" },
                { "clipboard", "📋", "剪贴板" },
            };`;
const railNew = `            string[,] items = new string[,]
            {
                { "desktop", "🏠", "首页" },
                { "ai", "◈", "WorkBuddy" },
                { "automation", "⚡", "自动化" },
                { "search", "🔍", "文件管理" },
                { "tasks", "☑", "任务" },
                { "weather", "☁", "天气" },
                { "monitor", "📈", "监控" },
                { "history", "🕘", "工作记录" },
                { "library", "★", "收藏" },
                { "disk", "💽", "磁盘" },
                { "clipboard", "📋", "剪贴板" },
            };`;
if (s.includes(railOld)) s = s.replace(railOld, railNew);

// 4) CreateView 增加 tasks / weather 分支
if (!s.includes('case "tasks"')) {
  s = s.replace('                    case "clipboard": return BuildClipboardView();',
    '                    case "clipboard": return BuildClipboardView();\n' +
    '                    case "tasks": return new DeskBox.UI.Views.TasksView(_store);\n' +
    '                    case "weather": return BuildWeatherView();');
}

// 5) BuildAiView 改为 WorkBuddy ChatView
const oldAi = `        private DeskBox.UI.Views.AiView BuildAiView()
        {
            var v = new DeskBox.UI.Views.AiView(_store);
            if (_searchView != null)
            {
                var cur = _searchView.CurrentEntry();
                if (cur != null) v.SetPaths(new List<string> { cur.Path });
            }
            return v;
        }`;
const newAi = `        private DeskBox.UI.Views.ChatView _chatView;
        private UserControl BuildAiView()
        {
            // WorkBuddy 对话界面；若 ChatView 尚未就绪则退回本地 AI 视图
            try
            {
                _chatView = new DeskBox.UI.Views.ChatView(_store);
                _chatView.SetServices(_chat, _providers);
                _chatView.SettingsRequested += delegate { OpenSettings(); };
                return _chatView;
            }
            catch (Exception ex)
            {
                AppLog.Write("error", "system", "ChatView 创建失败，回退本地 AI: " + ex.Message);
                return new DeskBox.UI.Views.AiView(_store);
            }
        }

        private UserControl BuildWeatherView()
        {
            return new DeskBox.UI.Views.WeatherView(_store);
        }`;
if (s.includes(oldAi)) s = s.replace(oldAi, newAi);

fs.writeFileSync(p, s, 'utf8');
console.log('MainForm patched:',
  'chat fields', s.includes('ChatService'),
  '| rail', s.includes('WorkBuddy'),
  '| tasks', s.includes('case "tasks"'),
  '| chatview', s.includes('new DeskBox.UI.Views.ChatView'));
