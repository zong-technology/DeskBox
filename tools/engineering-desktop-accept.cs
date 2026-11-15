using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using DeskBox;
using DeskBox.Core;
using DeskBox.UI;

public class EngineeringDesktopAccept
{
    static int pass, fail;
    static void C(string name, bool ok) { if (ok) pass++; else fail++; Console.WriteLine((ok ? "PASS " : "FAIL ") + name); }
    [STAThread] public static int Main()
    {
        var data = Path.Combine(Path.GetTempPath(), "deskbox-engineering-desktop-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        Store.DataDir = data; AppPaths.DataDir = data; var store = new Store(); store.Init();
        string[] names = { "Main.scl", "DCS-IO.xlsx", "HMI-screen.txt", "IO-List.csv", "FAT-plan.md", "OQ-protocol.md", "notes.txt" };
        var items = names.Select((name, i) => new DesktopItem { Id = "f" + i, Name = name, Path = Path.Combine(data, name), CategoryId = store.Categories.First().Id }).ToList();
        foreach (var item in items) File.WriteAllText(item.Path, "original");
        C("Siemens源代码识别", IconBoard.EngineeringGroup(items[0]) == "PLC ENGINEERING");
        C("DCS工程识别", IconBoard.EngineeringGroup(new DesktopItem { Name = "DeltaV.fhx" }).StartsWith("DCS"));
        C("FAT与验证分开", IconBoard.EngineeringGroup(items[4]) == "FAT / SAT" && IconBoard.EngineeringGroup(items[5]).StartsWith("VALIDATION"));
        C("目录识别", IconBoard.EngineeringGroup(new DesktopItem { Name = "Project", Kind = "dir" }).StartsWith("PROJECT FOLDERS"));
        using (var form = new Form { Width = 1200, Height = 800 })
        using (var board = new IconBoard(store) { Dock = DockStyle.Fill })
        {
            form.Controls.Add(board); board.SetData(items); board.SetEngineeringMode(true); form.Show(); Application.DoEvents();
            using (var bmp = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height)); }
            var field = typeof(IconBoard).GetField("_itemRects", BindingFlags.Instance | BindingFlags.NonPublic);
            var rects = (Dictionary<string, Rectangle>)field.GetValue(board);
            C("工程模式每文件仅一个绘制位置", rects.Count == items.Count);
            C("分组不更改路径分类或文件内容", items.All(x => x.CategoryId == store.Categories.First().Id && File.ReadAllText(x.Path) == "original"));
            var reload = new Store(); reload.Init(); C("工程模式可持久化", reload.Settings.EngineeringDesktop);
            board.SetEngineeringMode(false); C("可返回桌面模式", !board.EngineeringMode);
        }
        string image = Path.Combine(data, "preview.png"); using (var bitmap = new Bitmap(16, 16)) bitmap.Save(image);
        using (var preview = new PreviewPanel(store))
        {
            preview.ShowPreview(image);
            var result = (PreviewResult)typeof(PreviewPanel).GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preview);
            var oldImage = result.Image; preview.ShowPreview(image);
            bool disposed = false; try { var width = oldImage.Width; } catch (ArgumentException) { disposed = true; }
            C("切换预览释放旧图片", disposed);
            preview.ClosePreview(); C("关闭预览清理状态", !preview.IsOpen);
        }
        Console.WriteLine("Engineering desktop: PASS " + pass + " / FAIL " + fail); return fail == 0 ? 0 : 1;
    }
}
