using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using DeskBox.AppServices;
using DeskBox.Core;
using DeskBox.Domain;
using DeskBox.Fs;
using DeskBox.Native;

namespace DeskBox.UI
{
    internal sealed partial class ValidationCenterForm
    {
        private static readonly string[] StandardStages = { "URS", "FS", "DS", "DQ", "FAT", "SAT", "IQ", "OQ", "PQ", "CSV", "最终交付" };
        private static readonly string[] ExecutionStates = { "running", "passed", "failed", "blocked", "not-applicable", "withdrawn" };
        private static readonly string[] DeviationStates = { "new", "assessing", "investigating", "handling", "retest-pending", "review-pending", "withdrawn" };
        private static readonly string[] GenericStates = { "draft", "planned", "in-progress", "complete", "withdrawn" };
        private static readonly string[] Severity = { "low", "medium", "high", "critical" };
        private static readonly string[] Compliance = { "GAMP 5", "21 CFR Part 11", "EU GMP Annex 11", "ALCOA+", "用户权限审查", "Audit Trail Review", "备份恢复验证", "数据完整性检查" };

        private sealed class Field
        {
            internal string Key, Label;
            internal bool Multi;
            internal string[] Choices;
        }

        private static Field Spec(string key, string name, bool multi = false, string[] options = null)
        {
            return new Field { Key = key, Label = name, Multi = multi, Choices = options };
        }

        private static IEnumerable<Field> FieldsFor(Type type)
        {
            if (type == typeof(ValidationPlan)) return new[] { Spec("Stages", "适用阶段（逗号或换行）", true),
                Spec("ReviewerIds", "审核账号（每行一项，域\\账号）", true), Spec("RiskRule", "项目风险评价规则", true) };
            if (type == typeof(ValidationDocument)) return new[] { Spec("DocumentType", "文件类别"), Spec("DocumentVersion", "文件版本"),
                Spec("Applicable", "参与统计", false, new[] { "true", "false" }) };
            if (type == typeof(ValidationRequirement)) return new[] { Spec("DocumentId", "已登记来源文件"), Spec("DocumentNumber", "文档编号"),
                Spec("DocumentVersion", "文档版本"), Spec("Category", "需求类别"), Spec("RiskLevel", "风险等级"),
                Spec("Applicable", "适用状态", false, new[] { "true", "false" }), Spec("Voided", "作废", false, new[] { "false", "true" }) };
            if (type == typeof(DesignReference) || type == typeof(TestProtocol)) return new[] { Spec("DocumentId", "已登记设计/方案文件") };
            if (type == typeof(RiskAssessment)) return new[] { Spec("Level", "风险等级"), Spec("ImpactCategory", "影响类别"),
                Spec("Control", "控制措施", true), Spec("ResidualRisk", "剩余风险", true), Spec("Method", "评价方法") };
            if (type == typeof(TestCase)) return new[] { Spec("ProtocolId", "测试方案"), Spec("Purpose", "测试目的", true),
                Spec("Preconditions", "前置条件", true), Spec("ExpectedResult", "预期结果", true), Spec("Applicable", "适用状态", false, new[] { "true", "false" }) };
            if (type == typeof(Deviation)) return new[] { Spec("ExecutionId", "原测试执行"), Spec("TestCaseId", "测试用例"),
                Spec("RetestExecutionId", "关联重新测试"), Spec("Expected", "预期结果", true), Spec("Actual", "实际结果", true),
                Spec("Impact", "影响评估", true), Spec("RiskLevel", "风险等级"), Spec("Investigation", "调查记录", true),
                Spec("RootCause", "根本原因", true), Spec("Action", "处理措施", true) };
            if (type == typeof(CAPARecord)) return new[] { Spec("Owner", "负责人"), Spec("Due", "截止日期"),
                Spec("RootCause", "原因分析", true), Spec("Correction", "纠正措施", true), Spec("Prevention", "预防措施", true),
                Spec("Implementation", "实施记录", true), Spec("Effectiveness", "有效性评估", true) };
            if (type == typeof(PunchItem)) return new[] { Spec("Owner", "责任方"), Spec("Due", "截止日期"), Spec("Priority", "优先级"),
                Spec("Treatment", "处理记录", true) };
            if (type == typeof(Deliverable)) return new[] { Spec("Required", "是否必需", false, new[] { "true", "false" }), Spec("DocumentId", "交付文件") };
            if (type == typeof(ComplianceCheck)) return new[] { Spec("Framework", "框架", false, Compliance), Spec("BasisVersion", "依据版本"),
                Spec("BasisLocation", "依据位置"), Spec("Observation", "检查记录", true), Spec("Applicable", "适用状态", false, new[] { "true", "false" }) };
            return Enumerable.Empty<Field>();
        }

