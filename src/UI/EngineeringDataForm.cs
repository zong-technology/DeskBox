using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.AppServices;
using DeskBox.Domain;
using DeskBox.Engineering;

namespace DeskBox.UI
{
    internal sealed class EngineeringDataForm : Form
    {
        private readonly ProjectContext project;
        private readonly Func<ProjectContext> active;
        private readonly TagCenter tags = new TagCenter();
        private readonly TabControl tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(6), UseMnemonic = false };
        private readonly Button stop = new Button { Text = "取消当前任务", AutoSize = true, Enabled = false, Name = "engineering-data-cancel" };
        private readonly ListView tagList = Grid("工程 Tag", "系统", "位号", "设备", "数据类型", "地址", "单位", "版本");
        private readonly ListView records = Grid("工程资料", "对象/状态", "名称", "父对象/系统", "原文位置");
        private readonly TextBox tagDetails = Detail("Tag 来源和版本"), documentDetails = Detail("资料原文和报告");
        private readonly TextBox filter = Input("Tag 查询", 140), systemFilter = Input("系统筛选", 110);
        private readonly TextBox documentFilter = Input("资料对象过滤", 134), sheet = Input("XLSX 工作表（可空）", 110);
        private readonly TextBox system = Input("默认系统 ID", 100), version = Input("资料版本", 93);
        private readonly ComboBox encoding = new ComboBox { Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "文本编码" };
        private readonly Label location = new Label { Dock = DockStyle.Top, Height = 22, AutoEllipsis = true, UseMnemonic = false };
        private readonly Button commitLink, commitReplace, saveReport, restore;
        private readonly System.Windows.Forms.Timer scopeTimer = new System.Windows.Forms.Timer { Interval = 180 };
        private CancellationTokenSource work;
        private EngineeringDocument document;
        private EngineeringDiff report;
        private TagImportPreview preview;
        private string path, sort = "name";
        private bool descending;
        private int offset;
        private const int PageSize = 100;

