using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Core;
using DeskBox.Fs;

namespace DeskBox.UI
{
    /// <summary>批量重命名：规则配置 → 新旧名对照 → 冲突检查 → 执行 → 结果与撤销。</summary>
    public class BatchRenameForm : Form
    {
        private readonly List<string> _sources;
        private readonly FileOps _ops;
        private readonly Palette _p;
        private readonly RenameRule _rule = new RenameRule();

        private DarkInput _find, _replace, _number;
        private CheckBox _regex, _keepExt, _lower, _upper, _trim;
        private NumericUpDown _start, _step;
        private DarkList _preview;
        private Label _summary;
        private DarkButton _btnPreview, _btnRun, _btnUndo;
        private RenamePlan _plan;

        public BatchRenameForm(List<string> sources, FileOps ops, Palette palette)
        {
            _sources = sources ?? new List<string>();
            _ops = ops;
            _p = palette;
            Build();
            UpdatePreview();
        }

        private void Build()
        {
            Text = "批量重命名 · " + _sources.Count + " 个文件";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(940, 620);
            MinimumSize = new Size(760, 520);
            BackColor = _p.Bg;
            ForeColor = _p.Text;
            Font = new Font("Microsoft YaHei UI", 9F);
            ShowInTaskbar = false;

            var head = new Label
            {
                Text = "规则配置", Left = 16, Top = 12, AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = _p.Text
            };
            Controls.Add(head);

            int y = 44;
            AddLabel("查找", 16, y + 4);
            _find = new DarkInput { Left = 70, Top = y, Width = 150, Placeholder = "旧名称片段" };
            _find.Palette = _p;
            _find.Text = "";
            _find.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) UpdatePreview(); };