        private static string FieldValue(ValidationRecord record, string name)
        {
            var info = record.GetType().GetField(name);
            if (info == null) return "";
            object value = info.GetValue(record);
            var list = value as List<string>;
            if (list != null) return string.Join(Environment.NewLine, list);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static void SetField(ValidationRecord record, string key, string value)
        {
            var field = record.GetType().GetField(key);
            if (field.FieldType == typeof(bool)) field.SetValue(record, string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
            else if (field.FieldType == typeof(List<string>)) field.SetValue(record,
                value.Split(new[] { ',', '\n', '\r', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim())
                    .Where(item => item.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            else field.SetValue(record, value);
        }

        private void EditRecord(Type type, ValidationRecord original)
        {
            if (snapshot.Recovered) { status.Text = "请先恢复备份。"; return; }
            if (original == null && type == typeof(ValidationPlan) && snapshot.All<ValidationPlan>().Any())
            {
                status.Text = "本项目已有验证计划，请选择计划编辑。";
                return;
            }
            if (original != null && type == typeof(Deviation) && original.Status == "closed")
            {
                status.Text = "已关闭的偏差按当前版本保留。";
                return;
            }
            var draft = original == null ? (ValidationRecord)Activator.CreateInstance(type) : Clone(original);
            if (original == null)
            {
                draft.SystemId = project.System;
                draft.EquipmentId = project.Equipment;
                if (type == typeof(ValidationRequirement)) draft.Stage = "URS";
                if (type == typeof(Deviation)) draft.Status = "new";
                if (type == typeof(ValidationPlan))
                {
                    var plan = (ValidationPlan)draft;
                    plan.Title = "验证计划";
                    plan.Stages = StandardStages.ToList();
                    plan.ReviewerIds.Add(ValidationService.CurrentActor);
                }
            }
            var phases = snapshot.All<ValidationPlan>().SelectMany(plan => plan.Stages).ToList();
            if (phases.Count == 0) phases.AddRange(StandardStages);
            if (!string.IsNullOrEmpty(draft.Stage) && !phases.Contains(draft.Stage)) phases.Add(draft.Stage);
            using (var form = new EntryForm(this, original == null ? "新增记录" : "编辑 · " + DisplayTitle(original)))
            {
                form.TextField("Number", "编号", draft.Number);
                form.TextField("Title", "标题", draft.Title);
                form.Choice("Stage", "验证阶段", phases.Distinct(), draft.Stage, true);
                form.TextField("SystemId", "系统", draft.SystemId);
                form.TextField("EquipmentId", "设备", draft.EquipmentId);
                form.TextField("Description", "说明", draft.Description, true);
                if (type == typeof(ValidationDocument) || type == typeof(PunchItem) || type == typeof(CAPARecord) || type == typeof(Deliverable) ||
                    type == typeof(ComplianceCheck) || type == typeof(Deviation))
                    form.Choice("Status", "业务状态", type == typeof(Deviation) ? DeviationStates : GenericStates, draft.Status, false);
                foreach (var field in FieldsFor(type))
                {
                    string value = FieldValue(draft, field.Key);
                    Type linkedType = field.Key == "DocumentId" ? typeof(ValidationDocument) : field.Key == "ProtocolId" ? typeof(TestProtocol)
                        : field.Key == "TestCaseId" ? typeof(TestCase) : field.Key == "ExecutionId" || field.Key == "RetestExecutionId" ? typeof(TestExecution) : null;
                    if (linkedType != null) form.Selection(field.Key, field.Label, RecordsOf(linkedType), value, true);
                    else if (field.Choices != null) form.Choice(field.Key, field.Label, field.Choices, value, false);
                    else form.TextField(field.Key, field.Label, value, field.Multi);
                }
                form.TextField("source-path", "挂接来源文件", "", file: true);
                form.TextField("source-location", "原文位置", draft.Source == null ? "" : draft.Source.Location);
                form.TextField("source-version", "来源版本", draft.Source == null ? "" : draft.Source.Version);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                foreach (var key in new[] { "Number", "Title", "Stage", "SystemId", "EquipmentId", "Description", "Status" })
                    if (form.Has(key)) SetField(draft, key, form.Value(key));
                foreach (var field in FieldsFor(type)) SetField(draft, field.Key, form.Value(field.Key));
                var path = form.Value("source-path");
                string location = form.Value("source-location"), sourceVersion = form.Value("source-version");
                if (path.Length == 0 && draft.Source != null && draft.Source.Document.Length != 0 &&
                    (location != draft.Source.Location || sourceVersion != draft.Source.Version))
                    path = Path.Combine(project.Root, draft.Source.Document);
                int revision = original == null ? 0 : original.Revision;
                Run("保存验证记录", token =>
                {
                    if (path.Length != 0)
                        draft.Source = ValidationService.CaptureSource(project, path, location, sourceVersion, token);
                    return service.Save(project, draft, revision, token);
                }, saved => LoadSnapshot("已保存 " + DisplayTitle(saved) + " · 版本 " + saved.Revision));
            }
        }

        private IEnumerable<Pick> RecordsOf(Type type)
        {
            return snapshot.Records.Where(item => item.GetType() == type).Select(item => new Pick(item.Id, ChoiceTitle(item)));
        }

        private IEnumerable<Pick> AllRecordPicks()
        {
            return snapshot.Records.Where(item => !(item is TraceLink) && !(item is ReviewRecord))
                .Select(item => new Pick(item.Id, ChoiceTitle(item)));
        }

        private string ChoiceTitle(ValidationRecord item)
        {
            var view = Sections.FirstOrDefault(section => section.RecordType == item.GetType());
            string label = view == null ? item.GetType().Name : view.Label;
            return label + " · " + item.Number + " " + DisplayTitle(item) + " · v" + item.Revision + " · " + item.Id;
        }

        private static ValidationRecord Clone(ValidationRecord record)
        {
            return (ValidationRecord)new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }
                .Deserialize(Json.ToJson(record), record.GetType());
        }

        private void CloneCase()
        {
            var original = SelectedRecord as TestCase;
            if (original == null) return;
            using (var form = new EntryForm(this, "将选定规格复用为独立测试用例"))
            {
                form.TextField("number", "新测试编号", "");
                form.TextField("title", "新标题", original.Title);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var draft = (TestCase)Clone(original);
                draft.Id = "";
                draft.Revision = 0;
                draft.Number = form.Value("number");
                draft.Title = form.Value("title");
                draft.Status = "draft";
                foreach (var step in draft.Steps) { step.Id = ""; step.Revision = 0; }
                Run("保存复用用例", token => service.Save(project, draft, 0, token),
                    result => LoadSnapshot("已复用规范及步骤 · " + result.Id));
            }
        }

        private void EditTestCaseSteps(TestCase original)
        {
            if (original == null) return;
            var next = (TestCase)Clone(original);
            using (var form = new Form
            {
                Text = "测试步骤 · " + original.Number + " · " + original.Title, Size = new Size(820, 470),
                MinimumSize = new Size(540, 360), StartPosition = FormStartPosition.CenterParent
            })
            {
                var list = new DataGridView { Dock = DockStyle.Fill, RowHeadersVisible = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells, SelectionMode = DataGridViewSelectionMode.CellSelect };
                list.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
                list.Columns.Add("order", "顺序（整数）");
                list.Columns.Add("action", "测试动作");
                list.Columns.Add("expected", "预期结果");
                list.Columns[0].FillWeight = 25;
                foreach (var step in next.Steps.OrderBy(step => step.Order))
                    list.Rows[list.Rows.Add(step.Order.ToString(CultureInfo.InvariantCulture), step.Action, step.ExpectedResult)].Tag = step;
                var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.RightToLeft };
                var save = new Button { Text = "保存为新用例版本", AutoSize = true };
                save.Click += delegate
                {
                    list.EndEdit();
                    var steps = new List<TestStep>();
                    foreach (DataGridViewRow row in list.Rows)
                    {
                        if (row.IsNewRow) continue;
                        int order;
                        string action = Convert.ToString(row.Cells[1].Value), expected = Convert.ToString(row.Cells[2].Value);
                        if (!int.TryParse(Convert.ToString(row.Cells[0].Value), out order) || order < 1 || steps.Any(item => item.Order == order)
                            || string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(expected))
                        {
                            MessageBox.Show(form, "每行需有唯一的正整数顺序、动作和预期结果。"); return;
                        }
                        var step = row.Tag as TestStep ?? new TestStep();
                        step.Order = order; step.Action = action; step.ExpectedResult = expected;
                        steps.Add(step);
                    }
                    next.Steps = steps.OrderBy(step => step.Order).ToList();
                    form.DialogResult = DialogResult.OK;
                };
                footer.Controls.Add(save);
                footer.Controls.Add(new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel });
                form.Controls.Add(list); form.Controls.Add(footer);
                if (form.ShowDialog(this) == DialogResult.OK)
                    Run("保存步骤", token => service.Save(project, next, original.Revision, token), result => LoadSnapshot());
            }
        }

        private void Retest()
        {
            var prior = SelectedRecord as TestExecution;
            if (prior == null) return;
            var testCase = snapshot.Find<TestCase>(prior.TestCaseId);
            if (testCase == null) { status.Text = "原测试用例无法读取。"; return; }
            RecordExecution(null, testCase, prior.Id);
        }

        private void RecordExecution(TestExecution previous, TestCase selected, string retestOf = "")
        {
            if (snapshot.Recovered) { status.Text = "请先恢复备份。"; return; }
            if (previous == null && selected == null)
            {
                using (var choice = new EntryForm(this, "选择本次测试用例"))
                {
                    choice.Selection("test", "测试用例", RecordsOf(typeof(TestCase)), "", false);
                    if (choice.ShowDialog(this) != DialogResult.OK) return;
                    selected = snapshot.Find<TestCase>(choice.Value("test"));
                }
            }
            if (previous != null)
            {
                if (previous.Status != "running") { status.Text = "已结束的执行保留为历史。可新建重新测试。"; return; }
                selected = snapshot.Find<TestCase>(previous.TestCaseId);
                if (selected == null || selected.Revision != previous.TestCaseRevision)
                    selected = snapshot.History.OfType<TestCase>().FirstOrDefault(item => item.Id == previous.TestCaseId && item.Revision == previous.TestCaseRevision);
            }
            if (selected == null) { status.Text = "请在测试用例中建立用例后再记录。"; return; }
            var draft = previous == null ? new TestExecution { TestCaseId = selected.Id, TestCaseRevision = selected.Revision, RetestOfId = retestOf,
                // The service attributes the action to the current Windows identity.
                Stage = selected.Stage, Title = selected.Title, Status = "running" } : (TestExecution)Clone(previous);
            using (var form = new EntryForm(this, "人工记录执行 · " + selected.Number + " · 规格版本 " + selected.Revision))
            {
                form.Note("当前操作人  " + ValidationService.CurrentActor);
                form.Choice("status", "本次执行状态", ExecutionStates, draft.Status, false);
                form.TextField("result", "总体实际结果", draft.ResultText, true);
                foreach (var step in selected.Steps.OrderBy(item => item.Order))
                {
                    var actual = draft.StepResults.FirstOrDefault(item => item.StepId == step.Id);
                    form.Note(step.Order + "  " + step.Action + Environment.NewLine + "预期  " + step.ExpectedResult);
                    form.Choice("out-" + step.Id, "本步结果", ExecutionStates, actual == null ? "" : actual.Outcome, false, true);
                    form.TextField("actual-" + step.Id, "本步实际", actual == null ? "" : actual.Actual, true);
                }
                if (form.ShowDialog(this) != DialogResult.OK) return;
                draft.Status = form.Value("status");
                draft.ResultText = form.Value("result");
                draft.StepResults = selected.Steps.Select(step => new ValidationStepResult
                {
                    StepId = step.Id, Actual = form.Value("actual-" + step.Id), Outcome = form.Value("out-" + step.Id)
                }).Where(item => item.Actual.Length != 0 || item.Outcome.Length != 0).ToList();
                if (draft.Status != "running" && !Confirm("将此人工记录保存为 " + StatusLabel(draft.Status) + "？")) return;
                Run("保存执行记录", token => service.RecordExecution(project, draft, previous == null ? 0 : previous.Revision, token),
                    result => LoadSnapshot("执行记录 " + result.Id + " 已保存。"));
            }
        }

        private void Archive(TestExecution execution, Evidence parent)
        {
            if (snapshot.Recovered) { status.Text = "请先恢复备份。"; return; }
            var path = ChooseFile("选择要归档的原文件或独立衍生文件", "全部附件|*.*");
            if (path == null) return;
            using (var form = new EntryForm(this, parent == null ? "归档原始证据" : "归档独立衍生版本"))
            {
                var executionId = execution == null ? parent == null ? "" : parent.ExecutionId : execution.Id;
                form.Selection("execution", "测试执行", RecordsOf(typeof(TestExecution)), executionId, true);
                form.Selection("case", "测试用例", RecordsOf(typeof(TestCase)), parent == null ? "" : parent.TestCaseId, true);
                form.TextField("description", "归档说明", "", true);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var evidence = new Evidence
                {
                    ExecutionId = form.Value("execution"), TestCaseId = form.Value("case"), Description = form.Value("description"),
                    ParentEvidenceId = parent == null ? "" : parent.Id
                };
                Run("归档并校验文件", token => service.ArchiveEvidence(project, evidence, path, token),
                    result => LoadSnapshot("证据已归档 · " + result.FileName + " · " + result.Sha256));
            }
        }

        private void VerifyEvidence()
        {
            var record = SelectedRecord as Evidence;
            if (record == null) return;
            Run("校验证据", token => service.VerifyEvidence(project, record, token), value =>
            {
                status.Text = "校验结果 · " + (value == "verified" ? "哈希一致" : value == "missing" ? "文件缺失" : value == "corrupt" ? "文件损坏" : value);
                details.Text = DescribeRecord(record) + Environment.NewLine + status.Text;
            });
        }

        private sealed class EvidencePreview
        {
            internal string Path;
            internal PreviewResult Preview;
        }

        private void OpenEvidence()
        {
            var record = SelectedRecord as Evidence;
            if (record == null) return;
            Run("读取归档文件", token =>
            {
                var path = service.EvidencePath(project, record, token);
                return new EvidencePreview { Path = path, Preview = PreviewService.Produce(path, 12000, "." + record.FileType) };
            }, data =>
            {
                var result = data.Preview;
                try
                {
                    using (var form = new Form { Text = "归档原件 · " + record.FileName, StartPosition = FormStartPosition.CenterParent,
                        Size = new Size(760, 500), MinimumSize = new Size(470, 330) })
                    {
                        if (result.Image != null)
                        {
                            form.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = result.Image, SizeMode = PictureBoxSizeMode.Zoom });
                        }
                        else
                        {
                            var text = new StringBuilder().AppendLine(record.FileName).AppendLine(result.Subtitle)
                                .AppendLine(result.Error).AppendLine(result.Text);
                            foreach (var row in result.Rows) text.AppendLine(string.Join(" | ", row));
                            form.Controls.Add(new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, WordWrap = false,
                                ScrollBars = ScrollBars.Both, Text = text.ToString() });
                        }
                        var toolbar = Toolbar();
                        Add(toolbar, "定位归档", () => TryUi(() => Win32.RevealPath(data.Path)));
                        form.Controls.Add(toolbar);
                        form.ShowDialog(this);
                    }
                }
                finally { if (result.Image != null) result.Image.Dispose(); }
            });
        }

