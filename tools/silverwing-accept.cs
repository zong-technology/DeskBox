using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Isolated native renderer and preview checks. Never starts Program/MainForm or opens audio devices.
internal static class SilverwingAccept
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    private static void Check(bool result, string message) { if (!result) throw new Exception(message); checks++; }
    private static object Field(object owner, string name) { return owner.GetType().GetField(name, Private).GetValue(owner); }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            string output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            using (var stream = assembly.GetManifestResourceStream("DeskBox.Silverwing"))
            {
                Check(stream != null, "Character must be embedded in the native EXE");
                using (var image = new Bitmap(stream)) {
                    Check(image.Width == 1024 && image.Height == 1536, "Full-resolution asset");
                    Check(image.GetPixel(0, 0).A == 0, "Transparent background");
                }
            }
            var type = assembly.GetType("DeskBox.UI.LiveVoiceForm", true);
            using (var form = (Form)Activator.CreateInstance(type, true))
            {
                form.Show();
                Application.DoEvents();
                var core = (Control)Field(form, "_core");
                var coreType = core.GetType();
                Check((string)Field(core, "_scene") == "avatar", "Default scene is the reference character");
                Button preview = null;
                foreach (Control control in ((Control)Field(form, "_setup")).Controls)
                    if (control is Button && control.Text.StartsWith("预览全息")) preview = (Button)control;
                Check(preview != null, "Preview entry exists");
                preview.PerformClick();
                Application.DoEvents();
                Check((bool)coreType.GetProperty("Preview").GetValue(core, null), "Preview is explicitly labelled");
                Check(Field(form, "_session") == null, "Preview never creates a voice session");
                Check(!((Button)Field(form, "_mute")).Enabled, "Preview cannot open the microphone");

                foreach (var size in new[] { new Size(1600, 900), new Size(1280, 800), new Size(1024, 768) })
                {
                    core.Size = size;
                    using (var bitmap = new Bitmap(size.Width, size.Height))
                    {
                        core.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
                        bitmap.Save(Path.Combine(output, "native-preview-" + size.Width + ".png"), ImageFormat.Png);
                        // The central head, lower gown and feet must all remain inside the rendered viewport.
                        Check(Bright(bitmap, .50, .22, .08, .09) > 40, "Visible face at " + size);
                        Check(Bright(bitmap, .50, .60, .18, .10) > 100, "Visible full gown at " + size);
                        Check(Bright(bitmap, .50, .69, .08, .09) > 30, "Visible lower body at " + size);
                    }
                    var actions = (Dictionary<string, Rectangle>)Field(core, "_actions");
                    foreach (var item in actions) Check(new Rectangle(Point.Empty, size).Contains(item.Value), "Control fits: " + item.Key);
                    var hitboxes = new List<Rectangle>(actions.Values);
                    for (int i = 0; i < hitboxes.Count; i++) for (int j = i + 1; j < hitboxes.Count; j++)
                        Check(!hitboxes[i].IntersectsWith(hitboxes[j]), "Controls never overlap");
                }
                // Return from preview using the same action as the native end button.
                var returnAction = (Action)coreType.GetField("ReturnRequested").GetValue(core);
                returnAction();
                Check(!(bool)coreType.GetProperty("Preview").GetValue(core, null), "Preview ends cleanly");
                Check(((Control)Field(form, "_setup")).Visible, "Voice setup is restored");
                Check(Field(form, "_session") == null, "Returning keeps audio unopened");
                coreType.GetMethod("SetWorkspace").Invoke(core, new object[] { true, false });
                coreType.GetField("_visualSeconds", Private).SetValue(core, 3.0);
                core.Size = new Size(1280, 800);
                coreType.GetMethod("UpdateSignal").Invoke(core, new object[] { .17, true });
                using (var bitmap = new Bitmap(1280, 800)) {
                    core.DrawToBitmap(bitmap, new Rectangle(0, 0, 1280, 800));
                    bitmap.Save(Path.Combine(output, "native-speaking.png"), ImageFormat.Png);
                }
                coreType.GetField("_reduced", Private).SetValue(core, true);
                coreType.GetField("Muted").SetValue(core, true);
                using (var bitmap = new Bitmap(1280, 800)) {
                    core.DrawToBitmap(bitmap, new Rectangle(0, 0, 1280, 800));
                    bitmap.Save(Path.Combine(output, "native-reduced-muted.png"), ImageFormat.Png);
                }
                var timer = (Timer)Field(core, "_animation");
                form.Close();
                Check(!timer.Enabled, "Closing disposes the animation timer");
            }
            Console.WriteLine("PASS " + checks + " native checks (no live service or audio used)");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static int Bright(Bitmap image, double x, double y, double width, double height)
    {
        int count = 0;
        for (int py = (int)((y - height / 2) * image.Height); py < (y + height / 2) * image.Height; py += 2)
            for (int px = (int)((x - width / 2) * image.Width); px < (x + width / 2) * image.Width; px += 2) {
                var color = image.GetPixel(px, py);
                if (color.R > 110 && color.G > 100 && color.B > 130) count++;
            }
        return count;
    }
}
