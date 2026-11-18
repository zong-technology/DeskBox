using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DeskBox.Ai;
using DeskBox.Core;

namespace DeskBox.UI
{
    /// <summary>
    /// 授权目录管理：读写 data\trusted-roots.json。
    /// AI 工具只能在这些目录内读写；未授权时工具调用一律被拒绝（fail-closed）。
    /// </summary>
    public static class TrustedRoots
    {
        private static string Normalize(string value) { value = value.Trim(); if (value.Length == 2 && value[1] == ':') value += "\\"; var full = Path.GetFullPath(value); return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd('\\', '/') : full; }
        private static string FilePath { get { return Path.Combine(AppPaths.DataDir, "trusted-roots"+DeskBox.Ai.AiEnvironment.Suffix+".json"); } }

        public static List<string> Load()
        {
            try
            {
                var list = Json.Load<List<string>>(FilePath, null);
                if (list == null) return new List<string>();
                return list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "system", "读取授权目录失败: " + ex.Message);
                return new List<string>();
            }
        }

        public static void Save(List<string> roots)
        {
            try
            {
                var clean = (roots ?? new List<string>())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(Normalize)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, Json.ToJson(clean), new System.Text.UTF8Encoding(false));
                    if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null); else File.Move(temporary, FilePath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            catch (Exception ex) { AppLog.Write("error", "system", "保存授权目录失败: " + ex.Message); throw new IOException("授权保存失败，权限未更新：" + ex.Message, ex); }
        }

        public static void Add(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            var list = Load();
            root = Normalize(root);
            if (!list.Any(r => string.Equals(r, root, StringComparison.OrdinalIgnoreCase))) list.Add(root);
            Save(list);
        }

        public static void Remove(string root)
        {
            var list = Load();
            list.RemoveAll(r => string.Equals(r, Normalize(root), StringComparison.OrdinalIgnoreCase));
            Save(list);
        }
    }

    /// <summary>
    /// AI 工具执行确认对话框：展示工具名与参数摘要，由用户明确允许或拒绝。
    /// 该对话框在 UI 线程弹出；调用方（后台线程）阻塞等待结果。
    /// </summary>
    public static class ToolConfirm
    {
        /// <summary>线程安全的确认入口：可从任意线程调用，内部切回 UI 线程。</summary>
        public static bool Ask(Control owner, string toolName, string detail, Palette palette)
        {
            try
            {
                if (owner == null || owner.IsDisposed) return false;
                if (owner.InvokeRequired)
                {
                    bool result = false;
                    owner.Invoke(new Action(delegate { result = ShowDialog(owner, toolName, detail, palette); }));
                    return result;
                }
                return ShowDialog(owner, toolName, detail, palette);
            }
            catch (Exception ex)
            {
                AppLog.Write("warn", "ai", "工具确认对话框失败，按拒绝处理: " + ex.Message);
                return false;   // fail-closed
            }
        }

        private static bool ShowDialog(Control owner, string toolName, string detail, Palette palette)
        {
            using (var dlg = new Form())
            {
                dlg.Text = "AI&SI 请求执行操作";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.MaximizeBox = false;
                dlg.ShowInTaskbar = false;
                dlg.BackColor = palette.Bg;
                dlg.ForeColor = palette.Text;
                dlg.ClientSize = new System.Drawing.Size(560, 360);

                var title = new Label
                {
                    Text = "🤖 AI 请求执行：" + toolName,
                    Left = 18, Top = 16, Width = 520, Height = 26,
                    Font = new System.Drawing.Font("Microsoft YaHei UI", 11F, System.Drawing.FontStyle.Bold),
                    ForeColor = palette.Text, BackColor = palette.Bg
                };
                var warn = new Label
                {
                    Text = "该操作会真实修改你的文件或数据。请确认内容无误后再允许。",
                    Left = 18, Top = 44, Width = 520, Height = 22,
                    Font = new System.Drawing.Font("Microsoft YaHei UI", 8.5F),
                    ForeColor = System.Drawing.Color.FromArgb(248, 196, 113), BackColor = palette.Bg
                };
                var box = new TextBox
                {
                    Left = 18, Top = 72, Width = 524, Height = 214,
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    BackColor = palette.PanelAlt, ForeColor = palette.Text,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = new System.Drawing.Font("Consolas", 9F),
                    Text = detail ?? ""
                };
                var btnAllow = new DarkButton("允许执行") { Primary = true, Left = 352, Top = 302, Width = 100 };
                var btnDeny = new DarkButton("拒绝") { Left = 462, Top = 302, Width = 80 };
                bool result = false;
                btnAllow.Click += delegate { result = true; dlg.Close(); };
                btnDeny.Click += delegate { result = false; dlg.Close(); };

                dlg.Controls.AddRange(new Control[] { title, warn, box, btnAllow, btnDeny });
                dlg.TopMost = true;
                dlg.ShowDialog(owner.FindForm());
                return result;
            }
        }
    }
}