        private void Review(ValidationRecord record)
        {
            if (snapshot.Recovered) { status.Text = "请先恢复备份。"; return; }
            using (var form = new EntryForm(this, "录入业务审核"))
            {
                form.Note("审核账号  " + ValidationService.CurrentActor + Environment.NewLine + "审核记录绑定所选版本。");
                form.Selection("target", "被审核记录", AllRecordPicks(), record == null ? "" : record.Id, false);
                form.Choice("decision", "人工审核结论", new[] { "approved", "rejected" }, "", false);
                form.TextField("comment", "审核意见", "", true);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var target = snapshot.Records.FirstOrDefault(item => item.Id == form.Value("target"));
                if (target == null) { status.Text = "请选择被审核记录。"; return; }
                var decision = form.Value("decision"); var comment = form.Value("comment");
                Run("保存人工审核", token => service.Review(project, target.Id, target.Revision, decision, comment, token), result => LoadSnapshot());
            }
        }

        private void CloseDeviation()
        {
            var item = SelectedRecord as Deviation;
            if (item == null || !Confirm("确认使用当前版本的人工审核、调查及必要的重新测试记录申请关闭该偏差？")) return;
            Run("申请关闭偏差", token => service.CloseDeviation(project, item.Id, item.Revision, token),
                result => LoadSnapshot("偏差 " + result.Number + " 已关闭。"));
        }

