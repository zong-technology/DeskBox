using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace DeskBox.Shell
{
    public class ShellOperationResult
    {
        public bool Ok;
        public string Message = "";
        public int Completed;
        public int Total;
        public List<string> Outputs = new List<string>();
    }

    internal class ShellUndoAction
    {
        public string Kind = "";
        public string Source = "";
        public string Target = "";
    }

    public class ShellFileOperationService
    {
        private readonly Stack<List<ShellUndoAction>> _undo = new Stack<List<ShellUndoAction>>();
        public event Action<int, int, string> Progress;
        public bool CanUndo { get { return _undo.Count > 0; } }

        public ShellOperationResult Copy(IEnumerable<string> sources, string targetDirectory)
        {
            return Transfer(sources, targetDirectory, false);
        }

        public ShellOperationResult Move(IEnumerable<string> sources, string targetDirectory)
        {
            return Transfer(sources, targetDirectory, true);
        }

        private ShellOperationResult Transfer(IEnumerable<string> sources, string targetDirectory, bool move)
        {
            var list = (sources ?? Enumerable.Empty<string>()).Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!Directory.Exists(targetDirectory)) return Fail("目标目录不存在：" + targetDirectory);
            var result = new ShellOperationResult { Total = list.Count };
            var undo = new List<ShellUndoAction>();
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var source = list[i];
                    var target = ResolveConflict(Path.Combine(targetDirectory, Path.GetFileName(source)));
                    Raise(i, list.Count, Path.GetFileName(source));
                    if (Directory.Exists(source))
                    {
                        if (move) Directory.Move(source, target); else CopyDirectory(source, target);
                    }
                    else
                    {
                        if (move) File.Move(source, target); else File.Copy(source, target, false);
                    }
                    undo.Add(new ShellUndoAction { Kind = move ? "move" : "copy", Source = source, Target = target });
                    result.Outputs.Add(target); result.Completed++;
                }
                if (undo.Count > 0) _undo.Push(undo);
                result.Ok = true; result.Message = (move ? "已移动 " : "已复制 ") + result.Completed + " 项";
            }
            catch (Exception ex) { result.Message = (move ? "移动" : "复制") + "失败：" + ex.Message; }
            Raise(result.Completed, result.Total, result.Message);
            return result;
        }

        public ShellOperationResult Rename(string path, string newName)
        {
            if (!Exists(path)) return Fail("对象不存在：" + path);
            if (string.IsNullOrWhiteSpace(newName)) return Fail("名称不能为空");
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return Fail("名称包含非法字符");
            var target = Path.Combine(Path.GetDirectoryName(path), newName.Trim());
            if (Exists(target)) return Fail("同名对象已存在：" + target);
            try
            {
                if (Directory.Exists(path)) Directory.Move(path, target); else File.Move(path, target);
                _undo.Push(new List<ShellUndoAction> { new ShellUndoAction { Kind = "move", Source = path, Target = target } });
                return new ShellOperationResult { Ok = true, Completed = 1, Total = 1, Message = "已重命名为 " + newName, Outputs = new List<string> { target } };
            }
            catch (Exception ex) { return Fail("重命名失败：" + ex.Message); }
        }

        public ShellOperationResult CreateFolder(string parent, string name)
        {
            if (!Directory.Exists(parent)) return Fail("目录不存在：" + parent);
            try
            {
                var target = ResolveConflict(Path.Combine(parent, string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name.Trim()));
                Directory.CreateDirectory(target);
                _undo.Push(new List<ShellUndoAction> { new ShellUndoAction { Kind = "create", Target = target } });
                return Success("已创建文件夹", target);
            }
            catch (Exception ex) { return Fail("创建文件夹失败：" + ex.Message); }
        }

        public ShellOperationResult CreateTextFile(string parent, string name)
        {
            if (!Directory.Exists(parent)) return Fail("目录不存在：" + parent);
            try
            {
                var fileName = string.IsNullOrWhiteSpace(name) ? "新建文本文档.txt" : name.Trim();
                if (Path.GetExtension(fileName).Length == 0) fileName += ".txt";
                var target = ResolveConflict(Path.Combine(parent, fileName));
                File.WriteAllText(target, "", new System.Text.UTF8Encoding(false));
                _undo.Push(new List<ShellUndoAction> { new ShellUndoAction { Kind = "create", Target = target } });
                return Success("已创建文件", target);
            }
            catch (Exception ex) { return Fail("创建文件失败：" + ex.Message); }
        }

        public ShellOperationResult Delete(IEnumerable<string> paths, bool permanent)
        {
            var list = (paths ?? Enumerable.Empty<string>()).Where(Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var result = new ShellOperationResult { Total = list.Count };
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var path = list[i]; Raise(i, list.Count, Path.GetFileName(path));
                    if (permanent)
                    {
                        if (Directory.Exists(path)) Directory.Delete(path, true); else File.Delete(path);
                    }
                    else if (Directory.Exists(path))
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    else
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    result.Completed++;
                }
                result.Ok = true; result.Message = permanent ? "已永久删除 " + result.Completed + " 项" : "已移入回收站 " + result.Completed + " 项";
            }
            catch (Exception ex) { result.Message = "删除失败：" + ex.Message; }
            Raise(result.Completed, result.Total, result.Message); return result;
        }

        public ShellOperationResult Undo()
        {
            if (_undo.Count == 0) return Fail("没有可撤销的文件操作");
            var actions = _undo.Pop();
            try
            {
                foreach (var a in actions.AsEnumerable().Reverse())
                {
                    if (a.Kind == "move" && Exists(a.Target) && !Exists(a.Source))
                    {
                        if (Directory.Exists(a.Target)) Directory.Move(a.Target, a.Source); else File.Move(a.Target, a.Source);
                    }
                    else if ((a.Kind == "copy" || a.Kind == "create") && Exists(a.Target))
                    {
                        if (Directory.Exists(a.Target)) FileSystem.DeleteDirectory(a.Target, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        else FileSystem.DeleteFile(a.Target, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }
                }
                return new ShellOperationResult { Ok = true, Completed = actions.Count, Total = actions.Count, Message = "已撤销最近文件操作" };
            }
            catch (Exception ex) { return Fail("撤销失败：" + ex.Message); }
        }

        public static void SetClipboardFiles(IEnumerable<string> paths, bool cut)
        {
            var files = new StringCollection();
            files.AddRange((paths ?? Enumerable.Empty<string>()).Where(Exists).ToArray());
            if (files.Count == 0) return;
            var data = new DataObject(); data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new System.IO.MemoryStream(new byte[] { cut ? (byte)2 : (byte)1, 0, 0, 0 }));
            Clipboard.SetDataObject(data, true);
        }

        public static List<string> GetClipboardFiles()
        {
            try { return Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static bool ClipboardIsCut()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                var stream = data == null ? null : data.GetData("Preferred DropEffect") as System.IO.MemoryStream;
                if (stream == null) return false;
                stream.Position = 0;
                return stream.ReadByte() == 2;
            }
            catch { return false; }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private static string ResolveConflict(string path)
        {
            if (!Exists(path)) return path;
            var dir = Path.GetDirectoryName(path); var ext = Directory.Exists(path) ? "" : Path.GetExtension(path); var name = Path.GetFileNameWithoutExtension(path);
            for (int i = 2; i < 10000; i++) { var p = Path.Combine(dir, name + " (" + i + ")" + ext); if (!Exists(p)) return p; }
            throw new IOException("无法生成不冲突的文件名：" + path);
        }

        private static bool Exists(string path) { return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)); }
        private static ShellOperationResult Fail(string message) { return new ShellOperationResult { Message = message }; }
        private static ShellOperationResult Success(string message, string output) { return new ShellOperationResult { Ok = true, Completed = 1, Total = 1, Message = message, Outputs = new List<string> { output } }; }
        private void Raise(int done, int total, string item) { var h = Progress; if (h != null) h(done, total, item); }
    }
}
