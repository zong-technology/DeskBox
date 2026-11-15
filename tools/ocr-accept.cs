using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using DeskBox;
using DeskBox.Ai;
using DeskBox.Core;

public class OcrAccept
{
    public static int Main()
    {
        var dir = Path.Combine(Path.GetTempPath(), "deskbox-ocr-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); Store.DataDir = dir; AppPaths.DataDir = dir;
        var path = Path.Combine(dir, "扫描证据.pdf");
        using (var process = Process.Start(new ProcessStartInfo("python.exe", "\"" + Path.Combine(Environment.CurrentDirectory, "tools", "generate-ocr-fixture.py") + "\" \"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true })) { process.WaitForExit(); if (process.ExitCode != 0) return 2; }
        var context = new ToolContext { AllowedRoots = new List<string> { dir } };
        var args = new Dictionary<string,string> { { "path", path }, { "page", "1" } };
        var plain = ToolRegistry.Instance.Invoke("doc.read", args, context);
        var ocr = ToolRegistry.Instance.Invoke("doc.ocr", args, context);
        int pass = 0, fail = 0;
        Action<string,bool> check = (name, ok) => { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); };
        check("扫描件普通读取不伪造正文", !plain.Ok && plain.Error.Contains("OCR"));
        check("Windows OCR读取真实像素文字", ocr.Ok && ocr.Output.Contains("42") && ocr.Output.Replace(" ", "").Contains("设备"));
        check("OCR返回文件及页码出处", ocr.Ok && ocr.Output.Contains("Windows OCR") && ocr.Output.Contains(path));
        if (!ocr.Ok) Console.WriteLine(ocr.Error);
        args["page"] = "2"; check("页码越界明确失败", !ToolRegistry.Instance.Invoke("doc.ocr", args, context).Ok);
        Console.WriteLine("Windows OCR: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