        private void Link(ValidationRecord selected)
        {
            if (snapshot.Recovered) { status.Text = "请先恢复备份。"; return; }
            using (var form = new EntryForm(this, "建立稳定 ID 追踪关系"))
            {
                var records = AllRecordPicks().ToArray();
                form.Selection("from", "起点", records, selected == null ? "" : selected.Id, false);
                form.Choice("kind", "关系", LinkLabels.Keys, "verifies", false);
                form.Selection("to", "目标业务记录", records, "", true);
                form.TextField("external", "Tag 稳定 ID", "");
                var projectTasks = tasks == null ? new TaskItem[0] : tasks().Where(project.IncludesTask).ToArray();
                var projectIncidents = incidents == null ? new DeskBox.Sync.IncidentDto[0] : incidents().Where(item => item.ProjectId == project.Id).ToArray();
                form.Selection("task", "已有项目任务（用于 task）", projectTasks.Select(item => new Pick(item.Id, item.Title)), "", true);
                form.Selection("incident", "已有现场事件（用于 incident）", projectIncidents.Select(item => new Pick(item.Id, item.Description)), "", true);
                form.Note("需求覆盖使用「需求 → 测试用例（验证）」。任务照常使用已有提醒。");
                if (form.ShowDialog(this) != DialogResult.OK) return;
                string from = form.Value("from"), to = form.Value("to"), kind = form.Value("kind");
                if (kind == "tag") to = form.Value("external");
                var task = projectTasks.SingleOrDefault(item => item.Id == form.Value("task"));
                var incident = projectIncidents.SingleOrDefault(item => item.Id == form.Value("incident"));
                Run("建立追踪关系", token => kind == "task" ? service.LinkTask(project, from, task, token) :
                    kind == "incident" ? service.LinkIncident(project, from, incident == null ? "" : incident.Id,
                        incident == null ? "" : incident.ProjectId, token) : service.Link(project, from, to, kind, token),
                    result => LoadSnapshot("追踪关系已保存。"));
            }
        }

