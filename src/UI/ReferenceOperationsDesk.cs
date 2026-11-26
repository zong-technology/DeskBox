using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Engineering;
using DeskBox.UI.Views;

namespace DeskBox.UI
{
    public partial class ReferenceWorkspace
    {
        private Control BuildOperationsDesk()
        {
            var page = Page("工程工作区", "当前项目的工程上下文、设备资料与常用操作");
            var overview = Button("项目总览", () => Navigate("home"), false, 116);
            SetSampleHeading(page, "OPERATIONS DESK", "进入工具矩阵", () => Navigate("engineering"), overview);

            var host = new Panel { Dock = DockStyle.Fill };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19.3f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57.3f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 23.4f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var launchButtons = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6 };
            launchButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var launchScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            launchScroll.Controls.Add(launchButtons);
            var launchers = new[] {
                new[] { "TIA Portal", "SIMATIC STEP 7", "TIA Portal" },
                new[] { "WinCC", "工程组态", "WinCC" },
                new[] { "DeltaV", "DCS 工程工具", "DeltaV" },
                new[] { "Kepware", "OPC 服务器", "Kepware OPC UA" },
                new[] { "OPC UA", "客户端 / 本机诊断", "SCADA / OPC" },
                new[] { "S7-1200 / 1500", "PLC 工程 · TIA", "TIA Portal" }
            };
            for (int i = 0; i < launchers.Length; i++)
            {
                var item = launchers[i];
                string key = item[2];
                var tile = OperationsAction(item[0], "\uE7F4", item[1], () => LaunchEngineeringTool(key));
                tile.Dock = DockStyle.Fill;
                tile.Margin = new Padding(0, 0, 0, 8);
                launchButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / launchers.Length));
                launchButtons.Controls.Add(tile, 0, i);
            }
            Action sizeLaunchers = () =>
            {
                launchButtons.Height = grid.Width >= 1400 ? 488 : 368;
                launchScroll.AutoScroll = launchButtons.Height > launchScroll.ClientSize.Height;
            };
            launchScroll.Resize += delegate { sizeLaunchers(); };
            sizeLaunchers();
            Label launchHint;
            var launch = OperationsPanel("工程软件启动台", "首次点击选择本机程序", launchScroll, out launchHint);

            Action<int> showTools = mode =>
            {
                Navigate("engineering");
                var selected = _pages["engineering"].Controls.Find("engineering-tool-mode", true).FirstOrDefault() as DarkSelect;
                if (selected != null) selected.SelectedIndex = mode;
            };
            var toolCards = new[] {
                OperationsAction("S7-1200 / 1500", "\uE950", "PLC 网络诊断", () => showTools(2)),
                OperationsAction("OPC UA", "\uE968", "启动本机客户端", () => LaunchEngineeringTool("SCADA / OPC")),
                OperationsAction("Modbus", "\uE8B7", "寄存器与字节序", () => showTools(1)),
                OperationsAction("4–20 mA", "\uE90F", "线性量程换算", () => Navigate("calculator")),
                OperationsAction("FAT / SAT", "\uE99A", "制药验证与记录", () => showTools(3)),
                OperationsAction("I/O 点表", "\uE8A5", "搜索项目工程资料", () =>
                {
                    Navigate("search");
                    var project = CurrentProject();
                    var search = _serviceViews["search"] as SearchView;
                    if (project != null && search != null) search.SetProjectScope(project.Root, project.Name);
                }),
                OperationsAction("PLC 地址", "\uE73A", "S7 地址解析", () => showTools(1)),
                OperationsAction("通信诊断", "\uE823", "按需测试 Ping / TCP", () => showTools(2)),
                OperationsAction("工程转换", "\uE81E", "压力、温度与仪表", () => showTools(0))
            };
            var toolGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 3 };
            for (int i = 0; i < 3; i++)
            {
                toolGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                toolGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            }
            for (int i = 0; i < toolCards.Length; i++)
            {
                toolCards[i].Dock = DockStyle.Fill;
                toolCards[i].Margin = new Padding(0, 0, 10, 10);
                toolGrid.Controls.Add(toolCards[i], i % 3, i / 3);
            }
            var matrixScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            matrixScroll.Controls.Add(toolGrid);
            Action sizeMatrix = () => toolGrid.Height = grid.Width >= 1400 ? 374 : 250;
            matrixScroll.Resize += delegate { sizeMatrix(); };
            sizeMatrix();
            Label projectHint;
            var matrix = OperationsPanel("工程工具矩阵", "", matrixScroll, out projectHint);

            var facts = new DarkList { Dock = DockStyle.Fill, Palette = _p, AutoFitColumns = true,
                CardSurface = true, InsetRows = true, ShowHeader = false, RowHeight = 57, Font = FontOf(9.5f) };
            facts.SetColumns(new[] { "事项", "状态" }, new[] { 65, 225 });
            facts.RowActivated += delegate
            {
                var row = facts.SelectedRow;
                var route = row == null ? null : row.Tag as string;
                if (route != null) Navigate(route);
            };
            Label statusHint;
            var status = OperationsPanel("工程状态", "项目、任务、现场事件与同步", facts, out statusHint);

            grid.Controls.Add(launch, 0, 0);
            grid.Controls.Add(matrix, 1, 0);
            grid.Controls.Add(status, 2, 0);
            host.Controls.Add(grid);
            _reload["operations"] = delegate
            {
                var project = CurrentProject();
                projectHint.Text = project == null ? "当前项目 · 未选择" : "当前项目 · " + project.Name;
                var scope = _projects.Context;
                int tasks = _store.Tasks.Count(t => !t.Done && (scope == null || scope.IncludesTask(t)));
                int incidents = _sync.Incidents.Count(item => item.Status != "closed" && (project == null || item.ProjectId == project.Id));
                facts.SetRows(new List<ListRow> {
                    OperationFact("当前项目", project == null ? "未选择项目" : project.Name, "projects"),
                    OperationFact("验证进度", ValidationProgress.Display(project), "projects"),
                    OperationFact("待办任务", tasks == 0 ? "暂无待办" : tasks + " 项未完成", "tasks"),
                    OperationFact("现场事件", incidents == 0 ? "暂无未关闭事件" : incidents + " 项未关闭", "incidents"),
                    OperationFact("私人云", _cloud.Connected ? "在线" : _cloud.LoggedIn ? "连接中" : "未连接", "mobile")
                });
            };
            AddMain(page, host);
            return page;
        }

        private Panel OperationsPanel(string title, string subtitle, Control body, out Label hint)
        {
            var card = new ReferenceCard(_p) { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = new Padding(0, 0, 10, 0) };
            var heading = TextLabel(title, 12, true);
            heading.Dock = DockStyle.Top;
            heading.Height = 25;
            hint = TextLabel(subtitle, 9, false, _p.TextDim);
            hint.Dock = DockStyle.Top;
            hint.Height = 29;
            card.Controls.Add(body);
            card.Controls.Add(hint);
            card.Controls.Add(heading);
            return card;
        }

        private DesktopActionCard OperationsAction(string label, string glyph, string detail, Action action)
        {
            var tile = new DesktopActionCard(label, glyph, detail) { Palette = _p, Width = 130, Height = 56, Margin = new Padding(0, 0, 0, 8) };
            tile.Click += delegate
            {
                try { action(); }
                catch (Exception error) { _status.Text = error.Message; }
            };
            return tile;
        }

        private static ListRow OperationFact(string title, string detail, string route)
        {
            return new ListRow { Cells = new[] { title, detail }, Tag = route, Tip = detail };
        }
    }
}
