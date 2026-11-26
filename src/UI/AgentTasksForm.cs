using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Domain;

namespace DeskBox.UI
{
    internal sealed class AgentTasksForm : Form
    {
        private readonly ChatService chat;
        private readonly ListBox runs;
        private readonly TextBox details;
        private readonly Label status;
        private readonly Button refresh, stop;
        private bool loading;
        private readonly string openedProject, openedEnvironment;

        internal AgentTasksForm(ChatService chat)
        {
            this.chat = chat;
            openedProject = ProjectId;
            openedEnvironment = AiEnvironment.Mode;
            Name = "agent-tasks-dialog";
            Text = "Agent 执行记录";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 500);
            MinimumSize = new Size(480, 340);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
            refresh = new Button { Text = "刷新", AutoSize = true, Name = "agent-tasks-refresh" };
            stop = new Button { Text = "停止所选任务", AutoSize = true, Enabled = false };
            refresh.Click += delegate { Reload(); };
            stop.Click += delegate
            {
                var item = runs.SelectedItem as AgentTask;
                if (item == null || item.Id != chat.ActiveTaskId || item.ProjectId != ProjectId) return;
                chat.Cancel();
                status.Text = "已请求取消，运行结束后可刷新。";
                stop.Enabled = false;
            };
            toolbar.Controls.AddRange(new Control[] { refresh, stop });
            status = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), UseMnemonic = false };
            runs = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Name = "agent-tasks-list", HorizontalScrollbar = true };
            runs.Format += delegate(object sender, ListControlConvertEventArgs args)
            {
                var item = (AgentTask)args.ListItem;
                args.Value = When(item.StartedAt) + " · " + State(item.State) + " · " + (item.Mode == "local" ? "本地" : item.Model);
            };
            runs.FormattingEnabled = true;
            runs.SelectedIndexChanged += delegate { ShowRecord(); };
            details = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, Name = "agent-task-receipt" };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(runs);
            split.Panel2.Controls.Add(details);
            Controls.Add(split);
            Controls.Add(status);
            Controls.Add(toolbar);
            Shown += delegate
            {
                split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height / 3);
                chat.Changed += CloseOnScopeChange;
                Reload();
            };
            FormClosed += delegate { chat.Changed -= CloseOnScopeChange; };
        }

        private string ProjectId { get { var project = chat.Project; return project == null ? "" : project.Id; } }

        private void CloseOnScopeChange()
        {
            if (openedProject == ProjectId && openedEnvironment == AiEnvironment.Mode) return;
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(new Action(Close)); }
            catch (InvalidOperationException) { }
        }

        private async void Reload()
        {
            if (loading) return;
            string scope = ProjectId, environment = AiEnvironment.Mode;
            runs.Items.Clear();
            details.Clear();
            loading = true;
            refresh.Enabled = false;
            status.Text = "正在读取当前项目和 AI 环境的执行记录…";
            try
            {
                var items = await Task.Run(() => chat.ListAgentTasks(scope, environment));
                if (IsDisposed) return;
                if (ProjectId != scope || AiEnvironment.Mode != environment)
                {
                    status.Text = "工程或 AI 环境已切换，请刷新。";
                    return;
                }
                runs.Items.AddRange(items.Cast<object>().ToArray());
                status.Text = items.Count == 0 ? "当前范围没有执行记录。" : items.Count + " 条执行记录";
                if (items.Count > 0) runs.SelectedIndex = 0;
            }
            catch (Exception error) { if (!IsDisposed) status.Text = "无法读取执行记录 · " + error.Message; }
            finally
            {
                loading = false;
                if (!IsDisposed) refresh.Enabled = true;
            }
        }

        private void ShowRecord()
        {
            var item = runs.SelectedItem as AgentTask;
            if (item == null || item.ProjectId != ProjectId || item.Environment != AiEnvironment.Mode)
            {
                details.Clear();
                stop.Enabled = false;
                return;
            }
            stop.Enabled = item.State == "running" && item.Id == chat.ActiveTaskId;
            var header = new[] { "任务 " + item.Id, "会话 " + item.SessionId,
                "状态 " + State(item.State) + (item.ErrorKind.Length == 0 ? "" : " · " + item.ErrorKind), "" };
            details.Lines = header.Concat(item.Steps.Select(step => step.Kind + " · " + step.Name + " · " +
                State(step.State) + " · " + step.ElapsedMs + " ms" +
                (step.HttpStatus > 0 ? " · HTTP " + step.HttpStatus : "") +
                (step.Attempts > 0 ? " · " + step.Attempts + " 次请求" : "") +
                (step.InputTokens.HasValue || step.OutputTokens.HasValue
                    ? " · Tokens " + (step.InputTokens.HasValue ? step.InputTokens.ToString() : "未知") + "/" +
                        (step.OutputTokens.HasValue ? step.OutputTokens.ToString() : "未知") : "") +
                (step.Kind == "model.stream" ? " · " + (step.TerminalFrame ? "收到结束帧" : "未收到结束帧") : "") +
                (string.IsNullOrEmpty(step.ErrorKind) ? "" : " · " + step.ErrorKind))).ToArray();
        }

        private static string State(string value)
        {
            switch (value)
            {
                case "running": return "运行中";
                case "succeeded": return "完成";
                case "partial": return "部分完成";
                case "cancelled": return "已取消";
                case "interrupted": return "运行中断";
                case "failed": return "失败";
                default: return value;
            }
        }

        private static string When(long value) { return DateTimeOffset.FromUnixTimeMilliseconds(value).ToLocalTime().ToString("MM-dd HH:mm:ss"); }
    }
}
