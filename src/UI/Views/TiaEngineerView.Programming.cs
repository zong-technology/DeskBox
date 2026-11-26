using System;
using System.Drawing;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed partial class TiaEngineerView
    {
        private TiaProgrammingSource programmingSource;
        private TiaProgrammingTarget programmingTarget;
        private TiaProgrammingTask programmingTask, editorTask, previewTask;
        private RichTextBox programmingProgress;
        private DarkButton isolatedDraft;
        private readonly List<AiMessage> engineeringTurns = new List<AiMessage>();
        private string conversationScope;
        private const string EngineeringChatAction = "处理工程需求";
        private string lastNativeValidation = "", latestProgrammingStage = "";

        private static readonly Tuple<string, TiaProgrammingScope>[] ProgrammingScopes =
        {
            Tuple.Create("生成 SCL 草稿", TiaProgrammingScope.Draft), Tuple.Create("新增 FB", TiaProgrammingScope.NewFB),
            Tuple.Create("新增 FC", TiaProgrammingScope.NewFC), Tuple.Create("新增 DB 草稿", TiaProgrammingScope.NewDB),
            Tuple.Create("新增 UDT 草稿", TiaProgrammingScope.NewUDT), Tuple.Create("已有块的替换草稿", TiaProgrammingScope.EditBlock),
            Tuple.Create("程序分析", TiaProgrammingScope.Analyze), Tuple.Create("变量及 IO 辅助", TiaProgrammingScope.Variables),
            Tuple.Create("硬件建议", TiaProgrammingScope.Hardware)
        };

        private TiaProgrammingStore ProgrammingStore() { return new TiaProgrammingStore(Path.Combine(WorkbenchDataRoot, "tia-programming" + AiEnvironment.Suffix)); }

        private static Tuple<PlcSnapshot, TiaProgrammingSource> ReadProgrammingExports(string[] inputs, CancellationToken token)
        {
            var files = inputs.Select(PlcEngineering.FullLocalPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string hash = TiaProgrammingSource.ExportFingerprint(files, token);
            var data = PlcEngineering.ReadExports(files, token);
            var source = new TiaProgrammingSource { Name = data.Name, Origin = "导出资料", Files = files, Fingerprint = hash, ReadUtc = DateTime.UtcNow.ToString("O"),
                Cpus = data.Objects.Where(o => !string.IsNullOrWhiteSpace(o.Controller)).Select(o => o.Controller).Distinct().OrderBy(x => x).ToArray() };
            source.Verify(token);
            return Tuple.Create(data, source);
        }

        private void SetNativeProgrammingSource(string file, TiaResult result)
        {
            programmingSource = new TiaProgrammingSource { Name = result.Snapshot.Name, Origin = "原生工程", Version = "V20 · " + result.OpennessVersion,
                Native = true, Files = new[] { PlcEngineering.FullLocalPath(file) }, Cpus = result.Cpus.ToArray(), Copy = result.Copy,
                Fingerprint = result.Saved ? result.SavedCopyHash : result.SourceHash, ReadUtc = DateTime.UtcNow.ToString("O") };
        }

        private void InvalidateProgrammingTarget(string reason)
        {
            ResetEngineeringConversation();
            if (programmingTarget == null) return;
            programmingTarget = null;
            if (programmingTask != null && programmingTask.Target != null && programmingTask.State == TiaProgrammingState.AWAITING_REVIEW.ToString())
            {
                var store = ProgrammingStore();
                store.Step(programmingTask, TiaProgrammingState.BLOCKED, "旧目标确认已失效，草稿仍可打开。", "重新绑定核对后提交新任务。", reason);
                ShowProgrammingTask(store, programmingTask);
            }
            RefreshHeading();
            UpdateButtons();
        }

        private void BindEngineeringTarget()
        {
            if (programmingSource != null && snapshot != null) { ConfirmTargetSelection(); return; }
            PickBindingSource();
        }

        private void PickBindingSource()
        {
            using (var choose = ReviewWindow("绑定工程 · 选择资料", 650, 265))
            {
                var instructions = Caption("已打开的 V20 工程可先只读发现。需要离线编程时，选择已关闭的独立 .ap20 工程。\r\n也可读取工程导出资料后绑定目标 PLC。");
                instructions.Dock = DockStyle.Fill;
                instructions.AutoEllipsis = false;
                instructions.Padding = new Padding(13);
                var choices = Bar();
                choices.Dock = DockStyle.Bottom;
                var native = new Button { AutoSize = true, Text = "V20 工程或归档", DialogResult = DialogResult.Yes };
                var exports = new Button { AutoSize = true, Text = "工程导出资料", DialogResult = DialogResult.No };
                var discover = new Button { AutoSize = true, Text = "发现打开的工程", DialogResult = DialogResult.OK };
                var close = new Button { AutoSize = true, Text = "返回", DialogResult = DialogResult.Cancel };
                choices.Controls.AddRange(new Control[] { discover, native, exports, close });
                choose.Controls.AddRange(new Control[] { instructions, choices });
                choose.CancelButton = close;
                var result = choose.ShowDialog(FindForm());
                if (result == DialogResult.OK) DiscoverOpenProjects(false);
                if (result == DialogResult.Yes) OpenProgrammingProject(true);
                if (result == DialogResult.No)
                {
                    var files = SelectExports();
                    if (files != null) LoadExports(files, false, true);
                }
            }
        }

        private void ConfirmTargetSelection()
        {
            var source = programmingSource;
            if (source == null || snapshot == null) return;
            using (var dialog = ReviewWindow("绑定工程 · PLC、对象与能力", 815, 650))
            {
                var choices = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 4, AutoSize = true, Padding = new Padding(8) };
                choices.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TiaWorkbenchStyle.Pixels(this, 118)));
                choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var cpuChoice = BindingChoice("绑定目标 PLC");
                cpuChoice.Name = "tia-bind-cpu";
                cpuChoice.Items.Add(source.Cpus.Length == 0 ? "资料没有可确认的 PLC" : "请选择目标 PLC");
                cpuChoice.Items.AddRange(source.Cpus.Cast<object>().ToArray());
                cpuChoice.SelectedIndex = programmingTarget != null ? Math.Max(0, cpuChoice.Items.IndexOf(programmingTarget.Cpu)) : source.Cpus.Length == 1 ? 1 : 0;
                var scopeChoice = BindingChoice("编程范围");
                scopeChoice.Name = "tia-bind-scope";
                scopeChoice.Items.AddRange(ProgrammingScopes.Select(s => s.Item1).Cast<object>().ToArray());
                scopeChoice.SelectedIndex = 0;
                var itemChoice = BindingChoice("目标程序块、变量表或设备");
                itemChoice.Name = "tia-bind-object";
                var test = new CheckBox { Text = "标记为测试资料", AutoSize = true, Visible = !source.Native, ForeColor = Palette.Text };
                bool allowContext = Store.Settings.AiFileAccess;
                var share = new CheckBox { Text = "AI 任务可引用所选工程上下文", AutoSize = true, Checked = allowContext, ForeColor = Palette.Text };
                var settings = Bar();
                settings.Controls.Add(test);
                if (allowContext) settings.Controls.Add(share);
                else settings.Controls.Add(new Label { Text = "工程上下文须先在 AI 设置授权", AutoSize = true, ForeColor = Palette.TextDim });
                choices.Controls.Add(Caption("目标 PLC"), 0, 0); choices.Controls.Add(cpuChoice, 1, 0);
                choices.Controls.Add(Caption("编程范围"), 0, 1); choices.Controls.Add(scopeChoice, 1, 1);
                choices.Controls.Add(Caption("选定对象"), 0, 2); choices.Controls.Add(itemChoice, 1, 2);
                choices.Controls.Add(settings, 1, 3);

                var body = TextArea("绑定目标与可用能力");
                body.BackColor = Palette.CardBg; body.ForeColor = Palette.Text;
                var actions = Bar(); actions.Dock = DockStyle.Bottom;
                var confirm = new Button { Text = "确认绑定", AutoSize = true, DialogResult = DialogResult.Yes, AccessibleName = "确认绑定" };
                var pick = new Button { Text = "更换资料", AutoSize = true, DialogResult = DialogResult.Retry };
                var close = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
                actions.Controls.AddRange(new Control[] { confirm, pick, close });
                dialog.Controls.AddRange(new Control[] { body, actions, choices });
                dialog.CancelButton = close;
                TiaProgrammingTarget candidate = null;
                PlcObject[] objects = new PlcObject[0];
                Action update = delegate
                {
                    try
                    {
                        candidate = TiaProgrammingTarget.Bind(source, snapshot, cpuChoice.SelectedIndex <= 0 ? "" : (string)cpuChoice.SelectedItem,
                            ProgrammingScopes[scopeChoice.SelectedIndex].Item2, itemChoice.SelectedIndex <= 0 ? null : objects[itemChoice.SelectedIndex - 1], test.Checked);
                        body.Text = TargetSummary(candidate);
                        confirm.Enabled = true;
                    }
                    catch (InvalidOperationException e) { candidate = null; confirm.Enabled = false; body.Text = source.Name + "\r\n" + string.Join("\r\n", source.Files) + "\r\n\r\n" + e.Message; }
                };
                Action refill = delegate
                {
                    string targetCpu = cpuChoice.SelectedIndex <= 0 ? "" : (string)cpuChoice.SelectedItem;
                    objects = snapshot.Objects.Where(o => TiaProgrammingTarget.Allowed(o, targetCpu, ProgrammingScopes[scopeChoice.SelectedIndex].Item2))
                        .OrderBy(o => o.Identity).Take(2000).ToArray();
                    itemChoice.Items.Clear();
                    itemChoice.Items.Add("未选定单一对象");
                    itemChoice.Items.AddRange(objects.Select(o => o.Identity).Cast<object>().ToArray());
                    int current = Array.IndexOf(objects, selected);
                    itemChoice.SelectedIndex = current + 1;
                };
                cpuChoice.SelectedIndexChanged += delegate { refill(); };
                scopeChoice.SelectedIndexChanged += delegate { refill(); };
                itemChoice.SelectedIndexChanged += delegate { update(); };
                test.CheckedChanged += delegate { update(); };
                refill();
                var reply = dialog.ShowDialog(FindForm());
                if (reply == DialogResult.Retry) { PickBindingSource(); return; }
                if (reply != DialogResult.Yes || candidate == null) return;
                InvalidateProgrammingTarget("用户重新确认了工程绑定。");
                programmingTarget = candidate;
                useProjectContext.Checked = share.Checked;
                if (source.Native) cpu.SelectedIndex = Math.Max(0, cpu.Items.IndexOf(candidate.Cpu));
                RefreshHeading(); UpdateButtons();
            }
        }

        private ComboBox BindingChoice(string name)
        {
            var choice = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name, BackColor = Palette.CardBg, ForeColor = Palette.Text };
            FormatCombo(choice);
            return choice;
        }

        private Form ReviewWindow(string title, int width, int height)
        {
            var screen = Screen.FromControl(this).WorkingArea;
            return new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                Size = new Size(Math.Min(screen.Width - 24, TiaWorkbenchStyle.Pixels(this, width)), Math.Min(screen.Height - 24, TiaWorkbenchStyle.Pixels(this, height))),
                BackColor = Palette.Bg, ForeColor = Palette.Text, Font = UiKit.CachedFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular) };
        }

        private static string Known(string text) { return string.IsNullOrWhiteSpace(text) ? "未确认" : text; }

        private string TargetSummary(TiaProgrammingTarget target)
        {
            if (target == null) return "当前未确认编程目标。可以生成通用草稿，或从顶部选择「绑定工程」。";
            var source = target.Source;
            return source.Name + "  ·  " + source.Origin + "\r\nTIA 版本  " + Known(source.Version) + "\r\n工程完整路径\r\n" + string.Join("\r\n", source.Files) +
                "\r\n\r\nPLC  " + Known(target.Cpu) + "\r\n设备  " + Known(target.Device) + "\r\n型号  " + Known(target.Model) +
                "\r\n订货号  " + Known(target.OrderNumber) + "  ·  固件  " + Known(target.Firmware) +
                "\r\n选定对象  " + Known(target.ObjectIdentity) + "\r\n允许范围  " + ScopeLabel(target) +
                "\r\n工程状态  " + (source.Native ? "已读取独立离线副本 · 原始工程只读" : "已读取 · 只读资料 · 可编辑本机草稿") +
                (source.Copy.Length == 0 ? "" : "\r\n离线副本  " + source.Copy) +
                "\r\n最近读取 UTC  " + Known(source.ReadUtc) + "\r\n最近原生验证 UTC  " + Known(lastNativeValidation) +
                "\r\n\r\n读取、分析和本机 SCL 草稿可用。" +
                (target.CanTryInsert() ? "可在授权后尝试新增单个 ASCII FC/FB 并编译独立副本。" : target.CanTryCompile() ? "可在授权后尝试离线编译。当前范围没有块写入入口。" : "当前来源没有 TIA 写入或编译入口。");
        }

        private static string ScopeLabel(TiaProgrammingTarget target)
        {
            var match = ProgrammingScopes.FirstOrDefault(s => s.Item2.ToString() == target.Scope);
            return match == null ? target.Scope : match.Item1;
        }

        private void ViewProgrammingTarget()
        {
            using (var dialog = ReviewWindow("当前编程目标", 760, 590))
            {
                var details = TextArea("工程绑定证据");
                details.Text = TargetSummary(programmingTarget);
                details.BackColor = Palette.CardBg; details.ForeColor = Palette.Text;
                dialog.Controls.Add(details);
                dialog.ShowDialog(FindForm());
            }
        }

        private static string TaskStateLabel(string state)
        {
            switch (state)
            {
                case "WAITING_FOR_PROJECT": return "等待核对目标";
                case "READING_PROJECT": return "核对工程版本";
                case "ANALYZING_CONTEXT": return "分析工程上下文";
                case "GENERATING_CODE": return "等待模型回复";
                case "STATIC_CHECK": return "静态检查";
                case "AWAITING_REVIEW": return "待审查草稿";
                case "APPLYING_TO_COPY": return "执行已审批的离线作业";
                case "COMPILING": return "TIA 编译";
                case "VERIFYING": return "回读核验";
                case "COMPLETED": return "已编译并核验";
                case "PARTIAL_SUCCESS": return "副本已保存 · 核验未通过";
                case "ANSWERED": return "已回复";
                case "BLOCKED": return "待处理";
                case "FAILED": return "任务失败";
                case "CANCELLED": return "任务已取消";
                default: return state;
            }
        }

        private static string ProgrammingTaskSummary(TiaProgrammingStore store, TiaProgrammingTask task)
        {
            return TaskStateLabel(task.State) + "\r\n" + (task.Next.Length == 0 ? "" : task.Next + "\r\n") +
                "目标 " + (task.Target == null ? "通用草稿" : task.Target.Source.Name + " / " + Known(task.Target.Cpu)) +
                "\r\n任务 " + task.Id + "\r\n\r\n" + string.Join("\r\n\r\n", task.Steps.AsEnumerable().Reverse().Select(s =>
                    TaskStateLabel(s.State) + "  ·  " + s.StartedUtc + "\r\n" + s.Summary +
                    (s.Error.Length == 0 ? "" : "\r\n" + s.Error) + (s.NeedsApproval ? "\r\n需要人工确认" : "") +
                    (s.Outputs.Length == 0 ? "" : "\r\n" + string.Join("\r\n", s.Outputs)))) + "\r\n\r\n本机记录  " + store.Folder(task);
        }

        private void ShowProgrammingTask(TiaProgrammingStore store, TiaProgrammingTask task)
        {
            programmingTask = task;
            latestProgrammingStage = TaskStateLabel(task.State);
            programmingProgress.Text = ProgrammingTaskSummary(store, task);
            SetConversationSteps(RecentProgrammingSteps(task));
            UpdateAssistantStage();
        }

        private static string RecentProgrammingSteps(TiaProgrammingTask task)
        {
            return string.Join("  →  ", task.Steps.Skip(Math.Max(0, task.Steps.Count - 3)).Select(step => TaskStateLabel(step.State)));
        }

        private void UpdateAssistantStage()
        {
            if (assistantStage == null) return;
            bool generating = activeWorkbenchAction == EngineeringChatAction;
            string text = (generating || activeWorkbenchAction == null) && latestProgrammingStage.Length != 0
                ? latestProgrammingStage : activeWorkbenchAction ?? "等待工程需求";
            assistantStage.Text = text;
            assistantStage.ForeColor = phase == Phase.Failed ? TiaWorkbenchStyle.Error(Palette) :
                phase == Phase.WaitingForInput || phase == Phase.WaitingForApproval ? TiaWorkbenchStyle.Warning(Palette) :
                operation == null ? Palette.TextDim : Palette.Accent;
            workbenchTips.SetToolTip(assistantStage, text);
            UpdateComposerStatus();
        }

        private void OpenProgrammingHistory()
        {
            var store = ProgrammingStore();
            var tasks = store.Recent();
            using (var dialog = ReviewWindow("可恢复的 AI 编程任务", 935, 650))
            {
                var list = BindingChoice("历史编程任务");
                var details = TextArea("任务记录与产出");
                details.BackColor = Palette.CardBg; details.ForeColor = Palette.Text;
                var buttons = Bar(); buttons.Dock = DockStyle.Bottom;
                var restore = new Button { Text = "核对来源并恢复草稿", AutoSize = true, DialogResult = DialogResult.Yes, Enabled = false };
                buttons.Controls.Add(restore);
                list.Items.AddRange(tasks.Cast<object>().ToArray());
                list.SelectedIndexChanged += delegate
                {
                    var item = list.SelectedItem as TiaProgrammingTask;
                    if (item == null) return;
                    restore.Enabled = true;
                    details.Text = ProgrammingTaskSummary(store, item);
                };
                if (list.Items.Count != 0) list.SelectedIndex = 0;
                else details.Text = "当前 AI 环境暂无编程任务。";
                dialog.Controls.AddRange(new Control[] { details, buttons, list });
                if (dialog.ShowDialog(FindForm()) != DialogResult.Yes || list.SelectedItem == null) return;
                var task = (TiaProgrammingTask)list.SelectedItem;
                var confirmed = programmingTarget;
                Work("重新核对编程任务目标", token =>
                {
                    if (task.Target != null)
                    {
                        if (!task.Target.SameSelection(confirmed)) throw new InvalidOperationException("历史任务的工程路径、版本、PLC 或范围与当前绑定不一致。请重绑正确目标；原草稿文件仍保留。");
                        task.Target.Source.Verify(token);
                    }
                    return task.DraftFile.Length == 0 ? null : store.ReadDraft(task);
                }, source =>
                {
                    task.Historical = true;
                    if (new[] { "WAITING_FOR_PROJECT", "READING_PROJECT", "ANALYZING_CONTEXT", "GENERATING_CODE", "STATIC_CHECK", "APPLYING_TO_COPY", "COMPILING", "VERIFYING" }.Contains(task.State))
                        store.Step(task, TiaProgrammingState.CANCELLED, "识别到上次执行未结束的记录。", "保留已有产出，从当前已核对的目标重新提交。");
                    ShowProgrammingTask(store, task);
                    if (source == null) { requirement.Text = task.Requirement; FocusRequirement(); return; }
                    if (mode.SelectedIndex == 0) mode.SelectedIndex = 1;
                    if (editor.TextLength == 0 || SameSource(editor.Text, source)) LoadScl(source, owner: task);
                    else ShowDraftPreview(source, owner: task);
                }, keepPage: true);
            }
        }

        private bool AllowsNativeCompilation(bool add)
        {
            var target = programmingTarget;
            if (target == null || !target.CanTryCompile() || inspected == null || cpu.SelectedIndex <= 0 ||
                target.Cpu != Convert.ToString(cpu.SelectedItem) || target.Source.Fingerprint != NativeFingerprint() ||
                !string.Equals(target.Source.Files[0], apFile, StringComparison.OrdinalIgnoreCase)) return false;
            if (!add) return true;
            return target.CanTryInsert() && (editorTask == null || !editorTask.Isolated && !editorTask.Historical && editorTask.Target != null &&
                editorTask.Target.BindingId == target.BindingId && editorTask.State == TiaProgrammingState.AWAITING_REVIEW.ToString() &&
                editorTask.EditorHash == TiaProgrammingTask.HashEditorText(editor.Text));
        }

        private void ResetEngineeringConversation()
        {
            engineeringTurns.Clear();
            conversationScope = null;
            submittedRequirement = null;
            if (conversation != null) conversation.Text = EmptyEngineeringConversation;
            SetConversationSteps("");
        }

        private void SubmitRequirement(string prompt, bool requestIsolation = false, bool consumeInput = true)
        {
            if (operation != null || string.IsNullOrWhiteSpace(prompt)) return;
            var source = snapshot;
            var target = requestIsolation ? null : programmingTarget;
            var store = ProgrammingStore();
            bool useContext = target != null && useProjectContext.Checked && Store.Settings.AiFileAccess;
            var task = store.Start(prompt, target, !useContext);
            store.Step(task, TiaProgrammingState.WAITING_FOR_PROJECT, target == null ? "未确认工程目标。本任务仅生成通用隔离草稿。" :
                "已固定工程、PLC 和编程范围，等待核对版本。", "生成前核对本次任务的资料和权限。");
            ShowProgrammingTask(store, task);

            if (target != null && new[] { "Analyze", "Variables", "Hardware" }.Contains(target.Scope))
            {
                StopBeforeGeneration(store, task, "当前绑定为只读分析范围。", "使用对应分析页，或重新绑定为 SCL 草稿范围。");
                return;
            }
            if (target != null && target.Scope == "EditBlock" && !useContext)
            {
                StopBeforeGeneration(store, task, "替换草稿需要引用目标块资料。", "授权本次工程上下文，或选择「生成隔离草稿」。");
                return;
            }
            var provider = providers == null ? null : providers.DefaultProvider;
            if (provider == null || !provider.Enabled)
            {
                StopBeforeGeneration(store, task, "当前环境未配置默认 AI 服务商。", "配置模型后重生成，也可在本机载入缩放模板。");
                return;
            }
            if (projectScope != null && !projectScope.PermitsModel(provider))
            {
                StopBeforeGeneration(store, task, "当前工程只允许本机模型。", "选择本机模型或核对项目的外部模型权限。");
                return;
            }
            string authorization = useContext ? "已绑定 PLC 的对象、来源与可用代码。此操作只生成草稿。" : "";
            string scope = string.Join("\n", new[] { AiEnvironment.Suffix, provider.Id, provider.BaseUrl, provider.Protocol, provider.Model, provider.UpdatedAt.ToString(),
                target == null ? "" : target.BindingId, useContext.ToString() });
            var history = scope == conversationScope ? engineeringTurns.ToArray() : new AiMessage[0];
            var model = ApproveModel(authorization, useContext, history.Length != 0);
            if (model == null)
            {
                store.Step(task, TiaProgrammingState.CANCELLED, "用户未批准发送需求。", "可重新提交。");
                ShowProgrammingTask(store, task);
                SetPhase(Phase.Cancelled);
                return;
            }
            if (scope != conversationScope) { ResetEngineeringConversation(); ShowProgrammingTask(store, task); }
            conversationScope = scope;
            submittedRequirement = prompt;
            if (consumeInput && requirement.Text == prompt) requirement.Clear();
            AddConversation("工程需求", prompt);
            assistantTabs.SelectedIndex = 2;
            if (!compact && !Store.Settings.TiaAssistantCollapsed) assistantPane.Visible = true;
            if (mode.SelectedIndex == 0) mode.SelectedIndex = 1;
            string before = editor.Text;
            var progress = new Progress<Tuple<string, string, string, string>>(update =>
            {
                if (IsDisposed || programmingTask != task || operation == null || operation.IsCancellationRequested) return;
                latestProgrammingStage = update.Item1;
                programmingProgress.Text = update.Item2;
                assistant.Text = update.Item3;
                SetConversationSteps(update.Item4);
                UpdateAssistantStage();
            });
            Work(EngineeringChatAction, token => GenerateProgrammingDraft(store, task, source, model, before, history, progress, token), result =>
            {
                var draft = result.Item1;
                var review = result.Item2;
                ShowProgrammingTask(store, task);
                lastAiAnalysis = draft.Plan();
                assistant.Text = draft.Kind == "ANSWER" ? lastAiAnalysis :
                    (review == null ? "待确认的工程条件" : "已在本机保存 " + task.DraftFile) + "\r\n" + task.Next + "\r\n\r\n" + lastAiAnalysis;
                AddConversation("AI 工程师", assistant.Text);
                engineeringTurns.Add(new AiMessage { Role = "user", Content = prompt });
                engineeringTurns.Add(new AiMessage { Role = "assistant", Content = Limit(draft.Plan(), 2800) +
                    (draft.Scl.Length == 0 ? "" : "\r\n\r\nSCL 草稿\r\n" + Limit(draft.Scl, 6000)) });
                if (engineeringTurns.Count > 6) engineeringTurns.RemoveRange(0, 2);
                if (review == null) return;
                bool typing = requirement.Focused;
                if (editor.TextLength == 0 || SameSource(editor.Text, draft.Scl)) LoadScl(draft.Scl, draft.Name, task);
                else ShowDraftPreview(draft.Scl, draft.Name, task);
                if (typing) requirement.Focus();
                output.Text = "任务 " + task.Id + " · 本机源码 " + Path.Combine(store.Folder(task), task.DraftFile) + "\r\n\r\n" + DraftReview(review);
                if (pendingScl == null)
                {
                    verifiedDraft = editor.Text;
                    verificationStatic = review.Diagnostics.Count == 0 ? "已检查，当前规则未发现问题" : review.Diagnostics.Count + " 项诊断待复核";
                    SetDiagnostics(review.Diagnostics, editor.Text);
                }
            }, keepPage: true, running: Phase.Executing, outcome: result => result.Item1.Kind == "ANSWER" ? Phase.Completed :
                task.State == TiaProgrammingState.BLOCKED.ToString() ? Phase.WaitingForInput : Phase.WaitingForApproval,
                ended: error =>
                {
                    RecordProgrammingError(store, task, error, "检查错误后重试，已写出的成果保留在任务目录。");
                    if (IsDisposed || conversationScope != scope || draftEnvironment != AiEnvironment.Suffix) return;
                    bool cancelled = error is OperationCanceledException;
                    AddConversation(cancelled ? "任务已取消" : "任务未完成", cancelled ? "可从「更多操作」重新提交。" : error.Message);
                });
            if (consumeInput) requirement.Focus();
        }

        private void RecordProgrammingError(TiaProgrammingStore store, TiaProgrammingTask task, Exception error, string next)
        {
            var state = error is OperationCanceledException ? TiaProgrammingState.CANCELLED :
                task.State == "READING_PROJECT" || task.State == "APPLYING_TO_COPY" && error is InvalidOperationException ? TiaProgrammingState.BLOCKED : TiaProgrammingState.FAILED;
            try
            {
                store.Step(task, state, "本次任务已停止。", next, error.Message);
                if (!IsDisposed && programmingTask == task) ShowProgrammingTask(store, task);
            }
            catch (IOException storageError) { Store.Log("warn", "engineering", "无法更新任务 " + task.Id + " · " + storageError.Message); }
            catch (UnauthorizedAccessException storageError) { Store.Log("warn", "engineering", "无法更新任务 " + task.Id + " · " + storageError.Message); }
        }

        private void RecordNativeProgrammingResult(TiaProgrammingStore store, TiaProgrammingTask task, TiaResult result)
        {
            var receipts = new[] { result.Copy, result.Copy.Length == 0 ? "" : RecoveryReceiptPath(result), result.McpAudit }
                .Where(path => path.Length != 0).ToArray();
            if (result.CompileStartedUtc.Length != 0)
                store.Step(task, TiaProgrammingState.COMPILING, "Siemens 编译调用 UTC " + result.CompileStartedUtc + " 至 " + result.CompileFinishedUtc,
                    "按真实回执核对编译和保存结果。", result.Error, outputs: receipts);
            if (result.VerifyStartedUtc.Length != 0)
                store.Step(task, TiaProgrammingState.VERIFYING, "Siemens 回读开始 UTC " + result.VerifyStartedUtc,
                    "核对目标块与保存后的副本。", outputs: receipts);
            bool saved = result.Compiled && result.Saved && result.Verified && result.Error.Length == 0;
            store.Step(task, saved ? TiaProgrammingState.COMPLETED : result.Saved ? TiaProgrammingState.PARTIAL_SUCCESS : TiaProgrammingState.FAILED,
                saved ? "Siemens 回执确认独立副本已编译、保存并重新打开核验。" : "Siemens 回执没有确认编译并保存。",
                "核对本机作业回执和保留的草稿。", saved ? "" : result.Error.Length == 0 ? result.CompileState : result.Error, outputs: receipts);
            if (programmingTask == task) ShowProgrammingTask(store, task);
        }

        private void StopBeforeGeneration(TiaProgrammingStore store, TiaProgrammingTask task, string reason, string next)
        {
            store.Step(task, TiaProgrammingState.BLOCKED, "尚未调用模型。", next, reason);
            ShowProgrammingTask(store, task);
            SetPhase(Phase.WaitingForInput);
            assistant.Text = reason + "\r\n" + next;
            AddConversation("任务未发送", assistant.Text);
            assistantTabs.SelectedIndex = 2;
            requirement.Focus();
        }

        private static Tuple<PlcDraft, PlcSnapshot> GenerateProgrammingDraft(TiaProgrammingStore store, TiaProgrammingTask task, PlcSnapshot snapshot,
            AiProvider model, string earlier, AiMessage[] history, IProgress<Tuple<string, string, string, string>> progress, CancellationToken token)
        {
            var target = task.Target;
            Action<string> report = summary => progress.Report(Tuple.Create(TaskStateLabel(task.State), ProgrammingTaskSummary(store, task), summary, RecentProgrammingSteps(task)));
            Action<TiaProgrammingState, string, string> step = (state, note, next) =>
            {
                store.Step(task, state, note, next);
                report(note);
            };
            string context = "";
            if (target != null)
            {
                step(TiaProgrammingState.READING_PROJECT, "核对已绑定的原始路径和内容哈希。", "按目标 PLC 汇总可用对象。");
                target.Source.Verify(token);
                step(TiaProgrammingState.ANALYZING_CONTEXT, task.Isolated ? "工程版本已核对。本次使用隔离草稿模式。" : "筛选目标 PLC 和显式绑定对象。", "生成单块可编辑 SCL。");
                if (!task.Isolated)
                {
                    var scope = (TiaProgrammingScope)Enum.Parse(typeof(TiaProgrammingScope), target.Scope);
                    var current = snapshot.Objects.FirstOrDefault(o => target.ObjectIdentity.Length != 0 && o.Identity == target.ObjectIdentity && TiaProgrammingTarget.Allowed(o, target.Cpu, scope));
                    var objects = snapshot.Objects.Where(o => o.Controller == target.Cpu || o == current);
                    if (target.Scope == "EditBlock")
                    {
                        var existing = current == null || current.Language != "SCL" ? null : PlcEngineering.ReviewScl(current.Content, current.Source);
                        if (existing == null || existing.Objects.Count != 1)
                        {
                            var blocked = new PlcDraft { Kind = "PLAN", Purpose = "当前对象缺少可核验的完整 SCL。", MissingParameters = new[] { "授权读取块正文，或导入同一 PLC 的 SCL 源文件。" } };
                            store.Step(task, TiaProgrammingState.BLOCKED, blocked.Purpose, blocked.MissingParameters[0]);
                            return Tuple.Create(blocked, (PlcSnapshot)null);
                        }
                        earlier = current.Content;
                    }
                    context = Limit("绑定目标 · " + target.Source.Name + " / " + target.Cpu + " / " + ScopeLabel(target) + "\n" +
                        (current == null ? "" : current.Identity + "\n来源 " + current.Source + "\n" + current.Interface + "\n" + Limit(current.Content, 8500) + "\n\n") +
                        string.Join("\n", objects.Where(o => o != current).Take(80).Select(o => o.Identity + " · " + o.DataType + " " + o.Address + " · " + o.Source)), 15000);
                }
            }
            step(TiaProgrammingState.GENERATING_CODE, "开始请求模型，最多等待 90 秒。", "校验结构，保存来稿并执行本机静态检查。");
            PlcDraft draft;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(90));
                try { draft = TiaPlanning.Plan(model, task.Requirement, context, deadline.Token, task.Isolated, history); }
                catch (OperationCanceledException) { token.ThrowIfCancellationRequested(); throw new TimeoutException("AI 生成超过 90 秒。请重试或更换模型。"); }
            }
            if (draft.Kind == "ANSWER")
            {
                store.Step(task, TiaProgrammingState.ANSWERED, Limit(draft.Purpose, 4000), "可继续追问，或提出新的编程需求。");
                return Tuple.Create(draft, (PlcSnapshot)null);
            }
            if (draft.Kind == "PLAN")
            {
                store.Step(task, TiaProgrammingState.BLOCKED, "模型未生成源码。", "补充参数，或使用「生成隔离草稿」。",
                    string.Join("\r\n", draft.MissingParameters ?? new string[0]).Trim());
                return Tuple.Create(draft, (PlcSnapshot)null);
            }
            if (task.Isolated && Regex.IsMatch(SclEditing.Mask(draft.Scl, true), @"%\s*[IQEA](?:[BWDX])?(?:\d|\b)|\bAT\s+%", RegexOptions.IgnoreCase))
                throw new InvalidDataException("隔离草稿含有直接现场 I/O 地址。请在需求中明确只使用未映射的输入与输出参数。");
            if (!task.Isolated && target != null && ((target.Scope.StartsWith("New", StringComparison.Ordinal) && target.Scope.Substring(3) != draft.Kind) ||
                target.Scope == "EditBlock" && (!string.Equals(target.ObjectName, draft.Name, StringComparison.OrdinalIgnoreCase) ||
                    new[] { "FB", "FC", "DB", "UDT", "OB" }.Contains(target.ObjectKind) && target.ObjectKind != draft.Kind)))
                throw new InvalidDataException("模型结果的块类型或名称超出已绑定范围。");

            string file = store.Write(task, draft.Name + ".scl", draft.Scl);
            task.DraftFile = Path.GetFileName(file);
            task.DraftHash = PlcEngineering.Hash(draft.Scl);
            task.EditorHash = TiaProgrammingTask.HashEditorText(draft.Scl);
            store.Step(task, TiaProgrammingState.STATIC_CHECK, "已保存可编辑的 SCL 文件。", "执行本机静态检查。", outputs: new[] { file });
            report("已保存可编辑的 SCL 文件，正在静态检查。");
            var review = PlcEngineering.ReviewScl(draft.Scl, task.DraftFile);
            var diffFile = store.Write(task, "review.diff", SclEditing.Diff(earlier, draft.Scl));
            var reportFile = store.Write(task, "static-review.txt", draft.Plan() + "\r\n\r\n" + DraftReview(review));
            bool asksForInput = (draft.MissingParameters ?? new string[0]).Any(x => !string.IsNullOrWhiteSpace(x));
            bool errors = review.Diagnostics.Any(d => d.Severity == "Error");
            bool safetyReview = Regex.IsMatch(task.Requirement, "电机|阀门|联锁|急停|设备保护|安全回路|motor|valve|interlock|emergency[ -]?stop", RegexOptions.IgnoreCase);
            store.Step(task, errors || asksForInput || safetyReview ? TiaProgrammingState.BLOCKED : TiaProgrammingState.AWAITING_REVIEW,
                "本机静态检查完成。" + review.Diagnostics.Count + " 项诊断。未执行工程写入或 TIA 编译。",
                "查看或导出 SCL 草稿，核对差异及人工复核项。", errors ? "草稿存在静态错误。" : asksForInput ? "仍有关键条件待确认。" :
                    safetyReview ? "涉及设备控制或安全条件，需要工程师独立复核。" : "",
                true, file, diffFile, reportFile);
            return Tuple.Create(draft, review);
        }
    }
}
