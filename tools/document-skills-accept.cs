using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;

public class DocumentSkillsAccept
{
    static int pass, fail;
    static void C(string name, bool ok, string detail = "") { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name + " " + detail); }
    static ToolResult Tool(string name, ToolContext context, params string[] args) { var values = new Dictionary<string, string>(); for (int i = 0; i < args.Length; i += 2) values[args[i]] = args[i + 1]; return ToolRegistry.Instance.Invoke(name, values, context); }
    static void Entry(ZipArchive zip, string name, string text) { using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-documents-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); Store.DataDir = dir; AppPaths.DataDir = dir;
        var store = new Store(); store.Init(); var context = new ToolContext { Store = store, AllowedRoots = new List<string> { dir }, Confirm = text => true };
        C("文档技能已注册", new[] { "doc.read", "doc.search", "doc.create" }.All(x => ToolRegistry.Instance.Get(x) != null));
        var support = DocumentSkills.Run(new Dictionary<string, string> { { "op", "support" } }); C("真实解析运行时就绪", support.Ok && support.Text.Contains("true"), support.Error);
        foreach (var ext in new[] { ".docx", ".xlsx", ".pdf", ".txt", ".md" })
        {
            var path = Path.Combine(dir, "中文工程证据" + ext);
            string content = ext == ".xlsx" ? "设备\t接受标准\n反应器\t温度联锁验收阈值42" : "设备验证记录\n温度联锁验收阈值42\n药品生产设备测试";
            var created = Tool("doc.create", context, "path", path, "content", content); C("新建真实格式 " + ext, created.Ok && File.Exists(path), created.Error);
            var read = Tool("doc.read", context, "path", path); C("解析中文正文 " + ext, read.Ok && read.Output.Contains("温度联锁验收阈值42"), read.Error);
        }
        var pdf = Tool("doc.read", context, "path", Path.Combine(dir, "中文工程证据.pdf")); C("PDF返回页码出处", pdf.Output.Contains("[页 1]"));
        var excel = Tool("fs.read", context, "path", Path.Combine(dir, "中文工程证据.xlsx")); C("旧fs.read也能读取Excel实际单元格", excel.Ok && excel.Output.Contains("B2=") && excel.Output.Contains("工作表"));
        var found = Tool("doc.search", context, "root", dir, "keyword", "温度联锁验收阈值42", "limit", "30"); C("按正文而非文件名搜索并返回出处", found.Ok && found.Output.Contains("中文工程证据.pdf") && found.Output.Contains("Source"), found.Error);
        string longDoc = Path.Combine(dir, "long.docx"); Tool("doc.create", context, "path", longDoc, "content", new string('长', 2000) + "尾部证据");
        var first = Tool("doc.read", context, "path", longDoc, "maxChars", "100"); var tail = Tool("doc.read", context, "path", longDoc, "offset", "1900", "maxChars", "1000"); C("长文档明确截断且可继续读取", first.Output.Contains("NextOffset=100") && tail.Output.Contains("尾部证据"));
        string textPath = Path.Combine(dir, "中文工程证据.txt"); var denied = Tool("doc.create", context, "path", textPath, "content", "overwrite"); C("不覆盖旧文档", !denied.Ok && File.ReadAllText(textPath).Contains("温度联锁"));
        var noConfirm = new ToolContext { AllowedRoots = new List<string> { dir } }; var output = Path.Combine(dir, "must-not-exist.docx"); C("无确认不得写文档", !Tool("doc.create", noConfirm, "path", output, "content", "x").Ok && !File.Exists(output));
        var exact = new ToolContext { AllowedFiles = new List<string> { textPath } }; C("单文件授权可读取所选文件", Tool("doc.read", exact, "path", textPath).Ok); C("单文件授权不扩大到同目录其他文件", !Tool("doc.read", exact, "path", longDoc).Ok);
        var exactSearch = Tool("doc.search", exact, "root", textPath, "keyword", "温度联锁"); C("单文件授权可以只搜索该文件正文", exactSearch.Ok && exactSearch.Output.Contains("温度联锁"));
        string multiple = Path.Combine(dir, "multiple.xlsx");
        using (var zip = ZipFile.Open(multiple, ZipArchiveMode.Create))
        {
            Entry(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>");
            Entry(zip, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Entry(zip, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"主表\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"隐藏验证表\" sheetId=\"2\" state=\"hidden\" r:id=\"rId2\"/></sheets></workbook>");
            Entry(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Entry(zip, "xl/worksheets/sheet1.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>普通内容</t></is></c></row></sheetData></worksheet>");
            Entry(zip, "xl/worksheets/sheet2.xml", "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"3\"><c r=\"B3\" t=\"inlineStr\"><is><t>隐藏接受标准65</t></is></c></row></sheetData></worksheet>");
        }
        var multiRead = Tool("doc.read", context, "path", multiple); C("读取全部工作表及首列空白的单元格", multiRead.Ok && multiRead.Output.Contains("隐藏验证表") && multiRead.Output.Contains("B3=隐藏接受标准65"), multiRead.Error);
        string bad = Path.Combine(dir, "corrupt.docx"); File.WriteAllText(bad, "not a zip"); C("损坏文档不冒充读懂", !Tool("doc.read", context, "path", bad).Ok);
        string blank = Path.Combine(dir, "scanned.pdf"); Tool("doc.create", context, "path", blank, "content", ""); var empty = Tool("doc.read", context, "path", blank); C("无文字PDF明确提示OCR", !empty.Ok && empty.Error.Contains("OCR"));
        string visio = Path.Combine(dir, "diagram.vsdx"); using (var zip = ZipFile.Open(visio, ZipArchiveMode.Create)) Entry(zip, "visio/pages/page1.xml", "<PageContents xmlns=\"http://schemas.microsoft.com/office/visio/2012/main\"><Shapes><Shape ID=\"1\"><Text>纯化水系统联锁图</Text></Shape></Shapes></PageContents>");
        var diagram = Tool("doc.read", context, "path", visio); C("读取Visio形状文字与定位", diagram.Ok && diagram.Output.Contains("纯化水系统联锁图") && diagram.Output.Contains("形状 1"));
        string hostile = Path.Combine(dir, "entity.docx"); using (var zip = ZipFile.Open(hostile, ZipArchiveMode.Create)) Entry(zip, "word/document.xml", "<!DOCTYPE test [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><doc>&x;</doc>"); C("拒绝XML实体文档", !Tool("doc.read", context, "path", hostile).Ok);
        Console.WriteLine("Document skills: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
