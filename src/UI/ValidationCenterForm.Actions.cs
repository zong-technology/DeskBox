using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using DeskBox.AppServices;
using DeskBox.Domain;

namespace DeskBox.UI
{
    internal sealed partial class ValidationCenterForm
    {
        private void ShowOverview()
        {
            var current = snapshot;
            Run("计算验证指标", token => ValidationTrace.Statistics(project, current, traceFilter, token, service), report =>
            {
                Display(new[] { "指标", "分子", "分母", "比例" }, report.Items.Select(item => Row(item, item.Label,
                    item.Numerator.ToString(), item.Denominator.HasValue ? item.Denominator.ToString() : "—",
                    item.Rate.HasValue ? item.Rate.Value.ToString("P1", CultureInfo.CurrentCulture) : "—")));
                details.Text = "历史执行次数 " + report.HistoricalExecutions + "。既有阶段规划进度独立保留。\r\n" +
                    string.Join("\r\n", report.Items.Select(item => item.Label + " · " + item.Definition));
            });
        }

        private void ShowTrace()
        {
            var data = snapshot;
            Run("查询 RTM", token => ValidationTrace.Query(project, data, traceFilter, token, service), trace =>
            {
                var term = search.Text.Trim();
                var rows = trace.Rows.Where(row => term.Length == 0 || (row.RequirementNumber + " " + row.TestCaseNumber + " " + row.RequirementText)
                    .IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                Display(new[] { "需求", "测试用例", "执行", "需求审核", "证据", "开放偏差" }, rows.Select(row => Row(row,
                    row.RequirementNumber, row.TestCaseNumber, StatusLabel(row.ExecutionStatus), StatusLabel(row.ReviewStatus), row.EvidenceIds.Count.ToString(),
                    row.OpenDeviationIds.Count.ToString())));
                details.Text = "未覆盖需求 " + trace.UncoveredRequirementIds.Count + " · 无来源测试 " + trace.OrphanTestCaseIds.Count
                    + " · 无可用证据执行 " + trace.MissingEvidenceExecutionIds.Count + " · 无效关联 " + trace.InvalidLinkIds.Count;
            });
        }

        private static string DescribeTrace(ValidationTraceRow row)
        {
            var text = new StringBuilder();
            text.AppendLine("需求 ID " + row.RequirementId + " · 版本 " + row.RequirementRevision).AppendLine(row.RequirementText);
            if (row.ObjectSource != null) text.AppendLine(row.ObjectSource.Document + " · " + row.ObjectSource.Location + " · " + row.ObjectSource.Sha256);
            text.AppendLine("设计引用 " + string.Join(", ", row.DesignIds)).AppendLine("关联需求 " + string.Join(", ", row.RelatedRequirementIds));
            return text.AppendLine("测试 ID " + row.TestCaseId + " · 版本 " + row.CaseRevision)
                .AppendLine("执行 " + row.ExecutionId + " · " + row.ExecutionStatus).AppendLine(row.ExecutionResult)
                .AppendLine("Tag " + string.Join(", ", row.TagIds)).AppendLine("风险 " + string.Join(", ", row.RiskIds))
                .AppendLine("证据 " + string.Join(", ", row.EvidenceIds)).AppendLine("缺失或损坏 " + string.Join(", ", row.UnavailableEvidenceIds))
                .AppendLine("偏差 " + string.Join(", ", row.DeviationIds)).ToString();
        }

        private static void AppendImpact(StringBuilder text, ChangeImpact impact)
        {
            if (impact.Before != null) text.AppendLine("前版 " + impact.Before.Document + " · " + impact.Before.Version + " · " + impact.Before.Sha256);
            if (impact.After != null) text.AppendLine("后版 " + impact.After.Document + " · " + impact.After.Version + " · " + impact.After.Sha256);
            text.AppendLine("评估 " + impact.Assessment).AppendLine("重新测试决定 " + impact.RetestDecision);
            foreach (var item in impact.Affected.Take(120)) text.AppendLine(item.Kind + " · " + item.Number + " · " + item.Id);
            foreach (var key in impact.UnresolvedChangeKeys.Take(80)) text.AppendLine("待确认身份 · " + key);
        }

        private void ConfigureTrace()
        {
            using (var form = new EntryForm(this, "设置 RTM 筛选"))
            {
                form.TextField("stage", "阶段", traceFilter.Stage);
                form.TextField("system", "系统 ID", traceFilter.SystemId);
                form.TextField("device", "设备 ID", traceFilter.EquipmentId);
                form.TextField("tag", "Tag ID", traceFilter.TagId);
                form.TextField("risk", "风险等级", traceFilter.RiskLevel);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                traceFilter.Stage = form.Value("stage");
                traceFilter.SystemId = form.Value("system");
                traceFilter.EquipmentId = form.Value("device");
                traceFilter.TagId = form.Value("tag");
                traceFilter.RiskLevel = form.Value("risk");
                ShowTrace();
            }
        }

        private void Import(bool test)
        {
            string file = ChooseFile("选择验证表格或 Word 资料", "验证资料|*.xlsx;*.csv;*.tsv;*.docx");
            if (file == null) return;
            var opts = new ValidationImportOptions { Kind = test ? "testcase" : "requirement" };
            using (var form = new EntryForm(this, "导入来源与版本"))
            {
                form.TextField("stage", "阶段", test ? "FAT" : "URS");
                form.TextField("doc", "逻辑文档编号", Path.GetFileNameWithoutExtension(file));
                form.TextField("version", "来源版本", project.Version ?? "");
                form.TextField("sheet", "工作表（可空）", "");
                form.TextField("encoding", "CSV 编码", "auto");
                form.TextField("word", "Word 表格序号（0 可辅助提取段落）", "0");
                if (test) form.Selection("protocol", "测试方案", RecordsOf(typeof(TestProtocol)), "", true);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                opts.Stage = form.Value("stage");
                opts.DocumentNumber = form.Value("doc");
                opts.DocumentVersion = form.Value("version");
                opts.Worksheet = form.Value("sheet");
                opts.Encoding = form.Value("encoding");
                int index;
                if (!int.TryParse(form.Value("word"), out index) || index < 0) { status.Text = "Word 表格序号无效。"; return; }
                opts.WordTable = index;
                if (test) opts.ProtocolId = form.Value("protocol");
            }
            Run("读取源文件列", token => ValidationImport.Headers(project, file, opts.Worksheet, token, opts.WordTable, opts.Encoding), headers =>
            {
                using (var form = new EntryForm(this, "明确选择字段映射"))
                {
                    var map = ValidationImport.DefaultMapping(opts.Kind, headers);
                    var picks = headers.Select((header, i) => new Pick(i.ToString(CultureInfo.InvariantCulture), i + 1 + " · " + header)).ToArray();
                    foreach (var field in ValidationImport.Fields(opts.Kind))
                        form.Selection(field, field, picks, map.ContainsKey(field) ? map[field].ToString(CultureInfo.InvariantCulture) : "", true);
                    if (form.ShowDialog(this) != DialogResult.OK) return;
                    opts.Mapping = ValidationImport.Fields(opts.Kind).Where(field => form.Value(field).Length > 0)
                        .ToDictionary(field => field, field => int.Parse(form.Value(field), CultureInfo.InvariantCulture));
                }
                var importer = new ValidationImport(service);
                Run("生成导入预览", token => importer.Preview(project, file, opts, token), preview => ConfirmImport(importer, preview));
            });
        }

        private void ConfirmImport(ValidationImport importer, ValidationImportPreview preview)
        {
            var info = new StringBuilder().AppendLine(preview.Source.Document + " · SHA-256 " + preview.Source.Sha256)
                .AppendLine("导入项 " + preview.Rows.Count + " · 诊断 " + preview.Issues.Count);
            foreach (var row in preview.Rows.Take(80))
                info.AppendLine(row.State + " · " + row.Number + " · " + row.Location + " · " + row.Description);
            if (preview.Rows.Count > 80) info.AppendLine("其余记录共 " + (preview.Rows.Count - 80) + " 条。");
            foreach (var issue in preview.Issues.Take(160))
                info.AppendLine((issue.Blocking ? "错误" : "提示") + " · " + issue.Location + " · " + issue.Message);
            using (var form = new EntryForm(this, "核对真实来源和导入记录"))
            {
                form.Note(info.ToString());
                if (preview.Issues.Any(issue => issue.Blocking)) { form.ShowDialog(this); return; }
                form.Choice("mode", "写入方式", new[] { "新增，保留原记录", "新增并修订", "新增、修订并作废本来源缺失需求" }, "新增，保留原记录", false);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string mode = form.Value("mode");
                bool revise = mode != "新增，保留原记录";
                bool retire = mode == "新增、修订并作废本来源缺失需求";
                if (!Confirm("确认已人工核对来源、字段和写入方式，保存本次导入？")) return;
                Run("提交导入", token => importer.Commit(project, preview, revise, token, retire), result =>
                    LoadSnapshot("新增 " + result.Added + " · 修订 " + result.Updated + " · 作废 " + result.Retired + " · 跳过 " + result.Skipped));
            }
        }

        private void Impact()
        {
            string before = ChooseFile("选择变更前工程资料", "工程资料|*.csv;*.tsv;*.xlsx;*.fhx;*.scl;*.xml;*.aml;*.db;*.udt;*.json");
            if (before == null) return;
            string after = ChooseFile("选择变更后工程资料", "工程资料|*.csv;*.tsv;*.xlsx;*.fhx;*.scl;*.xml;*.aml;*.db;*.udt;*.json");
            if (after == null) return;
            using (var form = new EntryForm(this, "记录待评估工程变更"))
            {
                form.TextField("title", "标题", Path.GetFileName(before) + " → " + Path.GetFileName(after));
                form.TextField("before", "前版", "");
                form.TextField("after", "后版", project.Version ?? "");
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string title = form.Value("title"), old = form.Value("before"), next = form.Value("after");
                Run("分析影响", token =>
                {
                    var diff = EngineeringDocuments.Compare(project, before, after, token, beforeVersion: old, afterVersion: next);
                    return ValidationTrace.RecordImpact(service, project, diff, title, ValidationService.CurrentActor, token);
                }, record => LoadSnapshot("已生成待评估变更 · " + record.Id));
            }
        }

        private void RecoverEvidence()
        {
            var evidence = SelectedRecord as Evidence;
            if (evidence == null) return;
            string source = ChooseFile("选择工程内的同一份原始证据", "所有文件|*.*");
            if (source == null || !Confirm("仅在大小和归档 SHA-256 相符时恢复。确认继续？")) return;
            Run("恢复原始证据", token => { service.RestoreEvidence(project, evidence.Id, source, token); return true; },
                result => LoadSnapshot("证据已按原始哈希恢复。"));
        }

        private void AssessImpact()
        {
            var impact = SelectedRecord as ChangeImpact;
            if (impact == null) return;
            using (var form = new EntryForm(this, "人工评估变更影响"))
            {
                form.TextField("reason", "评估依据", impact.Assessment, true);
                form.Choice("retest", "重新测试决定", new[] { "undecided", "required", "not-required" }, impact.RetestDecision, false);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var reason = form.Value("reason");
                var decision = form.Value("retest");
                Run("保存人工评估", token => service.AssessImpact(project, impact.Id, impact.Revision, reason, decision, token),
                    result => LoadSnapshot("影响评估已记录。"));
            }
        }

        private void CaptureBaseline()
        {
            using (var form = new EntryForm(this, "建立验证基线"))
            {
                form.TextField("title", "基线标题", "");
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string title = form.Value("title");
                Run("建立验证基线", token => ValidationTrace.CaptureBaseline(service, project, title, ValidationService.CurrentActor, token),
                    result => LoadSnapshot("基线已保存 · " + result.Id));
            }
        }

        private void CompareBaselines()
        {
            var list = snapshot.All<ValidationBaseline>().ToArray();
            if (list.Length < 2) { status.Text = "至少需要两份基线。"; return; }
            using (var form = new EntryForm(this, "比较验证基线"))
            {
                var options = list.Select(item => new Pick(item.Id, item.Title + " · " + FormatTime(item.CreatedAt))).ToArray();
                form.Selection("left", "前版", options, options.First().Id, false);
                form.Selection("right", "后版", options, options.Last().Id, false);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var left = list.Single(item => item.Id == form.Value("left"));
                var right = list.Single(item => item.Id == form.Value("right"));
                Run("比较基线", token => ValidationTrace.CompareBaselines(project, left, right, token), changes =>
                {
                    details.Text = changes.Count == 0 ? "未发现差异。" :
                        string.Join("\r\n", changes.Take(1000).Select(item => item.Kind + " · " + item.Id + " · " + item.Difference));
                });
            }
        }

        private void ExportReport(string preset = null)
        {
            using (var form = new EntryForm(this, "导出当前验证记录"))
            {
                var options = ValidationReports.Kinds.Select(item => new Pick(item.Key, item.Value)).ToArray();
                form.Selection("report", "报告类别", options, preset ?? "summary", false);
                form.TextField("stage", "阶段（可空）", "");
                form.TextField("template", "DOCX 企业模板（可空）", "", file: true);
                form.TextField("mapping", "模板映射（每行 占位名=源字段）", "", true);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string kind = form.Value("report"), stage = form.Value("stage"), template = form.Value("template");
                var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string rule in form.Value("mapping").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int delimiter = rule.IndexOf('=');
                    if (delimiter < 1 || delimiter == rule.Length - 1) { status.Text = "模板映射格式无效。"; return; }
                    mapping[rule.Substring(0, delimiter).Trim()] = rule.Substring(delimiter + 1).Trim();
                }
                using (var save = new SaveFileDialog { Filter = "XLSX 报表|*.xlsx|DOCX 报告|*.docx", FileName = "DeskBox-" + kind,
                    InitialDirectory = project.Root, AddExtension = true, OverwritePrompt = false, FilterIndex = template.Length == 0 ? 1 : 2 })
                {
                    if (save.ShowDialog(this) != DialogResult.OK) return;
                    string path = save.FileName;
                    Run("导出待审核草稿", token => { new ValidationReports(service).Export(project, kind, path, token, template, mapping, stage); return path; },
                        result => { status.Text = "已导出 · " + result; details.Text = result; });
                }
            }
        }

