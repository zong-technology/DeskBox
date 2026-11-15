// 工程工具与 AI 工程模式验收。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;
using DeskBox.Engineering;

public class EngineeringAccept
{
    static int pass, fail;
    static void C(string n, bool ok, string d) { if (ok) { pass++; Console.WriteLine("PASS  " + n + "  [" + d + "]"); } else { fail++; Console.WriteLine("FAIL  " + n + "  [" + d + "]"); } }
    static bool Near(double a, double b, double e = 0.001) { return Math.Abs(a - b) <= e; }

    public static int Main()
    {
        Console.WriteLine("== 信号与仪表 ==");
        var s = EngineeringTools.FromCurrent(12, 0, 100);
        C("12mA 对应 50%", s.Ok && Near(s.Percent, 50) && Near(s.Value, 50), s.Value + "/" + s.Percent);
        var r = EngineeringTools.ToCurrent(75, 0, 100);
        C("75% 反算 16mA", r.Ok && Near(r.Value, 16), r.Value + "mA");
        C("反量程可计算", Near(EngineeringTools.FromCurrent(8, 100, 0).Value, 75), EngineeringTools.FromCurrent(8, 100, 0).Value.ToString());
        C("拒绝零量程", !EngineeringTools.FromCurrent(12, 10, 10).Ok, "ok");
        C("1bar=100kPa", Near(EngineeringTools.ConvertPressure(1, "bar", "kPa"), 100), "100");
        C("0°C=32°F", Near(EngineeringTools.ConvertTemperature(0, "C", "F"), 32), "32");
        C("Pt100 0°C=100Ω", Near(EngineeringTools.Pt100Resistance(0), 100), EngineeringTools.Pt100Resistance(0).ToString("0.####"));
        C("Pt100 100°C≈138.5055Ω", Near(EngineeringTools.Pt100Resistance(100), 138.5055, 0.0002), EngineeringTools.Pt100Resistance(100).ToString("0.####"));
        C("Pt100 反算", Near(EngineeringTools.Pt100Temperature(138.5055), 100, 0.01), EngineeringTools.Pt100Temperature(138.5055).ToString("0.###"));

        Console.WriteLine("\n== Siemens / Modbus ==");
        C("ABCD Float32", Near(EngineeringTools.DecodeModbusFloat(0x3F80, 0, "ABCD"), 1), "1.0");
        C("BADC Float32", Near(EngineeringTools.DecodeModbusFloat(0x803F, 0, "BADC"), 1), "1.0");
        C("CDAB Float32", Near(EngineeringTools.DecodeModbusFloat(0, 0x3F80, "CDAB"), 1), "1.0");
        C("DCBA Float32", Near(EngineeringTools.DecodeModbusFloat(0, 0x803F, "DCBA"), 1), "1.0");
        C("寄存器十六进制", EngineeringTools.ParseRegister("0xFFFF") == 65535, "65535");
        var a1 = EngineeringTools.ParseS7Address("DB10.DBX2.3");
        C("解析 DBX", a1.Ok && a1.DbNumber == 10 && a1.ByteOffset == 2 && a1.BitOffset == 3 && a1.SizeBits == 1, a1.Canonical);
        var a2 = EngineeringTools.ParseS7Address("DB20.DBD12");
        C("解析 DBD", a2.Ok && a2.SizeBits == 32 && a2.ByteOffset == 12, a2.Description);
        var a3 = EngineeringTools.ParseS7Address("QW2");
        C("解析 QW", a3.Ok && a3.Area == "Q" && a3.SizeBits == 16, a3.Canonical);
        C("拒绝 DBX 位越界", !EngineeringTools.ParseS7Address("DB1.DBX0.8").Ok, "rejected");

        Console.WriteLine("\n== 制药验证模板 ==");
        var oq = ValidationTemplates.Generate("OQ 运行确认", "冻干机控制系统", "验证工程师");
        C("OQ 包含报警联锁", oq.Contains("报警") && oq.Contains("联锁") && oq.Contains("接受标准"), "len=" + oq.Length);
        var p11 = ValidationTemplates.Generate("21 CFR Part 11 检查", "SCADA", "QA");
        C("Part11 包含审计追踪与电子签名", p11.Contains("审计追踪") && p11.Contains("电子签名"), "ok");
        C("验证任务可生成", ValidationTemplates.TaskTitles("IQ 安装确认", "配液系统").Count >= 2, "count=" + ValidationTemplates.TaskTitles("IQ 安装确认", "配液系统").Count);

        Console.WriteLine("\n== 项目工作区持久化 ==");
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-eng-" + DateTime.Now.Ticks);
        var project = Path.Combine(dir, "PLC-Project"); Directory.CreateDirectory(project);
        Store.DataDir = dir; AppPaths.DataDir = dir;
        var ws = new WorkspaceStore(); var item = ws.Add("灌装线 PLC", project, "PLC");
        C("添加工作区", ws.Items.Count == 1 && ws.Items[0].Root == Path.GetFullPath(project), ws.Items[0].Name);
        ws.Add("重复", project, "DCS");
        C("工作区路径去重", ws.Items.Count == 1, "count=" + ws.Items.Count);
        ws.Touch(item.Id); C("记录最近打开", ws.Items[0].LastOpenedAt > 0, ws.Items[0].LastOpenedAt.ToString());
        ws.Update(item.Id, "灌装线控制项目", "PLC", "制药工厂 A", "灌装线", "TIA Portal V19");
        C("更新项目元数据", ws.Items[0].Name == "灌装线控制项目" && ws.Items[0].Customer == "制药工厂 A" && ws.Items[0].System == "灌装线", ws.Items[0].Customer + "/" + ws.Items[0].System);
        ws.TogglePin(item.Id); C("切换置顶", !ws.Items[0].Pinned, "pinned=" + ws.Items[0].Pinned);
        var ws2 = new WorkspaceStore(); C("工作区重载", ws2.Items.Count == 1 && ws2.Items[0].Name == "灌装线控制项目", "count=" + ws2.Items.Count);

        Console.WriteLine("\n== AI 工程工具与模式 ==");
        var tools = ToolRegistry.Instance.Tools.Select(x => x.Name).ToList();
        C("注册工程工具", tools.Contains("eng.scale_4_20") && tools.Contains("eng.s7_address") && tools.Contains("eng.modbus_float") && tools.Contains("validation.template"), string.Join(",", tools));
        var tr = ToolRegistry.Instance.Invoke("eng.scale_4_20", new Dictionary<string, string> { { "mA", "12" }, { "low", "0" }, { "high", "100" } }, new ToolContext());
        C("AI 量程工具真实计算", tr.Ok && tr.Output.Contains("50"), tr.Output);
        var ar = ToolRegistry.Instance.Invoke("eng.s7_address", new Dictionary<string, string> { { "address", "DB10.DBW4" } }, new ToolContext());
        C("AI S7 工具真实解析", ar.Ok && ar.Output.Contains("16 bit"), ar.Output);

        var store = new Store(); store.Init();
        var chat = new ChatService(store, new ProviderStore());
        chat.SetGenerationOptions("plc", 0.0, 2048);
        var build = typeof(ChatService).GetMethod("BuildContext", BindingFlags.NonPublic | BindingFlags.Instance);
        var msgs = (List<AiMessage>)build.Invoke(chat, new object[] { chat.Current });
        var sys = msgs[0].Content;
        C("PLC 模式注入领域提示", sys.Contains("Siemens PLC") && sys.Contains("S7-1200/1500") && sys.Contains("变更控制"), "len=" + sys.Length);
        C("生成参数真实保存", chat.Current.Temperature == 0 && chat.Current.MaxTokens == 2048 && chat.Current.DomainMode == "plc", chat.Current.MaxTokens.ToString());

        Console.WriteLine("\n==== 工程能力验收: PASS " + pass + " / FAIL " + fail + " ====");
        return fail == 0 ? 0 : 1;
    }
}
