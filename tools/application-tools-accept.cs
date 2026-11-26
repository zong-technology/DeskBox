using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using DeskBox;
using DeskBox.Core;
using DeskBox.Ai;
using DeskBox.Native;
using DeskBox.Sync;

public class ApplicationToolsAccept
{
    static int pass,fail;
    static void Check(string label,bool ok){if(ok)pass++;else fail++;Console.WriteLine((ok?"PASS ":"FAIL ")+label);}
    static ToolResult Tool(ToolContext ctx,string name,params string[] pairs){var args=new Dictionary<string,string>();for(int i=0;i<pairs.Length;i+=2)args[pairs[i]]=pairs[i+1];return ToolRegistry.Instance.Invoke(name,args,ctx);}
    [STAThread] public static int Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"deskbox-app-tools-"+Guid.NewGuid().ToString("N"));var allowed=Path.Combine(root,"allowed");var outside=Path.Combine(root,"outside");Directory.CreateDirectory(allowed);Directory.CreateDirectory(outside);Store.DataDir=Path.Combine(root,"data");AppPaths.DataDir=Store.DataDir;
        var store=new Store();store.Init();var projects=new DeskBox.Engineering.WorkspaceStore();projects.Add("任务联动项目",allowed,"PLC");
        using(var sync=new LocalSyncCoordinator(store,projects))
        {
            bool fileEnabled=false,confirm=true;int ui=0;string navigated="";
            var ctx=new ToolContext{Store=store,Projects=projects,Sync=sync,Files=new DeskBox.Fs.FileOps(store),AllowedRoots=new List<string>{allowed},FileAccessEnabled=()=>fileEnabled,Confirm=message=>confirm,UiInvoke=action=>{ui++;return action();},Navigate=view=>navigated=view};
            store.Tasks.Add(new TaskItem{Id="T1",Title="核查联锁",Priority="high",Notes="检查药品设备"});
            var listed=Tool(ctx,"task.list");Check("AI读取真实任务无需文件开关",listed.Ok&&listed.Output.Contains("核查联锁")&&listed.Output.Contains("Expected"));
            Check("AI应用状态不包含密钥",Tool(ctx,"app.context").Ok&&!Tool(ctx,"app.context").Output.Contains("ApiKey"));
            var chat=new ChatService(store,new ProviderStore());chat.UiInvoke=ctx.UiInvoke;
            typeof(ChatService).GetMethod("ExecuteToolCalls",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(chat,new object[]{chat.Current,ToolCallParser.Parse("```tool\n{\"name\":\"task.list\",\"args\":{}}\n```"),CancellationToken.None,chat.Project,new Action<string>(_=>{})});
            Check("聊天工具闭环返回真实任务",chat.Current.Messages.Any(m=>m.Role=="tool"&&m.ToolName=="task.list"&&m.Content.Contains("核查联锁")));
            Check("项目工具接入真实仓库",Tool(ctx,"project.list").Output.Contains("任务联动项目"));
            var stamp=ApplicationTool.TaskStamp(store.Tasks[0]);confirm=false;
            Check("任务修改无确认拒绝",!Tool(ctx,"task.update","id","T1","expected",stamp,"done","true").Ok&&!store.Tasks[0].Done);
            confirm=true;Check("任务完成回写真实列表",Tool(ctx,"task.update","id","T1","expected",stamp,"done","true").Ok&&store.Tasks[0].Done&&store.Tasks[0].DoneAt>0);
            Check("旧任务比较值不能覆盖新内容",!Tool(ctx,"task.update","id","T1","expected",stamp,"title","旧内容").Ok&&store.Tasks[0].Title=="核查联锁");
            Check("应用工具通过UI调度",ui>=5);
            Check("可打开应用页面",Tool(ctx,"app.navigate","view","tasks").Ok&&navigated=="tasks");
            Check("不能把导航当命令执行",!Tool(ctx,"app.navigate","view","cmd.exe").Ok);
            sync.SaveIncident(new IncidentDto{Id="I1",Description="现场压力异常",Equipment="PT-101"},0);
            Check("AI可读现场事件",Tool(ctx,"incident.list").Output.Contains("PT-101"));
            Check("事件处理工具回写并检查版本",Tool(ctx,"incident.update","id","I1","expected_version","1","status","resolved","solution","复测压力联锁").Ok&&sync.Incidents[0].Status=="resolved");
            var file=Path.Combine(allowed,"a.txt");File.WriteAllText(file,"旧内容");
            Check("关闭文件权限仍拒绝文件读取",!Tool(ctx,"fs.read","path",file).Ok);fileEnabled=true;
            Check("文件夹浏览工具真实列出",Tool(ctx,"fs.list","path",allowed).Output.Contains("a.txt"));
            Check("可改写文本并保存原件备份",Tool(ctx,"fs.write","path",file,"content","新内容").Ok&&File.ReadAllText(file)=="新内容"&&Directory.GetFiles(Path.Combine(AppPaths.BackupDir,"ai-edits"),"a.txt",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)=="旧内容"));
            Check("文件重命名",Tool(ctx,"fs.rename","path",file,"new_name","renamed.txt").Ok&&File.Exists(Path.Combine(allowed,"renamed.txt")));
            Check("文件重命名可撤销",Tool(ctx,"fs.undo","undo_id",ctx.Files.LastUndo.Id).Ok&&File.Exists(file));
            Check("拒绝移动到未授权目录",!Tool(ctx,"fs.move","path",file,"target_dir",outside).Ok&&File.Exists(file));
            var target=Path.Combine(allowed,"target");Directory.CreateDirectory(target);
            Check("可复制授权文件",Tool(ctx,"fs.copy","path",file,"target_dir",target).Ok&&File.Exists(Path.Combine(target,"a.txt")));
            Check("复制不覆盖已有文件",!Tool(ctx,"fs.copy","path",file,"target_dir",target).Ok);
            Check("删除移到备份而非永久删除",Tool(ctx,"fs.trash","path",file).Ok&&!File.Exists(file));
            Check("备份删除可以撤销",Tool(ctx,"fs.undo","undo_id",ctx.Files.LastUndo.Id).Ok&&File.Exists(file));
            confirm=false;Check("缺少确认不改写文件",!Tool(ctx,"fs.write","path",file,"content","拒绝内容").Ok&&File.ReadAllText(file)=="新内容");confirm=true;
            foreach(var ext in new[]{".docx",".xlsx"})
            {
                var document=Path.Combine(allowed,"联锁证据"+ext);var content=ext==".xlsx"?"设备\t要求\nPT-101\t联锁A":"验收联锁A";
                var created=Tool(ctx,"doc.create","path",document,"content",content);Check("创建文档样本"+ext,created.Ok);
                using(var archive=ZipFile.Open(document,ZipArchiveMode.Update))
                {
                    var marker=archive.CreateEntry("test-preserved.bin");using(var stream=marker.Open())stream.Write(new byte[]{1,7,9},0,3);
                    var name=ext==".docx"?"word/document.xml":"xl/worksheets/sheet1.xml";var entry=archive.GetEntry(name);string xml;using(var reader=new StreamReader(entry.Open()))xml=reader.ReadToEnd();entry.Delete();
                    if(ext==".docx")xml=xml.Replace("<w:document ","<w:document xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" mc:Ignorable=\"w14\" ");
                    else{var position=xml.IndexOf("</row>",xml.IndexOf("<row r=\"2\""));xml=xml.Insert(position,"<c r=\"C2\"><f>1+1</f><v>2</v></c>").Replace("A1:B2","A1:C2");}
                    using(var writer=new StreamWriter(archive.CreateEntry(name).Open()))writer.Write(xml);
                }
                var read=Tool(ctx,"doc.read","path",document);string hash=read.Output.Split('\n')[0].Replace("SHA256=","").Trim();Check("文档读取返回版本校验"+ext,read.Ok&&hash.Length==64);
                var edited=Tool(ctx,"doc.edit","path",document,"find","联锁A","replacement","联锁B","expected_sha256",hash);
                Check("Office文本定点改写并可读"+ext,edited.Ok&&Tool(ctx,"doc.read","path",document).Output.Contains("联锁B"));
                using(var archive=ZipFile.OpenRead(document))
                {
                    using(var stream=archive.GetEntry("test-preserved.bin").Open()){var bytes=new byte[3];stream.Read(bytes,0,3);Check("Office其他压缩部件保留"+ext,bytes.SequenceEqual(new byte[]{1,7,9}));}
                    string xml;using(var reader=new StreamReader(archive.GetEntry(ext==".docx"?"word/document.xml":"xl/worksheets/sheet1.xml").Open()))xml=reader.ReadToEnd();
                    Check("Word兼容命名空间或Excel公式保留"+ext,ext==".docx"?xml.Contains("mc:Ignorable=\"w14\"")&&xml.Contains("xmlns:w14="):xml.Contains("<f>1+1</f>"));
                }
                Check("旧文档校验值拒绝覆盖"+ext,!Tool(ctx,"doc.edit","path",document,"find","联锁B","replacement","联锁C","expected_sha256",hash).Ok);
                if(ext==".docx"){var currentHash=Tool(ctx,"doc.read","path",document).Output.Split('\n')[0].Replace("SHA256=","").Trim();Check("拒绝会破坏Office XML的控制字符",!Tool(ctx,"doc.edit","path",document,"find","联锁B","replacement","\u0001","expected_sha256",currentHash).Ok&&Tool(ctx,"doc.read","path",document).Output.Contains("联锁B"));}
            }
            using(var shortcut=new WinFShortcut(()=>{}))
            {
                Check("普通F不会拦截",shortcut.ProcessGesture(70,true,false,false)==0);
                Check("Win加F只触发一次",shortcut.ProcessGesture(70,true,true,false)==2&&shortcut.ProcessGesture(70,true,true,false)==1);
                Check("释放F结束本次捕获",shortcut.ProcessGesture(70,false,false,false)==1&&shortcut.ProcessGesture(70,true,false,false)==0);
                Check("额外修饰键不误触",shortcut.ProcessGesture(70,true,true,true)==0);
                Check("Windows组合键处理可注册",shortcut.Start());
            }
        }
        Console.WriteLine("Application tools: PASS "+pass+" / FAIL "+fail);return fail==0?0:1;
    }
}
