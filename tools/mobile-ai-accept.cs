using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DeskBox;
using DeskBox.Core;
using DeskBox.Ai;
using DeskBox.Engineering;
using DeskBox.UI.Views;

public class MobileAiAccept
{
    static int passed,failed;
    static void Check(string name,bool value){if(value)passed++;else failed++;Console.WriteLine((value?"PASS ":"FAIL ")+name);}
    public static int Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"deskbox-mobile-ai-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Store.DataDir=root;AppPaths.DataDir=root;var store=new Store();store.Init();store.Tasks.Add(new TaskItem{Id="T1",Title="核对药品设备联锁"});
        var projects=new WorkspaceStore();projects.Add("超滤项目",root,"PLC");
        var providers=new ProviderStore();var provider=providers.Add(new AiProvider{Name="手机测试",Model="mock-mobile-tools",BaseUrl="http://127.0.0.1:18080",ApiKey="test",Enabled=true,TimeoutMs=60000,MaxRetries=0});
        var runner=new MobileAiRunner(providers,()=>new ToolContext{Store=store,Projects=projects,UiInvoke=action=>action(),AllowedRoots=new List<string>(),FileAccessEnabled=()=>false});var messages=new List<AiMessage>{new AiMessage{Role="user",Content="查询任务"}};var stages=new List<string>();
        var result=runner.Run("J1",messages,CancellationToken.None,p=>stages.Add(p.Stage)).GetAwaiter().GetResult();
        Console.WriteLine("Fixture J1: "+result.Status+" / "+result.Stage+" / "+result.Error+" / "+result.Text);
        Check("手机AI查询电脑真实任务",result.Status=="completed"&&result.Text.Contains("核对药品设备联锁"));Check("发布真实工具执行阶段",stages.Any(s=>s.Contains("task.list")));Check("不返回服务商密钥",!result.Text.Contains("Bearer test")&&!result.Text.Contains("ApiKey"));
        provider.Model="mock-mobile-write";providers.Update(provider);result=runner.Run("J2",messages,CancellationToken.None,p=>{}).GetAwaiter().GetResult();Console.WriteLine("Fixture J2: "+result.Status+" / "+result.Error+" / "+result.Text);Check("手机写入保留原有确认",result.Text.Contains("未获确认")&&store.Tasks.Count==1&&store.Tasks[0].Title=="核对药品设备联锁");
        provider.Model="mock-mobile-create";providers.Update(provider);result=runner.Run("J-create",new List<AiMessage>{new AiMessage{Role="user",Content="帮我在超滤项目记录SIP的FS需要更新步骤"}},CancellationToken.None,p=>{}).GetAwaiter().GetResult();
        Console.WriteLine("Fixture create: "+result.Status+" / "+result.Stage+" / "+result.Error+" / "+result.Text);
        Check("文字AI完成项目查询到真实创建的多轮闭环",result.Status=="completed"&&store.Tasks.Count==2&&store.Tasks.Last().Project==projects.Items.Last().Id&&store.Tasks.Last().Notes.Contains("供应商"));
        Check("文字AI结果携带可折叠操作证据",result.Text.StartsWith("```deskbox-results")&&result.Text.Contains("task.create")&&result.Text.Contains("已保存超滤"));
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();result=runner.Run("J3",messages,cancelled.Token,p=>{}).GetAwaiter().GetResult();Check("取消请求不继续调用模型",result.Status=="cancelled");}
        provider.Enabled=false;providers.Update(provider);result=runner.Run("J4",messages,CancellationToken.None,p=>{}).GetAwaiter().GetResult();Check("已禁用服务商不会被手机调用",result.Status=="error"&&result.Error.Contains("已启用"));
        var project=new EngineerWorkspace{ValidationSteps=ValidationProgress.Create()};foreach(var step in project.ValidationSteps)step.Percent=null;project.ValidationSteps[0].Percent=100;project.ValidationSteps[1].Percent=50;project.ValidationSteps[1].Weight=3;Check("验证进度按自定义权重计算",ValidationProgress.Overall(project)==62.5);project.ValidationSteps[0].Weight=0;Check("零权重不计入进度",ValidationProgress.Overall(project)==50);
        var json=Json.FromJson<ValidationStep>("{\"Percent\":50}");Check("旧项目缺少权重保持1",json.Weight==1);
        var csv=RecordsView.ExportCsv(new[]{new LogEntry{Ts=1,Action="task",Level="info",Message="=SUM(1,2)\r\n\"quote\""}});Check("记录导出保留换行且防公式执行",csv.Contains("\"'=SUM(1,2)")&&csv.Contains("\"\"quote\"\""));
        Console.WriteLine("Mobile AI: PASS "+passed+" / FAIL "+failed);return failed==0?0:1;
    }
}
