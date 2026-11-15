using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

public static class VisualCompare
{
    public static int Main(string[] args)
    {
        int tolerance;
        double limit;
        if (args.Length != 5 || !int.TryParse(args[3], out tolerance) || tolerance < 0 || tolerance > 255 ||
            !double.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out limit) ||
            double.IsNaN(limit) || limit < 0 || limit > 100)
        {
            Console.Error.WriteLine("Usage: visual-compare BASELINE ACTUAL OUTPUT CHANNEL_TOLERANCE MAX_CHANGED_PERCENT");
            return 2;
        }
        var baseline = Path.GetFullPath(args[0]);
        var actual = Path.GetFullPath(args[1]);
        var output = Path.GetFullPath(args[2]);
        if (string.Equals(output, baseline, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(output, actual, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a separate output directory.");
        Directory.CreateDirectory(output);
        var report = new StringBuilder("# Visual comparison\n\n");
        report.AppendLine("Baseline: " + baseline).AppendLine("Actual: " + actual)
            .AppendLine("Channel tolerance: " + tolerance + "; maximum changed %: " + limit.ToString(CultureInfo.InvariantCulture))
            .AppendLine().AppendLine("| Image | Baseline | Actual | Changed pixels | Changed % | Bounds (x,y,w,h) | Status |")
            .AppendLine("|---|---|---|---:|---:|---|---|");
        var names = Directory.GetFiles(baseline, "*.png").Concat(Directory.GetFiles(actual, "*.png"))
            .Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        int failures = names.Length == 0 ? 1 : 0;
        foreach (var name in names)
        {
            string before = Path.Combine(baseline, name), after = Path.Combine(actual, name);
            if (!File.Exists(before) || !File.Exists(after))
            {
                report.AppendFormat("| {0} | {1} | {2} | — | — | — | MISSING |\n", name,
                    File.Exists(before) ? "Present" : "Missing", File.Exists(after) ? "Present" : "Missing");
                failures++;
                continue;
            }
            using (var a = new Bitmap(before))
            using (var b = new Bitmap(after))
            using (var diff = new Bitmap(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height)))
            {
                int changed = 0, left = diff.Width, top = diff.Height, right = -1, bottom = -1;
                for (int y = 0; y < diff.Height; y++)
                    for (int x = 0; x < diff.Width; x++)
                    {
                        bool hasA = x < a.Width && y < a.Height, hasB = x < b.Width && y < b.Height;
                        var original = hasA ? a.GetPixel(x, y) : Color.Transparent;
                        var candidate = hasB ? b.GetPixel(x, y) : Color.Transparent;
                        if (hasA != hasB || Delta(original, candidate) > tolerance)
                        {
                            changed++;
                            left = Math.Min(left, x); top = Math.Min(top, y);
                            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                            diff.SetPixel(x, y, Color.Magenta);
                        }
                        else diff.SetPixel(x, y, Color.FromArgb(candidate.R / 4, candidate.G / 4, candidate.B / 4));
                    }
                diff.Save(Path.Combine(output, name), ImageFormat.Png);
                double percent = 100.0 * changed / ((long)diff.Width * diff.Height);
                bool sizeChanged = a.Size != b.Size;
                bool failed = sizeChanged || percent > limit;
                if (failed) failures++;
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "| [{0}]({0}) | {1}x{2} | {3}x{4} | {5} | {6:F3} | {7} | {8} |\n",
                    name, a.Width, a.Height, b.Width, b.Height, changed, percent,
                    changed == 0 ? "—" : string.Format("{0},{1},{2},{3}", left, top, right - left + 1, bottom - top + 1),
                    sizeChanged ? "SIZE MISMATCH" : failed ? "PIXEL CHANGE" : "WITHIN LIMIT");
            }
        }
        report.AppendLine().AppendLine(names.Length == 0 ? "No PNG inputs." : "Failed comparisons: " + failures + " / " + names.Length);
        File.WriteAllText(Path.Combine(output, "report.md"), report.ToString());
        Console.WriteLine("Images: " + names.Length + "; failed: " + failures);
        return failures == 0 ? 0 : 1;
    }

    private static int Delta(Color a, Color b)
    {
        return Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)),
                        Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
    }
}
