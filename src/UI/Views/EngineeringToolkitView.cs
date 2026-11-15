using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using DeskBox.Engineering;

namespace DeskBox.UI.Views
{
    /// <summary>面向自动化/PLC/DCS/制药验证工程师的真实工具箱。</summary>
    public partial class EngineeringToolkitView : ViewBase
    {
        private DarkSelect _mode;
        private Panel _page;
        private Label _status;
        private bool _referenceLayout,_building;
        private int _layoutWidth;
        public void SelectMode(int index){_mode.SelectedIndex=Math.Max(0,Math.Min(3,index));}
        public override void UseReferenceLayout(){base.UseReferenceLayout();_referenceLayout=true;_mode.Parent.Visible=false;BuildPage();}

        public EngineeringToolkitView(Store store) : base(store, "工程工具矩阵", "4–20mA · Pt100 · S7 地址 · Modbus Float · 网络诊断 · GxP/CSV 验证模板")
        {
            Build();
        }

        private void Build()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Palette.Bg };
            _mode = new DarkSelect(new[] { "信号 / 仪表", "Siemens PLC / Modbus", "网络诊断", "制药验证 / CSV" }) { Left = 0, Top = 3, Width = 210 };
            _mode.SelectedIndexChanged += delegate { BuildPage(); };
            _status = new Label { Left = 224, Top = 11, AutoSize = true, ForeColor = Palette.TextFaint, BackColor = Palette.Bg, Font = new Font("Microsoft YaHei UI", 8.5F), Text = "所有计算均在本地完成" };
            top.Controls.AddRange(new Control[] { _mode, _status });
            _page = new Panel { Dock = DockStyle.Fill, BackColor = Palette.Bg, AutoScroll = true };
            _page.Resize+=delegate{if(_referenceLayout&&!_building&&_page.Width!=_layoutWidth)LayoutReferenceCards();};
            Body.Controls.Add(_page);
            Body.Controls.Add(top);
            _page.BringToFront();
            _mode.SelectedIndex = 0;
            BuildPage();
        }

        private void BuildPage()
        {
            if(_building)return;_building=true;_layoutWidth=_page.Width;
            _page.SuspendLayout();
            while(_page.Controls.Count>0)_page.Controls[0].Dispose();
            try{
            switch (_mode.SelectedIndex)
            {
                case 1: BuildPlcPage(); break;
                case 2: BuildNetworkPage(); break;
                case 3: BuildValidationPage(); break;
                default: if(_referenceLayout)BuildReferenceSignalPage();else BuildSignalPage(); break;
            }
            }finally{_page.ResumeLayout(true);_building=false;if(_referenceLayout)LayoutReferenceCards();}
        }
        private void LayoutReferenceCards()
        {
            _layoutWidth = _page.Width;
            int available = Math.Max(1, _page.ClientSize.Width - 22);
            var cards = new List<Panel>();
            foreach (Control child in _page.Controls)
                if (child is Panel) cards.Add((Panel)child);
            if (cards.Count == 0) return;
            int first = MinimumToolCardWidth(cards[0]);
            int second = cards.Count > 1 ? MinimumToolCardWidth(cards[1]) : 0;
            bool stack = cards.Count > 1 && first + second + 14 > available;
            int leftWidth = stack ? available : Math.Max(first,
                Math.Min(available - second - 14, (available - 14) * 55 / 100));
            int y = 8;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (i == 1 && !stack)
                {
                    card.Location = new Point(20 + leftWidth, 8);
                    card.Width = available - leftWidth - 14;
                    y = Math.Max(y, card.Bottom + 14);
                }
                else
                {
                    card.Location = new Point(6, y);
                    card.Width = Math.Max(MinimumToolCardWidth(card), i == 0 && !stack ? leftWidth : available);
                    y = card.Bottom + 14;
                }
                foreach (Control child in card.Controls)
                {
                    var label = child as Label;
                    if (label != null && !label.AutoSize && (label.Top == 40 || label.Height >= 32))
                        label.Width = Math.Max(1, card.Width - label.Left - 16);
                }
            }
        }

        private static int MinimumToolCardWidth(Panel card)
        {
            int width = 290;
            foreach (Control child in card.Controls)
                if (child is DarkInput || child is DarkSelect || child is DarkButton)
                    width = Math.Max(width, child.Right + 16);
            return width;
        }

        private Panel Card(string title, string hint, int x, int y, int w, int h)
        {
            var p = new Panel { Left = x, Top = y, Width = w, Height = h, BackColor = Palette.CardBg, Padding = new Padding(16) };
            p.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Palette.DrawRounded(e.Graphics, Color.FromArgb(120, Palette.Accent), new Rectangle(0, 0, p.Width - 1, p.Height - 1), 12, 1f);
            };
            p.Controls.Add(new Label { Text = title, Left = 16, Top = 14, AutoSize = true, ForeColor = Palette.Text, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold) });
            p.Controls.Add(new Label { Text = hint, Left = 16, Top = 40, Width = w - 32, Height = 34, ForeColor = Palette.TextFaint, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 8.2F) });
            _page.Controls.Add(p);
            return p;
        }

        private DarkInput Input(Panel p, string placeholder, int x, int y, int w, string value)
        {
            var i = new DarkInput { Left = x, Top = y, Width = w, Placeholder = placeholder, Text = value };
            i.ApplyPalette(Palette); p.Controls.Add(i); return i;
        }

        private Label Output(Panel p, int x, int y, int w)
        {
            var l = new Label { Left = x, Top = y, Width = w, Height = 48, ForeColor = Palette.Accent, BackColor = Palette.CardBg, Font = new Font("Consolas", 11F, FontStyle.Bold), Text = "等待计算…" };
            p.Controls.Add(l); return l;
        }

        private void BuildSignalPage()
        {
            int total = Math.Max(1000, _page.ClientSize.Width - 18);
            int leftW = (total - 14) * 55 / 100;
            int rightW = total - 14 - leftW;
            var scale = Card("4–20 mA 线性量程", "支持正量程与反量程；同时显示百分比和越界状态。", 6, 8, leftW, 260);
            var ma = Input(scale, "电流 mA", 16, 82, 120, "12");
            var low = Input(scale, "量程下限", 144, 82, 120, "0");
            var high = Input(scale, "量程上限", 272, 82, 120, "100");
            var calc = new DarkButton("mA → 工程值") { Primary = true, Left = 400, Top = 82, Width = 150 };
            var value = Input(scale, "工程值（反算）", 16, 126, 160, "50");
            var reverse = new DarkButton("工程值 → mA") { Left = 184, Top = 126, Width = 150 };
            var scaleOut = Output(scale, 16, 178, 560);
            calc.Click += delegate
            {
                try { var r = EngineeringTools.FromCurrent(D(ma.Text), D(low.Text), D(high.Text)); scaleOut.Text = r.Ok ? "VALUE = " + r.Value.ToString("0.####") + "    SPAN = " + r.Percent.ToString("0.##") + "%" + (r.Percent < 0 || r.Percent > 100 ? "  [越界]" : "") : r.Error; } catch (Exception ex) { scaleOut.Text = ex.Message; }
            };
            reverse.Click += delegate
            {
                try { var r = EngineeringTools.ToCurrent(D(value.Text), D(low.Text), D(high.Text)); scaleOut.Text = r.Ok ? "CURRENT = " + r.Value.ToString("0.####") + " mA    SPAN = " + r.Percent.ToString("0.##") + "%" : r.Error; } catch (Exception ex) { scaleOut.Text = ex.Message; }
            };
            scale.Controls.AddRange(new Control[] { calc, reverse });

            var pt = Card("Pt100 / IEC 60751", "温度与电阻双向换算，支持 -200…850°C。", 20 + leftW, 8, rightW, 260);
            var temp = Input(pt, "温度 °C", 16, 82, 140, "100");
            var res = Input(pt, "电阻 Ω", 164, 82, 140, "138.5055");
            var t2r = new DarkButton("温度 → 电阻") { Primary = true, Left = 312, Top = 82, Width = 140 };
            var r2t = new DarkButton("电阻 → 温度") { Left = 312, Top = 126, Width = 140 };
            var ptOut = Output(pt, 16, 178, 450);
            t2r.Click += delegate { try { ptOut.Text = EngineeringTools.Pt100Resistance(D(temp.Text)).ToString("0.####") + " Ω"; } catch (Exception ex) { ptOut.Text = ex.Message; } };
            r2t.Click += delegate { try { ptOut.Text = EngineeringTools.Pt100Temperature(D(res.Text)).ToString("0.###") + " °C"; } catch (Exception ex) { ptOut.Text = ex.Message; } };
            pt.Controls.AddRange(new Control[] { t2r, r2t });

            var units = Card("压力 / 温度单位", "常用仪表工程单位快速转换。", 6, 282, total, 210);
            var unitValue = Input(units, "数值", 16, 82, 130, "1");
            var pressureFrom = new DarkSelect(new[] { "Pa", "kPa", "MPa", "bar", "mbar", "psi", "kgf/cm²" }) { Left = 154, Top = 82, Width = 110 };
            var pressureTo = new DarkSelect(new[] { "Pa", "kPa", "MPa", "bar", "mbar", "psi", "kgf/cm²" }) { Left = 272, Top = 82, Width = 110 };
            pressureFrom.SelectedIndex = 3; pressureTo.SelectedIndex = 1;
            var pc = new DarkButton("转换压力") { Primary = true, Left = 390, Top = 82, Width = 110 };
            var tempFrom = new DarkSelect(new[] { "C", "F", "K" }) { Left = 540, Top = 82, Width = 80 };
            var tempTo = new DarkSelect(new[] { "C", "F", "K" }) { Left = 628, Top = 82, Width = 80 };
            tempFrom.SelectedIndex = 0; tempTo.SelectedIndex = 1;
            var tc = new DarkButton("转换温度") { Left = 716, Top = 82, Width = 110 };
            var unitOut = Output(units, 16, 140, 1000);
            pc.Click += delegate { try { var a = pressureFrom.Inner.Text; var b = pressureTo.Inner.Text; unitOut.Text = D(unitValue.Text).ToString("0.####") + " " + a + " = " + EngineeringTools.ConvertPressure(D(unitValue.Text), a, b).ToString("0.######") + " " + b; } catch (Exception ex) { unitOut.Text = ex.Message; } };
            tc.Click += delegate { try { var a = tempFrom.Inner.Text; var b = tempTo.Inner.Text; unitOut.Text = D(unitValue.Text).ToString("0.####") + " °" + a + " = " + EngineeringTools.ConvertTemperature(D(unitValue.Text), a, b).ToString("0.####") + " °" + b; } catch (Exception ex) { unitOut.Text = ex.Message; } };
            units.Controls.AddRange(new Control[] { pressureFrom, pressureTo, pc, tempFrom, tempTo, tc });
        }

        private void BuildPlcPage()
        {
            int total = Math.Max(1000, _page.ClientSize.Width - 18);
            int leftW = (total - 14) * 58 / 100;
            int rightW = total - 14 - leftW;
            var s7 = Card("Siemens S7 地址解析器", "检查 DB/I/Q/M 地址，输出规范地址、字节偏移、位号与数据宽度。", 6, 8, leftW, 250);
            var addr = Input(s7, "例如 DB10.DBX2.3 / QW2 / MD10", 16, 82, 330, "DB10.DBX2.3");
            var parse = new DarkButton("解析地址") { Primary = true, Left = 354, Top = 82, Width = 110 };
            var s7Out = Output(s7, 16, 136, 650);
            parse.Click += delegate { var r = EngineeringTools.ParseS7Address(addr.Text); s7Out.Text = r.Ok ? r.Canonical + "\r\n" + r.Description : "地址错误：" + r.Error; };
            s7.Controls.Add(parse);

            var modbus = Card("Modbus 32-bit Float 解码", "输入两个 16 位寄存器（十进制或 0x 十六进制），支持常见字节序。", 20 + leftW, 8, rightW, 250);
            var r1 = Input(modbus, "寄存器 1", 16, 82, 120, "0x4145");
            var r2 = Input(modbus, "寄存器 2", 144, 82, 120, "0x8548");
            var order = new DarkSelect(new[] { "ABCD", "BADC", "CDAB", "DCBA" }) { Left = 272, Top = 82, Width = 90 };
            order.SelectedIndex = 0;
            var decode = new DarkButton("解码") { Primary = true, Left = 370, Top = 82, Width = 80 };
            var mbOut = Output(modbus, 16, 140, 430);
            decode.Click += delegate { try { var a = EngineeringTools.ParseRegister(r1.Text); var b = EngineeringTools.ParseRegister(r2.Text); var v = EngineeringTools.DecodeModbusFloat(a, b, order.Inner.Text); mbOut.Text = "FLOAT32 = " + v.ToString("G9", CultureInfo.InvariantCulture) + "\r\nREG = " + a + " / " + b; } catch (Exception ex) { mbOut.Text = ex.Message; } };
            modbus.Controls.AddRange(new Control[] { order, decode });

            var refs = Card("工程速查", "常用工业协议端口与西门子数据宽度。", 6, 272, total, 220);
            refs.Controls.Add(new Label { Left = 16, Top = 78, Width = 1120, Height = 116, ForeColor = Palette.TextDim, BackColor = Palette.CardBg, Font = new Font("Consolas", 10F), Text = "S7comm / ISO-on-TCP : TCP 102     Modbus/TCP : TCP 502     OPC UA : TCP 4840\r\nPROFINET DCP : Layer 2             WinCC OA 常用 : 4999/5678（以项目配置为准）\r\nBOOL=1 bit   BYTE=8 bit   WORD/INT=16 bit   DWORD/DINT/REAL=32 bit   LREAL=64 bit\r\n注意：在线修改 PLC/DCS 前必须执行备份、差异审查、变更控制和回退方案。" });
        }

        private void BuildNetworkPage()
        {
            int total = Math.Max(1000, _page.ClientSize.Width - 18);
            int leftW = (total - 14) * 68 / 100;
            int rightW = total - 14 - leftW;
            var diag = Card("PLC / DCS 网络诊断", "真实执行 DNS、ICMP Ping 与 TCP 端口连接测试；不会扫描网段。", 6, 8, leftW, 280);
            var host = Input(diag, "PLC / 服务器 IP 或主机名", 16, 82, 260, "192.168.0.1");
            var port = Input(diag, "TCP 端口", 284, 82, 100, "102");
            var preset = new DarkSelect(new[] { "S7 102", "Modbus 502", "OPC UA 4840", "HTTPS 443", "RDP 3389" }) { Left = 392, Top = 82, Width = 130 };
            preset.SelectedIndexChanged += delegate { var t = preset.Inner.Text.Split(' '); if (t.Length > 1) port.Text = t[t.Length - 1]; };
            var ping = new DarkButton("Ping") { Primary = true, Left = 530, Top = 82, Width = 90 };
            var tcp = new DarkButton("测试端口") { Left = 628, Top = 82, Width = 110 };
            var output = Output(diag, 16, 140, 760); output.Height = 100;
            ping.Click += delegate { RunNetwork(delegate { using (var p = new Ping()) { var reply = p.Send(host.Text.Trim(), 2500); return "DNS/IP = " + reply.Address + "\r\nPING = " + reply.Status + (reply.Status == IPStatus.Success ? " · " + reply.RoundtripTime + " ms · TTL " + reply.Options.Ttl : ""); } }, output); };
            tcp.Click += delegate { RunNetwork(delegate { int po; if (!int.TryParse(port.Text, out po) || po < 1 || po > 65535) throw new ArgumentException("端口必须是 1…65535"); var addresses = Dns.GetHostAddresses(host.Text.Trim()); var ip = addresses.Length > 0 ? addresses[0] : null; if (ip == null) throw new Exception("DNS 未返回地址"); using (var c = new TcpClient()) { var ar = c.BeginConnect(ip, po, null, null); bool ok = ar.AsyncWaitHandle.WaitOne(2500); if (!ok) return "TCP " + ip + ":" + po + " = TIMEOUT / FILTERED"; c.EndConnect(ar); return "TCP " + ip + ":" + po + " = OPEN"; } }, output); };
            diag.Controls.AddRange(new Control[] { preset, ping, tcp });

            var checklist = Card("现场网络排障顺序", "适合 PLC、DCS、SCADA 与验证环境的低风险检查路径。", 20 + leftW, 8, rightW, 460);
            checklist.Controls.Add(new Label { Left = 16, Top = 76, Width = 320, Height = 350, ForeColor = Palette.TextDim, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 9F), Text = "01 记录当前故障时间与影响范围\r\n\r\n02 核对 IP / 掩码 / 网关 / VLAN\r\n\r\n03 Ping 仅证明 ICMP，不代表应用端口可用\r\n\r\n04 测试 TCP 102 / 502 / 4840\r\n\r\n05 核对交换机端口、链路、错误计数\r\n\r\n06 核对 PLC/DCS 诊断缓冲区\r\n\r\n07 检查时间同步与证书有效期\r\n\r\n08 变更前备份，记录差异和回退方案\r\n\r\n⚠ 不在生产网络执行未经批准的端口扫描" });
        }

        private void RunNetwork(Func<string> work, Label output)
        {
            output.Text = "正在诊断…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text;
                try { text = work(); } catch (Exception ex) { text = "诊断失败：" + ex.Message; }
                try { if (!IsDisposed) BeginInvoke(new Action(delegate { output.Text = text; Store.Log("info", "network", text.Replace("\r\n", " · ")); })); } catch { }
            });
        }

        private void BuildValidationPage()
        {
            int total = Math.Max(1000, _page.ClientSize.Width - 18);
            int leftW = (total - 14) * 36 / 100;
            int rightW = total - 14 - leftW;
            var config = Card("GxP / CSV 文档生成器", "生成可编辑 Markdown 检查清单；内容覆盖 GAMP 5、ALCOA+、审计追踪、备份恢复和自动化测试。", 6, 8, leftW, 470);
            var type = new DarkSelect(ValidationTemplates.Types) { Left = 16, Top = 82, Width = 250 }; type.SelectedIndex = 0;
            var system = Input(config, "系统/设备名称", 16, 126, 360, "制药设备控制系统");
            var owner = Input(config, "负责人", 16, 170, 360, "");
            var preview = new DarkButton("生成预览") { Primary = true, Left = 16, Top = 218, Width = 110 };
            var export = new DarkButton("导出 Markdown") { Left = 134, Top = 218, Width = 130 };
            var tasks = new DarkButton("创建验证任务") { Left = 272, Top = 218, Width = 120 };
            var hint = new Label { Left = 16, Top = 274, Width = 380, Height = 150, ForeColor = Palette.TextDim, BackColor = Palette.CardBg, Font = new Font("Microsoft YaHei UI", 8.8F), Text = "建议用法：\r\n· URS：从工艺、数据完整性和接口需求开始\r\n· DQ：做设计与需求追溯\r\n· IQ：记录硬件、软件、网络和基线\r\n· OQ：测试功能、报警、联锁和审计追踪\r\n· PQ：执行代表性批次与最差条件\r\n· Part 11：检查电子记录与电子签名" };
            config.Controls.AddRange(new Control[] { type, preview, export, tasks, hint });

            var result = Card("文档预览", "生成结果可直接保存到验证项目目录并纳入版本控制。", 20 + leftW, 8, rightW, 570);
            var text = new TextBox { Left = 16, Top = 76, Width = Math.Max(300, rightW - 34), Height = 470, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Palette.PanelAlt, ForeColor = Palette.Text, Font = new Font("Consolas", 9F), ReadOnly = false, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            result.Controls.Add(text);
            Func<string> generate = delegate { var value = ValidationTemplates.Generate(type.Inner.Text, system.Text, owner.Text); text.Text = value; return value; };
            preview.Click += delegate { generate(); _status.Text = "已生成本地验证清单，可继续编辑"; };
            export.Click += delegate
            {
                var value = generate();
                using (var d = new SaveFileDialog { Filter = "Markdown (*.md)|*.md|文本 (*.txt)|*.txt", FileName = SafeName(system.Text) + "-" + type.Inner.Text.Split(' ')[0] + ".md" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllText(d.FileName, value, new System.Text.UTF8Encoding(false));
                    Store.Log("info", "validation", "导出验证模板：" + d.FileName);
                    _status.Text = "已导出：" + d.FileName;
                }
            };
            tasks.Click += delegate
            {
                var titles = ValidationTemplates.TaskTitles(type.Inner.Text, system.Text);
                if (!Prompt.Confirm(this, "创建验证任务", "将创建 " + titles.Count + " 条任务，用于跟踪「" + type.Inner.Text + "」。继续？", Palette)) return;
                foreach (var title in titles) Store.Tasks.Add(new TaskItem { Id = "t_" + Guid.NewGuid().ToString("N").Substring(0, 10), Title = title, Priority = "high", Project = system.Text, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                Store.SaveTasksOnly(); Store.Log("info", "validation", "创建验证任务 " + titles.Count + " 条：" + system.Text); _status.Text = "已创建 " + titles.Count + " 条验证任务";
            };
        }

        private static double D(string value)
        {
            double d;
            if (double.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out d)) return d;
            if (double.TryParse((value ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("请输入有效数字：" + value);
        }

        private static string SafeName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "验证文档" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return value;
        }

        protected override void OnActivated() { BuildPage(); }
    }
}
