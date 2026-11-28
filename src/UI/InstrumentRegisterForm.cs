using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.AppServices;
using DeskBox.Domain;
using DeskBox.Engineering;
using DeskBox.Native;

namespace DeskBox.UI
{
    internal sealed class InstrumentRegisterForm : Form
    {
        private readonly ProjectContext project;
        private readonly Func<ProjectContext> active;
        private readonly InstrumentService service = new InstrumentService();
        private readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 250 };
        private readonly TextBox search = new TextBox { Width = 165, AccessibleName = "搜索资产编号、名称或 Tag" };
        private readonly CheckBox due = new CheckBox { Text = "只看到期或逾期（含未来 30 天）", AutoSize = true };
        private readonly Button cancel = new Button { Text = "取消操作", AutoSize = true, Enabled = false };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 28, AutoEllipsis = true, UseMnemonic = false };
        private readonly DataGridView assets = Grid("instrument-assets", "资产编号", "名称", "类型", "Tag", "校准到期", "状态");
        private readonly DataGridView history = Grid("instrument-history", "实际日期", "记录类型", "证书／版本", "附件", "记录人");
        private readonly TextBox details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private InstrumentRegister register;
        private Dictionary<string, string> tagLabels = new Dictionary<string, string>();
        private CancellationTokenSource work;
        private readonly string tagFilter;
        private bool binding;

        internal static void Open(IWin32Window owner, Func<ProjectContext> active, string tagFilter = "", bool dueOnly = false)
        {
            var project = active();
            if (project == null)
            {
                MessageBox.Show(owner, "请先选择工程项目。", "仪表资产");
                return;
            }
            using (var form = new InstrumentRegisterForm(project, active, tagFilter, dueOnly)) form.ShowDialog(owner);
        }

        private InstrumentRegisterForm(ProjectContext project, Func<ProjectContext> active, string tagFilter, bool dueOnly)
        {
            this.project = project;
            this.active = active;
            this.tagFilter = tagFilter;
            Name = "instrument-workbench";
            Text = "仪表资产 · " + project.Name + (tagFilter.Length == 0 ? "" : " · 关联 Tag");
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            MinimumSize = new Size(630, 425);
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(970, screen.Width - 35), Math.Min(645, screen.Height - 60));

            var actions = Bar();
            Button(actions, "新建", delegate { EditAsset(null); });
            Button(actions, "编辑", delegate { if (SelectedAsset != null) EditAsset(SelectedAsset); });
            Button(actions, "设置手册附件…", SetManual);
            Button(actions, "核验或定位手册", delegate { Verify(false); });
            Button(actions, "导出台账…", Export);
            Button(actions, "重新读取", delegate { Reload(); });
            Button(actions, "从上一版恢复…", Restore);
            actions.Controls.Add(cancel);
            var filter = Bar();
            filter.Controls.Add(new Label { Text = "筛选", AutoSize = true, Margin = new Padding(5, 7, 3, 3) });
            filter.Controls.Add(search);
            due.Name = "instrument-due-only";
            due.Checked = dueOnly;
            filter.Controls.Add(due);
            var entryActions = Bar();
            Button(entryActions, "追加校准…", delegate { Record("calibration"); });
            Button(entryActions, "追加维护…", delegate { Record("maintenance"); });
            Button(entryActions, "核验或定位记录附件", delegate { Verify(true); });
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            body.Controls.Add(assets, 0, 0);
            body.Controls.Add(entryActions, 0, 1);
            body.Controls.Add(history, 0, 2);
            body.Controls.Add(details, 0, 3);
            Controls.Add(body);
            Controls.Add(filter);
            Controls.Add(actions);
            Controls.Add(status);

            search.TextChanged += delegate { ShowAssets(); };
            due.CheckedChanged += delegate { ShowAssets(); };
            assets.SelectionChanged += delegate { if (!binding) ShowHistory(); };
            history.SelectionChanged += delegate { if (!binding) ShowDetails(); };
            assets.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs args)
            {
                if (args.RowIndex >= 0 && SelectedAsset != null) EditAsset(SelectedAsset);
            };
            cancel.Click += delegate { if (work != null) work.Cancel(); };
            watch.Tick += delegate { if (!project.SameScope(active())) Close(); };
            Shown += delegate { watch.Start(); Reload(); };
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            watch.Dispose();
            if (work != null) work.Cancel();
            base.OnFormClosed(e);
        }

        private InstrumentAsset SelectedAsset { get { return assets.CurrentRow == null ? null : assets.CurrentRow.Tag as InstrumentAsset; } }
        private InstrumentActivity SelectedEntry { get { return history.CurrentRow == null ? null : history.CurrentRow.Tag as InstrumentActivity; } }

        private async void Run<T>(string action, Func<CancellationToken, T> worker, Action<T> ready)
        {
            if (work != null) { status.Text = "请等待当前操作或先取消。"; return; }
            if (!project.SameScope(active())) return;
            var operation = new CancellationTokenSource();
            work = operation;
            cancel.Enabled = true;
            status.Text = action + "…";
            try
            {
                var result = await Task.Run(() => worker(operation.Token), operation.Token);
                if (IsDisposed || !project.SameScope(active())) return;
                // A write can finish just before a cancellation request arrives. Display the committed result.
                work = null;
                status.Text = action + "完成。";
                ready(result);
            }
            catch (OperationCanceledException) { if (!IsDisposed) status.Text = "操作已取消。"; }
            catch (Exception error) { if (!IsDisposed) status.Text = "操作失败 · " + error.Message; }
            finally
            {
                if (ReferenceEquals(work, operation)) work = null;
                if (!IsDisposed) cancel.Enabled = work != null;
                operation.Dispose();
            }
        }

        private void Reload(string selectId = null)
        {
            Run("读取仪表台账", token => new
            {
                Register = service.Read(project, token),
                Tags = new TagCenter().Snapshot(project, token).Items.ToDictionary(row => row.TagId, row => row.SystemId + " / " + row.TagName)
            }, result =>
            {
                register = result.Register;
                tagLabels = result.Tags;
                ShowAssets(selectId);
            });
        }

        private void ShowAssets(string selectId = null)
        {
            if (register == null) return;
            selectId = selectId ?? (SelectedAsset == null ? "" : SelectedAsset.Id);
            var entries = register.Activities.ToLookup(row => row.AssetId);
            var today = DateTime.Today;
            var query = search.Text.Trim();
            binding = true;
            assets.Rows.Clear();
            foreach (var row in register.Assets.OrderBy(item => item.Number, StringComparer.OrdinalIgnoreCase))
            {
                string tag = TagLabel(row.TagId);
                if ((tagFilter.Length > 0 && tagFilter != row.TagId) ||
                    (row.Number + " " + row.Name + " " + row.Kind + " " + row.SerialNumber + " " + tag)
                        .IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var latest = InstrumentService.LatestCalibration(entries[row.Id]);
                var state = InstrumentService.DueStatus(latest, today);
                if (due.Checked && (state == "未设到期日" || state == "未到期")) continue;
                int index = assets.Rows.Add(row.Number, row.Name, row.Kind, tag, latest == null ? "" : latest.DueDate, state);
                assets.Rows[index].Tag = row;
                if (row.Id == selectId) assets.CurrentCell = assets.Rows[index].Cells[0];
            }
            binding = false;
            status.Text = assets.Rows.Count + " 项仪表 · 台账共 " + register.Assets.Count
                + (register.Recovered ? " · 当前为上一版只读视图" : "");
            ShowHistory();
        }

        private void ShowHistory()
        {
            binding = true;
            history.Rows.Clear();
            var asset = SelectedAsset;
            if (asset != null)
            {
                foreach (var entry in register.Activities.Where(row => row.AssetId == asset.Id).OrderByDescending(row => row.Date)
                    .ThenByDescending(row => row.RecordedAt))
                    history.Rows[history.Rows.Add(entry.Date, entry.Kind == "calibration" ? "校准" : "维护",
                        entry.CertificateNumber, entry.Source == null ? "" : entry.Source.Document, entry.RecordedBy)].Tag = entry;
                foreach (var row in register.History.Where(row => row.Id == asset.Id).OrderByDescending(row => row.Revision))
                    history.Rows[history.Rows.Add("", "资产旧版本", "v" + row.Revision, row.Manual == null ? "" : row.Manual.Document, row.UpdatedBy)].Tag = row;
                history.ClearSelection();
                history.CurrentCell = null;
            }
            binding = false;
            ShowDetails();
        }

        private void ShowDetails()
        {
            var entry = SelectedEntry;
            if (entry != null)
            {
                details.Text = "记录 ID " + entry.Id + " · " + entry.Date + "   下次校准 " + entry.DueDate + "\r\n"
                    + entry.Notes + "\r\n" + DescribeSource(entry.Source)
                    + (entry.EvidenceId.Length == 0 ? "" : "\r\n归档证据 " + entry.EvidenceId);
                return;
            }
            var row = history.CurrentRow == null ? null : history.CurrentRow.Tag as InstrumentAsset;
            row = row ?? SelectedAsset;
            details.Text = row == null ? "当前范围没有仪表。" : "资产 ID " + row.Id + " · v" + row.Revision + " · " + row.UpdatedBy
                + "\r\n" + row.Manufacturer + " " + row.Model + " / " + row.SerialNumber + " / " + row.Location
                + "\r\n信号 " + row.Signal + " · 量程 " + row.RangeLow + " 至 " + row.RangeHigh + " " + row.Unit
                + "\r\nTag " + TagLabel(row.TagId) + " " + row.TagId + "\r\n" + DescribeSource(row.Manual);
        }

        private void EditAsset(InstrumentAsset original)
        {
            if (work != null || register == null) return;
            var row = original ?? new InstrumentAsset { TagId = tagFilter };
            string[] labels = { "资产编号", "名称", "类型", "厂商", "型号", "序列号", "安装位置", "信号", "工程单位", "量程下限", "量程上限", "关联工程 Tag" };
            var options = new string[labels.Length][];
            var byLabel = tagLabels.ToDictionary(pair => pair.Value + " [" + pair.Key + "]", pair => pair.Key);
            if (row.TagId.Length > 0 && !tagLabels.ContainsKey(row.TagId)) byLabel.Add(row.TagId, row.TagId);
            byLabel["不关联"] = "";
            options[11] = byLabel.Keys.OrderBy(key => key, StringComparer.CurrentCulture).ToArray();
            var values = new[] { row.Number, row.Name, row.Kind, row.Manufacturer, row.Model, row.SerialNumber, row.Location,
                row.Signal, row.Unit, row.RangeLow.ToString(), row.RangeHigh.ToString(), byLabel.First(pair => pair.Value == row.TagId).Key };
            using (var edit = new EngineeringFieldsForm(original == null ? "新建仪表" : "编辑仪表", labels, values, options))
            {
                while (edit.ShowDialog(this) == DialogResult.OK)
                {
                    var input = edit.Values;
                    decimal? low, high;
                    if (!Number(input[9], out low) || !Number(input[10], out high))
                    {
                        MessageBox.Show(this, "量程必须为数字。", "仪表资产");
                        continue;
                    }
                    var next = new InstrumentAsset { Id = row.Id, Number = input[0], Name = input[1], Kind = input[2],
                        RevisionToken = row.RevisionToken, Manufacturer = input[3], Model = input[4], SerialNumber = input[5], Location = input[6], Signal = input[7],
                        Unit = input[8], RangeLow = low, RangeHigh = high, TagId = byLabel[input[11]] };
                    Run("保存仪表", token => service.Save(project, next, row.Revision, token), saved => Reload(saved.Id));
                    return;
                }
            }
        }

        private void SetManual()
        {
            var selected = SelectedAsset;
            if (selected == null || work != null) return;
            string path = ChooseFile("选择工程项目范围内的手册");
            if (path == null) return;
            if (selected.Manual != null && MessageBox.Show(this, "替换当前手册引用？旧引用将在资产历史中保留。", "仪表资产",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Run("保存手册引用", token => service.Save(project, selected, selected.Revision, token, path), item => Reload(item.Id));
        }

        private void Record(string kind)
        {
            var selected = SelectedAsset;
            if (selected == null || work != null) return;
            bool calibration = kind == "calibration";
            var labels = calibration ? new[] { "实际日期 yyyy-MM-dd", "下次校准 yyyy-MM-dd", "证书编号", "实际工作记录" }
                : new[] { "实际日期 yyyy-MM-dd", "实际工作记录" };
            var values = new string[labels.Length];
            values[0] = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using (var edit = new EngineeringFieldsForm(calibration ? "追加校准记录" : "追加维护记录", labels, values))
            {
                if (edit.ShowDialog(this) != DialogResult.OK) return;
                var input = edit.Values;
                var entry = new InstrumentActivity { AssetId = selected.Id, Kind = kind, Date = input[0], Notes = input.Last(),
                    DueDate = calibration ? input[1] : "", CertificateNumber = calibration ? input[2] : "" };
                var paths = new[] { "无附件", "引用工程原件", "选用验证中心已归档证据" };
                using (var choice = new EngineeringFieldsForm("附件来源", new[] { "来源" }, new[] { paths[0] }, new[] { paths }))
                {
                    if (choice.ShowDialog(this) != DialogResult.OK) return;
                    if (choice.Values[0] == paths[2])
                    {
                        Run("读取验收证据", token => new ValidationService().Read(project, token).All<Evidence>().ToArray(),
                            items => ChooseEvidence(entry, items));
                        return;
                    }
                    string file = choice.Values[0] == paths[1] ? ChooseFile("选择证书或现场记录") : "";
                    if (file != null) SaveEntry(entry, file);
                }
            }
        }

        private void ChooseEvidence(InstrumentActivity entry, Evidence[] items)
        {
            if (items.Length == 0) { status.Text = "当前项目没有已归档证据。"; return; }
            var map = items.ToDictionary(item => item.FileName + " [" + item.Id + "]", item => item.Id);
            var labels = map.Keys.ToArray();
            using (var dialog = new EngineeringFieldsForm("选择归档证据", new[] { "证据" }, new[] { labels[0] }, new[] { labels }))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                entry.EvidenceId = map[dialog.Values[0]];
                SaveEntry(entry, null);
            }
        }

        private void SaveEntry(InstrumentActivity entry, string file)
        {
            Run("保存实际记录", token => service.Record(project, entry, token, file), item => Reload(item.AssetId));
        }

        private void Verify(bool activity)
        {
            var asset = SelectedAsset;
            var entry = SelectedEntry;
            if (asset == null || activity && entry == null) return;
            Run("校验附件 SHA-256", token => service.VerifyAttachment(project, asset.Id, activity ? entry.Id : null, token), path =>
            {
                if (path.Length > 0) Win32.RevealPath(path);
                status.Text = "附件与登记的原始 SHA-256 一致。";
            });
        }

        private void Export()
        {
            if (work != null || register == null) return;
            using (var dialog = new SaveFileDialog { InitialDirectory = project.Root, FileName = "instrument-register.xlsx",
                Filter = "Excel 工作簿|*.xlsx|CSV|*.csv", AddExtension = true, OverwritePrompt = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string path = dialog.FileName;
                Run("导出当前项目台账", token => { service.Export(project, path, token); return path; }, saved => status.Text = "已导出 " + saved);
            }
        }

        private void Restore()
        {
            if (work != null || register == null || !register.Recovered) return;
            if (MessageBox.Show(this, "确认使用上一版恢复？损坏文件会保留。", "仪表资产",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Run("恢复台账", token => { service.Restore(project, token); return true; }, result => Reload());
        }

        private string ChooseFile(string title)
        {
            using (var dialog = new OpenFileDialog { InitialDirectory = project.Root, Title = title, CheckFileExists = true })
                return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
        }

        private string TagLabel(string id)
        {
            string value;
            return id.Length == 0 ? "" : tagLabels.TryGetValue(id, out value) ? value : id;
        }

        private static bool Number(string text, out decimal? number)
        {
            decimal parsed;
            number = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture, out parsed)) return false;
            number = parsed;
            return true;
        }

        private static string DescribeSource(EngineeringSource source)
        {
            return source == null ? "无附件" : source.Document + " · " + source.Version + "\r\nSHA-256 " + source.Sha256;
        }

        private static FlowLayoutPanel Bar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        }

        private static void Button(FlowLayoutPanel bar, string label, Action click)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += delegate { click(); };
            bar.Controls.Add(button);
        }

        private static DataGridView Grid(string name, params string[] columns)
        {
            var grid = new DataGridView { Name = name, Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            foreach (var column in columns) grid.Columns.Add(column, column);
            return grid;
        }
    }
}
