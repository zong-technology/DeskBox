using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace DeskBox.UI
{
    public class CommandItem
    {
        public string Icon = "⌁";
        public string Name = "";
        public string Hint = "";
        public Action Execute;
    }

    /// <summary>Ctrl+K 全局命令面板：不用返回桌面即可跳转到所有核心能力。</summary>
    public class CommandPaletteForm : Form
    {
        private readonly List<CommandItem> _commands;
        private readonly Palette _palette;
        private readonly Func<string, List<CommandItem>> _dynamicProvider;
        private readonly DarkInput _query;
        private readonly DarkList _list;

        public CommandPaletteForm(IEnumerable<CommandItem> commands, Palette palette, Func<string, List<CommandItem>> dynamicProvider = null)
        {
            _commands = commands == null ? new List<CommandItem>() : commands.ToList();
            _palette = palette;
            _dynamicProvider = dynamicProvider;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Width = 720; Height = 520;
            BackColor = palette.Bg;
            Padding = new Padding(2);
            KeyPreview = true;

            var shell = new Panel { Dock = DockStyle.Fill, BackColor = palette.CardBg, Padding = new Padding(18) };
            shell.Paint += delegate(object s, PaintEventArgs e) { e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; Palette.DrawRounded(e.Graphics, Color.FromArgb(190, palette.Accent), new Rectangle(0, 0, shell.Width - 1, shell.Height - 1), 14, 1.4f); };
            var title = new Label { Text = "⌁  COMMAND MATRIX", Left = 20, Top = 16, AutoSize = true, ForeColor = palette.Accent, BackColor = palette.CardBg, Font = new Font("Consolas", 12F, FontStyle.Bold) };
            var hint = new Label { Text = "输入功能、模块或工程关键词；↑↓ 选择，Enter 执行，Esc 关闭", Left = 20, Top = 46, AutoSize = true, ForeColor = palette.TextFaint, BackColor = palette.CardBg, Font = new Font("Microsoft YaHei UI", 8.5F) };
            _query = new DarkInput { Left = 20, Top = 78, Width = 676, Height = 34, Placeholder = "例如：PLC、验证、AI、文件、监控、任务…" };
            _query.ApplyPalette(palette);
            _list = new DarkList { Left = 20, Top = 126, Width = 676, Height = 366, Palette = palette, RowHeight = 42 };
            _list.SetColumns(new[] { "命令", "说明" }, new[] { 270, 360 });
            _query.TextChanged += delegate { ReloadData(); };
            _query.KeyPressed += OnQueryKey;
            _list.RowActivated += delegate { ExecuteSelected(); };
            shell.Controls.AddRange(new Control[] { title, hint, _query, _list });
            Controls.Add(shell);
            Shown += delegate { _query.Inner.Focus(); ReloadData(); };
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
        }

        private void ReloadData()
        {
            string q = (_query.Text ?? "").Trim();
            var matches = _commands.Where(x => q.Length == 0 || (x.Name + " " + x.Hint).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (q.Length > 0 && _dynamicProvider != null)
            {
                try
                {
                    var dynamicItems = _dynamicProvider(q) ?? new List<CommandItem>();
                    foreach (var item in dynamicItems)
                        if (!matches.Any(x => x.Name == item.Name && x.Hint == item.Hint)) matches.Add(item);
                }
                catch { }
            }
            _list.SetRows(matches.Select(x => new ListRow { Icon = x.Icon, Cells = new[] { x.Name, x.Hint }, Tag = x, Accent = x.Name.Contains("AI&SI") || x.Name.Contains("工程工具") ? _palette.Accent : (Color?)null }).ToList());
            if (_list.RowCount > 0) _list.SelectIndex(0);
        }

        private void OnQueryKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down) { _list.SelectIndex(Math.Min(_list.RowCount - 1, _list.SelectedIndex + 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { _list.SelectIndex(Math.Max(0, _list.SelectedIndex - 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ExecuteSelected(); }
            else if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        private void ExecuteSelected()
        {
            var row = _list.SelectedRow;
            var command = row == null ? null : row.Tag as CommandItem;
            if (command == null) return;
            Close();
            try { if (command.Execute != null) command.Execute(); } catch { }
        }
    }
}
