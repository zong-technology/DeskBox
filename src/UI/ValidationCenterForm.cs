using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.AppServices;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Native;

namespace DeskBox.UI
{
    internal sealed partial class ValidationCenterForm : Form
    {
        private readonly ProjectContext project;
        private readonly Func<ProjectContext> activeProject;
        private readonly Func<IEnumerable<TaskItem>> tasks;
        private readonly Func<IEnumerable<DeskBox.Sync.IncidentDto>> incidents;
        private readonly ValidationService service = new ValidationService();
        private readonly ComboBox section = new ComboBox { Name = "validation-section", DropDownStyle = ComboBoxStyle.DropDownList, Width = 137, AccessibleName = "验证工作台分区" };
        private readonly TextBox search = new TextBox { Width = 155, AccessibleName = "筛选编号、标题或 ID" };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 27, Padding = new Padding(5, 4, 5, 0), AutoEllipsis = true, UseMnemonic = false };
        private readonly FlowLayoutPanel actions = Toolbar();
        private readonly DataGridView grid = new DataGridView
        {
            Name = "validation-records", Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoGenerateColumns = false, AllowUserToResizeRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        private readonly TextBox details = new TextBox
        {
            Name = "validation-details", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "记录明细"
        };
        private readonly Button cancel = new Button { Name = "validation-cancel", Text = "取消任务", AutoSize = true, Enabled = false };
        private readonly System.Windows.Forms.Timer contextWatch = new System.Windows.Forms.Timer { Interval = 250 };
        private readonly ValidationTraceFilter traceFilter = new ValidationTraceFilter();
        private ValidationSnapshot snapshot;
        private CancellationTokenSource work;

        private sealed class Section
        {
            internal readonly string Label;
            internal readonly Type RecordType;
            internal Section(string label, Type type = null) { Label = label; RecordType = type; }
            public override string ToString() { return Label; }
        }

        private static readonly Section[] Sections =
        {
            new Section("验证总览"),
            new Section("阶段与计划", typeof(ValidationPlan)), new Section("验证文件", typeof(ValidationDocument)),
            new Section("需求管理", typeof(ValidationRequirement)), new Section("设计引用", typeof(DesignReference)),
            new Section("风险评估", typeof(RiskAssessment)), new Section("测试方案", typeof(TestProtocol)),
            new Section("测试用例", typeof(TestCase)), new Section("测试步骤", typeof(TestStep)),
            new Section("测试执行记录", typeof(TestExecution)), new Section("证据库", typeof(Evidence)),
            new Section("偏差管理", typeof(Deviation)), new Section("CAPA", typeof(CAPARecord)),
            new Section("Punch List", typeof(PunchItem)), new Section("审核记录", typeof(ReviewRecord)),
            new Section("追踪关系", typeof(TraceLink)), new Section("RTM"),
            new Section("变更影响", typeof(ChangeImpact)), new Section("验证基线", typeof(ValidationBaseline)),
            new Section("合规辅助", typeof(ComplianceCheck)), new Section("交付资料", typeof(Deliverable)), new Section("报告导出")
        };

        private Section SelectedSection { get { return section.SelectedItem as Section ?? Sections[0]; } }
        private ValidationRecord SelectedRecord { get { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as ValidationRecord; } }

        internal static void Open(IWin32Window owner, Func<ProjectContext> activeProject,
            Func<IEnumerable<TaskItem>> tasks = null, Func<IEnumerable<DeskBox.Sync.IncidentDto>> incidents = null)
        {
            var project = activeProject();
            if (project == null)
            {
                MessageBox.Show(owner, "请先选择工程项目。", "验证工作台", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var form = new ValidationCenterForm(project, activeProject, tasks, incidents)) form.ShowDialog(owner);
        }

        private ValidationCenterForm(ProjectContext project, Func<ProjectContext> activeProject,
            Func<IEnumerable<TaskItem>> tasks, Func<IEnumerable<DeskBox.Sync.IncidentDto>> incidents)
        {
            this.project = project;
            this.activeProject = activeProject;
            this.tasks = tasks;
            this.incidents = incidents;
            Name = "validation-workbench";
            Text = "验证工作台 · " + project.Name;
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(635, 425);
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(995, screen.Width - 45), Math.Min(660, screen.Height - 85));

            var top = Toolbar();
            top.Controls.Add(LabelFor("分区"));
            top.Controls.Add(section);
            top.Controls.Add(LabelFor("筛选"));
            top.Controls.Add(search);
            Add(top, "查找", RefreshRows);
            Add(top, "重新读取", () => LoadSnapshot());
            Add(top, "恢复备份…", Restore);
            top.Controls.Add(cancel);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 90, Panel2MinSize = 90 };
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(details);
            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(actions);
            Controls.Add(top);

            section.Items.AddRange(Sections);
            section.SelectedIndex = 0;
            section.SelectedIndexChanged += delegate { search.Clear(); RefreshRows(); };
            search.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; RefreshRows(); }
            };
            grid.SelectionChanged += delegate { SelectRow(); };
            cancel.Click += delegate { if (work != null) work.Cancel(); };
            Shown += delegate { if (split.Height > 230) split.SplitterDistance = (int)(split.Height * .56); contextWatch.Start(); LoadSnapshot(); };
            contextWatch.Tick += delegate { if (!project.SameScope(activeProject())) Close(); };
            FormClosed += delegate
            {
                contextWatch.Stop();
                contextWatch.Dispose();
                if (work != null) work.Cancel();
            };
        }

        private void Restore()
        {
            if (snapshot == null || !snapshot.Recovered)
            {
                status.Text = "当前台账未处于备份恢复模式。";
                return;
            }
            if (!Confirm("恢复上一份可读取台账，保留损坏原件供检查？")) return;
            Run("恢复台账", t => { service.Restore(project, t); return true; }, value => LoadSnapshot("已恢复备份。"));
        }

        private async void Run<T>(string activity, Func<CancellationToken, T> operation, Action<T> display)
        {
            if (work != null) { status.Text = "当前任务仍在运行。"; return; }
            if (!project.SameScope(activeProject())) return;
            var cancellation = new CancellationTokenSource();
            work = cancellation;
            status.Text = activity + "…";
            cancel.Enabled = true;
            section.Enabled = false;
            try
            {
                var value = await Task.Run(() => operation(cancellation.Token), cancellation.Token);
                if (!IsDisposed && project.SameScope(activeProject()))
                {
                    if (!cancellation.IsCancellationRequested)
                    {
                        work = null;
                        status.Text = activity + "完成。";
                        display(value);
                    }
                    else status.Text = "任务已取消。";
                }
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "任务已取消。"; }
            catch (Exception error) { if (!IsDisposed) status.Text = activity + "失败 · " + error.Message; }
            finally
            {
                if (ReferenceEquals(work, cancellation)) work = null;
                cancellation.Dispose();
                if (!IsDisposed)
                {
                    cancel.Enabled = work != null;
                    section.Enabled = work == null;
                }
            }
        }

        private void LoadSnapshot(string note = null)
        {
            Run("读取项目台账", token => service.Read(project, token), data =>
            {
                snapshot = data;
                RefreshRows();
                if (data.Recovered) status.Text = "主文件损坏。当前展示上一份可读取备份，可先检查再恢复。";
                else if (note != null) status.Text = note;
            });
        }

        private void RefreshRows()
        {
            if (snapshot == null || work != null) return;
            actions.Controls.Clear();
            var view = SelectedSection;
            if (view.Label == "验证总览")
            {
                ShowOverview();
                return;
            }
            if (view.Label == "RTM")
            {
                Add(actions, "筛选条件…", ConfigureTrace);
                Add(actions, "导出 RTM…", () => ExportReport("rtm"));
                ShowTrace();
                return;
            }
            if (view.Label == "报告导出")
            {
                Add(actions, "生成 XLSX / DOCX…", () => ExportReport());
                Display(new[] { "报告类别", "格式" }, ValidationReports.Kinds
                    .Select(item => Row(item.Key, item.Value, "XLSX / DOCX")));
                details.Text = "报告来源为当前项目台账。缺少的结果、签核或证据会在报告中保留空缺。DOCX 可使用企业模板。";
                return;
            }
            if (view.RecordType == typeof(TestStep))
            {
                Add(actions, "维护用例测试步骤…", () => EditTestCaseSteps(SelectedRecord as TestCase));
                Display(new[] { "测试编号", "测试用例", "步骤数", "用例版本" }, snapshot.All<TestCase>()
                    .Where(item => RecordsMatch(item)).Select(item => Row(item, item.Number, item.Title, item.Steps.Count.ToString(), item.Revision.ToString())));
                return;
            }
            ConfigureRecordActions(view.RecordType);
            var records = snapshot.Records.Where(item => item != null && item.GetType() == view.RecordType)
                .Where(RecordsMatch).OrderBy(item => item.Stage).ThenBy(item => item.Number, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Title);
            Display(new[] { "编号 / 类型", "标题 / 对象", "阶段", "版本", "状态", "来源 / 关联" }, records.Select(item =>
                Row(item, DisplayNumber(item), DisplayTitle(item), item.Stage, item.Revision.ToString(), StatusLabel(item.Status), Summary(item))));
        }

        private bool RecordsMatch(ValidationRecord row)
        {
            var term = search.Text.Trim();
            return term.Length == 0 || new[] { row.Number, row.Title, row.Id, row.Description, row.Stage, row.Status, row.SystemId, row.EquipmentId, Summary(row) }
                .Any(value => (value ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void ConfigureRecordActions(Type type)
        {
            bool special = type == typeof(TestExecution) || type == typeof(Evidence) || type == typeof(ReviewRecord) ||
                type == typeof(TraceLink) || type == typeof(ChangeImpact) || type == typeof(ValidationBaseline);
            if (!special)
            {
                Add(actions, "新增…", () => EditRecord(type, null));
                Add(actions, "编辑…", () => EditRecord(type, SelectedRecord));
                if (type == typeof(ValidationRequirement) || type == typeof(TestCase))
                    Add(actions, "导入…", () => Import(type == typeof(TestCase)));
                if (type == typeof(TestCase))
                {
                    Add(actions, "复用为新用例…", CloneCase);
                    Add(actions, "维护步骤…", () => EditTestCaseSteps(SelectedRecord as TestCase));
                    Add(actions, "记录执行…", () => RecordExecution(null, SelectedRecord as TestCase));
                }
                if (type == typeof(Deviation))
                    Add(actions, "确认关闭偏差…", CloseDeviation);
            }
            if (type == typeof(TestExecution))
            {
                Add(actions, "记录执行…", () => RecordExecution(null, null));
                Add(actions, "编辑进行中的记录…", () => RecordExecution(SelectedRecord as TestExecution, null));
                Add(actions, "记录重新测试…", Retest);
                Add(actions, "添加证据…", () => Archive(SelectedRecord as TestExecution, null));
            }
            if (type == typeof(Evidence))
            {
                Add(actions, "归档证据…", () => Archive(null, null));
                Add(actions, "添加衍生版本…", () => Archive(null, SelectedRecord as Evidence));
                Add(actions, "哈希校验", VerifyEvidence);
                Add(actions, "预览或定位原件…", OpenEvidence);
                Add(actions, "恢复证据…", RecoverEvidence);
            }
            if (type == typeof(ReviewRecord)) Add(actions, "录入人工审核…", () => Review(null));
            else if (type != typeof(TraceLink) && type != typeof(ValidationBaseline)) Add(actions, "审核…", () => Review(SelectedRecord));
            if (type == typeof(TraceLink))
            {
                Add(actions, "新建关联…", () => Link(null));
                Add(actions, "撤回关联…", () =>
                {
                    var link = SelectedRecord as TraceLink;
                    if (link == null || link.Status == "withdrawn" || !Confirm("确认撤回此追踪关系？其历史仍可查询。")) return;
                    Run("撤回关联", token => service.WithdrawLink(project, link.Id, link.Revision, token), item => LoadSnapshot());
                });
            }
            else if (type != typeof(ValidationBaseline)) Add(actions, "添加关联…", () => Link(SelectedRecord));
            if (type == typeof(ChangeImpact))
            {
                Add(actions, "评估两版工程资料…", Impact);
                Add(actions, "记录评估决定…", AssessImpact);
            }
            if (type == typeof(ValidationBaseline))
            {
                Add(actions, "建立快照…", CaptureBaseline);
                Add(actions, "比较快照…", CompareBaselines);
                Add(actions, "导出基线…", () => ExportReport("baselines"));
            }
            Add(actions, "历史版本…", History);
            if (type != typeof(Evidence)) Add(actions, "定位来源", OpenSource);
        }

        private sealed class GridRow
        {
            internal object Item;
            internal string[] Values;
        }

        private static GridRow Row(object data, params string[] values) { return new GridRow { Item = data, Values = values }; }

        private void Display(string[] headers, IEnumerable<GridRow> rows)
        {
            grid.SuspendLayout();
            try
            {
                grid.Rows.Clear();
                grid.Columns.Clear();
                for (int i = 0; i < headers.Length; i++)
                    grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "col" + i, HeaderText = headers[i], SortMode = DataGridViewColumnSortMode.Automatic });
                int count = 0;
                foreach (var row in rows.Take(1001))
                {
                    if (++count > 1000) break;
                    int i = grid.Rows.Add(row.Values.Select(value => (object)(value ?? "")).ToArray());
                    grid.Rows[i].Tag = row.Item;
                }
                status.Text = count == 0 ? "当前分区无记录。" : count > 1000 ? "展示前 1000 条，可通过筛选缩小范围。" : "记录数 " + count;
                if (grid.Rows.Count > 0) grid.CurrentCell = grid.Rows[0].Cells[0];
            }
            finally { grid.ResumeLayout(); }
            SelectRow();
        }

        private void SelectRow()
        {
            object row = grid.CurrentRow == null ? null : grid.CurrentRow.Tag;
            var record = row as ValidationRecord;
            if (record != null)
            {
                details.Text = DescribeRecord(record);
                return;
            }
            var matrix = row as ValidationTraceRow;
            if (matrix != null)
            {
                details.Text = DescribeTrace(matrix);
                return;
            }
            details.Clear();
        }

        private string DescribeRecord(ValidationRecord record)
        {
            var text = new StringBuilder();
            text.AppendLine(DisplayTitle(record));
            text.AppendLine("ID  " + record.Id + " · 版本 " + record.Revision + " · " + StatusLabel(record.Status));
            text.AppendLine("阶段  " + record.Stage + "   系统  " + record.SystemId + "   设备  " + record.EquipmentId);
            text.AppendLine("创建  " + record.CreatedBy + " · " + FormatTime(record.CreatedAt));
            text.AppendLine("更新  " + record.UpdatedBy + " · " + FormatTime(record.UpdatedAt));
            if (record.Source != null) text.AppendLine("来源  " + record.Source.Document + " " + record.Source.Location + " · " + record.Source.Version + " · " + record.Source.Sha256);
            text.AppendLine(record.Description);
            var review = snapshot.All<ReviewRecord>().Where(item => item.TargetId == record.Id && item.TargetRevision == record.Revision)
                .OrderByDescending(item => item.CreatedAt).FirstOrDefault();
            if (review != null) text.AppendLine("当前版本人工审核  " + StatusLabel(review.Decision) + " · " + review.CreatedBy);
            foreach (var field in FieldsFor(record.GetType()).Where(item => item.Key != "Description" && item.Key != "Status"))
            {
                var value = FieldValue(record, field.Key);
                if (value.Length != 0) text.AppendLine(field.Label + "  " + value);
            }
            AppendSpecial(text, record);
            var linked = snapshot.All<TraceLink>().Where(item => item.FromId == record.Id || item.ToId == record.Id).ToList();
            if (linked.Count > 0)
            {
                text.AppendLine().AppendLine("双向追踪");
                foreach (var link in linked.Take(100))
                    text.AppendLine(DisplayEndpoint(link.FromId) + "  →  " + DisplayEndpoint(link.ToId) + " · " + LinkName(link.Kind));
                if (linked.Count > 100) text.AppendLine("另有 " + (linked.Count - 100) + " 条关联。");
            }
            return text.ToString();
        }

        private string DisplayEndpoint(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            var item = snapshot.Records.FirstOrDefault(record => record.Id == id);
            return item == null ? id : DisplayTitle(item) + " [" + id + "]";
        }

        private static string DisplayNumber(ValidationRecord item)
        {
            var link = item as TraceLink;
            if (link != null) return LinkName(link.Kind);
            return item.Number;
        }

        private string DisplayTitle(ValidationRecord row)
        {
            var evidence = row as Evidence;
            if (evidence != null) return evidence.FileName;
            var execution = row as TestExecution;
            if (execution != null) return DisplayEndpoint(execution.TestCaseId);
            var relationship = row as TraceLink;
            if (relationship != null) return DisplayEndpoint(relationship.FromId) + " → " + DisplayEndpoint(relationship.ToId);
            var review = row as ReviewRecord;
            if (review != null) return StatusLabel(review.Decision) + " · " + DisplayEndpoint(review.TargetId);
            return string.IsNullOrWhiteSpace(row.Title) ? row.Number : row.Title;
        }

        private static string Summary(ValidationRecord row)
        {
            var execution = row as TestExecution;
            if (execution != null) return execution.PerformedBy + " · " + FormatTime(execution.PerformedAt);
            var evidence = row as Evidence;
            if (evidence != null) return evidence.Sha256;
            var review = row as ReviewRecord;
            if (review != null) return review.TargetId + " · 版本 " + review.TargetRevision;
            if (row.Source == null) return "";
            return (row.Source.Document ?? "") + " " + row.Source.Location;
        }

        private static string FormatTime(long value)
        {
            if (value <= 0) return "";
            try { return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
            catch (ArgumentOutOfRangeException) { return value.ToString(CultureInfo.InvariantCulture); }
        }

        private bool Confirm(string message)
        {
            return MessageBox.Show(this, message, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private static FlowLayoutPanel Toolbar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 3, 4, 3) };
        }

        private static Label LabelFor(string text)
        {
            return new Label { Text = text, UseMnemonic = false, AutoSize = true, Margin = new Padding(5, 8, 1, 0) };
        }

        private static void Add(FlowLayoutPanel bar, string text, Action action)
        {
            var button = new Button { Text = text, UseMnemonic = false, AutoSize = true };
            button.Click += delegate { action(); };
            bar.Controls.Add(button);
        }

        private string ChooseFile(string title, string filter)
        {
            using (var dialog = new OpenFileDialog { Title = title, CheckFileExists = true, Filter = filter,
                InitialDirectory = Directory.Exists(project.Root) ? project.Root : null })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }
    }
}