        private void History()
        {
            var item = SelectedRecord;
            if (item == null) return;
            var versions = snapshot.History.Concat(new[] { item }).Where(record => record.Id == item.Id)
                .OrderBy(record => record.Revision).ToArray();
            if (versions.Length < 2) { status.Text = "当前记录只有一个已保存版本。"; return; }
            var choices = versions.Select(record => new Pick(record.Revision.ToString(CultureInfo.InvariantCulture),
                "版本 " + record.Revision + " · " + FormatTime(record.UpdatedAt))).ToArray();
            using (var form = new EntryForm(this, "比较历史版本 · " + item.Number))
            {
                form.Selection("before", "变更前", choices, choices.First().Id, false);
                form.Selection("after", "变更后", choices, choices.Last().Id, false);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                var before = versions.First(record => record.Revision.ToString(CultureInfo.InvariantCulture) == form.Value("before"));
                var after = versions.First(record => record.Revision.ToString(CultureInfo.InvariantCulture) == form.Value("after"));
                var text = new StringBuilder().AppendLine("记录 " + item.Id + " · 版本 " + before.Revision + " → " + after.Revision);
                var common = new[] { Spec("Number", "编号"), Spec("Title", "标题"), Spec("Stage", "阶段"), Spec("Status", "状态"), Spec("Description", "内容"),
                    Spec("SystemId", "系统"), Spec("EquipmentId", "设备") };
                foreach (var field in common.Concat(FieldsFor(item.GetType())))
                {
                    string left = FieldValue(before, field.Key), right = FieldValue(after, field.Key);
                    if (left != right) text.AppendLine(field.Label + Environment.NewLine + "  前  " + left + Environment.NewLine + "  后  " + right);
                }
                if (Json.ToJson(before.Source) != Json.ToJson(after.Source))
                {
                    text.AppendLine("来源变更");
                    text.AppendLine(before.Source.Document + " · " + before.Source.Version + " · " + before.Source.Location + " · " + before.Source.Sha256);
                    text.AppendLine(after.Source.Document + " · " + after.Source.Version + " · " + after.Source.Location + " · " + after.Source.Sha256);
                }
                var beforeCase = before as TestCase; var afterCase = after as TestCase;
                if (beforeCase != null && Json.ToJson(beforeCase.Steps) != Json.ToJson(afterCase.Steps))
                    text.AppendLine("步骤版本有变更。前 " + beforeCase.Steps.Count + " 步 · 后 " + afterCase.Steps.Count + " 步。");
                details.Text = text.ToString();
            }
        }