        private sealed class Pick
        {
            internal readonly string Id, Label;
            internal Pick(string id, string label) { Id = id; Label = label; }
            public override string ToString() { return Label; }
        }

        private sealed class EntryForm : Form
        {
            private readonly Dictionary<string, Control> fields = new Dictionary<string, Control>();
            private readonly TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            private readonly ValidationCenterForm owner;

            internal EntryForm(ValidationCenterForm owner, string title)
            {
                this.owner = owner;
                Text = title;
                StartPosition = FormStartPosition.CenterParent;
                var screen = Screen.FromControl(owner).WorkingArea;
                ClientSize = new Size(Math.Min(700, screen.Width - 35), Math.Min(550, screen.Height - 80));
                MinimumSize = new Size(420, 250);
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
                scroll.Controls.Add(table);
                var commands = Toolbar();
                commands.Dock = DockStyle.Bottom;
                var ok = new Button { Text = "确认", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
                commands.Controls.AddRange(new Control[] { ok, cancel });
                AcceptButton = ok;
                CancelButton = cancel;
                Controls.Add(scroll);
                Controls.Add(commands);
            }

            internal bool Has(string name) { return fields.ContainsKey(name); }
            internal string Value(string name)
            {
                Control input;
                if (!fields.TryGetValue(name, out input)) return "";
                var choice = input as ComboBox;
                var pick = choice == null ? null : choice.SelectedItem as Pick;
                return (pick == null ? input.Text : pick.Id).Trim();
            }

            internal void Note(string text)
            {
                var memo = new TextBox { Text = text, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                    Height = Math.Min(260, Math.Max(48, text.Split('\n').Length * 22)), Dock = DockStyle.Fill };
                int row = table.RowCount++;
                table.Controls.Add(memo, 0, row);
                table.SetColumnSpan(memo, 2);
            }

            internal void TextField(string name, string description, string value, bool multi = false, bool file = false)
            {
                var text = new TextBox { Text = value ?? "", Multiline = multi, MaxLength = 32000, Dock = DockStyle.Fill };
                if (multi) { text.Height = 68; text.ScrollBars = ScrollBars.Vertical; text.AcceptsReturn = true; }
                if (!file) { AddField(name, description, text, text); return; }
                var button = new Button { Text = "选择…", AutoSize = true, Dock = DockStyle.Right };
                button.Click += delegate { string path = owner.ChooseFile(description, "所有文件|*.*"); if (path != null) text.Text = path; };
                var container = new Panel { Height = 28, Dock = DockStyle.Fill };
                container.Controls.Add(text);
                container.Controls.Add(button);
                AddField(name, description, text, container);
            }

            internal void Choice(string name, string description, IEnumerable<string> values, string selected, bool editable, bool empty = false)
            {
                var items = values.Distinct().ToList();
                if (empty) items.Insert(0, "");
                var choice = new ComboBox { DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList };
                choice.Items.AddRange(items.Cast<object>().ToArray());
                if (editable) choice.Text = selected;
                else if (items.Contains(selected)) choice.SelectedItem = selected;
                else if (items.Count > 0) choice.SelectedIndex = 0;
                AddField(name, description, choice, choice);
            }

            internal void Selection(string name, string description, IEnumerable<Pick> values, string selected, bool empty)
            {
                var items = values.ToList();
                if (empty) items.Insert(0, new Pick("", "未选择"));
                var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                choice.Items.AddRange(items.Cast<object>().ToArray());
                if (items.Count > 0) choice.SelectedItem = items.FirstOrDefault(item => item.Id == selected) ?? items[0];
                AddField(name, description, choice, choice);
            }

            private void AddField(string name, string description, Control input, Control visible)
            {
                fields.Add(name, input);
                input.AccessibleName = description;
                visible.Dock = DockStyle.Fill;
                int row = table.RowCount++;
                table.Controls.Add(LabelFor(description), 0, row);
                table.Controls.Add(visible, 1, row);
            }
        }
    }
}
