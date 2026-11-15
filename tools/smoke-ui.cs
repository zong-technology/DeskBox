// Compile with render-v4-ui.cs, /main:Smoke, and a reference to DeskBox.exe.
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DeskBox.UI;

public static class Smoke
{
    private static readonly string[] Routes = {
        "home", "desktop", "projects", "engineering", "ai", "search",
        "tasks", "history", "automation", "incidents", "weather", "mobile"
    };

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr key, IntPtr data);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: smoke-ui.exe OUTPUT WIDTH HEIGHT");
            return 2;
        }
        try
        {
            return RenderV4Ui.Run(args.Concat(new[] { "--reference" }).ToArray(), CheckNavigation);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error.Message);
            return 1;
        }
    }

    private static void CheckNavigation(MainForm form, Action<string> capture)
    {
        var workspace = form.Controls.OfType<ReferenceWorkspace>().Single();
        if (!ReferenceWorkspace.ReferencePages.SequenceEqual(Routes))
            throw new InvalidOperationException("The 12-page route contract changed.");

        // Start away from home so its click must switch pages as well.
        foreach (var route in Routes.Skip(1).Concat(Routes.Take(1)))
        {
            var nav = workspace.Controls.Find("nav-" + route, true).Single();
            if (!nav.Visible || !nav.Enabled || workspace.CurrentPage == route)
                throw new InvalidOperationException("Navigation is not testable: " + route);
            SendMessage(nav.Handle, 0x0100, (IntPtr)Keys.Enter, IntPtr.Zero);
            SendMessage(nav.Handle, 0x0101, (IntPtr)Keys.Enter, IntPtr.Zero);
            Application.DoEvents();
            var page = workspace.Controls.Find("page-" + route, true).SingleOrDefault();
            if (workspace.CurrentPage != route || page == null || !page.Visible ||
                page.Width <= 0 || page.Height <= 0 ||
                page.Parent.Controls.Cast<Control>().Count(control => control.Visible) != 1)
                throw new InvalidOperationException("Expected active page: " + route);
            capture(route);
            Console.WriteLine("PASS " + route + " " + page.ClientSize);
        }
        Console.WriteLine("NAVIGATION PASS 12/12");
    }
}