        private void OpenSource()
        {
            var item = SelectedRecord;
            if (item == null || item.Source == null || string.IsNullOrWhiteSpace(item.Source.Document))
            { status.Text = "该记录没有原始文件。"; return; }
            Run("核对并定位来源", token => ValidationImport.Locate(project, item.Source, token), lines =>
            {
                details.Text = item.Source.Document + " · " + item.Source.Location + "\r\nSHA-256 " + item.Source.Sha256 +
                    "\r\n\r\n" + string.Join("\r\n", lines);
            });
        }

        private void TryUi(Action action)
        {
            try { action(); }
            catch (Exception error) { status.Text = error.Message; }
        }

        private static readonly Dictionary<string, string> LinkLabels = new Dictionary<string, string>
        {
            { "derives", "需求派生" }, { "design", "设计引用" }, { "risk", "风险" }, { "verifies", "验证" }, { "tag", "Tag" },
            { "evidence", "证据" }, { "deviation", "偏差" }, { "retest", "重新测试" }, { "capa", "CAPA" }, { "impact", "变更影响" },
            { "document", "文档" }, { "task", "项目任务" }, { "incident", "现场事件" }
        };

        private static string LinkName(string kind)
        {
            string text;
            return kind != null && LinkLabels.TryGetValue(kind, out text) ? text : kind;
        }

