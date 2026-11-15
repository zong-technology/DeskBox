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
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Console.WriteLine("Loading isolated native renderer");
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
                Console.WriteLine("Creating preview window");
                var core = (Control)Field(form, "_core");
                var timer = (Timer)Field(core, "_animation");
                timer.Stop(); // Deterministic captures must not pump an endless animated WM_PAINT queue.
                form.Show();
                Console.WriteLine("Window created");
                Application.DoEvents();
                Console.WriteLine("Initial paint completed");
                var coreType = core.GetType();
                Check((string)Field(core, "_scene") == "avatar", "Default scene is the reference character");
                Check(!(bool)coreType.GetProperty("ParticleStyle").GetValue(core, null), "Original hologram remains the default");
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
                    Console.WriteLine("Rendering " + size);
                    core.Dock = DockStyle.None;
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
                // Switch the actual setup selector, then use the immersive style menu to switch back.
                var selector = (ComboBox)Field(form, "_hologramStyle");
                var sessionBefore = Field(form, "_session");
                coreType.GetField("Muted").SetValue(core, true);
                coreType.GetField("OutputMuted").SetValue(core, true);
                coreType.GetField("Paused").SetValue(core, true);
                coreType.GetField("_transcript", Private).SetValue(core, "风格切换保留字幕");
                selector.SelectedIndex = 1;
                Check((bool)coreType.GetProperty("ParticleStyle").GetValue(core, null), "Setup selector enables the particle style");
                Check((bool)coreType.GetField("Muted").GetValue(core), "Style switching preserves microphone mute");
                Check((bool)coreType.GetField("OutputMuted").GetValue(core), "Style switching preserves speaker mute");
                Check((bool)coreType.GetField("Paused").GetValue(core), "Style switching preserves pause");
                Check((string)Field(core, "_transcript") == "风格切换保留字幕", "Style switching preserves captions");
                Check(Field(form, "_session") == sessionBefore && !(bool)Field(form, "_dropPlayback"), "Style switching never replaces the call or flushes playback");
                core.Size = new Size(1280, 800);
                using (var paused = new Bitmap(1280, 800)) {
                    core.DrawToBitmap(paused, new Rectangle(0, 0, 1280, 800));
                    Check(Light(paused, .5, .22, .08, .09) > 30, "Changing style while paused keeps the figure visible");
                }
                coreType.GetField("Muted").SetValue(core, false);
                coreType.GetField("OutputMuted").SetValue(core, false);
                coreType.GetField("Paused").SetValue(core, false);
                coreType.GetField("_transcript", Private).SetValue(core, "");
                coreType.GetField("_visualSeconds", Private).SetValue(core, 2.0);
                foreach (var size in new[] { new Size(1600, 900), new Size(1280, 800), new Size(1024, 768) })
                {
                    core.Size = size;
                    using (var bitmap = new Bitmap(size.Width, size.Height)) {
                        core.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
                        bitmap.Save(Path.Combine(output, "native-photon-" + size.Width + ".png"), ImageFormat.Png);
                        Check(Light(bitmap, .5, .22, .08, .09) > 30, "Particle face remains visible at " + size);
                        Check(Light(bitmap, .5, .62, .18, .10) > 100, "Particle gown remains visible at " + size);
                    }
                    var actions = (Dictionary<string, Rectangle>)Field(core, "_actions");
                    Check(actions.ContainsKey("style"), "Immersive view has a style entry");
                    Check(new Rectangle(Point.Empty, size).Contains(actions["style"]), "Style button fits at " + size);
                }
                int count = (int)assembly.GetType("DeskBox.UI.PhotonProjection").GetProperty("PointCount").GetValue(null, null);
                Check(count > 5000, "Particle style uses actual sampled light points");
                Console.WriteLine("Particle samples: " + count);
                core.Size = new Size(1280, 800);
                using (var a = new Bitmap(1280, 800)) using (var b = new Bitmap(1280, 800)) {
                    core.DrawToBitmap(a, new Rectangle(0, 0, 1280, 800));
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (int frame = 0; frame < 6; frame++) core.DrawToBitmap(a, new Rectangle(0, 0, 1280, 800));
                    Console.WriteLine("Particle render mean: " + (clock.Elapsed.TotalMilliseconds / 6).ToString("0.0") + " ms (offscreen CI host)");
                    using (var original = new Bitmap(Path.Combine(output, "native-preview-1280.png")))
                        Check(Differences(a, original) > 2000, "Particle projection is visually distinct from the original");
                    coreType.GetField("_reduced", Private).SetValue(core, true);
                    core.DrawToBitmap(a, new Rectangle(0, 0, 1280, 800));
                    coreType.GetField("_visualSeconds", Private).SetValue(core, 4.0);
                    core.DrawToBitmap(b, new Rectangle(0, 0, 1280, 800));
                    Check(Differences(a, b) == 0, "Reduced motion freezes particle movement");
                    a.Save(Path.Combine(output, "native-photon-reduced.png"), ImageFormat.Png);
                    coreType.GetField("_reduced", Private).SetValue(core, false);
                    coreType.GetMethod("UpdateSignal").Invoke(core, new object[] { .2, true });
                    core.DrawToBitmap(b, new Rectangle(0, 0, 1280, 800));
                    Check(Differences(a, b) > 300, "Actual voice signal changes the particle projection");
                }
                var styleMenu = (ContextMenuStrip)Field(core, "_styleMenu");
                ((ToolStripMenuItem)styleMenu.Items[0]).PerformClick();
                Check(selector.SelectedIndex == 0, "Immersive menu synchronizes the setup selector");
                Check(!(bool)coreType.GetProperty("ParticleStyle").GetValue(core, null), "Original style can be restored");
                coreType.GetMethod("UpdateSignal").Invoke(core, new object[] { 0.0, false });
                coreType.GetField("_visualSeconds", Private).SetValue(core, 0.0);
                using (var restored = new Bitmap(1280, 800)) using (var original = new Bitmap(Path.Combine(output, "native-preview-1280.png"))) {
                    core.DrawToBitmap(restored, new Rectangle(0, 0, 1280, 800));
                    Check(Differences(restored, original) == 0, "Restoring the original style preserves its appearance");
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
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    for (int frame = 0; frame < 6; frame++) core.DrawToBitmap(bitmap, new Rectangle(0, 0, 1280, 800));
                    Console.WriteLine("Cached native render mean: " + (clock.Elapsed.TotalMilliseconds / 6).ToString("0.0") + " ms (offscreen CI host)");
                }
                coreType.GetField("_reduced", Private).SetValue(core, true);
                coreType.GetField("Muted").SetValue(core, true);
                using (var bitmap = new Bitmap(1280, 800)) {
                    core.DrawToBitmap(bitmap, new Rectangle(0, 0, 1280, 800));
                    bitmap.Save(Path.Combine(output, "native-reduced-muted.png"), ImageFormat.Png);
                }
                timer.Start();
                Console.WriteLine("Disposing preview window");
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

    private static int Light(Bitmap image, double x, double y, double width, double height)
    {
        int count = 0;
        for (int py = (int)((y - height / 2) * image.Height); py < (y + height / 2) * image.Height; py += 2)
            for (int px = (int)((x - width / 2) * image.Width); px < (x + width / 2) * image.Width; px += 2) {
                var color = image.GetPixel(px, py);
                if (color.B > 65 && color.G > 45) count++;
            }
        return count;
    }

    private static int Differences(Bitmap a, Bitmap b)
    {
        int count = 0;
        for (int y = 0; y < a.Height; y += 3) for (int x = 0; x < a.Width; x += 3)
            if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++;
        return count;
    }
}
