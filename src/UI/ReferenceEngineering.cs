using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Fs;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildEngineeringDashboard()
        {
            var page = Page("工程驾驶舱", "今日工作、工程资料和交付状态");
            var toolbar = Toolbar();
            toolbar.Controls.Add(Button("项目中心", () => Navigate("projects"), true, 104));
            toolbar.Controls.Add(Button("工程启动台", () => Navigate("operations"), false, 124));
            toolbar.Controls.Add(Button("AI 工程助手", () => Navigate("ai"), false, 123));
            toolbar.Controls.Add(Button("通知", () => ShowLegacy("notifications"), false, 78));

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, Math.Max(138, 3 * FontOf(10).Height + 70)));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 48));

            var tasks = List(new[] { "任务", "截止", "优先级" }, new[] { 285, 125, 80 });
            var issues = List(new[] { "事件", "设备", "状态" }, new[] { 270, 140, 88 });
            var files = List(new[] { "工程文件", "修改时间" }, new[] { 310, 170 });
            var recent = List(new[] { "最近工作", "时间" }, new[] { 310, 170 });
            tasks.Name = "dashboard-tasks";
            issues.Name = "dashboard-issues";
            files.Name = "dashboard-files";
            recent.Name = "dashboard-activity";
            tasks.RowActivated += delegate { Navigate("tasks"); };
            issues.RowActivated += delegate { Navigate("incidents"); };
            files.RowActivated += delegate
            {
                var scope = _projects.Context;
                var path = files.SelectedRow == null ? null : files.SelectedRow.Tag as string;
                if (scope != null && scope.CanAccessPath(path)) OpenPath(path);
            };
            recent.RowActivated += delegate
            {
                var route = recent.SelectedRow == null ? null : recent.SelectedRow.Tag as string;
                if (!string.IsNullOrEmpty(route)) Navigate(route);
            };
            var progressText = TextLabel("", 10, false, _p.TextDim);
            var environmentText = TextLabel("", 10, false, _p.TextDim);
            progressText.Dock = environmentText.Dock = DockStyle.Fill;
            Label unused, taskHint, issueHint, filesHint, recentHint;
            grid.Controls.Add(OperationsPanel("工程进度", "", progressText, out unused), 0, 0);
            unused.Visible = false;
            grid.Controls.Add(OperationsPanel("工作建议与服务状态", "", environmentText, out unused), 1, 0);
            unused.Visible = false;
            grid.Controls.Add(OperationsPanel("今日任务", "", tasks, out taskHint), 0, 1);
            grid.Controls.Add(OperationsPanel("未关闭问题", "", issues, out issueHint), 1, 1);
            grid.Controls.Add(OperationsPanel("最近变更文件", "", files, out filesHint), 0, 2);
            grid.Controls.Add(OperationsPanel("最近工作", "", recent, out recentHint), 1, 2);
            foreach (Control panel in grid.Controls) panel.Margin = new Padding(0, 0, 10, 10);

            _reload["home"] = delegate
            {
                var scope = _projects.Context;
                var project = CurrentProject();
                var scopedTasks = _store.Tasks.Where(task => scope != null && scope.IncludesTask(task)).ToArray();
                var events = _sync.Incidents.Where(item => scope != null && item.ProjectId == scope.Id).ToArray();
                var open = scopedTasks.Where(task => !task.Done).ToArray();
                var pending = open.Where(task => TaskDate(task) <= DateTime.Today.AddDays(7))
                    .OrderBy(TaskDate).ThenByDescending(task => task.Priority == "high").ToArray();
                var openIssues = events.Where(item => item.Status != "closed").OrderByDescending(item => item.UpdatedAt).ToArray();
                var stepData = project == null ? null : project.ValidationSteps;
                bool hasProgress = project != null && ((stepData != null && stepData.Count > 0)
                    || (project.StageProgress != null && project.StageProgress.Any(stage => stage.Value > 0)));
                progressText.Text = scope == null ? "请选择项目，或在项目中心绑定工程目录。"
                    : "阶段 " + ValueOr(scope.Stage) + "   ·   版本 " + ValueOr(scope.Version)
                       + "\r\n加权进度 " + (hasProgress ? ValidationProgress.Display(project) : "待录入")
                       + "   ·   需求覆盖率待建立追踪矩阵"
                       + "\r\n当前任务 " + ValueOr(scopedTasks.Where(task => task.Id == scope.CurrentTaskId).Select(task => task.Title).FirstOrDefault());

                var index = FileIndex.Instance;
                var paths = ScopedFiles(scope, scope == null ? null : scope.Root, null, 8);
                var local = _providers == null ? null : _providers.DefaultProvider;
                var ai = local == null ? "本地检索" : scope != null && !scope.PermitsModel(local) ? "本地检索" : "模型已配置";
                string next = scope == null ? "选择工程项目" : !Directory.Exists(scope.Root) ? "检查工程目录"
                    : pending.Length > 0 ? "优先处理 " + pending[0].Title
                    : openIssues.Length > 0 ? "复核未关闭现场事件" : !hasProgress ? "录入验证进度" : "检查项目资料与交付项";
                environmentText.Text = next + "\r\nAI " + ai + "   ·   文件索引 " + index.State + "\r\n"
                    + (_sync.Running ? "局域网同步运行中" : "局域网同步未启动") + "   ·   "
                    + (_cloud.Connected ? "云同步在线" : "云同步未连接");
                taskHint.Text = scope == null ? "选择项目查看任务" : pending.Length == 0 ? "近 7 日无到期待办"
                    : pending.Count(task => TaskDate(task) < DateTime.Today) + " 项逾期 · "
                    + pending.Count(task => TaskDate(task) == DateTime.Today) + " 项今日到期 · 近 7 日共 " + pending.Length + " 项";
                issueHint.Text = scope == null ? "选择项目查看现场问题" : openIssues.Length == 0 ? "暂无未关闭问题" : openIssues.Length + " 项待跟进";
                filesHint.Text = scope == null ? "选择项目后查看文件"
                    : !Directory.Exists(scope.Root) ? "项目目录不可访问"
                    : index.IsIndexing ? "索引更新中" : paths.Length == 0 ? "索引暂无工程文件" : "双击或按 Enter 打开源文件";
                tasks.SetRows(pending.Take(10).Select(task => new ListRow
                {
                    Cells = new[] { task.Title, task.Due, task.Priority == "high" ? "高" : task.Priority == "low" ? "低" : "普通" }
                }).ToList());
                issues.SetRows(openIssues.Take(10).Select(item => new ListRow
                {
                    Cells = new[] { item.Description, item.Equipment, IncidentsView.StateText(item.Status) }
                }).ToList());
                files.SetRows(paths.Select(path => new ListRow { Cells = new[] { path.Name, TimeText(path.Mtime) }, Tag = path.Path }).ToList());
                var activity = scopedTasks.Where(task => task.Done && task.DoneAt > 0).Select(task => Tuple.Create(task.DoneAt, "完成任务 · " + task.Title, "tasks"))
                    .Concat(events.Where(item => item.UpdatedAt > 0).Select(item => Tuple.Create(item.UpdatedAt, "更新事件 · " + item.Description, "incidents")))
                    .Concat((stepData ?? new List<ValidationStep>()).Where(step => step.UpdatedAt > 0).Select(step => Tuple.Create(step.UpdatedAt, "更新验证 · " + step.Name, "validation")))
                    .Concat(_library.Recent.Where(item => scope != null && scope.IncludesPath(item.Path)).Take(8)
                        .Select(item => Tuple.Create(item.LastOpenAt, "打开文件 · " + Path.GetFileName(item.Path), "search")))
                    .OrderByDescending(item => item.Item1).Take(8).ToArray();
                recentHint.Text = scope == null ? "选择项目查看历史" : activity.Length == 0 ? "暂无工作记录" : "来自任务、事件、验证和文件操作";
                recent.SetRows(activity.Select(item => new ListRow { Cells = new[] { item.Item2, TimeText(item.Item1) }, Tag = item.Item3 }).ToList());
            };
            AddMain(page, grid, toolbar);
            return page;
        }

        private static DateTime TaskDate(TaskItem task)
        {
            DateTime value;
            return DateTime.TryParseExact(task.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out value) ? value : DateTime.MaxValue;
        }

        private static string ValueOr(string value) { return string.IsNullOrWhiteSpace(value) ? "未设置" : value; }

        private static FileEntry[] ScopedFiles(ProjectContext scope, string path, string[] extensions, int limit)
        {
            if (scope == null || !scope.CanAccessPath(path)) return new FileEntry[0];
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                if (extensions != null && !extensions.Contains(info.Extension, StringComparer.OrdinalIgnoreCase)) return new FileEntry[0];
                return new[] { new FileEntry { Path = info.FullName, Name = info.Name, Ext = info.Extension,
                    Size = info.Length, Mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds() } };
            }
            if (!Directory.Exists(path)) return new FileEntry[0];
            var query = new SearchQuery { Kind = "file", Limit = limit, SortBy = "mtime", SortDesc = true, PathFilter = scope.IncludesPath };
            query.Roots.Add(path);
            if (extensions != null) query.Exts.AddRange(extensions);
            return FileIndex.Instance.Search(query).Hits.Select(hit => hit.Entry)
                .Where(file => scope.CanAccessPath(file.Path) && File.Exists(file.Path)).ToArray();
        }

        private Control BuildProjectContext()
        {
            var page = Page("工程上下文", "为当前项目绑定资料目录、版本与工程权限");
            var toolbar = Toolbar();
            var edit = Button("编辑项目上下文", EditProjectContext, true, 170);
            toolbar.Controls.Add(edit);
            var data = List(new[] { "项目属性", "当前值" }, new[] { 190, 710 });
            data.Name = "project-context-properties";
            _reload["project-context"] = delegate
            {
                var scope = _projects.Context;
                edit.Enabled = scope != null;
                if (scope == null)
                {
                    data.SetRows(new List<ListRow> { new ListRow { Cells = new[] { "项目", "请在项目中心创建或选择工程" } } });
                    return;
                }
                var task = _store.Tasks.FirstOrDefault(item => item.Id == scope.CurrentTaskId && scope.IncludesTask(item));
                var rows = new[]
                {
                    new[] { "项目 ID", scope.Id }, new[] { "项目名称", scope.Name },
                    new[] { "客户", scope.Customer }, new[] { "设备", scope.Equipment },
                    new[] { "系统", string.IsNullOrWhiteSpace(scope.System) ? scope.Kind : scope.System },
                    new[] { "阶段", scope.Stage }, new[] { "工程版本", scope.Version },
                    new[] { "当前任务", task == null ? "" : task.Title }, new[] { "工程根目录", scope.Root },
                    new[] { "PLC 工程与导出资料", scope.PlcPath }, new[] { "DCS 导出资料", scope.DcsPath },
                    new[] { "验证资料", scope.ValidationPath },
                    new[] { "外部 AI", scope.AllowExternalAi ? "允许按设置使用外部模型" : "仅允许本机模型" }
                };
                data.SetRows(rows.Select(row => new ListRow { Cells = new[] { row[0], ValueOr(row[1]) }, Tip = row[1] }).ToList());
            };
            AddMain(page, data, toolbar);
            return page;
        }

        private void EditProjectContext()
        {
            var scope = _projects.Context;
            if (scope == null) return;
            using (var dialog = new Form { Text = "工程上下文 · " + scope.Name, StartPosition = FormStartPosition.CenterParent,
                BackColor = _p.Bg, ForeColor = _p.Text, Size = new Size(745, 465), MinimumSize = new Size(545, 390), AutoScaleMode = AutoScaleMode.Dpi })
            {
                var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(14) };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                string[] labels = { "工程阶段", "工程版本", "PLC 资料路径", "DCS 资料路径", "验证资料目录" };
                string[] values = { scope.Stage, scope.Version, scope.PlcPath, scope.DcsPath, scope.ValidationPath };
                var inputs = new TextBox[labels.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
                    var label = TextLabel(labels[i]);
                    label.Dock = DockStyle.Fill;
                    table.Controls.Add(label, 0, i);
                    inputs[i] = new TextBox { Text = values[i], AccessibleName = labels[i], Dock = DockStyle.Fill,
                        ForeColor = _p.Text, BackColor = _p.PanelAlt, Font = FontOf(10) };
                    table.Controls.Add(inputs[i], 1, i);
                }
                var tasks = _store.Tasks.Where(scope.IncludesTask).ToArray();
                var task = new DarkSelect(new[] { "未设置" }.Concat(tasks.Select(item => item.Title)).ToArray())
                    { Dock = DockStyle.Fill, Palette = _p, AccessibleName = "当前任务" };
                task.SelectedIndex = Array.FindIndex(tasks, item => item.Id == scope.CurrentTaskId) + 1;
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
                table.Controls.Add(TextLabel("当前任务"), 0, labels.Length);
                table.Controls.Add(task, 1, labels.Length);
                var external = new CheckBox { Text = "允许向已配置的外部 AI 发送本项目对话和获授权资料",
                    Checked = scope.AllowExternalAi, AutoSize = true };
                table.Controls.Add(external, 1, labels.Length + 1);
                var status = TextLabel("资料路径需在项目根目录内。文件读取仍受 AI 文件权限控制。", 9, false, _p.TextDim);
                status.Dock = DockStyle.Bottom;
                status.Height = 38;
                var footer = Toolbar();
                footer.Dock = DockStyle.Bottom;
                var save = Button("保存", delegate
                {
                    try
                    {
                        _projects.SaveContext(scope.Id, inputs[0].Text, inputs[2].Text, inputs[3].Text, inputs[4].Text,
                            inputs[1].Text, task.SelectedIndex <= 0 ? "" : tasks[task.SelectedIndex - 1].Id, external.Checked);
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception error) { status.Text = error.Message; }
                }, true, 95);
                footer.Controls.Add(save);
                footer.Controls.Add(new Button { Text = "取消", Width = 95, Height = 34, DialogResult = DialogResult.Cancel });
                dialog.Controls.Add(table);
                dialog.Controls.Add(footer);
                dialog.Controls.Add(status);
                dialog.ShowDialog(this);
            }
        }

        private Control BuildValidationCenter()
        {
            var page = Page("验证中心", "阶段进度与项目验证资料");
            var flow = new ValidationFlowControl(_p) { Dock = DockStyle.Fill };
            var toolbar = Toolbar();
            var edit = Button("录入验证进度", delegate
            {
                var project = CurrentProject();
                if (project != null) using (var window = new ProjectProgressForm(_projects, project, _p)) window.ShowDialog(this);
            }, true, 142);
            var folder = Button("打开验证资料", delegate
            {
                var scope = _projects.Context;
                if (scope == null || !scope.CanAccessPath(scope.ValidationPath))
                    throw new InvalidOperationException("请先在工程上下文绑定验证目录。");
                OpenPath(scope.ValidationPath);
            }, false, 142);
            toolbar.Controls.Add(edit);
            toolbar.Controls.Add(folder);
            toolbar.Controls.Add(Button("验证模板", () => ShowLegacy("engineering", control =>
            {
                var toolkit = control as EngineeringToolkitView;
                if (toolkit != null) toolkit.SelectMode(3);
            }), false, 122));
            toolbar.Controls.Add(Button("配置资料", () => Navigate("project-context"), false, 112));
            var detail = TextLabel("", 10, false, _p.TextDim);
            detail.Dock = DockStyle.Top;
            detail.Height = 35;
            var body = new Panel { Dock = DockStyle.Fill };
            body.Controls.Add(flow);
            body.Controls.Add(detail);
            _reload["validation"] = delegate
            {
                var project = CurrentProject();
                edit.Enabled = folder.Enabled = project != null;
                detail.Text = project == null ? "选择工程项目查看验证阶段。"
                    : project.Name + " · " + (project.ValidationSteps == null ? "验证进度待录入" : "阶段加权进度 " + ValidationProgress.Display(project));
                flow.Visible = project != null && project.ValidationSteps != null;
                flow.SetProject(project);
            };
            AddMain(page, body, toolbar);
            return page;
        }

        private Control BuildDcsStudio()
        {
            var page = Page("DCS Studio", "DeltaV 项目导出资料 · 本地只读预览");
            var toolbar = Toolbar();
            toolbar.Controls.Add(Button("设置资料目录", () => Navigate("project-context"), true, 142));
            toolbar.Controls.Add(Button("启动 DeltaV", () => LaunchEngineeringTool("DeltaV"), false, 130));
            toolbar.Controls.Add(Button("刷新索引", () => FileIndex.Instance.Rebuild(), false, 114));
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61));
            var documentList = List(new[] { "导出资料", "修改时间" }, new[] { 240, 160 });
            documentList.Name = "dcs-project-files";
            var preview = new RichTextBox { Dock = DockStyle.Fill, BackColor = _p.CardBg, ForeColor = _p.Text,
                ReadOnly = true, DetectUrls = false, WordWrap = false, BorderStyle = BorderStyle.None, Font = FontOf(10), Name = "dcs-export-preview" };
            Label listHint, previewHint;
            grid.Controls.Add(OperationsPanel("项目导出文件", "", documentList, out listHint), 0, 0);
            grid.Controls.Add(OperationsPanel("原文预览", "", preview, out previewHint), 1, 0);
            int revision = 0;
            string displayedPath = null;
            string previousProject = null;
            FileEntry[] displayedFiles = new FileEntry[0];
            documentList.RowActivated += async delegate
            {
                var row = documentList.SelectedRow;
                var scope = _projects.Context;
                var path = row == null ? null : row.Tag as string;
                if (scope == null || !scope.CanAccessPath(path)) return;
                int current = ++revision;
                displayedPath = path;
                previewHint.Text = Path.GetFileName(path);
                preview.Clear();
                try
                {
                    string text = await Task.Run(() => ReadDcsText(path));
                    if (current == revision && !preview.IsDisposed && scope.SameScope(_projects.Context)) preview.Text = text;
                }
                catch (Exception error)
                {
                    if (current == revision && !preview.IsDisposed) preview.Text = "读取失败：" + error.Message;
                }
            };
            _reload["dcs"] = delegate
            {
                var scope = _projects.Context;
                var root = scope == null ? null : string.IsNullOrEmpty(scope.DcsPath) ? scope.Root : scope.DcsPath;
                var entries = ScopedFiles(scope, root, new[] { ".fhx", ".csv", ".txt", ".log", ".xml", ".dat", ".ufl" }, 250);
                string projectId = scope == null ? "" : scope.Id;
                var fileBefore = displayedFiles.FirstOrDefault(file => file.Path == displayedPath);
                var fileNow = entries.FirstOrDefault(file => file.Path == displayedPath);
                if (previousProject != projectId || displayedPath == null || fileNow == null || fileBefore == null || fileNow.Mtime != fileBefore.Mtime)
                {
                    revision++;
                    preview.Clear();
                    displayedPath = null;
                    previewHint.Text = "选择导出文件并按 Enter 查看前 64 Ki 字符";
                }
                bool needsRows = previousProject != projectId ||
                    !entries.Select(file => Tuple.Create(file.Path, file.Mtime)).SequenceEqual(
                        displayedFiles.Select(file => Tuple.Create(file.Path, file.Mtime)));
                previousProject = projectId;
                displayedFiles = entries;
                listHint.Text = scope == null ? "请选择工程项目" : entries.Length == 0
                    ? "没有可读取的导出文件 · 核对资料目录与索引" : entries.Length + " 个文件 · 双击或按 Enter 预览";
                if (needsRows)
                {
                    documentList.SetRows(entries.Select(file => new ListRow
                        { Cells = new[] { file.Name, TimeText(file.Mtime) }, Tag = file.Path, Tip = file.Path }).ToList());
                    if (displayedPath != null) documentList.SelectIndex(Array.FindIndex(entries, file => file.Path == displayedPath));
                }
            };
            AddMain(page, grid, toolbar);
            return page;
        }

        private static string ReadDcsText(string path)
        {
            const int limit = 64 * 1024;
            using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
            {
                var buffer = new char[limit];
                int count = reader.ReadBlock(buffer, 0, buffer.Length);
                if (Array.IndexOf(buffer, '\0', 0, count) >= 0) throw new InvalidDataException("文件包含二进制数据，请导出为带 BOM 的 Unicode 文本或 UTF-8。");
                return new string(buffer, 0, count) + (reader.Peek() < 0 ? "" : "\r\n\r\n仅显示文件开头 64 Ki 字符。");
            }
        }
    }
}