        internal static void Open(IWin32Window owner, Func<ProjectContext> context, string firstPath = null, string category = null)
        {
            var scope = context();
            if (scope == null)
            {
                MessageBox.Show(owner, "请先选择工程项目。", "统一工程数据", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var form = new EngineeringDataForm(scope, context, firstPath, category)) form.ShowDialog(owner);
        }

        internal EngineeringDataForm(ProjectContext scope, Func<ProjectContext> current, string initial, string category)
        {
            project = scope;
            active = current;
            Name = "engineering-data-dialog";
            Text = (category == null ? "统一工程 Tag" : category + " · 只读资料") + " · " + scope.Name;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(960, 630);
            MinimumSize = new Size(560, 435);
            if (Screen.PrimaryScreen != null && Screen.PrimaryScreen.WorkingArea.Height < 704)
                ClientSize = new Size(Math.Min(960, Screen.PrimaryScreen.WorkingArea.Width - 30), Screen.PrimaryScreen.WorkingArea.Height - 84);

            var global = Bar();
            global.Controls.Add(stop);
            stop.Click += delegate { if (work != null) work.Cancel(); };
            Controls.Add(tabs);
            Controls.Add(status);
            Controls.Add(global);

            var ledger = new TabPage("Tag 台账");
            var tagActions = Bar();
            tagActions.Controls.AddRange(new Control[] { Caption("查询"), filter, Caption("系统"), systemFilter });
            ActionButton(tagActions, "查找", () => RefreshTags(0));
            ActionButton(tagActions, "上一页", () => { if (offset > 0) RefreshTags(Math.Max(0, offset - PageSize)); });
            ActionButton(tagActions, "下一页", () => { if (tagList.Items.Count == PageSize) RefreshTags(offset + PageSize); });
            ActionButton(tagActions, "新增…", () => EditTag(null));
            ActionButton(tagActions, "编辑…", () => { if (SelectedTag != null) EditTag(SelectedTag); });
            ActionButton(tagActions, "关联仪表…", () =>
            {
                if (SelectedTag != null) InstrumentRegisterForm.Open(this, active, SelectedTag.TagId);
            });
            ActionButton(tagActions, "版本比较", ShowRevisions);
            ActionButton(tagActions, "来源定位", ShowTagSource);
            ActionButton(tagActions, "导出 CSV/XLSX", ExportTags);
            restore = ActionButton(tagActions, "恢复台账", RestoreTags);
            restore.Enabled = false;
            tagList.Name = "engineering-tags-list";
            tagList.ColumnClick += delegate(object sender, ColumnClickEventArgs args)
            {
                var next = args.Column == 0 ? "system" : args.Column == 2 ? "equipment" : "name";
                descending = sort == next && !descending;
                sort = next;
                RefreshTags(0);
            };
            tagList.SelectedIndexChanged += delegate { ShowTag(); };
            Fill(ledger, tagList, tagDetails, tagActions);
            tabs.TabPages.Add(ledger);

            var files = new TabPage("工程资料与差异");
            var fileActions = Bar();
            var select = ActionButton(fileActions, "打开资料…", Browse);
            select.Name = "engineering-data-open-file";
            ActionButton(fileActions, "加载／重读", ReadDocument);
            ActionButton(fileActions, "与另一版本比较…", Compare);
            ActionButton(fileActions, "定位原文", ShowExcerpt);
            saveReport = ActionButton(fileActions, "导出差异 JSON", ExportReport);
            saveReport.Enabled = false;
            var inputs = Bar();
            encoding.Items.AddRange(new object[] { "auto", "utf-8", "utf-16", "utf-16BE", "gb18030", "windows-1252" });
            encoding.SelectedIndex = 0;
            system.Text = category == "PLC Studio" ? "PLC" : category == "DCS Studio" ? "DeltaV" : "";
            version.Text = scope.Version;
            inputs.Controls.AddRange(new Control[] { Caption("工作表"), sheet, Caption("编码"), encoding, Caption("默认系统"), system, Caption("来源版本"), version });
            var imports = Bar();
            ActionButton(imports, "映射并预览 Tag…", Preview);
            commitLink = ActionButton(imports, "导入／补充来源", () => Commit(false));
            commitReplace = ActionButton(imports, "确认更新映射字段", () => Commit(true));
            commitLink.Enabled = commitReplace.Enabled = false;
            imports.Controls.Add(Caption("对象搜索"));
            imports.Controls.Add(documentFilter);
            documentFilter.TextChanged += delegate { ShowRecords(); };
            records.Name = "engineering-document-list";
            records.SelectedIndexChanged += delegate { ShowRecord(); };
            records.DoubleClick += delegate { ShowExcerpt(); };
            Fill(files, records, documentDetails, imports, inputs, location, fileActions);
            tabs.TabPages.Add(files);

            path = initial;
            if (category != null) tabs.SelectedIndex = 1;
            location.Text = path ?? "请选择当前工程范围内的资料文件。";
            scopeTimer.Tick += delegate { if (!project.SameScope(active())) { if (work != null) work.Cancel(); Close(); } };
            Shown += delegate
            {
                scopeTimer.Start();
                if (tabs.SelectedIndex == 0) RefreshTags(0);
                else if (!string.IsNullOrEmpty(path)) ReadDocument();
            };
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedIndex == 0 && work == null) RefreshTags(0); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            scopeTimer.Stop();
            scopeTimer.Dispose();
            if (work != null) work.Cancel();
            base.OnFormClosed(e);
        }

        private EngineeringTag SelectedTag { get { return tagList.SelectedItems.Count == 0 ? null : tagList.SelectedItems[0].Tag as EngineeringTag; } }
        private EngineeringRecord SelectedRecord { get { return records.SelectedItems.Count == 0 ? null : records.SelectedItems[0].Tag as EngineeringRecord; } }

