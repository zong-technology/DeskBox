// 真实取消验收：配置可达但很慢的 mock 服务商，AI 节点执行中取消
using System; using System.Collections.Generic; using System.IO; using System.Threading;
using DeskBox.Ai; using DeskBox.Automation; using DeskBox.Core;
public class WfCancel {
  static int pass=0, fail=0;
  static void C(string n, bool ok, string d){ if(ok){pass++;Console.WriteLine("PASS  "+n+"  ["+d+"]");} else {fail++;Console.WriteLine("FAIL  "+n+"  ["+d+"]");} }
  public static int Main(string[] a) {
    var dir = Path.Combine(Path.GetTempPath(), "wfcancel-" + DateTime.Now.Ticks);
    Directory.CreateDirectory(dir);
    DeskBox.Core.AppPaths.DataDir = dir; DeskBox.Store.DataDir = dir;
    var store = new DeskBox.Store(); store.Init();

    // 写入一个慢速服务商（mock 端点不存在也会快速失败；这里用超时端点模拟慢响应）
    var ps = new ProviderStore();
    var p = new AiProvider { Name="慢速", Protocol="openai", BaseUrl="http://127.0.0.1:18080", Model="err-timeout", ApiKey="sk-x", TimeoutMs=30000, MaxRetries=0, Enabled=true };
    ps.Add(p); ps.Save();

    var engine = new WorkflowEngine(store);
    C("执行器就绪", engine.Runner != null, "ok");

    var wf = engine.Add("取消测试", "manual", 0);
    wf.UseNodes = true;
    wf.Nodes = new List<WorkflowNode> {
      new WorkflowNode { Id="n1", Type="ai", Kind="generate", Params = new Dictionary<string,string>{{"instruction","测试"},{"input","内容"}}, TimeoutMs=60000 }
    };
    engine.Save();

    string runId = "r_cancel_real";
    var t = new Thread(delegate() { Thread.Sleep(800); engine.Cancel(runId); });
    t.IsBackground = true; t.Start();

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var run = engine.Runner.Run(wf, wf.Nodes, false, runId);
    sw.Stop();
    Console.WriteLine("  耗时 " + sw.ElapsedMilliseconds + "ms, ok=" + run.Ok + ", msg=" + run.Message);
    C("取消后未报告成功", !run.Ok, run.Message);
    C("取消在 10 秒内生效（未等到 30 秒超时）", sw.ElapsedMilliseconds < 10000, sw.ElapsedMilliseconds + "ms");
    C("记录中被标记为取消或有失败原因", run.Cancelled || run.NodeRecords.Exists(r => r.Error.Length > 0), "cancelled=" + run.Cancelled);
    Console.WriteLine();
    Console.WriteLine("==== 取消验收: PASS " + pass + " / FAIL " + fail + " ====");
    return fail==0?0:1;
  }
}
