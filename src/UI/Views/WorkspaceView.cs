using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;
using DeskBox.Native;

namespace DeskBox.UI.Views
{
    /// <summary>工程项目工作区：把 PLC/DCS/CSV 项目根目录固定在桌面工作台内。</summary>
    public class WorkspaceView : ViewBase
    {
        private readonly WorkspaceStore _workspaces;
        private DarkList _list;
        private DarkInput _filter;
        private DarkSelect _kind;
        private Label _status;
        public event Action<EngineerWorkspace> SearchRequested;

        public WorkspaceView(Store store, WorkspaceStore workspaces) : base(store, "工程项目舱", "集中管理 PLC、DCS、SCADA、验证与调试资料；双击直接打开")
        {
            _workspaces = workspaces;
            Build();
            if (_workspaces != null) _workspaces.Changed += OnChanged;
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Palette.Bg };
            var add = new DarkButton("＋ 添加项目目录") { Primary = true, Left = 0, Top = 2, Width = 130 };
            var open = new DarkButton("打开") { Left = 138, Top = 2, Width = 70 };
            var reveal = new DarkButton("定位") { Left = 216, Top = 2, Width = 70 };
            var edit = new DarkButton("编辑信息") { Left = 294, Top = 2, Width = 80 };
            var pin = new DarkButton("置顶/取消") { Left = 382, Top = 2, Width = 90 };
            var remove = new DarkButton("移除") { Left = 480, Top = 2, Width = 70 };
            _kind = new DarkSelect(new[] { "全部类型", "PLC", "DCS", "SCADA", "CSV/验证", "仪表", "其他" }) { Left = 562, Top = 2, Width = 110 };
            _filter = new DarkInput { Left = 680, Top = 2, Width = 280, Placeholder = "筛选项目、客户、路径" };
            var current = new DarkButton("设为当前") { Left = 968, Top = 2, Width = 88 };
            var progress = new DarkButton("更新进度") { Left = 1064, Top = 2, Width = 88 };
            var search = new DarkButton("项目内搜索") { Left = 1160, Top = 2, Width = 100 };
            var tagCenter = new DarkButton("统一工程 Tag…") { Left = 0, Top = 38, Width = 132 };
            tagCenter.Click += delegate { EngineeringDataForm.Open(FindForm(), () => _workspaces.Context); };
            var validation = new DarkButton("验证工作台…") { Left = 140, Top = 38, Width = 128 };
            validation.Click += delegate { ValidationCenterForm.Open(FindForm(), () => _workspaces.Context, () => Store.Tasks); };
            search.Click += delegate { var project = Selected(); if (project == null) { _status.Text = "请先选择项目"; return; } if (SearchRequested != null) SearchRequested(project); };
            _status = new Label { Left = 280, Top = 48, AutoSize = true, ForeColor = Palette.TextFaint, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 8.5F) };
            add.Click += delegate { AddWorkspace(); };
            open.Click += delegate { OpenSelected(false); };
            reveal.Click += delegate { OpenSelected(true); };
            edit.Click += delegate { EditSelected(); };
            pin.Click += delegate { TogglePin(); };
            remove.Click += delegate { RemoveSelected(); };
            current.Click += delegate { SetCurrent(); };
            progress.Click += delegate { UpdateProgress(); };
            _kind.SelectedIndexChanged += delegate { ReloadData(); };
            _filter.TextChanged += delegate { ReloadData(); };
            top.AutoScroll = true;
            top.Controls.AddRange(new Control[] { add, open, reveal, edit, pin, remove, _kind, _filter, current, progress, search, tagCenter, validation, _status });