        private async void Run<T>(string activity, Func<CancellationToken, T> action, Action<T> show)
        {
            if (work != null) { status.Text = "操作正在运行，可以先取消。"; return; }
            if (!project.SameScope(active())) return;
            var operation = new CancellationTokenSource();
            work = operation;
            status.Text = activity + "…";
            stop.Enabled = true;
            try
            {
                var data = await Task.Run(() => action(operation.Token), operation.Token);
                if (!IsDisposed && project.SameScope(active()))
                {
                    if (operation.IsCancellationRequested) status.Text = "操作已取消。";
                    else
                    {
                        status.Text = activity + "完成。";
                        work = null;
                        show(data);
                    }
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "操作已取消。"; }
            catch (Exception error) { if (!IsDisposed) status.Text = "操作失败 · " + error.Message; }
            finally
            {
                if (ReferenceEquals(work, operation)) work = null;
                if (!IsDisposed) stop.Enabled = work != null;
                operation.Dispose();
            }
        }

        private void RefreshTags(int start, string note = null)
        {
            string q = filter.Text, scope = systemFilter.Text, column = sort;
            bool reverse = descending;
            Run("读取 Tag 台账", t => tags.Query(project, q, scope, column, reverse, start, PageSize, t), page =>
            {
                offset = start;
                restore.Enabled = page.Recovered;
                Rows(tagList, page.Items.Select(tag => Entry(tag, tag.SystemId, tag.TagName, tag.EquipmentId, tag.DataType, tag.Address,
                    tag.EngineeringUnit, tag.Revision.ToString(CultureInfo.InvariantCulture))));
                tagDetails.Clear();
                status.Text = (note == null ? "" : note + " · ") + "共 " + page.Total + " 条 · 本页 " + page.Items.Count +
                    (page.Recovered ? " · 主文件损坏，已读取备份。恢复前禁止写入。" : "");
            });
        }

        private void ShowTag()
        {
            var item = SelectedTag;
            if (item == null) return;
            tagDetails.Text = "TagId " + item.TagId + " · 版本 " + item.Revision + "\r\n"
                + string.Join("\r\n", TagCenter.Fields.Select(field => field + "  " + TagCenter.Value(item, field))) + "\r\n\r\n"
                + (item.Sources.Count == 0 ? "手工记录 · 无来源文件。" : string.Join("\r\n", item.Sources.Select(link =>
                    link.SourceType + " · " + link.SourceDocument + " · " + link.SourceLocation + " · " + link.SourceVersion + "\r\nSHA-256 " + link.Sha256)))
                + (item.Relations.Count == 0 ? "" : "\r\n\r\n" + string.Join("\r\n", item.Relations.Select(link => link.Kind + " · " + link.EntityId)));
        }

        private void EditTag(EngineeringTag original)
        {
            if (work != null) return;
            using (var edit = new EngineeringFieldsForm(original == null ? "新增 Tag" : "编辑 Tag", TagCenter.Fields.ToArray(),
                TagCenter.Fields.Select(field => original == null ? "" : TagCenter.Value(original, field)).ToArray()))
            {
                if (edit.ShowDialog(this) != DialogResult.OK) return;
                var next = new EngineeringTag { TagId = original == null ? "" : original.TagId, ProjectId = project.Id };
                try { for (int i = 0; i < TagCenter.Fields.Count; i++) TagCenter.Set(next, TagCenter.Fields[i], edit.Values[i]); }
                catch (Exception error) { status.Text = "无效字段 · " + error.Message; return; }
                Run("保存 Tag", t => tags.Save(project, next, original == null ? 0 : original.Revision, t), item => RefreshTags(offset));
            }
        }

        private void ShowTagSource()
        {
            var item = SelectedTag;
            if (item == null || item.Sources.Count == 0 || work != null) return;
            var options = item.Sources.Select(link => link.SourceDocument + " · " + link.SourceLocation + " · " + link.SourceVersion).ToArray();
            using (var select = new EngineeringFieldsForm("选择来源", new[] { "文件及位置" }, new[] { options.Last() }, new[] { options }))
            {
                if (select.ShowDialog(this) != DialogResult.OK) return;
                var link = item.Sources[Array.LastIndexOf(options, select.Values[0])];
                Run("定位关联文件", t => EngineeringDocuments.SourceLocation(project, link, t), value => tagDetails.Text = value);
            }
        }

        private void ShowRevisions()
        {
            var item = SelectedTag;
            if (item == null) return;
            Run("读取版本", t => tags.History(project, item.TagId, t), history =>
            {
                if (history.Count < 2) { status.Text = "只有一个已保存版本。"; return; }
                string[] versions = history.Select(tag => tag.Revision.ToString(CultureInfo.InvariantCulture)).ToArray();
                using (var select = new EngineeringFieldsForm("比较 Tag 版本", new[] { "起点版本", "目标版本" },
                    new[] { versions.Last(), versions[0] }, new[] { versions, versions }))
                {
                    if (select.ShowDialog(this) != DialogResult.OK) return;
                    int old = int.Parse(select.Values[0]), next = int.Parse(select.Values[1]);
                    Run("比较 Tag 版本", t => tags.CompareRevisions(project, item.TagId, old, next, t), diff =>
                    {
                        tabs.SelectedIndex = 1;
                        DisplayReport(diff);
                    });
                }
            });
        }

        private void ExportTags()
        {
            var file = SavePath("Excel 工作簿|*.xlsx|CSV 文件|*.csv", "工程Tag.xlsx");
            if (file != null) Run("导出 Tag", t => { tags.Export(project, file, t); return file; }, value => status.Text = "已导出 · " + value);
        }

        private void RestoreTags()
        {
            if (MessageBox.Show(this, "恢复将启用上一份可读取台账，损坏文件留待检查。确认恢复？", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Run("恢复 Tag 台账", t => { tags.Restore(project, t); return true; }, value => RefreshTags(0));
        }

        private void Browse()
        {
            if (work != null) return;
            string file = Choose("选择工程导出资料");
            if (file == null) return;
            path = file;
            ReadDocument();
        }

        private void ReadDocument()
        {
            if (string.IsNullOrEmpty(path) || work != null) return;
            string file = path, rev = version.Text, textEncoding = encoding.Text, worksheet = sheet.Text;
            document = null;
            ResetPreview();
            ResetReport();
            Rows(records, Enumerable.Empty<ListViewItem>());
            documentDetails.Clear();
            location.Text = file;
            Run("读取资料与来源", t => EngineeringDocuments.Read(project, file, rev, t, textEncoding, worksheet), data =>
            {
                document = data;
                location.Text = data.Source.Document + " · " + data.Source.Sha256;
                ShowRecords();
                documentDetails.Text = "对象 " + data.Records.Count + " · 未识别 " + data.Records.Count(row => !row.Recognized) + "\r\n"
                    + "编码 " + data.Source.Encoding + " · 版本 " + data.Source.Version + "\r\n" + Issues(data.Issues);
            });
        }

        private void ShowRecords()
        {
            if (document == null || preview != null || report != null) return;
            string query = documentFilter.Text.Trim();
            var matches = document.Records.Where(row => query.Length == 0 ||
                (row.Key + " " + row.Kind + " " + row.Name + " " + row.Parent).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Rows(records, matches.Take(400).Select(row => Entry(row, row.Recognized ? row.Kind : row.Kind + "（未识别）",
                row.Name, row.Parent, row.Location)));
            status.Text = "符合 " + matches.Count + " 项 · 展示前 " + Math.Min(400, matches.Count) + " 项";
        }

        private void ShowRecord()
        {
            var row = SelectedRecord;
            if (row == null) return;
            documentDetails.Text = row.Kind + " · " + row.Name + " · " + row.Location + "\r\n" +
                string.Join("\r\n", row.Fields.Select(field => field.Key + " = " + field.Value)) +
                (document == null || document.Issues.Count == 0 ? "" : "\r\n\r\n解析诊断\r\n" + Issues(document.Issues));
        }

        private void ShowExcerpt()
        {
            var row = SelectedRecord;
            if (document == null || row == null || row.StartLine <= 0) return;
            var source = document.Source;
            if (source.Type == "xlsx") { status.Text = "Excel 定位 · " + source.Document + " · " + row.Location; return; }
            Run("定位原始记录", t => EngineeringDocuments.Excerpt(project, source, row.StartLine,
                Math.Max(12, row.EndLine - row.StartLine + 1), t), value => documentDetails.Text = value);
        }

        private void Compare()
        {
            if (string.IsNullOrEmpty(path) || work != null) { status.Text = "请先选择基线文件。"; return; }
            var other = Choose("选择比较目标版本");
            if (other == null) return;
            var left = path;
            var rev = version.Text;
            var charSet = encoding.Text;
            var worksheet = sheet.Text;
            ResetPreview();
            ResetReport();
            Run("确定性比较", t => EngineeringDocuments.Compare(project, left, other, t, worksheet, charSet, rev), DisplayReport);
        }

        private void DisplayReport(EngineeringDiff data)
        {
            report = data;
            saveReport.Enabled = true;
            Rows(records, report.Changes.Take(400).Select(row => Entry(row, row.State, row.Key, row.Field, row.BeforeLocation + " → " + row.AfterLocation)));
            documentDetails.Text = EngineeringDocuments.Summarize(data, 100);
            status.Text = "新建 " + data.Added + " · 删除 " + data.Removed + " · 修改 " + data.Modified +
                (data.Issues.Any(item => item.Blocking) ? " · 存在阻塞诊断，请查阅详情" : "");
        }

        private void ExportReport()
        {
            var data = report;
            if (data == null || work != null) return;
            string target = SavePath("JSON 差异报告|*.json", "工程差异.json");
            if (target != null) Run("写入差异报告", t => { EngineeringDocuments.ExportDiff(project, data, target, t); return target; },
                value => status.Text = "已保存 · " + value);
        }

        private void Preview()
        {
            if (string.IsNullOrEmpty(path) || work != null) { status.Text = "请先选择导入文件。"; return; }
            string file = path, worksheet = sheet.Text, charSet = encoding.Text, rev = version.Text, defaultSystem = system.Text;
            ResetPreview();
            ResetReport();
            if (!EngineeringDocuments.IsTable(Path.GetExtension(file).TrimStart('.').ToLowerInvariant()))
            {
                Prepare(file, worksheet, charSet, rev, defaultSystem, null);
                return;
            }
            Run("读取导入列", t => EngineeringDocuments.Headers(project, file, worksheet, charSet, t), columns =>
            {
                if (columns.Length == 0) { status.Text = "没有找到表头。"; return; }
                var fields = TagCenter.Fields.ToArray();
                var options = new[] { "不导入" }.Concat(columns.Select((item, index) => (index + 1) + " · " + item)).ToArray();
                var guessed = TagCenter.DefaultMapping(columns);
                var initial = fields.Select(field => guessed.ContainsKey(field) ? options[guessed[field] + 1] : options[0]).ToArray();
                using (var map = new EngineeringFieldsForm("确认字段映射", fields, initial, fields.Select(field => options).ToArray()))
                {
                    if (map.ShowDialog(this) != DialogResult.OK) return;
                    var values = map.Values;
                    var mapping = Enumerable.Range(0, fields.Length).Where(i => values[i] != options[0])
                        .ToDictionary(i => fields[i], i => Array.IndexOf(options, values[i]) - 1);
                    Prepare(file, worksheet, charSet, rev, defaultSystem, mapping);
                }
            });
        }

        private void Prepare(string file, string worksheet, string charset, string rev, string defaultSystem, IDictionary<string, int> map)
        {
            Run("生成导入预览", t => tags.Preview(project, file, rev, defaultSystem, t, map, worksheet, charset), plan =>
            {
                preview = plan;
                var usable = plan.Rows.Count > 0 && !plan.Issues.Any(issue => issue.Blocking);
                commitLink.Enabled = commitReplace.Enabled = usable;
                Rows(records, plan.Rows.Take(400).Select(row => Entry(row, row.State, row.Value.TagName, row.Value.SystemId, row.Value.SourceLocation)));
                documentDetails.Text = "来源 " + plan.Source.Document + " · " + plan.Source.Sha256 + "\r\n" +
                    "导入行 " + plan.Rows.Count + " · 冲突 " + plan.Rows.Count(row => row.State == "conflict") +
                    "\r\n「导入／补充来源」保留已有字段。「确认更新映射字段」按本次映射更新已存在的 Tag。\r\n\r\n" + Issues(plan.Issues);
                status.Text = usable ? "预览完成，等待确认写入。" : "预览存在阻塞错误，请查看诊断。";
            });
        }

        private void Commit(bool replace)
        {
            var plan = preview;
            if (plan == null || work != null) return;
            if (MessageBox.Show(this, replace ? "确认覆盖现有同系统、同名 Tag 的已映射字段？" : "确认导入新 Tag，并为已有 Tag 增加来源链接？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Run("原子写入 Tag 台账", t => tags.Commit(project, plan, replace, t), result =>
            {
                ResetPreview();
                documentDetails.Text = "新增 " + result.Added + " · 更新 " + result.Updated + " · 新来源 " + result.Linked + " · 未变更 " + result.Unchanged +
                    "\r\n源文件 " + result.Source.Document + " · " + result.Source.Sha256 +
                    "\r\nTag ID\r\n" + string.Join("\r\n", result.TagIds.Take(400));
            });
        }

        private void ResetPreview() { preview = null; commitLink.Enabled = commitReplace.Enabled = false; }
        private void ResetReport() { report = null; saveReport.Enabled = false; }

        private string Choose(string caption)
        {
            using (var dialog = new OpenFileDialog { Title = caption, InitialDirectory = project.Root, CheckFileExists = true,
                Filter = "工程导出|*.fhx;*.xml;*.aml;*.scl;*.db;*.udt;*.csv;*.tsv;*.xlsx;*.json|所有文件|*.*" })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private string SavePath(string filters, string filename)
        {
            if (work != null) return null;
            using (var dialog = new SaveFileDialog { Filter = filters, FileName = filename, InitialDirectory = project.Root, OverwritePrompt = false, AddExtension = true })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private static string Issues(IReadOnlyList<EngineeringIssue> items)
        {
            return string.Join("\r\n", items.Take(300).Select(issue => (issue.Blocking ? "错误" : "提示") + " · " +
                issue.Location + " · " + issue.Message)) + (items.Count > 300 ? "\r\n其余诊断共 " + (items.Count - 300) + " 条。" : "");
        }

        private static void Fill(Control page, Control upper, Control lower, params Control[] top)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(upper);
            split.Panel2.Controls.Add(lower);
            page.Controls.Add(split);
            foreach (var control in top) page.Controls.Add(control);
            page.VisibleChanged += delegate { if (page.Visible && split.Height > 180) split.SplitterDistance = split.Height / 2; };
        }

        private static ListView Grid(string title, params string[] fields)
        {
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, AccessibleName = title };
            foreach (string field in fields) list.Columns.Add(field, field == "名称" ? 238 : 129);
            return list;
        }

        private static void Rows(ListView list, IEnumerable<ListViewItem> items)
        {
            list.BeginUpdate();
            try { list.Items.Clear(); list.Items.AddRange(items.ToArray()); }
            finally { list.EndUpdate(); }
        }

        private static ListViewItem Entry(object value, params string[] cells) { return new ListViewItem(cells) { Tag = value }; }
        private static Label Caption(string label) { return new Label { Text = label, AutoSize = true, Margin = new Padding(6, 8, 2, 0) }; }
        private static FlowLayoutPanel Bar() { return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true }; }
        private static TextBox Input(string name, int width) { return new TextBox { Width = width, AccessibleName = name }; }
        private static TextBox Detail(string name)
        {
            return new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both, AccessibleName = name };
        }
        private static Button ActionButton(Control parent, string label, Action action)
        {
            var item = new Button { Text = label, AutoSize = true };
            item.Click += delegate { action(); };
            parent.Controls.Add(item);
            return item;
        }
    }

    internal sealed class EngineeringFieldsForm : Form
    {
        private readonly Control[] inputs;
        internal string[] Values { get { return inputs.Select(input => input.Text).ToArray(); } }

        internal EngineeringFieldsForm(string title, string[] labels, string[] values, string[][] choices = null)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, Math.Min(570, 70 + labels.Length * 29));
            MinimumSize = new Size(380, 205);
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(9), AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputs = new Control[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                table.Controls.Add(new Label { Text = labels[i], AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, 0, i);
                if (choices == null || choices[i] == null) inputs[i] = new TextBox { Text = values[i] };
                else
                {
                    var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    list.Items.AddRange(choices[i]);
                    list.SelectedItem = values[i];
                    inputs[i] = list;
                }
                inputs[i].AccessibleName = labels[i];
                inputs[i].Dock = DockStyle.Fill;
                table.Controls.Add(inputs[i], 1, i);
            }
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom };
            var ok = new Button { Text = "确认", AutoSize = true, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            actions.Controls.AddRange(new Control[] { ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(table);
            Controls.Add(actions);
        }
    }
}
