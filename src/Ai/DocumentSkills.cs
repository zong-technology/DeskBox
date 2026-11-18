using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DeskBox.Core;

namespace DeskBox.Ai
{
    public class DocumentResponse
    {
        public bool Ok;
        public string Text = "", Error = "";
        public bool Truncated;
        public int NextOffset = -1;
    }

    public static class DocumentSkills
    {
        private static readonly object Gate = new object();
        private static string Worker;
        public static string Instructions
        {
            get { using (var stream = typeof(DocumentSkills).Assembly.GetManifestResourceStream("DeskBox.DocumentSkill")) using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd(); }
        }
        public static DocumentResponse Run(Dictionary<string, string> request, CancellationToken cancellation = default(CancellationToken))
        {
            if (cancellation.IsCancellationRequested) return new DocumentResponse { Error = "文档操作已取消" };
            try
            {
                foreach(var key in new[]{"path","root","backup_dir"}) {string value;if(request.TryGetValue(key,out value))AiEnvironment.RequireLocalPath(value);}
                string worker;
                lock (Gate)
                {
                    if(Worker!=null)AiEnvironment.RequireLocalPath(Worker);
                    if (Worker == null || !File.Exists(Worker))
                    {
                        var directory = Path.Combine(Path.GetTempPath(), "DeskBoxDocument-" + Guid.NewGuid().ToString("N"));AiEnvironment.RequireLocalPath(directory);Directory.CreateDirectory(directory);
                        Worker = Path.Combine(directory, "worker.py");
                        using (var resource = typeof(DocumentSkills).Assembly.GetManifestResourceStream("DeskBox.DocumentWorker"))
                        using (var file = new FileStream(Worker, FileMode.CreateNew, FileAccess.Write, FileShare.None)) resource.CopyTo(file);
                        using (var resource = typeof(DocumentSkills).Assembly.GetManifestResourceStream("DeskBox.DocumentOcr"))
                        using (var file = new FileStream(Path.Combine(directory, "windows-ocr.ps1"), FileMode.CreateNew, FileAccess.Write, FileShare.None)) resource.CopyTo(file);
                    }
                    worker = Worker;
                }
                string python = Environment.GetEnvironmentVariable("DESKBOX_DOCUMENT_PYTHON");
                if (string.IsNullOrWhiteSpace(python))
                {
                    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Python");
                    AiEnvironment.RequireLocalPath(root);
                    python = Directory.Exists(root) ? Directory.GetDirectories(root).Select(x => Path.Combine(x, "python.exe")).FirstOrDefault(File.Exists) : null;
                    if (python == null) python = "python.exe";
                }
                if(AiEnvironment.Local && !Path.IsPathRooted(python))throw new IOException("本地文档解析请配置本机 Python 绝对路径（DESKBOX_DOCUMENT_PYTHON），隔离模式不搜索外部 PATH。");
                AiEnvironment.RequireLocalPath(python);
                var start = new ProcessStartInfo(python, "-I -u \"" + worker + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                using (var process = Process.Start(start))
                {
                    var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync();
                    using (var input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) input.Write(Json.ToJson(request));
                    var clock = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        if (cancellation.IsCancellationRequested || clock.ElapsedMilliseconds > 30000)
                        {
                            try { process.Kill(); process.WaitForExit(2000); } catch { }
                            return new DocumentResponse { Error = cancellation.IsCancellationRequested ? "文档操作已取消" : "文档解析超过 30 秒，已停止；请缩小文档或搜索范围" };
                        }
                    }
                    if (!output.Wait(2000)) return new DocumentResponse { Error = "读取文档工具输出超时" };
                    var result = Json.FromJson<DocumentResponse>(output.Result);
                    return result ?? new DocumentResponse { Error = "文档运行时未返回有效 JSON；请检查 Python 3.12+、pypdf/openpyxl/PyMuPDF 是否安装" };
                }
            }
            catch (Exception ex) { return new DocumentResponse { Error = "文档技能不可用：" + ex.Message + "。请配置 Python 3.12+ 与文档解析依赖。" }; }
        }

        public static ToolResult Execute(string operation, ToolContext context, Dictionary<string, string> args)
        {
            string path; args.TryGetValue(operation == "search" ? "root" : "path", out path);
            if (string.IsNullOrWhiteSpace(path)) return ToolResult.Fail("缺少文件 path 或搜索 root");
            string error; if (!ToolRegistry.IsAllowed(context, path, out error)) return ToolResult.Fail(error);
            if (operation == "create")
            {
                if (File.Exists(path)) return ToolResult.Fail("目标文件已存在，请使用新文件名；不会覆盖原文件");
                if (context.Confirm == null || !context.Confirm("AI 将新建文档：\r\n" + path + "\r\n不会覆盖现有文件。")) return ToolResult.Fail("文档创建未获确认");
            }
            if(operation=="edit"&&(context.Confirm==null||!context.Confirm("AI将定点改写并备份原文件：\r\n"+path)))return ToolResult.Fail("文档改写未获确认");
            if(!ToolRegistry.IsAllowed(context,path,out error))return ToolResult.Fail(error);
            var request = new Dictionary<string, string>(args); request["op"] = operation;
            if(operation=="edit")request["backup_dir"]=Path.Combine(AppPaths.BackupDir,"ai-edits");
            var result = Run(request, context.Cancellation);
            if (!result.Ok) return ToolResult.Fail(result.Error);
            return ToolResult.Success(result.Text + (result.Truncated ? "\r\n[内容未读完；NextOffset=" + result.NextOffset + "，使用 doc.read 继续读取]" : ""));
        }
    }

    public class DocumentTool : IAiTool
    {
        private readonly string _operation;
        public DocumentTool(string operation) { _operation = operation; }
        public string Name { get { return "doc." + _operation; } }
        public bool RequiresConfirm { get { return _operation == "create"||_operation=="edit"; } }
        public string Description
        {
            get
            {
                if (_operation == "read") return "读取 PDF/DOCX/XLSX/XLSM/VSDX/TXT/Markdown/CSV 真实正文，返回页码/段落/工作表行；args:{path,offset=0,maxChars=16000}，截断后继续翻页读取。";
                if (_operation == "ocr") return "扫描PDF按页本地OCR，返回页码与识别文本；args:{path,page=1}，需要Windows OCR语言包。OCR可能误识别，关键数值须核对原文。";
                if (_operation == "search") return "搜索授权文件或目录中文档正文，返回路径与出处；root可以是所选单个文件；目录最多100个支持文档；args:{root,keyword,limit=10}。";
                if (_operation == "edit") return "经确认定点替换DOCX/XLSX字符串或UTF-8文本，备份原件并检查读取版本。args:{path,find,replacement,expected_sha256}；先doc.read获取SHA256。保留Office其他压缩部件，不跨格式片段，不改公式/数值，不支持PDF原位排版编辑。";
                return "经用户确认新建 DOCX/XLSX/PDF/TXT/Markdown/CSV，不覆盖旧文件；args:{path,content}，Excel内容用TSV。";
            }
        }
        public ToolResult Invoke(ToolContext ctx, Dictionary<string, string> args) { return DocumentSkills.Execute(_operation, ctx, args); }
    }
}