            _list = new DarkList { Dock = DockStyle.Fill, Palette = Palette, RowHeight = 38 };
            _list.SetColumns(new[] { "项目", "类型", "进度", "风险/截止", "客户/系统", "根目录" }, new[] { 220, 90, 90, 150, 190, 500 });
            _list.RowActivated += delegate { OpenSelected(false); };
            Body.Controls.Add(_list);
            Body.Controls.Add(top);
            _list.BringToFront();
        }

        private void AddWorkspace()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "选择 PLC / DCS / 验证项目根目录";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var name = Prompt.Input(this, "添加工程项目", "项目名称", Path.GetFileName(d.SelectedPath), Palette);
                if (name == null) return;
                string kind = _kind.SelectedIndex > 0 ? _kind.Inner.Items[_kind.SelectedIndex].ToString() : "PLC";
                try
                {
                    var item = _workspaces.Add(name, d.SelectedPath, kind);
                    try
                    {
                        var index = DeskBox.Fs.FileIndex.Instance;
                        var roots = index.Roots.ToList();
                        if (!roots.Any(r => string.Equals(r, item.Root, StringComparison.OrdinalIgnoreCase)))
                        {
                            roots.Add(item.Root);
                            index.Configure(roots, index.Excludes.ToList());
                            index.Rebuild();
                        }
                    }
                    catch (Exception ex) { AppLog.Write("warn", "workspace", "项目目录加入索引失败：" + ex.Message); }
                    Store.Log("info", "workspace", "添加工程项目：" + item.Name + " · " + item.Root);
                    _status.Text = "已添加：" + item.Name;
                }
                catch (Exception ex) { _status.Text = ex.Message; }
                ReloadData();
            }
        }

        private EngineerWorkspace Selected()
        {
            var row = _list.SelectedRow;
            return row == null ? null : row.Tag as EngineerWorkspace;
        }

        private void OpenSelected(bool reveal)
        {
            var x = Selected();
            if (x == null) { _status.Text = "请先选择一个项目"; return; }
            if (!Directory.Exists(x.Root)) { _status.Text = "项目目录已不存在：" + x.Root; return; }
            try
            {
                if (reveal) Win32.RevealPath(x.Root); else Win32.OpenPath(x.Root);
                _workspaces.Touch(x.Id);
                Store.Log("info", "workspace", "打开工程项目：" + x.Name);
            }
            catch (Exception ex) { _status.Text = "打开失败：" + ex.Message; }
        }

        private void TogglePin()
        {
            var x = Selected();
            if (x == null) { _status.Text = "请先选择一个项目"; return; }
            _workspaces.TogglePin(x.Id);
            ReloadData();
        }

        private void SetCurrent()
        {
            var x = Selected(); if (x == null) { _status.Text = "请先选择一个项目"; return; }
            _workspaces.SetCurrent(x.Id); Store.Log("info", "project", "设置当前项目：" + x.Name); ReloadData();
        }

        private void UpdateProgress()
        {
            var x = Selected(); if (x == null) { _status.Text = "请先选择一个项目"; return; }
            using (var form = new ProjectProgressForm(_workspaces, x, Palette)) if (form.ShowDialog(this) == DialogResult.OK) Store.Log("info", "project", "更新验证进度：" + x.Name);
            ReloadData();
        }

        private void EditSelected()
        {
            var x = Selected();
            if (x == null) { _status.Text = "请先选择一个项目"; return; }
            var name = Prompt.Input(this, "编辑工程项目", "项目名称", x.Name, Palette); if (name == null) return;
            var customer = Prompt.Input(this, "编辑工程项目", "客户 / 工厂", x.Customer, Palette); if (customer == null) return;
            var system = Prompt.Input(this, "编辑工程项目", "系统 / 设备", x.System, Palette); if (system == null) return;
            var number = Prompt.Input(this, "编辑工程项目", "项目编号", x.Number, Palette); if (number == null) return;
            var equipment = Prompt.Input(this, "编辑工程项目", "设备 / 产线", x.Equipment, Palette); if (equipment == null) return;
            var owner = Prompt.Input(this, "编辑工程项目", "负责人", x.Owner, Palette); if (owner == null) return;
            var deadline = Prompt.Input(this, "编辑工程项目", "截止日期 yyyy-MM-dd（可留空）", x.Deadline, Palette); if (deadline == null) return;
            var risk = Prompt.Input(this, "编辑工程项目", "风险 low / medium / high / critical", x.Risk, Palette); if (risk == null) return;
            var notes = Prompt.Input(this, "编辑工程项目", "备注", x.Notes, Palette); if (notes == null) return;
            string kind = _kind.SelectedIndex > 0 ? _kind.Inner.Items[_kind.SelectedIndex].ToString() : x.Kind;
            _workspaces.Update(x.Id, name, kind, customer, system, notes);
            _workspaces.UpdateProjectDetails(x.Id, number, equipment, owner, x.Start, deadline, risk);
            Store.Log("info", "workspace", "更新工程项目：" + name);
            ReloadData();
        }

        private void RemoveSelected()
        {
            var x = Selected();
            if (x == null) { _status.Text = "请先选择一个项目"; return; }
            if (!Prompt.Confirm(this, "移除项目", "只从 DeskBox 项目列表移除「" + x.Name + "」，不会删除磁盘文件。继续？", Palette)) return;
            _workspaces.Remove(x.Id);
            Store.Log("info", "workspace", "移除工程项目配置：" + x.Name);
            ReloadData();
        }

        public void ReloadData()
        {
            var rows = new List<ListRow>();
            if (_workspaces != null)
            {
                var q = (_filter.Text ?? "").Trim();
                var type = _kind.SelectedIndex > 0 ? _kind.Inner.Items[_kind.SelectedIndex].ToString() : "";
                foreach (var x in _workspaces.Items)
                {
                    var all = (x.Name + " " + x.Root + " " + x.Customer + " " + x.System);
                    if (q.Length > 0 && all.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (type.Length > 0 && !string.Equals(x.Kind, type, StringComparison.OrdinalIgnoreCase)) continue;
                    bool isCurrent = _workspaces.Current != null && _workspaces.Current.Id == x.Id;
                    rows.Add(new ListRow { Icon = isCurrent ? "▶" : x.Pinned ? "◆" : "◇", Cells = new[] { x.Name, x.Kind, ValidationProgress.Display(x), (x.Risk ?? "medium").ToUpperInvariant() + (string.IsNullOrEmpty(x.Deadline) ? "" : " · " + x.Deadline), Join(x.Customer, x.System), x.Root }, Tag = x, Accent = isCurrent || x.Pinned ? Palette.Accent : (Color?)null, Tip = x.Notes });
                }
                _status.Text = rows.Count + " 个项目 · 当前：" + (_workspaces.Current == null ? "未设置" : _workspaces.Current.Name) + " · 只保存路径，不复制工程文件";
            }
            _list.SetRows(rows);
        }

        private static string Join(string a, string b) { return string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + " / " + b; }
        private void OnChanged() { try { if (InvokeRequired) BeginInvoke(new Action(ReloadData)); else ReloadData(); } catch { } }
        protected override void OnActivated() { ReloadData(); }
        protected override void Dispose(bool disposing) { if (_workspaces != null) _workspaces.Changed -= OnChanged; base.Dispose(disposing); }
    }
}
