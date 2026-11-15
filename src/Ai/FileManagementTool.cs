using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DeskBox.Core;

namespace DeskBox.Ai
{
    public class FileManagementTool : IAiTool
    {
        private readonly string _name;
        public FileManagementTool(string name){_name=name;}
        public string Name{get{return _name;}}
        public bool RequiresConfirm{get{return Name!="fs.list";}}
        public string Description{get{
            switch(Name){case "fs.list":return "列出授权目录的直接子目录与文件。args:{path,limit=100}";case "fs.rename":return "重命名授权文件或文件夹，不覆盖目标，可撤销，需确认。args:{path,new_name}";case "fs.trash":return "把授权文件/文件夹移到软件备份，不永久删除，可撤销，需确认。args:{path}";case "fs.undo":return "撤销指定文件操作，仍检查当前目录权限，需确认。args:{undo_id}";default:return "在授权目录间"+(Name=="fs.copy"?"复制":"移动")+"文件/文件夹，不覆盖目标，可撤销，需确认。args:{path,target_dir}";}
        }}
        private static string Get(Dictionary<string,string> args,string key){string value;return args.TryGetValue(key,out value)?value??"":"";}
        public ToolResult Invoke(ToolContext ctx,Dictionary<string,string> args)
        {
            string path=Get(args,"path"),error;
            if(Name=="fs.list")
            {
                if(!ToolRegistry.IsAllowed(ctx,path,out error))return ToolResult.Fail(error);
                if(!Directory.Exists(path))return ToolResult.Fail("目录不存在");
                int limit;if(!int.TryParse(Get(args,"limit"),out limit))limit=100;limit=Math.Max(1,Math.Min(200,limit));
                var entries=new List<object>();bool more=false;
                foreach(var item in Directory.EnumerateFileSystemEntries(path))
                {
                    ctx.Cancellation.ThrowIfCancellationRequested();
                    if(!ToolRegistry.IsAllowed(ctx,item,out error))continue;
                    if(entries.Count==limit){more=true;break;}
                    bool dir=Directory.Exists(item);entries.Add(new{Path=item,Directory=dir,Bytes=dir?0:new FileInfo(item).Length});
                }
                return ToolResult.Success(Json.ToJson(new{Entries=entries,More=more}));
            }
            if(ctx.Files==null)return ToolResult.Fail("文件操作服务不可用");
            if(ctx.Confirm==null||!ctx.Confirm("AI文件操作："+Name+"\n"+path+"\n目标："+Get(args,"target_dir")+Get(args,"new_name")))return ToolResult.Fail("文件操作未获确认");
            if(Name=="fs.undo")
            {
                var batch=ctx.Files.Batches.FirstOrDefault(b=>b.Id==Get(args,"undo_id")&&!b.Used);if(batch==null)return ToolResult.Fail("撤销记录不存在或已使用");
                foreach(var action in batch.Actions)foreach(var candidate in new[]{action.From,action.To})
                {
                    if(string.IsNullOrEmpty(candidate))continue;
                    if(!ToolRegistry.IsAllowed(ctx,candidate,out error))
                    {
                        var backup=Path.GetFullPath(Path.Combine(Store.DataDir,"backup")).TrimEnd('\\')+"\\";
                        if(!Path.GetFullPath(candidate).StartsWith(backup,StringComparison.OrdinalIgnoreCase))return ToolResult.Fail(error);
                        var part=Path.GetFullPath(candidate);while(!string.IsNullOrEmpty(part)){if((File.Exists(part)||Directory.Exists(part))&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)return ToolResult.Fail("备份路径包含链接，已拒绝撤销");part=Path.GetDirectoryName(part);}
                    }
                }
                if(ctx.Cancellation.IsCancellationRequested||(ctx.FileAccessEnabled!=null&&!ctx.FileAccessEnabled()))return ToolResult.Fail("文件访问已取消或关闭");
                string undoMessage;return ctx.Files.Undo(batch.Id,out undoMessage)?ToolResult.Success(undoMessage):ToolResult.Fail(undoMessage);
            }
            if(!ToolRegistry.IsAllowed(ctx,path,out error))return ToolResult.Fail(error);
            if(!File.Exists(path)&&!Directory.Exists(path))return ToolResult.Fail("源路径不存在");
            var stack=new Stack<string>();if(Directory.Exists(path))stack.Push(path);
            while(stack.Count>0)
            {
                ctx.Cancellation.ThrowIfCancellationRequested();
                foreach(var entry in Directory.EnumerateFileSystemEntries(stack.Pop()))
                {
                    if(!ToolRegistry.IsAllowed(ctx,entry,out error))return ToolResult.Fail(error);
                    if(Directory.Exists(entry))stack.Push(entry);
                }
            }
            string message;bool ok;
            if(Name=="fs.rename")
            {
                var name=Get(args,"new_name");if(string.IsNullOrWhiteSpace(name)||Path.GetFileName(name)!=name||name=="."||name==".."||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return ToolResult.Fail("new_name必须是单个合法文件名");
                var target=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),name);if(!ToolRegistry.IsAllowed(ctx,target,out error))return ToolResult.Fail(error);
                var plan=ctx.Files.PlanRename(new List<string>{path},new RenameRule{Find=Path.GetFileName(path),Replace=name,TrimSpace=false},false);
                ok=ctx.Files.ExecuteRename(plan,out message);
            }
            else if(Name=="fs.trash")ok=ctx.Files.DeleteToBackup(new List<string>{path},out message);
            else
            {
                var target=Get(args,"target_dir");if(!ToolRegistry.IsAllowed(ctx,target,out error))return ToolResult.Fail(error);
                if(!Directory.Exists(target))return ToolResult.Fail("目标目录不存在，请先选择已存在的授权目录");
                ok=Name=="fs.copy"?ctx.Files.CopyFiles(new List<string>{path},target,out message):ctx.Files.MoveFiles(new List<string>{path},target,out message);
            }
            return ok?ToolResult.Success(message+"\nUndoId="+(ctx.Files.LastUndo==null?"":ctx.Files.LastUndo.Id)):ToolResult.Fail(message);
        }
    }
}
