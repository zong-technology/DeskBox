using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Engineering;
using DeskBox.Engineering.Tia;

namespace DeskBox.UI.Views
{
    public sealed class McpCenterView : ViewBase
    {
        private readonly Func<ProjectContext> currentProject;
        private readonly CheckBox enabled, offline;
        private readonly DarkButton probe;
        private readonly DarkList tools;
        private readonly TextBox details;
        private CancellationTokenSource operation;

        public McpCenterView(Store store, Func<ProjectContext> context) : base(store, "MCP 管理", "DeskBox TIA Openness · 本机 stdio · MCP 2025-06-18")
        {
            currentProject = context;
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            enabled = new CheckBox { Text = "启用本机服务", AutoSize = true, Padding = new Padding(6, 8, 8, 6) };
            offline = new CheckBox { Text = "允许经审批的离线编译与新增", AutoSize = true, Padding = new Padding(6, 8, 8, 6) };
            var save = new DarkButton("保存策略") { Width = 105, Height = 34, Palette = Palette };
            save.Click += delegate
            {
                try
                {
                    new TiaMcpPolicy { Enabled = enabled.Checked, OfflineEngineeringEnabled = offline.Checked }.Save();
                    OnActivated();
                    details.AppendText("\r\n策略已保存。后续运行会重新检查策略和现有作业审批。");
                }
                catch (Exception error) { details.Text = "保存失败：" + error.Message; }
            };
            probe = new DarkButton("检测 MCP 协议") { Width = 145, Height = 34, Primary = true, Palette = Palette };
            probe.Click += Probe;
            toolbar.Controls.AddRange(new Control[] { enabled, offline, save, probe });

            tools = new DarkList { Dock = DockStyle.Fill, Palette = Palette, AutoFitColumns = true, RowHeight = 40 };
            tools.SetColumns(new[] { "一作业一工具", "能力", "生效策略" }, new[] { 410, 230, 126 });
            details = new TextBox
            {
                Name = "mcp-actual-receipt", Dock = DockStyle.Bottom, Height = 176, Multiline = true, ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical,
                ForeColor = Palette.Text, BackColor = Palette.CardBg, Font = UiKit.CachedFont("Microsoft YaHei UI", 10, FontStyle.Regular)
            };
            Body.Controls.Add(tools);
            Body.Controls.Add(details);
            Body.Controls.Add(toolbar);
        }

        public override void ApplyPalette(Palette palette)
        {
            base.ApplyPalette(palette);
            if (tools == null) return;
            tools.Palette = palette;
            details.ForeColor = palette.Text;
            details.BackColor = palette.CardBg;
            foreach (var control in new[] { enabled, offline }) control.ForeColor = palette.Text;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            var policy = TiaMcpPolicy.Load();
            enabled.Checked = policy.Enabled;
            offline.Checked = policy.OfflineEngineeringEnabled;
            probe.Enabled = policy.Enabled && operation == null;
            tools.SetRows(TiaMcp.Capabilities.Select(tool =>
            {
                bool restricted = TiaMcp.RequiresOfflineApproval(tool.Key);
                return new ListRow { Cells = new[]
                {
                    tool.Value, restricted ? "离线副本 · 人工审批" : "只读 · 由当前作业授权",
                    policy.Enabled && (!restricted || policy.OfflineEngineeringEnabled) ? "可申请" : "已禁用"
                } };
            }).ToList());
            RefreshReceipt();
        }

        internal void RefreshReceipt()
        {
            var project = currentProject();
            try
            {
                var root = TiaOpenness.JobRoot(project);
                var audit = Directory.Exists(root) ? Directory.EnumerateDirectories(root).Select(path => Path.Combine(path, "mcp-audit.jsonl"))
                    .Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (audit == null)
                {
                    details.Text = "当前范围还没有 MCP 运行回执。检测协议后可查看真实响应。";
                    return;
                }
                var data = Json.ReadLines<Dictionary<string, object>>(audit, 1).FirstOrDefault();
                if (data == null) throw new InvalidDataException("MCP 运行记录无法读取。");
                details.Text = "最近一次 MCP 响应   " + data["utc"] + "\r\n" + data["tool"] + " · " + data["step"] +
                    " · " + data["elapsedMs"] + " ms\r\n" + data["status"] + "\r\n" + audit;
            }
            catch (Exception error) { details.Text = "无法读取 MCP 回执：" + error.Message; }
        }

        internal void ProjectChanged()
        {
            if (operation != null) operation.Cancel();
            RefreshReceipt();
        }

        private async void Probe(object sender, EventArgs args)
        {
            if (operation != null) return;
            var project = currentProject();
            var active = new CancellationTokenSource();
            operation = active;
            probe.Enabled = false;
            details.Text = "启动私有 MCP 进程并协商工具列表…";
            try
            {
                await Task.Run(() => TiaOpenness.ProbeMcp(project, active.Token), active.Token);
                if (!IsDisposed && !active.IsCancellationRequested) RefreshReceipt();
            }
            catch (Exception error)
            {
                if (!IsDisposed && !active.IsCancellationRequested) details.Text = "MCP 检测失败：" + error.Message;
            }
            finally
            {
                operation = null;
                active.Dispose();
                if (!IsDisposed) probe.Enabled = TiaMcpPolicy.Load().Enabled;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && operation != null) operation.Cancel();
            base.Dispose(disposing);
        }
    }
}
