using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI.Views
{
    public class ProjectProgressForm : Form
    {
        private readonly DataGridView _grid;
        public ProjectProgressForm(WorkspaceStore store, EngineerWorkspace project, Palette palette)
        {
            Text = project.Name + " · 验证进度管理"; Width = Math.Min(1520, Screen.PrimaryScreen.WorkingArea.Width); Height = 820; MinimumSize = new Size(900, 600); StartPosition = FormStartPosition.CenterParent;
            BackColor = palette.Bg; ForeColor = palette.Text; Font = new Font("Microsoft YaHei UI", 9);
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 130, ColumnCount = 4, RowCount = 3, Padding = new Padding(10) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            var root = new TextBox { Text = project.Root, Dock = DockStyle.Fill };
            var browse = new Button { Text = "选择目录", Dock = DockStyle.Fill };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { SelectedPath = root.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) root.Text = dialog.SelectedPath; };
            var start = new TextBox { Text = project.Start, Dock = DockStyle.Fill }; var end = new TextBox { Text = project.Deadline, Dock = DockStyle.Fill };
            var notes = new TextBox { Text = project.Notes, Dock = DockStyle.Fill };
            fields.Controls.Add(new Label { Text = "项目目录", AutoSize = true }, 0, 0); fields.Controls.Add(root, 1, 0); fields.Controls.Add(browse, 2, 0);
            fields.Controls.Add(new Label { Text = "开始日期", AutoSize = true }, 0, 1); fields.Controls.Add(start, 1, 1);
            fields.Controls.Add(new Label { Text = "结束日期", AutoSize = true }, 2, 1); fields.Controls.Add(end, 3, 1);
            fields.Controls.Add(new Label { Text = "项目备注", AutoSize = true }, 0, 2); fields.Controls.Add(notes, 1, 2); fields.SetColumnSpan(notes, 3);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(10), FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存进度", Width = 110, Height = 32 }; var cancel = new Button { Text = "取消", Width = 80, Height = 32, DialogResult = DialogResult.Cancel };
            footer.Controls.Add(save); footer.Controls.Add(cancel); footer.Controls.Add(new Label { AutoSize = true, Text = "进度填 0–100 或 N/A；日期 yyyy-MM-dd；N/A 不计入总进度\r\n项目更新时间：" + (project.UpdatedAt <= 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(project.UpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")) });
            var split = new SplitContainer { Size = new Size(1200, 550), Dock = DockStyle.Fill, SplitterDistance = 620 };
            var flow = new ValidationFlowControl(palette) { Dock = DockStyle.Fill }; flow.SetProject(project); split.Panel1.Controls.Add(flow);
            _grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = palette.CardBg, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.CellSelect };
            _grid.DefaultCellStyle.BackColor = palette.PanelAlt; _grid.DefaultCellStyle.ForeColor = palette.Text; _grid.DefaultCellStyle.SelectionBackColor = palette.Line; _grid.DefaultCellStyle.SelectionForeColor = palette.Text;
            foreach (var name in new[] { "流程节点", "进度 / N/A", "开始日期", "结束日期", "备注", "更新时间", "权重" }) _grid.Columns.Add(name, name);
            _grid.Columns[0].ReadOnly = true; _grid.Columns[5].ReadOnly = true;
            var steps = project.ValidationSteps ?? ValidationProgress.Create();
            foreach (var step in steps) { int i = _grid.Rows.Add(step.Name, step.Percent.HasValue ? step.Percent.Value.ToString("0.#") : "N/A", step.Start, step.End, step.Notes, step.UpdatedAt <= 0 ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(step.UpdatedAt).LocalDateTime.ToString("yyyy-MM-dd HH:mm"),step.Weight); _grid.Rows[i].Tag = step.Id; }
            split.Panel2.Controls.Add(_grid);
            save.Click += delegate
            {
                try
                {
                    _grid.EndEdit(); var values = new List<ValidationStep>();
                    foreach (DataGridViewRow row in _grid.Rows)
                    {
                        string input = Convert.ToString(row.Cells[1].Value).Trim().TrimEnd('%'); double percent;
                        double? value = null;
                        if (!string.Equals(input, "N/A", StringComparison.OrdinalIgnoreCase)) { if (!double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out percent)) throw new ArgumentException(Convert.ToString(row.Cells[0].Value) + "：请输入百分比或 N/A"); value = percent; }
                        double weight;if(!double.TryParse(Convert.ToString(row.Cells[6].Value),out weight))throw new ArgumentException("权重请输入数字，0表示不计入总进度");
                        values.Add(new ValidationStep { Id = (string)row.Tag, Name = Convert.ToString(row.Cells[0].Value), Percent = value, Weight=weight, Start = Convert.ToString(row.Cells[2].Value).Trim(), End = Convert.ToString(row.Cells[3].Value).Trim(), Notes = Convert.ToString(row.Cells[4].Value) });
                    }
                    store.SaveValidation(project.Id, values, start.Text.Trim(), end.Text.Trim(), notes.Text, root.Text.Trim()); DialogResult = DialogResult.OK; Close();
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            Controls.Add(split); Controls.Add(fields); Controls.Add(footer); CancelButton = cancel;
        }
    }
}