        private static readonly Dictionary<string, string> StatusLabels = new Dictionary<string, string>
        {
            {"draft","草稿"}, {"planned","已计划"}, {"in-progress","进行中"}, {"complete","已完成"}, {"submitted","待审核"},
            {"not-executed","未执行"}, {"running","执行中"}, {"passed","通过"}, {"failed","失败"}, {"blocked","阻塞"},
            {"not-applicable","不适用"}, {"withdrawn","已撤回"}, {"new","新建"}, {"assessing","待评估"},
            {"investigating","调查中"}, {"handling","待处理"}, {"retest-pending","待重新测试"}, {"review-pending","待审核"},
            {"closed","已关闭"}, {"approved","已审核通过"}, {"rejected","审核退回"}, {"pending","待审核"},
            {"true","是"}, {"false","否"}
        };

        private static string StatusLabel(string key)
        {
            string value;
            return key != null && StatusLabels.TryGetValue(key, out value) ? value : key ?? "";
        }

        private void AppendSpecial(StringBuilder text, ValidationRecord record)
        {
            var test = record as TestCase;
            if (test != null)
                foreach (var step in test.Steps.OrderBy(item => item.Order))
                    text.AppendLine(step.Order + " · " + step.Action + " · 预期 " + step.ExpectedResult);
            var execution = record as TestExecution;
            if (execution != null)
            {
                text.AppendLine("所执行用例  " + execution.TestCaseId + " · 规格版本 " + execution.TestCaseRevision);
                text.AppendLine("执行记录  " + execution.PerformedBy + " · " + FormatTime(execution.PerformedAt));
                text.AppendLine("人工实际结果  " + execution.ResultText);
                if (!string.IsNullOrWhiteSpace(execution.RetestOfId)) text.AppendLine("重新测试来源  " + execution.RetestOfId);
                foreach (var step in execution.StepResults)
                    text.AppendLine(step.StepId + " · " + StatusLabel(step.Outcome) + " · 实际 " + step.Actual);
            }
            var evidence = record as Evidence;
            if (evidence != null)
            {
                text.AppendLine("SHA-256  " + evidence.Sha256 + " · " + evidence.Size + " 字节");
                text.AppendLine("测试用例  " + evidence.TestCaseId);
                text.AppendLine("执行  " + evidence.ExecutionId);
                if (!string.IsNullOrWhiteSpace(evidence.ParentEvidenceId)) text.AppendLine("衍生自  " + evidence.ParentEvidenceId);
            }
            var impact = record as ChangeImpact;
            if (impact != null) AppendImpact(text, impact);
            var baseline = record as ValidationBaseline;
            if (baseline != null)
            {
                text.AppendLine("记录 " + baseline.Records.Count + " · Tag 版本 " + baseline.TagRevisions.Count + " · 无效关联 " + baseline.InvalidLinkIds.Count);
                foreach (var reference in baseline.Records.Concat(baseline.TagRevisions).Take(100))
                    text.AppendLine(reference.Kind + " · " + reference.Number + " · v" + reference.Revision + " · " + reference.Id);
            }
        }
    }
}
