using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.AppServices;
using DeskBox.Domain;

namespace DeskBox.UI
{
    internal sealed class McpGatewayForm : Form
    {
        private readonly McpGateway gateway = new McpGateway();
        private readonly string scope;
        private readonly Func<string> currentScope;
        private readonly ComboBox servers;
        private readonly ListBox tools;
        private readonly TextBox token, schema, arguments, response;
        private readonly Label status;
        private readonly Button add, edit, toggle, discover, grant, revoke, invoke, cancel, receipts;
        private CancellationTokenSource operation;
        private McpCatalog catalog;
        private bool loadingServers;

        internal McpGatewayForm(string projectId, Func<string> currentScope)
        {
            scope = projectId;
            this.currentScope = currentScope;
            Name = "mcp-gateway-dialog";
            Text = "第三方 MCP";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(900, 670);
            MinimumSize = new Size(620, 500);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
            for (int row = 0; row < 4; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            servers = new ComboBox { Dock = DockStyle.Fill, Name = "mcp-registered-servers", DropDownStyle = ComboBoxStyle.DropDownList,
                FormattingEnabled = true, AccessibleName = "当前项目 MCP 服务" };
            servers.Format += delegate(object sender, ListControlConvertEventArgs args)
            {
                var item = (McpServer)args.ListItem;
                args.Value = item.Name + " · " + item.Transport + " · " + (item.Enabled ? "启用" : "停用");
            };
            servers.SelectedIndexChanged += delegate
            {
                if (loadingServers) return;
                token.Clear();
                SetCatalog(null);
                ShowReceipts();
                UpdateActions();
            };
            layout.Controls.Add(servers, 0, 0);

            var serverActions = Toolbar();
            add = AddButton(serverActions, "注册", "mcp-register", () => Edit(null));
            edit = AddButton(serverActions, "编辑", "mcp-edit", () => Edit(SelectedServer));
            toggle = AddButton(serverActions, "停用／启用", "mcp-enable", Toggle);
            discover = AddButton(serverActions, "检测并发现工具", "mcp-discover", () => Run(false));
            cancel = AddButton(serverActions, "取消运行", "mcp-cancel", delegate { if (operation != null) operation.Cancel(); });
            receipts = AddButton(serverActions, "调用记录", "mcp-history", ShowReceipts);
            layout.Controls.Add(serverActions, 0, 1);

            token = new TextBox { Width = 205, UseSystemPasswordChar = true, MaxLength = 4096,
                AccessibleName = "本次 HTTP Bearer 令牌", Name = "mcp-request-token" };
            var credential = Toolbar();
            credential.Controls.Add(new Label { Text = "本次 HTTP Bearer 令牌（可空）", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            credential.Controls.Add(token);
            layout.Controls.Add(credential, 0, 2);

            var toolActions = Toolbar();
            grant = AddButton(toolActions, "授权只读工具", "mcp-allow-tool", GrantTool);
            revoke = AddButton(toolActions, "撤销授权", "mcp-revoke-tool", RevokeGrant);
            invoke = AddButton(toolActions, "人工调用", "mcp-invoke", () => Run(true));
            layout.Controls.Add(toolActions, 0, 3);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            var toolPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            toolPane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            toolPane.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            tools = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, Name = "mcp-discovered-tools",
                DisplayMember = "Name", AccessibleName = "实际发现的工具" };
            tools.SelectedIndexChanged += delegate { ShowSchema(); UpdateActions(); };
            schema = TextArea("工具 Schema 与提示", true);
            toolPane.Controls.Add(tools, 0, 0);
            toolPane.Controls.Add(schema, 1, 0);
            split.Panel1.Controls.Add(toolPane);

            var callPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            callPane.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            callPane.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            callPane.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            callPane.Controls.Add(new Label { Text = "调用参数 JSON", AutoSize = true }, 0, 0);
            arguments = TextArea("调用参数 JSON", false);
            arguments.Name = "mcp-tool-arguments";
            arguments.MaxLength = 256 * 1024;
            arguments.Text = "{}";
            response = TextArea("真实响应或回执", true);
            response.Name = "mcp-call-receipt";
            callPane.Controls.Add(arguments, 0, 1);
            callPane.Controls.Add(response, 0, 2);
            split.Panel2.Controls.Add(callPane);
            layout.Controls.Add(split, 0, 4);

            status = new Label { Dock = DockStyle.Fill, UseMnemonic = false, AutoEllipsis = true, Name = "mcp-operation-status" };
            layout.Controls.Add(status, 0, 5);
            Controls.Add(layout);
            Shown += delegate
            {
                split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height * 45 / 100);
                LoadServers(null);
            };
        }

        private McpServer SelectedServer { get { return servers.SelectedItem as McpServer; } }
        private McpTool SelectedTool { get { return tools.SelectedItem as McpTool; } }
        private bool InScope { get { return scope == currentScope(); } }

        private void LoadServers(string select)
        {
            loadingServers = true;
            try
            {
                bool recovered;
                var items = gateway.Servers(scope, out recovered);
                servers.Items.Clear();
                servers.Items.AddRange(items.Cast<object>().ToArray());
                if (items.Count > 0) servers.SelectedIndex = Math.Max(0, items.FindIndex(item => item.Id == select));
                status.Text = recovered ? "从备份载入注册。全部服务已停用，需重新发现和授权。"
                    : items.Count == 0 ? "当前工程范围没有第三方 MCP 服务。请先注册。" : "当前工程范围 · " + items.Count + " 个服务";
            }
            catch (Exception error) { status.Text = error.Message; }
            finally { loadingServers = false; UpdateActions(); }
        }

        private void Edit(McpServer server)
        {
            if (!InScope) return;
            using (var editor = new McpRegistrationForm(server))
            {
                if (editor.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var saved = gateway.SaveServer(scope, editor.Value);
                    token.Clear();
                    SetCatalog(null);
                    LoadServers(saved.Id);
                    status.Text = saved.Enabled ? "注册已更新。" : "注册已保存，服务处于停用状态。";
                }
                catch (Exception error) { status.Text = error.Message; }
            }
        }

        private void Toggle()
        {
            var server = SelectedServer;
            if (!InScope || server == null) return;
            bool enable = !server.Enabled;
            if (enable && MessageBox.Show(this, server.Transport == "stdio"
                ? "仅启用可信的本机程序。执行时，程序使用当前 Windows 用户权限。确认启用？"
                : "请核对该端点是授权的 MCP 服务。确认启用？", Text, MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            server.Enabled = enable;
            try { gateway.SaveServer(scope, server); SetCatalog(null); LoadServers(server.Id); }
            catch (Exception error) { string message = error.Message; LoadServers(server.Id); status.Text = message; }
        }

        private async void Run(bool call)
        {
            var server = SelectedServer;
            var tool = SelectedTool;
            if (operation != null || !InScope || server == null || !server.Enabled || (call && tool == null)) return;
            if (call && MessageBox.Show(this, "将向 " + server.Name + " 发送已填写的参数，执行 " + tool.Name + "。确认本次为授权只读操作？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var run = new CancellationTokenSource();
            operation = run;
            string bearer = token.Text.Trim(), json = arguments.Text;
            response.Clear();
            status.Text = call ? "正在执行并等待真实 MCP 响应…" : "正在协商协议和读取工具目录…";
            UpdateActions();
            try
            {
                var result = await Task.Run(() => call
                    ? gateway.Invoke(scope, server.Id, tool.Name, json, true, bearer, run.Token)
                    : gateway.Discover(scope, server.Id, bearer, run.Token));
                if (IsDisposed || !InScope) return;
                if (result.Catalog != null) SetCatalog(result.Catalog, tool == null ? null : tool.Name);
                else if (!call) SetCatalog(null);
                var receipt = result.Receipt;
                status.Text = Describe(receipt) + (result.Error.Length == 0 ? "" : " · " + result.Error);
                response.Text = status.Text + "\r\n" + result.Output;
            }
            catch (Exception error) { if (!IsDisposed && InScope) status.Text = error.Message; }
            finally
            {
                operation = null;
                run.Dispose();
                if (!IsDisposed) UpdateActions();
            }
        }

        private void GrantTool()
        {
            var tool = SelectedTool;
            if (!InScope || tool == null || catalog == null) return;
            if (MessageBox.Show(this, "已核对 " + tool.Name + " 的文档和 Schema，确认授权此版本的只读工具？",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                var server = gateway.SetToolGrant(scope, catalog, tool.Name, true);
                LoadServers(server.Id);
                status.Text = "授权已保存。每次调用仍需确认。";
            }
            catch (Exception error) { status.Text = error.Message; }
        }

        private void RevokeGrant()
        {
            var server = SelectedServer;
            if (!InScope || server == null || server.Grants.Count == 0) return;
            var menu = new ContextMenuStrip();
            foreach (var permission in server.Grants)
            {
                var name = permission.Tool;
                menu.Items.Add(name.Replace("&", "&&"), null, delegate
                {
                    try
                    {
                        gateway.RevokeTool(scope, server.Id, name);
                        LoadServers(server.Id);
                        status.Text = "已撤销工具授权 · " + name;
                    }
                    catch (Exception error) { status.Text = error.Message; }
                });
            }
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(revoke, new Point(0, revoke.Height));
        }

        internal void ScopeChanged()
        {
            if (operation != null) operation.Cancel();
            if (!IsDisposed) Close();
        }

        private void SetCatalog(McpCatalog value, string selected = null)
        {
            catalog = value;
            tools.Items.Clear();
            schema.Clear();
            if (value != null)
            {
                tools.Items.AddRange(value.Tools.Cast<object>().ToArray());
                if (value.Tools.Count > 0) tools.SelectedIndex = Math.Max(0, value.Tools.ToList().FindIndex(tool => tool.Name == selected));
                else schema.Text = value.ServerName + " · 当前没有可列出的工具。";
            }
        }

        private void ShowSchema()
        {
            var tool = SelectedTool;
            schema.Text = tool == null ? "" : catalog.ServerName + " " + catalog.ServerVersion + " · " + catalog.ProtocolVersion + "\r\n"
                + tool.Description + "\r\n\r\n输入 Schema\r\n" + tool.InputSchema + "\r\n\r\n输出 Schema\r\n" + tool.OutputSchema
                + "\r\n\r\n服务提示\r\n" + tool.Annotations;
        }

        private void ShowReceipts()
        {
            if (response == null) return;
            var server = SelectedServer;
            response.Clear();
            if (!InScope || server == null) return;
            try
            {
                var history = gateway.Receipts(scope, server.Id);
                response.Text = history.Count == 0 ? "该服务没有调用记录。" : string.Join("\r\n", history.Select(item =>
                    DateTimeOffset.FromUnixTimeMilliseconds(item.StartedAt).ToLocalTime().ToString("MM-dd HH:mm:ss") + " · " + Describe(item)));
            }
            catch (Exception error) { response.Text = error.Message; }
        }

        private static string Describe(McpReceipt receipt)
        {
            return receipt.Operation + " " + receipt.Tool + " · " + receipt.State +
                (receipt.ErrorKind.Length == 0 ? "" : "/" + receipt.ErrorKind) + " · " + receipt.ElapsedMs + " ms · "
                + receipt.ToolCount + " 个工具" + (receipt.Dispatched ? " · 已尝试发送" : "") +
                (receipt.HttpStatus == 0 ? "" : " · HTTP " + receipt.HttpStatus) +
                (receipt.RpcCode.HasValue ? " · RPC " + receipt.RpcCode : "") + " · " + receipt.Id;
        }

        private void UpdateActions()
        {
            if (add == null || tools == null) return;
            var server = SelectedServer;
            var tool = SelectedTool;
            bool idle = InScope && operation == null;
            bool registered = idle && server != null;
            bool available = registered && server.Enabled;
            bool authorized = tool != null && server != null && server.Grants.Any(item => item.Tool == tool.Name);
            add.Enabled = servers.Enabled = idle;
            edit.Enabled = toggle.Enabled = receipts.Enabled = registered;
            discover.Enabled = available;
            token.Enabled = available && server.Transport == "http";
            grant.Enabled = available && tool != null && !tool.ProhibitedByHints;
            revoke.Enabled = registered && server.Grants.Count > 0;
            invoke.Enabled = available && authorized && !tool.ProhibitedByHints;
            cancel.Enabled = operation != null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && operation != null) operation.Cancel();
            base.Dispose(disposing);
        }

        private static FlowLayoutPanel Toolbar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        }

        private static Button AddButton(Control parent, string text, string name, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Name = name };
            button.Click += delegate { action(); };
            parent.Controls.Add(button);
            return button;
        }

        private static TextBox TextArea(string name, bool readOnly)
        {
            return new TextBox { AccessibleName = name, Dock = DockStyle.Fill, ReadOnly = readOnly, Multiline = true,
                ScrollBars = ScrollBars.Both, WordWrap = false };
        }
    }

    internal sealed class McpRegistrationForm : Form
    {
        private readonly TextBox title, address, arguments;
        private readonly ComboBox transport;
        private readonly NumericUpDown deadline;
        private readonly McpServer original;

        internal McpRegistrationForm(McpServer server)
        {
            original = server;
            Name = "mcp-registration-dialog";
            Text = server == null ? "注册 MCP 服务" : "编辑 MCP 服务";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(600, 250);
            MinimumSize = new Size(460, 220);
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            title = new TextBox { Text = server == null ? "" : server.Name, MaxLength = 120, Name = "mcp-name" };
            transport = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Name = "mcp-transport" };
            transport.Items.AddRange(new object[] { "HTTP", "stdio" });
            transport.SelectedIndex = server != null && server.Transport == "stdio" ? 1 : 0;
            address = new TextBox { Text = server == null ? "" : server.Address, MaxLength = 2048, Name = "mcp-address" };
            arguments = new TextBox { Text = server == null ? "" : server.Arguments, MaxLength = 4096, Name = "mcp-process-arguments" };
            arguments.Enabled = transport.SelectedIndex == 1;
            transport.SelectedIndexChanged += delegate { arguments.Enabled = transport.SelectedIndex == 1; };
            deadline = new NumericUpDown { Minimum = 1, Maximum = 120, Value = server == null ? 15 : server.TimeoutMs / 1000,
                Name = "mcp-timeout" };
            AddRow(form, 0, "服务名称", title);
            AddRow(form, 1, "传输", transport);
            AddRow(form, 2, "地址／绝对路径", address);
            AddRow(form, 3, "stdio 启动参数", arguments);
            AddRow(form, 4, "超时（秒）", deadline);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            AcceptButton = new Button { Text = "保存", AutoSize = true, DialogResult = DialogResult.OK, Name = "mcp-save" };
            CancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { (Control)AcceptButton, (Control)CancelButton });
            form.Controls.Add(buttons, 1, 5);
            Controls.Add(form);
        }

        internal McpServer Value
        {
            get
            {
                return new McpServer { Id = original == null ? "" : original.Id, Revision = original == null ? 0 : original.Revision,
                    Enabled = original != null && original.Enabled, Name = title.Text, Address = address.Text,
                    Transport = transport.SelectedIndex == 0 ? "http" : "stdio",
                    Arguments = transport.SelectedIndex == 0 ? "" : arguments.Text, TimeoutMs = (int)deadline.Value * 1000 };
            }
        }

        private static void AddRow(TableLayoutPanel form, int row, string name, Control input)
        {
            form.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            form.Controls.Add(new Label { AutoSize = true, Text = name, Anchor = AnchorStyles.Left }, 0, row);
            input.Dock = DockStyle.Fill;
            input.AccessibleName = name;
            form.Controls.Add(input, 1, row);
        }
    }
}
