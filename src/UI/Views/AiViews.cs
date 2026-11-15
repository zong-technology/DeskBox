using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Automation;
using DeskBox.Core;

namespace DeskBox.UI.Views
{
    /// <summary>AI 文件助手：摘要 / 对比 / 工作总结 / 整理建议（默认全本地，远程需显式开启）。</summary>
    public class AiView : ViewBase
    {
        private readonly Store _store;
        private DarkInput _ask;
        private DarkButton _btnAsk;
        private DarkButton _btnSummarize, _btnCompare, _btnReport, _btnOrganize, _btnRemote;
        private DarkList _answerList;
        private Label _provider;
        private TextBox _answerText;
        private readonly List<string> _paths = new List<string>();

        public AiView(Store store) : base(store, "AI 文件助手", "摘要/对比/总结/整理建议默认全部本地计算，不联网、不上传文件内容")
        {
            _store = store;
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Palette.Bg };
            _ask = new DarkInput { Left = 0, Top = 2, Width = 520, Placeholder = "问点什么，或输入自然语言搜索条件…" };
            _ask.Palette = Palette;
            _ask.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AskFree(); } };
            _btnAsk = new DarkButton("提问") { Primary = true, Left = 528, Top = 2, Width = 70 };
            _btnAsk.Click += delegate { AskFree(); };

            _btnSummarize = new DarkButton("摘要所选文件") { Left = 606, Top = 2, Width = 110 };
            _btnSummarize.Click += delegate { RunLocal("summarize"); };
            _btnCompare = new DarkButton("对比两个文件") { Left = 724, Top = 2, Width = 110 };
            _btnCompare.Click += delegate { RunCompare(); };
            _btnReport = new DarkButton("生成工作总结") { Left = 842, Top = 2, Width = 110 };
            _btnReport.Click += delegate { RunLocal("report"); };
            _btnOrganize = new DarkButton("整理建议") { Left = 960, Top = 2, Width = 90 };
            _btnOrganize.Click += delegate { RunLocal("organize"); };

            _provider = new Label { Left = 0, Top = 48, AutoSize = true, ForeColor = Palette.TextFaint, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            UpdateProviderLabel();
            top.Controls.AddRange(new Control[] { _ask, _btnAsk, _btnSummarize, _btnCompare, _btnReport, _btnOrganize, _provider });

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 420,
                BackColor = Palette.Bg,
                SplitterWidth = 6
            };
            split.Panel1.BackColor = Palette.Bg;
            split.Panel2.BackColor = Palette.Bg;
            _answerList = new DarkList { Dock = DockStyle.Fill };
            _answerList.Palette = Palette;
            _answerList.SetColumns(new[] { "结论" }, new[] { 400 });
            _answerText = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Palette.PanelAlt, ForeColor = Palette.Text, BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            split.Panel1.Controls.Add(_answerList);
            split.Panel2.Controls.Add(_answerText);

            Body.Controls.Add(split);
            Body.Controls.Add(top);
            split.BringToFront();

            HintLabel.Text = "提示：在「搜索」或「桌面」中选中文件后，这里可直接对选中项做摘要/对比/统计";
        }

        private void UpdateProviderLabel()
        {
            var ai = DeskBox.Ai.AiService.Instance;
            _provider.Text = "当前引擎：" + ai.ActiveProvider + (ai.RemoteEnabled ? "（远程已启用，发送前会二次确认）" : "（全本地，不联网）");
        }

        public void SetPaths(List<string> paths)
        {
            _paths.Clear();
            if (paths != null) _paths.AddRange(paths.Where(p => !string.IsNullOrEmpty(p)));
            HintLabel.Text = _paths.Count == 0 ? "未选中文件" : "已选中 " + _paths.Count + " 个文件：" + string.Join("，", _paths.Take(3).Select(Path.GetFileName).ToArray());
        }

        private void AskFree()
        {
            var text = _ask.Text.Trim();
            if (text.Length == 0) return;
            var ai = DeskBox.Ai.AiService.Instance;
            var req = new DeskBox.Ai.AiRequest { Kind = "nlsearch", Text = text, Paths = new List<string>(_paths) };
            var ans = ai.Ask(req);
            Show(ans);
            var q = DeskBox.Ai.NlQuery.Parse(text);
            HintLabel.Text = "已解析：" + DeskBox.Ai.NlQuery.Explain(q) + "（可到「搜索」执行）";
        }

        private void RunLocal(string kind)
        {
            var ai = DeskBox.Ai.AiService.Instance;
            var req = new DeskBox.Ai.AiRequest { Kind = kind, Paths = new List<string>(_paths) };
            if (kind == "summarize" && _paths.Count == 0) { _answerText.Text = "请先在搜索结果中选中一个文件。"; return; }
            if (kind == "report" && _paths.Count == 0) { _answerText.Text = "请先在搜索结果中选中若干文件。"; return; }
            var ans = ai.Ask(req);
            Show(ans);
        }

        private void RunCompare()
        {
            if (_paths.Count < 2) { _answerText.Text = "请选中 2 个文件（Ctrl 多选）后再对比。"; return; }
            var ai = DeskBox.Ai.AiService.Instance;
            var ans = ai.Ask(new DeskBox.Ai.AiRequest { Kind = "compare", Paths = _paths.Take(2).ToList() });
            Show(ans);
        }

        private void Show(DeskBox.Ai.AiAnswer a)
        {
            var rows = new List<ListRow>();
            if (!a.Ok)
            {
                rows.Add(new ListRow { Icon = "⚠", Cells = new[] { "失败：" + a.Error }, Accent = Color.FromArgb(248, 113, 113) });
            }
            else
            {
                if (!string.IsNullOrEmpty(a.Title)) rows.Add(new ListRow { Icon = "◆", Cells = new[] { a.Title }, Accent = Palette.Accent });
                foreach (var b in a.Bullets) rows.Add(new ListRow { Icon = "·", Cells = new[] { b } });
                foreach (var m in a.Meta) rows.Add(new ListRow { Icon = " ", Cells = new[] { m.Key + "：" + m.Value } });
            }
            _answerList.SetRows(rows);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("【" + (a.Provider ?? "本地") + "】" + (a.Title ?? ""));
            sb.AppendLine();
            if (!string.IsNullOrEmpty(a.Text)) sb.AppendLine(a.Text).AppendLine();
            foreach (var b in a.Bullets) sb.AppendLine("• " + b);
            if (a.Meta.Count > 0)
            {
                sb.AppendLine();
                foreach (var m in a.Meta) sb.AppendLine(m.Key + "：" + m.Value);
            }
            _answerText.Text = sb.ToString();
        }

        protected override void OnActivated() { UpdateProviderLabel(); }
    }

    /// <summary>自动化工作流：触发器 / 条件 / 动作 / 预演 / 执行 / 运行日志。</summary>
    public class AutomationView : ViewBase
    {
        private WorkflowGraphControl _graph;
        private Action<WorkflowRun> _runCompleted;
        private readonly WorkflowEngine _engine;
        private DarkList _wfList, _runList;
        private DarkButton _btnNew, _btnRun, _btnDry, _btnDelete, _btnRefresh, _btnCopy, _btnToggle, _btnCancel;
        private DarkInput _name;
        private DarkSelect _trigger, _action;
        private DarkInput _root, _target, _pattern;
        private Label _status;

        public AutomationView(Store store, WorkflowEngine engine) : base(store, "自动化工作流", "所有动作先预演再执行；删除类动作只能移入备份目录，可撤销")
        {
            _engine = engine;
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Palette.Bg };
            top.AutoScroll = true;
            _name = new DarkInput { Left = 0, Top = 2, Width = 180, Placeholder = "工作流名称" };
            _name.Palette = Palette;
            _trigger = new DarkSelect(new[] { "手动", "启动时", "定时（60 分钟）" }) { Left = 188, Top = 2, Width = 140 };
            _trigger.Palette = Palette;
            _action = new DarkSelect(new[] { "移动到目录", "复制到目录", "备份副本", "移动后删源（进备份）", "仅记录日志", "AI 总结首个文件（v2）", "AI 生成工作报告（v2）", "创建任务并通知（v2）" }) { Left = 336, Top = 2, Width = 190 };
            _action.Palette = Palette;
            _root = new DarkInput { Left = 534, Top = 2, Width = 220, Placeholder = "监控目录，如 D:\\桌面" };
            _root.Palette = Palette;
            _pattern = new DarkInput { Left = 762, Top = 2, Width = 100, Placeholder = "*.xlsx" };
            _pattern.Palette = Palette;
            _target = new DarkInput { Left = 870, Top = 2, Width = 200, Placeholder = "目标目录（可选）" };
            _target.Palette = Palette;

            _btnNew = new DarkButton("新建工作流") { Primary = true, Left = 0, Top = 42, Width = 110 };
            _btnNew.Click += delegate { Create(); };
            _btnDry = new DarkButton("预演") { Left = 118, Top = 42, Width = 70 };
            _btnDry.Click += delegate { Run(true); };
            _btnRun = new DarkButton("执行") { Left = 196, Top = 42, Width = 70 };
            _btnRun.Click += delegate { Run(false); };
            _btnDelete = new DarkButton("删除") { Left = 274, Top = 42, Width = 70 };
            _btnDelete.Click += delegate { DeleteSelected(); };
            _btnRefresh = new DarkButton("刷新") { Left = 352, Top = 42, Width = 70 };
            _btnRefresh.Click += delegate { Reload(); };
            _btnCopy = new DarkButton("复制") { Left = 430, Top = 42, Width = 70 };
            _btnCopy.Click += delegate { CopySelected(); };
            _btnToggle = new DarkButton("启用/停用") { Left = 508, Top = 42, Width = 90 };
            _btnToggle.Click += delegate { ToggleSelected(); };
            _btnCancel = new DarkButton("取消运行") { Left = 606, Top = 42, Width = 90, Enabled = false };
            _btnCancel.Click += delegate { if (_engine != null) { _engine.CancelAll(); _status.Text = "正在取消…"; } };
            _status = new Label { Left = 704, Top = 50, AutoSize = true, ForeColor = Palette.TextDim, Font = new Font("Microsoft YaHei UI", 8.5F), BackColor = Palette.Bg };
            top.Controls.AddRange(new Control[] { _name, _trigger, _action, _root, _pattern, _target, _btnNew, _btnDry, _btnRun, _btnDelete, _btnRefresh, _btnCopy, _btnToggle, _btnCancel, _status });

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 560,
                BackColor = Palette.Bg,
                SplitterWidth = 6
            };
            split.Panel1.BackColor = Palette.Bg;
            split.Panel2.BackColor = Palette.Bg;
            _wfList = new DarkList { Dock = DockStyle.Fill };
            _wfList.Palette = Palette;
            _wfList.SetColumns(new[] { "名称", "触发器", "动作", "上次结果" }, new[] { 180, 90, 160, 260 });
            _wfList.SelectionChanged += delegate { ReloadGraph(); };

            _runList = new DarkList { Dock = DockStyle.Fill };
            _runList.Palette = Palette;
            _runList.SetColumns(new[] { "时间", "工作流", "结果" }, new[] { 130, 150, 420 });

            split.Panel1.Controls.Add(_wfList);
            split.Panel2.Controls.Add(_runList);

            Body.Controls.Add(split);
            _graph = new WorkflowGraphControl(Palette) { Dock = DockStyle.Top, Height = 184 };
            _graph.InspectRequested += delegate(WorkflowNode node, NodeRecord record)
            {
                using (var dialog = new Form { Text = node.Label + " · 节点详情", Width = 800, Height = 580, StartPosition = FormStartPosition.CenterParent })
                {
                    var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, BackColor = Palette.CardBg, ForeColor = Palette.Text, Font = new Font("Consolas", 10), Text = "节点：" + node.Id + "\r\n参数：\r\n" + Json.ToJson(node.Params) + "\r\n\r\n" + (record == null ? "没有节点运行记录；这不是正在执行的进度。" : record.Describe() + "\r\n\r\n输入：\r\n" + record.InputSummary + "\r\n\r\n输出：\r\n" + record.OutputSummary + "\r\n\r\n错误：\r\n" + record.Error) };
                    dialog.Controls.Add(text); dialog.ShowDialog(this);
                }
            };
            Body.Controls.Add(_graph);
            Body.Controls.Add(top);
            split.BringToFront();

            if (_engine != null) { _runCompleted = delegate(WorkflowRun r) { SafeUi(Reload); }; _engine.RunCompleted += _runCompleted; }
        }

        private void SafeUi(Action a)
        {
            try { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(a); else a(); } catch { }
        }

        public void Reload()
        {
            if (_engine == null) return;
            var selected = SelectedId();
            var rows = new List<ListRow>();
            foreach (var w in _engine.Workflows)
                rows.Add(new ListRow
                {
                    Icon = w.Enabled ? "⚡" : "·",
                    Cells = new[] { w.Name, TriggerName(w.Trigger), ActionsText(w), w.LastResult ?? "未运行" },
                    Tag = w.Id
                });
            _wfList.SetRows(rows);
            if (rows.Count > 0) _wfList.SelectIndex(Math.Max(0, rows.FindIndex(x => (string)x.Tag == selected)));

            var runs = new List<ListRow>();
            foreach (var r in _engine.Runs.Take(100))
                runs.Add(new ListRow
                {
                    Cells = new[] { DateTimeOffset.FromUnixTimeMilliseconds(r.Ts).LocalDateTime.ToString("MM-dd HH:mm:ss"), r.WorkflowName, (r.DryRun ? "[预演] " : "") + r.Message },
                    Accent = r.Ok ? (Color?)null : Color.FromArgb(248, 113, 113)
                });
            _runList.SetRows(runs);
            _status.Text = _engine.Workflows.Count + " 个工作流 · " + _engine.Runs.Count + " 次运行记录";
            ReloadGraph();
        }

        private void ReloadGraph()
        {
            if (_graph == null || _engine == null) return;
            var id = SelectedId(); var workflow = _engine.Workflows.FirstOrDefault(x => x.Id == id);
            _graph.SetWorkflow(workflow, _engine.Runs.FirstOrDefault(x => x.WorkflowId == id));
        }

        private static string TriggerName(string t)
        {
            if (t == "interval") return "定时";
            if (t == "startup") return "启动时";
            return "手动";
        }

        private static string ActionsText(Workflow w)
        {
            if (w.Nodes != null && w.Nodes.Count > 0)
                return string.Join(" → ", w.Nodes.Where(n => n.Enabled).Select(n => n.Label).Take(4).ToArray());
            if (w.Actions.Count == 0) return "无";
            return w.Actions[0].Type + (w.Actions[0].Params.ContainsKey("pattern") ? " " + w.Actions[0].Params["pattern"] : "");
        }

        private void Create()
        {
            if (_engine == null) return;
            if (string.IsNullOrWhiteSpace(_name.Text)) { _status.Text = "请填写工作流名称"; return; }
            if (_action.SelectedIndex != 7 && (string.IsNullOrWhiteSpace(_root.Text) || !Directory.Exists(_root.Text))) { _status.Text = "监控目录不存在：" + _root.Text; return; }
            if ((_action.SelectedIndex == 5 || _action.SelectedIndex == 6) && string.IsNullOrWhiteSpace(_target.Text)) { _status.Text = "AI 工作流需要填写结果文件或目标目录"; return; }
            var trigger = _trigger.SelectedIndex == 1 ? "startup" : _trigger.SelectedIndex == 2 ? "interval" : "manual";
            var wf = _engine.Add(_name.Text.Trim(), trigger, 60);
            if (_action.SelectedIndex >= 5)
            {
                BuildNodeTemplate(wf, _action.SelectedIndex);
                _engine.Save();
                _status.Text = "已创建 v2 节点工作流，建议先点「预演」";
                Reload();
                return;
            }
            var act = new WorkflowStep();
            switch (_action.SelectedIndex)
            {
                case 0: act.Type = "move"; break;
                case 1: act.Type = "copy"; break;
                case 2: act.Type = "backup"; break;
                case 3: act.Type = "deleteToBackup"; break;
                default: act.Type = "log"; break;
            }
            act.Params["root"] = _root.Text.Trim();
            act.Params["pattern"] = string.IsNullOrWhiteSpace(_pattern.Text) ? "*" : _pattern.Text.Trim();
            act.Params["targetDir"] = _target.Text.Trim();
            wf.Actions.Add(act);
            _engine.Save();
            _status.Text = "已创建工作流，建议先点「预演」";
            Reload();
        }

        private WorkflowNode Node(string type, string kind)
        {
            return new WorkflowNode { Id = "n_" + Guid.NewGuid().ToString("N").Substring(0, 8), Type = type, Kind = kind };
        }

        private void BuildNodeTemplate(Workflow wf, int template)
        {
            wf.UseNodes = true;
            wf.Description = template == 5 ? "搜索首个匹配文件并由真实 AI 总结后保存" : template == 6 ? "读取首个匹配文件并由真实 AI 生成工作报告" : "创建任务并发送应用内通知";
            wf.Nodes.Clear();
            wf.Nodes.Add(Node("trigger", wf.Trigger == "interval" ? "schedule" : "manual"));
            if (template == 7)
            {
                var task = Node("action", "task");
                task.Params["title"] = wf.Name;
                task.Params["priority"] = "normal";
                wf.Nodes.Add(task);
                var notify = Node("action", "notify");
                notify.Params["text"] = "已创建任务：" + wf.Name;
                wf.Nodes.Add(notify);
                return;
            }
            var search = Node("action", "search");
            search.Params["root"] = _root.Text.Trim();
            search.Params["pattern"] = string.IsNullOrWhiteSpace(_pattern.Text) ? "*" : _pattern.Text.Trim();
            wf.Nodes.Add(search);
            var read = Node("action", "read");
            read.Params["input"] = "{{prev}}";
            wf.Nodes.Add(read);
            var ai = Node("ai", template == 5 ? "summarize" : "generate");
            ai.Params["input"] = "{{prev}}";
            ai.Params["prompt"] = template == 5 ? "请总结以下内容，保留关键事实和待办事项。" : "请根据以下内容生成结构化工作报告，包含摘要、进展、风险和下一步。";
            ai.TimeoutMs = 120000;
            wf.Nodes.Add(ai);
            var target = _target.Text.Trim();
            if (Directory.Exists(target)) target = Path.Combine(target, SanitizeFileName(wf.Name) + "-{{date}}.md");
            var write = Node("action", template == 5 ? "write" : "report");
            write.Params["path"] = target;
            write.Params["input"] = "{{prev}}";
            wf.Nodes.Add(write);
            var done = Node("action", "notify");
            done.Params["text"] = "工作流已完成，结果文件：" + target;
            wf.Nodes.Add(done);
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "DeskBox报告" : name.Trim();
        }

        private string SelectedId()
        {
            var row = _wfList.SelectedRow;
            return row == null ? null : row.Tag as string;
        }

        private void Run(bool dry)
        {
            if (_engine == null) return;
            var id = SelectedId();
            if (id == null) { _status.Text = "先选中一个工作流"; return; }
            var wf = _engine.Workflows.FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            if (!dry)
            {
                var plan = wf.Nodes != null && wf.Nodes.Count > 0
                    ? "将按顺序执行 " + wf.Nodes.Count + " 个节点：\r\n" + string.Join(" → ", wf.Nodes.Select(n => n.Label).ToArray())
                    : "将按「" + (wf.Actions.Count > 0 ? wf.Actions[0].Type : "?") + "」处理目录 " +
                      (wf.Actions.Count > 0 ? wf.Actions[0].Get("root", "") : "") + " 中匹配 " +
                      (wf.Actions.Count > 0 ? wf.Actions[0].Get("pattern", "*") : "*") + " 的文件。";
                if (!Prompt.Confirm(this, "确认执行工作流", plan + "\r\n\r\n建议先「预演」确认影响范围。", Palette)) return;
            }
            _btnRun.Enabled = false; _btnDry.Enabled = false; _btnCancel.Enabled = true;
            _status.Text = dry ? "正在预演…" : "正在执行…";
            var thread = new Thread(delegate()
            {
                WorkflowRun run = null;
                try { run = wf.Nodes != null && wf.Nodes.Count > 0 ? _engine.RunV2(id, dry) : _engine.Run(id, dry); }
                catch (Exception ex) { run = new WorkflowRun { Ok = false, Message = ex.Message }; }
                SafeUi(delegate
                {
                    _btnRun.Enabled = true; _btnDry.Enabled = true; _btnCancel.Enabled = false;
                    _status.Text = run == null ? "执行失败" : run.Message;
                    Reload();
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void CopySelected()
        {
            var id = SelectedId();
            if (_engine == null || id == null) { _status.Text = "先选中一个工作流"; return; }
            var copy = _engine.Copy(id);
            _status.Text = copy == null ? "复制失败" : "已复制：" + copy.Name;
            Reload();
        }

        private void ToggleSelected()
        {
            var id = SelectedId();
            if (_engine == null || id == null) { _status.Text = "先选中一个工作流"; return; }
            var wf = _engine.Workflows.FirstOrDefault(w => w.Id == id);
            if (wf == null) return;
            wf.Enabled = !wf.Enabled;
            _engine.Save();
            _status.Text = wf.Enabled ? "已启用" : "已停用";
            Reload();
        }

        private void DeleteSelected()
        {
            if (_engine == null) return;
            var id = SelectedId();
            if (id == null) { _status.Text = "先选中一个工作流"; return; }
            if (!Prompt.Confirm(this, "删除工作流", "删除选中的工作流定义？历史运行记录保留。", Palette)) return;
            _engine.Remove(id);
            Reload();
        }

        protected override void OnActivated() { Reload(); }
        protected override void Dispose(bool disposing) { if (disposing && _engine != null && _runCompleted != null) _engine.RunCompleted -= _runCompleted; base.Dispose(disposing); }
    }
}