            AddLabel("替换为", 232, y + 4);
            _replace = new DarkInput { Left = 290, Top = y, Width = 150, Placeholder = "新名称片段" };
            _replace.Palette = _p;
            _replace.KeyPressed += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) UpdatePreview(); };

            AddLabel("编号前缀", 452, y + 4);
            _number = new DarkInput { Left = 522, Top = y, Width = 100, Placeholder = "如 序号_" };
            _number.Palette = _p;

            AddLabel("起始", 634, y + 4);
            _start = new NumericUpDown { Left = 672, Top = y + 4, Width = 54, Value = 1, Minimum = 0, Maximum = 99999, BackColor = _p.PanelAlt, ForeColor = _p.Text, BorderStyle = BorderStyle.FixedSingle };
            AddLabel("步长", 734, y + 4);
            _step = new NumericUpDown { Left = 772, Top = y + 4, Width = 54, Value = 1, Minimum = 1, Maximum = 9999, BackColor = _p.PanelAlt, ForeColor = _p.Text, BorderStyle = BorderStyle.FixedSingle };

            y += 40;
            _regex = MakeCheck("正则表达式", 16, y, 110);
            _keepExt = MakeCheck("保留扩展名", 136, y, 110);
            _lower = MakeCheck("小写", 256, y, 70);
            _upper = MakeCheck("大写", 326, y, 70);
            _trim = MakeCheck("去多余空格", 396, y, 110);
            _keepExt.Checked = true;
            _trim.Checked = true;
            _regex.CheckedChanged += delegate { UpdatePreview(); };
            _keepExt.CheckedChanged += delegate { UpdatePreview(); };
            _lower.CheckedChanged += delegate { if (_lower.Checked) _upper.Checked = false; UpdatePreview(); };
            _upper.CheckedChanged += delegate { if (_upper.Checked) _lower.Checked = false; UpdatePreview(); };
            _trim.CheckedChanged += delegate { UpdatePreview(); };

            _btnPreview = new DarkButton("预览新名称") { Left = 520, Top = y - 4, Width = 110, Height = 30 };
            _btnPreview.Click += delegate { UpdatePreview(); };
            _btnRun = new DarkButton("执行重命名") { Primary = true, Left = 638, Top = y - 4, Width = 110, Height = 30 };
            _btnRun.Click += delegate { Execute(); };
            _btnUndo = new DarkButton("撤销上次") { Left = 756, Top = y - 4, Width = 100, Height = 30 };
            _btnUndo.Click += delegate { Undo(); };
            Controls.AddRange(new Control[] { _find, _replace, _number, _start, _step, _btnPreview, _btnRun, _btnUndo });

            _preview = new DarkList { Left = 16, Top = y + 44, Width = 908, Height = 430, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            _preview.Palette = _p;
            _preview.SetColumns(new[] { "原名称", "→", "新名称", "状态" }, new[] { 340, 30, 340, 160 });
            Controls.Add(_preview);

            _summary = new Label
            {
                Left = 16, Top = y + 484, AutoSize = true, ForeColor = _p.TextDim,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Text = ""
            };
            Controls.Add(_summary);
        }

        private CheckBox MakeCheck(string text, int x, int y, int w)
        {
            var c = new CheckBox { Text = text, Left = x, Top = y, Width = w, ForeColor = _p.Text };
            Controls.Add(c);
            return c;
        }

        private void AddLabel(string text, int x, int y)
        {
            Controls.Add(new Label { Text = text, Left = x, Top = y, Width = 60, ForeColor = _p.TextDim });
        }

        private RenameRule CurrentRule()
        {
            _rule.Find = _find.Text;
            _rule.Replace = _replace.Text;
            _rule.UseRegex = _regex.Checked;
            _rule.NumberPattern = (_number.Text ?? "").Trim();
            _rule.NumberStart = (int)_start.Value;
            _rule.NumberStep = (int)_step.Value;
            _rule.LowerCase = _lower.Checked;
            _rule.UpperCase = _upper.Checked;
            _rule.TrimSpace = _trim.Checked;
            return _rule;
        }

        private void UpdatePreview()
        {
            try
            {
                _plan = _ops.PlanRename(_sources, CurrentRule(), _keepExt.Checked);
                var rows = new List<ListRow>();
                foreach (var it in _plan.Items)
                {
                    rows.Add(new ListRow
                    {
                        Cells = new[] { it.OldName, "→", it.NewName, it.Conflict ? "冲突：" + it.ConflictReason : (it.Changed ? "可执行" : "无变化") },
                        Accent = it.Conflict ? Color.FromArgb(248, 113, 113) : (it.Changed ? _p.Accent : (Color?)null)
                    });
                }
                _preview.SetRows(rows);
                _summary.Text = "共 " + _plan.Items.Count + " 项，待改名 " + _plan.ChangedCount + " 项，冲突 " + _plan.ConflictCount + " 项"
                    + (string.IsNullOrEmpty(_plan.Errors) ? "" : " · " + _plan.Errors);
                _summary.ForeColor = _plan.ConflictCount > 0 ? Color.FromArgb(248, 113, 113) : _p.TextDim;
            }
            catch (Exception ex)
            {
                _summary.Text = "规则错误：" + ex.Message;
                _summary.ForeColor = Color.FromArgb(248, 113, 113);
            }
        }

        private void Execute()
        {
            if (_plan == null) { UpdatePreview(); return; }
            if (_plan.ConflictCount > 0)
            {
                var ok = Prompt.Confirm(this, "存在冲突", "有 " + _plan.ConflictCount + " 项冲突（目标已存在或重名），继续将跳过这些项。是否继续？", _p);
                if (!ok) return;
            }
            if (_plan.ChangedCount == 0) { MessageBox.Show(this, "没有需要改名的项目。", "DeskBox", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

            var confirm = Prompt.Confirm(this, "确认执行",
                "将重命名 " + _plan.ChangedCount + " 个文件。\r\n执行后会记录撤销批次，可点「撤销上次」回滚。", _p);
            if (!confirm) return;

            string msg;
            bool ok2 = _ops.ExecuteRename(_plan, out msg);
            UpdatePreview();
            MessageBox.Show(this, msg, ok2 ? "完成" : "部分失败", MessageBoxButtons.OK,
                ok2 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private void Undo()
        {
            try
            {
                var last = _ops.LastUndo;
                if (last == null) { MessageBox.Show(this, "没有可撤销的批次。", "DeskBox"); return; }
                if (!Prompt.Confirm(this, "撤销", "撤销「" + last.Title + "」（" + last.Actions.Count + " 个动作）？", _p)) return;
                string msg;
                bool ok = _ops.Undo(last.Id, out msg);
                MessageBox.Show(this, msg, ok ? "已撤销" : "撤销失败", MessageBoxButtons.OK,
                    ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                UpdatePreview();
            }
            catch (Exception ex) { MessageBox.Show(this, "撤销异常：" + ex.Message); }
        }
    }
}
